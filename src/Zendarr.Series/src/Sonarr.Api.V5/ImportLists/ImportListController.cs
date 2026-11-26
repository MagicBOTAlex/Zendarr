using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.SignalR;
using Sonarr.Api.V5.Provider;
using Sonarr.Http;
using Sonarr.Http.Subsystem;

namespace Sonarr.Api.V5.ImportLists;

[V5ApiController]
[AppSubsystem(AppSubsystem.Series)]
public class ImportListController : ProviderControllerBase<ImportListResource, ImportListBulkResource, IImportList, ImportListDefinition>
{
    public static readonly ImportListResourceMapper ResourceMapper = new();
    public static readonly ImportListBulkResourceMapper BulkResourceMapper = new();

    private readonly IImportListFactory _importListFactory;
    private readonly IImportListStatusService _importListStatusService;

    public ImportListController(IBroadcastSignalRMessage signalRBroadcaster,
        IImportListFactory importListFactory,
        IImportListStatusService importListStatusService,
        RootFolderExistsValidator rootFolderExistsValidator,
        QualityProfileExistsValidator qualityProfileExistsValidator)
        : base(signalRBroadcaster, importListFactory, "importlist", ResourceMapper, BulkResourceMapper)
    {
        _importListFactory = importListFactory;
        _importListStatusService = importListStatusService;

        SharedValidator.RuleFor(c => c.RootFolderPath).Cascade(CascadeMode.Stop)
            .IsValidPath()
            .SetValidator(rootFolderExistsValidator);

        SharedValidator.RuleFor(c => c.QualityProfileId).Cascade(CascadeMode.Stop)
            .ValidId()
            .SetValidator(qualityProfileExistsValidator);
    }

    // Explicitly clear a failure backoff; testing an import list no longer records status.
    [HttpPost("{id:int}/reset")]
    [Produces("application/json")]
    public Results<NoContent, NotFound> Reset(int id)
    {
        if (!_importListFactory.Exists(id))
        {
            return TypedResults.NotFound();
        }

        _importListStatusService.Reset(id);

        return TypedResults.NoContent();
    }
}
