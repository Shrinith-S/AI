using Chitra.Application.Abstractions;
using Chitra.Application.Dtos;
using Microsoft.AspNetCore.SignalR;

namespace Chitra.Api.Hubs;

/// <summary>
/// Real-time channel for generation, export and model-download progress. Clients only receive events for their own
/// user; the server pushes and clients never need to call methods.
/// </summary>
public sealed class StudioHub(ICurrentUser currentUser) : Hub
{
    public const string Path = "/hubs/studio";

    public static string UserGroup(Guid userId) => $"user:{userId:N}";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(currentUser.UserId));
        await base.OnConnectedAsync();
    }
}

public sealed class SignalRNotifier(IHubContext<StudioHub> hub) : IRealtimeNotifier
{
    public Task GenerationUpdatedAsync(Guid userId, GenerationDto generation, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(StudioHub.UserGroup(userId)).SendAsync("generationUpdated", generation, cancellationToken);

    public Task ExportUpdatedAsync(Guid userId, VideoExportDto export, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(StudioHub.UserGroup(userId)).SendAsync("exportUpdated", export, cancellationToken);

    public Task ModelDownloadUpdatedAsync(ModelDownloadStatusDto status, CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendAsync("modelDownloadUpdated", status, cancellationToken);
}
