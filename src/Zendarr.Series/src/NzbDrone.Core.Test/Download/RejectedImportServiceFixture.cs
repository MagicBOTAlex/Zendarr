using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class RejectedImportServiceFixture : CoreTest<RejectedImportService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            _trackedDownload = Builder<TrackedDownload>.CreateNew()
                .With(t => t.State = TrackedDownloadState.Downloading)
                .With(t => t.DownloadItem = Builder<DownloadClientItem>.CreateNew()
                                                    .With(d => d.Title = "Movie.2020")
                                                    .Build())
                .With(t => t.RemoteMovie = new RemoteMovie
                {
                    Release = new ReleaseInfo { IndexerId = 1 }
                })
                .Build();
        }

        private void GivenFailDownloads(params FailDownloads[] failDownloads)
        {
            Mocker.GetMock<ICachedIndexerSettingsProvider>()
                  .Setup(s => s.GetSettings(1))
                  .Returns(new CachedIndexerSettings
                  {
                      FailDownloads = new HashSet<FailDownloads>(failDownloads)
                  });
        }

        [Test]
        public void should_fail_movie_download_for_user_rejected_extension()
        {
            GivenFailDownloads(FailDownloads.UserDefinedExtensions);

            var importResult = new ImportResult(
                new ImportDecision(
                    new LocalMovie { Path = @"C:\TestPath\Movie.2020.exe" },
                    new ImportRejection(ImportRejectionReason.UserRejectedExtension, "Caution: Found file with user defined rejected extension: '.exe'")),
                "Caution: Found file with user defined rejected extension: '.exe'");

            Subject.Process(_trackedDownload, importResult).Should().BeTrue();

            _trackedDownload.State.Should().Be(TrackedDownloadState.FailedPending);
        }

        [Test]
        public void should_not_fail_movie_download_for_user_rejected_extension_when_setting_is_disabled()
        {
            GivenFailDownloads(FailDownloads.Executables);

            var importResult = new ImportResult(
                new ImportDecision(
                    new LocalMovie { Path = @"C:\TestPath\Movie.2020.exe" },
                    new ImportRejection(ImportRejectionReason.UserRejectedExtension, "Caution: Found file with user defined rejected extension: '.exe'")),
                "Caution: Found file with user defined rejected extension: '.exe'");

            Subject.Process(_trackedDownload, importResult).Should().BeTrue();

            _trackedDownload.State.Should().Be(TrackedDownloadState.Downloading);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }
    }
}
