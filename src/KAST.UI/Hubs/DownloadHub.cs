using KAST.Core.Events;
using Microsoft.AspNetCore.SignalR;

namespace KAST.UI.Hubs;

public class DownloadHub : Hub
{
    public async Task SubscribeToDownloads()
        => await Groups.AddToGroupAsync(Context.ConnectionId, "downloads");

    public async Task UnsubscribeFromDownloads()
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, "downloads");

    public static async Task BroadcastDownloadProgress(IHubContext<DownloadHub> hubContext, ModDownloadProgressEvent progress)
        => await hubContext.Clients.Group("downloads").SendAsync("DownloadProgress", progress);

    public static async Task BroadcastModStatusChanged(IHubContext<DownloadHub> hubContext, ModStatusChangedEvent status)
        => await hubContext.Clients.Group("downloads").SendAsync("ModStatusChanged", status);
}
