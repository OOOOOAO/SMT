using System.ComponentModel;
using System.Text;
using System.Xml.Serialization;
using EVEStandard.Models;
using EVEStandard.Models.API;
using EVEStandard.Models.SSO;

namespace SMT.EVEData
{
    //jumpclones
    /// <summary>
    /// Represents a single asset item in a solar system.
    /// </summary>
    public class AssetEntry
    {
        public long TypeId { get; set; }
        public string TypeName { get; set; }
        public long Quantity { get; set; }
        public string LocationFlag { get; set; }
        public string CharacterName { get; set; }

        /// <summary>
        /// The ESI item_id of this asset. Used to detect containers.
        /// </summary>
        public long ItemId { get; set; }

        /// <summary>
        /// True if other assets list this item as their parent (i.e. it's a ship/container with contents).
        /// Items flagged true should appear as expandable container headers, not in the flat standalone list.
        /// </summary>
        public bool IsContainer { get; set; }

        /// <summary>
        /// The ItemId of the direct parent container (ship/can). 0 if directly in station.
        /// </summary>
        public long ParentItemId { get; set; }

        /// <summary>
        /// Resolved location path from outermost container to direct parent.
        /// Empty = item directly in station hangar or solar system.
        /// e.g. ["Jita Trading Hub"] = item in player structure.
        /// e.g. ["Jita Trading Hub", "My Caracal"] = item in ship in structure.
        /// </summary>
        public List<string> LocationPath { get; set; } = new List<string>();
    }


    public class AssetCacheWrapper
    {
        public int    Version       { get; set; } = 2;   // bump when AssetEntry schema changes
        public string CharacterName { get; set; }
        public string Language      { get; set; }
        public DateTime UpdatedAt   { get; set; }
        public Dictionary<long, List<AssetEntry>> AssetsBySystem { get; set; }
    }

    public class LocalCharacter : Character, INotifyPropertyChanged 
    {
        public static readonly string SaveVersion = "03";

        [XmlIgnoreAttribute]
        public object ActiveRouteLock;

        [XmlIgnoreAttribute]
        public SemaphoreSlim UpdateLock;

        [XmlIgnoreAttribute]
        public bool warningSystemsNeedsUpdate;

        /// <summary>
        /// Ship Decloak Event Handler
        /// </summary>
        public delegate void RouteUpdatedEventHandler();

        /// <summary>
        /// Ship Decloaked
        /// </summary>
        public event RouteUpdatedEventHandler RouteUpdatedEvent;

        private bool esiRouteNeedsUpdate;

        private bool esiRouteUpdating;

        private bool esiSendRouteClear;

        /// <summary>
        /// The name of the system this character is currently in
        /// </summary>
        private string location;

        private RoutingMode m_NavigationMode;

        private bool m_UseAnsiblexGates;

        private bool m_UseTheraRouting;

        private bool m_UseZarzakhRouting;

        private bool m_UseTurnurRouting;

        private bool m_isOnline;

        private bool m_ObservatoryDecloakWarningEnabled;

        private bool m_GateDecloakWarningEnabled;

        private bool m_DecloakWarningEnabled;

        private bool m_CombatWarningEnabled;

        private bool routeNeedsUpdate = false;

        private int ssoErrorCount = 0;

        private int m_activeRouteLength = 0;

