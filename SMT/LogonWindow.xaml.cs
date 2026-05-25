using System;
using System.Net;
using System.Threading.Tasks;
using System.Windows;

namespace SMT
{
    /// <summary>
    /// Interaction logic for LogonWindow.xaml
    /// </summary>
    public partial class LogonWindow : Window
    {
        private HttpListener listener;

        public LogonWindow()
        {
            InitializeComponent();
            new Task(StartServer).Start();
        }

        private bool serverDone = false;

        private void StartServer()
        {
            // create the http Server
            listener = new HttpListener();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string challengeCode = EVEDataUtils.Misc.RandomString(32);
            string esiLogonURL = EVEData.EveManager.Instance.GetESILogonURL(challengeCode);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(esiLogonURL) { UseShellExecute = true });

#if DEBUG
            string debugLogPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SMT", "logon_debug.log");
            try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(debugLogPath)); } catch { }
            Action<string> dlog = (msg) =>
            {
                try { System.IO.File.AppendAllText(debugLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
            };
#endif

            try
            {
                // HttpListener requires prefix to end with '/'
                string prefix = EVEData.EveAppConfig.CallbackURL;
                if (!prefix.EndsWith("/")) prefix += "/";
#if DEBUG
                dlog($"Adding prefix: {prefix}");
#endif
                listener.Prefixes.Add(prefix);
#if DEBUG
                dlog("Calling listener.Start()...");
#endif
                listener.Start();
#if DEBUG
                dlog($"listener.Start() OK. IsListening={listener.IsListening}");
#endif

                while (!serverDone)
                {
                    Console.WriteLine("Listening...");
#if DEBUG
                    dlog("Waiting for incoming request (GetContext)...");
#endif

                    // Note: The GetContext method blocks while waiting for a request.
                    HttpListenerContext context = listener.GetContext();
                    HttpListenerRequest request = context.Request;
#if DEBUG
                    dlog($"Got request: {request.HttpMethod} {request.Url}");
#endif

                    EVEData.EveManager.Instance.HandleEveAuthSMTUri(request.Url, challengeCode);

                    // Obtain a response object.
                    HttpListenerResponse response = context.Response;
                    // Construct a response.
                    string responseString = $"<HTML><HEAD title=\"SMT Auth\"><meta http-equiv=\"refresh\" content=\"1;url={esiLogonURL}\"></HEAD><BODY>SMT Character Added..</HTML>";

                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                    // Get a response stream and write the response to it.
                    response.ContentLength64 = buffer.Length;
                    System.IO.Stream output = response.OutputStream;
                    output.Write(buffer, 0, buffer.Length);
                }
            }
#if DEBUG
            catch (Exception ex)
            {
                string logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SMT", "logon_error.log");
                try
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                    System.IO.File.AppendAllText(logPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CallbackURL={EVEData.EveAppConfig.CallbackURL}\n" +
                        $"Exception: {ex.GetType().FullName}: {ex.Message}\n" +
                        $"StackTrace:\n{ex.StackTrace}\n\n");
                }
                catch { }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"ESI Logon HttpListener failed!\n\nCallback: {EVEData.EveAppConfig.CallbackURL}\n\n{ex.GetType().Name}: {ex.Message}\n\nLog: {logPath}",
                        "SMT Logon Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
#else
            catch
            {
            }
#endif
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                serverDone = true;

                if (listener != null && listener.IsListening)
                {
                    listener.Stop();
                }
            }
            catch
            {
            }
        }
    }
}