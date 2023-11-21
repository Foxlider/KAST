using KAST.Core;
using KAST.Core.Services;
using KAST.Data;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>(); 
builder.Services.AddSingleton<ServerInfoService>();
builder.Services.AddMudServices();


builder.Services.AddDbContext<ApplicationDbContext>();



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

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();