using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Validation;
using NzbDrone.SignalR;
using Sonarr.Api.V5.Provider;
using Sonarr.Http;
using Sonarr.Http.ClientSchema;
using Sonarr.Http.Subsystem;

namespace Sonarr.Api.V5.Indexers;

[V5ApiController]
[AppSubsystem(AppSubsystem.Series)]
public class IndexerController : ProviderControllerBase<IndexerResource, IndexerBulkResource, IIndexer, IndexerDefinition>
{
    public static readonly IndexerResourceMapper ResourceMapper = new();
    public static readonly IndexerBulkResourceMapper BulkResourceMapper = new();

    private readonly IIndexerFactory _indexerFactory;
    private readonly IIndexerStatusService _indexerStatusService;

    public IndexerController(IBroadcastSignalRMessage signalRBroadcaster,
        IndexerFactory indexerFactory,
        IIndexerStatusService indexerStatusService,
        DownloadClientExistsValidator downloadClientExistsValidator)
        : base(signalRBroadcaster, indexerFactory, "indexer", ResourceMapper, BulkResourceMapper)
    {
        _indexerFactory = indexerFactory;
        _indexerStatusService = indexerStatusService;

        SharedValidator.RuleFor(c => c.Priority).InclusiveBetween(1, 50);
        SharedValidator.RuleFor(c => c.SeasonSearchMaximumSingleEpisodeAge).GreaterThanOrEqualTo(0);
        SharedValidator.RuleFor(c => c.DownloadClientId).SetValidator(downloadClientExistsValidator);
    }

    // Operator escape hatch: testing a provider no longer records status (a test must not clear a
    // backoff by accident), so a provider blocked until DisabledTill needs an explicit clear.
    [HttpPost("{id:int}/reset")]
    [Produces("application/json")]
    public Results<NoContent, NotFound> Reset(int id)
    {
        if (!_indexerFactory.Exists(id))
        {
            return TypedResults.NotFound();
        }

        _indexerStatusService.Reset(id);

        return TypedResults.NoContent();
    }

    protected override bool IsFieldVisible(Field field, AppSubsystem subsystem)
    {
        return ProviderFieldScoping.IsFieldVisible(field.Name, subsystem, ProviderFieldScoping.IndexerFields.SeriesOnly, ProviderFieldScoping.IndexerFields.MovieOnly);
    }
}
