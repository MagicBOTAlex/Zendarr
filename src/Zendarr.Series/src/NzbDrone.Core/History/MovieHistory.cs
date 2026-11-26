using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.History
{
    public class MovieHistory : ModelBase
    {
        public const string DOWNLOAD_CLIENT = "downloadClient";
        public const string MOVIE_MATCH_TYPE = "movieMatchType";
        public const string RELEASE_SOURCE = "releaseSource";
        public const string RELEASE_GROUP = "releaseGroup";
        public const string SIZE = "size";
        public const string INDEXER = "indexer";

        public MovieHistory()
        {
            Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MediaType = NzbDrone.Core.ImportLists.MediaType.Movie;
            EventType = MovieHistoryEventType.Unknown;
        }

        public int MovieId { get; set; }
        public string SourceTitle { get; set; }
        public QualityModel Quality { get; set; }
        public DateTime Date { get; set; }
        public Movie Movie { get; set; }
        public MovieHistoryEventType EventType { get; set; }
        public Dictionary<string, string> Data { get; set; }
        public List<Language> Languages { get; set; }

        public string DownloadId { get; set; }

        // D3 superset discriminator: movie rows are Movie (1). Episode rows share the
        // physical "History" table and are written through EpisodeHistory (Series = 0).
        public MediaType MediaType { get; set; }
    }

    public enum MovieHistoryEventType
    {
        // History is shared with episodes; movie values occupy a separate range.
        Unknown = 100,
        Grabbed = 101,
        DownloadFolderImported = 103,
        DownloadFailed = 104,
        MovieFileDeleted = 106,
        MovieFolderImported = 107,
        MovieFileRenamed = 108,
        DownloadIgnored = 109
    }
}