        private bool m_updateTick = true;
        private DateTime m_nextAssetUpdate = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="Character" /> class
        /// </summary>
        public LocalCharacter()
        {
            UseAnsiblexGates = true;

            ESILinked = false;
            ESIAuthCode = string.Empty;
            ESIAccessToken = string.Empty;
            ESIRefreshToken = string.Empty;

            Standings = new Dictionary<long, float>();

            LabelMap = new Dictionary<long, long>();
            LabelNames = new Dictionary<long, string>();

            FleetInfo = new Fleet();

            FleetInfo.IsFleetBoss = false;
            FleetInfo.FleetID = 0;

            // Random Offset to stop all the errors hitting at once
            Random R = new Random();
            int randomOffset = R.Next(90);
            FleetInfo.NextFleetMembershipCheck = DateTime.Now + TimeSpan.FromSeconds(randomOffset);

            Waypoints = new List<string>();
            ActiveRoute = new List<Navigation.RoutePoint>();

            ActiveRouteLock = new object();
            UpdateLock = new SemaphoreSlim(1);

            CorporationID = -1;
            CorporationName = null;
            CorporationTicker = null;
            AllianceID = -1;
            AllianceName = null;
            AllianceTicker = null;

            DangerZoneRange = 5;

            UseAnsiblexGates = true;

            IsOnline = true;
            CombatWarningEnabled = true;
            ObservatoryDecloakWarningEnabled = true;
            DecloakWarningEnabled = true;
            GateDecloakWarningEnabled = true;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Character" /> class.
        /// </summary>
        /// <param name="name">Name of Character</param>
        /// <param name="lcf">Local Chat File Location</param>
        /// <param name="location">Current Location of Character</param>
        public LocalCharacter(string name, string lcf, string location)
            : this()
        {
            Name = name;
            LocalChatFile = lcf;
            Location = location;
            IsOnline = true;

            CombatWarningEnabled = false;
            ObservatoryDecloakWarningEnabled = true;
            DecloakWarningEnabled = true;
            GateDecloakWarningEnabled = true;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets or sets the current active route
        /// </summary>
        [XmlIgnoreAttribute]
        public List<Navigation.RoutePoint> ActiveRoute { get; set; }

        public int ActiveRouteLength
        {
            get
            {
                return m_activeRouteLength;
            }
            set
            {
                m_activeRouteLength = value;
                OnPropertyChanged("ActiveRouteLength");
            }
        }

        public bool DangerZoneActive { get; set; }
        public bool DeepSearchEnabled { get; set; }

        /// <summary>
        /// Gets or sets the ESI access token
        /// </summary>
        [XmlIgnoreAttribute]
        public string ESIAccessToken { get; set; }

        /// <summary>
        /// Gets or sets the ESI access token expiry time
        /// </summary>
        public DateTime ESIAccessTokenExpiry { get; set; }

        /// <summary>
        /// Gets or sets the ESI auth code
        /// </summary>
        public string ESIAuthCode { get; set; }

        /// <summary>
        /// Stored scopes string for building AuthDTO (space-separated from CharacterDetails.Scopes).
        /// </summary>
        public string ESIScopesStored { get; set; }

        /// <summary>
        /// Gets or sets if this character is linked with ESI
        /// </summary>
        public bool ESILinked { get; set; }

        /// <summary>
        /// Gets or sets the ESI refresh Token
        /// </summary>
        public string ESIRefreshToken { get; set; }

        /// <summary>
        /// Builds AuthDTO for EVEStandard API calls. Returns null if not ESI linked or token missing.
        /// </summary>
        public AuthDTO GetAuthDTO()
        {
            if (!ESILinked || ID == 0 || string.IsNullOrEmpty(ESIAccessToken))
                return null;
            var expiry = ESIAccessTokenExpiry.Kind == DateTimeKind.Utc ? ESIAccessTokenExpiry : ESIAccessTokenExpiry.ToUniversalTime();
            return new AuthDTO
            {
                CharacterId = ID,
                AccessToken = new AccessTokenDetails
                {
                    AccessToken = ESIAccessToken,
                    RefreshToken = ESIRefreshToken ?? string.Empty,
                    ExpiresUtc = expiry
                },
                Scopes = ESIScopesStored ?? string.Empty
            };
        }

        /// <summary>
        /// Gets or sets the current fleet info for this character
        /// </summary>
        [XmlIgnoreAttribute]
        public Fleet FleetInfo { get; set; }

        /// <summary>
        /// Gets assets grouped by solar system ID. Key = solar system ID, Value = asset count.
        /// Updated by UpdateAssetsFromESI().
        /// </summary>
        [XmlIgnoreAttribute]
        public Dictionary<long, List<AssetEntry>> AssetsBySystem { get; set; } = new Dictionary<long, List<AssetEntry>>();

        /// <summary>
        /// Fleet Updated Event Handler
        /// </summary>
        public delegate void FleetUpdatedHandler(LocalCharacter fleetOwner);

        /// <summary>
        /// Fleet Updated Events
        /// </summary>
        public event FleetUpdatedHandler FleetUpdatedEvent;

        public bool IsOnline
        {
            get
            {
                return m_isOnline;
            }
            set
            {
                m_isOnline = value;
                OnPropertyChanged("IsOnline");
            }
        }

        public bool ObservatoryDecloakWarningEnabled
        {
            get
            {
                return m_ObservatoryDecloakWarningEnabled;
            }
            set
            {
                m_ObservatoryDecloakWarningEnabled = value;
                OnPropertyChanged("ObservatoryDecloakWarningEnabled");
            }
        }

        public bool GateDecloakWarningEnabled
        {
            get
            {
                return m_GateDecloakWarningEnabled;
            }
            set
            {
                m_GateDecloakWarningEnabled = value;
                OnPropertyChanged("GateDecloakWarningEnabled");
            }
        }

        public bool DecloakWarningEnabled
        {
            get
            {
                return m_DecloakWarningEnabled;
            }
            set
            {
                m_DecloakWarningEnabled = value;
                OnPropertyChanged("DecloakWarningEnabled");
            }
        }

        public bool CombatWarningEnabled
        {
            get
            {
                return m_CombatWarningEnabled;
            }
            set
            {
                m_CombatWarningEnabled = value;
                OnPropertyChanged("CombatWarningEnabled");
            }
        }

        private string m_gameLogWarningText;

        [XmlIgnoreAttribute]
        public string GameLogWarningText
        {
            get
            {
                return m_gameLogWarningText;
            }
            set
            {
                m_gameLogWarningText = value;
                if (string.IsNullOrEmpty(value))
                {
                    WarningState = "";
                }
                else
                {
                    WarningState = "Warning";
                }

                OnPropertyChanged("GameLogWarningText");
                OnPropertyChanged("WarningState");
            }
        }

        public string WarningState { get; set; }

        [XmlIgnoreAttribute]
        public Dictionary<long, long> LabelMap { get; set; }

        [XmlIgnoreAttribute]
        public Dictionary<long, string> LabelNames { get; set; }

        /// <summary>
        /// Gets or sets the The location of the local file bound to this session's "Local" chat channel
        /// </summary>
        public string LocalChatFile { get; set; }

        /// <summary>
        /// Gets or sets the location of the character
        /// </summary>
        public string Location
        {
            get
            {
                return location;
            }

            set
            {
                if (location == value)
                {
                    return;
                }

                location = value;
                routeNeedsUpdate = true;
                warningSystemsNeedsUpdate = true;

                // clear the warning everytime the location updates
                GameLogWarningText = "";
                OnPropertyChanged("Location");
            }
        }

        public RoutingMode NavigationMode
        {
            get
            {
                return m_NavigationMode;
            }
            set
            {
                if (m_NavigationMode == value)
                {
                    return;
                }

                m_NavigationMode = value;
                routeNeedsUpdate = true;
                OnPropertyChanged("NavigationMode");
            }
        }

        public string Region { get; set; }

        /// <summary>
        /// Gets or sets the character standings dictionary
        /// </summary>
        [XmlIgnoreAttribute]
        public Dictionary<long, float> Standings { get; set; }

        public bool UseAnsiblexGates
        {
            get
            {
                return m_UseAnsiblexGates;
            }
            set
            {
                if (m_UseAnsiblexGates == value)
                {
                    return;
                }

                m_UseAnsiblexGates = value;
                routeNeedsUpdate = true;
                esiRouteNeedsUpdate = true;
                OnPropertyChanged("UseAnsiblexGates");
            }
        }

        public bool UseTheraRouting
        {
            get
            {
                return m_UseTheraRouting;
            }
            set
            {
                if (m_UseTheraRouting == value)
                {
                    return;
                }

                m_UseTheraRouting = value;
                routeNeedsUpdate = true;
                esiRouteNeedsUpdate = true;
                OnPropertyChanged("UseTheraRouting");
            }
        }

        public bool UseZarzakhRouting
        {
            get
            {
                return m_UseZarzakhRouting;
            }
            set
            {
                if (m_UseZarzakhRouting == value)
                {
                    return;
                }

                m_UseZarzakhRouting = value;
                routeNeedsUpdate = true;
                esiRouteNeedsUpdate = true;
                OnPropertyChanged("UseZarzakhRouting");
            }
        }

        public bool UseTurnurRouting
        {
            get
            {
                return m_UseTurnurRouting;
            }
            set
            {
                if (m_UseTurnurRouting == value)
                {
                    return;
                }

                m_UseTurnurRouting = value;
                routeNeedsUpdate = true;
                esiRouteNeedsUpdate = true;
                OnPropertyChanged("UseTurnurRouting");
            }
        }

        public int DangerZoneRange { get; set; }

        [XmlIgnoreAttribute]
        public List<string> WarningSystems { get; set; }

        [XmlIgnoreAttribute]
        public Uri PortraitLocation { get; set; }

        [XmlIgnoreAttribute]
        public String AlertText { get; set; }

        [XmlIgnoreAttribute]
        public bool EdenCommStandingsGood { get; set; }

        [XmlIgnoreAttribute]
        public bool TrigStandingsGood { get; set; }

        [XmlIgnoreAttribute]
        public List<string> Waypoints { get; set; }

        [XmlIgnoreAttribute]
        public List<string> JumpWaypoints { get; set; }

        /// <summary>
        /// Add Destination to the route
        /// </summary>
        /// <param name="systemID">System to set destination to</param>
        /// <param name="clear">Clear all waypoints before setting?</param>
        public void AddDestination(long systemID, bool clear)
        {
            lock (ActiveRouteLock)
            {
                if (clear)
                {
                    Waypoints.Clear();
                    ActiveRoute.Clear();
                    ActiveRouteLength = 0;
                }
            }

            Waypoints.Add(EveManager.Instance.SystemIDToName[systemID]);

            routeNeedsUpdate = true;
            esiRouteNeedsUpdate = true;
        }

        public void ClearAllWaypoints()
        {
            lock (ActiveRouteLock)
            {
                ActiveRoute.Clear();
                ActiveRouteLength = 0;
                Waypoints.Clear();
            }
            routeNeedsUpdate = true;
            esiSendRouteClear = true;
        }

        public async Task<List<JumpBridge>> FindJumpGates(string JumpBridgeFilterString = " » ")
        {
            List<JumpBridge> jbl = new List<JumpBridge>();

            if (!ESILinked)
                return jbl;

            await UpdateLock.WaitAsync();
            {
                AuthDTO auth = GetAuthDTO();
                if (auth == null)
                {
                    UpdateLock.Release();
                    return jbl;
                }

                try
                {
                    var esr = await EveManager.Instance.EveApiClient.Search.SearchCharacterAsync(auth, new List<string> { "structure" }, JumpBridgeFilterString);
                    if (!ESIHelpers.ValidateESICall(esr) || esr.Model == null)
                    {
                        UpdateLock.Release();
                        return jbl;
                    }

                    List<long> structureIds = esr.Model.Structure ?? new List<long>();
                    foreach (long stationID in structureIds)
                    {
                        var esrs = await EveManager.Instance.EveApiClient.Universe.GetStructureInfoAsync(auth, stationID);
                        if (ESIHelpers.ValidateESICall(esrs) && esrs.Model != null)
                        {
                            if (esrs.Model.TypeId == 35841)
                            {
                                string[] parts = (esrs.Model.Name ?? string.Empty).Split(' ');
                                if (parts.Length >= 3)
                                {
                                    string from = parts[0];
                                    string to = parts[2];
                                    EveManager.Instance.AddUpdateJumpBridge(from, to, stationID);
                                }
                            }
                        }
                        Thread.Sleep(100);
                    }
                }
                catch
                {
                    // ESI-Search failed
                }
            }
            UpdateLock.Release();

            return jbl;
        }

        public string GetWayPointText()
        {
            string ClipboardText = "Waypoints\n==============\n";

            lock (ActiveRouteLock)
            {
                foreach (Navigation.RoutePoint rp in ActiveRoute)
                {
                    string WayPointText = string.Empty;
                    long wayPointSysID = EveManager.Instance.GetEveSystem(rp.SystemName).ID;
                    // explicitly add interim waypoints for ansiblex gates or actual waypoints
                    if (rp.GateToTake == Navigation.GateType.Ansiblex)
                    {
                        bool isSystemLink = true;

                        if (rp.GateToTake == Navigation.GateType.Ansiblex)
                        {
                            string GateDesto = string.Empty;

                            foreach (JumpBridge jb in EveManager.Instance.JumpBridges)
                            {
                                if (jb.From == rp.SystemName)
                                {
                                    if (jb.FromID != 0)
                                    {
                                        wayPointSysID = jb.FromID;
                                        isSystemLink = false;
                                    }

                                    GateDesto = jb.To;
                                    break;
                                }

                                if (jb.To == rp.SystemName)
                                {
                                    if (jb.ToID != 0)
                                    {
                                        wayPointSysID = jb.ToID;
                                        isSystemLink = false;
                                    }

                                    GateDesto = jb.From;
                                    break;
                                }
                            }

                            if (isSystemLink)
                            {
                                WayPointText = "Ansiblex: <url=showinfo:5//" + wayPointSysID + ">" + rp.SystemName + " » " + GateDesto + " </url>\n";
                            }
                            else
                            {
                                WayPointText = "Ansiblex: <url=showinfo:35841//" + wayPointSysID + ">" + rp.SystemName + " » " + GateDesto + "</url>\n";
                            }
                        }
                    }

                    if (Waypoints.Contains(rp.SystemName))
                    {
                        // regular waypoint
                        wayPointSysID = EveManager.Instance.GetEveSystem(rp.SystemName).ID;

                        WayPointText = "<url=showinfo:5//" + wayPointSysID + ">" + rp.SystemName + "</url>\n";
                    }

                    ClipboardText += WayPointText;
                }
            }

            return ClipboardText;
        }

        public void RecalcRoute()
        {
            routeNeedsUpdate = true;
            esiRouteNeedsUpdate = true;
        }

        /// <summary>
        /// To String
        /// </summary>
        public override string ToString()
        {
            string toStr = Name;
            if (ESILinked)
            {
                toStr += " (ESI)";
            }

            return toStr;
        }
        
        

        /// <summary>
        /// Update the Character info
        /// </summary>
        public async Task Update()
        {
            await UpdateLock.WaitAsync();
            {

                TimeSpan ts = ESIAccessTokenExpiry - DateTime.Now;
                if (ts.Minutes < 1)
                {
                    await RefreshAccessToken().ConfigureAwait(false);
                    await UpdateInfoFromESI().ConfigureAwait(false);
                }

                // if we're forcing ESI for our location OR we havent had one yet (due to timeout errors with the location endpoint)
                if (EveManager.Instance.UseESIForCharacterPositions || string.IsNullOrEmpty(Location))
                {
                    await UpdatePositionFromESI().ConfigureAwait(false);
                }

                // update onliune and fleet status every other tick
                if(m_updateTick)
                {
                    await UpdateOnlineStatus().ConfigureAwait(false);
                    await UpdateFleetInfo().ConfigureAwait(false);
                }

                m_updateTick = !m_updateTick;


                if (routeNeedsUpdate)
                {
                    routeNeedsUpdate = false;
                    UpdateActiveRoute();

                    if (RouteUpdatedEvent != null)
                    {
                        RouteUpdatedEvent();
                    }
                }

                if (warningSystemsNeedsUpdate)
                {
                    warningSystemsNeedsUpdate = false;
                    UpdateWarningSystems();
                }
            }
            UpdateLock.Release();
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        /// <summary>
        /// Refresh the ESI access token
        /// </summary>
        public async Task RefreshAccessToken()
        {
            if (String.IsNullOrEmpty(ESIRefreshToken) || !ESILinked)
            {
                return;
            }

            try
            {
                AccessTokenDetails tokenDetails = await EveManager.Instance.Sso.GetNewPKCEAccessAndRefreshTokenAsync(ESIRefreshToken);
                if (tokenDetails == null || string.IsNullOrEmpty(tokenDetails.AccessToken))
                {
                    ssoErrorCount++;
                    Thread.Sleep(20000);
                    if (ssoErrorCount > 50)
                    {
                        ESIRefreshToken = "";
                        ESILinked = false;
                    }
                    return;
                }

                CharacterDetails characterDetails = await EveManager.Instance.Sso.GetCharacterDetailsAsync(tokenDetails.AccessToken);
                if (characterDetails == null)
                {
                    return;
                }

                ESIAccessToken = tokenDetails.AccessToken;
                ESIAccessTokenExpiry = tokenDetails.ExpiresUtc.ToLocalTime();
                ESIRefreshToken = tokenDetails.RefreshToken ?? string.Empty;
                ESILinked = true;
                ESIScopesStored = characterDetails.Scopes != null ? string.Join(" ", characterDetails.Scopes) : string.Empty;
            }
            catch (Exception ex)
            {
                // expired token
                if (ex.HResult == -2147024809)
                {
                    ESIRefreshToken = "";
                    ESILinked = false;
                }
            }
        }

        /// <summary>
        /// Update the active route for the character
        /// </summary>
        private async void UpdateActiveRoute()
        {
            if (esiSendRouteClear)
            {
                esiSendRouteClear = false;
                esiRouteNeedsUpdate = false;

                System s = EveManager.Instance.GetEveSystem(Location);
                if (s != null)
                {
                    AuthDTO auth = GetAuthDTO();
                    if (auth != null)
                    {
                        try
                        {
                            await EveManager.Instance.EveApiClient.UserInterface.SetAutopilotWaypointAsync(auth, true, true, s.ID);
                        }
                        catch { }
                    }
                }
                return;
            }

            if (Waypoints.Count == 0)
            {
                return;
            }

            {
                // new routing
                string start = string.Empty;
                string end = Location;

                // grab the simple list of thera connections
                List<string> currentActiveTheraConnections = new List<string>();
                foreach (TheraConnection tc in EveManager.Instance.TheraConnections.ToList())
                {
                    currentActiveTheraConnections.Add(tc.System);
                }
                Navigation.UpdateTheraConnections(currentActiveTheraConnections);

                // grab the simple list of turnur connections
                List<string> currentActiveTurnurConnections = new List<string>();
                foreach (TurnurConnection tc in EveManager.Instance.TurnurConnections.ToList())
                {
                    currentActiveTurnurConnections.Add(tc.System);
                }
                Navigation.UpdateTurnurConnections(currentActiveTurnurConnections);

                lock (ActiveRouteLock)
                {
                    if (Location == Waypoints[0])
                    {
                        Waypoints.RemoveAt(0);
                    }
                }

                ActiveRoute.Clear();

                // loop through all the waypoints
                for (int i = 0; i < Waypoints.Count; i++)
                {
                    start = end;
                    end = Waypoints[i];

                    List<Navigation.RoutePoint> sysList = Navigation.Navigate(start, end, UseAnsiblexGates, UseTheraRouting, UseZarzakhRouting, UseTurnurRouting, NavigationMode);

                    if (sysList != null)
                    {
                        lock (ActiveRouteLock)
                        {
                            foreach (Navigation.RoutePoint s in sysList)
                            {
                                ActiveRoute.Add(s);
                            }
                        }
                    }
                }

                ActiveRouteLength = ActiveRoute.Count;
            }

            if (esiRouteNeedsUpdate && !esiRouteUpdating)
            {
                esiRouteNeedsUpdate = false;
                esiRouteUpdating = true;

                List<long> WayPointsToAdd = new List<long>();

                lock (ActiveRouteLock)
                {
                    foreach (Navigation.RoutePoint rp in ActiveRoute)
                    {
                        // explicitly add interim waypoints for ansiblex gates or actual waypoints
                        if (
                                rp.GateToTake == Navigation.GateType.Ansiblex ||
                                rp.GateToTake == Navigation.GateType.Thera ||
                                rp.GateToTake == Navigation.GateType.Turnur ||
                                rp.GateToTake == Navigation.GateType.Zarzakh ||
                                Waypoints.Contains(rp.SystemName)
                            )
                        {
                            long wayPointSysID = EveManager.Instance.GetEveSystem(rp.SystemName).ID;

                            if (rp.GateToTake == Navigation.GateType.Ansiblex)
                            {
                                foreach (JumpBridge jb in EveManager.Instance.JumpBridges)
                                {
                                    if (jb.From == rp.SystemName)
                                    {
                                        if (jb.FromID != 0)
                                        {
                                            wayPointSysID = jb.FromID;
                                        }
                                        break;
                                    }

                                    if (jb.To == rp.SystemName)
                                    {
                                        if (jb.ToID != 0)
                                        {
                                            wayPointSysID = jb.ToID;
                                        }
                                        break;
                                    }
                                }
                            }
                            WayPointsToAdd.Add(wayPointSysID);
                        }
                    }
                }

                bool firstRoute = true;

                AuthDTO auth = GetAuthDTO();
                if (auth != null)
                {
                    foreach (long SysID in WayPointsToAdd)
                    {
                        try
                        {
                            await EveManager.Instance.EveApiClient.UserInterface.SetAutopilotWaypointAsync(auth, firstRoute, false, SysID);
                        }
                        catch { }
                        firstRoute = false;
                        Thread.Sleep(200);
                    }
                }

                esiRouteUpdating = false;
            }
        }

        /// <summary>
        /// Update the characters FleetInfo
        /// </summary>
        private async Task UpdateFleetInfo()
        {
            AuthDTO auth = GetAuthDTO();
            if (auth == null || ID == 0 || !ESILinked)
            {
                return;
            }

            try
            {
                bool sendFleetUpdatedEvent = false;

                if (FleetInfo.NextFleetMembershipCheck < DateTime.Now)
                {
                    FleetInfo.NextFleetMembershipCheck = DateTime.Now + TimeSpan.FromSeconds(240);

                    var esr = await EveManager.Instance.EveApiClient.Fleets.GetCharacterFleetInfoAsync(auth);
                    if (ESIHelpers.ValidateESICall(esr) && esr.Model != null)
                    {
                        FleetInfo.FleetID = esr.Model.FleetId;
                        FleetInfo.IsFleetBoss = esr.Model.Role == "fleet_commander";
                    }
                    else
                    {
                        FleetInfo.FleetID = 0;
                        FleetInfo.Members.Clear();
                        sendFleetUpdatedEvent = true;
                    }
                }

                if (FleetInfo.FleetID != 0 && FleetInfo.IsFleetBoss)
                {
                    List<int> characterIDsToResolve = new List<int>();

                    var esrf = await EveManager.Instance.EveApiClient.Fleets.GetFleetMembersAsync(auth, FleetInfo.FleetID);
                    if (ESIHelpers.ValidateESICall(esrf) && esrf.Model != null)
                    {
                        foreach (Fleet.FleetMember ff in FleetInfo.Members)
                        {
                            ff.IsValid = false;
                        }

                        foreach (FleetMember esifm in esrf.Model)
                        {
                            Fleet.FleetMember fm = null;

                            foreach (Fleet.FleetMember ff in FleetInfo.Members)
                            {
                                if (ff.CharacterID == esifm.CharacterId)
                                {
                                    fm = ff;
                                    fm.IsValid = true;
                                }
                            }

                            if (fm == null)
                            {
                                fm = new Fleet.FleetMember();
                                fm.IsValid = true;
                                FleetInfo.Members.Add(fm);
                                sendFleetUpdatedEvent = true;
                            }

                            EVEData.System es = EveManager.Instance.GetEveSystemFromID(esifm.SolarSystemId);

                            fm.Name = EveManager.Instance.GetCharacterName((int)esifm.CharacterId);

                            fm.CharacterID = (int)esifm.CharacterId;

                            if (es == null)
                            {
                                fm.Location = "";
                                fm.Region = "";
                            }
                            else
                            {
                                fm.Location = es.Name;
                                fm.Region = es.Region;
                            }
                            if (EveManager.Instance.ShipTypes.ContainsKey(esifm.ShipTypeId.ToString()))
                            {
                                fm.ShipType = EveManager.Instance.ShipTypes[esifm.ShipTypeId.ToString()];
                            }
                            else
                            {
                                fm.ShipType = "Unknown : " + esifm.ShipTypeId.ToString();
                            }

                            if (String.IsNullOrEmpty(fm.Name))
                            {
                                characterIDsToResolve.Add((int)esifm.CharacterId);
                            }
                        }

                        if (characterIDsToResolve.Count > 0)
                        {
                            EveManager.Instance.ResolveCharacterIDs(characterIDsToResolve).Wait();
                        }

                        foreach (Fleet.FleetMember ff in FleetInfo.Members.ToList())
                        {
                            if (!ff.IsValid)
                            {
                                FleetInfo.Members.Remove(ff);
                                sendFleetUpdatedEvent = true;
                            }
                        }
                    }
                    else
                    {
                        // something went wrong (probably lost fleet_commander), reset this check
                        FleetInfo.NextFleetMembershipCheck = DateTime.Now + TimeSpan.FromSeconds(60);
                        FleetInfo.FleetID = 0;

                        FleetInfo.Members.Clear();
                        sendFleetUpdatedEvent = true;
                    }
                }

                if (sendFleetUpdatedEvent)
                {
                    if (FleetUpdatedEvent != null)
                    {
                        FleetUpdatedEvent(this);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Update the character info from the ESI data if linked
        /// </summary>
        public async Task UpdateInfoFromESI()
        {
            AuthDTO auth = GetAuthDTO();
            if (auth == null || ID == 0 || !ESILinked)
            {
                if (ESILinked)
                {
                    ESIAccessTokenExpiry = DateTime.Now;
                }
                return;
            }

            List<int> AllianceToResolve = new List<int>();

            try
            {
                var esr = await EveManager.Instance.EveApiClient.Character.GetCharacterPublicInfoAsync(ID);
                if (ESIHelpers.ValidateESICall(esr) && esr.Model != null)
                {
                    CorporationID = (int)esr.Model.CorporationId;
                    AllianceID = esr.Model.AllianceId ?? 0;
                }

                if (Standings.Count == 0)
                {
                    if (AllianceID != 0)
                    {
                        int page = 1;
                        int maxPageCount = 1;
                        do
                        {
                            var esrAlliance = await EveManager.Instance.EveApiClient.Contacts.GetAllianceContactsAsync(auth, AllianceID, page);
                            if (ESIHelpers.ValidateESICall(esrAlliance) && esrAlliance.Model != null)
                            {
                                maxPageCount = esrAlliance.MaxPages > 0 ? esrAlliance.MaxPages : 1;
                                foreach (AllianceContact con in esrAlliance.Model)
                                {
                                    Standings[con.ContactId] = (float)con.Standing;
                                    if (con.ContactType == "alliance")
                                    {
                                        AllianceToResolve.Add((int)con.ContactId);
                                    }
                                }
                            }
                            page++;
                        }
                        while (page <= maxPageCount);
                    }

                    if (CorporationID != 0)
                    {
                        int page = 1;
                        int maxPageCount = 1;
                        do
                        {
                            var esrCorp = await EveManager.Instance.EveApiClient.Contacts.GetCorporationContactsAsync(auth, CorporationID, page);
                            if (ESIHelpers.ValidateESICall(esrCorp) && esrCorp.Model != null)
                            {
                                maxPageCount = esrCorp.MaxPages > 0 ? esrCorp.MaxPages : 1;
                                foreach (CorporationContact con in esrCorp.Model)
                                {
                                    Standings[con.ContactId] = (float)con.Standing;
                                    if (con.ContactType == "alliance")
                                    {
                                        AllianceToResolve.Add((int)con.ContactId);
                                    }
                                }
                            }
                            page++;
                        }
                        while (page <= maxPageCount);
                    }

                }

                string portraitRoot = Path.Combine(EveManager.Instance.SaveDataRootFolder, "Portraits");
                string characterPortrait = Path.Combine(portraitRoot, ID.ToString());
                if (!File.Exists(characterPortrait))
                {
                    var esri = await EveManager.Instance.EveApiClient.Character.GetCharacterPortraitsAsync(ID);
                    if (ESIHelpers.ValidateESICall(esri) && esri.Model != null && !string.IsNullOrEmpty(esri.Model.Px128x128))
                    {
                        try
                        {
                            HttpClient hc = new HttpClient();
                            var response = await hc.GetAsync(esri.Model.Px128x128);
                            using (var fs = new FileStream(characterPortrait, FileMode.CreateNew))
                            {
                                await response.Content.CopyToAsync(fs);
                            }
                        }
                        catch { }
                    }
                }

                if (File.Exists(characterPortrait))
                {
                    PortraitLocation = new Uri(characterPortrait);
                }

                if (CorporationID != -1)
                {
                    var esrc = await EveManager.Instance.EveApiClient.Corporation.GetCorporationInfoAsync(CorporationID);
                    if (ESIHelpers.ValidateESICall(esrc) && esrc.Model != null)
                    {
                        CorporationName = esrc.Model.Name;
                        CorporationTicker = esrc.Model.Ticker;
                    }
                }

                if (AllianceID > 0)
                {
                    var esra = await EveManager.Instance.EveApiClient.Alliance.GetAllianceInfoAsync(AllianceID);
                    if (ESIHelpers.ValidateESICall(esra) && esra.Model != null)
                    {
                        AllianceName = esra.Model.Name;
                        AllianceTicker = esra.Model.Ticker;
                    }
                }
                else
                {
                    AllianceName = null;
                    AllianceTicker = null;
                }

                EdenCommStandingsGood = false;
                TrigStandingsGood = false;
                var essl = await EveManager.Instance.EveApiClient.Character.GetStandingsAsync(auth);
                if (ESIHelpers.ValidateESICall(essl) && essl.Model != null)
                {
                    foreach (Standing standing in essl.Model)
                    {
                        if (standing.FromId == 500027 && standing.StandingValue > 0)
                        {
                            EdenCommStandingsGood = true;
                        }
                        if (standing.FromId == 500026 && standing.StandingValue > 0)
                        {
                            TrigStandingsGood = true;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            await EveManager.Instance.ResolveAllianceIDs(AllianceToResolve);
        }

        /// <summary>
        /// Update the characters logged on status from ESI
        /// </summary>
        private async Task UpdateOnlineStatus()
        {
            AuthDTO auth = GetAuthDTO();
            if (auth == null || ID == 0 || !ESILinked)
            {
                return;
            }

            try
            {
                var esr = await EveManager.Instance.EveApiClient.Location.GetCharacterOnlineAsync(auth);
                if (ESIHelpers.ValidateESICall(esr) && esr.Model != null)
                {
                    IsOnline = esr.Model.Online;
                }
            }
            catch { }
        }

        /// <summary>
        /// Update the characters position from ESI (will override the position read from any log files
        /// </summary>
        public async Task UpdatePositionFromESI()
        {
            AuthDTO auth = GetAuthDTO();
            if (auth == null || ID == 0 || !ESILinked)
            {
                return;
            }

            try
            {
                var esr = await EveManager.Instance.EveApiClient.Location.GetCharacterLocationAsync(auth);
                if (ESIHelpers.ValidateESICall(esr) && esr.Model != null)
                {
                    if (!EveManager.Instance.SystemIDToName.ContainsKey(esr.Model.SolarSystemId))
                    {
                        Location = "";
                        Region = "";
                        return;
                    }
                    Location = EveManager.Instance.SystemIDToName[esr.Model.SolarSystemId];
                    System s = EVEData.EveManager.Instance.GetEveSystem(Location);
                    if (s != null)
                    {
                        Region = s.Region;
                    }
                    else
                    {
                        Region = "";
                    }
                }
            }
            catch { }
        }

        private void UpdateWarningSystems()
        {
            // only track warning systems if the character is logged in
            if (IsOnline)
            {
                if (!string.IsNullOrEmpty(Location) && DangerZoneRange > 0 && DangerZoneActive)
                {
                    WarningSystems = Navigation.GetSystemsXJumpsFrom(new List<string>(), Location, DangerZoneRange);
                }
            }
            else
            {
                if (WarningSystems != null)
                {
                    WarningSystems.Clear();
                }
            }

        }
        /// Fetches character assets from ESI and builds AssetsBySystem.
        /// <summary>
        /// Each entry includes type name, quantity, location flag, character name,
        /// and a resolved LocationPath for nested items (player structures, ships, containers).
        /// Cached for 1 hour to match ESI TTL.
        /// </summary>
        public async Task UpdateAssetsFromESI()
        {
            if (m_nextAssetUpdate > DateTime.Now)
                return;

            AuthDTO auth = GetAuthDTO();
            if (auth == null || ID == 0 || !ESILinked)
                return;

            if (string.IsNullOrEmpty(ESIScopesStored) || !ESIScopesStored.Contains("esi-assets.read_assets.v1"))
                return;

            try
            {
                // Phase 1: Fetch ALL pages and build item map
                var allAssets = new List<EVEStandard.Models.Asset>();
                var itemMap   = new Dictionary<long, EVEStandard.Models.Asset>();
                int page = 1, maxPage = 1;
                do
                {
                    var esr = await EveManager.Instance.EveApiClient.Assets.GetCharacterAssetsAsync(auth, page);
                    if (!ESIHelpers.ValidateESICall(esr) || esr.Model == null)
                        break;
                    maxPage = esr.MaxPages > 0 ? esr.MaxPages : 1;
                    foreach (var a in esr.Model)
                    {
                        allAssets.Add(a);
                        itemMap[a.ItemId] = a;
                    }
                    page++;
                }
                while (page <= maxPage);

                // Phase 1b: Resolve type names for any TypeIds not already in ItemTypes cache
                // (invTypes.csv is only used at DataGen time; at runtime we call /universe/names/)
                {
                    var unknownTypeIds = allAssets
                        .Select(a => a.TypeId)
                        .Distinct()
                        .Where(id => !EveManager.Instance.ItemTypes.ContainsKey(id))
                        .ToList();

                    const int typeNameBatch = 1000;
                    for (int b = 0; b < unknownTypeIds.Count; b += typeNameBatch)
                    {
                        var batch = unknownTypeIds.Skip(b).Take(typeNameBatch).ToList();
                        try
                        {
                            var nr = await EveManager.Instance.EveApiClient.Universe
                                         .GetNamesAndCategoriesFromIdsAsync(batch);
                            if (ESIHelpers.ValidateESICall(nr) && nr.Model != null)
                            {
                                foreach (var entry in nr.Model)
                                {
                                    if (entry.Category == "inventory_type")
                                        EveManager.Instance.ItemTypes[entry.Id] = entry.Name;
                                }
                            }
                        }
                        catch { }
                    }
                }

                // Phase 1c: Batch-resolve NPC station names via /universe/names/
                var stationNameMap = new Dictionary<long, string>();
                {
                    var stationIds = allAssets
                        .Where(a => a.LocationType == "station")
                        .Select(a => a.LocationId)
                        .Distinct()
                        .ToList();
                    if (stationIds.Count > 0)
                    {
                        const int stBatch = 1000;
                        for (int b = 0; b < stationIds.Count; b += stBatch)
                        {
                            var batch = stationIds.Skip(b).Take(stBatch).ToList();
                            try
                            {
                                var nr = await EveManager.Instance.EveApiClient.Universe
                                             .GetNamesAndCategoriesFromIdsAsync(batch);
                                if (ESIHelpers.ValidateESICall(nr) && nr.Model != null)
                                    foreach (var entry in nr.Model)
                                        if (entry.Category == "station")
                                            stationNameMap[entry.Id] = entry.Name;
                            }
                            catch { }
                        }
                    }
                }

                // Phase 1d: If language is not English, fetch localized names
                string esiLang = EveManager.GetESILanguageCode();
                if (esiLang != "en-us")
                {
                    var allTypeIds = allAssets.Select(a => a.TypeId).Distinct().ToList();
                    await EveManager.Instance.FetchTypeNamesWithLanguageAsync(allTypeIds, esiLang);
                }

                // Phase 2: Batch-resolve container names (ships, citadels, structures with custom names)
                // NOTE: GetCharacterAssetNamesAsync only returns items that have customisable names
                // (ships, containers, Upwell structures). Regular items are NOT returned — their
                // type names come from ItemTypes (populated in Phase 1b above).
                var itemNameMap = new Dictionary<long, string>();
                var itemIdsForNames = new HashSet<long>();
                foreach (var a in allAssets)
                {
                    // Only the direct parent containers need name resolution
                    if (a.LocationType == "item")
                        itemIdsForNames.Add(a.LocationId);
                }
                const int batchSize = 1000;
                var idList = itemIdsForNames.ToList();
                for (int b = 0; b < idList.Count; b += batchSize)
                {
                    var batch = idList.Skip(b).Take(batchSize).ToList();
                    try
                    {
                        var nameResult = await EveManager.Instance.EveApiClient.Assets.GetCharacterAssetNamesAsync(auth, batch);
                        if (ESIHelpers.ValidateESICall(nameResult) && nameResult.Model != null)
                            foreach (var n in nameResult.Model)
                                itemNameMap[n.ItemId] = n.Name;
                    }
                    catch { }
                }

                // Phase 3: Recursive resolver => (solarSystemId, locationPath)
                bool canReadStructures = !string.IsNullOrEmpty(ESIScopesStored)
                    && ESIScopesStored.Contains("esi-universe.read_structures.v1");
                var structureSystemCache = new Dictionary<long, (long sysId, string name)>();

                async Task<(long sysId, List<string> path)> ResolveAsset(
                    EVEStandard.Models.Asset asset, int depth)
                {
                    if (depth > 10)
                        return (0, new List<string>());
                    if (asset.LocationType == "solar_system")
                        return (asset.LocationId, new List<string>());
                    if (asset.LocationType == "station")
                    {
                        long sysId = 0;
                        if (EveManager.Instance.StationIDToSystemID.ContainsKey(asset.LocationId))
                            sysId = EveManager.Instance.StationIDToSystemID[asset.LocationId];
                        else
                        {
                            var sys = EveManager.Instance.GetEveSystemFromID(asset.LocationId);
                            if (sys != null) sysId = sys.ID;
                        }
                        return (sysId, stationNameMap.TryGetValue(asset.LocationId, out var sn)
                            ? new List<string> { sn }
                            : new List<string> { $"Station [{asset.LocationId}]" });
                    }
                    if (asset.LocationType == "item")
                    {
                        long locId = asset.LocationId;

                        // Resolve container display name: prefer TypeName, append custom name if present
                        string containerName;
                        string customName = itemNameMap.ContainsKey(locId) ? itemNameMap[locId] : null;
                        if (itemMap.ContainsKey(locId))
                        {
                            long containerTypeId = itemMap[locId].TypeId;
                            string typeName = EveManager.Instance.GetItemTypeName(containerTypeId)
                                ?? string.Format("Unknown [{0}]", containerTypeId);
                            containerName = customName != null ? $"{typeName} \"{customName}\"" : typeName;
                        }
                        else
                        {
                            containerName = customName ?? string.Format("Container [{0}]", locId);
                        }

                        if (itemMap.ContainsKey(locId))
                        {
                            var (parentSysId, parentPath) = await ResolveAsset(itemMap[locId], depth + 1);
                            var path = new List<string>(parentPath) { containerName };
                            return (parentSysId, path);
                        }
                        else if (locId > 1000000000000L)
                        {
                            if (structureSystemCache.ContainsKey(locId))
                            {
                                var cached = structureSystemCache[locId];
                                return (cached.sysId, new List<string> { cached.name });
                            }
                            if (canReadStructures)
                            {
                                try
                                {
                                    var structResult = await EveManager.Instance.EveApiClient.Universe
                                        .GetStructureInfoAsync(auth, locId);
                                    if (ESIHelpers.ValidateESICall(structResult) && structResult.Model != null)
                                    {
                                        string structName = structResult.Model.Name
                                            ?? string.Format("Structure [{0}]", locId);
                                        long structSysId = structResult.Model.SolarSystemId;
                                        structureSystemCache[locId] = (structSysId, structName);
                                        return (structSysId, new List<string> { structName });
                                    }
                                }
                                catch { }
                            }
                            string fallbackName = itemNameMap.ContainsKey(locId)
                                ? itemNameMap[locId]
                                : string.Format("Structure [{0}]", locId);
                            return (0, new List<string> { fallbackName });
                        }
                    }
                    return (0, new List<string>());
                }

                // Phase 4: Build AssetsBySystem with LocationPath
                // Pre-compute which item IDs act as containers (other items list them as parent)
                var containerItemIds = new HashSet<long>(
                    allAssets
                        .Where(a => a.LocationType == "item" && itemMap.ContainsKey(a.LocationId))
                        .Select(a => a.LocationId));

                var newAssetsBySystem = new Dictionary<long, List<AssetEntry>>();
                foreach (var asset in allAssets)
                {
                    try
                    {
                        var (solarSystemId, locationPath) = await ResolveAsset(asset, 0);
                        if (solarSystemId <= 0)
                            continue;
                        string typeName = EveManager.Instance.GetItemTypeName(asset.TypeId)
                            ?? string.Format("Unknown ({0})", asset.TypeId);
                        var entry = new AssetEntry
                        {
                            TypeId        = asset.TypeId,
                            TypeName      = typeName,
                            Quantity      = asset.Quantity,
                            LocationFlag  = asset.LocationFlag,
                            CharacterName = Name,
                            LocationPath  = locationPath,
                            ItemId        = asset.ItemId,
                            IsContainer   = containerItemIds.Contains(asset.ItemId),
                            ParentItemId  = asset.LocationType == "item" && itemMap.ContainsKey(asset.LocationId) ? asset.LocationId : 0,
                        };
                        if (!newAssetsBySystem.ContainsKey(solarSystemId))
                            newAssetsBySystem[solarSystemId] = new List<AssetEntry>();
                        newAssetsBySystem[solarSystemId].Add(entry);
                    }
                    catch { }
                }
                AssetsBySystem = newAssetsBySystem;
                SaveAssetsToCache();
                m_nextAssetUpdate = DateTime.Now.AddSeconds(3600);
            }
            catch { }
        }

        private string GetAssetCacheFilePath()
        {
            string safeName = string.Concat(Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            return Path.Combine(EVEData.EveManager.Instance.SaveDataRootFolder, $"Assets_{safeName}.json");
        }

        public void SaveAssetsToCache()
        {
            try
            {
                var wrapper = new AssetCacheWrapper
                {
                    CharacterName  = Name,
                    Language       = EveManager.CurrentLanguage,
                    UpdatedAt      = DateTime.UtcNow,
                    AssetsBySystem = AssetsBySystem,
                };
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(wrapper, Newtonsoft.Json.Formatting.None);
                File.WriteAllText(GetAssetCacheFilePath(), json, Encoding.UTF8);
            }
            catch { }
        }

        public void LoadAssetsFromCache()
        {
            try
            {
                string path = GetAssetCacheFilePath();
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path, Encoding.UTF8);
                var wrapper = Newtonsoft.Json.JsonConvert.DeserializeObject<AssetCacheWrapper>(json);
                if (wrapper == null) return;
                // Invalidate if schema version changed or language changed
                if (wrapper.Version != 2)
                    return;
                if (!string.Equals(wrapper.Language, EveManager.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
                    return;
                AssetsBySystem = wrapper.AssetsBySystem ?? new Dictionary<long, List<AssetEntry>>();
            }
            catch { }
        }
    }
}
