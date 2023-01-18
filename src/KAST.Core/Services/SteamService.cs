using BytexDigital.Steam.ContentDelivery;
using BytexDigital.Steam.Core;
using KAST.Core.Interfaces;
using KAST.Data;
using KAST.Data.Models;
using Microsoft.Extensions.DependencyInjection;

namespace KAST.Core.Services
{
    public class SteamService : ISteamService
    {
        SettingsService settings;

        SteamClient steamClient;
        SteamContentClient contentClient;

        public SteamService(SettingsService settingsService)
        {
            settings = settingsService;
            steamClient = new SteamClient(new SteamCredentials(settings.Account.AccountName, settings.Account.AccountPassword));
            contentClient = new SteamContentClient(steamClient);
        }

        public async Task<SteamMod> GetModInfo(ulong id)
        {
            var token = new CancellationTokenSource(5000);
            var itemInfo = await contentClient.GetPublishedFileDetailsAsync(id, token.Token);

            //Console.WriteLine($"Title: {itemInfo.title}");
            //Console.WriteLine($"IsInstalled: {itemInfo?.IsInstalled}");
            //Console.WriteLine($"IsDownloading: {itemInfo?.IsDownloading}");
            //Console.WriteLine($"IsDownloadPending: {itemInfo?.IsDownloadPending}");
            //Console.WriteLine($"IsSubscribed: {itemInfo?.IsSubscribed}");
            //Console.WriteLine($"NeedsUpdate: {itemInfo?.NeedsUpdate}");0
            //Console.WriteLine($"Description: {itemInfo?.Description}");

            var mod = new SteamMod() 
            { 
                Name = itemInfo?.filename,
                SteamID = id,
                Url = itemInfo?.url,
                SteamLastUpdated = UtilitiesService.UnixTimeStampToDateTime(itemInfo.time_updated),
            };
            return mod;
        }
    }
}
