using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.HistoryTests
{
    [TestFixture]
    public class HistoryServiceDownloadIdFixture : DbTest<HistoryService, EpisodeHistory>
    {
        private const string ImportDownloadId = "download-id";
        private const int TestSeriesId = 7;
        private const int TestEpisodeId = 12;
        private const int TestEpisodeFileId = 42;

        private Episode _episode;

        [SetUp]
        public void Setup()
        {
            // DbTest would mock IHistoryRepository; the whole point here is the real
            // serialization round-trip of the History.Data dictionary.
            Mocker.SetConstant<IHistoryRepository>(Mocker.Resolve<HistoryRepository>());

            _episode = new Episode
            {
                Id = TestEpisodeId,
                SeriesId = TestSeriesId,
                SeasonNumber = 1,
                EpisodeNumber = 1,
                Title = "episode"
            };
        }

        [Test]
        public void should_copy_download_id_to_deleted_history_from_import_history()
        {
            InsertImportHistory();

            Subject.Handle(new EpisodeFileDeletedEvent(CreateEpisodeFile(), DeleteMediaFileReason.Upgrade));

            StoredHistory(EpisodeHistoryEventType.EpisodeFileDeleted).DownloadId.Should().Be(ImportDownloadId);
        }

        [Test]
        public void should_copy_download_id_to_renamed_history_from_import_history()
        {
            InsertImportHistory();

            var series = new Series { Id = TestSeriesId, Path = "/series/show" };

            Subject.Handle(new EpisodeFileRenamedEvent(series, CreateEpisodeFile(), "/series/show/old.mkv"));

            StoredHistory(EpisodeHistoryEventType.EpisodeFileRenamed).DownloadId.Should().Be(ImportDownloadId);
        }

        private void InsertImportHistory()
        {
            var importHistory = new EpisodeHistory
            {
                EventType = EpisodeHistoryEventType.DownloadFolderImported,
                Date = DateTime.UtcNow,
                SeriesId = TestSeriesId,
                EpisodeId = TestEpisodeId,
                SourceTitle = "import",
                DownloadId = ImportDownloadId,
                Quality = new QualityModel(),
                Languages = new List<Language> { Language.English }
            };

            // Persisted through EmbeddedDocumentConverter, so the stored key is "fileId".
            importHistory.Data.Add("FileId", TestEpisodeFileId.ToString());

            Storage.Insert(importHistory);
        }

        private EpisodeFile CreateEpisodeFile()
        {
            return new EpisodeFile
            {
                Id = TestEpisodeFileId,
                SeriesId = TestSeriesId,
                RelativePath = "Season 1/Show - S01E01.mkv",
                Quality = new QualityModel(),
                Languages = new List<Language> { Language.English },
                Episodes = new LazyLoaded<List<Episode>>(new List<Episode> { _episode })
            };
        }

        private EpisodeHistory StoredHistory(EpisodeHistoryEventType eventType)
        {
            return Storage.All().Single(h => h.EventType == eventType);
        }
    }
}
