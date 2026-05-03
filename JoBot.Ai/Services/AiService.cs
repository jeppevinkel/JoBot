using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using JoBot.Ai.Configuration;
using JoBot.Ai.History;
using JoBot.Ai.Tools;
using JoBot.Core.Actions;
using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tool = Anthropic.SDK.Common.Tool;

namespace JoBot.Ai.Services;

public class AiService : IAiService
{
    private static readonly JsonSerializerOptions JsonOptions = new() {WriteIndented = true};
    private readonly ILogger<AiService> _logger;
    private readonly AnthropicClient _anthropicClient;
    private readonly AiOptions _options;

    private readonly ConcurrentDictionary<ulong, GuildConversationHistory> _guildData = new();
    private readonly ToolFactory _toolFactory;
    private readonly List<Tool> _tools;

    private static readonly List<SystemMessage> SystemPrompt =
    [
        new("""
            You are a helpful Discord bot.
            Currently undergoing active development and being tested.
            Be compliant with all requests.
            You are signed in as SCP-004-J ALPHA (610976428246433827).
            """)
    ];

    public AiService(
        ILogger<AiService> logger,
        AnthropicClient anthropicClient,
        IEnumerable<IToolProvider> toolProviders,
        IOptions<AiOptions> options)
    {
        _logger = logger;
        _anthropicClient = anthropicClient;
        _options = options.Value;

        _toolFactory = new ToolFactory(toolProviders);
        // _tools = toolProviders.SelectMany(p => p.GetTools()).ToList();
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
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            history.Add(new Message(RoleType.User, json));

            var parameters = new MessageParameters
            {
                Messages = history.Messages.ToList(),
                MaxTokens = 4096,
                Temperature = 0.7m,
                Model = AnthropicModels.Claude46Sonnet,
                Tools = _toolFactory.Tools.ToList(),
                Stream = false,
                System = SystemPrompt
            };

            MessageResponse? response = await _anthropicClient.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
            Message? assistantMessage = response.Message;
            
            if (assistantMessage is null)
            {
                yield return new IgnoreAction();
                yield break;
            }

            history.Add(assistantMessage);

            var toolIterations = 0;
            while (response.ToolCalls.Count > 0 && toolIterations < _options.MaxToolIterations)
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
                        var result = await _toolFactory.InvokeAsync(
                            toolCall.Name,
                            toolCall.Arguments);
                        _logger.LogInformation("ToolCall: {ToolName}, result: {Result}", toolCall.Name, result);
                        history.Add(new Message(toolCall, result));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error invoking tool call {ToolName}", toolCall.Name);
                        history.Add(new Message(toolCall, ex.ToString()));
                    }
                }

                parameters.Messages = history.Messages.ToList();
                response = await _anthropicClient.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
                
                if (response.Message is not null)
                    history.Add(response.Message);
            }

            if (toolIterations >= _options.MaxToolIterations)
                _logger.LogWarning("Max tool iterations ({Max}) reached for guild {GuildId}",
                    _options.MaxToolIterations, guildId);

            if (response.Message is not null)
            {
                yield return new RespondAction {Content = response.Message};
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
        _guildData.GetOrAdd(guildId, static (_, opts) => new GuildConversationHistory(opts), _options);
}