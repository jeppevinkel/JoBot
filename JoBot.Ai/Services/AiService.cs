using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using JoBot.Ai.Configuration;
using JoBot.Ai.History;
using JoBot.Ai.Models;
using JoBot.Ai.Tools;
using JoBot.Core.Actions;
using JoBot.Core.Interfaces;
using JoBot.Core.Interfaces.Repositories;
using JoBot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JoBot.Ai.Services;

public class AiService : IAiService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string NewMessagesNote =
        "[NEW MESSAGES] While you were working on this, the following arrived in the same channel. Handle them " +
        "along with what you were already doing. Your replies from here on are read by everyone in the channel, " +
        "so make clear who you're answering where it isn't obvious.";
    private readonly ILogger<AiService> _logger;
    private readonly AnthropicClient _anthropicClient;
    private readonly AiOptions _options;

    private readonly ConcurrentDictionary<ulong, GuildConversationHistory> _guildData = new();
    private readonly ToolFactory _toolFactory;

    private readonly IConversationRepository _conversationRepository;
    private readonly IGuildSettingsService _settingsService;

    public AiService(
        ILogger<AiService> logger,
        AnthropicClient anthropicClient,
        IEnumerable<IToolProvider> toolProviders,
        IConversationRepository conversationRepository,
        IGuildSettingsService settingsService,
        IOptions<AiOptions> options)
    {
        _logger = logger;
        _anthropicClient = anthropicClient;
        _options = options.Value;
        _conversationRepository = conversationRepository;
        _settingsService = settingsService;

        _toolFactory = new ToolFactory(toolProviders);
    }

    public async IAsyncEnumerable<AiAction> ProcessAsync(
        ulong guildId,
        MessagePayload payload,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received payload:\n{Payload}", JsonSerializer.Serialize(payload, JsonOptions));

        GuildConversationHistory history = GetGuildHistory(guildId);
        var channelKey = GetMergeableChannelKey(payload);

        // Someone is already being answered in this channel: join that answer instead of waiting for it
        if (channelKey is not null &&
            history.ActiveTurn is { } activeTurn &&
            activeTurn.ChannelKey == channelKey &&
            activeTurn.TryPost(JsonSerializer.Serialize(payload, JsonOptions)) is { } posted)
        {
            if (await posted.WaitAsync(cancellationToken))
            {
                _logger.LogInformation("Added message {MessageId} to the reply already in progress in guild {GuildId}",
                    payload.Message.Id, guildId);

                // Keep the caller around until the reply is out, e.g. so the bot doesn't leave voice before speaking it
                await activeTurn.Completed.WaitAsync(cancellationToken);
                yield return new MergedAction();
                yield break;
            }

            // The turn finished before reaching its next tool call; answer this on its own
        }

        await history.Lock.WaitAsync(cancellationToken);
        TurnInbox? inbox = channelKey is null ? null : new TurnInbox(channelKey);
        try
        {
            await history.EnsureInitializedAsync();

            if (history.IsFirstAfterReboot)
                payload = payload with { IsFirstAfterReboot = true };

            await history.AddAsync(new Message(RoleType.User,
                JsonSerializer.Serialize(payload, JsonOptions)));

            history.ActiveTurn = inbox;

            await foreach (AiAction action in RunTurnAsync(
                               guildId, () => history.Messages, history.AddAsync, inbox, cancellationToken))
                yield return action;
        }
        finally
        {
            history.ActiveTurn = null;
            inbox?.Close();
            history.Lock.Release();
        }
    }

    public async IAsyncEnumerable<AiAction> ProcessEventAsync(
        ulong guildId,
        Func<CancellationToken, Task<AutomatedEventPayload?>> buildPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        GuildConversationHistory history = GetGuildHistory(guildId);
        AutomatedEventPayload? payload;
        List<Message> messages;

        // Wait for anyone being answered, so the event sees what they asked for and what was done about it.
        // The event itself runs on a copy, without holding up messages that arrive meanwhile.
        await history.Lock.WaitAsync(cancellationToken);
        try
        {
            payload = await buildPayload(cancellationToken);
            if (payload is null)
                yield break;

            await history.EnsureInitializedAsync();
            messages = history.Messages.ToList();
        }
        finally
        {
            history.Lock.Release();
        }

        _logger.LogInformation("Received automated event:\n{Payload}", JsonSerializer.Serialize(payload, JsonOptions));

        messages.Add(new Message(RoleType.User, JsonSerializer.Serialize(payload, JsonOptions)));

        await foreach (AiAction action in RunTurnAsync(
                           guildId,
                           () => messages,
                           message =>
                           {
                               messages.Add(message);
                               return Task.CompletedTask;
                           },
                           inbox: null,
                           cancellationToken))
            yield return action;
    }

    /// <summary>
    /// Messages that can be added to a reply already in progress in the same channel, keyed by that channel;
    /// null for those that can't.
    /// </summary>
    /// <remarks>
    /// Replies go back the way the message came, so only messages from the same channel and medium can share one.
    /// Spoken follow-ups that didn't name the bot may not be meant for it, which a shared reply can't express.
    /// </remarks>
    private static string? GetMergeableChannelKey(MessagePayload payload) =>
        payload.SpokenInput is { AddressedByName: false }
            ? null
            : $"{payload.Source.Type}:{payload.Source.Channel.Id}";

    /// <summary>Gets Claude's answer to the last message, running tools until it's done.</summary>
    /// <param name="getMessages">The conversation so far, ending with the message to answer.</param>
    /// <param name="addMessage">Appends Claude's responses and tool results to the conversation.</param>
    /// <param name="inbox">Messages to add to the turn as they arrive, if it takes any.</param>
    private async IAsyncEnumerable<AiAction> RunTurnAsync(
        ulong guildId,
        Func<IReadOnlyList<Message>> getMessages,
        Func<Message, Task> addMessage,
        TurnInbox? inbox,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        GuildSettings settings = await _settingsService.GetSettingsAsync(guildId);

        var parameters = new MessageParameters
        {
            Messages = getMessages().ToList(),
            MaxTokens = _options.MaxTokens,
            Temperature = settings.AiTemperature,
            Model = _options.Model,
            Tools = _toolFactory.Tools.ToList(),
            Stream = false,
            System = [new SystemMessage(settings.SystemPrompt)]
        };

        ApiCallResult result = await GetClaudeResponseAsync(parameters, guildId, cancellationToken);
        MessageResponse response;
        if (result is not ApiCallSuccess { Response: var claudeResponse })
        {
            // Compiler knows this must be ApiCallError
            yield return ((ApiCallError)result).Error;
            yield break;
        }
        response = claudeResponse;

        if (response.Message is null)
        {
            yield return new IgnoreAction();
            yield break;
        }

        await addMessage(response.Message);

        var toolIterations = 0;
        while (response.ToolCalls?.Count > 0 && toolIterations < _options.MaxToolIterations)
        {
            toolIterations++;

            // Yield any text Claude produced alongside the tool calls
            var textContent = response.Message?.Content
                .OfType<TextContent>()
                .FirstOrDefault()?.Text;

            if (!string.IsNullOrWhiteSpace(textContent))
                yield return new RespondAction { Content = textContent };

            // All results go in one message, followed by anything people said in the meantime
            var results = new List<ContentBase>();
            foreach (Function? toolCall in response.ToolCalls)
            {
                try
                {
                    var toolResult = await _toolFactory.InvokeAsync(
                        toolCall.Name,
                        toolCall.Arguments);
                    _logger.LogInformation("ToolCall: {ToolName}, result: {Result}", toolCall.Name, toolResult);
                    results.AddRange(new Message(toolCall, toolResult).Content);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error invoking tool call {ToolName}", toolCall.Name);
                    results.AddRange(new Message(toolCall, ex.ToString()).Content);
                }
            }

            var newMessages = inbox?.Drain() ?? [];
            if (newMessages.Count > 0)
            {
                results.Add(new TextContent { Text = NewMessagesNote });
                results.AddRange(newMessages.Select(message => new TextContent { Text = message }));
            }

            await addMessage(new Message { Role = RoleType.User, Content = results });

            parameters.Messages = getMessages().ToList();
            result = await GetClaudeResponseAsync(parameters, guildId, cancellationToken);
            if (result is not ApiCallSuccess { Response: var secondClaudeResponse })
            {
                // Compiler knows this must be ApiCallError
                yield return ((ApiCallError)result).Error;
                yield break;
            }

            response = secondClaudeResponse;

            if (response.Message is not null)
                await addMessage(response.Message);
        }

        if (toolIterations >= _options.MaxToolIterations)
            _logger.LogWarning("Max tool iterations ({Max}) reached for guild {GuildId}",
                _options.MaxToolIterations, guildId);

        if (response.Message is not null)
        {
            yield return new RespondAction { Content = response.Message };
        }
        else
        {
            yield return new IgnoreAction();
        }
    }

    private GuildConversationHistory GetGuildHistory(ulong guildId) =>
        _guildData.GetOrAdd(
            guildId,
            static (id, args) => new GuildConversationHistory(
                id, args.Repository, args.Settings),
            (Repository: _conversationRepository, Settings: _settingsService));

    private async Task<ApiCallResult> GetClaudeResponseAsync(
        MessageParameters parameters,
        ulong guildId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            MessageResponse response = await _anthropicClient.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
            return new ApiCallSuccess(response);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("content filtering"))
        {
            _logger.LogWarning("Response blocked by content filtering for guild {GuildId}", guildId);
            return new ApiCallError(new ReplyAction { Content = "I'm not able to respond to that request." });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "API request failed for guild {GuildId}", guildId);
            return new ApiCallError(new ReplyAction { Content = "Something went wrong communicating with the AI." });
        }
    }
}