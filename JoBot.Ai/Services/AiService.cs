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

        await history.Lock.WaitAsync(cancellationToken);
        try
        {
            await history.EnsureInitializedAsync();

            GuildSettings settings = await _settingsService.GetSettingsAsync(guildId);

            if (history.IsFirstAfterReboot)
                payload = payload with { IsFirstAfterReboot = true };

            await history.AddAsync(new Message(RoleType.User,
                JsonSerializer.Serialize(payload, JsonOptions)));

            var parameters = new MessageParameters
            {
                Messages = history.Messages.ToList(),
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

            await history.AddAsync(response.Message);

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

                foreach (Function? toolCall in response.ToolCalls)
                {
                    try
                    {
                        var toolResult = await _toolFactory.InvokeAsync(
                            toolCall.Name,
                            toolCall.Arguments);
                        _logger.LogInformation("ToolCall: {ToolName}, result: {Result}", toolCall.Name, toolResult);
                        await history.AddAsync(new Message(toolCall, toolResult));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error invoking tool call {ToolName}", toolCall.Name);
                        await history.AddAsync(new Message(toolCall, ex.ToString()));
                    }
                }

                parameters.Messages = history.Messages.ToList();
                result = await GetClaudeResponseAsync(parameters, guildId, cancellationToken);
                if (result is not ApiCallSuccess { Response: var secondClaudeResponse })
                {
                    // Compiler knows this must be ApiCallError
                    yield return ((ApiCallError)result).Error;
                    yield break;
                }

                response = secondClaudeResponse;

                if (response.Message is not null)
                    await history.AddAsync(response.Message);
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
        finally
        {
            history.Lock.Release();
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