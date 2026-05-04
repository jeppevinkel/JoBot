namespace JoBot.Discord.Helpers;

public static class MessageChunker
{
    private const int MaxLength = 2000;

    public static IEnumerable<string> Split(string content)
    {
        if (content.Length <= MaxLength)
        {
            yield return content;
            yield break;
        }

        var start = 0;

        while (start < content.Length)
        {
            if (content.Length - start <= MaxLength)
            {
                yield return content[start..].Trim();
                break;
            }

            var end = start + MaxLength;

            // Search backwards from end for a clean split point
            var splitAt = content.LastIndexOf('\n', end - 1, MaxLength);

            if (splitAt <= start)
                splitAt = content.LastIndexOf(' ', end - 1, MaxLength);

            if (splitAt <= start)
                splitAt = end; // Hard cut if no good split point found

            yield return content[start..splitAt].Trim();

            // Advance past any leading whitespace for next chunk
            start = splitAt;
            while (start < content.Length && char.IsWhiteSpace(content[start]))
                start++;
        }
    }
}