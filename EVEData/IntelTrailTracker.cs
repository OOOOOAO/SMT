//-----------------------------------------------------------------------
// Intel Trail Tracker (DEMO)
//
// Track the movement of an "enemy" mentioned in intel chat. Each intel
// line typically looks like:
//
//   <Reporter> > <System> <EnemyName1> [<EnemyName2> ...] <Ship> +N nv
//
// We do NOT have ESI character lookup yet for hostile pilots, so for
// this demo we apply a coarse heuristic:
//   - tokenise the intel text
//   - throw away tokens we can identify (matched system names, ship
//     types, count markers, common noise words)
//   - the first remaining token is taken as the "enemy id"
//
// This will misfire on:
//   - multiple hostile pilots in one report (we pick only the first)
//   - misspellings / abbreviations
//   - reports with no name at all (general "+5 reds")
//
// But it is good enough to *see* whether trails-on-the-map is a useful
// idea before investing in a proper parser.
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

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
        /// Tokens we throw away while looking for the enemy id. Anything in this
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
        };

        /// <summary>
        /// Process one intel line. Caller has already populated
        /// <see cref="IntelData.Systems"/>. We pick an enemy id and append a
        /// sighting in the *first* matched system (good enough for demo).
        /// </summary>
        public void Ingest(IntelData id, IEnumerable<string> shipNames)
        {
            if(id == null) return;
            if(id.ClearNotification) return;            // demo: skip clears
            if(id.Systems == null || id.Systems.Count == 0) return;

            string enemyId = ExtractEnemyId(id.IntelString, id.Systems, shipNames);
            if(string.IsNullOrEmpty(enemyId)) return;

            string firstSystem = id.Systems[0];

            lock(_lock)
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
                    return;
                }

                trail.Points.Add(new IntelTrailPoint
                {
                    SystemName = firstSystem,
                    Time = id.IntelTime,
                });
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

        public void Clear()
        {
            lock(_lock) { _trails.Clear(); }
        }

        // -------- internal heuristics --------

        private static string ExtractEnemyId(string intelText,
                                             IList<string> matchedSystems,
                                             IEnumerable<string> shipNames)
        {
            if(string.IsNullOrWhiteSpace(intelText)) return null;

            // Build per-call rejection sets so we treat 'Y-ZXIO' / 'Vargur' as known.
            var systems = new HashSet<string>(matchedSystems ?? Enumerable.Empty<string>(),
                                              StringComparer.OrdinalIgnoreCase);
            var ships = new HashSet<string>(shipNames ?? Enumerable.Empty<string>(),
                                            StringComparer.OrdinalIgnoreCase);

            foreach(var raw in intelText.Split(new[] { ' ', '\t', '\r', '\n' },
                                               StringSplitOptions.RemoveEmptyEntries))
            {
                var tok = raw.Trim().Trim(',', '.', ';', ':', '!', '?', '(', ')', '[', ']');
                if(tok.Length < 2) continue;

                // skip count markers like "+3" or pure digits
                if(tok[0] == '+' || tok.All(c => char.IsDigit(c))) continue;

                if(NoiseTokens.Contains(tok)) continue;
                if(systems.Contains(tok)) continue;
                if(ships.Contains(tok)) continue;

                // Also skip tokens that *contain* a known system name as substring,
                // e.g. "Y-ZXIO," after trimming punctuation we'd already match,
                // but defensive.
                bool isKnown = false;
                foreach(var s in systems)
                {
                    if(tok.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) { isKnown = true; break; }
                }
                if(isKnown) continue;

                // First survivor wins.
                return tok;
            }

            return null;
        }
    }
}
