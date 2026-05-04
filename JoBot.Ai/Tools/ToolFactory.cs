using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Anthropic.SDK.Common;
using JoBot.Core.Interfaces;
using JoBot.Core.Attributes;

namespace JoBot.Ai.Tools;

public class ToolFactory
{
    private readonly Dictionary<string, Func<JsonNode?, Task<string>>> _handlers = new();
    private readonly List<Tool> _tools = [];
    private static readonly JsonSerializerOptions ParameterDeserializeOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public IReadOnlyList<Tool> Tools => _tools;

    public ToolFactory(IEnumerable<IToolProvider> providers)
    {
        foreach (var provider in providers)
            RegisterProvider(provider);
    }

    private void RegisterProvider(IToolProvider provider)
    {
        var methods = provider.GetType()
            .GetMethods()
            .Where(m => m.GetCustomAttribute<AiToolAttribute>() is not null);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<AiToolAttribute>()!;
            var toolName = ToSnakeCase(method.Name);

            _tools.Add(BuildTool(toolName, attr.Description, method));
            _handlers[toolName] = args => InvokeMethod(provider, method, args);
        }
    }

    public async Task<string> InvokeAsync(string toolName, JsonNode? arguments)
    {
        if (!_handlers.TryGetValue(toolName, out var handler))
            throw new InvalidOperationException($"No handler registered for tool '{toolName}'");

        return await handler(arguments);
    }

    private static Tool BuildTool(string name, string description, MethodInfo method)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in method.GetParameters())
        {
            var paramAttr = param.GetCustomAttribute<AiParameterAttribute>();
            if (paramAttr is null) continue;

            properties[param.Name!] = new
            {
                type = GetJsonType(param.ParameterType),
                description = paramAttr.Description
            };

            if (paramAttr.Required)
                required.Add(param.Name!);
        }

        var schema = JsonSerializer.SerializeToNode(new
        {
            type = "object",
            properties,
            required
        });

        return new Tool(new Function(name, description, schema));
    }

    private static async Task<string> InvokeMethod(
        IToolProvider provider,
        MethodInfo method,
        JsonNode? arguments)
    {
        var args = arguments?.AsObject()
                       .ToDictionary(
                           kvp => kvp.Key,
                           kvp => kvp.Value)
                   ?? [];

        var parameters = method.GetParameters()
            .Select(p => DeserializeParameter(p, args))
            .ToArray();

        var result = method.Invoke(provider, parameters);

        return result switch
        {
            Task<string> task => await task,
            Task task => await task.ContinueWith(_ => "Done"),
            string str => str,
            _ => result?.ToString() ?? "Done"
        };
    }

    private static object? DeserializeParameter(
        ParameterInfo param,
        Dictionary<string, JsonNode?> args)
    {
        if (!args.TryGetValue(param.Name!, out JsonNode? node))
            return param.HasDefaultValue ? param.DefaultValue : null;

        return node.Deserialize(param.ParameterType, ParameterDeserializeOptions);
    }

    private static string GetJsonType(Type type) => type switch
    {
        _ when type == typeof(string) => "string",
        _ when type == typeof(int) || type == typeof(long) => "integer",
        _ when type == typeof(float) || type == typeof(double) || type == typeof(decimal) => "number",
        _ when type == typeof(bool) => "boolean",
        _ when type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) => "array",
        _ => "string"
    };

    private static string ToSnakeCase(string name) =>
        string.Concat(name.Select((c, i) =>
                i > 0 && char.IsUpper(c) ? $"_{c}" : $"{c}"))
            .ToLower()
            .Replace("_async", "");
}