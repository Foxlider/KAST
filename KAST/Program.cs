using KAST.Components;
using KAST.Core.Services;
using KAST.Data;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace KAST
{
    public class Program
    {
        const string serviceName = "KAST";

        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add MudBlazor services
            builder.Services.AddMudServices();

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Logging.AddOpenTelemetry(options =>
            {
                options
                    .SetResourceBuilder(
                        ResourceBuilder.CreateDefault()
                            .AddService(serviceName))
                    .AddOtlpExporter();
            });
            builder.Services.AddOpenTelemetry()
                  .ConfigureResource(resource => resource.AddService(serviceName))
                  .WithTracing(tracing => tracing
                        .AddSource("ConfigFileService")
                        .AddSource("KeyValueConfigFormat")
                        .AddSource("ClassHierarchyConfigFormat")
                        //.AddAspNetCoreInstrumentation()
                        .AddEntityFrameworkCoreInstrumentation()
                        //.AddHttpClientInstrumentation()
                        .AddOtlpExporter())
                  .WithMetrics(metrics => metrics
                        .AddMeter("*")
                        //.AddHttpClientInstrumentation()
                        //.AddAspNetCoreInstrumentation()
                        .AddOtlpExporter());

            builder.Services.AddDbContext<ApplicationDbContext>();

            builder.Services.AddSingleton<ServerInfoService>();
            builder.Services.AddScoped<InstanceManagerService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            using (IServiceScope serviceScope = app.Services.CreateScope())
            {
                //Apply last Entity Framework migration
                try
                {
                    ApplicationDbContext context = serviceScope.ServiceProvider.GetService<ApplicationDbContext>();

                    if (app.Environment.IsDevelopment())
                    {
                        context.Database.EnsureDeleted();
                        context.Database.EnsureCreated();
                    }
                    else
                        context.Database.Migrate();

                    context.EnsureSeedData();
                }
                catch (Exception) { throw; }
            }

            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            await app.RunAsync();
        }
    }
}