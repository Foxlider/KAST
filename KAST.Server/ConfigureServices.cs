using Blazor.Analytics;
using KAST.Server.Services;
using KAST.Server.Services.Navigation;
using KAST.Server.Services.Notifications;
using BlazorDownloadFile;
using Blazored.LocalStorage;
using MudBlazor;
using MudBlazor.Services;
using Toolbelt.Blazor.Extensions.DependencyInjection;

namespace KAST.Server
{
    public static class ConfigureServices
    {
        public static IServiceCollection AddBlazorUIServices(this IServiceCollection services)
        {
            services.AddMudBlazorDialog();
            services.AddServerSideBlazor(
                options =>
                {
                    options.DetailedErrors = true;
                    options.DisconnectedCircuitMaxRetained = 100;
                    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
                    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
                    options.MaxBufferedUnacknowledgedRenderBatches = 10;
                }
                ).AddHubOptions(options =>
                {
                    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
                    options.EnableDetailedErrors = false;
                    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
                    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                    options.MaximumParallelInvocationsPerClient = 1;
                    options.MaximumReceiveMessageSize = 32 * 1024;
                    options.StreamBufferCapacity = 10;
                })
                .AddCircuitOptions(option => { option.DetailedErrors = true; });
            services.AddHotKeys();
            services.AddBlazoredLocalStorage();
            services.AddMudServices(config =>
            {
                config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
                config.SnackbarConfiguration.PreventDuplicates = false;
                config.SnackbarConfiguration.NewestOnTop = true;
                config.SnackbarConfiguration.ShowCloseIcon = true;
                config.SnackbarConfiguration.VisibleStateDuration = 4000;
                config.SnackbarConfiguration.HideTransitionDuration = 500;
                config.SnackbarConfiguration.ShowTransitionDuration = 500;
                config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
            });



            services.AddScoped<LayoutService>();
            services.AddBlazorDownloadFile();
            services.AddScoped<IUserPreferencesService, UserPreferencesService>();
            services.AddScoped<IMenuService, MenuService>();
            services.AddScoped<INotificationService, InMemoryNotificationService>();
            //services.AddGoogleAnalytics("G-PRYNCB61NV");

            return services;
        }
    }
}