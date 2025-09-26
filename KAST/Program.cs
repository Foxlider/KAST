using KAST.Components;
using KAST.Core.Helpers;
using KAST.Core.Services;
using KAST.Data;
using KAST.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using System.Diagnostics;

namespace KAST
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.AddServiceDefaults();

            // Add MudBlazor services
            builder.Services.AddMudServices();

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddDbContext<ApplicationDbContext>();

            builder.Services.AddSingleton<ITracingNamingProvider, TracingNamingProvider>();
            builder.Services.AddSingleton<ITelemetryService, TelemetryService>();
            builder.Services.AddSingleton(new ActivitySource(builder.Environment.ApplicationName));
            builder.Services.AddSingleton<ServerInfoService>();
            builder.Services.AddScoped<InstanceManagerService>();
            builder.Services.AddScoped<ConfigService>();
            builder.Services.AddScoped<ModService>();
            builder.Services.AddScoped<ProfileService>();
            builder.Services.AddScoped<FileSystemService>(sp =>
            {
                var env = sp.GetRequiredService<IWebHostEnvironment>(); // Get environment
                return new FileSystemService(env.ContentRootPath);
            });

            var app = builder.Build();

            app.MapDefaultEndpoints();

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