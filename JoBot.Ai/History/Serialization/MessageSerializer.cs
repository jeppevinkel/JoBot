using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Anthropic.SDK.Messaging;

namespace JoBot.Ai.History.Serialization;

public static class MessageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(Message message)
    {
        var stored = new StoredMessage
        {
            Role = message.Role == RoleType.User ? "user" : "assistant",
            Content = message.Content.Select(ToStoredBlock).ToList()
        };

        return JsonSerializer.Serialize(stored, Options);
    }

    public static Message Deserialize(string json)
    {
        StoredMessage stored = JsonSerializer.Deserialize<StoredMessage>(json, Options)
                               ?? throw new JsonException("Failed to deserialize message.");

        return new Message
        {
            Role = stored.Role == "user" ? RoleType.User : RoleType.Assistant,
            Content = stored.Content.Select(ToContentBase).ToList()
        };
    }

    private static StoredContentBlock ToStoredBlock(ContentBase content) => content switch
    {
        TextContent text => new StoredContentBlock
        {
            Type = "text",
            Text = text.Text
        },
        ToolUseContent toolUse => new StoredContentBlock
        {
            Type = "tool_use",
            Id = toolUse.Id,
            Name = toolUse.Name,
            InputJson = toolUse.Input?.ToJsonString()
        },
        ToolResultContent toolResult => new StoredContentBlock
        {
            Type = "tool_result",
            ToolUseId = toolResult.ToolUseId,
            Content = toolResult.Content?
                .OfType<TextContent>()
                .FirstOrDefault()?.Text,
            IsError = toolResult.IsError
        },
        _ => throw new NotSupportedException(
            $"Unsupported content type: {content.GetType().Name}")
    };

    private static ContentBase ToContentBase(StoredContentBlock block) => block.Type switch
    {
        "text" => new TextContent
        {
            Text = block.Text ?? string.Empty
        },
        "tool_use" => new ToolUseContent
        {
            Id = block.Id ?? string.Empty,
            Name = block.Name ?? string.Empty,
            Input = block.InputJson is not null
                ? JsonNode.Parse(block.InputJson)?.AsObject()
                : null
        },
        "tool_result" => new ToolResultContent
        {
            ToolUseId = block.ToolUseId ?? string.Empty,
            Content = block.Content is not null
                ? [new TextContent { Text = block.Content }]
                : null,
            IsError = block.IsError ?? false
        },
        _ => throw new NotSupportedException(
            $"Unsupported content type: '{block.Type}'")
    };
}