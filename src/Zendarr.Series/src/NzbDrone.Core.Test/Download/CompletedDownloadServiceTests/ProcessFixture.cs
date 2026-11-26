using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;
using MovieImport = NzbDrone.Core.MediaFiles.MovieImport;

namespace NzbDrone.Core.Test.Download.CompletedDownloadServiceTests
{
    [TestFixture]
    public class ProcessFixture : CoreTest<CompletedDownloadService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            var completed = Builder<DownloadClientItem>.CreateNew()
                                                    .With(h => h.Status = DownloadItemStatus.Completed)
                                                    .With(h => h.OutputPath = new OsPath(@"C:\DropFolder\MyDownload".AsOsAgnostic()))
                                                    .With(h => h.RemainingSize = 0)
                                                    .With(h => h.Title = "Drone.S01E01.HDTV")
                                                    .Build();

            var remoteEpisode = BuildRemoteEpisode();

            _trackedDownload = Builder<TrackedDownload>.CreateNew()
                    .With(c => c.State = TrackedDownloadState.Downloading)
                    .With(c => c.DownloadItem = completed)
                    .With(c => c.RemoteEpisode = remoteEpisode)
                    .Build();

            Mocker.GetMock<IDownloadClient>()
              .SetupGet(c => c.Definition)
              .Returns(new DownloadClientDefinition { Id = 1, Name = "testClient" });

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(c => c.Get(It.IsAny<int>()))
                  .Returns(Mocker.GetMock<IDownloadClient>().Object);

