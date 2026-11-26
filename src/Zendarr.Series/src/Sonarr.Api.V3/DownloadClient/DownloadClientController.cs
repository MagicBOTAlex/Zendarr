using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Download;
using NzbDrone.SignalR;
using Sonarr.Http;

namespace Sonarr.Api.V3.DownloadClient
{
    // One shared download-client store serves both domains, and the unified Settings page renders
    // one merged form (Series + Movies sections) through the series key, so every field is returned
    // here too. Per-subject fields are grouped by their `section`.
    [V3ApiController]
    public class DownloadClientController : ProviderControllerBase<DownloadClientResource, DownloadClientBulkResource, IDownloadClient, DownloadClientDefinition>
    {
        public static readonly DownloadClientResourceMapper ResourceMapper = new();
        public static readonly DownloadClientBulkResourceMapper BulkResourceMapper = new();

        private readonly IDownloadClientFactory _downloadClientFactory;
        private readonly IDownloadClientStatusService _downloadClientStatusService;

        public DownloadClientController(IBroadcastSignalRMessage signalRBroadcaster,
            IDownloadClientFactory downloadClientFactory,
            IDownloadClientStatusService downloadClientStatusService)
            : base(signalRBroadcaster, downloadClientFactory, "downloadclient", ResourceMapper, BulkResourceMapper)
        {
            _downloadClientFactory = downloadClientFactory;
            _downloadClientStatusService = downloadClientStatusService;

            SharedValidator.RuleFor(c => c.Priority).InclusiveBetween(1, 50);
        }

        // Explicitly clear a failure backoff; testing a client no longer records status.
        [HttpPost("{id:int}/reset")]
        [Produces("application/json")]
        public ActionResult Reset(int id)
        {
            if (!_downloadClientFactory.Exists(id))
            {
                return NotFound();
            }

            _downloadClientStatusService.Reset(id);

            return NoContent();
        }
    }
}
