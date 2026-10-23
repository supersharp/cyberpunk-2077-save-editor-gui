using System;
using System.Collections.Generic;
using System.Linq;

namespace CP2077SaveEditor.Utils
{
    /// <summary>
    /// High-performance fuzzy search and matching utility for filtering lists, catalog items,
    /// quest facts, save slots, and records with support for:
    /// - Fast exact substring matching
    /// - Multi-term space-separated queries (all terms must match)
    /// - Acronym / word-initial matching (e.g. "mb" -> "Mantis Blades")
    /// - Subsequence matching (e.g. "ovrt" -> "Overture")
    /// - Damerau-Levenshtein typo tolerance (transpositions, insertions, deletions)
    /// - Relevance scoring for ranking suggestions
    /// </summary>
    public static class FuzzySearch
    {
        private static readonly char[] WordSeparators = { ' ', '_', '.', '-', '/', '\\', ':', ';', ',', '(', ')', '[', ']' };

        /// <summary>
        /// Checks whether the query matches the target string using fuzzy criteria.
        /// </summary>
        public static bool Matches(string? text, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var trimmedQuery = query.Trim();

            // 1. Instant fast-path: case-insensitive substring
            if (text.IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // 2. Multi-term query: every term must match
            if (trimmedQuery.IndexOf(' ') >= 0)
            {
                var terms = trimmedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    if (!MatchesSingleTerm(text, term))
                    {
                        return false;
                    }
                }
                return true;
            }

            return MatchesSingleTerm(text, trimmedQuery);
        }

        /// <summary>
        /// Checks whether all terms in the query match at least one of the provided target fields.
        /// </summary>
        public static bool MatchesAny(string? query, params string?[] fields)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }
            if (fields == null || fields.Length == 0)
            {
                return false;
            }

            var trimmedQuery = query.Trim();

            // Fast-path: check if any field contains the entire query directly
            foreach (var field in fields)
            {
                if (!string.IsNullOrEmpty(field) && field.IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            // For multi-term queries: every term must match in at least one field
            var terms = trimmedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var term in terms)
            {
                var termMatched = false;
                foreach (var field in fields)
                {
                    if (!string.IsNullOrEmpty(field) && MatchesSingleTerm(field, term))
                    {
                        termMatched = true;
                        break;
                    }
                }
                if (!termMatched)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Scores how well a text matches a query. Returns 0 if no match, higher values indicate better matches.
        /// Useful for sorting suggestions or autocomplete results.
        /// </summary>
        public static int Score(string? text, string? query)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var trimmedQuery = query.Trim();

            // Exact match
            if (string.Equals(text, trimmedQuery, StringComparison.OrdinalIgnoreCase))
            {
                return 1000;
            }

            // Starts with
            if (text.StartsWith(trimmedQuery, StringComparison.OrdinalIgnoreCase))
            {
                return 800 - Math.Min(200, (text.Length - trimmedQuery.Length) * 2);
            }

            // Substring match
            var subIndex = text.IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase);
            if (subIndex >= 0)
            {
                return 600 - Math.Min(200, subIndex * 5);
            }

            // Multi-term scoring
            var terms = trimmedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var tokens = Tokenize(text);

            var totalTermScore = 0;
            foreach (var term in terms)
            {
                var bestTermScore = 0;
                foreach (var token in tokens)
                {
                    var tokenScore = ScoreTermAgainstToken(token, term);
                    if (tokenScore > bestTermScore)
                    {
                        bestTermScore = tokenScore;
                    }
                }

                // Check acronym
                var acronymScore = ScoreAcronym(tokens, term);
                if (acronymScore > bestTermScore)
                {
                    bestTermScore = acronymScore;
                }

                if (bestTermScore == 0)
                {
                    return 0; // All terms must have some match
                }

                totalTermScore += bestTermScore;
            }

            return Math.Max(1, totalTermScore / terms.Length);
        }

