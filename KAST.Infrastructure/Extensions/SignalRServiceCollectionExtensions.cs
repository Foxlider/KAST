using KAST.Infrastructure.Hubs;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace KAST.Infrastructure.Extensions
{
    public static class SignalRServiceCollectionExtensions
    {
        public static void AddSignalRServices(this IServiceCollection services)
            => services.AddSingleton<IUsersStateContainer, UsersStateContainer>()
                       .AddScoped<CircuitHandler, CircuitHandlerService>()
                       .AddScoped<HubClient>()
                       .AddSignalR();
    }
}