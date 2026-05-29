//-----------------------------------------------------------------------
// Intel Trail Tracker
//
// Track the movement of enemies mentioned in intel chat. Each intel
// line typically looks like:
//
//   <Reporter> > <System>  <EnemyName1>  <EnemyName2>  <Ship> nv
//
// EVE client inserts double-spaces between linked items (system names,
// character names, ship types). We exploit this structure:
//   1. Split on two-or-more spaces → segments (each = one entity)
//   2. Discard segments that match known systems, ships, noise, or
//      Chinese ship names
//   3. All survivors are enemy candidates
//
// Falls back to old single-space tokenisation when the line contains
// no double-space separators.
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SMT.EVEData
{
    /// <summary>
    /// A single sighting of an enemy in a system at a point in time.
    /// </summary>
    public class IntelTrailPoint
    {
        public string SystemName { get; set; }
        public DateTime Time { get; set; }
    }

    /// <summary>
    /// Time-ordered list of sightings for one enemy id.
    /// </summary>
    public class EnemyTrail
    {
        public string EnemyId { get; set; }
        public List<IntelTrailPoint> Points { get; } = new List<IntelTrailPoint>();

        /// <summary>True when no points are inside the active window.</summary>
        public bool IsExpired(DateTime now, TimeSpan lifetime)
        {
            if(Points.Count == 0) return true;
            return (now - Points[Points.Count - 1].Time) > lifetime;
        }
    }

    public class IntelTrailTracker
    {
        private readonly Dictionary<string, EnemyTrail> _trails =
            new Dictionary<string, EnemyTrail>(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new object();

        /// <summary>How long after the last sighting a trail stays drawable.</summary>
        public TimeSpan TrailLifetime { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Currently focused enemy id (set by the UI when the user clicks a system
        /// on one of the trails). Used by the renderer to switch that trail into
        /// "active / flowing" presentation. null = no selection.
        /// </summary>
        public string SelectedEnemyId { get; set; }

        /// <summary>
        /// Find all enemy ids whose trail passes through <paramref name="systemName"/>
        /// (currently in the active window). Returned in most-recent-sighting-first
        /// order, so the caller can pick [0] as the "best guess" if multiple match.
        /// </summary>
        public List<string> EnemiesAtSystem(string systemName)
        {
            var hits = new List<(string id, DateTime lastSeen)>();
            if(string.IsNullOrEmpty(systemName)) return new List<string>();

            lock(_lock)
            {
                foreach(var kv in _trails)
                {
                    DateTime? lastSeenHere = null;
                    foreach(var p in kv.Value.Points)
                    {
                        if(string.Equals(p.SystemName, systemName, StringComparison.OrdinalIgnoreCase))
                        {
                            if(lastSeenHere == null || p.Time > lastSeenHere) lastSeenHere = p.Time;
                        }
                    }
                    if(lastSeenHere.HasValue) hits.Add((kv.Key, lastSeenHere.Value));
                }
            }
            hits.Sort((a, b) => b.lastSeen.CompareTo(a.lastSeen));
            return hits.ConvertAll(h => h.id);
        }

        /// <summary>
        /// Tokens we throw away while looking for enemy ids. Anything in this
        /// list is *definitely not* a pilot name.
        /// </summary>
        private static readonly HashSet<string> NoiseTokens = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            // counts / markers
            "nv", "neut", "neuts", "red", "reds", "hostile", "hostiles",
            "friendly", "friendlies", "blue", "blues",
            "clr", "clear", "cleared", "safe",
            "gate", "gates", "station", "stations", "dock", "docked",
            "on", "in", "at", "to", "from", "the", "a", "an", "and", "or",
            "is", "are", "was", "were", "have", "has", "had", "do", "don't",
            "engage", "engaging", "warning", "alert",
            "bubble", "bubbled", "bubbles",
            "fleet", "gang", "wing", "squad", "forming", "staging",
            "pilot", "pilots", "char",
            "kill", "killed", "kills",
            "mwd", "ab", "tackle", "tackled",
            "interdictor", "dictor", "hictor",
            "+1", "+2", "+3", "+4", "+5", "+6", "+7", "+8", "+9", "+10",
            "?", "??", "???", "...", "..", ".", ",", ";", ":", "!", "-",

            // Phase-2 additions — intel shorthand & filler words
            "hct", "ess", "mln", "min", "mb", "omw", "rgr", "kiki",
            "jumped", "jumping", "jump", "back", "out", "into", "through",
            "visual", "any", "those", "where", "here", "there",
            "got", "it", "we", "up", "down",
            "cloaked", "off", "like", "both", "need",
            "please", "give", "accurate", "numbers", "when",
            "ur", "reporting", "could", "be",
            "power", "prospect", "cat", "navy",
            "scanner", "probes", "ansi",
        };

        /// <summary>
        /// EVE character names cannot contain CJK characters. Any segment that
        /// contains Chinese/Japanese/Korean glyphs is definitely a ship name,
        /// game term, or chatter — never an enemy pilot name.
        /// </summary>
        private static bool ContainsCJK(string s)
        {
            foreach(char c in s)
            {
                // CJK Unified Ideographs (U+4E00–U+9FFF) + Extension A (U+3400–U+4DBF)
                if(c >= '\u4E00' && c <= '\u9FFF') return true;
                if(c >= '\u3400' && c <= '\u4DBF') return true;
            }
            return false;
        }

        /// <summary>Regex for the Kill: prefix line format.</summary>
        private static readonly Regex KillPrefixRegex = new Regex(
            @"^\s*kill\s*:\s*(.+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Regex to strip parenthesized ship info, e.g. "(Nemesis)", "(预言级)".</summary>
        private static readonly Regex ParenShipRegex = new Regex(
            @"\([^)]+\)",
            RegexOptions.Compiled);

        /// <summary>Regex for double-space splitting.</summary>
        private static readonly Regex DoubleSpaceRegex = new Regex(
            @"\s{2,}",
            RegexOptions.Compiled);

        /// <summary>
        /// Process one intel line. Caller has already populated
        /// <see cref="IntelData.Systems"/>. We extract enemy candidates
        /// and append a sighting in the *first* matched system for each.
        /// </summary>
        public void Ingest(IntelData id, IEnumerable<string> shipNames)
        {
            if(id == null) return;
            if(id.ClearNotification) return;
            if(id.Systems == null || id.Systems.Count == 0) return;

            string intelText = id.IntelString;

            // --- Handle "Kill:" prefix → remove trail instead of adding ---
            var killMatch = KillPrefixRegex.Match(intelText ?? string.Empty);
            if(killMatch.Success)
            {
                string killBody = killMatch.Groups[1].Value.Trim();
                // Strip parenthesized ship info: "CharName (Ship)" → "CharName"
                killBody = ParenShipRegex.Replace(killBody, "").Trim();
                killBody = killBody.TrimEnd('*').Trim();
                if(!string.IsNullOrEmpty(killBody))
                {
                    RemoveTrail(killBody);
                }
                return;
            }

            // --- Normal intel line: extract all enemy candidates ---
            List<string> enemies = ExtractEnemyCandidates(intelText, id.Systems, shipNames);
            if(enemies.Count == 0) return;

            string firstSystem = id.Systems[0];

            lock(_lock)
            {
                foreach(var enemyId in enemies)
                {
                    if(!_trails.TryGetValue(enemyId, out var trail))
                    {
                        trail = new EnemyTrail { EnemyId = enemyId };
                        _trails[enemyId] = trail;
                    }

                    // dedupe: don't record the same system back-to-back inside 30s
                    var lastPoint = trail.Points.Count > 0
                        ? trail.Points[trail.Points.Count - 1]
                        : null;
                    if(lastPoint != null &&
                       string.Equals(lastPoint.SystemName, firstSystem, StringComparison.OrdinalIgnoreCase) &&
                       (id.IntelTime - lastPoint.Time).TotalSeconds < 30)
                    {
                        continue;
                    }

                    trail.Points.Add(new IntelTrailPoint
                    {
                        SystemName = firstSystem,
                        Time = id.IntelTime,
                    });
                }
            }
        }

        /// <summary>
        /// Snapshot of all trails currently inside the lifetime window. Returns
        /// a copy so callers can iterate without holding the lock.
        /// </summary>
        public List<EnemyTrail> GetActiveTrails()
        {
            var now = DateTime.Now;
            var result = new List<EnemyTrail>();

            lock(_lock)
            {
                // drop expired entries while we're here
                var toRemove = new List<string>();
                foreach(var kv in _trails)
                {
                    if(kv.Value.IsExpired(now, TrailLifetime))
                    {
                        toRemove.Add(kv.Key);
                    }
                    else
                    {
                        // also drop points older than lifetime within the trail
                        var trimmed = new EnemyTrail { EnemyId = kv.Value.EnemyId };
                        foreach(var p in kv.Value.Points)
                        {
                            if((now - p.Time) <= TrailLifetime)
                            {
                                trimmed.Points.Add(p);
                            }
                        }
                        if(trimmed.Points.Count >= 2) // need ≥2 points to draw a line
                        {
                            result.Add(trimmed);
                        }
                    }
                }
                foreach(var k in toRemove) _trails.Remove(k);
            }

            return result;
        }

        /// <summary>
        /// Remove the trail for a specific enemy (e.g. after a ZKill confirms death).
        /// </summary>
        public bool RemoveTrail(string enemyId)
        {
            if(string.IsNullOrEmpty(enemyId)) return false;
            lock(_lock)
            {
                return _trails.Remove(enemyId);
            }
        }

        public void Clear()
        {
            lock(_lock) { _trails.Clear(); }
        }

        // -------- internal heuristics --------

        /// <summary>
        /// Extract all enemy-candidate names from an intel line.
        ///
        /// Primary strategy: split on double-space boundaries (EVE client
        /// inserts ≥2 spaces between linked entities) and test each segment.
        ///
        /// Fallback: if no double-spaces exist in the text, revert to the
        /// original single-space tokenisation.
        /// </summary>
        internal static List<string> ExtractEnemyCandidates(string intelText,
                                             IList<string> matchedSystems,
                                             IEnumerable<string> shipNames)
        {
            var result = new List<string>();
            if(string.IsNullOrWhiteSpace(intelText)) return result;

            // Build per-call rejection sets
            var systems = new HashSet<string>(matchedSystems ?? Enumerable.Empty<string>(),
                                              StringComparer.OrdinalIgnoreCase);
            var ships = new HashSet<string>(shipNames ?? Enumerable.Empty<string>(),
                                            StringComparer.OrdinalIgnoreCase);

            bool hasDoubleSpaces = DoubleSpaceRegex.IsMatch(intelText);

            if(hasDoubleSpaces)
            {
                // --- Double-space segmentation (primary path) ---
                var segments = DoubleSpaceRegex.Split(intelText);
                foreach(var rawSeg in segments)
                {
                    string seg = rawSeg.Trim();
                    if(string.IsNullOrEmpty(seg)) continue;

                    // Strip trailing * (EVE link artifact)
                    seg = seg.TrimEnd('*').Trim();
                    if(string.IsNullOrEmpty(seg)) continue;

                    // Strip parenthesized ship info: "(Nemesis)", "(预言级)"
                    seg = ParenShipRegex.Replace(seg, "").Trim();
                    if(string.IsNullOrEmpty(seg)) continue;

                    // Strip leading & at segment edges (e.g. "& red hound")
                    seg = seg.Trim('&').Trim();
                    if(string.IsNullOrEmpty(seg)) continue;

                    // Skip short junk
                    if(seg.Length < 2) continue;

                    // Skip count markers like "+3" or pure digits
                    if(seg[0] == '+' && seg.Length <= 3) continue;
                    if(seg.All(c => char.IsDigit(c))) continue;

                    // Check as whole segment against rejection sets
                    if(NoiseTokens.Contains(seg)) continue;
                    if(systems.Contains(seg)) continue;
                    if(ships.Contains(seg)) continue;
                    if(ContainsCJK(seg)) continue;

                    // Also check if segment *contains* a system name as
                    // substring (defensive against "Y-ZXIO*" after trim)
                    bool isKnown = false;
                    foreach(var s in systems)
                    {
                        if(seg.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isKnown = true;
                            break;
                        }
                    }
                    if(isKnown) continue;

                    // For multi-word segments, also check if every word is noise
                    // (e.g. "ess 5 min 205mln" — each sub-word is noise/number)
                    if(seg.Contains(' '))
                    {
                        var subTokens = seg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        bool allNoise = subTokens.All(t =>
                        {
                            var st = t.Trim(',', '.', ';', ':', '!', '?').TrimEnd('*');
                            return st.Length < 2 ||
                                   st.All(c => char.IsDigit(c)) ||
                                   (st[0] == '+' && st.Length <= 3) ||
                                   NoiseTokens.Contains(st) ||
                                   ships.Contains(st) ||
                                   ContainsCJK(st);
                        });
                        if(allNoise) continue;
                    }

                    // Survivor — this segment is an enemy candidate
                    result.Add(seg);
                }
            }
            else
            {
                // --- Fallback: single-space tokenisation (legacy path) ---
                foreach(var raw in intelText.Split(new[] { ' ', '\t', '\r', '\n' },
                                                   StringSplitOptions.RemoveEmptyEntries))
                {
                    var tok = raw.Trim().Trim(',', '.', ';', ':', '!', '?', '(', ')', '[', ']');
                    tok = tok.TrimEnd('*');
                    if(tok.Length < 2) continue;

                    // skip count markers like "+3" or pure digits
                    if(tok[0] == '+' || tok.All(c => char.IsDigit(c))) continue;

                    if(NoiseTokens.Contains(tok)) continue;
                    if(systems.Contains(tok)) continue;
                    if(ships.Contains(tok)) continue;
                    if(ContainsCJK(tok)) continue;

                    // Also skip tokens that *contain* a known system name as
                    // substring
                    bool isKnown = false;
                    foreach(var s in systems)
                    {
                        if(tok.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) { isKnown = true; break; }
                    }
                    if(isKnown) continue;

                    // Collect all survivors (not just the first)
                    result.Add(tok);
                }
            }

            return result;
        }
    }
}
