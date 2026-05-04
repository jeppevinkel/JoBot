using Anthropic.SDK.Messaging;
using JoBot.Core.Actions;

namespace JoBot.Ai.Models;

public abstract record ApiCallResult;
public sealed record ApiCallSuccess(MessageResponse Response) : ApiCallResult;
public sealed record ApiCallError(AiAction Error) : ApiCallResult;