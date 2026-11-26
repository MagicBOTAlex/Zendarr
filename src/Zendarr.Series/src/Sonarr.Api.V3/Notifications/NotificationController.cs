using System;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Notifications;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.ClientSchema;
using Sonarr.Http.Subsystem;

namespace Sonarr.Api.V3.Notifications
{
    [V3ApiController]
    public class NotificationController : ProviderControllerBase<NotificationResource, NotificationBulkResource, INotification, NotificationDefinition>
    {
        public static readonly NotificationResourceMapper ResourceMapper = new();
        public static readonly NotificationBulkResourceMapper BulkResourceMapper = new();

        private readonly INotificationFactory _notificationFactory;
        private readonly INotificationStatusService _notificationStatusService;

        public NotificationController(IBroadcastSignalRMessage signalRBroadcaster,
            NotificationFactory notificationFactory,
            INotificationStatusService notificationStatusService)
            : base(signalRBroadcaster, notificationFactory, "notification", ResourceMapper, BulkResourceMapper)
        {
            _notificationFactory = notificationFactory;
            _notificationStatusService = notificationStatusService;
        }

        // Explicitly clear a failure backoff; testing a notification no longer records status.
        [HttpPost("{id:int}/reset")]
        [Produces("application/json")]
        public ActionResult Reset(int id)
        {
            if (!_notificationFactory.Exists(id))
            {
                return NotFound();
            }

            _notificationStatusService.Reset(id);

            return NoContent();
        }

        protected override bool IsFieldVisible(Field field, AppSubsystem subsystem)
        {
            return ProviderFieldScoping.IsFieldVisible(field.Name, subsystem, ProviderFieldScoping.NotificationFields.SeriesOnly, ProviderFieldScoping.NotificationFields.MovieOnly);
        }

        [NonAction]
        public override ActionResult<NotificationResource> UpdateProvider([FromBody] NotificationBulkResource providerResource)
        {
            throw new NotImplementedException();
        }

        [NonAction]
        public override object DeleteProviders([FromBody] NotificationBulkResource resource)
        {
            throw new NotImplementedException();
        }
    }
}