            Mocker.GetMock<IProvideImportItemService>()
                  .Setup(c => c.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                  .Returns((DownloadClientItem item, DownloadClientItem previous) => item);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(_trackedDownload.DownloadItem.DownloadId))
                  .Returns(new List<EpisodeHistory>());

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetSeries("Drone.S01E01.HDTV"))
                  .Returns(remoteEpisode.Series);
        }

        private RemoteEpisode BuildRemoteEpisode()
        {
            return new RemoteEpisode
            {
                Series = new Series(),
                Episodes = new List<Episode> { new Episode { Id = 1 } }
            };
        }

        private void GivenNoGrabbedHistory()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(_trackedDownload.DownloadItem.DownloadId))
                .Returns(new List<EpisodeHistory>());
        }

        private void GivenCompletedDownloadHandling(bool enabled = true)
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnableCompletedDownloadHandling)
                  .Returns(enabled);
        }

        private void GivenSeriesMatch()
        {
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetSeries(It.IsAny<string>()))
                  .Returns(_trackedDownload.RemoteEpisode.Series);
        }

        private void GivenABadlyNamedDownload()
        {
            _trackedDownload.DownloadItem.DownloadId = "1234";
            _trackedDownload.DownloadItem.Title = "Droned Pilot"; // Set a badly named download
            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.Is<string>(i => i == "1234")))
                  .Returns(new List<EpisodeHistory>
                  {
                      new EpisodeHistory() { SourceTitle = "Droned S01E01", EventType = EpisodeHistoryEventType.Grabbed }
                  });

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetSeries(It.IsAny<string>()))
                  .Returns((Series)null);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetSeries("Droned S01E01"))
                  .Returns(BuildRemoteEpisode().Series);
        }

        [TestCase(DownloadItemStatus.Downloading)]
        [TestCase(DownloadItemStatus.Failed)]
        [TestCase(DownloadItemStatus.Queued)]
        [TestCase(DownloadItemStatus.Paused)]
        public void should_not_process_if_download_status_isnt_completed(DownloadItemStatus status)
        {
            _trackedDownload.DownloadItem.Status = status;

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
        }

        [Test]
        public void should_process_a_finished_warned_download()
        {
            // A client reports Warning when the download finished but its post-processing failed.
            // It must enter the normal import path instead of being skipped.
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Warning;
            _trackedDownload.DownloadItem.Category = "tv";
            GivenCompletedDownloadHandling();

            Subject.Check(_trackedDownload);

            AssertReadyToImport();
        }

        [Test]
        public void should_not_process_a_warned_download_that_is_still_downloading()
        {
            // A warning while data is still outstanding (e.g. a stalled torrent) belongs to the
            // Downloading state; the files must not be imported yet.
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Warning;
            _trackedDownload.DownloadItem.RemainingSize = 1024;
            _trackedDownload.DownloadItem.Category = "tv";
            GivenCompletedDownloadHandling();

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
        }

        [Test]
        public void should_not_process_a_warned_download_when_completed_download_handling_is_disabled()
        {
            // ImportPending is only consumed with completed download handling enabled; without it
            // the warned item must stay in its client state instead of a dead state.
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Warning;
            _trackedDownload.DownloadItem.Category = "tv";
            GivenCompletedDownloadHandling(false);

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
            _trackedDownload.State.Should().Be(TrackedDownloadState.Downloading);
        }

        [Test]
        public void should_fail_a_warned_download_when_it_still_cannot_be_imported_after_max_attempts()
        {
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Warning;
            _trackedDownload.DownloadItem.Category = null;
            _trackedDownload.ImportAttempts = 2;
            GivenNoGrabbedHistory();
            GivenCompletedDownloadHandling();

            Subject.Check(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.FailedPending);
            _trackedDownload.ImportAttempts.Should().Be(3);
            _trackedDownload.StatusMessages.Should().NotBeEmpty();
        }

        [Test]
        public void should_fail_a_warned_download_when_its_import_attempts_are_exhausted()
        {
            // The import path leaves other downloads in ImportBlocked for manual interaction, but a
            // warned download must reach a terminal state once its budget is spent.
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Warning;
            _trackedDownload.State = TrackedDownloadState.ImportBlocked;
            _trackedDownload.ImportAttempts = 3;
            GivenCompletedDownloadHandling();

            Subject.Check(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.FailedPending);
        }

        [Test]
        public void should_not_process_if_matching_history_is_not_found_and_no_category_specified()
        {
            _trackedDownload.DownloadItem.Category = null;
            GivenNoGrabbedHistory();

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
        }

        [Test]
        public void should_process_if_matching_history_is_not_found_but_category_specified()
        {
            _trackedDownload.DownloadItem.Category = "tv";
            GivenNoGrabbedHistory();
            GivenSeriesMatch();

            Subject.Check(_trackedDownload);

            AssertReadyToImport();
        }

        [Test]
        public void should_not_process_if_output_path_is_empty()
        {
            _trackedDownload.DownloadItem.OutputPath = default(OsPath);

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
        }

        [Test]
        public void should_not_process_if_the_download_cannot_be_tracked_using_the_source_title_as_it_was_initiated_externally()
        {
            GivenABadlyNamedDownload();

            Mocker.GetMock<IDownloadedEpisodesImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Series>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(new ImportDecision(new LocalEpisode { Path = @"C:\TestPath\Droned.S01E01.mkv" }))
                           });

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
        }

        [Test]
        public void should_not_process_when_there_is_a_title_mismatch()
        {
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetSeries("Drone.S01E01.HDTV"))
                  .Returns((Series)null);

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
        }

        private void AssertNotReadyToImport()
        {
            _trackedDownload.State.Should().NotBe(TrackedDownloadState.ImportPending);
        }

        private void AssertReadyToImport()
        {
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }
    }

    // Movie counterpart of ProcessFixture, covering the CheckMovie branch of
    // CompletedDownloadService.Check for downloads tracked with a RemoteMovie.
    [TestFixture]
    public class MovieProcessFixture : CoreTest<CompletedDownloadService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            var completed = Builder<DownloadClientItem>.CreateNew()
                                                    .With(h => h.Status = DownloadItemStatus.Completed)
                                                    .With(h => h.OutputPath = new OsPath(@"C:\DropFolder\MyDownload".AsOsAgnostic()))
                                                    .With(h => h.RemainingSize = 0)
                                                    .With(h => h.Title = "Drone.S01E01.HDTV")
                                                    .Build();

            var remoteMovie = BuildRemoteMovie();

            _trackedDownload = Builder<TrackedDownload>.CreateNew()
                    .With(c => c.State = TrackedDownloadState.Downloading)
                    .With(c => c.DownloadItem = completed)
                    .With(c => c.RemoteMovie = remoteMovie)
                    .Build();

            Mocker.GetMock<IDownloadClient>()
              .SetupGet(c => c.Definition)
              .Returns(new DownloadClientDefinition { Id = 1, Name = "testClient" });

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(c => c.Get(It.IsAny<int>()))
                  .Returns(Mocker.GetMock<IDownloadClient>().Object);

            Mocker.GetMock<IProvideImportItemService>()
                  .Setup(c => c.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                  .Returns((DownloadClientItem item, DownloadClientItem previous) => item);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindMovieByDownloadId(_trackedDownload.DownloadItem.DownloadId))
                  .Returns(new List<MovieHistory>());

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie("Drone.S01E01.HDTV"))
                  .Returns(remoteMovie.Movie);
        }

        private RemoteMovie BuildRemoteMovie()
        {
            return new RemoteMovie
            {
                Movie = new Movie(),
            };
        }

        private void GivenNoGrabbedHistory()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindMovieByDownloadId(_trackedDownload.DownloadItem.DownloadId))
                .Returns(new List<MovieHistory>());
        }

        private void GivenCompletedDownloadHandling(bool enabled = true)
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnableCompletedDownloadHandling)
                  .Returns(enabled);
        }

        private void GivenMovieMatch()
        {
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie(It.IsAny<string>()))
                  .Returns(_trackedDownload.RemoteMovie.Movie);
        }

        private void GivenABadlyNamedDownload()
        {
            _trackedDownload.DownloadItem.DownloadId = "1234";
            _trackedDownload.DownloadItem.Title = "Droned Pilot"; // Set a badly named download
            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindMovieByDownloadId(It.Is<string>(i => i == "1234")))
                  .Returns(new List<MovieHistory>
                  {
                      new MovieHistory() { SourceTitle = "Droned S01E01", EventType = MovieHistoryEventType.Grabbed }
                  });

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie(It.IsAny<string>()))
                  .Returns((Movie)null);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie("Droned S01E01"))
                  .Returns(BuildRemoteMovie().Movie);
        }

        [TestCase(DownloadItemStatus.Downloading)]
        [TestCase(DownloadItemStatus.Failed)]
        [TestCase(DownloadItemStatus.Queued)]
        [TestCase(DownloadItemStatus.Paused)]
        public void should_not_process_movie_if_download_status_isnt_completed(DownloadItemStatus status)
        {
            _trackedDownload.DownloadItem.Status = status;

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
        }

        [Test]
        public void should_process_a_finished_warned_movie_download()
        {
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Warning;
            _trackedDownload.DownloadItem.Category = "movies";
            GivenCompletedDownloadHandling();

            Subject.Check(_trackedDownload);

            AssertReadyToImport();
        }

        [Test]
        public void should_not_process_a_warned_movie_download_that_is_still_downloading()
        {
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Warning;
            _trackedDownload.DownloadItem.RemainingSize = 1024;
            _trackedDownload.DownloadItem.Category = "movies";
            GivenCompletedDownloadHandling();

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
        }

        [Test]
        public void should_fail_a_warned_movie_download_when_it_still_cannot_be_imported_after_max_attempts()
        {
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Warning;
            _trackedDownload.DownloadItem.Category = null;
            _trackedDownload.ImportAttempts = 2;
            GivenNoGrabbedHistory();
            GivenCompletedDownloadHandling();

            Subject.Check(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.FailedPending);
            _trackedDownload.ImportAttempts.Should().Be(3);
            _trackedDownload.StatusMessages.Should().NotBeEmpty();
        }

        [Test]
        public void should_not_process_movie_if_matching_history_is_not_found_and_no_category_specified()
        {
            _trackedDownload.DownloadItem.Category = null;
            GivenNoGrabbedHistory();

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
        }

        [Test]
        public void should_process_movie_if_matching_history_is_not_found_but_category_specified()
        {
            _trackedDownload.DownloadItem.Category = "movies";
            GivenNoGrabbedHistory();
            GivenMovieMatch();

            Subject.Check(_trackedDownload);

            AssertReadyToImport();
        }

        [Test]
        public void should_not_process_movie_if_output_path_is_empty()
        {
            _trackedDownload.DownloadItem.OutputPath = default(OsPath);

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
        }

        [Test]
        public void should_not_process_movie_if_the_download_cannot_be_tracked_using_the_source_title_as_it_was_initiated_externally()
        {
            GivenABadlyNamedDownload();

            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<MovieImport.ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<MovieImport.ImportResult>
                           {
                               new MovieImport.ImportResult(new MovieImport.ImportDecision(new LocalMovie { Path = @"C:\TestPath\Droned.S01E01.mkv" }))
                           });

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
        }

        [Test]
        public void should_not_process_movie_when_there_is_a_title_mismatch()
        {
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie("Drone.S01E01.HDTV"))
                  .Returns((Movie)null);

            Subject.Check(_trackedDownload);

            AssertNotReadyToImport();
        }

        private void AssertNotReadyToImport()
        {
            _trackedDownload.State.Should().NotBe(TrackedDownloadState.ImportPending);
        }

        private void AssertReadyToImport()
        {
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }
    }
}
