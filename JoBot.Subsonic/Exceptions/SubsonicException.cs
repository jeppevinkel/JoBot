namespace JoBot.Subsonic.Exceptions;

public class SubsonicException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}