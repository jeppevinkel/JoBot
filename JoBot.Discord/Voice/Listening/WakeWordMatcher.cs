using System.Globalization;
using System.Text;

namespace JoBot.Discord.Voice.Listening;

/// <summary>Finds a wake word (the bot's name) in a transcript, tolerating how speech-to-text tends to mangle names.</summary>
public static class WakeWordMatcher
{
    /// <summary>
    /// Returns true if any of <paramref name="wakeWords"/> occurs in <paramref name="transcript"/>.
    /// </summary>
    /// <remarks>
    /// Case, accents and punctuation are ignored, and a name may be split or joined differently
    /// ("Jo Bot" matches "jobot" and vice versa). Long names also allow a small number of misspelled letters;
    /// short ones must match exactly, since otherwise they'd match ordinary words ("jobot" vs "robot").
    /// </remarks>
    public static bool ContainsWakeWord(string transcript, IEnumerable<string> wakeWords)
    {
        var tokens = Tokenize(transcript);
        if (tokens.Count == 0)
            return false;

        foreach (var wakeWord in wakeWords)
        {
            var wakeTokens = Tokenize(wakeWord);
            if (wakeTokens.Count == 0)
                continue;

            var target = string.Concat(wakeTokens);
            var maxEdits = MaxEdits(target.Length);

            // Try the same number of words, plus one more or less, to catch names split or joined differently
            for (var windowSize = Math.Max(1, wakeTokens.Count - 1); windowSize <= wakeTokens.Count + 1; windowSize++)
            {
                for (var start = 0; start + windowSize <= tokens.Count; start++)
                {
                    var candidate = string.Concat(tokens.Skip(start).Take(windowSize));
                    if (Math.Abs(candidate.Length - target.Length) <= maxEdits &&
                        LevenshteinDistance(candidate, target) <= maxEdits)
                        return true;
                }
            }
        }

        return false;
    }

    private static int MaxEdits(int length) => length switch
    {
        < 6 => 0,
        < 10 => 1,
        _ => 2
    };

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var c in RemoveDiacritics(text.ToLowerInvariant()))
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else if (c is '\'' or '’')
            {
                // Keep "jobot's" as one word
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private static string RemoveDiacritics(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
