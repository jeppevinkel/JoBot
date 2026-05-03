using System.Text.Json.Serialization;

namespace JoBot.Core.Actions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(ReplyAction), "reply")]
[JsonDerivedType(typeof(RespondAction), "respond")]
[JsonDerivedType(typeof(IgnoreAction), "ignore")]
public abstract record AiAction;