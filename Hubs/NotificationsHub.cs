using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace JobRecruitment.Hubs
{
    [Authorize(Roles = "JobSeeker")]
    public class NotificationsHub : Hub
    {
        private readonly ILogger<NotificationsHub> _logger;
        public NotificationsHub(ILogger<NotificationsHub> logger) => _logger = logger;

        public override async Task OnConnectedAsync()
        {
            var uid = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            _logger.LogInformation(
                "NotificationsHub connected. ConnId={ConnId}, UserId={UserId}, Authenticated={Auth}",
                Context.ConnectionId,
                string.IsNullOrWhiteSpace(uid) ? "(null)" : uid,
                Context.User?.Identity?.IsAuthenticated ?? false
            );

            if (!string.IsNullOrWhiteSpace(uid))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, uid);

                // Send a small diagnostic so the client knows the socket is live.
                await Clients.Caller.SendAsync("applicationStatus", new
                {
                    applicationId = "DIAG",
                    status = "Connected",
                    title = "Diagnostics",
                    company = "",
                    whenUtc = DateTime.UtcNow,
                    link = "",
                    source = "HubConnected",
                    meta = new { note = "Hub connected & group joined", connectionId = Context.ConnectionId }
                });
            }
            else
            {
                _logger.LogWarning(
                    "No ClaimTypes.NameIdentifier found for ConnId={ConnId}. Check authentication & claims mapping.",
                    Context.ConnectionId
                );
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var uid = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            _logger.LogInformation(
                "NotificationsHub disconnected. ConnId={ConnId}, UserId={UserId}, Error={Error}",
                Context.ConnectionId,
                string.IsNullOrWhiteSpace(uid) ? "(null)" : uid,
                exception?.Message
            );

            if (!string.IsNullOrWhiteSpace(uid))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, uid);
            }

            await base.OnDisconnectedAsync(exception);
        }

        // Optional manual join (usually not needed because we do it in OnConnectedAsync).
        public Task Join(string userId) =>
            Groups.AddToGroupAsync(Context.ConnectionId, userId);

        // Simple test method you can call from the client to verify round-trip.
        public Task Ping(string? message) =>
            Clients.Caller.SendAsync("applicationStatus", new
            {
                applicationId = "PING",
                status = "Ping",
                title = string.IsNullOrWhiteSpace(message) ? "Ping" : message,
                company = "",
                whenUtc = DateTime.UtcNow,
                link = "",
                source = "HubPing"
            });
    }
}
