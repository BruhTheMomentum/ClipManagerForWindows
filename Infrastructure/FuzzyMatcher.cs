using System;
using System.Linq;

namespace ClipManagerForWindows.Infrastructure;

public static class FuzzyMatcher
{
    private static readonly char[] WordSeparators = [' ', '\t', '\n', '\r', '.', ',', ';', ':', '/', '\\', '-', '_', '(', ')', '[', ']', '{', '}'];

    public static bool FuzzyMatch(string searchText, string entryText)
    {
        // Fast path: exact substring match
        if (entryText.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            return true;

        // Token-based fuzzy match
        var searchTokens = searchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (searchTokens.Length == 0)
            return true;

        var entryWords = entryText.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (entryWords.Length == 0)
            return false;

        foreach (var token in searchTokens)
        {
            int tolerance = token.Length <= 3 ? 0 : token.Length <= 6 ? 1 : 2;
            bool matched = entryWords.Any(word => WordMatchesToken(word, token, tolerance));
            if (!matched)
                return false;
        }

        return true;
    }

    private static bool WordMatchesToken(string word, string token, int tolerance)
    {
        if (word.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            return true;

        if (tolerance == 0)
            return false;

        // Compare token against word truncated to token length
        var compareTo = word.Length > token.Length ? word[..token.Length] : word;
        return LevenshteinDistance(token, compareTo) <= tolerance;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        int n = a.Length, m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        // Single-row DP
        var row = new int[m + 1];
        for (int j = 0; j <= m; j++)
            row[j] = j;

        for (int i = 1; i <= n; i++)
        {
            int prev = row[0];
            row[0] = i;
            char ca = char.ToLowerInvariant(a[i - 1]);

            for (int j = 1; j <= m; j++)
            {
                int temp = row[j];
                int cost = ca == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), prev + cost);
                prev = temp;
            }
        }

        return row[m];
    }
}
