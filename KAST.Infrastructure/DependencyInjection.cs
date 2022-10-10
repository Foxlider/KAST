// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Infrastructure.Extensions;
using KAST.Infrastructure.Persistence.Interceptors;
using Microsoft.Extensions.Configuration;

namespace KAST.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<AuditableEntitySaveChangesInterceptor>();
            if (configuration.GetValue<bool>("UseInMemoryDatabase"))
            {
                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseInMemoryDatabase("KAST.Dev");
                    options.EnableSensitiveDataLogging();
                });
            }
            else
            {
                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseSqlServer(
                          configuration.GetConnectionString("DefaultConnection"),
                          builder =>
                          {
                              builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                              builder.EnableRetryOnFailure(maxRetryCount: 5,
                                                           maxRetryDelay: TimeSpan.FromSeconds(10),
                                                           errorNumbersToAdd: null);
                              builder.CommandTimeout(15);
                          });
                    options.EnableDetailedErrors(detailedErrorsEnabled: true);
                    options.EnableSensitiveDataLogging();
                });
                services.AddDatabaseDeveloperPageExceptionFilter();
            }

            services.Configure<DashboardSettings>(configuration.GetSection(DashboardSettings.SectionName));
            services.Configure<AppConfigurationSettings>(configuration.GetSection(AppConfigurationSettings.SectionName));
            services.AddSingleton(s => s.GetRequiredService<IOptions<DashboardSettings>>().Value);
            services.AddScoped<IDbContextFactory<ApplicationDbContext>, BlazorContextFactory<ApplicationDbContext>>();
            services.AddTransient<IApplicationDbContext>(provider => provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());


            services
                .AddDefaultIdentity<ApplicationUser>()
                .AddRoles<ApplicationRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            services.AddLocalizationServices();
            services.AddServices()
                    .AddSerialization()
                    .AddMultiTenantService()
                    .AddMessageServices(configuration)
                    .AddSignalRServices();
            services.AddAuthenticationService(configuration);
            services.AddControllers();
            return services;
        }



    }
}