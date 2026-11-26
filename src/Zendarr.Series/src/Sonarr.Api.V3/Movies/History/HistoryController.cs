using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Movies;
using Sonarr.Http;
using Sonarr.Http.Extensions;
using Sonarr.Http.Subsystem;

namespace Sonarr.Api.V3.Movies.History
{
    [V3ApiController("history")]
    [AppSubsystem(AppSubsystem.Movies)]
    public class HistoryController : Controller
    {
        private readonly IHistoryService _historyService;
        private readonly IMovieService _movieService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IUpgradableSpecification _upgradableSpecification;
        private readonly IFailedDownloadService _failedDownloadService;

        public HistoryController(IHistoryService historyService,
                             IMovieService movieService,
                             ICustomFormatCalculationService formatCalculator,
                             IUpgradableSpecification upgradableSpecification,
                             IFailedDownloadService failedDownloadService)
        {
            _historyService = historyService;
            _movieService = movieService;
            _formatCalculator = formatCalculator;
            _upgradableSpecification = upgradableSpecification;
            _failedDownloadService = failedDownloadService;
        }

        protected HistoryResource MapToResource(MovieHistory model, bool includeMovie)
        {
            if (model.Movie == null)
            {
                model.Movie = _movieService.GetMovie(model.MovieId);
            }

            var resource = model.ToResource(_formatCalculator);

            if (includeMovie)
            {
                resource.Movie = model.Movie.ToResource(0);
            }

            if (model.Movie != null)
            {
                resource.QualityCutoffNotMet = _upgradableSpecification.QualityCutoffNotMet(model.Movie.QualityProfile, model.Quality);
            }

            return resource;
        }

        [HttpGet]
        [Produces("application/json")]
        public PagingResource<HistoryResource> GetHistory([FromQuery] PagingRequestResource paging, bool includeMovie, [FromQuery(Name = "eventType")] int[] eventTypes, string downloadId, [FromQuery] int[] movieIds = null, [FromQuery] int[] languages = null, [FromQuery] int[] quality = null)
        {
            var pagingResource = new PagingResource<HistoryResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<HistoryResource, MovieHistory>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "date",
                    "languages",
                    "movieMetadata.sortTitle",
                    "quality"
                },
                "date",
                SortDirection.Descending);

            if (eventTypes != null && eventTypes.Any())
            {
                // Clients may still send the pre-migration numeric movie event values.
                eventTypes = eventTypes.Select(NormalizeEventType).ToArray();
                pagingSpec.FilterExpressions.Add(v => eventTypes.Contains((int)v.EventType));
            }

            if (downloadId.IsNotNullOrWhiteSpace())
            {
                pagingSpec.FilterExpressions.Add(h => h.DownloadId == downloadId);
            }

            if (movieIds != null && movieIds.Any())
            {
                pagingSpec.FilterExpressions.Add(h => movieIds.Contains(h.MovieId));
            }

            return pagingSpec.ApplyToPage(h => _historyService.Paged(pagingSpec, languages, quality), h => MapToResource(h, includeMovie));
        }

        [HttpGet("since")]
        [Produces("application/json")]
        public ActionResult<List<HistoryResource>> GetHistorySince(DateTime date, string eventType = null, bool includeMovie = false)
        {
            if (!TryParseEventType(eventType, out var parsed))
            {
                return BadRequest("Invalid history event type");
            }

            return _historyService.SinceMovie(date, parsed).Select(h => MapToResource(h, includeMovie)).ToList();
        }

        [HttpGet("movie")]
        [Produces("application/json")]
        public ActionResult<List<HistoryResource>> GetMovieHistory(int movieId, string eventType = null, bool includeMovie = false)
        {
            if (!TryParseEventType(eventType, out var parsed))
            {
                return BadRequest("Invalid history event type");
            }

            return _historyService.GetByMovieId(movieId, parsed).Select(h => MapToResource(h, includeMovie)).ToList();
        }

        private static int NormalizeEventType(int value)
        {
            // Mirrors migration 325: legacy movie values 2 (SeriesFolderImported) and
            // 5 (EpisodeFileDeleted) have no movie-range member, so they fold onto the
            // closest defined movie events instead of becoming undefined 102/105.
            switch (value)
            {
                case 2:
                    return (int)MovieHistoryEventType.MovieFolderImported;
                case 5:
                    return (int)MovieHistoryEventType.MovieFileDeleted;
                default:
                    return value >= 0 && value <= 9 ? value + 100 : value;
            }
        }

        private static bool TryParseEventType(string value, out MovieHistoryEventType? result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            if (!Enum.TryParse<MovieHistoryEventType>(value, true, out var parsed))
            {
                return false;
            }

            parsed = (MovieHistoryEventType)NormalizeEventType((int)parsed);
            if (!Enum.IsDefined(typeof(MovieHistoryEventType), parsed))
            {
                return false;
            }

            result = parsed;
            return true;
        }

        [HttpPost("failed/{id}")]
        public object MarkAsFailed([FromRoute] int id)
        {
            _failedDownloadService.MarkAsFailedMovie(id);
            return new { };
        }
    }
}
