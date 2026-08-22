using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Security.Policy;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SMT.EVEData;
using SMT.Helpers;
using SMT.ResourceUsage;

namespace SMT
{
    /// <summary>
    /// Interaction logic for RegionControl.xaml
    /// </summary>
    public partial class RegionControl : UserControl, INotifyPropertyChanged
    {
        private static SolidColorBrush FrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static readonly RoutedEvent UniverseSystemSelectEvent = EventManager.RegisterRoutedEvent("UniverseSystemSelect", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UniverseControl));
        private const int SYSTEM_LINK_INDEX = 19;
        private const double SYSTEM_REGION_TEXT_WIDTH = 100;
        private const double SYSTEM_REGION_TEXT_X_OFFSET = -SYSTEM_REGION_TEXT_WIDTH / 2;
        private const double SYSTEM_REGION_TEXT_Y_OFFSET = SYSTEM_TEXT_Y_OFFSET + SYSTEM_TEXT_TEXT_SIZE + 3;
        private const double SYSTEM_SHAPE_OFFSET = SYSTEM_SHAPE_SIZE / 2;
        private const double SYSTEM_SHAPE_SIZE = 18;
        private const double SYSTEM_TEXT_TEXT_SIZE = 6;
        private const double SYSTEM_SHAPE_OOR_SIZE = 14;
        private const double SYSTEM_SHAPE_OOR_OFFSET = SYSTEM_SHAPE_OOR_SIZE / 2;

        private const int SYSTEM_TEXT_WIDTH = 100;
        private const int SYSTEM_TEXT_HEIGHT = 50;
        private const double SYSTEM_TEXT_X_OFFSET = SYSTEM_TEXT_WIDTH / 2;
        private const double SYSTEM_TEXT_Y_OFFSET = SYSTEM_TEXT_HEIGHT / 2;

        // depth order of data
        private const int ZINDEX_CHARACTERS = 140;

        private const int ZINDEX_POI = 113;
        private const int ZINDEX_SOV_FIGHT_LOGO = 105;
        private const int ZINDEX_CYNOBEACON = 105;
        private const int ZINDEX_TEXT = 101;
        private const int ZINDEX_SYSTEM = 100;
        private const int ZINDEX_SYSTEM_OUTLINE = 99;
        private const int ZINDEX_SOV_FIGHT_SHAPE = 97;
        private const int ZINDEX_THERA = 97;
        private const int ZINDEX_TURNER = 97;
        private const int ZINDEX_STORM = 95;
        private const int ZINDEX_TRIG = 97;
        private const int ZINDEX_RANGEMARKER = 96;
        private const int ZINDEX_SYSICON = 100;
        private const int ZINDEX_ADM = 99;
        private const int ZINDEX_POLY = 98;
        private const double STANDING_REGION_STROKE_THICKNESS = 2.25;
        private const int ZINDEX_JOVE = 105;

        private const int THERA_Z_INDEX = 22;

        private readonly Brush SelectedAllianceBrush = FrozenBrush(Color.FromArgb(180, 200, 200, 200));
        private Dictionary<string, EVEData.EveManager.JumpShip> activeJumpSpheres;
        private string currentCharacterJumpSystem;
        private string currentJumpCharacter;

        // Store the Dynamic Map elements so they can seperately be cleared
        private List<System.Windows.UIElement> DynamicMapElements;

        private List<System.Windows.UIElement> DynamicMapElementsSysLinkHighlight;
        private List<System.Windows.UIElement> DynamicMapElementsCharacters;
        private List<System.Windows.UIElement> DynamicMapElementsJBHighlight;
        private List<System.Windows.UIElement> DynamicMapElementsRangeMarkers;
        private List<System.Windows.UIElement> DynamicMapElementsRouteHighlight;
        private System.Windows.Media.Imaging.BitmapImage edencomLogoImage;
        private System.Windows.Media.Imaging.BitmapImage fightImage;
        private System.Windows.Media.Imaging.BitmapImage joveLogoImage;
        private System.Windows.Media.Imaging.BitmapImage stormImageBase;
        private System.Windows.Media.Imaging.BitmapImage stormImageEM;
        private System.Windows.Media.Imaging.BitmapImage stormImageExp;
        private System.Windows.Media.Imaging.BitmapImage stormImageKin;
        private System.Windows.Media.Imaging.BitmapImage stormImageTherm;

        private EVEData.EveManager.JumpShip jumpShipType;
        private LocalCharacter m_ActiveCharacter;

        // Map Controls
        private double m_ESIOverlayScale = 1.0f;

        private bool m_ShowJumpBridges = true;
        private bool m_ShowNPCKills;
        private bool m_ShowPodKills;
        private bool m_ShowShipJumps;
        private bool m_ShowShipKills;
        private bool m_ShowSovOwner;
        private bool m_ShowStandings;
        private bool m_ShowSystemADM;
        private bool m_ShowSystemSecurity;
        private bool m_ShowSystemTimers;
        private Dictionary<string, List<KeyValuePair<int, string>>> NameTrackingLocationMap = new Dictionary<string, List<KeyValuePair<int, string>>>();
        private long SelectedAlliance;
        private bool showJumpDistance;
        private Brush StandingBadBrush = FrozenBrush(Color.FromArgb(110, 196, 72, 6));
        private Brush StandingGoodBrush = FrozenBrush(Color.FromArgb(110, 43, 101, 196));
        private Brush StandingNeutBrush = FrozenBrush(Color.FromArgb(110, 140, 140, 140));

        // Constant Colours
        private Brush StandingVBadBrush = FrozenBrush(Color.FromArgb(110, 148, 5, 5));
        private Brush StandingVGoodBrush = FrozenBrush(Color.FromArgb(110, 5, 34, 120));

        /// <summary>Standing tier colours for map tickers: same semantic tiers as the kill feed, higher luminance for dark map backgrounds.</summary>
        private static readonly SolidColorBrush TickerStandingTerribleBrush;
        private static readonly SolidColorBrush TickerStandingBadBrush;
        private static readonly SolidColorBrush TickerStandingGoodBrush;
        private static readonly SolidColorBrush TickerStandingExcellentBrush;

        static RegionControl()
        {
            TickerStandingTerribleBrush = new SolidColorBrush(Color.FromRgb(255, 95, 95));
            TickerStandingBadBrush = new SolidColorBrush(Color.FromRgb(255, 184, 77));
            TickerStandingGoodBrush = new SolidColorBrush(Color.FromRgb(110, 220, 255));
            TickerStandingExcellentBrush = new SolidColorBrush(Color.FromRgb(140, 180, 255));
            TickerStandingTerribleBrush.Freeze();
            TickerStandingBadBrush.Freeze();
            TickerStandingGoodBrush.Freeze();
            TickerStandingExcellentBrush.Freeze();
        }

        private List<Point> SystemIcon_Astrahaus = new List<Point>
        {
            new Point(6,12),
            new Point(6,7),
            new Point(9,7),
            new Point(9,4),
            //new Point(10,4),
            new Point(9,7),
            new Point(12,7),
            new Point(12,12),
        };

        private List<Point> SystemIcon_Fortizar = new List<Point>
        {
            new Point(4,12),
            new Point(4,7),
            new Point(6,7),
            new Point(6,5),
            new Point(12,5),
            new Point(12,7),
            new Point(14,7),
            new Point(14,12),
        };

        private List<Point> SystemIcon_Keepstar = new List<Point>
        {
            new Point(1,17),
            new Point(1,0),
            new Point(7,0),
            new Point(7,7),
            new Point(12,7),
            new Point(12,0),
            new Point(18,0),
            new Point(18,17),
        };

        private System.Windows.Media.Imaging.BitmapImage trigLogoImage;

        // Timer to Re-draw the map
        private System.Windows.Threading.DispatcherTimer uiRefreshTimer;

        // events

        /// <summary>
        /// Intel Updated Event Handler
        /// </summary>
        public delegate void SystemHover(string system);

        /// <summary>
        /// Intel Updated Event
        /// </summary>
        public event SystemHover SystemHoverEvent;

