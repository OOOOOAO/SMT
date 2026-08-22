//-----------------------------------------------------------------------
// EVE App Config
//-----------------------------------------------------------------------

namespace SMT.EVEData
{
    public class EveAppConfig
    {
        #region Fields

        /// <summary>
        /// Callback URL for eve
        /// </summary>
        public const string CallbackURL = @"http://localhost:8080/eapi/smt-extension";

        /// <summary>
        /// Client ID from the EVE Developer setup
        /// </summary>
        public const string ClientID = "ID Goes Here";

        /// <summary>
        /// SMT Version Tagline
        /// </summary>
        public const string SMT_TITLE = "Command those carriers!";

        /// <summary>
        /// SMT Version
        /// </summary>
        public const string SMT_VERSION = "v1.0.1";


        /// <summary>
        /// SMT User Agent Details 
        /// </summary>
        public const string SMT_USERAGENT_DETAILS = " (+https://github.com/OOOOOAO/SMT; eve:OOOOOAO, discord:OOOOOAO)";


        /// <summary>
        /// Folder to store all of the data from
        /// </summary>
        public static readonly string StorageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OMT");


        /// <summary>
        /// Folder to store all of the data from
        /// </summary>
        public static readonly string VersionStorage = Path.Combine(StorageRoot, $"{SMT_VERSION}");

        #endregion Fields
    }
}
