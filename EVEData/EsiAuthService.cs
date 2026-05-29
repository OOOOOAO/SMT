//-----------------------------------------------------------------------
// ESI Authentication Service
// Extracted from EveManager to isolate OAuth PKCE flow and SSO concerns.
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using EVEStandard;
using EVEStandard.Enumerations;
using EVEStandard.Models.SSO;

namespace SMT.EVEData
{
    /// <summary>
    /// Handles ESI SSO authentication (PKCE flow) - logon URL generation,
    /// callback handling, and token exchange.
    /// </summary>
    public class EsiAuthService
    {
        private readonly SSOv2 _sso;
        private readonly List<string> _scopes;
        private readonly string _versionStr;

        /// <summary>
        /// Called when a new character is authenticated and needs to be resolved
        /// or created in the character manager.
        /// </summary>
        public Func<string, LocalCharacter> FindCharacter { get; set; }

        /// <summary>
        /// Called when a brand-new character needs to be added to tracking.
        /// </summary>
        public Action<LocalCharacter> AddCharacter { get; set; }

        /// <summary>
        /// Pending PKCE code_verifier for the next callback.
        /// </summary>
        private string _pendingPkceCodeVerifier;

        /// <summary>
        /// Provides access to the underlying SSO client (needed by LocalCharacter token refresh).
        /// </summary>
        public SSOv2 Sso => _sso;

        /// <summary>
        /// The ESI scopes requested during authentication.
        /// </summary>
        public IReadOnlyList<string> Scopes => _scopes;

        public EsiAuthService(string versionStr)
        {
            _versionStr = versionStr;
            _sso = new SSOv2(DataSource.Tranquility, EveAppConfig.CallbackURL, EveAppConfig.ClientID, null);

            _scopes = new List<string>
            {
                "publicData",
                "esi-location.read_location.v1",
                "esi-location.read_ship_type.v1",
                "esi-skills.read_skills.v1",
                "esi-skills.read_skillqueue.v1",
                "esi-wallet.read_character_wallet.v1",
                "esi-wallet.read_corporation_wallet.v1",
                "esi-search.search_structures.v1",
                "esi-characters.read_contacts.v1",
                "esi-universe.read_structures.v1",
                "esi-corporations.read_corporation_membership.v1",
                "esi-assets.read_assets.v1",
                "esi-planets.manage_planets.v1",
                "esi-fleets.read_fleet.v1",
                "esi-fleets.write_fleet.v1",
                "esi-ui.open_window.v1",
                "esi-ui.write_waypoint.v1",
                "esi-characters.write_contacts.v1",
                "esi-markets.structure_markets.v1",
                "esi-corporations.read_structures.v1",
                "esi-characters.read_loyalty.v1",
                "esi-characters.read_chat_channels.v1",
                "esi-characters.read_medals.v1",
                "esi-characters.read_standings.v1",
                "esi-characters.read_agents_research.v1",
                "esi-industry.read_character_jobs.v1",
                "esi-markets.read_character_orders.v1",
                "esi-characters.read_blueprints.v1",
                "esi-characters.read_corporation_roles.v1",
                "esi-location.read_online.v1",
                "esi-characters.read_fatigue.v1",
                "esi-corporations.track_members.v1",
                "esi-wallet.read_corporation_wallets.v1",
                "esi-characters.read_notifications.v1",
                "esi-corporations.read_divisions.v1",
                "esi-corporations.read_contacts.v1",
                "esi-assets.read_corporation_assets.v1",
                "esi-corporations.read_titles.v1",
                "esi-corporations.read_blueprints.v1",
                "esi-corporations.read_standings.v1",
                "esi-corporations.read_starbases.v1",
                "esi-industry.read_corporation_jobs.v1",
                "esi-markets.read_corporation_orders.v1",
                "esi-corporations.read_container_logs.v1",
                "esi-industry.read_character_mining.v1",
                "esi-industry.read_corporation_mining.v1",
                "esi-planets.read_customs_offices.v1",
                "esi-corporations.read_facilities.v1",
                "esi-corporations.read_medals.v1",
                "esi-characters.read_titles.v1",
                "esi-alliances.read_contacts.v1",
                "esi-characters.read_fw_stats.v1",
                "esi-corporations.read_fw_stats.v1",
                "esi-corporations.read_projects.v1",
                "esi-corporations.read_freelance_jobs.v1",
                "esi-characters.read_freelance_jobs.v1",
                "esi-structures.read_corporation.v1",
                "esi-structures.read_character.v1",
                "esi-activities.read_character.v1",
                "esi-access.read_lists.v1"
            };
        }

        /// <summary>
        /// Get the ESI Logon URL String. Uses PKCE derivation:
        /// code_verifier = base64url(UTF8(challengeCode)),
        /// code_challenge = base64url(SHA256(UTF8(code_verifier))).
        /// </summary>
        public string GetESILogonURL(string challengeCode)
        {
            string codeVerifier = ToBase64UrlString(Encoding.UTF8.GetBytes(challengeCode));
            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
            }
            string codeChallenge = ToBase64UrlString(hash);
            _pendingPkceCodeVerifier = codeVerifier;
            return _sso.AuthorizeToSSOPKCEUri(_versionStr, codeChallenge, _scopes);
        }

        /// <summary>
        /// Handle the custom smtauth- callback URL from the logon screen.
        /// </summary>
        public async void HandleEveAuthSMTUri(Uri uri, string challengeCode)
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            if (query["code"] == null)
                return;

            string code = query["code"];
            string codeVerifier = _pendingPkceCodeVerifier ?? ToBase64UrlString(Encoding.UTF8.GetBytes(challengeCode));
            _pendingPkceCodeVerifier = null;

            AccessTokenDetails tokenDetails;
            try
            {
                tokenDetails = await _sso.VerifyAuthorizationForPKCEAuthAsync(code, codeVerifier);
                if (tokenDetails == null || tokenDetails.ExpiresIn <= 0)
                    return;
            }
            catch
            {
                return;
            }

            CharacterDetails characterDetails;
            try
            {
                characterDetails = await _sso.GetCharacterDetailsAsync(tokenDetails.AccessToken);
                if (characterDetails == null)
                    return;
            }
            catch
            {
                return;
            }

            LocalCharacter esiChar = FindCharacter?.Invoke(characterDetails.CharacterName);
            if (esiChar == null)
            {
                esiChar = new LocalCharacter(characterDetails.CharacterName, string.Empty, string.Empty);
                AddCharacter?.Invoke(esiChar);
            }

            esiChar.ESIRefreshToken = tokenDetails.RefreshToken;
            esiChar.ESILinked = true;
            esiChar.ESIAccessToken = tokenDetails.AccessToken;
            esiChar.ESIAccessTokenExpiry = tokenDetails.ExpiresUtc.ToLocalTime();
            esiChar.ID = characterDetails.CharacterId;
            esiChar.ESIScopesStored = characterDetails.Scopes != null ? string.Join(" ", characterDetails.Scopes) : string.Empty;
        }

        private static string ToBase64UrlString(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
