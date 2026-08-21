using EVEStandard.Models;
using EVEStandard.Models.API;
using EVEStandard.Models.SSO;

namespace SMT.EVEData
{
    /// <summary>
    /// Pushes one <see cref="RouteLine"/> to the character's in-game autopilot. Deliberately
    /// its own thin ESI wrapper rather than <see cref="LocalCharacter.AddDestination"/> : that path mutates
    /// shared <see cref="LocalCharacter.Waypoints"/>/<see cref="LocalCharacter.ActiveRoute"/> state guarded by
    /// a lock this code has no business taking, and swallows failures with an empty catch.
    /// </summary>
    public static class BookmarkRoutePusher
    {
        /// <summary>
        /// Applies a line's targets as autopilot waypoints, one ESI call at a time. The first waypoint clears
        /// whatever route is already set in-game ; the rest append, so the client draws only this line's
        /// gate path instead of bridging the jump gap with a long gate detour.
        /// Returns null on success, or a user-facing error message on failure.
        /// </summary>
        public static async Task<string> ApplyLineAsync(LocalCharacter character, RouteLine line)
        {
            if (character == null)
            {
                return "No active character.";
            }

            if (line == null || line.Targets.Count == 0)
            {
                return "Line has no waypoints.";
            }

            AuthDTO auth = character.GetAuthDTO();
            if (auth == null)
            {
                return "Character is not ESI linked.";
            }

            bool first = true;
            foreach (string sysName in line.Targets)
            {
                System sys = EveManager.Instance.GetEveSystem(sysName);
                if (sys == null)
                {
                    return $"Unknown system: {sysName}";
                }

                try
                {
                    // (auth, addToBeginning, clearOtherWaypoints, destinationId) -- verified against the
                    // EVEStandard 4.0.2 DLL metadata. This call returns a bare Task (the ESI
                    // "post waypoint" endpoint has no response body), so there's no ESIModelDTO to hand to
                    // ESIHelpers for rate-limit tracking on this specific call.
                    await EveManager.Instance.EveApiClient.UserInterface.SetAutopilotWaypointAsync(auth, false, first, sys.ID);
                }
                catch (Exception ex)
                {
                    return $"ESI push failed at {sysName}: {ex.Message}";
                }

                first = false;
                await Task.Delay(200);
            }

            return null;
        }
    }
}