        private static bool MatchesSingleTerm(string text, string term)
        {
            if (text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var tokens = Tokenize(text);

            // Check if any token matches acronym, prefix, subsequence, or edit distance
            if (MatchesAcronym(tokens, term))
            {
                return true;
            }

            var termLen = term.Length;
            var maxDistance = termLen switch
            {
                <= 2 => 0,      // No typos for 1-2 char terms
                <= 4 => 1,      // 1 typo allowed for 3-4 chars
                _ => 2          // 2 typos allowed for 5+ chars
            };

            foreach (var token in tokens)
            {
                if (token.Length == 0) continue;

                // Subsequence matching (for terms >= 3 chars)
                if (termLen >= 3 && IsSubsequence(token, term))
                {
                    return true;
                }

                // Edit distance
                if (maxDistance > 0 && Math.Abs(token.Length - termLen) <= maxDistance)
                {
                    var dist = DamerauLevenshteinDistance(token.ToLowerInvariant(), term.ToLowerInvariant(), maxDistance);
                    if (dist <= maxDistance)
                    {
                        return true;
                    }
                }
            }

            // Whole-text subsequence for compound identifiers (e.g. "ovrt" matching "Preset_Overture")
            if (termLen >= 3 && IsSubsequence(text, term))
            {
                return true;
            }

            return false;
        }

        private static string[] Tokenize(string text)
        {
            // Split by common separators and camelCase / PascalCase boundaries
            var rawTokens = text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>(rawTokens.Length * 2);

            foreach (var raw in rawTokens)
            {
                result.Add(raw);

                // If token contains camelCase (e.g. "MantisBlades" -> "Mantis", "Blades")
                var lastStart = 0;
                for (var i = 1; i < raw.Length; i++)
                {
                    if (char.IsUpper(raw[i]) && !char.IsUpper(raw[i - 1]))
                    {
                        if (i - lastStart > 1)
                        {
                            result.Add(raw.Substring(lastStart, i - lastStart));
                        }
                        lastStart = i;
                    }
                }
                if (lastStart > 0 && raw.Length - lastStart > 1)
                {
                    result.Add(raw.Substring(lastStart));
                }
            }

            return result.ToArray();
        }

        private static bool MatchesAcronym(string[] tokens, string term)
        {
            if (term.Length < 2 || tokens.Length < term.Length)
            {
                return false;
            }

            // Check if term matches initials of tokens
            var matched = 0;
            for (var i = 0; i < tokens.Length && matched < term.Length; i++)
            {
                if (tokens[i].Length > 0 && char.ToUpperInvariant(tokens[i][0]) == char.ToUpperInvariant(term[matched]))
                {
                    matched++;
                }
            }

            return matched == term.Length;
        }

        private static int ScoreAcronym(string[] tokens, string term)
        {
            return MatchesAcronym(tokens, term) ? 350 : 0;
        }

        private static bool IsSubsequence(string target, string query)
        {
            var tIdx = 0;
            var qIdx = 0;
            var firstMatch = -1;
            var lastMatch = -1;

            while (tIdx < target.Length && qIdx < query.Length)
            {
                if (char.ToUpperInvariant(target[tIdx]) == char.ToUpperInvariant(query[qIdx]))
                {
                    if (firstMatch == -1) firstMatch = tIdx;
                    lastMatch = tIdx;
                    qIdx++;
                }
                tIdx++;
            }

            if (qIdx == query.Length)
            {
                // Ensure match isn't excessively spread out
                var span = lastMatch - firstMatch + 1;
                return span <= query.Length * 3;
            }

            return false;
        }

        private static int ScoreTermAgainstToken(string token, string term)
        {
            if (string.Equals(token, term, StringComparison.OrdinalIgnoreCase))
            {
                return 500;
            }
            if (token.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            {
                return 400 - Math.Min(100, (token.Length - term.Length) * 2);
            }
            if (token.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 300;
            }
            if (term.Length >= 3 && IsSubsequence(token, term))
            {
                return 200;
            }
            if (term.Length >= 3)
            {
                var dist = DamerauLevenshteinDistance(token.ToLowerInvariant(), term.ToLowerInvariant(), 2);
                if (dist <= 2)
                {
                    return Math.Max(50, 180 - dist * 50);
                }
            }
            return 0;
        }

        /// <summary>
        /// Calculates Damerau-Levenshtein distance with early-exit if exceeds maxDistance.
        /// </summary>
        public static int DamerauLevenshteinDistance(string source, string target, int maxDistance = int.MaxValue)
        {
            if (string.IsNullOrEmpty(source)) return string.IsNullOrEmpty(target) ? 0 : target.Length;
            if (string.IsNullOrEmpty(target)) return source.Length;

            var len1 = source.Length;
            var len2 = target.Length;

            if (Math.Abs(len1 - len2) > maxDistance) return maxDistance + 1;

            var d = new int[len1 + 1, len2 + 1];

            for (var i = 0; i <= len1; i++) d[i, 0] = i;
            for (var j = 0; j <= len2; j++) d[0, j] = j;

            for (var i = 1; i <= len1; i++)
            {
                var minRow = int.MaxValue;
                for (var j = 1; j <= len2; j++)
                {
                    var cost = (source[i - 1] == target[j - 1]) ? 0 : 1;
                    var val = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);

                    // Transposition
                    if (i > 1 && j > 1 && source[i - 1] == target[j - 2] && source[i - 2] == target[j - 1])
                    {
                        val = Math.Min(val, d[i - 2, j - 2] + cost);
                    }

                    d[i, j] = val;
                    if (val < minRow) minRow = val;
                }

                if (minRow > maxDistance) return maxDistance + 1;
            }

            return d[len1, len2];
        }
    }
}
