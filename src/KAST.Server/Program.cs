using KAST.Core.Services;
using KAST.Data;
using KAST.Server.Data;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using MudBlazor;
using MudBlazor.Services;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
            configuration.ReadFrom.Configuration(context.Configuration)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Error)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Error)
                .MinimumLevel.Override("Serilog", LogEventLevel.Error)
          .Enrich.FromLogContext()
          .Enrich.WithClientIp()
          .Enrich.WithClientAgent()
          .WriteTo.Console()
    );

StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

//Cookie Policy
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => true;
    options.MinimumSameSitePolicy = Microsoft.AspNetCore.Http.SameSiteMode.None;
});


// Add response caching for quicker responses
builder.Services.AddResponseCaching();


// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

//Entity Framework Core database context
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<KastDbContext>(options => options
    //.UseLoggerFactory(loggerFactory)
    .UseSqlServer(connectionString, sqlServerOptions => sqlServerOptions.CommandTimeout(60))

);


builder.Services.AddScoped<ModsService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<SteamService>();

builder.Services.AddMudServices();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-XSS-Protection", "1");
    context.Response.Headers.Add("Strict-Transport-Security", "max-age=63072000");
    context.Response.Headers.Add("X-Frame-Options", "SAMEORIGIN");
    context.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue() { Public = false, NoCache = true, };
    await next();
});

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
        KastDbContext context = serviceScope.ServiceProvider.GetService<KastDbContext>();
        SettingsService settings = serviceScope.ServiceProvider.GetService<SettingsService>();

        context.Database.SetCommandTimeout(2400);

        if (app.Environment.IsDevelopment())
        {
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
        }
        else
            context.Database.Migrate();

        context.EnsureSeedData();

        //Set default login details
        if (string.IsNullOrEmpty(settings.Account.AccountName)) 
        {
            settings.Account.AccountName = "anonymous";
            settings.Account.AccountPassword = "anonymous";
        }
        //Set Default Mod Staging Dir
        if(string.IsNullOrEmpty(settings.Mods.ModStatingDir)) 
        {
            settings.Mods.ModStatingDir = "./Mods";
        }

        if (string.IsNullOrEmpty(settings.Theme.PalettePrimary))
        {
            var provider = new MudThemeProvider();
            
            provider.Theme = new MudTheme();
            provider.GetSystemPreference();
            settings.Theme.PalettePrimary = provider.Theme.Palette.Primary.Value;
            settings.Theme.PaletteSecondary = provider.Theme.Palette.Secondary.Value;
            settings.Theme.PaletteBackground = provider.Theme.Palette.AppbarBackground.Value;
            settings.Theme.DarkTheme = provider.IsDarkMode;
        }


        settings.Save();


        settings.Save();
        if (app.Environment.IsDevelopment()) 
            context.DebugSeedData();
    } 
    catch (Exception) { throw; }
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseResponseCaching();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();