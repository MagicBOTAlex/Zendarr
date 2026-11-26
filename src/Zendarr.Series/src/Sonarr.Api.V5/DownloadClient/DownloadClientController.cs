using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Download;
using NzbDrone.SignalR;
using Sonarr.Api.V5.Provider;
using Sonarr.Http;
using Sonarr.Http.Subsystem;

namespace Sonarr.Api.V5.DownloadClient;

// One shared download-client store serves both domains, and the unified Settings page renders one
// merged form (Series + Movies sections) through the series key, so every field is returned here.
// Per-subject fields are grouped by their `section`; the movie category itself is hidden because
// the shared "zendarr" category is used for both.
[V5ApiController]
[AppSubsystem(AppSubsystem.Series)]
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
    public Results<NoContent, NotFound> Reset(int id)
    {
        if (!_downloadClientFactory.Exists(id))
        {
            return TypedResults.NotFound();
        }

        _downloadClientStatusService.Reset(id);

        return TypedResults.NoContent();
    }
}
