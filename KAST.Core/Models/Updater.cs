using BytexDigital.Steam.ContentDelivery;
using BytexDigital.Steam.ContentDelivery.Exceptions;
using BytexDigital.Steam.Core;
using SteamKit2.Internal;
using System.Configuration;

namespace KAST.Core.Models
{
    public sealed class Updater
    {
        public Updater()
        { Parameters = new SteamUpdaterModel(); DbContext = new(); }

        private Updater(SteamUpdaterModel model)
        { Parameters = model; DbContext = new(); }

        public static Updater Instance => lazy.Value;
        private static readonly Lazy<Updater>
            lazy =
                new(() => new Updater(new SteamUpdaterModel()));

        public SteamUpdaterModel Parameters { get; set; }

        public KastContext DbContext;

        internal SteamClient SteamClient;
        internal SteamContentClient SteamContentClient;
        private SteamCredentials _steamCredentials;
        private SteamAuthenticationCodesProvider authProvider;


        public async Task<PublishedFileDetails> GetModInfo(ulong id)
        {
            if (!await SteamLogin())
            { throw new KastLogonFailedException(); }

            return await SteamContentClient.GetPublishedFileDetailsAsync(id); 
        }


        internal async Task<bool> SteamLogin()
        {
            var path = Path.Combine(Utilities.GetAppDataPath() ?? string.Empty, "sentries");
            SteamAuthenticationFilesProvider sentryFileProvider = new DirectorySteamAuthenticationFilesProvider(path);
            if (_steamCredentials == null || _steamCredentials.IsAnonymous)
                _steamCredentials = new SteamCredentials(Parameters.Username, Parameters.Password, Parameters.ApiKey);

            if (authProvider == null)
                authProvider = new AuthCodeProvider();

            SteamClient ??= new SteamClient(_steamCredentials, authProvider, sentryFileProvider);

            if (!SteamClient.IsConnected || SteamClient.IsFaulted)
            {
                Console.WriteLine($"Connecting to Steam as {(_steamCredentials.IsAnonymous ? "anonymous" : _steamCredentials.Username)}");
                SteamClient.MaximumLogonAttempts = 3;
                CancellationTokenSource cs = new();
                cs.CancelAfter(3000);

                try
                { await SteamClient.ConnectAsync(cs.Token); }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nFailed! Error: {ex.Message}");
                    SteamClient.Shutdown();
                    SteamClient.Dispose();
                    SteamClient = null;

                    if (ex.GetBaseException() is SteamLogonException { Result: SteamKit2.EResult.InvalidPassword })
                    {
                        Console.WriteLine("\nWarning: The logon may have failed due to expired sentry-data."
                                             + $"\nIf you are sure that the provided username and password are correct, consider deleting the .bin and .key file for the user \"{SteamClient?.Credentials.Username}\" in the sentries directory."
                                             + $"{path}");
                    }
                    return false;
                }
            }

            SteamContentClient = new SteamContentClient(SteamClient, Parameters.CliWorkers);
            Console.WriteLine("\nConnected !");
            return SteamClient.IsConnected;
        }

        public void SetAuthProvider(SteamAuthenticationCodesProvider provider)
        {
            authProvider = provider;
        }
    }
    public class AuthCodeProvider : SteamAuthenticationCodesProvider
    {
        public override string GetEmailAuthenticationCode(SteamCredentials steamCredentials)
        {
            Console.WriteLine("\nPlease enter your email auth code: ");


            var input = Console.ReadLine();

            Console.WriteLine("\nRetrying... ");

            return input;
        }

        public override string GetTwoFactorAuthenticationCode(SteamCredentials steamCredentials)
        {
            Console.WriteLine("\nPlease enter your 2FA code: ");

            var input = Console.ReadLine();

            Console.WriteLine("\nRetrying... ");

            return input;
        }
    }

    public class SteamUpdaterModel
    {
        private string _output;
        private bool _isUpdating;
        private double _progress;
        private KastContext _context;

        private KastContext Context
        {
            get { 
                if (_context == null)
                    _context = new();
                return _context; 
            }
        }

        public string InstallDirectory
        {
            get => Context.Settings.First().ArmaPath;
            set
            {
                Context.Settings.First().ArmaPath = value;
                Context.SaveChanges();
            }
        }

        public string Username
        {
            get => Context.Users.First().Login;
            set
            {
                Context.Users.First().Login = value;
                Context.SaveChanges();
            }
        }

        public string Password
        {
            get => Context.Users.First().Pass;
            set
            {
                Context.Users.First().Pass = value;
                Context.SaveChanges();
            }
        }

        public string ModStagingDirectory
        {
            get => Context.Settings.First().ModStagingDir;
            set
            {
                Context.Settings.First().ModStagingDir = value;
                Context.SaveChanges();
            }
        }

        //public bool UsingPerfBinaries
        //{
        //    get => Settings.Default.usingPerfBinaries;
        //    set
        //    {
        //        Settings.Default.usingPerfBinaries = value;
        //        Settings.Default.Save();
        //        RaisePropertyChanged(nameof(UsingPerfBinaries));
        //    }
        //}

        //public bool UsingContactDlc
        //{
        //    get => Settings.Default.usingContactDlc;
        //    set
        //    {
        //        Settings.Default.usingContactDlc = value;
        //        Settings.Default.Save();
        //        RaisePropertyChanged(nameof(UsingContactDlc));
        //    }
        //}

        //public bool UsingGMDlc
        //{
        //    get => Settings.Default.usingGMDlc;
        //    set
        //    {
        //        Settings.Default.usingGMDlc = value;
        //        Settings.Default.Save();
        //        RaisePropertyChanged(nameof(UsingGMDlc));
        //    }
        //}

        //public bool UsingPFDlc
        //{
        //    get => Settings.Default.usingPFDlc;
        //    set
        //    {
        //        Settings.Default.usingPFDlc = value;
        //        Settings.Default.Save();
        //        RaisePropertyChanged(nameof(UsingPFDlc));
        //    }
        //}

        //public bool UsingCLSADlc
        //{
        //    get => Settings.Default.usingCLSADlc;
        //    set
        //    {
        //        Settings.Default.usingCLSADlc = value;
        //        Settings.Default.Save();
        //        RaisePropertyChanged(nameof(UsingCLSADlc));
        //    }
        //}

        //public bool UsingWSDlc
        //{
        //    get => Settings.Default.usingWSDlc;
        //    set
        //    {
        //        Settings.Default.usingWSDlc = value;
        //        Settings.Default.Save();
        //        RaisePropertyChanged(nameof(UsingWSDlc));
        //    }
        //}


        public string ApiKey
        {
            get => !string.IsNullOrEmpty(Context.Settings.FirstOrDefault()?.ApiKey)
                       ? Context.Settings.First().ApiKey
                       : Statics.SteamApiKey;
            set
            {
                Context.Settings.First().ApiKey = value;
                Context.SaveChanges();
            }
        }

        public ushort CliWorkers
        {
            get => Context.Settings.First().CliWorkers != null
                       ? (ushort)Context.Settings.First().CliWorkers
                       : Statics.CliWorkers;
            set
            {
                Context.Settings.First().CliWorkers = value;
                Context.SaveChanges();
            }
        }
    }
}
