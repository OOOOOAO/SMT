namespace SMT.EVEData
{
    /// <summary>
    /// Parses the tab-separated text copied from the in-game "Locations" window.
    /// Takes a system-name resolver delegate so it can be unit-checked without EveManager /
    /// the 33MB data files (see <see cref="BookmarkRouteSelfCheck"/>).
    /// </summary>
    public static class BookmarkParser
    {
        // Free-text columns a bookmark's stranger-author controls : name (0), notes (7), creator (8).
        // Skipped during the fallback scan so a bookmark named after a destination can't hijack the row.
        private static readonly int[] FreeTextColumns = { 0, 7, 8 };

        public static ParseResult Parse(string text, Func<string, string> resolveSystemName)
        {
            ParseResult result = new ParseResult();

            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] fields = line.Split('\t');
                string resolved = null;

                // Primary : the real sample format is exactly 9 tab-separated columns, system name at index 3.
                if (fields.Length == 9)
                {
                    resolved = resolveSystemName(fields[3].Trim());
                }

                // Fallback : column count is off, or index 3 didn't resolve. Scan the rest, skipping free text.
                if (resolved == null)
                {
                    for (int idx = 0; idx < fields.Length; idx++)
                    {
                        if (Array.IndexOf(FreeTextColumns, idx) >= 0)
                        {
                            continue;
                        }

                        resolved = resolveSystemName(fields[idx].Trim());
                        if (resolved != null)
                        {
                            break;
                        }
                    }
                }

                if (resolved != null)
                {
                    result.BookmarkCounts[resolved] = result.BookmarkCounts.GetValueOrDefault(resolved) + 1;
                }
                else
                {
                    result.UnparsedLines.Add(line);
                }
            }

            return result;
        }
    }

    public class ParseResult
    {
        public Dictionary<string, int> BookmarkCounts { get; } = new Dictionary<string, int>();

        public List<string> UnparsedLines { get; } = new List<string>();
    }
}
