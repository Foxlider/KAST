using BytexDigital.Steam.ContentDelivery;
using BytexDigital.Steam.ContentDelivery.Exceptions;
using BytexDigital.Steam.Core;
using KAST.Core.Models;
using SteamKit2.Internal;
using System.Diagnostics;

namespace KAST.Core.Managers
{
    /// <summary>
    /// Manager for Steam integration including App Updates and Workshop Updates. 
    /// </summary>
    public sealed class UpdateManager
    {
        internal SteamClient? SteamClient;
        internal SteamContentClient SteamContentClient;
        private SteamCredentials _steamCredentials;
        private SteamAuthenticationCodesProvider authProvider;

        private readonly KastContext context;

        public KastContext Context => context ?? new KastContext();

        public UpdateManager()
        {
            if(Context.Settings.FirstOrDefault() == null)
            {
                Context.Settings.Add(new Settings());
                Context.SaveChanges();
            }

            if (Context.Users.FirstOrDefault() == null)
            {
                Context.Users.Add(new User() { Login = "anonymous", Name = "Anonymous", Pass = "anonymous"});
                Context.SaveChanges();
            }
        }

        public UpdateManager(KastContext context)
        { this.context = context; }

        
        public string Username
        { 
            get { return Context.Users.FirstOrDefault()?.Login ?? "anonymous"; }
            set { Context.Users.First().Login = value;}
        }

        public string Password
        {
            get { return Context.Users.FirstOrDefault()?.Pass ?? "anonymous"; }
            set { Context.Users.First().Pass = value; }
        }

        public string ApiKey
        {
            get { return Context.Settings.FirstOrDefault()?.ApiKey ?? Statics.SteamApiKey; }
            set { Context.Settings.First().ApiKey = value; }
        }

        public int CliWorkers
        {
            get { return Context.Settings.FirstOrDefault()?.CliWorkers ?? Statics.CliWorkers; }
            set { Context.Settings.First().CliWorkers = value; }
        }

        /// <summary>
        /// Returns a mod's infos from Steam workshop
        /// </summary>
        /// <param name="id">Mod Workshop ID</param>
        /// <returns></returns>
        /// <exception cref="KastLogonFailedException">Thrown when the updater could not login</exception>
        public async Task<PublishedFileDetails> GetModInfo(ulong id)
        {
            if (!await SteamLogin())
                throw new KastLogonFailedException();

            if (SteamContentClient == null)
                throw new InvalidOperationException("Steam Content Client could not be created");

            return await SteamContentClient.GetPublishedFileDetailsAsync(id);
        }


        /// <summary>
        /// Update a mod to the ModStagingDir
        /// </summary>
        /// <param name="id">Mod Workshop ID</param>
        /// <returns></returns>
        /// <exception cref="KastLogonFailedException">Thrown when the updater could not login</exception>
        public async Task UpdateMod(ulong id)
        {
            if (!await SteamLogin())
                throw new KastLogonFailedException();

        }

        /// <summary>
        /// Provides login logic for the updater
        /// </summary>
        /// <returns></returns>
        internal async Task<bool> SteamLogin()
        {
            var path = Path.Combine(Utilities.GetAppDataPath() ?? string.Empty, "sentries");
            SteamAuthenticationFilesProvider sentryFileProvider = new DirectorySteamAuthenticationFilesProvider(path);
            if (_steamCredentials == null || _steamCredentials.IsAnonymous)
                _steamCredentials = new SteamCredentials(Username, Password, ApiKey);

            if (authProvider == null)
                authProvider = new AuthCodeProvider();

            SteamClient ??= new SteamClient(_steamCredentials, authProvider, sentryFileProvider);

            SteamClient.InternalClientConnected += () => Debug.WriteLine("Event: Connected");
            SteamClient.InternalClientDisconnected += () => Debug.WriteLine("Event: Disconnected");
            SteamClient.InternalClientLoggedOn += () => Debug.WriteLine("Event: Logged on");
            SteamClient.InternalClientLoggedOff += () => Debug.WriteLine("Event: Logged off");


            if (!SteamClient.IsConnected || SteamClient.IsFaulted)
            {
                Debug.WriteLine($"Connecting to Steam as {(_steamCredentials.IsAnonymous ? "anonymous" : _steamCredentials.Username)}");
                SteamClient.MaximumLogonAttempts = 3;

                try
                { await SteamClient.ConnectAsync(); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"\nFailed! Error: {ex.Message}");
                    SteamClient.Shutdown();
                    SteamClient.Dispose();
                    SteamClient = null;

                    if (ex.GetBaseException() is SteamLogonException { Result: SteamKit2.EResult.InvalidPassword })
                    {
                        Debug.WriteLine("\nWarning: The logon may have failed due to expired sentry-data."
                                             + $"\nIf you are sure that the provided username and password are correct, consider deleting the .bin and .key file for the user \"{SteamClient?.Credentials.Username}\" in the sentries directory."
                                             + $"{path}");
                    }
                    throw;
                }
            }

            SteamContentClient = new SteamContentClient(SteamClient, CliWorkers);
            Debug.WriteLine("\nConnected !");
            return true;
        }
        /// <summary>
        /// Auth Provider override
        /// </summary>
        /// <param name="provider"></param>
        public void SetAuthProvider(SteamAuthenticationCodesProvider provider)
        { authProvider = provider; }

    }


    public class AuthCodeProvider : SteamAuthenticationCodesProvider
    {
        public override string GetEmailAuthenticationCode(SteamCredentials steamCredentials)
        {
            Debug.WriteLine("\nPlease enter your email auth code: ");


            var input = Console.ReadLine() ?? "";

            Debug.WriteLine("\nRetrying... ");

            return input;
        }

        public override string GetTwoFactorAuthenticationCode(SteamCredentials steamCredentials)
        {
            Debug.WriteLine("\nPlease enter your 2FA code: ");

            var input = Console.ReadLine() ?? "";

            Debug.WriteLine("\nRetrying... ");

            return input;
        }
    }
}
