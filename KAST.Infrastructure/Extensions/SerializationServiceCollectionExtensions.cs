using KAST.Application.Common.Interfaces.Serialization;
using KAST.Infrastructure.Services.Serialization;

namespace KAST.Infrastructure.Extensions
{
    public static class SerializationServiceCollectionExtensions
    {
        public static IServiceCollection AddSerialization(this IServiceCollection services)
            => services.AddSingleton<ISerializer, SystemTextJsonSerializer>();
    }
}