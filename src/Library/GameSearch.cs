using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameShelf
{
    internal static class GameSearch
    {
        private static readonly Regex Words = new Regex(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled);

        internal static string[] Tokens(string text)
        {
            return Words.Matches(text ?? "").Cast<Match>().Select(match => match.Value.ToUpperInvariant()).ToArray();
        }

        internal static int Score(string[] terms, string[] words)
        {
            int score = 0;
            foreach (string term in terms)
            {
                int best = 0;
                foreach (string word in words)
                {
                    if (word == term) { best = 5; break; }
                    if (word.StartsWith(term, StringComparison.Ordinal)) best = Math.Max(best, 4);
                    else if (word.Contains(term)) best = Math.Max(best, 3);
                    else if (best == 0 && term.Length >= 4 && term.Length <= 32 && term.Any(char.IsLetter) &&
                        Near(term, word, term.Length >= 7 ? 2 : 1)) best = 1;
                }
                if (best == 0) return -1;
                score += best;
            }
            return score;
        }

        private static bool Near(string term, string word, int limit)
        {
            if (Math.Abs(term.Length - word.Length) > limit) return false;
            var distance = new int[term.Length + 1, word.Length + 1];
            for (int i = 0; i <= term.Length; i++) distance[i, 0] = i;
            for (int j = 0; j <= word.Length; j++) distance[0, j] = j;
            for (int i = 1; i <= term.Length; i++)
            for (int j = 1; j <= word.Length; j++)
            {
                int change = term[i - 1] == word[j - 1] ? 0 : 1;
                int edits = Math.Min(distance[i - 1, j] + 1,
                    Math.Min(distance[i, j - 1] + 1, distance[i - 1, j - 1] + change));
                if (i > 1 && j > 1 && term[i - 1] == word[j - 2] && term[i - 2] == word[j - 1])
                    edits = Math.Min(edits, distance[i - 2, j - 2] + 1);
                distance[i, j] = edits;
            }
            return distance[term.Length, word.Length] <= limit;
        }
    }
}