        /// <summary>
        /// Constructor
        /// </summary>
        public RegionControl()
        {
            InitializeComponent();
            DataContext = this;

            activeJumpSpheres = new Dictionary<string, EVEData.EveManager.JumpShip>();

            joveLogoImage = ResourceLoader.LoadBitmapFromResource("Images/Jove_logo.png");
            trigLogoImage = ResourceLoader.LoadBitmapFromResource("Images/TrigTile.png");
            edencomLogoImage = ResourceLoader.LoadBitmapFromResource("Images/edencom.png");
            fightImage = ResourceLoader.LoadBitmapFromResource("Images/fight.png");
            stormImageBase = ResourceLoader.LoadBitmapFromResource("Images/cloud_unknown.png");
            stormImageEM = ResourceLoader.LoadBitmapFromResource("Images/cloud_em.png");
            stormImageExp = ResourceLoader.LoadBitmapFromResource("Images/cloud_explosive.png");
            stormImageKin = ResourceLoader.LoadBitmapFromResource("Images/cloud_kinetic.png");
            stormImageTherm = ResourceLoader.LoadBitmapFromResource("Images/cloud_thermal.png");

            helpIcon.MouseLeftButtonDown += HelpIcon_MouseLeftButtonDown;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public event PropertyChangedEventHandler RegionChanged;

        public event RoutedEventHandler UniverseSystemSelect
        {
            add { AddHandler(UniverseSystemSelectEvent, value); }
            remove { RemoveHandler(UniverseSystemSelectEvent, value); }
        }

        public LocalCharacter ActiveCharacter
        {
            get
            {
                return m_ActiveCharacter;
            }
            set
            {
                m_ActiveCharacter = value;
                OnPropertyChanged("ActiveCharacter");
            }
        }

        public AnomManager ANOMManager { get; set; }

        public EveManager EM { get; set; }

        public double ESIOverlayScale
        {
            get
            {
                return m_ESIOverlayScale;
            }
            set
            {
                m_ESIOverlayScale = value;
                OnPropertyChanged("ESIOverlayScale");
            }
        }

        public bool FollowCharacter
        {
            get
            {
                return FollowCharacterChk.IsChecked.Value;
            }
            set
            {
                FollowCharacterChk.IsChecked = value;
            }
        }

        public MapConfig MapConf { get; set; }

        public EVEData.MapRegion Region { get; set; }

        public string SelectedSystem { get; set; }

        public bool ShowJumpBridges
        {
            get
            {
                return m_ShowJumpBridges;
            }
            set
            {
                m_ShowJumpBridges = value;
                OnPropertyChanged("ShowJumpBridges");
            }
        }

        public bool ShowNPCKills
        {
            get { return m_ShowNPCKills; }
            set { m_ShowNPCKills = value; if(value) ClearOtherStatOverlays(nameof(ShowNPCKills)); OnPropertyChanged(nameof(ShowNPCKills)); }
        }

        public bool ShowPodKills
        {
            get { return m_ShowPodKills; }
            set { m_ShowPodKills = value; if(value) ClearOtherStatOverlays(nameof(ShowPodKills)); OnPropertyChanged(nameof(ShowPodKills)); }
        }

        public bool ShowShipJumps
        {
            get { return m_ShowShipJumps; }
            set { m_ShowShipJumps = value; if(value) ClearOtherStatOverlays(nameof(ShowShipJumps)); OnPropertyChanged(nameof(ShowShipJumps)); }
        }

        public bool ShowShipKills
        {
            get { return m_ShowShipKills; }
            set { m_ShowShipKills = value; if(value) ClearOtherStatOverlays(nameof(ShowShipKills)); OnPropertyChanged(nameof(ShowShipKills)); }
        }

        /// <summary>
        /// Mutual exclusion for stat overlay toggles — when one is turned on, the others are turned off.
        /// </summary>
        private void ClearOtherStatOverlays(string except)
        {
            if(except != nameof(ShowNPCKills))  { m_ShowNPCKills = false;  OnPropertyChanged(nameof(ShowNPCKills)); }
            if(except != nameof(ShowPodKills))  { m_ShowPodKills = false;  OnPropertyChanged(nameof(ShowPodKills)); }
            if(except != nameof(ShowShipJumps)) { m_ShowShipJumps = false; OnPropertyChanged(nameof(ShowShipJumps)); }
            if(except != nameof(ShowShipKills)) { m_ShowShipKills = false; OnPropertyChanged(nameof(ShowShipKills)); }
        }

        public bool ShowSovOwner
        {
            get
            {
                return m_ShowSovOwner;
            }
            set
            {
                m_ShowSovOwner = value;
                OnPropertyChanged("ShowSovOwner");
            }
        }

        public bool ShowStandings
        {
            get
            {
                return m_ShowStandings;
            }
            set
            {
                m_ShowStandings = value;

                OnPropertyChanged("ShowStandings");
            }
        }

        public bool ShowSystemADM
        {
            get
            {
                return m_ShowSystemADM;
            }
            set
            {
                m_ShowSystemADM = value;
                if(m_ShowSystemADM)
                {
                    ShowSystemSecurity = false;
                }
                OnPropertyChanged("ShowSystemADM");
            }
        }

        public bool ShowSystemSecurity
        {
            get
            {
                return m_ShowSystemSecurity;
            }
            set
            {
                m_ShowSystemSecurity = value;
                if(m_ShowSystemSecurity)
                {
                    ShowSystemADM = false;
                }
                OnPropertyChanged("ShowSystemSecurity");
            }
        }

        public bool ShowSystemTimers
        {
            get
            {
                return m_ShowSystemTimers;
            }
            set
            {
                m_ShowSystemTimers = value;
                OnPropertyChanged("ShowSystemTimers");
            }
        }

        public List<InfoItem> InfoLayer { get; set; }

        public void AddSovConflictsToMap()
        {
            if(!ShowSystemTimers)
            {
                return;
            }

            Brush ActiveSovFightBrush = new SolidColorBrush(Colors.DarkRed);
            ActiveSovFightBrush.Freeze();

            foreach(SOVCampaign sc in EM.ActiveSovCampaigns)
            {
                if(Region.IsSystemOnMap(sc.System))
                {
                    MapSystem ms = Region.MapSystems[sc.System];

                    Image SovFightLogo = new Image
                    {
                        Width = 10,
                        Height = 10,
                        Name = "FightLogo",
                        Source = fightImage,
                        Stretch = Stretch.Uniform,
                        IsHitTestVisible = false,
                    };
                    SovFightLogo.IsHitTestVisible = false;

                    Canvas.SetLeft(SovFightLogo, ms.Layout.X - SYSTEM_SHAPE_OFFSET + 5);
                    Canvas.SetTop(SovFightLogo, ms.Layout.Y - SYSTEM_SHAPE_OFFSET + 5);
                    Canvas.SetZIndex(SovFightLogo, ZINDEX_SOV_FIGHT_LOGO);
                    MainCanvas.Children.Add(SovFightLogo);
                    DynamicMapElements.Add(SovFightLogo);

                    if(sc.IsActive || sc.Type == "IHub")
                    {
                        Shape activeSovFightShape = new Ellipse() { Height = SYSTEM_SHAPE_SIZE + 18, Width = SYSTEM_SHAPE_SIZE + 18 };

                        activeSovFightShape.Stroke = ActiveSovFightBrush;
                        activeSovFightShape.StrokeThickness = 9;
                        activeSovFightShape.StrokeLineJoin = PenLineJoin.Round;
                        activeSovFightShape.Fill = ActiveSovFightBrush;

                        Canvas.SetLeft(activeSovFightShape, ms.Layout.X - (SYSTEM_SHAPE_OFFSET + 9));
                        Canvas.SetTop(activeSovFightShape, ms.Layout.Y - (SYSTEM_SHAPE_OFFSET + 9));
                        Canvas.SetZIndex(activeSovFightShape, ZINDEX_SOV_FIGHT_SHAPE);
                        MainCanvas.Children.Add(activeSovFightShape);
                        DynamicMapElements.Add(activeSovFightShape);
                    }
                }
            }
        }

        public void AddWHLinksSystemsToMap()
        {
            Brush TheraWHLinkBrush = new SolidColorBrush(MapConf.ActiveColourScheme.TheraEntranceSystem);
            TheraWHLinkBrush.Freeze();
            Brush TurnurWHLinkBrush = new SolidColorBrush(MapConf.ActiveColourScheme.ThurnurEntranceSystem);
            TurnurWHLinkBrush.Freeze();

            AddWHConnectionOverlays(EM.TheraConnections.ToList().Select(tc => tc.System), TheraWHLinkBrush, ZINDEX_THERA);
            AddWHConnectionOverlays(EM.TurnurConnections.ToList().Select(tc => tc.System), TurnurWHLinkBrush, ZINDEX_TURNER);
        }

        /// <summary>
        /// Renders wormhole connection overlays for the given systems (shared by Thera and Turnur).
        /// </summary>
        private void AddWHConnectionOverlays(IEnumerable<string> systemNames, Brush brush, int zIndex)
        {
            foreach(string systemName in systemNames)
            {
                if(Region.IsSystemOnMap(systemName))
                {
                    MapSystem ms = Region.MapSystems[systemName];

                    Shape shape;
                    if(ms.ActualSystem.HasNPCStation)
                    {
                        shape = new Rectangle() { Height = SYSTEM_SHAPE_SIZE + 6, Width = SYSTEM_SHAPE_SIZE + 6 };
                    }
                    else
                    {
                        shape = new Ellipse() { Height = SYSTEM_SHAPE_SIZE + 6, Width = SYSTEM_SHAPE_SIZE + 6 };
                    }

                    shape.Stroke = brush;
                    shape.StrokeThickness = 1.5;
                    shape.StrokeLineJoin = PenLineJoin.Round;
                    shape.Fill = brush;

                    Canvas.SetLeft(shape, ms.Layout.X - (SYSTEM_SHAPE_OFFSET + 3));
                    Canvas.SetTop(shape, ms.Layout.Y - (SYSTEM_SHAPE_OFFSET + 3));
                    Canvas.SetZIndex(shape, zIndex);
                    MainCanvas.Children.Add(shape);
                }
            }
        }

        public void AddPOIsToMap()
        {
            Brush POIBrush = new SolidColorBrush(Colors.White);
            POIBrush.Freeze();

            foreach(POI p in EM.PointsOfInterest)
            {
                if(Region.IsSystemOnMap(p.System))
                {
                    MapSystem ms = Region.MapSystems[p.System];
                    string POISymbol = "ℹ";

                    Label poiLbl = new Label();
                    poiLbl.FontSize = 9;
                    poiLbl.IsHitTestVisible = false;
                    poiLbl.Content = POISymbol;
                    poiLbl.HorizontalContentAlignment = HorizontalAlignment.Center;
                    poiLbl.VerticalContentAlignment = VerticalAlignment.Center;
                    poiLbl.Width = SYSTEM_SHAPE_SIZE + 6;
                    poiLbl.Height = SYSTEM_SHAPE_SIZE + 6;
                    poiLbl.Foreground = POIBrush;
                    poiLbl.FontWeight = FontWeights.Bold;

                    Canvas.SetLeft(poiLbl, ms.Layout.X - (SYSTEM_SHAPE_OFFSET + 3));
                    Canvas.SetTop(poiLbl, ms.Layout.Y - (SYSTEM_SHAPE_OFFSET + 3));
                    Canvas.SetZIndex(poiLbl, ZINDEX_POI);
                    MainCanvas.Children.Add(poiLbl);
                    DynamicMapElements.Add(poiLbl);
                }
            }
        }

        public void AddStormsToMap()
        {
            foreach(Storm s in EM.MetaliminalStorms)
            {
                if(Region.IsSystemOnMap(s.System))
                {
                    MapSystem ms = Region.MapSystems[s.System];

                    Image stormCloud = new Image
                    {
                        Width = 28,
                        Height = 28,
                        Name = "Storm",
                        Source = stormImageBase,
                        Stretch = Stretch.Uniform,
                        IsHitTestVisible = false,
                    };

                    stormCloud.UseLayoutRounding = true;
                    stormCloud.SnapsToDevicePixels = true;

                    switch(s.Type)
                    {
                        case "Plasma":
                            {
                                stormCloud.Source = stormImageTherm;
                            }
                            break;

                        case "Gamma":
                            {
                                stormCloud.Source = stormImageExp;
                            }
                            break;

                        case "Exotic":
                            {
                                stormCloud.Source = stormImageKin;
                            }
                            break;

                        case "Electric":
                            {
                                stormCloud.Source = stormImageEM;
                            }
                            break;
                    }

                    Canvas.SetLeft(stormCloud, ms.Layout.X - SYSTEM_SHAPE_OFFSET - 15);
                    Canvas.SetTop(stormCloud, ms.Layout.Y - SYSTEM_SHAPE_OFFSET - 11);
                    Canvas.SetZIndex(stormCloud, ZINDEX_STORM);
                    MainCanvas.Children.Add(stormCloud);
                    DynamicMapElements.Add(stormCloud);

                    // now the strong area..
                    foreach(string strongSys in s.StrongArea)
                    {
                        if(Region.IsSystemOnMap(strongSys))
                        {
                            MapSystem mss = Region.MapSystems[strongSys];

                            Image strongStormCloud = new Image
                            {
                                Width = 28,
                                Height = 28,
                                Name = "Storm",
                                Source = stormCloud.Source,
                                Stretch = Stretch.Uniform,
                                IsHitTestVisible = false,
                                Opacity = 1.0,
                            };

                            Canvas.SetLeft(strongStormCloud, mss.Layout.X - SYSTEM_SHAPE_OFFSET - 15);
                            Canvas.SetTop(strongStormCloud, mss.Layout.Y - SYSTEM_SHAPE_OFFSET - 11);
                            Canvas.SetZIndex(strongStormCloud, ZINDEX_STORM);
                            MainCanvas.Children.Add(strongStormCloud);
                            DynamicMapElements.Add(strongStormCloud);
                        }
                    }

                    // now the wiki area..
                    foreach(string weakSys in s.WeakArea)
                    {
                        if(Region.IsSystemOnMap(weakSys))
                        {
                            MapSystem msw = Region.MapSystems[weakSys];

                            Image weakStormCloud = new Image
                            {
                                Width = 18,
                                Height = 18,
                                Name = "Storm",
                                Source = stormCloud.Source,
                                Stretch = Stretch.Uniform,
                                IsHitTestVisible = false,
                                // Opacity = 0.5,
                            };

                            Canvas.SetLeft(weakStormCloud, msw.Layout.X - SYSTEM_SHAPE_OFFSET - 10);
                            Canvas.SetTop(weakStormCloud, msw.Layout.Y - SYSTEM_SHAPE_OFFSET - 6);
                            Canvas.SetZIndex(weakStormCloud, ZINDEX_STORM);
                            MainCanvas.Children.Add(weakStormCloud);
                            DynamicMapElements.Add(weakStormCloud);
                        }
                    }
                }
            }
        }

        public void AddTrigInvasionSytemsToMap()
        {
            if(!MapConf.ShowTrigInvasions)
            {
                return;
            }

            Brush trigBrush = new SolidColorBrush(Colors.DarkRed);
            trigBrush.Freeze();
            Brush trigOutlineBrush = new SolidColorBrush(Colors.Black);
            trigOutlineBrush.Freeze();
            Brush trigSecStatusChangeBrush = new SolidColorBrush(Colors.Orange);
            trigSecStatusChangeBrush.Freeze();

            ImageBrush ib = new ImageBrush();
            ib.TileMode = TileMode.Tile;
            ib.Stretch = Stretch.None;
            ib.ImageSource = trigLogoImage;
            ib.Freeze();

            foreach(KeyValuePair<string, EVEData.MapSystem> kvp in Region.MapSystems)
            {
                EVEData.MapSystem ms = kvp.Value;
                if(ms.ActualSystem.TrigInvasionStatus != EVEData.System.EdenComTrigStatus.None && !ms.OutOfRegion)
                {
                    Polygon TrigShape;
                    TrigShape = new Polygon();
                    TrigShape.Points.Add(new Point(ms.Layout.X - 13, ms.Layout.Y + 6));
                    TrigShape.Points.Add(new Point(ms.Layout.X, ms.Layout.Y - 14));
                    TrigShape.Points.Add(new Point(ms.Layout.X + 13, ms.Layout.Y + 6));

                    TrigShape.Stroke = trigOutlineBrush;
                    TrigShape.StrokeThickness = 1;
                    TrigShape.StrokeLineJoin = PenLineJoin.Round;
                    TrigShape.Fill = trigBrush;

                    Canvas.SetZIndex(TrigShape, ZINDEX_TRIG);

                    MainCanvas.Children.Add(TrigShape);
                    DynamicMapElements.Add(TrigShape);
                }
            }
        }

        /// <summary>
        /// Initialise the control
        /// </summary>
        public void Init()
        {
            EM = EVEData.EveManager.Instance;
            SelectedSystem = string.Empty;

            List<EVEData.System> globalSystemList = new List<EVEData.System>(EM.Systems);
            globalSystemList.Sort((a, b) => string.Compare(a.Name, b.Name));
            GlobalSystemDropDownAC.SelectedItem = null;
            GlobalSystemDropDownAC.ItemsSource = globalSystemList;

            DynamicMapElements = new List<UIElement>();
            DynamicMapElementsRangeMarkers = new List<UIElement>();
            DynamicMapElementsRouteHighlight = new List<UIElement>();
            DynamicMapElementsCharacters = new List<UIElement>();
            DynamicMapElementsJBHighlight = new List<UIElement>();
            DynamicMapElementsSysLinkHighlight = new List<UIElement>();

            ActiveCharacter = null;

            RegionSelectCB.ItemsSource = EM.Regions;

            ShowJumpBridges = MapConf.ToolBox_ShowJumpBridges;
            ShowNPCKills = MapConf.ToolBox_ShowNPCKills;
            ShowPodKills = MapConf.ToolBox_ShowPodKills;
            ShowShipJumps = MapConf.ToolBox_ShowShipJumps;
            ShowShipKills = MapConf.ToolBox_ShowShipKills;
            ShowSovOwner = MapConf.ToolBox_ShowSovOwner;
            ShowStandings = MapConf.ToolBox_ShowStandings;
            ShowSystemADM = MapConf.ToolBox_ShowSystemADM;
            ShowSystemSecurity = MapConf.ToolBox_ShowSystemSecurity;
            ShowSystemTimers = MapConf.ToolBox_ShowSystemTimers;
            ESIOverlayScale = MapConf.ToolBox_ESIOverlayScale;

            SelectRegion(MapConf.DefaultRegion);

            uiRefreshTimer = new System.Windows.Threading.DispatcherTimer();
            uiRefreshTimer.Tick += UiRefreshTimer_Tick; ;
            uiRefreshTimer.Interval = new TimeSpan(0, 0, 2);
            uiRefreshTimer.Start();

            DataContext = this;

            List<EVEData.MapSystem> newList = Region.MapSystems.Values.ToList().OrderBy(o => o.Name).ToList();
            SystemDropDownAC.ItemsSource = newList;

            PropertyChanged += MapObjectChanged;
        }

        /// <summary>
        /// Redraw the map
        /// </summary>
        /// <param name="FullRedraw">Clear all the static items or not</param>
        public void ReDrawMap(bool FullRedraw = false)
        {
            if(ActiveCharacter != null && FollowCharacter == true)
            {
                UpdateActiveCharacter();
            }

            if(FullRedraw)
            {
                Color c1 = MapConf.ActiveColourScheme.MapBackgroundColour;
                Color c2 = MapConf.ActiveColourScheme.MapBackgroundColour;
                c1.R = (byte)(0.9 * c1.R);
                c1.G = (byte)(0.9 * c1.G);
                c1.B = (byte)(0.9 * c1.B);

                LinearGradientBrush lgb = new LinearGradientBrush();
                lgb.StartPoint = new Point(0, 0);
                lgb.EndPoint = new Point(0, 1);

                lgb.GradientStops.Add(new GradientStop(c1, 0.0));
                lgb.GradientStops.Add(new GradientStop(c2, 0.05));
                lgb.GradientStops.Add(new GradientStop(c2, 0.95));
                lgb.GradientStops.Add(new GradientStop(c1, 1.0));

                MainCanvasGrid.Background = lgb;
                MainZoomControl.Background = lgb;

                MainCanvas.Children.Clear();

                // re-add the static content
                AddSystemsToMap();
            }
            else
            {
                // remove anything temporary
                foreach(UIElement uie in DynamicMapElements)
                {
                    MainCanvas.Children.Remove(uie);
                }
                DynamicMapElements.Clear();

                foreach(UIElement uie in DynamicMapElementsRangeMarkers)
                {
                    MainCanvas.Children.Remove(uie);
                }
                DynamicMapElementsRangeMarkers.Clear();

                foreach(UIElement uie in DynamicMapElementsRouteHighlight)
                {
                    MainCanvas.Children.Remove(uie);
                }
                DynamicMapElementsRouteHighlight.Clear();

                foreach(UIElement uie in DynamicMapElementsCharacters)
                {
                    MainCanvas.Children.Remove(uie);
                }
                DynamicMapElementsCharacters.Clear();
            }

            AddFWDataToMap();

            AddCharactersToMap();
            AddDataToMap();
            AddSystemIntelOverlay();
            AddIntelTrailsOverlay();
            AddHighlightToSystem(SelectedSystem);

            if(MapConf.DrawRoute)
            {
                AddRouteToMap();
            }

            AddBookmarkRouteToMap();

            AddWHLinksSystemsToMap();
            AddStormsToMap();
            AddSovConflictsToMap();
            AddTrigInvasionSytemsToMap();
            AddPOIsToMap();
        }

        /// <summary>
        /// Select A Region
        /// </summary>
        /// <param name="regionName">Region to Select</param>
        public void SelectRegion(string regionName)
        {
            // check we havent selected the same system
            if(Region != null && Region.Name == regionName)
            {
                return;
            }

            FollowCharacter = false;

            // close the context menu if its open
            ContextMenu cm = this.FindResource("SysRightClickContextMenu") as ContextMenu;
            cm.IsOpen = false;

            SelectedAlliance = 0;

            EM.UpdateIDsForMapRegion(regionName);

            // check its a valid system
            EVEData.MapRegion mr = EM.GetRegion(regionName);
            if(mr == null)
            {
                return;
            }

            // update the selected region
            Region = mr;
            RegionNameLabel.Content = mr.LocalizedName;
            MapConf.DefaultRegion = mr.Name;

            List<EVEData.MapSystem> newList = Region.MapSystems.Values.ToList().OrderBy(o => o.Name).ToList();
            SystemDropDownAC.ItemsSource = newList;

            // SJS Disabled until ticket resolved with CCP
            //            if (ActiveCharacter != null)
            //            {
            //                ActiveCharacter.UpdateStructureInfoForRegion2(regionName);
            //            }

            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                ReDrawMap(true);
            }), DispatcherPriority.Normal);

            // reset the zoom / export 
            MainZoomControl.ZoomToFill();

            // select the item in the dropdown
            RegionSelectCB.SelectedItem = Region;

            OnRegionChanged(regionName);
        }

        public void SelectSystem(string name, bool changeRegion = false)
        {
            if(SelectedSystem == name)
            {
                return;
            }

            EVEData.System sys = EM.GetEveSystem(name);

            if(sys == null)
            {
                return;
            }

            if(changeRegion && !Region.IsSystemOnMap(name))
            {
                SelectRegion(sys.Region);
            }

            foreach(KeyValuePair<string, MapSystem> kvp in Region.MapSystems)
            {
                if(kvp.Value.Name == name)
                {
                    if(MainZoomControl.Mode == ZoomControl.ZoomControlModes.Custom && MapConf.FollowOnZoom)
                    {
                        MainZoomControl.Show(kvp.Value.Layout.X, kvp.Value.Layout.Y, MainZoomControl.Zoom);
                    }

                    SystemDropDownAC.SelectedItem = kvp.Value;
                    SelectedSystem = kvp.Value.Name;
                    AddHighlightToSystem(name);

                    // Intel trail focus: clicking any system that lies on an active
                    // enemy trail selects that trail (the most recently-sighted one
                    // wins if multiple enemies have passed through). Clicking a
                    // system that no trail visits clears the selection. The next
                    // ReDrawMap will redraw the trail in its flowing/active style.
                    if(EM != null && EM.IntelTrails != null)
                    {
                        var hits = EM.IntelTrails.EnemiesAtSystem(kvp.Value.Name);
                        EM.IntelTrails.SelectedEnemyId = hits.Count > 0 ? hits[0] : null;
                    }

                    break;
                }
            }

            // now setup the anom data

            EVEData.AnomData system = ANOMManager.GetSystemAnomData(name);
            ANOMManager.ActiveSystem = system;
        }

        public void UpdateActiveCharacter(EVEData.LocalCharacter c = null)
        {
            if(ActiveCharacter != c && c != null)
            {
                ActiveCharacter = c;
            }

            if(ActiveCharacter != null && FollowCharacter)
            {
                EVEData.System s = EM.GetEveSystem(ActiveCharacter.Location);
                if(s != null)
                {
                    if(s.Region != Region.Name)
                    {
                        // change region
                        SelectRegion(s.Region);
                    }

                    SelectSystem(ActiveCharacter.Location);

                    // force the follow as this will be reset by the region change
                    FollowCharacter = true;
                }
            }
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if(handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        protected void OnRegionChanged(string name)
        {
            PropertyChangedEventHandler handler = RegionChanged;
            if(handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        /// <summary>
        /// Add Characters to the region
        /// </summary>
        private void AddCharactersToMap()
        {
            // Cache all characters in the same system so we can render them on seperate lines
            if(!MapConf.ShowCharacterNamesOnMap)
            {
                return;
            }

            // 0 = online
            // 1 = offline
            // 2 = fleet
            // 3 = warning
            NameTrackingLocationMap.Clear();

            foreach(EVEData.LocalCharacter c in EM.GetLocalCharactersCopy())
            {
                // ignore characters out of this Map..
                if(!Region.IsSystemOnMap(c.Location))
                {
                    continue;
                }

                // skip offline characters if enabled..
                if(!MapConf.ShowOfflineCharactersOnMap && !c.IsOnline)
                {
                    continue;
                }

                if(!NameTrackingLocationMap.ContainsKey(c.Location))
                {
                    NameTrackingLocationMap[c.Location] = new List<KeyValuePair<int, string>>();
                }

                int type = 0;
                if(!c.IsOnline)
                {
                    type = 2;
                }

                if(!string.IsNullOrEmpty(c.GameLogWarningText))
                {
                    type = 1;
                }

                NameTrackingLocationMap[c.Location].Add(new KeyValuePair<int, string>(type, c.Name));
            }

            if(ActiveCharacter != null && MapConf.FleetShowOnMap)
            {
                foreach(Fleet.FleetMember fm in ActiveCharacter.FleetInfo.Members)
                {
                    if(!Region.IsSystemOnMap(fm.Location))
                    {
                        continue;
                    }

                    // check its not one of our characters
                    bool addFleetMember = true;
                    foreach(EVEData.LocalCharacter c in EM.LocalCharacters)
                    {
                        if(c.Name == fm.Name)
                        {
                            addFleetMember = false;
                            break;
                        }
                    }

                    if(addFleetMember)
                    {
                        // ignore characters out of this Map..
                        if(!Region.IsSystemOnMap(fm.Location))
                        {
                            continue;
                        }

                        if(!NameTrackingLocationMap.ContainsKey(fm.Location))
                        {
                            NameTrackingLocationMap[fm.Location] = new List<KeyValuePair<int, string>>();
                        }

                        string displayName = fm.Name;
                        if(MapConf.FleetShowShipType)
                        {
                            displayName += " (" + fm.ShipType + ")";
                        }
                        NameTrackingLocationMap[fm.Location].Add(new KeyValuePair<int, string>(3, displayName));
                    }
                }
            }

            foreach(string lkvpk in NameTrackingLocationMap.Keys)
            {
                List<KeyValuePair<int, string>> lkvp = NameTrackingLocationMap[lkvpk];

                lkvp = lkvp.OrderByDescending(o => o.Key).ToList();

                EVEData.MapSystem ms = Region.MapSystems[lkvpk];

                bool addIndividualFleetMembers = true;
                int fleetMemberCount = 0;
                foreach(KeyValuePair<int, string> kvp in lkvp)
                {
                    if(kvp.Key == 3)
                    {
                        fleetMemberCount++;
                    }
                }

                if(fleetMemberCount > MapConf.FleetMaxMembersPerSystem)
                {
                    addIndividualFleetMembers = false;
                }

                double textYOffset = -24;
                double textXOffset = 6;

                SolidColorBrush fleetMemberText = new SolidColorBrush(MapConf.ActiveColourScheme.FleetMemberTextColour);
                fleetMemberText.Freeze();
                SolidColorBrush localCharacterText = new SolidColorBrush(MapConf.ActiveColourScheme.CharacterTextColour);
                localCharacterText.Freeze();
                SolidColorBrush localCharacterOfflineText = new SolidColorBrush(MapConf.ActiveColourScheme.CharacterOfflineTextColour);
                localCharacterOfflineText.Freeze();
                SolidColorBrush characterTextOutline = new SolidColorBrush(Colors.Black);
                characterTextOutline.Freeze();

                if(MapConf.ShowCompactCharactersOnMap)
                {
                    OutlinedTextBlock charText = new OutlinedTextBlock();
                    charText.Text = lkvp.Count.ToString();
                    charText.IsHitTestVisible = false;
                    charText.Stroke = characterTextOutline;
                    charText.Fill = localCharacterText;
                    charText.StrokeThickness = 2;

                    Canvas.SetLeft(charText, ms.Layout.X + textXOffset);
                    Canvas.SetTop(charText, ms.Layout.Y + textYOffset);
                    Canvas.SetZIndex(charText, ZINDEX_CHARACTERS);
                    MainCanvas.Children.Add(charText);
                    DynamicMapElements.Add(charText);
                }
                else
                {
                    foreach(KeyValuePair<int, string> kvp in lkvp)
                    {
                        if(kvp.Key == 1 && !MapConf.ShowOfflineCharactersOnMap)
                        {
                            continue;
                        }

                        if(kvp.Key == 0 || kvp.Key == 1 || kvp.Key == 2 || kvp.Key == 3 && addIndividualFleetMembers)
                        {
                            OutlinedTextBlock charText = new OutlinedTextBlock();
                            charText.Text = kvp.Value;
                            charText.IsHitTestVisible = false;
                            charText.Stroke = characterTextOutline;
                            charText.Fill = localCharacterText;
                            charText.StrokeThickness = 2;

                            switch(kvp.Key)
                            {
                                case 0:
                                    charText.Fill = localCharacterText;

                                    break;

                                case 2:
                                    charText.Fill = localCharacterOfflineText;
                                    charText.Text += "(Offline)";
                                    break;

                                case 3:
                                    charText.Fill = fleetMemberText;
                                    break;

                                case 1:
                                    charText.Fill = localCharacterText;
                                    charText.Text = "⚠ " + kvp.Value + " ⚠";
                                    break;
                            }

                            if(MapConf.ActiveColourScheme.CharacterTextSize > 0)
                            {
                                charText.FontSize = MapConf.ActiveColourScheme.CharacterTextSize;
                            }

                            Canvas.SetLeft(charText, ms.Layout.X + textXOffset);
                            Canvas.SetTop(charText, ms.Layout.Y + textYOffset);
                            Canvas.SetZIndex(charText, ZINDEX_CHARACTERS);
                            MainCanvas.Children.Add(charText);
                            DynamicMapElements.Add(charText);

                            textYOffset -= (MapConf.ActiveColourScheme.CharacterTextSize + 4);
                        }
                    }
                }

                if(!addIndividualFleetMembers)
                {
                    Label charText = new Label();
                    charText.Content = "Fleet (" + fleetMemberCount + ")";
                    charText.Foreground = fleetMemberText;
                    charText.IsHitTestVisible = false;

                    if(MapConf.ActiveColourScheme.CharacterTextSize > 0)
                    {
                        charText.FontSize = MapConf.ActiveColourScheme.CharacterTextSize;
                    }

                    Canvas.SetLeft(charText, ms.Layout.X + textXOffset);
                    Canvas.SetTop(charText, ms.Layout.Y + textYOffset);
                    Canvas.SetZIndex(charText, ZINDEX_CHARACTERS);
                    MainCanvas.Children.Add(charText);
                    DynamicMapElements.Add(charText);

                    textYOffset -= (MapConf.ActiveColourScheme.CharacterTextSize + 4);
                }

                // add circle for system

                double circleSize = 26;
                double circleOffset = circleSize / 2;

                Shape highlightSystemCircle = new Ellipse() { Height = circleSize, Width = circleSize };

                var charHighlightBrush = new SolidColorBrush(MapConf.ActiveColourScheme.CharacterHighlightColour);
                charHighlightBrush.Freeze();
                highlightSystemCircle.Stroke = charHighlightBrush;
                highlightSystemCircle.StrokeThickness = 3;

                RotateTransform rt = new RotateTransform();
                rt.CenterX = circleSize / 2;
                rt.CenterY = circleSize / 2;
                highlightSystemCircle.RenderTransform = rt;

                DoubleCollection dashes = new DoubleCollection();
                dashes.Add(1.0);
                dashes.Add(1.0);

                highlightSystemCircle.StrokeDashArray = dashes;

                Canvas.SetLeft(highlightSystemCircle, ms.Layout.X - circleOffset);
                Canvas.SetTop(highlightSystemCircle, ms.Layout.Y - circleOffset);
                Canvas.SetZIndex(highlightSystemCircle, ZINDEX_CHARACTERS - 1);

                MainCanvas.Children.Add(highlightSystemCircle);
                DynamicMapElements.Add(highlightSystemCircle);

                // Storyboard s = new Storyboard();
                DoubleAnimation da = new DoubleAnimation();
                da.From = 360;
                da.To = 0;
                da.Duration = new Duration(TimeSpan.FromSeconds(12));
                da.RepeatBehavior = RepeatBehavior.Forever;

                Timeline.SetDesiredFrameRate(da, 20);

                RotateTransform eTransform = (RotateTransform)highlightSystemCircle.RenderTransform;
                eTransform.BeginAnimation(RotateTransform.AngleProperty, da);
            }

            List<string> WarningZoneHighlights = new List<string>();

            foreach(EVEData.LocalCharacter c in EM.LocalCharacters)
            {
                if(MapConf.ShowDangerZone && c.WarningSystems != null && c.DangerZoneActive)
                {
                    foreach(string s in c.WarningSystems)
                    {
                        if(!WarningZoneHighlights.Contains(s))
                        {
                            WarningZoneHighlights.Add(s);
                        }
                    }
                }
            }

            double warningCircleSize = 40;
            double warningCircleSizeOffset = warningCircleSize / 2;

            foreach(string s in WarningZoneHighlights)
            {
                if(Region.IsSystemOnMap(s))
                {
                    EVEData.MapSystem mss = Region.MapSystems[s];
                    Shape WarninghighlightSystemCircle = new Ellipse() { Height = warningCircleSize, Width = warningCircleSize };
                    var warningBrush = new SolidColorBrush(Colors.IndianRed);
                    warningBrush.Freeze();
                    WarninghighlightSystemCircle.Stroke = warningBrush;
                    WarninghighlightSystemCircle.StrokeThickness = 3;

                    Canvas.SetLeft(WarninghighlightSystemCircle, mss.Layout.X - warningCircleSizeOffset);
                    Canvas.SetTop(WarninghighlightSystemCircle, mss.Layout.Y - warningCircleSizeOffset);
                    Canvas.SetZIndex(WarninghighlightSystemCircle, 15);
                    MainCanvas.Children.Add(WarninghighlightSystemCircle);
                    DynamicMapElements.Add(WarninghighlightSystemCircle);
                }
            }
        }

        private void AddDataToMap()
        {
            Color DataColor = MapConf.ActiveColourScheme.ESIOverlayColour;
            Color DataLargeColor = MapConf.ActiveColourScheme.ESIOverlayColour;

            DataLargeColor.R = (byte)(DataLargeColor.R * 0.75);
            DataLargeColor.G = (byte)(DataLargeColor.G * 0.75);
            DataLargeColor.B = (byte)(DataLargeColor.B * 0.75);

            Color DataLargeColorDelta = MapConf.ActiveColourScheme.ESIOverlayColour;
            DataLargeColorDelta.R = (byte)(DataLargeColorDelta.R * 0.4);
            DataLargeColorDelta.G = (byte)(DataLargeColorDelta.G * 0.4);
            DataLargeColorDelta.B = (byte)(DataLargeColorDelta.B * 0.4);

            SolidColorBrush dataColor = new SolidColorBrush(DataColor);
            dataColor.Freeze();
            SolidColorBrush infoColour = dataColor;

            SolidColorBrush PositiveDeltaColor = new SolidColorBrush(Colors.Green);
            PositiveDeltaColor.Freeze();
            SolidColorBrush NegativeDeltaColor = new SolidColorBrush(Colors.Red);
            NegativeDeltaColor.Freeze();

            Brush JumpInRange = new SolidColorBrush(MapConf.ActiveColourScheme.JumpRangeInColour);
            JumpInRange.Freeze();
            Brush JumpInRangeMulti = new SolidColorBrush(Colors.Black);
            JumpInRangeMulti.Freeze();

            SolidColorBrush infoColourDelta = new SolidColorBrush(DataLargeColorDelta);
            infoColourDelta.Freeze();

            SolidColorBrush zkbColour = new SolidColorBrush(MapConf.ActiveColourScheme.ZKillDataOverlay);
            zkbColour.Freeze();

            SolidColorBrush infoLargeColour = new SolidColorBrush(DataLargeColor);
            infoLargeColour.Freeze();
            SolidColorBrush infoVulnerable = new SolidColorBrush(MapConf.ActiveColourScheme.SOVStructureVulnerableColour);
            infoVulnerable.Freeze();
            SolidColorBrush infoVulnerableSoon = new SolidColorBrush(MapConf.ActiveColourScheme.SOVStructureVulnerableSoonColour);
            infoVulnerableSoon.Freeze();

            // Group standing Voronoi cells by SOV alliance so adjacent same-alliance cells merge.
            Dictionary<int, List<List<Vector2>>> standingCellsByAlliance = null;
            Dictionary<int, Brush> standingBrushByAlliance = null;
            if(ShowStandings && ActiveCharacter != null && ActiveCharacter.ESILinked)
            {
                standingCellsByAlliance = new Dictionary<int, List<List<Vector2>>>();
                standingBrushByAlliance = new Dictionary<int, Brush>();
            }

            BridgeInfoStackPanel.Children.Clear();
            if(!string.IsNullOrEmpty(currentJumpCharacter))
            {
                EVEData.System js = EM.GetEveSystem(currentCharacterJumpSystem);
                if(js != null)
                {
                    string text = "";
                    if(MapConf.ShowCharacterNamesOnMap)
                    {
                        text = $"{jumpShipType} range from {currentJumpCharacter} : {currentCharacterJumpSystem} ({js.Region})";
                    }
                    else
                    {
                        text = $"{jumpShipType} range from {currentCharacterJumpSystem} ({js.Region})";
                    }

                    Label l = new Label();
                    l.Content = text;
                    l.FontSize = 14;
                    l.FontWeight = FontWeights.Bold;
                    var inRegionTextBrush = new SolidColorBrush(MapConf.ActiveColourScheme.InRegionSystemTextColour);
                    inRegionTextBrush.Freeze();
                    l.Foreground = inRegionTextBrush;

                    BridgeInfoStackPanel.Children.Add(l);
                }
            }
            foreach(string key in activeJumpSpheres.Keys)
            {
                EVEData.System js = EM.GetEveSystem(key);
                string text = $"{activeJumpSpheres[key]} range from {key} ({js.Region})";

                Label l = new Label();
                l.Content = text;
                l.FontSize = 14;
                l.FontWeight = FontWeights.Bold;
                var inRegionTextBrush2 = new SolidColorBrush(MapConf.ActiveColourScheme.InRegionSystemTextColour);
                inRegionTextBrush2.Freeze();
                l.Foreground = inRegionTextBrush2;

                BridgeInfoStackPanel.Children.Add(l);
            }

            foreach(EVEData.MapSystem sys in Region.MapSystems.Values.ToList())
            {
                bool isSystemOOR = sys.OutOfRegion;

                if(Region.MetaRegion)
                {
                    isSystemOOR = !sys.ActualSystem.FactionWarSystem;
                }

                if(MapConf.LimitESIDataToRegion && isSystemOOR)
                {
                    continue;
                }

                infoColour = dataColor;
                long SystemAlliance = sys.ActualSystem.SOVAllianceID;

                int nPCKillsLastHour = sys.ActualSystem.NPCKillsLastHour;
                int podKillsLastHour = sys.ActualSystem.PodKillsLastHour;
                int shipKillsLastHour = sys.ActualSystem.ShipKillsLastHour;
                int jumpsLastHour = sys.ActualSystem.JumpsLastHour;

                int infoValue = -1;
                double infoSize = 0.0;

                if(ShowNPCKills)
                {
                    infoValue = nPCKillsLastHour;
                    infoSize = 0.15f * infoValue * ESIOverlayScale;

                    if(MapConf.ShowRattingDataAsDelta)
                    {
                        if(MapConf.ShowNegativeRattingDelta)
                        {
                            infoValue = Math.Abs(sys.ActualSystem.NPCKillsDeltaLastHour);
                            infoSize = 0.15f * infoValue * ESIOverlayScale;

                            if(sys.ActualSystem.NPCKillsDeltaLastHour > 0)
                            {
                                infoColour = PositiveDeltaColor;
                            }
                            else
                            {
                                infoColour = NegativeDeltaColor;
                            }
                        }
                    }
                }

                if(ShowPodKills)
                {
                    infoValue = podKillsLastHour;
                    infoSize = 20.0f * infoValue * ESIOverlayScale;
                }

                if(ShowShipKills)
                {
                    infoValue = shipKillsLastHour;
                    infoSize = 20.0f * infoValue * ESIOverlayScale;
                }

                if(ShowShipJumps)
                {
                    infoValue = sys.ActualSystem.JumpsLastHour;
                    infoSize = infoValue * ESIOverlayScale;
                }

                if(ShowSystemTimers && MapConf.ShowIhubVunerabilities && sys.ActualSystem.SovVunerabliltyStart != default)
                {
                    DateTime now = DateTime.Now;

                    if(now > sys.ActualSystem.SovVunerabliltyStart && now < sys.ActualSystem.SovVunerabliltyEnd)
                    {
                        infoValue = (int)sys.ActualSystem.SovADM;
                        infoSize = 30;
                        infoColour = infoVulnerable;
                    }
                    else if(now.AddMinutes(30) > sys.ActualSystem.SovVunerabliltyStart)
                    {
                        infoValue = (int)sys.ActualSystem.SovADM;
                        infoSize = 27;
                        infoColour = infoVulnerableSoon;
                    }
                    else
                    {
                        infoValue = -1;
                    }
                }

                if(infoValue > 0)
                {
                    // clamp to a minimum
                    if(infoSize < 24)
                        infoSize = 24;

                    if(MapConf.ClampMaxESIOverlayValue)
                    {
                        if(infoSize > MapConf.MaxESIOverlayValue)
                        {
                            infoSize = MapConf.MaxESIOverlayValue;
                        }
                    }

                    Shape infoCircle = new Ellipse() { Height = infoSize, Width = infoSize };
                    infoCircle.Fill = infoColour;

                    Canvas.SetZIndex(infoCircle, 10);
                    Canvas.SetLeft(infoCircle, sys.Layout.X - (infoSize / 2));
                    Canvas.SetTop(infoCircle, sys.Layout.Y - (infoSize / 2));
                    MainCanvas.Children.Add(infoCircle);
                    DynamicMapElements.Add(infoCircle);
                }

                if(ShowNPCKills && MapConf.ShowRattingDataAsDelta && !MapConf.ShowNegativeRattingDelta && sys.ActualSystem.NPCKillsDeltaLastHour > 0)
                {
                    infoValue = Math.Abs(sys.ActualSystem.NPCKillsDeltaLastHour);
                    infoSize = 0.15f * infoValue * ESIOverlayScale;

                    if(MapConf.ClampMaxESIOverlayValue)
                    {
                        if(infoSize > MapConf.MaxESIOverlayValue * .8)
                        {
                            infoSize = MapConf.MaxESIOverlayValue * .8;
                        }
                    }

                    Shape infoCircle = new Ellipse() { Height = infoSize, Width = infoSize };
                    infoCircle.Fill = infoColourDelta;

                    Canvas.SetZIndex(infoCircle, 12);
                    Canvas.SetLeft(infoCircle, sys.Layout.X - (infoSize / 2));
                    Canvas.SetTop(infoCircle, sys.Layout.Y - (infoSize / 2));
                    MainCanvas.Children.Add(infoCircle);
                    DynamicMapElements.Add(infoCircle);
                }

                if(standingCellsByAlliance != null && sys.ActualSystem.SOVAllianceID != 0)
                {
                    float Standing = 0.0f;

                    if(ActiveCharacter.AllianceID != 0 && ActiveCharacter.AllianceID == sys.ActualSystem.SOVAllianceID)
                    {
                        Standing = 10.0f;
                    }

                    if(sys.ActualSystem.SOVCorp != 0 && ActiveCharacter.Standings.Keys.Contains(sys.ActualSystem.SOVCorp))
                    {
                        Standing = ActiveCharacter.Standings[sys.ActualSystem.SOVCorp];
                    }

                    if(sys.ActualSystem.SOVAllianceID != 0 && ActiveCharacter.Standings.Keys.Contains(sys.ActualSystem.SOVAllianceID))
                    {
                        Standing = ActiveCharacter.Standings[sys.ActualSystem.SOVAllianceID];
                    }

                    if(Standing != 0.0f && sys.CellPoints != null && sys.CellPoints.Count >= 3)
                    {
                        Brush br = StandingNeutBrush;
                        if(Standing == -10.0)
                        {
                            br = StandingVBadBrush;
                        }
                        else if(Standing == -5.0)
                        {
                            br = StandingBadBrush;
                        }
                        else if(Standing == 5.0)
                        {
                            br = StandingGoodBrush;
                        }
                        else if(Standing == 10.0)
                        {
                            br = StandingVGoodBrush;
                        }

                        int allianceId = sys.ActualSystem.SOVAllianceID;
                        if(!standingCellsByAlliance.TryGetValue(allianceId, out List<List<Vector2>> cells))
                        {
                            cells = new List<List<Vector2>>();
                            standingCellsByAlliance[allianceId] = cells;
                            standingBrushByAlliance[allianceId] = br;
                        }

                        cells.Add(sys.CellPoints);
                    }
                }

                if(activeJumpSpheres.Count > 0 || currentJumpCharacter != null)
                {
                    bool AddHighlight = false;
                    bool DoubleHighlight = false;

                    // check character
                    if(!string.IsNullOrEmpty(currentJumpCharacter))
                    {
                        decimal Distance = EM.GetRangeBetweenSystems(currentCharacterJumpSystem, sys.Name);

                        decimal Max = 0.1m;

                        switch(jumpShipType)
                        {
                            case EVEData.EveManager.JumpShip.Super: { Max = 6.0m; } break;
                            case EVEData.EveManager.JumpShip.Titan: { Max = 6.0m; } break;
                            case EVEData.EveManager.JumpShip.Dread: { Max = 7.0m; } break;
                            case EVEData.EveManager.JumpShip.Carrier: { Max = 7.0m; } break;
                            case EVEData.EveManager.JumpShip.FAX: { Max = 7.0m; } break;
                            case EVEData.EveManager.JumpShip.CommandCarrier: { Max = 7.5m; } break;
                            case EVEData.EveManager.JumpShip.Blops: { Max = 8.0m; } break;
                            case EVEData.EveManager.JumpShip.Rorqual: { Max = 10.0m; } break;
                            case EVEData.EveManager.JumpShip.JF: { Max = 10.0m; } break;
                        }

                        if(Distance < Max && Distance > 0.0m && sys.ActualSystem.TrueSec <= 0.45 && currentCharacterJumpSystem != sys.Name)
                        {
                            AddHighlight = true;
                        }
                    }

                    foreach(string key in activeJumpSpheres.Keys)
                    {
                        if(!string.IsNullOrEmpty(currentJumpCharacter) && key == currentCharacterJumpSystem)
                        {
                            continue;
                        }

                        decimal Distance = EM.GetRangeBetweenSystems(key, sys.Name);
                        decimal Max = 0.1m;

                        switch(activeJumpSpheres[key])
                        {
                            case EVEData.EveManager.JumpShip.Super: { Max = 6.0m; } break;
                            case EVEData.EveManager.JumpShip.Titan: { Max = 6.0m; } break;
                            case EVEData.EveManager.JumpShip.Dread: { Max = 7.0m; } break;
                            case EVEData.EveManager.JumpShip.Carrier: { Max = 7.0m; } break;
                            case EVEData.EveManager.JumpShip.FAX: { Max = 7.0m; } break;
                            case EVEData.EveManager.JumpShip.CommandCarrier: { Max = 7.5m; } break;
                            case EVEData.EveManager.JumpShip.Blops: { Max = 8.0m; } break;
                            case EVEData.EveManager.JumpShip.Rorqual: { Max = 10.0m; } break;
                            case EVEData.EveManager.JumpShip.JF: { Max = 10.0m; } break;
                        }

                        if(Distance < Max && Distance > 0.0m && sys.ActualSystem.TrueSec <= 0.45 && key != sys.Name)
                        {
                            if(AddHighlight)
                            {
                                DoubleHighlight = true;
                            }
                            AddHighlight = true;
                        }
                    }

                    if(AddHighlight)
                    {
                        Brush HighlightBrush = JumpInRange;
                        if(DoubleHighlight)
                        {
                            HighlightBrush = JumpInRangeMulti;
                        }

                        if(MapConf.JumpRangeInAsOutline)
                        {
                            Shape InRangeMarker;

                            double ShapeSize = SYSTEM_SHAPE_SIZE + 10;
                            double halfShapeSize = ShapeSize / 2;

                            if(sys.ActualSystem.HasNPCStation)
                            {
                                InRangeMarker = new Rectangle() { Height = ShapeSize, Width = ShapeSize };
                            }
                            else
                            {
                                InRangeMarker = new Ellipse() { Height = ShapeSize, Width = ShapeSize };
                            }

                            InRangeMarker.Stroke = HighlightBrush;
                            InRangeMarker.StrokeThickness = 6;
                            InRangeMarker.StrokeLineJoin = PenLineJoin.Round;
                            InRangeMarker.Fill = HighlightBrush;

                            Canvas.SetLeft(InRangeMarker, sys.Layout.X - halfShapeSize);
                            Canvas.SetTop(InRangeMarker, sys.Layout.Y - halfShapeSize);
                            Canvas.SetZIndex(InRangeMarker, ZINDEX_RANGEMARKER);

                            MainCanvas.Children.Add(InRangeMarker);
                            DynamicMapElements.Add(InRangeMarker);
                        }
                        else
                        {
                            Polygon poly = new Polygon();

                            foreach(Vector2 p in sys.CellPoints)
                            {
                                System.Windows.Point wp = new Point(p.X, p.Y);
                                poly.Points.Add(wp);
                            }

                            poly.Fill = HighlightBrush;
                            poly.SnapsToDevicePixels = true;
                            poly.Stroke = poly.Fill;
                            poly.StrokeThickness = 3;
                            poly.StrokeDashCap = PenLineCap.Round;
                            poly.StrokeLineJoin = PenLineJoin.Round;
                            MainCanvas.Children.Add(poly);
                            DynamicMapElements.Add(poly);
                        }
                    }
                }
            }

            if(standingCellsByAlliance != null)
            {
                AddMergedStandingRegions(standingCellsByAlliance, standingBrushByAlliance);
            }

            Dictionary<string, int> ZKBBaseFeed = new Dictionary<string, int>();
            {
                foreach(EVEData.ZKillRedisQ.ZKBDataSimple zs in EM.ZKillFeed.KillStream.ToList())
                {
                    if(ZKBBaseFeed.Keys.Contains(zs.SystemName))
                    {
                        ZKBBaseFeed[zs.SystemName]++;
                    }
                    else
                    {
                        ZKBBaseFeed[zs.SystemName] = 1;
                    }
                }

                foreach(KeyValuePair<string, EVEData.MapSystem> kvp in Region.MapSystems)
                {
                    EVEData.MapSystem sys = kvp.Value;

                    if(ZKBBaseFeed.Keys.Contains(sys.ActualSystem.Name))
                    {
                        double ZKBValue = 24 + ((double)ZKBBaseFeed[sys.ActualSystem.Name] * ESIOverlayScale * 2);

                        Shape infoCircle = new Ellipse() { Height = ZKBValue, Width = ZKBValue };
                        infoCircle.Fill = zkbColour;

                        Canvas.SetZIndex(infoCircle, 11);
                        Canvas.SetLeft(infoCircle, sys.Layout.X - (ZKBValue / 2));
                        Canvas.SetTop(infoCircle, sys.Layout.Y - (ZKBValue / 2));
                        MainCanvas.Children.Add(infoCircle);
                        DynamicMapElements.Add(infoCircle);
                    }
                }
            }

            // Draw Infrastructure Upgrade indicators (green circles)
            Brush SysOutlineBrush = new SolidColorBrush(MapConf.ActiveColourScheme.SystemOutlineColour);
            SysOutlineBrush.Freeze();
            foreach(KeyValuePair<string, EVEData.MapSystem> kvp in Region.MapSystems)
            {
                EVEData.MapSystem sys = kvp.Value;
                bool isSystemOOR = sys.OutOfRegion;

                if(Region.MetaRegion)
                {
                    isSystemOOR = !sys.ActualSystem.FactionWarSystem;
                }

                if(!isSystemOOR && sys.ActualSystem.InfrastructureUpgrades.Count > 0)
                {
                    Shape UpgradeIndicator = new Ellipse { Width = 6, Height = 6 };
                    UpgradeIndicator.Stroke = SysOutlineBrush;
                    UpgradeIndicator.StrokeThickness = 1.0;
                    UpgradeIndicator.StrokeLineJoin = PenLineJoin.Round;
                    var limeGreenBrush = new SolidColorBrush(Colors.LimeGreen);
                    limeGreenBrush.Freeze();
                    UpgradeIndicator.Fill = limeGreenBrush;

                    Canvas.SetLeft(UpgradeIndicator, sys.Layout.X - 14);
                    Canvas.SetTop(UpgradeIndicator, sys.Layout.Y - 3);
                    Canvas.SetZIndex(UpgradeIndicator, ZINDEX_CYNOBEACON);
                    MainCanvas.Children.Add(UpgradeIndicator);
                    DynamicMapElements.Add(UpgradeIndicator);
                }
            }
        }

        private void AddMergedStandingRegions(Dictionary<int, List<List<Vector2>>> cellsByAlliance, Dictionary<int, Brush> brushByAlliance)
        {
            foreach(KeyValuePair<int, List<List<Vector2>>> kvp in cellsByAlliance)
            {
                AddMergedCellRegion(kvp.Value, brushByAlliance[kvp.Key], STANDING_REGION_STROKE_THICKNESS, trackAsDynamic: true);
            }
        }

        private void AddMergedCellRegion(IEnumerable<IReadOnlyList<Vector2>> cells, Brush fill, double strokeThickness, int zIndex = -1, bool trackAsDynamic = false)
        {
            PathGeometry geometry = PolygonUnion.UnionToPathGeometry(cells);
            if(geometry == null)
            {
                return;
            }

            Path regionPath = new Path
            {
                Data = geometry,
                Fill = fill,
                Stroke = CreateMergedRegionStroke(fill),
                StrokeThickness = strokeThickness,
                StrokeDashCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.None
            };

            if(zIndex >= 0)
            {
                Canvas.SetZIndex(regionPath, zIndex);
            }

            MainCanvas.Children.Add(regionPath);
            if(trackAsDynamic)
            {
                DynamicMapElements.Add(regionPath);
            }
        }

        private static Brush CreateMergedRegionStroke(Brush fill)
        {
            if(fill is SolidColorBrush solid)
            {
                Color c = solid.Color;
                return new SolidColorBrush(Color.FromArgb(230, c.R, c.G, c.B));
            }

            return fill;
        }

        private Brush Gallente_FL = FrozenBrush(Color.FromArgb(100, 73, 171, 104));
        private Brush Gallente_CLO = FrozenBrush(Color.FromArgb(100, 36, 90, 52));
        private Brush Gallente_RG = FrozenBrush(Color.FromArgb(100, 13, 35, 19));

        private Brush Caldari_FL = FrozenBrush(Color.FromArgb(100, 14, 186, 207));
        private Brush Caldari_CLO = FrozenBrush(Color.FromArgb(100, 0, 110, 129));
        private Brush Caldari_RG = FrozenBrush(Color.FromArgb(100, 0, 36, 43));

        private Brush Amarr_FL = FrozenBrush(Color.FromArgb(100, 216, 191, 25));
        private Brush Amarr_CLO = FrozenBrush(Color.FromArgb(100, 138, 114, 14));
        private Brush Amarr_RG = FrozenBrush(Color.FromArgb(100, 46, 36, 5));

        private Brush Minmatar_FL = FrozenBrush(Color.FromArgb(100, 221, 74, 79));
        private Brush Minmatar_CLO = FrozenBrush(Color.FromArgb(100, 140, 34, 41));
        private Brush Minmatar_RG = FrozenBrush(Color.FromArgb(100, 54, 11, 14));

        private Brush GetBrushForFWState(FactionWarfareSystemInfo.State state, int Owner)
        {
            //500001: "Caldari State";
            //500002: "Minmatar Republic";
            //500003: "Amarr Empire";
            //500004: "Gallente Federation";

            switch(state)
            {
                case FactionWarfareSystemInfo.State.Frontline:
                    {
                        switch(Owner)
                        {
                            case 500001: return Caldari_FL;
                            case 500002: return Minmatar_FL;
                            case 500003: return Amarr_FL;
                            case 500004: return Gallente_FL;
                        }
                    }
                    break;

                case FactionWarfareSystemInfo.State.CommandLineOperation:
                    {
                        switch(Owner)
                        {
                            case 500001: return Caldari_CLO;
                            case 500002: return Minmatar_CLO;
                            case 500003: return Amarr_CLO;
                            case 500004: return Gallente_CLO;
                        }
                    }
                    break;

                case FactionWarfareSystemInfo.State.Rearguard:
                    {
                        switch(Owner)
                        {
                            case 500001: return Caldari_RG;
                            case 500002: return Minmatar_RG;
                            case 500003: return Amarr_RG;
                            case 500004: return Gallente_RG;
                        }
                    }
                    break;
            }

            return null;
        }

        private void AddFWDataToMap()
        {
            if(!Region.MetaRegion || !ShowSovOwner)
            {
                return;
            }

            Brush FWLineBrushA = new SolidColorBrush(Colors.Yellow);
            FWLineBrushA.Freeze();
            Brush FWLineBrushB = new SolidColorBrush(Colors.Orange);
            FWLineBrushB.Freeze();
            Brush FWLineBrushC = new SolidColorBrush(Colors.OrangeRed);
            FWLineBrushC.Freeze();

            DoubleCollection dashes = new DoubleCollection();
            dashes.Add(1.0);
            dashes.Add(3.0);

            DoubleAnimation da = new DoubleAnimation();
            da.From = 20;
            da.To = 0;
            da.By = 2;
            da.Duration = new Duration(TimeSpan.FromSeconds(10));
            da.RepeatBehavior = RepeatBehavior.Forever;
            Timeline.SetDesiredFrameRate(da, 20);

            foreach(EVEData.MapSystem sys in Region.MapSystems.Values.ToList())
            {
                FactionWarfareSystemInfo fsw = null;
                foreach(FactionWarfareSystemInfo i in EveManager.Instance.FactionWarfareSystems)
                {
                    if(i.SystemID == sys.ActualSystem.ID)
                    {
                        fsw = i;
                        break;
                    }
                }

                if(fsw == null)
                {
                    continue;
                }

                if(fsw.SystemState == FactionWarfareSystemInfo.State.None)
                {
                    continue;
                }

                Polygon poly = new Polygon();

                foreach(Vector2 p in sys.CellPoints)
                {
                    System.Windows.Point wp = new Point(p.X, p.Y);
                    poly.Points.Add(wp);
                }

                poly.Fill = GetBrushForFWState(fsw.SystemState, fsw.OccupierID);
                poly.SnapsToDevicePixels = true;
                poly.Stroke = null;
                poly.StrokeThickness = 2;
                poly.StrokeDashCap = PenLineCap.Round;
                poly.StrokeLineJoin = PenLineJoin.Round;
                MainCanvas.Children.Add(poly);
                DynamicMapElements.Add(poly);

                if(fsw.SystemState == FactionWarfareSystemInfo.State.Rearguard)
                {
                    foreach(FactionWarfareSystemInfo i in EveManager.Instance.FactionWarfareSystems)
                    {
                        if(i.SystemID != fsw.SystemID && i.OccupierID == fsw.OccupierID && i.SystemState == FactionWarfareSystemInfo.State.CommandLineOperation && sys.ActualSystem.Jumps.Contains(i.SystemName))
                        {
                            foreach(EVEData.MapSystem ms in Region.MapSystems.Values.ToList())
                            {
                                if(ms.Name == i.SystemName)
                                {
                                    Line l = new Line();
                                    l.X1 = sys.Layout.X;
                                    l.Y1 = sys.Layout.Y;
                                    l.X2 = ms.Layout.X;
                                    l.Y2 = ms.Layout.Y;
                                    l.StrokeThickness = 1;
                                    l.Stroke = FWLineBrushA;
                                    l.StrokeDashArray = dashes;
                                    l.BeginAnimation(Shape.StrokeDashOffsetProperty, da);

                                    Canvas.SetZIndex(l, 19);
                                    MainCanvas.Children.Add(l);
                                    DynamicMapElements.Add(l);
                                    break;
                                }
                            }
                        }
                    }
                }

                if(fsw.SystemState == FactionWarfareSystemInfo.State.CommandLineOperation)
                {
                    foreach(FactionWarfareSystemInfo i in EveManager.Instance.FactionWarfareSystems)
                    {
                        if(i.SystemID != fsw.SystemID && i.OccupierID == fsw.OccupierID && i.SystemState == FactionWarfareSystemInfo.State.Frontline && sys.ActualSystem.Jumps.Contains(i.SystemName))
                        {
                            foreach(EVEData.MapSystem ms in Region.MapSystems.Values.ToList())
                            {
                                if(ms.Name == i.SystemName)
                                {
                                    Line l = new Line();
                                    l.X1 = sys.Layout.X;
                                    l.Y1 = sys.Layout.Y;
                                    l.X2 = ms.Layout.X;
                                    l.Y2 = ms.Layout.Y;
                                    l.StrokeThickness = 2;
                                    l.Stroke = FWLineBrushB;
                                    l.StrokeDashArray = dashes;
                                    l.BeginAnimation(Shape.StrokeDashOffsetProperty, da);

                                    Canvas.SetZIndex(l, 19);
                                    MainCanvas.Children.Add(l);
                                    DynamicMapElements.Add(l);
                                    break;
                                }
                            }
                        }
                    }
                }

                if(fsw.SystemState == FactionWarfareSystemInfo.State.Frontline)
                {
                    foreach(FactionWarfareSystemInfo i in EveManager.Instance.FactionWarfareSystems)
                    {
                        if(i.SystemID != fsw.SystemID && i.OccupierID != fsw.OccupierID && i.SystemState == FactionWarfareSystemInfo.State.Frontline && sys.ActualSystem.Jumps.Contains(i.SystemName))
                        {
                            foreach(EVEData.MapSystem ms in Region.MapSystems.Values.ToList())
                            {
                                if(ms.Name == i.SystemName)
                                {
                                    Line l = new Line();
                                    l.X1 = sys.Layout.X;
                                    l.Y1 = sys.Layout.Y;
                                    l.X2 = ms.Layout.X;
                                    l.Y2 = ms.Layout.Y;
                                    l.StrokeThickness = 3;
                                    l.Stroke = FWLineBrushC;
                                    l.BeginAnimation(Shape.StrokeDashOffsetProperty, da);

                                    Canvas.SetZIndex(l, 19);
                                    MainCanvas.Children.Add(l);
                                    DynamicMapElements.Add(l);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void AddHighlightToSystem(string name)
        {
            if(!Region.MapSystems.Keys.Contains(name))
            {
                return;
            }

            EVEData.MapSystem selectedSys = Region.MapSystems[name];
            if(selectedSys != null)
            {
                double centerX = selectedSys.Layout.X;
                double centerY = selectedSys.Layout.Y;
                Color ringColor = MapConf.ActiveColourScheme.SelectedSystemColour;

                int    ringCount  = 3;
                double startSize  = 64.0;
                double endSize    = 22.0;
                double duration   = 1.4;
                double stagger    = duration / ringCount;

                var ringBrush = new SolidColorBrush(ringColor);
                ringBrush.Freeze();

                for(int i = 0; i < ringCount; i++)
                {
                    double delaySeconds = i * stagger;

                    Ellipse ring = new Ellipse
                    {
                        Width             = startSize,
                        Height            = startSize,
                        Stroke            = ringBrush,
                        StrokeThickness   = 2.0,
                        Fill              = Brushes.Transparent,
                        IsHitTestVisible  = false,
                        Opacity           = 0.0,
                    };

                    ScaleTransform st = new ScaleTransform(1.0, 1.0, startSize / 2, startSize / 2);
                    ring.RenderTransform = st;

                    Canvas.SetLeft(ring, centerX - startSize / 2);
                    Canvas.SetTop(ring,  centerY - startSize / 2);
                    Canvas.SetZIndex(ring, 19);
                    MainCanvas.Children.Add(ring);
                    DynamicMapElements.Add(ring);

                    double endScale = endSize / startSize;
                    DoubleAnimation scaleXAnim = new DoubleAnimation
                    {
                        From           = 1.0,
                        To             = endScale,
                        Duration       = new Duration(TimeSpan.FromSeconds(duration)),
                        BeginTime      = TimeSpan.FromSeconds(delaySeconds),
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                    };
                    DoubleAnimation scaleYAnim = scaleXAnim.Clone();
                    Timeline.SetDesiredFrameRate(scaleXAnim, 30);
                    Timeline.SetDesiredFrameRate(scaleYAnim, 30);

                    DoubleAnimationUsingKeyFrames opacityAnim = new DoubleAnimationUsingKeyFrames
                    {
                        BeginTime      = TimeSpan.FromSeconds(delaySeconds),
                        RepeatBehavior = RepeatBehavior.Forever,
                        Duration       = new Duration(TimeSpan.FromSeconds(duration)),
                    };
                    opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.0,  KeyTime.FromPercent(0.0)));
                    opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromPercent(0.12)));
                    opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.0,  KeyTime.FromPercent(1.0)));
                    Timeline.SetDesiredFrameRate(opacityAnim, 30);

                    try
                    {
                        st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
                        st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
                        ring.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
                    }
                    catch { }
                }

                // small static dot to anchor the selection
                Ellipse dot = new Ellipse
                {
                    Width            = 6,
                    Height           = 6,
                    Fill             = ringBrush,
                    IsHitTestVisible = false,
                    Opacity          = 0.8,
                };
                Canvas.SetLeft(dot, centerX - 3);
                Canvas.SetTop(dot,  centerY - 3);
                Canvas.SetZIndex(dot, 20);
                MainCanvas.Children.Add(dot);
                DynamicMapElements.Add(dot);
            }
        }


        /// <summary>Planned route from the bookmark route planner, independent of ActiveRoute.</summary>
        public EVEData.BookmarkRoute BookmarkRoute { get; set; }

        /// <summary>Character location at the time BookmarkRoute was calculated; used only to draw the jump-from-start edge.</summary>
        public string BookmarkRouteStartSystem { get; set; }


        /// Draws the bookmark route planner's planned route : gate legs solid, capital jump legs
        /// dashed with an LY label. Independent of ActiveRoute/AddRouteToMap by design -- this is its
        /// own overlay, driven by <see cref="BookmarkRoute"/>.
        ///
        /// Known limitation : RegionControl only ever shows one region. When an edge's two endpoints aren't
        /// both on the currently displayed map (very often true of a capital jump leg, which by definition
        /// spans a gap the gate network doesn't bridge, sometimes into a different region), it's drawn as a
        /// stub + label pointing off the edge of the map toward the far system, the same treatment
        /// AddSystemsToMap already gives a jump bridge that leaves the region. An edge with both endpoints off
        /// the map is skipped entirely, same as AddRouteToMap does for ActiveRoute above. UniverseControl draws
        /// the same route in full, with no such gap, since it isn't limited to one region.
        /// </summary>
        private void AddBookmarkRouteToMap()
        {
            if(BookmarkRoute == null)
            {
                return;
            }

            Brush GateLegBrush = FrozenBrush(MapConf.ActiveColourScheme.SelectedSystemColour);
            Brush JumpLegBrush = FrozenBrush(MapConf.ActiveColourScheme.JumpRangeInColour);

            // Both leg types are dashed, so the flow animation below can show which way to travel. The gate
            // dashes are fine enough that the leg still reads as near-solid ; jump legs stay distinct by their
            // colour and LY label. Frozen because every leg shares the same two collections.
            DoubleCollection jumpDashes = new DoubleCollection { 4.0, 3.0 };
            DoubleCollection gateDashes = new DoubleCollection { 1.0, 1.0 };
            jumpDashes.Freeze();
            gateDashes.Freeze();

            try
            {
                foreach(EVEData.BookmarkRouteMapHelper.Edge edge in EVEData.BookmarkRouteMapHelper.EnumerateEdges(BookmarkRoute, BookmarkRouteStartSystem))
                {
                    bool fromOnMap = Region.IsSystemOnMap(edge.From);
                    bool toOnMap = Region.IsSystemOnMap(edge.To);

                    if(!fromOnMap && !toOnMap)
                    {
                        continue;
                    }

                    Brush legBrush = edge.IsJump ? JumpLegBrush : GateLegBrush;
                    Point fromPt, toPt;
                    string offMapLabel = null;

                    if(fromOnMap && toOnMap)
                    {
                        EVEData.MapSystem fromSys = Region.MapSystems[edge.From];
                        EVEData.MapSystem toSys = Region.MapSystems[edge.To];
                        fromPt = new Point(fromSys.Layout.X, fromSys.Layout.Y);
                        toPt = new Point(toSys.Layout.X, toSys.Layout.Y);
                    }
                    else
                    {
                        // one end is off the current region : anchor on the on-map system and draw a short
                        // stub toward the edge of the map, same treatment as an out-of-region jump bridge.
                        string onMapName = fromOnMap ? edge.From : edge.To;
                        string offMapName = fromOnMap ? edge.To : edge.From;
                        EVEData.MapSystem onMapSys = Region.MapSystems[onMapName];
                        EVEData.System offMapSys = EM.GetEveSystem(offMapName);

                        fromPt = new Point(onMapSys.Layout.X, onMapSys.Layout.Y);

                        // ponytail: same fixed offset AddSystemsToMap uses for an out-of-region jump bridge stub,
                        // so a system with both could overlap the two labels. Jitter/stack them if that shows up in testing.
                        toPt = new Point(onMapSys.Layout.X - 20, onMapSys.Layout.Y - 40);

                        offMapLabel = offMapSys != null ? $"{offMapSys.LocalizedName}\n({offMapSys.Region})" : offMapName;
                    }

                    Line routeLine = new Line
                    {
                        X1 = fromPt.X,
                        Y1 = fromPt.Y,
                        X2 = toPt.X,
                        Y2 = toPt.Y,
                        StrokeThickness = 3,
                        Stroke = legBrush,
                        Visibility = Visibility.Visible,
                    };

                    double dashCycle = edge.IsJump ? 7.0 : 2.0;
                    routeLine.StrokeDashArray = edge.IsJump ? jumpDashes : gateDashes;

                    if(!MapConf.DisableRoutePathAnimation)
                    {
                        // Offset runs 0 -> -dashCycle, which marches the dashes from (X1,Y1) toward (X2,Y2) --
                        // the direction of travel, which is the whole point : a still image of this route is an
                        // unreadable tangle. Duration scales with the cycle so both leg types flow at the same
                        // speed. Same treatment AddIntelTrailsOverlay gives the selected enemy's trail.
                        DoubleAnimation flow = new DoubleAnimation
                        {
                            From = 0,
                            To = -dashCycle,
                            Duration = new Duration(TimeSpan.FromSeconds(dashCycle / 6.0)),
                            RepeatBehavior = RepeatBehavior.Forever,
                        };
                        Timeline.SetDesiredFrameRate(flow, 20);
                        routeLine.BeginAnimation(Shape.StrokeDashOffsetProperty, flow);
                    }

                    Canvas.SetZIndex(routeLine, SYSTEM_LINK_INDEX);
                    MainCanvas.Children.Add(routeLine);
                    DynamicMapElements.Add(routeLine);

                    if(offMapLabel != null)
                    {
                        Shape offMapBlob = new Ellipse { Height = 6, Width = 6, Stroke = legBrush, Fill = legBrush };
                        Canvas.SetLeft(offMapBlob, toPt.X - 3);
                        Canvas.SetTop(offMapBlob, toPt.Y - 3);
                        Canvas.SetZIndex(offMapBlob, SYSTEM_LINK_INDEX);
                        MainCanvas.Children.Add(offMapBlob);
                        DynamicMapElements.Add(offMapBlob);

                        string text = edge.IsJump ? $"{offMapLabel}\n{edge.LY:0.##} LY" : offMapLabel;
                        Label offMapText = new Label { Content = text, Foreground = legBrush, IsHitTestVisible = false };
                        if(MapConf.ActiveColourScheme.SystemSubTextSize > 2)
                        {
                            offMapText.FontSize = MapConf.ActiveColourScheme.SystemSubTextSize;
                        }

                        Canvas.SetLeft(offMapText, toPt.X - 20);
                        Canvas.SetTop(offMapText, toPt.Y - 20);
                        Canvas.SetZIndex(offMapText, ZINDEX_SYSTEM);
                        MainCanvas.Children.Add(offMapText);
                        DynamicMapElements.Add(offMapText);
                    }
                    else if(edge.IsJump)
                    {
                        Label lyLabel = new Label { Content = $"{edge.LY:0.##} LY", Foreground = legBrush, IsHitTestVisible = false };
                        if(MapConf.ActiveColourScheme.SystemSubTextSize > 2)
                        {
                            lyLabel.FontSize = MapConf.ActiveColourScheme.SystemSubTextSize;
                        }

                        Canvas.SetLeft(lyLabel, (fromPt.X + toPt.X) / 2);
                        Canvas.SetTop(lyLabel, (fromPt.Y + toPt.Y) / 2);
                        Canvas.SetZIndex(lyLabel, ZINDEX_SYSTEM);
                        MainCanvas.Children.Add(lyLabel);
                        DynamicMapElements.Add(lyLabel);
                    }
                }
            }
            catch
            {
                // best-effort overlay, mirrors AddRouteToMap above : a bad/renamed system shouldn't take the whole redraw down.
            }
        }

        private void AddRouteToMap()
        {
            if(ActiveCharacter == null)
                return;

            Brush RouteBrush = new SolidColorBrush(Colors.Yellow);
            RouteBrush.Freeze();
            Brush RouteAnsiblexBrush = new SolidColorBrush(Colors.DarkGray);
            RouteAnsiblexBrush.Freeze();

            // no active route
            if(ActiveCharacter.ActiveRoute.Count == 0)
            {
                return;
            }

            string Start = "";
            string End = ActiveCharacter.Location;

            try
            {
                for(int i = 1; i < ActiveCharacter.ActiveRoute.Count; i++)
                {
                    Start = End;
                    End = ActiveCharacter.ActiveRoute[i].SystemName;

                    if(!(Region.IsSystemOnMap(Start) && Region.IsSystemOnMap(End)))
                    {
                        continue;
                    }

                    EVEData.MapSystem from = Region.MapSystems[Start];
                    EVEData.MapSystem to = Region.MapSystems[End];

                    Line routeLine = new Line();

                    routeLine.X1 = from.Layout.X;
                    routeLine.Y1 = from.Layout.Y;

                    routeLine.X2 = to.Layout.X;
                    routeLine.Y2 = to.Layout.Y;

                    routeLine.StrokeThickness = 5;
                    routeLine.Visibility = Visibility.Visible;
                    if(ActiveCharacter.ActiveRoute[i - 1].GateToTake == Navigation.GateType.Ansiblex)
                    {
                        routeLine.Stroke = RouteAnsiblexBrush;
                    }
                    else
                    {
                        routeLine.Stroke = RouteBrush;
                    }

                    DoubleCollection dashes = new DoubleCollection();
                    dashes.Add(1.0);
                    dashes.Add(1.0);

                    routeLine.StrokeDashArray = dashes;

                    // animate the jump bridges
                    DoubleAnimation da = new DoubleAnimation();
                    da.From = 200;
                    da.To = 0;
                    da.By = 2;
                    da.Duration = new Duration(TimeSpan.FromSeconds(40));
                    da.RepeatBehavior = RepeatBehavior.Forever;
                    Timeline.SetDesiredFrameRate(da, 20);

                    routeLine.StrokeDashArray = dashes;

                    if(!MapConf.DisableRoutePathAnimation)
                    {
                        routeLine.BeginAnimation(Shape.StrokeDashOffsetProperty, da);
                    }

                    Canvas.SetZIndex(routeLine, 19);
                    MainCanvas.Children.Add(routeLine);

                    DynamicMapElements.Add(routeLine);
                }
            }
            catch
            {
            }
        }

        private void AddSystemIntelOverlay()

        {

            //The tolist creates a temporary copy; however this is updated on a second thread

            foreach(EVEData.IntelData id in EM.IntelDataList.ToList())

            {

                foreach(string sysStr in id.Systems)

                {

                    if(Region.IsSystemOnMap(sysStr))

                    {

                        EVEData.MapSystem sys = Region.MapSystems[sysStr];



                        double radiusScale = (DateTime.Now - id.IntelTime).TotalSeconds / (double)MapConf.MaxIntelSeconds;



                        if(radiusScale < 0.0 || radiusScale >= 1.0)

                        {

                            continue;

                        }



                        // Age factor: 0.0 = fresh, 1.0 = about to expire

                        double ageFactor = radiusScale; // 0..1



                        // Colour: danger red for threat, muted green for clear

                        Color baseColor = id.ClearNotification

                            ? MapConf.ActiveColourScheme.IntelClearOverlayColour

                            : MapConf.ActiveColourScheme.IntelOverlayColour;



                        double centerX = sys.Layout.X;

                        double centerY = sys.Layout.Y;



                        // Number of pulse rings scales with freshness: 3 when fresh → 1 when old

                        int ringCount = ageFactor < 0.33 ? 3 : (ageFactor < 0.66 ? 2 : 1);

                        double startSize = 54.0;

                        double endSize   = 13.0;

                        // Duration slows down as intel ages (fresh = fast pulse, old = slow)

                        double duration  = 1.0 + (ageFactor * 0.8);

                        double stagger   = duration / 3.0;

                        var intelRingBrush = new SolidColorBrush(baseColor);
                        intelRingBrush.Freeze();

                        for(int ri = 0; ri < ringCount; ri++)

                        {

                            double delaySeconds = ri * stagger;



                            Ellipse ring = new Ellipse

                            {

                                Width            = startSize,

                                Height           = startSize,

                                Stroke           = intelRingBrush,

                                StrokeThickness  = id.ClearNotification ? 1.5 : 2.5,

                                Fill             = Brushes.Transparent,

                                IsHitTestVisible = false,

                                Opacity          = 0.0,

                            };



                            ScaleTransform st = new ScaleTransform(1.0, 1.0, startSize / 2, startSize / 2);

                            ring.RenderTransform = st;



                            Canvas.SetLeft(ring, centerX - startSize / 2);

                            Canvas.SetTop(ring,  centerY - startSize / 2);

                            Canvas.SetZIndex(ring, 15);

                            MainCanvas.Children.Add(ring);

                            DynamicMapElements.Add(ring);



                            double endScale = endSize / startSize;

                            DoubleAnimation scaleXAnim = new DoubleAnimation

                            {

                                From           = 1.0,

                                To             = endScale,

                                Duration       = new Duration(TimeSpan.FromSeconds(duration)),

                                BeginTime      = TimeSpan.FromSeconds(delaySeconds),

                                RepeatBehavior = RepeatBehavior.Forever,

                                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },

                            };

                            DoubleAnimation scaleYAnim = scaleXAnim.Clone();

                            Timeline.SetDesiredFrameRate(scaleXAnim, 30);

                            Timeline.SetDesiredFrameRate(scaleYAnim, 30);



                            // Max opacity fades with age: fresh=0.9, old=0.4

                            double peakOpacity = 0.9 - (ageFactor * 0.5);

                            DoubleAnimationUsingKeyFrames opacityAnim = new DoubleAnimationUsingKeyFrames

                            {

                                BeginTime      = TimeSpan.FromSeconds(delaySeconds),

                                RepeatBehavior = RepeatBehavior.Forever,

                                Duration       = new Duration(TimeSpan.FromSeconds(duration)),

                            };

                            opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.0,          KeyTime.FromPercent(0.0)));

                            opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(peakOpacity,  KeyTime.FromPercent(0.12)));

                            opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.0,          KeyTime.FromPercent(1.0)));

                            Timeline.SetDesiredFrameRate(opacityAnim, 30);



                            try

                            {

                                st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);

                                st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);

                                ring.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

                            }

                            catch { }

                        }



                        // Static fill dot — fades out as intel ages

                        double dotOpacity = Math.Max(0.15, 0.6 - ageFactor * 0.5);

                        Color dotFill = baseColor;

                        dotFill.A = (byte)(dotOpacity * 255);

                        var dotFillBrush = new SolidColorBrush(dotFill);
                        dotFillBrush.Freeze();

                        Ellipse fillDot = new Ellipse

                        {

                            Width            = 14,

                            Height           = 14,

                            Fill             = dotFillBrush,

                            IsHitTestVisible = false,

                        };

                        Canvas.SetLeft(fillDot, centerX - 7);

                        Canvas.SetTop(fillDot,  centerY - 7);

                        Canvas.SetZIndex(fillDot, 14);

                        MainCanvas.Children.Add(fillDot);

                        DynamicMapElements.Add(fillDot);

                    }

                }

            }

        }

        /// <summary>
        /// DEMO: Draw faint trails between systems where the same enemy id has
        /// been reported by intel. Trails are connectionless polylines; each
        /// segment fades out individually as it ages so the most recent moves
        /// stand out and ancient sightings dissolve.
        ///
        /// Visual budget: max 1.5px stroke, peak opacity ~0.28. The trails sit
        /// at ZIndex 13 — under the pulse rings (15) but above standard map
        /// links so a hostile route is visible at a glance without stealing
        /// attention from the active pulse.
        /// </summary>
        private void AddIntelTrailsOverlay()
        {
            if(EM == null || EM.IntelTrails == null) return;

            var lifetime = EM.IntelTrails.TrailLifetime;
            var now = DateTime.Now;
            var trails = EM.IntelTrails.GetActiveTrails();
            if(trails.Count == 0) return;

            string selectedId = EM.IntelTrails.SelectedEnemyId;
            // Trail colours come from the active theme; fall back to Cyan if scheme is missing.
            Color intelColor = MapConf.ActiveColourScheme.IntelTrailColour;
            Color intelSelectedColor = MapConf.ActiveColourScheme.IntelTrailSelectedColour;

            // ---- visual budget ----
            const double idleStroke    = 1.5;     // unselected: very subtle dashed thread
            const double activeStroke  = 2.8;     // selected: bold and present
            const double idleAlphaMax  = 0.22;    // unselected: barely there
            const double activeAlpha   = 0.92;    // selected: fully visible
            const double secondsPerSeg = 0.6;     // flow speed (a dash period crosses one segment)

            foreach(var trail in trails)
            {
                bool isSelected = selectedId != null &&
                                  string.Equals(selectedId, trail.EnemyId, StringComparison.OrdinalIgnoreCase);

                for(int i = 1; i < trail.Points.Count; i++)
                {
                    var prev = trail.Points[i - 1];
                    var curr = trail.Points[i];

                    if(!Region.IsSystemOnMap(prev.SystemName)) continue;
                    if(!Region.IsSystemOnMap(curr.SystemName)) continue;

                    EVEData.MapSystem a = Region.MapSystems[prev.SystemName];
                    EVEData.MapSystem b = Region.MapSystems[curr.SystemName];

                    double ageSec = (now - curr.Time).TotalSeconds;
                    double ageFactor = Math.Max(0.0, Math.Min(1.0, ageSec / lifetime.TotalSeconds));

                    double opacity = isSelected
                        ? activeAlpha                                  // selected: ignore age, stay bright
                        : idleAlphaMax * (1.0 - ageFactor);            // idle: fade with age
                    if(opacity < 0.02) continue;

                    Color segColor = isSelected ? intelSelectedColor : intelColor;
                    segColor.A = (byte)(opacity * 255);

                    var segBrush = new SolidColorBrush(segColor);
                    segBrush.Freeze();

                    var line = new System.Windows.Shapes.Line
                    {
                        X1 = a.Layout.X,
                        Y1 = a.Layout.Y,
                        X2 = b.Layout.X,
                        Y2 = b.Layout.Y,
                        Stroke = segBrush,
                        StrokeThickness = isSelected ? activeStroke : idleStroke,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        IsHitTestVisible = false,
                        StrokeDashCap = PenLineCap.Round,
                        StrokeDashArray = new DoubleCollection { 4, 3 },
                    };

                    if(isSelected)
                    {
                        // Flow! Animate StrokeDashOffset from 0 to -(dashCycle) over the
                        // time it takes to cross one full segment. WPF normalises offset
                        // by stroke thickness, hence the * thickness.
                        const double dashCycle = 4.0 + 3.0;
                        double dashEndOffset = -dashCycle;
                        var flowAnim = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0,
                            To = dashEndOffset,
                            Duration = TimeSpan.FromSeconds(secondsPerSeg),
                            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                        };
                        System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(flowAnim, 30);
                        line.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, flowAnim);
                    }

                    Canvas.SetZIndex(line, 13);
                    MainCanvas.Children.Add(line);
                    DynamicMapElements.Add(line);

                    // Endpoint dot at the most recent sighting (the head of the trail).
                    if(i == trail.Points.Count - 1)
                    {
                        double dotSize = isSelected ? 8.0 : 5.0;
                        Color dotColor = isSelected ? intelSelectedColor : intelColor;
                        double dotAlpha = isSelected ? activeAlpha : Math.Min(idleAlphaMax + 0.10, 0.4);
                        dotColor.A = (byte)(dotAlpha * 255);
                        var trailDotBrush = new SolidColorBrush(dotColor);
                        trailDotBrush.Freeze();
                        var dot = new Ellipse
                        {
                            Width = dotSize,
                            Height = dotSize,
                            Fill = trailDotBrush,
                            IsHitTestVisible = false,
                        };
                        Canvas.SetLeft(dot, b.Layout.X - dotSize / 2.0);
                        Canvas.SetTop(dot, b.Layout.Y - dotSize / 2.0);
                        Canvas.SetZIndex(dot, 13);
                        MainCanvas.Children.Add(dot);
                        DynamicMapElements.Add(dot);
                    }
                }
            }
        }

        /// <summary>
        /// Alliance ticker / list row colour from standings (bright tier colours for dark maps) when ESI-linked; otherwise <paramref name="defaultBrush"/>.
        /// Resolution matches the map standings overlay (own alliance, then corp, then alliance contact). Use <paramref name="sovCorpId"/> 0 for alliance-only rows (e.g. legend list).
        /// </summary>
        private static Brush GetAllianceTickerBrushFromStanding(LocalCharacter c, long sovAllianceId, long sovCorpId, Brush defaultBrush)
        {
            if(c == null || !c.ESILinked)
            {
                return defaultBrush;
            }

            float standing = 0.0f;

            if(c.AllianceID != 0 && c.AllianceID == sovAllianceId)
            {
                standing = 10.0f;
            }

            if(sovCorpId != 0 && c.Standings.Keys.Contains(sovCorpId))
            {
                standing = c.Standings[sovCorpId];
            }

            if(sovAllianceId != 0 && c.Standings.Keys.Contains(sovAllianceId))
            {
                standing = c.Standings[sovAllianceId];
            }

            if(standing == -10.0f)
            {
                return TickerStandingTerribleBrush;
            }

            if(standing == -5.0f)
            {
                return TickerStandingBadBrush;
            }

            if(standing == 5.0f)
            {
                return TickerStandingGoodBrush;
            }

            if(standing == 10.0f)
            {
                return TickerStandingExcellentBrush;
            }

            return defaultBrush;
        }

        /// <summary>
        /// Add the base systems, and jumps to the map
        /// </summary>
        private void AddSystemsToMap()
        {
            // brushes
            Brush SysOutlineBrush = new SolidColorBrush(MapConf.ActiveColourScheme.SystemOutlineColour);
            SysOutlineBrush.Freeze();
            Brush SysInRegionBrush = new SolidColorBrush(MapConf.ActiveColourScheme.InRegionSystemColour);
            SysInRegionBrush.Freeze();
            Brush SysOutRegionBrush = new SolidColorBrush(MapConf.ActiveColourScheme.OutRegionSystemColour);
            SysOutRegionBrush.Freeze();

            Brush SysInRegionDarkBrush = new SolidColorBrush(DarkenColour(MapConf.ActiveColourScheme.InRegionSystemColour));
            SysInRegionDarkBrush.Freeze();
            Brush SysOutRegionDarkBrush = new SolidColorBrush(DarkenColour(MapConf.ActiveColourScheme.OutRegionSystemColour));
            SysOutRegionDarkBrush.Freeze();

            Brush HasIceBrush = new SolidColorBrush(Colors.LightBlue);
            HasIceBrush.Freeze();

            Brush SysInRegionTextBrush = new SolidColorBrush(MapConf.ActiveColourScheme.InRegionSystemTextColour);
            SysInRegionTextBrush.Freeze();
            Brush SysOutRegionTextBrush = new SolidColorBrush(MapConf.ActiveColourScheme.OutRegionSystemTextColour);
            SysOutRegionTextBrush.Freeze();

            Brush FriendlyJumpBridgeBrush = new SolidColorBrush(MapConf.ActiveColourScheme.FriendlyJumpBridgeColour);
            FriendlyJumpBridgeBrush.Freeze();
            Brush DisabledJumpBridgeBrush = new SolidColorBrush(MapConf.ActiveColourScheme.DisabledJumpBridgeColour);
            DisabledJumpBridgeBrush.Freeze();

            Brush JumpInRange = new SolidColorBrush(MapConf.ActiveColourScheme.JumpRangeInColour);
            JumpInRange.Freeze();
            Brush JumpInRangeMulti = new SolidColorBrush(Colors.Black);
            JumpInRangeMulti.Freeze();

            Brush Incursion = new SolidColorBrush(MapConf.ActiveColourScheme.ActiveIncursionColour);
            Incursion.Freeze();

            Brush ConstellationHighlight = new SolidColorBrush(MapConf.ActiveColourScheme.ConstellationHighlightColour);
            ConstellationHighlight.Freeze();

            Brush DarkTextColourBrush = new SolidColorBrush(Colors.Black);
            DarkTextColourBrush.Freeze();

            Color bgtc = MapConf.ActiveColourScheme.MapBackgroundColour;
            bgtc.A = 192;
            Brush SysTextBackgroundBrush = new SolidColorBrush(bgtc);
            SysTextBackgroundBrush.Freeze();

            Color bgd = MapConf.ActiveColourScheme.MapBackgroundColour;

            float darkenFactor = 0.9f;

            bgd.R = (byte)(darkenFactor * bgd.R);
            bgd.G = (byte)(darkenFactor * bgd.G);
            bgd.B = (byte)(darkenFactor * bgd.B);

            Brush MapBackgroundBrushDarkend = new SolidColorBrush(bgd);
            MapBackgroundBrushDarkend.Freeze();

            List<long> AlliancesKeyList = new List<long>();

            Brush NormalGateBrush = new SolidColorBrush(MapConf.ActiveColourScheme.NormalGateColour);
            NormalGateBrush.Freeze();
            Brush ConstellationGateBrush = new SolidColorBrush(MapConf.ActiveColourScheme.ConstellationGateColour);
            ConstellationGateBrush.Freeze();
            Brush RegionGateBrush = new SolidColorBrush(MapConf.ActiveColourScheme.RegionGateColour);
            RegionGateBrush.Freeze();

            // cache all system links
            List<GateHelper> systemLinks = new List<GateHelper>();

            Random rnd = new Random(4);

            EVEData.System selectedEveSystem = EM.GetEveSystem(SelectedSystem);
            bool highlightConstellation = selectedEveSystem != null && ShowSystemTimers && MapConf.ShowIhubVunerabilities;
            List<List<Vector2>> incursionCells = MapConf.ShowActiveIncursions ? new List<List<Vector2>>() : null;
            List<List<Vector2>> constellationCells = highlightConstellation ? new List<List<Vector2>>() : null;
            List<List<Vector2>> selectedAllianceCells = (ShowSovOwner && SelectedAlliance != 0) ? new List<List<Vector2>>() : null;

            foreach(KeyValuePair<string, EVEData.MapSystem> kvp in Region.MapSystems)
            {
                EVEData.MapSystem mapSystem = kvp.Value;

                bool isSystemOOR = mapSystem.OutOfRegion;

                if(Region.MetaRegion)
                {
                    var fws = EM.FactionWarfareSystems.FirstOrDefault(c => c.SystemName == mapSystem.Name);
                    if(fws == null)
                    {
                        isSystemOOR = true;
                    }
                    else
                    {
                        isSystemOOR = false;
                    }
                }

                double trueSecVal = mapSystem.ActualSystem.TrueSec;
                if(MapConf.ShowSimpleSecurityView)
                {
                    if(mapSystem.ActualSystem.TrueSec >= 0.45)
                    {
                        trueSecVal = 1.0;
                    }
                    else if(mapSystem.ActualSystem.TrueSec > 0.0)
                    {
                        trueSecVal = 0.4;
                    }
                }

                Brush securityColorFill = new SolidColorBrush(MapColours.GetSecStatusColour(trueSecVal, MapConf.ShowTrueSec));
                securityColorFill.Freeze();



                var systemSubTextLines = new List<(string Text, Brush Foreground)>();

                // add circle for system
                Polygon systemShape = new Polygon();
                systemShape.StrokeThickness = 1.5;

                bool needsOutline = true;
                bool drawNPCStation = mapSystem.ActualSystem.HasNPCStation;

                if(drawNPCStation)
                {
                    needsOutline = true;
                }

                // override
                if(ShowSystemADM)
                {
                    needsOutline = true;
                }

                if(mapSystem.ActualSystem.HasIceBelt || mapSystem.ActualSystem.HasBlueA0Star)
                {
                    string icons = "";
                    // ☀❄ // ⛭☼ ☀

                    if(mapSystem.ActualSystem.HasBlueA0Star)
                    {
                        icons += "⛭";
                    }

                    if(mapSystem.ActualSystem.HasIceBelt)
                    {
                        icons += "❄";
                    }

                    Label sysIcons = new Label();
                    sysIcons.FontSize = 8;
                    sysIcons.IsHitTestVisible = false;
                    sysIcons.Content = icons;
                    sysIcons.HorizontalContentAlignment = HorizontalAlignment.Center;
                    sysIcons.VerticalContentAlignment = VerticalAlignment.Center;
                    sysIcons.Foreground = HasIceBrush;

                    Canvas.SetLeft(sysIcons, mapSystem.Layout.X - SYSTEM_SHAPE_OFFSET + 11);
                    Canvas.SetTop(sysIcons, mapSystem.Layout.Y - SYSTEM_SHAPE_OFFSET - 9);
                    Canvas.SetZIndex(sysIcons, ZINDEX_SYSICON);
                    MainCanvas.Children.Add(sysIcons);
                }

                double shapeSize = SYSTEM_SHAPE_SIZE;
                double shapeOffset = SYSTEM_SHAPE_OFFSET;

                if(mapSystem.OutOfRegion)
                {
                    shapeSize = SYSTEM_SHAPE_OOR_SIZE;
                    shapeOffset = SYSTEM_SHAPE_OOR_OFFSET;
                }

                if(needsOutline)
                {
                    Shape SystemOutline;
                    if(mapSystem.ActualSystem.HasNPCStation)
                    {
                        SystemOutline = new Rectangle { Width = shapeSize, Height = shapeSize };
                    }
                    else
                    {
                        SystemOutline = new Ellipse { Width = shapeSize, Height = shapeSize };
                    }

                    SystemOutline.Stroke = SysOutlineBrush;
                    SystemOutline.StrokeThickness = 1.5;
                    SystemOutline.StrokeLineJoin = PenLineJoin.Round;

                    if(isSystemOOR)
                    {
                        SystemOutline.Fill = SysOutRegionBrush;
                    }
                    else
                    {
                        SystemOutline.Fill = SysInRegionBrush;
                    }

                    // override with sec status colours
                    if(ShowSystemSecurity)
                    {
                        SystemOutline.Fill = securityColorFill;
                    }

                    if(ShowSystemADM && mapSystem.ActualSystem.SovADM != 0.0f)
                    {
                        float SovVal = mapSystem.ActualSystem.SovADM;

                        float Blend = 1.0f - ((SovVal - 1.0f) / 5.0f);
                        byte r, g;

                        if(Blend < 0.5)
                        {
                            r = 255;
                            g = (byte)(255 * Blend / 0.5);
                        }
                        else
                        {
                            g = 255;
                            r = (byte)(255 - (255 * (Blend - 0.5) / 0.5));
                        }

                        var admBrush = new SolidColorBrush(Color.FromRgb(r, g, 0));
                        admBrush.Freeze();
                        SystemOutline.Fill = admBrush;
                    }

                    SystemOutline.DataContext = mapSystem;
                    SystemOutline.MouseDown += ShapeMouseDownHandler;
                    SystemOutline.MouseEnter += ShapeMouseOverHandler;
                    SystemOutline.MouseLeave += ShapeMouseOverHandler;

                    Canvas.SetLeft(SystemOutline, mapSystem.Layout.X - shapeOffset);
                    Canvas.SetTop(SystemOutline, mapSystem.Layout.Y - shapeOffset);
                    Canvas.SetZIndex(SystemOutline, ZINDEX_SYSTEM_OUTLINE);
                    MainCanvas.Children.Add(SystemOutline);
                }

                if(ShowSystemADM && mapSystem.ActualSystem.SovADM != 0.0 && !ShowSystemTimers && !mapSystem.OutOfRegion)
                {
                    Label sovADM = new Label();
                    sovADM.Content = "1.0";
                    sovADM.FontSize = 7;
                    sovADM.IsHitTestVisible = false;
                    sovADM.Content = $"{mapSystem.ActualSystem.SovADM:f1}";
                    sovADM.HorizontalContentAlignment = HorizontalAlignment.Center;
                    sovADM.VerticalContentAlignment = VerticalAlignment.Center;
                    sovADM.Width = shapeSize + 2;
                    sovADM.Height = shapeSize + 2;
                    sovADM.Foreground = DarkTextColourBrush;
                    sovADM.FontWeight = FontWeights.Bold;

                    Canvas.SetLeft(sovADM, mapSystem.Layout.X - (shapeOffset + 1));
                    Canvas.SetTop(sovADM, mapSystem.Layout.Y - (shapeOffset + 1));
                    Canvas.SetZIndex(sovADM, ZINDEX_ADM);
                    MainCanvas.Children.Add(sovADM);
                }

                Grid sysTextGrid = new Grid
                {
                    Width = SYSTEM_TEXT_WIDTH,
                    Height = SYSTEM_TEXT_HEIGHT,
                };

                StackPanel sp = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                };

                Label sysText = new Label();
                sysText.Content = mapSystem.LocalizedName;


                if (MapConf.ActiveColourScheme.SystemTextSize > 0)
                {
                    sysText.FontSize = MapConf.ActiveColourScheme.SystemTextSize;
                }

                sysText.Foreground = SysInRegionTextBrush;
                double sysTextOffset = SYSTEM_TEXT_Y_OFFSET;

                if(mapSystem.OutOfRegion)
                {
                    sysText.Foreground = SysOutRegionTextBrush;
                    sysText.FontSize -= 2;
                    sysTextOffset -= 2;
                }

                Thickness border = new Thickness(0.0);

                sysText.Padding = border;
                sysText.Margin = border;
                sysText.IsHitTestVisible = false;

                sp.Children.Add(sysText);

                switch(mapSystem.TextPos)
                {
                    case MapSystem.TextPosition.Top:
                        {
                            double spLeft = mapSystem.Layout.X - (SYSTEM_TEXT_X_OFFSET);
                            double spTop = mapSystem.Layout.Y - (SYSTEM_SHAPE_OFFSET + SYSTEM_TEXT_HEIGHT + 1);
                            Canvas.SetLeft(sysTextGrid, spLeft);
                            Canvas.SetTop(sysTextGrid, spTop);

                            sysText.HorizontalContentAlignment = HorizontalAlignment.Center;
                            sysText.VerticalContentAlignment = VerticalAlignment.Center;
                            sp.VerticalAlignment = VerticalAlignment.Bottom;
                            sp.HorizontalAlignment = HorizontalAlignment.Center;

                            sysTextGrid.Children.Add(sp);
                        }
                        break;

                    case MapSystem.TextPosition.Bottom:
                        {
                            double spLeft = mapSystem.Layout.X - (SYSTEM_TEXT_X_OFFSET);
                            double spTop = mapSystem.Layout.Y + (SYSTEM_SHAPE_OFFSET) - 1;
                            Canvas.SetLeft(sysTextGrid, spLeft);
                            Canvas.SetTop(sysTextGrid, spTop);

                            sysText.HorizontalContentAlignment = HorizontalAlignment.Center;
                            sysText.VerticalContentAlignment = VerticalAlignment.Center;

                            sp.VerticalAlignment = VerticalAlignment.Top;
                            sp.HorizontalAlignment = HorizontalAlignment.Center;

                            sysTextGrid.Children.Add(sp);
                        }
                        break;

                    case MapSystem.TextPosition.Left:
                        {
                            double spLeft = mapSystem.Layout.X - (SYSTEM_SHAPE_OFFSET + SYSTEM_TEXT_WIDTH + 3);
                            double spTop = mapSystem.Layout.Y - (SYSTEM_TEXT_Y_OFFSET);
                            Canvas.SetLeft(sysTextGrid, spLeft);
                            Canvas.SetTop(sysTextGrid, spTop);

                            sysText.HorizontalContentAlignment = HorizontalAlignment.Right;
                            sysText.VerticalContentAlignment = VerticalAlignment.Center;
                            sp.VerticalAlignment = VerticalAlignment.Center;
                            sp.HorizontalAlignment = HorizontalAlignment.Right;
                            sysTextGrid.Children.Add(sp);
                        }
                        break;

                    case MapSystem.TextPosition.Right:
                        {
                            double spLeft = mapSystem.Layout.X + SYSTEM_SHAPE_OFFSET + 3;
                            double spTop = mapSystem.Layout.Y - SYSTEM_TEXT_Y_OFFSET;
                            Canvas.SetLeft(sysTextGrid, spLeft);
                            Canvas.SetTop(sysTextGrid, spTop);
                            sp.VerticalAlignment = VerticalAlignment.Center;
                            sp.HorizontalAlignment = HorizontalAlignment.Left;

                            sysText.HorizontalContentAlignment = HorizontalAlignment.Left;
                            sysText.VerticalContentAlignment = VerticalAlignment.Center;
                            sysTextGrid.Children.Add(sp);
                        }
                        break;
                }

                Canvas.SetZIndex(sysTextGrid, ZINDEX_SYSTEM);
                Canvas.SetZIndex(sysText, ZINDEX_SYSTEM);

                MainCanvas.Children.Add(sysTextGrid);

                // generate the list of links
                foreach(string jumpTo in mapSystem.ActualSystem.Jumps)
                {
                    if(Region.IsSystemOnMap(jumpTo))
                    {
                        EVEData.MapSystem to = Region.MapSystems[jumpTo];

                        bool NeedsAdd = true;
                        foreach(GateHelper gh in systemLinks)
                        {
                            if(((gh.from == mapSystem) || (gh.to == mapSystem)) && ((gh.from == to) || (gh.to == to)))
                            {
                                NeedsAdd = false;
                                break;
                            }
                        }

                        if(NeedsAdd)
                        {
                            GateHelper g = new GateHelper();
                            g.from = mapSystem;
                            g.to = to;
                            systemLinks.Add(g);
                        }
                    }
                }

                double regionMarkerOffset = SYSTEM_REGION_TEXT_Y_OFFSET;

                if(incursionCells != null && mapSystem.ActualSystem.ActiveIncursion && mapSystem.CellPoints != null && mapSystem.CellPoints.Count >= 3)
                {
                    incursionCells.Add(mapSystem.CellPoints);
                }

                if(MapConf.ShowCynoBeacons && mapSystem.ActualSystem.HasJumpBeacon)
                {
                    Shape CynoBeaconLogo = new Ellipse { Width = 8, Height = 8 };
                    CynoBeaconLogo.Stroke = SysOutlineBrush;
                    CynoBeaconLogo.StrokeThickness = 1.0;
                    CynoBeaconLogo.StrokeLineJoin = PenLineJoin.Round;
                    var orangeRedBrush = new SolidColorBrush(Colors.OrangeRed);
                    orangeRedBrush.Freeze();
                    CynoBeaconLogo.Fill = orangeRedBrush;

                    Canvas.SetLeft(CynoBeaconLogo, mapSystem.Layout.X + 7);
                    Canvas.SetTop(CynoBeaconLogo, mapSystem.Layout.Y - 12);
                    Canvas.SetZIndex(CynoBeaconLogo, ZINDEX_CYNOBEACON);
                    MainCanvas.Children.Add(CynoBeaconLogo);
                }

                if(MapConf.ShowJoveObservatories && mapSystem.ActualSystem.HasJoveObservatory && !ShowSystemADM && !ShowSystemTimers)
                {
                    Image JoveLogo = new Image
                    {
                        Width = (shapeSize / 20) * 10,
                        Height = (shapeSize / 20) * 10,
                        Name = "JoveLogo",
                        Source = joveLogoImage,
                        Stretch = Stretch.Uniform,
                        IsHitTestVisible = false,
                    };

                    RenderOptions.SetBitmapScalingMode(JoveLogo, BitmapScalingMode.NearestNeighbor);

                    Canvas.SetLeft(JoveLogo, mapSystem.Layout.X - (JoveLogo.Width / 2));
                    Canvas.SetTop(JoveLogo, mapSystem.Layout.Y - (JoveLogo.Height / 2));
                    Canvas.SetZIndex(JoveLogo, ZINDEX_JOVE);
                    MainCanvas.Children.Add(JoveLogo);
                }

                if(constellationCells != null && mapSystem.ActualSystem.ConstellationID == selectedEveSystem.ConstellationID && mapSystem.CellPoints != null && mapSystem.CellPoints.Count >= 3)
                {
                    constellationCells.Add(mapSystem.CellPoints);
                }

                int SystemAlliance = mapSystem.ActualSystem.SOVAllianceID;

                if(selectedAllianceCells != null && SystemAlliance == SelectedAlliance && mapSystem.CellPoints != null && mapSystem.CellPoints.Count >= 3)
                {
                    selectedAllianceCells.Add(mapSystem.CellPoints);
                }

                if(isSystemOOR)
                {
                    string localRegionName = EM.GetRegion(mapSystem.Region)?.LocalizedName ?? mapSystem.Region;
                    systemSubTextLines.Add(("(" + localRegionName + ")", SysOutRegionTextBrush));

                    Polygon poly = new Polygon();
                    foreach(Vector2 p in mapSystem.CellPoints)
                    {
                        System.Windows.Point wp = new Point(p.X, p.Y);
                        poly.Points.Add(wp);
                    }

                    //poly.Fill
                    poly.Fill = MapBackgroundBrushDarkend;
                    poly.SnapsToDevicePixels = true;
                    poly.Stroke = MapBackgroundBrushDarkend;
                    poly.StrokeThickness = 3;
                    poly.StrokeDashCap = PenLineCap.Round;
                    poly.StrokeLineJoin = PenLineJoin.Round;
                    MainCanvas.Children.Add(poly);
                }

                if((ShowSovOwner) && SystemAlliance != 0 && EM.IDToAlliance.Keys.Contains(SystemAlliance))
                {
                    string allianceName = EM.GetAllianceName(SystemAlliance);
                    string allianceTicker = EM.GetAllianceTicker(SystemAlliance);
                    string content = allianceTicker;

                    Brush defaultAllianceTickerBrush = isSystemOOR ? SysOutRegionTextBrush : SysInRegionTextBrush;
                    Brush allianceSubTextBrush = GetAllianceTickerBrushFromStanding(
                        ActiveCharacter,
                        SystemAlliance,
                        mapSystem.ActualSystem.SOVCorp,
                        defaultAllianceTickerBrush);
                    systemSubTextLines.Add((content, allianceSubTextBrush));

                    if(mapSystem.ActualSystem.SovIsCapitalSystem)
                    {
                        systemSubTextLines.Add(("(Capital)", allianceSubTextBrush));
                    }


                    if(!AlliancesKeyList.Contains(SystemAlliance))
                    {
                        AlliancesKeyList.Add(SystemAlliance);
                    }
                }

                if(systemSubTextLines.Count > 0)
                {
                    TextAlignment subTextAlignment;
                    switch(mapSystem.TextPos)
                    {
                        case MapSystem.TextPosition.Left:
                            subTextAlignment = TextAlignment.Right;
                            break;

                        case MapSystem.TextPosition.Right:
                            subTextAlignment = TextAlignment.Left;
                            break;

                        case MapSystem.TextPosition.Top:
                        case MapSystem.TextPosition.Bottom:
                        default:
                            subTextAlignment = TextAlignment.Center;
                            break;
                    }

                    if(isSystemOOR)
                    {
                        regionMarkerOffset -= 4;
                    }

                    foreach((string lineText, Brush lineBrush) in systemSubTextLines)
                    {
                        TextBlock sysSubText = new TextBlock
                        {
                            Text = lineText,
                            Width = SYSTEM_REGION_TEXT_WIDTH,
                            Padding = new Thickness(0),
                            Margin = new Thickness(0),
                            TextAlignment = subTextAlignment,
                            IsHitTestVisible = false,
                            Foreground = lineBrush,
                        };

                        if(MapConf.ActiveColourScheme.SystemSubTextSize > 0)
                        {
                            sysSubText.FontSize = MapConf.ActiveColourScheme.SystemSubTextSize;
                        }

                        sp.Children.Add(sysSubText);
                    }
                }
            }

            if(incursionCells != null && incursionCells.Count > 0)
            {
                AddMergedCellRegion(incursionCells, Incursion, STANDING_REGION_STROKE_THICKNESS, ZINDEX_POLY);
            }

            if(constellationCells != null && constellationCells.Count > 0)
            {
                AddMergedCellRegion(constellationCells, ConstellationHighlight, STANDING_REGION_STROKE_THICKNESS, ZINDEX_POLY);
            }

            if(selectedAllianceCells != null && selectedAllianceCells.Count > 0)
            {
                AddMergedCellRegion(selectedAllianceCells, SelectedAllianceBrush, STANDING_REGION_STROKE_THICKNESS, ZINDEX_POLY);
            }

            // now add the links
            foreach(GateHelper gh in systemLinks)
            {
                Line sysLink = new Line();

                sysLink.X1 = gh.from.Layout.X;
                sysLink.Y1 = gh.from.Layout.Y;

                sysLink.X2 = gh.to.Layout.X;
                sysLink.Y2 = gh.to.Layout.Y;

                sysLink.Stroke = NormalGateBrush;

                if(gh.from.ActualSystem.ConstellationID != gh.to.ActualSystem.ConstellationID)
                {
                    sysLink.Stroke = ConstellationGateBrush;
                }

                if(gh.from.ActualSystem.Region != gh.to.ActualSystem.Region)
                {
                    sysLink.Stroke = RegionGateBrush;
                }

                sysLink.StrokeThickness = 2;
                sysLink.Visibility = Visibility.Visible;

                Canvas.SetZIndex(sysLink, SYSTEM_LINK_INDEX);
                MainCanvas.Children.Add(sysLink);
            }

            if(ShowJumpBridges && EM.JumpBridges != null)
            {
                foreach(EVEData.JumpBridge jb in EM.JumpBridges)
                {
                    if(Region.IsSystemOnMap(jb.From) || Region.IsSystemOnMap(jb.To))
                    {
                        EVEData.MapSystem from;
                        EVEData.System to;

                        if(!Region.IsSystemOnMap(jb.From))
                        {
                            from = Region.MapSystems[jb.To];
                            to = EM.GetEveSystem(jb.From);
                        }
                        else
                        {
                            from = Region.MapSystems[jb.From];
                            to = EM.GetEveSystem(jb.To);
                        }

                        Point startPoint = new Point(from.Layout.X, from.Layout.Y);
                        Point endPoint;

                        if(!Region.IsSystemOnMap(jb.To) || !Region.IsSystemOnMap(jb.From))
                        {
                            endPoint = new Point(from.Layout.X - 20, from.Layout.Y - 40);

                            Shape jbOutofSystemBlob = new Ellipse() { Height = 6, Width = 6 };
                            Canvas.SetLeft(jbOutofSystemBlob, endPoint.X - 3);
                            Canvas.SetTop(jbOutofSystemBlob, endPoint.Y - 3);
                            Canvas.SetZIndex(jbOutofSystemBlob, 19);

                            MainCanvas.Children.Add(jbOutofSystemBlob);

                            Label jbOutofRegionText = new Label();

                            if(jb.Disabled)
                            {
                                jbOutofSystemBlob.Stroke = DisabledJumpBridgeBrush;
                                jbOutofRegionText.Foreground = DisabledJumpBridgeBrush;
                            }
                            else
                            {
                                jbOutofSystemBlob.Stroke = FriendlyJumpBridgeBrush;
                                jbOutofRegionText.Foreground = FriendlyJumpBridgeBrush;
                            }
                            jbOutofSystemBlob.Fill = jbOutofSystemBlob.Stroke;

                            jbOutofRegionText.Content = $"{to.LocalizedName}\n({to.Region})";
                            if (MapConf.ActiveColourScheme.SystemSubTextSize > 2)
                            {
                                jbOutofRegionText.FontSize = MapConf.ActiveColourScheme.SystemSubTextSize;
                            }
                            jbOutofRegionText.IsHitTestVisible = false;

                            Canvas.SetLeft(jbOutofRegionText, from.Layout.X - 20);
                            Canvas.SetTop(jbOutofRegionText, from.Layout.Y - 60);
                            Canvas.SetZIndex(jbOutofRegionText, ZINDEX_SYSTEM);

                            MainCanvas.Children.Add(jbOutofRegionText);
                        }
                        else
                        {
                            EVEData.MapSystem toSys = Region.MapSystems[jb.To];
                            endPoint = new Point(toSys.Layout.X, toSys.Layout.Y);
                        }

                        Line jbLine = new Line();

                        jbLine.X1 = startPoint.X;
                        jbLine.Y1 = startPoint.Y;

                        jbLine.X2 = endPoint.X;
                        jbLine.Y2 = endPoint.Y;

                        jbLine.StrokeThickness = 2;

                        DoubleCollection dashes = new DoubleCollection();

                        if(!jb.Disabled)
                        {
                            dashes.Add(1.0);
                            dashes.Add(3.0);
                            jbLine.Stroke = FriendlyJumpBridgeBrush;
                        }
                        else
                        {
                            dashes.Add(1.0);
                            dashes.Add(6.0);
                            jbLine.Stroke = DisabledJumpBridgeBrush;
                        }

                        jbLine.StrokeDashArray = dashes;

                        // animate the jump bridges
                        DoubleAnimation da = new DoubleAnimation();
                        da.From = 0;
                        da.To = 200;
                        da.By = 2;
                        da.Duration = new Duration(TimeSpan.FromSeconds(100));
                        da.RepeatBehavior = RepeatBehavior.Forever;
                        Timeline.SetDesiredFrameRate(da, 20);

                        if(!MapConf.DisableJumpBridgesPathAnimation)
                        {
                            jbLine.BeginAnimation(Shape.StrokeDashOffsetProperty, da);
                        }

                        Canvas.SetZIndex(jbLine, 19);

                        MainCanvas.Children.Add(jbLine);
                    }
                }
            }

            bool showZakLinks = true;
            if(showZakLinks && Region.IsSystemOnMap("Zarzakh"))
            {
                MapSystem zarSystem = Region.MapSystems["Zarzakh"];

                foreach(MapSystem ms in Region.MapSystems.Values)
                {
                    if(ms.Name == "Zarzakh" || !ms.ActualSystem.HasJoveGate)
                    {
                        continue;
                    }

                    Line zarLink = new Line();

                    zarLink.X1 = zarSystem.Layout.X;
                    zarLink.Y1 = zarSystem.Layout.Y;

                    zarLink.X2 = ms.Layout.X;
                    zarLink.Y2 = ms.Layout.Y;

                    zarLink.StrokeThickness = 1.2;

                    DoubleCollection dashes = new DoubleCollection();

                    dashes.Add(1.0);
                    dashes.Add(1.0);
                    zarLink.StrokeDashArray = dashes;
                    zarLink.Stroke = ConstellationGateBrush;
                    MainCanvas.Children.Add(zarLink);
                }
            }

            if(AlliancesKeyList.Count > 0)
            {
                AllianceNameList.Visibility = Visibility.Visible;
                AllianceNameListStackPanel.Children.Clear();

                Brush fontColour = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF767576"));
                fontColour.Freeze();
                Brush SelectedFont = new SolidColorBrush(Colors.White);
                SelectedFont.Freeze();

                List<Label> AllianceNameListLabels = new List<Label>();

                Thickness p = new Thickness(1);

                foreach(int allianceID in AlliancesKeyList)
                {
                    string allianceName = EM.GetAllianceName(allianceID);
                    string allianceTicker = EM.GetAllianceTicker(allianceID);

                    Label akl = new Label();
                    akl.MouseDown += AllianceKeyList_MouseDown;
                    akl.DataContext = allianceID.ToString();
                    akl.Content = $"{allianceTicker}\t{allianceName}";
                    akl.Foreground = GetAllianceTickerBrushFromStanding(ActiveCharacter, allianceID, 0, fontColour);
                    akl.Margin = p;
                    akl.Padding = p;

                    if(allianceID == SelectedAlliance)
                    {
                        akl.Foreground = SelectedFont;
                    }

                    AllianceNameListLabels.Add(akl);
                }

                List<Label> SortedAlliance = AllianceNameListLabels.OrderBy(an => an.Content).ToList();

                foreach(Label l in SortedAlliance)
                {
                    AllianceNameListStackPanel.Children.Add(l);
                }
            }
            else
            {
                AllianceNameList.Visibility = Visibility.Hidden;
            }

            // now add any info items
            if(InfoLayer != null)
            {
                foreach(InfoItem ii in InfoLayer)
                {
                    if(ii.Region == Region.Name)
                    {
                        Shape s = ii.Draw();
                        MainCanvas.Children.Add(s);
                    }
                }
            }
        }

        private void AllianceKeyList_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if(sender is not Label { DataContext: string allianceIDText } ||
               !long.TryParse(allianceIDText, out long allianceID))
            {
                return;
            }

            if(e.ClickCount == 2)
            {
                string AURL = $"https://zkillboard.com/region/{Region.ID}/alliance/{allianceID}/";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AURL) { UseShellExecute = true });
            }
            else
            {
                if(SelectedAlliance == allianceID)
                {
                    SelectedAlliance = 0;
                }
                else
                {
                    SelectedAlliance = allianceID;
                }
                ReDrawMap(true);
            }
        }

        private void characterRightClickAutoRange_Clicked(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            if(mi != null)
            {
                EveManager.JumpShip js = EveManager.JumpShip.Super;

                LocalCharacter lc = ((MenuItem)mi.Parent).DataContext as LocalCharacter;

                if(mi.DataContext as string == "6")
                {
                    js = EveManager.JumpShip.Super;
                }
                if(mi.DataContext as string == "7")
                {
                    js = EveManager.JumpShip.Carrier;
                }

                if(mi.DataContext as string == "7.5")
                {
                    js = EveManager.JumpShip.CommandCarrier;
                }

                if(mi.DataContext as string == "8")
                {
                    js = EveManager.JumpShip.Blops;
                }

                if(mi.DataContext as string == "10")
                {
                    js = EveManager.JumpShip.JF;
                }

                if(mi.DataContext as string == "0")
                {
                    showJumpDistance = false;
                    currentJumpCharacter = "";
                    currentCharacterJumpSystem = "";
                }
                else
                {
                    showJumpDistance = true;
                    currentJumpCharacter = lc.Name;
                    currentCharacterJumpSystem = lc.Location;
                    jumpShipType = js;
                }
            }

            ReDrawMap(false);
        }

        private static Color DarkenColour(Color inCol)
        {
            Color Dark = inCol;
            Dark.R = (Byte)(0.8 * Dark.R);
            Dark.G = (Byte)(0.8 * Dark.G);
            Dark.B = (Byte)(0.8 * Dark.B);
            return Dark;
        }

        private void FollowCharacterChk_Checked(object sender, RoutedEventArgs e)
        {
            UpdateActiveCharacter();
        }

        private void GlobalSystemDropDownAC_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FollowCharacter = false;

            EVEData.System sd = GlobalSystemDropDownAC.SelectedItem as EVEData.System;

            if(sd != null && Region != null)
            {
                bool ChangeRegion = sd.Region != Region.Name;
                SelectSystem(sd.Name, ChangeRegion);
                ReDrawMap(ChangeRegion);
            }
        }

        private void HelpIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if(HelpList.Visibility == Visibility.Hidden)
            {
                HelpList.Visibility = Visibility.Visible;
                var yellowBrush = new SolidColorBrush(Colors.Yellow);
                yellowBrush.Freeze();
                helpIcon.Fill = yellowBrush;
                var blackBrush = new SolidColorBrush(Colors.Black);
                blackBrush.Freeze();
                HelpQM.Foreground = blackBrush;
            }
            else
            {
                HelpList.Visibility = Visibility.Hidden;
                var blackBrush2 = new SolidColorBrush(Colors.Black);
                blackBrush2.Freeze();
                helpIcon.Fill = blackBrush2;
                var whiteBrush = new SolidColorBrush(Colors.White);
                whiteBrush.Freeze();
                HelpQM.Foreground = whiteBrush;
            }
        }

        private void MapObjectChanged(object sender, PropertyChangedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                ReDrawMap(true);
            }), DispatcherPriority.Normal);
        }

        /// <summary>
        /// Region Selection Changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RegionSelectCB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FollowCharacter = false;

            EVEData.MapRegion rd = RegionSelectCB.SelectedItem as EVEData.MapRegion;
            if(rd == null)
            {
                return;
            }

            SelectRegion(rd.Name);
        }

        private void SetJumpRange_Click(object sender, RoutedEventArgs e)
        {
            EVEData.MapSystem eveSys = ((System.Windows.FrameworkElement)((System.Windows.FrameworkElement)sender).Parent).DataContext as EVEData.MapSystem;

            MenuItem mi = sender as MenuItem;
            if(mi != null)
            {
                EveManager.JumpShip js = EveManager.JumpShip.Super;

                if(mi.DataContext as string == "6")
                {
                    js = EveManager.JumpShip.Super;
                }
                if(mi.DataContext as string == "7")
                {
                    js = EveManager.JumpShip.Carrier;
                }

                if(mi.DataContext as string == "7.5")
                {
                    js = EveManager.JumpShip.CommandCarrier;
                }


                if(mi.DataContext as string == "8")
                {
                    js = EveManager.JumpShip.Blops;
                }

                if(mi.DataContext as string == "10")
                {
                    js = EveManager.JumpShip.JF;
                }

                activeJumpSpheres[eveSys.Name] = js;

                if(mi.DataContext as string == "0")
                {
                    if(activeJumpSpheres.Keys.Contains(eveSys.Name))
                    {
                        activeJumpSpheres.Remove(eveSys.Name);
                    }
                }

                if(mi.DataContext as string == "-1")
                {
                    activeJumpSpheres.Clear();
                    currentJumpCharacter = "";
                    currentCharacterJumpSystem = "";
                }

                if(!string.IsNullOrEmpty(currentJumpCharacter))
                {
                    showJumpDistance = true;
                }
                else
                {
                    showJumpDistance = activeJumpSpheres.Count > 0;
                }

                ReDrawMap(true);
            }
        }

        /// <summary>
        /// Shape (ie System) MouseDown handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ShapeMouseDownHandler(object sender, MouseButtonEventArgs e)
        {
            Shape obj = sender as Shape;

            EVEData.MapSystem selectedSys = obj.DataContext as EVEData.MapSystem;

            if(e.ChangedButton == MouseButton.Left)
            {
                if(e.ClickCount == 1)
                {
                    bool redraw = false;
                    if(showJumpDistance || (ShowSystemTimers && MapConf.ShowIhubVunerabilities))
                    {
                        redraw = true;
                    }
                    FollowCharacter = false;
                    SelectSystem(selectedSys.Name);

                    ReDrawMap(redraw);
                }

                if(e.ClickCount == 2 && selectedSys.Region != Region.Name)
                {
                    foreach(EVEData.MapRegion rd in EM.Regions)
                    {
                        if(rd.Name == selectedSys.Region)
                        {
                            RegionSelectCB.SelectedItem = rd;

                            ReDrawMap();
                            SelectSystem(selectedSys.Name);
                            break;
                        }
                    }
                }
            }

            if(e.ChangedButton == MouseButton.Right)
            {
                ContextMenu cm = this.FindResource("SysRightClickContextMenu") as ContextMenu;
                cm.PlacementTarget = obj;
                cm.DataContext = selectedSys;

                MenuItem setDesto = cm.Items[2] as MenuItem;
                MenuItem addWaypoint = cm.Items[4] as MenuItem;
                MenuItem clearRoute = cm.Items[6] as MenuItem;

                MenuItem characters = cm.Items[7] as MenuItem;
                characters.Items.Clear();

                setDesto.IsEnabled = false;
                addWaypoint.IsEnabled = false;
                clearRoute.IsEnabled = false;

                characters.IsEnabled = false;
                characters.Visibility = Visibility.Collapsed;

                if(ActiveCharacter != null && ActiveCharacter.ESILinked)
                {
                    setDesto.IsEnabled = true;
                    addWaypoint.IsEnabled = true;
                    clearRoute.IsEnabled = true;
                }

                // get a list of characters in this system
                List<LocalCharacter> charactersInSystem = new List<LocalCharacter>();
                foreach(LocalCharacter lc in EM.LocalCharacters)
                {
                    if(lc.Location == selectedSys.Name)
                    {
                        charactersInSystem.Add(lc);
                    }
                }

                if(charactersInSystem.Count > 0)
                {
                    characters.IsEnabled = true;
                    characters.Visibility = Visibility.Visible;

                    foreach(LocalCharacter lc in charactersInSystem)
                    {
                        MenuItem miChar = new MenuItem();
                        miChar.Header = lc.Name;
                        characters.Items.Add(miChar);

                        // Use zh-CN menu/popup strings when that UI language is active
                        bool isZH = SMT.EVEData.EveManager.CurrentLanguage == "zh-CN";

                        // now create the child menu's
                        MenuItem miAutoRange = new MenuItem();
                        miAutoRange.Header = isZH ? "自动跳跃范围" : "Auto Jump Range";
                        miAutoRange.DataContext = lc;
                        miChar.Items.Add(miAutoRange);

                        MenuItem miARNone = new MenuItem();
                        miARNone.Header = isZH ? "无" : "None";
                        miARNone.DataContext = "0";
                        miARNone.Click += characterRightClickAutoRange_Clicked;
                        miAutoRange.Items.Add(miARNone);

                        MenuItem miARSuper = new MenuItem();
                        miARSuper.Header = isZH ? "超级航母/泰坦 (6.0LY)" : "Super/Titan  (6.0LY)";
                        miARSuper.DataContext = "6";
                        miARSuper.Click += characterRightClickAutoRange_Clicked;
                        miAutoRange.Items.Add(miARSuper);

                        MenuItem miARCF = new MenuItem();
                        miARCF.Header = isZH ? "航母/无畏/传母 (7.0LY)" : "Carriers/Fax (7.0LY)";
                        miARCF.DataContext = "7";
                        miARCF.Click += characterRightClickAutoRange_Clicked;
                        miAutoRange.Items.Add(miARCF);

                        MenuItem miARCC = new MenuItem();
                        miARCC.Header = "Command Carriers (7.5LY)";
                        miARCC.DataContext = "7.5";
                        miARCC.Click += characterRightClickAutoRange_Clicked;
                        miAutoRange.Items.Add(miARCC);


                        MenuItem miARBlops = new MenuItem();
                        miARBlops.Header = isZH ? "黑隐特勤舰 (8.0LY)" : "Black Ops    (8.0LY)";
                        miARBlops.DataContext = "8";
                        miARBlops.Click += characterRightClickAutoRange_Clicked;
                        miAutoRange.Items.Add(miARBlops);

                        MenuItem miARJFR = new MenuItem();
                        miARJFR.Header = isZH ? "跳货/大鲸鱼 (10.0LY)" : "JF/Rorq     (10.0LY)";
                        miARJFR.DataContext = "10";
                        miARJFR.Click += characterRightClickAutoRange_Clicked;
                        miAutoRange.Items.Add(miARJFR);

                        if (!string.IsNullOrEmpty(lc.GameLogWarningText))
                        {
                            MenuItem miRemoveWarning = new MenuItem();
                            miRemoveWarning.Header = isZH ? "清除警告" : "Clear Warning";
                            miRemoveWarning.DataContext = lc;
                            miRemoveWarning.Click += characterRightClickClearWarning;
                            miChar.Items.Add(miRemoveWarning);
                        }
                    }
                }

                cm.IsOpen = true;
            }
        }

        private void characterRightClickClearWarning(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;

            LocalCharacter lc = mi.DataContext as LocalCharacter;
            if(lc != null)
            {
                lc.GameLogWarningText = "";
            }
        }

        /// <summary>
        /// Shape (ie System) Mouse over handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ShapeMouseOverHandler(object sender, MouseEventArgs e)
        {
            Shape obj = sender as Shape;

            EVEData.MapSystem selectedSys = obj.DataContext as EVEData.MapSystem;

            Thickness one = new Thickness(1);

            if(obj.IsMouseOver && MapConf.ShowSystemPopup)
            {
                SystemInfoPopup.PlacementTarget = obj;
                SystemInfoPopup.VerticalOffset = 5;
                SystemInfoPopup.HorizontalOffset = 15;
                SystemInfoPopup.DataContext = selectedSys.ActualSystem;

                // --- Modern Space Dark popup ---

                SystemInfoPopupSP.Background = Brushes.Transparent;

                SystemInfoPopupSP.Children.Clear();



                // colour tokens

                Color popupFg      = Color.FromRgb(0xe6, 0xed, 0xf3); // #e6edf3 primary text

                Color popupMuted   = Color.FromRgb(0x8b, 0x94, 0x9e); // #8b949e muted label

                Color popupAccent  = Color.FromRgb(0x58, 0xa6, 0xff); // #58a6ff accent blue

                Color popupBorder  = Color.FromArgb(0x40, 0xff, 0xff, 0xff);



                Brush fgBrush     = new SolidColorBrush(popupFg);
                fgBrush.Freeze();

                Brush mutedBrush  = new SolidColorBrush(popupMuted);
                mutedBrush.Freeze();

                Brush accentBrush = new SolidColorBrush(popupAccent);
                accentBrush.Freeze();



                // helper: add a key/value row

                void AddRow(string key, string val, Brush valBrush = null)

                {

                    Grid row = new Grid { Margin = new Thickness(12, 3, 12, 3) };

                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });



                    TextBlock keyTb = new TextBlock

                    {

                        Text = key,

                        Foreground = mutedBrush,

                        FontSize = 11,

                        VerticalAlignment = VerticalAlignment.Center,

                    };

                    TextBlock valTb = new TextBlock

                    {

                        Text = val,

                        Foreground = valBrush ?? fgBrush,

                        FontSize = 11,

                        VerticalAlignment = VerticalAlignment.Center,

                        FontWeight = FontWeights.Medium,

                    };

                    Grid.SetColumn(keyTb, 0);

                    Grid.SetColumn(valTb, 1);

                    row.Children.Add(keyTb);

                    row.Children.Add(valTb);

                    SystemInfoPopupSP.Children.Add(row);

                }



                // helper: thin separator line

                void AddDivider()

                {

                    Rectangle div = new Rectangle

                    {

                        Height = 1,

                        Margin = new Thickness(8, 4, 8, 4),

                        Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xff, 0xff, 0xff)),

                    };

                    SystemInfoPopupSP.Children.Add(div);

                }



                // --- Header ---

                Border headerBg = new Border

                {

                    Background       = new SolidColorBrush(Color.FromArgb(0x50, 0x1f, 0x6f, 0xeb)),

                    CornerRadius     = new CornerRadius(5, 5, 0, 0),

                    Padding          = new Thickness(12, 8, 12, 8),

                };

                TextBlock headerTb = new TextBlock

                {

                    Text       = selectedSys.LocalizedName,

                    FontSize   = 14,

                    FontWeight = FontWeights.SemiBold,

                    Foreground = new SolidColorBrush(Color.FromRgb(0xe6, 0xed, 0xf3)),

                };

                headerBg.Child = headerTb;

                SystemInfoPopupSP.Children.Add(headerBg);



                // spacer

                SystemInfoPopupSP.Children.Add(new Border { Height = 4 });

                // Use zh-CN popup labels when that UI language is active

                bool isZH = SMT.EVEData.EveManager.CurrentLanguage == "zh-CN";



                // --- Characters in this system ---

                List<string> charNames = new List<string>();

                foreach(LocalCharacter c in EM.LocalCharacters)

                {

                    if(c.Location == selectedSys.Name)

                    {

                        string cname = c.Name + (c.IsOnline ? "" : (isZH ? " (离线)" : " (Offline)"));

                        charNames.Add(cname);

                    }

                }

                charNames.Sort();

                if(charNames.Count > 0)

                {

                    foreach(string s in charNames)

                        AddRow(isZH ? "角色" : "Pilot", s, accentBrush);

                    AddDivider();

                }



                // --- System info ---

                AddRow(isZH ? "星座" : "Const", selectedSys.ActualSystem.ConstellationName);



                // Security: colour by sec type

                string secStr = $"{selectedSys.ActualSystem.TrueSec:0.00}  {selectedSys.ActualSystem.SecType}";

                Brush secBrush;

                double trueSec = selectedSys.ActualSystem.TrueSec;

                if(trueSec >= 0.5)

                    secBrush = new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50));   // green

                else if(trueSec > 0.0)

                    secBrush = new SolidColorBrush(Color.FromRgb(0xf0, 0x88, 0x3e));   // orange

                else

                    secBrush = new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49));   // red

                AddRow(isZH ? "安全等级" : "Security", secStr, secBrush);



                // --- Kill / jump stats ---

                bool hasStats = selectedSys.ActualSystem.ShipKillsLastHour != 0

                             || selectedSys.ActualSystem.PodKillsLastHour  != 0

                             || selectedSys.ActualSystem.NPCKillsLastHour  != 0

                             || selectedSys.ActualSystem.JumpsLastHour     != 0;

                if(hasStats)

                {

                    AddDivider();

                    Brush dangerBrush = new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49));

                    if(selectedSys.ActualSystem.ShipKillsLastHour != 0)

                        AddRow(isZH ? "舰船击杀" : "Ship Kills", selectedSys.ActualSystem.ShipKillsLastHour.ToString(), dangerBrush);

                    if(selectedSys.ActualSystem.PodKillsLastHour != 0)

                        AddRow(isZH ? "太空舱击杀" : "Pod Kills",  selectedSys.ActualSystem.PodKillsLastHour.ToString(), dangerBrush);

                    if(selectedSys.ActualSystem.NPCKillsLastHour != 0)
                    {
                        int npcKills = selectedSys.ActualSystem.NPCKillsLastHour;
                        int npcDelta = selectedSys.ActualSystem.NPCKillsDeltaLastHour;

                        // delta: increase = bad (red), decrease = good (green), zero = neutral
                        string deltaSymbol;
                        Brush  deltaBrush;
                        if(npcDelta > 0)
                        {
                            deltaSymbol = "▲ " + npcDelta.ToString();
                            deltaBrush  = new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49));
                        }
                        else if(npcDelta < 0)
                        {
                            deltaSymbol = "▼ " + Math.Abs(npcDelta).ToString();
                            deltaBrush  = new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50));
                        }
                        else
                        {
                            deltaSymbol = "—";
                            deltaBrush  = mutedBrush;
                        }

                        Grid npcRow = new Grid { Margin = new Thickness(12, 3, 12, 3) };
                        npcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                        npcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        TextBlock npcKeyTb = new TextBlock
                        {
                            Text = isZH ? "NPC 击杀" : "NPC Kills",
                            Foreground = mutedBrush,
                            FontSize = 11,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        TextBlock npcValTb = new TextBlock
                        {
                            FontSize = 11,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontWeight = FontWeights.Medium,
                        };
                        npcValTb.Inlines.Add(new System.Windows.Documents.Run(npcKills.ToString()) { Foreground = fgBrush });
                        npcValTb.Inlines.Add(new System.Windows.Documents.Run("  "));
                        npcValTb.Inlines.Add(new System.Windows.Documents.Run(deltaSymbol) { Foreground = deltaBrush });
                        Grid.SetColumn(npcKeyTb, 0);
                        Grid.SetColumn(npcValTb, 1);
                        npcRow.Children.Add(npcKeyTb);
                        npcRow.Children.Add(npcValTb);
                        SystemInfoPopupSP.Children.Add(npcRow);
                    }
                    if(selectedSys.ActualSystem.JumpsLastHour != 0)

                        AddRow(isZH ? "跳跃数" : "Jumps", selectedSys.ActualSystem.JumpsLastHour.ToString());

                }



                // --- Jump bridges ---

                if(ShowJumpBridges)

                {

                    Point from = new Point();

                    Point to = new Point();

                    bool AddJBHighlight = false;
                    int JBZone = 0;
                    long targetSystemID = 0;



                    bool jbSectionOpen  = false;



                    foreach(EVEData.JumpBridge jb in EM.JumpBridges)

                    {

                        string jbTarget = null;

                        if(selectedSys.Name == jb.From) jbTarget = jb.To;

                        else if(selectedSys.Name == jb.To) jbTarget = jb.From;



                        if(jbTarget != null)

                        {

                            if(!jbSectionOpen) { AddDivider(); jbSectionOpen = true; }

                            targetSystemID = EM.GetEveSystem(jbTarget).ID;

                            string jbLabel = jbTarget;

                            if(!Region.IsSystemOnMap(jbTarget))

                            {

                                EVEData.System jbSys = EM.GetEveSystem(jbTarget);

                                if(jbSys != null) jbLabel += $"  ({jbSys.Region})";

                            }

                            AddRow("JB →", jbLabel, accentBrush);



                            from.X = selectedSys.Layout.X;

                            from.Y = selectedSys.Layout.Y;

                            bool isFrom = (selectedSys.Name == jb.From);

                            string mapTarget = isFrom ? jb.To : jb.From;

                            if(Region.IsSystemOnMap(mapTarget) && !jb.Disabled)

                            {

                                MapSystem ms = Region.MapSystems[mapTarget];

                                to.X = ms.Layout.X;

                                to.Y = ms.Layout.Y;

                                AddJBHighlight = true;

                            }

                        }

                    }


                    if(targetSystemID != 0)
                    {
                        EVEData.System targetSystem = EM.GetEveSystemFromID(targetSystemID);
                        if(targetSystem != null)
                        {
                            if(targetSystem.SOVAllianceID != 0)
                            {
                                // get the alliance capital
                                if(EM.IDToAlliance.ContainsKey(targetSystem.SOVAllianceID))
                                {
                                    EVEData.Alliance a = EM.IDToAlliance[targetSystem.SOVAllianceID];
                                    if(a.CapitalSystemID != 0)
                                    {
                                        // calculate the distance between the target system and the alliance capital
                                        EVEData.System capitalSystem = EM.GetEveSystemFromID(a.CapitalSystemID);
                                        if(capitalSystem != null)
                                        {
                                            Decimal distance = EM.GetRangeBetweenSystems(targetSystem.Name, capitalSystem.Name);
                                            {
                                                JBZone = 5;
                                                if(distance <= 20)
                                                {
                                                    JBZone = 4;
                                                }
                                                if(distance <= 15)
                                                {
                                                    JBZone = 3;
                                                }
                                                if(distance <= 10)
                                                {
                                                    JBZone = 2;
                                                }
                                                if(distance <= 5)
                                                {
                                                    JBZone = 1;
                                                }

                                                Label jbzl = new Label();
                                                jbzl.Padding = one;
                                                jbzl.Margin = one;
                                                jbzl.Foreground = new SolidColorBrush(MapConf.ActiveColourScheme.PopupText);
                                                jbzl.Content = $"JB Zone\t: {JBZone}";
                                                SystemInfoPopupSP.Children.Add(jbzl);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if(AddJBHighlight)

                    {

                        Brush highlightBrush = new SolidColorBrush(Color.FromRgb(0x79, 0xc0, 0xff));



                        Line jbHighlight = new Line

                        {

                            X1 = from.X, Y1 = from.Y,

                            X2 = to.X,   Y2 = to.Y,

                            Stroke = highlightBrush,

                            StrokeThickness = 3,

                            IsHitTestVisible = false,

                            StrokeDashArray = new DoubleCollection { 1.0, 1.0 },

                            Visibility = Visibility.Visible,

                        };

                        DynamicMapElementsJBHighlight.Add(jbHighlight);

                        Canvas.SetZIndex(jbHighlight, 19);

                        MainCanvas.Children.Add(jbHighlight);



                        double circleSize   = 30;

                        double circleOffset = circleSize / 2;

                        Ellipse jbCircle = new Ellipse

                        {

                            Height = circleSize, Width = circleSize,

                            Stroke = highlightBrush, StrokeThickness = 1.5,

                        };

                        Canvas.SetLeft(jbCircle, to.X - circleOffset);

                        Canvas.SetTop(jbCircle,  to.Y - circleOffset);

                        DynamicMapElementsJBHighlight.Add(jbCircle);

                        Canvas.SetZIndex(jbCircle, 19);

                        MainCanvas.Children.Add(jbCircle);

                    }

                }



                // --- Gate highlights ---

                {

                    Brush NormalGateBrush        = new SolidColorBrush(MapConf.ActiveColourScheme.NormalGateColour);

                    Brush ConstellationGateBrush  = new SolidColorBrush(MapConf.ActiveColourScheme.ConstellationGateColour);

                    Brush RegionGateBrush         = new SolidColorBrush(MapConf.ActiveColourScheme.RegionGateColour);



                    foreach(string connection in selectedSys.ActualSystem.Jumps)

                    {

                        if(Region.MapSystems.ContainsKey(connection))

                        {

                            MapSystem s1 = Region.MapSystems[connection];

                            Brush linkBrush = NormalGateBrush;

                            if(selectedSys.ActualSystem.ConstellationID != s1.ActualSystem.ConstellationID)

                                linkBrush = ConstellationGateBrush;

                            if(selectedSys.ActualSystem.Region != s1.ActualSystem.Region)

                                linkBrush = RegionGateBrush;



                            Line sysLink = new Line

                            {

                                X1 = selectedSys.Layout.X, Y1 = selectedSys.Layout.Y,

                                X2 = s1.Layout.X,          Y2 = s1.Layout.Y,

                                Stroke = linkBrush, StrokeThickness = 4,

                            };

                            DynamicMapElementsSysLinkHighlight.Add(sysLink);

                            Canvas.SetZIndex(sysLink, 19);

                            MainCanvas.Children.Add(sysLink);

                        }

                    }

                }

                // --- Sov / IHUB ---
                if(selectedSys.ActualSystem.SovADM != 0.0f)
                {
                    AddDivider();

                    if(selectedSys.ActualSystem.SovVunerabliltyStart != default)
                    {
                        AddRow("IHUB", $"{selectedSys.ActualSystem.SovVunerabliltyStart:t} – {selectedSys.ActualSystem.SovVunerabliltyEnd:t}");
                    }

                    string admInfo = selectedSys.ActualSystem.SovADM.ToString();
                    if(selectedSys.ActualSystem.SovIsCapitalSystem)
                    {
                        admInfo += " (Capital)";
                    }
                    AddRow("ADM", admInfo);
                    AddRow(" - Strategy", selectedSys.ActualSystem.SovStrategyLevel.ToString());
                    AddRow(" - Military", selectedSys.ActualSystem.SovMilitaryLevel.ToString());
                    AddRow(" - Industry", selectedSys.ActualSystem.SovIndustyLevel.ToString());
                }


                // --- Infrastructure upgrades ---

                if(selectedSys.ActualSystem.InfrastructureUpgrades.Count > 0)

                {

                    AddDivider();

                    foreach(EVEData.InfrastructureUpgrade upgrade in selectedSys.ActualSystem.InfrastructureUpgrades.OrderBy(u => u.SlotNumber))

                    {

                        Brush upgBrush = new SolidColorBrush(upgrade.IsOnline

                            ? Color.FromRgb(0x3f, 0xb9, 0x50)

                            : Color.FromRgb(0x48, 0x4f, 0x58));

                        AddRow($"{upgrade.SlotNumber}.", $"{upgrade.DisplayName}  {upgrade.Status}", upgBrush);

                    }

                }



                // --- Thera / Turnur / Storms / POIs / Trig ---

                bool extraSection = false;

                List<TheraConnection>  currentTheraConnections  = EM.TheraConnections.ToList();

                List<TurnurConnection> currentTurnurConnections = EM.TurnurConnections.ToList();



                foreach(EVEData.TheraConnection tc in currentTheraConnections)

                {

                    if(selectedSys.Name == tc.System)

                    {

                        if(!extraSection) { AddDivider(); extraSection = true; }

                        AddRow("Thera ↓", tc.InSignatureID,  accentBrush);

                        AddRow("Thera ↑", tc.OutSignatureID, accentBrush);

                    }

                }

                foreach(EVEData.TurnurConnection tc in currentTurnurConnections)

                {

                    if(selectedSys.Name == tc.System)

                    {

                        if(!extraSection) { AddDivider(); extraSection = true; }

                        AddRow("Turnur ↓", tc.InSignatureID,  accentBrush);

                        AddRow("Turnur ↑", tc.OutSignatureID, accentBrush);

                    }

                }

                foreach(EVEData.Storm s in EM.MetaliminalStorms)

                {

                    if(selectedSys.Name == s.System)

                    {

                        if(!extraSection) { AddDivider(); extraSection = true; }

                        Brush stormBrush = new SolidColorBrush(Color.FromRgb(0xf0, 0x88, 0x3e));

                        AddRow(isZH ? "风暴" : "Storm", s.Type, stormBrush);

                    }

                }

                foreach(POI p in EM.PointsOfInterest)

                {

                    if(selectedSys.Name == p.System)

                    {

                        if(!extraSection) { AddDivider(); extraSection = true; }

                        AddRow(p.Type, p.ShortDesc, accentBrush);

                    }

                }

                if(MapConf.ShowTrigInvasions && selectedSys.ActualSystem.TrigInvasionStatus != EVEData.System.EdenComTrigStatus.None)

                {

                    if(!extraSection) { AddDivider(); extraSection = true; }

                    Brush trigBrush = new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49));

                    AddRow(isZH ? "入侵" : "Invasion", selectedSys.ActualSystem.TrigInvasionStatus.ToString(), trigBrush);

                }



                // bottom padding

                SystemInfoPopupSP.Children.Add(new Border { Height = 6 });



                // trigger the hover event

                if(SystemHoverEvent != null)

                    SystemHoverEvent(selectedSys.Name);



                SystemInfoPopup.IsOpen = true;
            }
            else
            {
                SystemInfoPopup.IsOpen = false;

                foreach(UIElement uie in DynamicMapElementsSysLinkHighlight)
                {
                    MainCanvas.Children.Remove(uie);
                }

                foreach(UIElement uie in DynamicMapElementsJBHighlight)
                {
                    MainCanvas.Children.Remove(uie);
                }

                // trigger the hover event

                if(SystemHoverEvent != null)
                {
                    SystemHoverEvent(string.Empty);
                }

                DynamicMapElementsJBHighlight.Clear();
                DynamicMapElementsSysLinkHighlight.Clear();
            }
        }

        /// <summary>
        /// Add Waypoint Clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SysContexMenuItemAddWaypoint_Click(object sender, RoutedEventArgs e)
        {
            EVEData.MapSystem eveSys = ((System.Windows.FrameworkElement)((System.Windows.FrameworkElement)sender).Parent).DataContext as EVEData.MapSystem;
            if(ActiveCharacter != null)
            {
                ActiveCharacter.AddDestination(eveSys.ActualSystem.ID, false);
            }
        }

        private void SysContexMenuItemAddWaypointAll_Click(object sender, RoutedEventArgs e)
        {
            EVEData.MapSystem eveSys = ((System.Windows.FrameworkElement)((System.Windows.FrameworkElement)sender).Parent).DataContext as EVEData.MapSystem;
            foreach(LocalCharacter lc in EM.LocalCharacters)
            {
                if(lc.IsOnline && lc.ESILinked)
                {
                    lc.AddDestination(eveSys.ActualSystem.ID, false);
                }
            }
        }

        /// <summary>
        /// Ckear Route  Clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SysContexMenuItemClearRoute_Click(object sender, RoutedEventArgs e)
        {
            if(ActiveCharacter != null)
            {
                ActiveCharacter.ClearAllWaypoints();
            }
        }

        /// <summary>
        /// Copy Clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SysContexMenuItemCopy_Click(object sender, RoutedEventArgs e)
        {
            EVEData.MapSystem eveSys = ((System.Windows.FrameworkElement)((System.Windows.FrameworkElement)sender).Parent).DataContext as EVEData.MapSystem;

            try
            {
                if(eveSys != null)
                {
                    Clipboard.SetText(eveSys.Name);
                }
            }
            catch { }
        }

        private void SysContexMenuItemCopyEncoded_Click(object sender, RoutedEventArgs e)
        {
            EVEData.MapSystem eveSys = ((System.Windows.FrameworkElement)((System.Windows.FrameworkElement)sender).Parent).DataContext as EVEData.MapSystem;

            try
            {
                if(eveSys != null)
                {
                    Clipboard.SetText($"<url=showinfo:5//{eveSys.ActualSystem.ID}>{eveSys.Name}</url>");
                }
            }
            catch { }
        }



        /// <summary>
        /// Dotlan Clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SysContexMenuItemDotlan_Click(object sender, RoutedEventArgs e)
        {
            EVEData.MapSystem eveSys = ((System.Windows.FrameworkElement)((System.Windows.FrameworkElement)sender).Parent).DataContext as EVEData.MapSystem;
            EVEData.MapRegion rd = EM.GetRegion(eveSys.Region);

            string uRL = string.Format("http://evemaps.dotlan.net/map/{0}/{1}", rd.DotLanRef, eveSys.Name);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uRL) { UseShellExecute = true });
        }

        /// <summary>
        /// Set Destination Clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SysContexMenuItemSetDestination_Click(object sender, RoutedEventArgs e)
        {
            EVEData.MapSystem eveSys = ((System.Windows.FrameworkElement)((System.Windows.FrameworkElement)sender).Parent).DataContext as EVEData.MapSystem;
            if(ActiveCharacter != null)
            {
                ActiveCharacter.AddDestination(eveSys.ActualSystem.ID, true);
            }
        }

        private void SysContexMenuItemSetDestinationAll_Click(object sender, RoutedEventArgs e)
        {
            EVEData.MapSystem eveSys = ((System.Windows.FrameworkElement)((System.Windows.FrameworkElement)sender).Parent).DataContext as EVEData.MapSystem;
            foreach(LocalCharacter lc in EM.LocalCharacters)
            {
                if(lc.IsOnline && lc.ESILinked)
                {
                    lc.AddDestination(eveSys.ActualSystem.ID, true);
                }
            }
        }

        private void SysContexMenuItemShowInUniverse_Click(object sender, RoutedEventArgs e)
        {
            EVEData.MapSystem eveSys = ((System.Windows.FrameworkElement)((System.Windows.FrameworkElement)sender).Parent).DataContext as EVEData.MapSystem;

            RoutedEventArgs newEventArgs = new RoutedEventArgs(UniverseSystemSelectEvent, eveSys.Name);
            RaiseEvent(newEventArgs);
        }

        /// <summary>
        /// ZKillboard Clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SysContexMenuItemZKB_Click(object sender, RoutedEventArgs e)
        {
            EVEData.MapSystem eveSys = ((System.Windows.FrameworkElement)((System.Windows.FrameworkElement)sender).Parent).DataContext as EVEData.MapSystem;
            EVEData.MapRegion rd = EM.GetRegion(eveSys.Region);

            string uRL = string.Format("https://zkillboard.com/system/{0}/", eveSys.ActualSystem.ID);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uRL) { UseShellExecute = true });
        }

        private void SystemDropDownAC_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EVEData.MapSystem sd = SystemDropDownAC.SelectedItem as EVEData.MapSystem;

            if(sd != null)
            {
                SelectSystem(sd.Name);
                ReDrawMap(false);
            }
        }

        /// <summary>
        /// UI Refresh Timer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UiRefreshTimer_Tick(object sender, EventArgs e)
        {
            if(!IsVisible)
            {
                return;
            }

            if(currentJumpCharacter != "")
            {
                foreach(LocalCharacter c in EM.LocalCharacters)
                {
                    if(c.Name == currentJumpCharacter)
                    {
                        currentCharacterJumpSystem = c.Location;
                    }
                }
            }

            ReDrawMap(false);
        }

        private struct GateHelper
        {
            public EVEData.MapSystem from { get; set; }
            public EVEData.MapSystem to { get; set; }
        }
    }
}
