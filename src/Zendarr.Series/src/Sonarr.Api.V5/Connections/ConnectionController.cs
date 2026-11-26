using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Notifications;
using NzbDrone.SignalR;
using Sonarr.Api.V5.Provider;
using Sonarr.Http;
using Sonarr.Http.Subsystem;

namespace Sonarr.Api.V5.Connections;

[V5ApiController]
[AppSubsystem(AppSubsystem.Series)]
public class ConnectionController : ProviderControllerBase<ConnectionResource, ConnectionBulkResource, INotification, NotificationDefinition>
{
    public static readonly ConnectionResourceMapper ResourceMapper = new();
    public static readonly ConnectionBulkResourceMapper BulkResourceMapper = new();

    private readonly INotificationFactory _notificationFactory;
    private readonly INotificationStatusService _notificationStatusService;

    public ConnectionController(IBroadcastSignalRMessage signalRBroadcaster,
        NotificationFactory notificationFactory,
        INotificationStatusService notificationStatusService)
        : base(signalRBroadcaster, notificationFactory, "connection", ResourceMapper, BulkResourceMapper)
    {
        _notificationFactory = notificationFactory;
        _notificationStatusService = notificationStatusService;
    }

    // Explicitly clear a failure backoff; testing a connection no longer records status.
    [HttpPost("{id:int}/reset")]
    [Produces("application/json")]
    public Results<NoContent, NotFound> Reset(int id)
    {
        if (!_notificationFactory.Exists(id))
        {
            return TypedResults.NotFound();
        }

        _notificationStatusService.Reset(id);

        return TypedResults.NoContent();
    }

    [NonAction]
    public override Results<Ok<IEnumerable<ConnectionResource>>, BadRequest> UpdateProvider([FromBody] ConnectionBulkResource providerResource)
    {
        throw new NotImplementedException();
    }

    [NonAction]
    public override NoContent DeleteProviders([FromBody] ConnectionBulkResource resource)
    {
        throw new NotImplementedException();
    }
}
