using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadHistoryTests
{
    [TestFixture]
    public class DownloadHistoryServiceFixture : CoreTest<DownloadHistoryService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IDownloadHistoryRepository>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<DownloadHistory>());
        }

        [Test]
        public void should_record_terminal_history_when_download_is_removed()
        {
            var trackedDownload = new TrackedDownload
            {
                DownloadClient = 3,
                Protocol = DownloadProtocol.Torrent,
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "abc123",
                    Title = "Series.S01E01.1080p",
                    DownloadClientInfo = new DownloadClientItemClientInfo
                    {
                        Type = "QBittorrent",
                        Name = "qBittorrent"
                    }
                }
            };

            Subject.RecordRemovedDownload(trackedDownload);

            Mocker.GetMock<IDownloadHistoryRepository>()
                  .Verify(v => v.Insert(It.Is<DownloadHistory>(h =>
                      h.EventType == DownloadHistoryEventType.DownloadIgnored &&
                      h.DownloadId == "abc123" &&
                      h.SourceTitle == "Series.S01E01.1080p")),
                      Times.Once());
        }

        [Test]
        public void should_not_record_history_without_download_id()
        {
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem
                {
                    Title = "Series.S01E01.1080p"
                }
            };

            Subject.RecordRemovedDownload(trackedDownload);

            Mocker.GetMock<IDownloadHistoryRepository>()
                  .Verify(v => v.Insert(It.IsAny<DownloadHistory>()), Times.Never());
        }
    }
}
