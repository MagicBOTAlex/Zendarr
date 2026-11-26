using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Messaging.Events
{
    [TestFixture]
    public class EventAggregatorFailedHistoryFixture : DbTest
    {
        [SetUp]
        public void Setup()
        {
            // PublishEvent must not fan out to real handlers here; only the persisted
            // projections are under test.
            var mainDb = Mocker.Resolve<IMainDatabase>();

            Mocker.GetMock<IServiceFactory>()
                  .Setup(c => c.BuildAll<IHandle<DownloadFailedEvent>>())
                  .Returns(new List<IHandle<DownloadFailedEvent>>());

            Mocker.GetMock<IServiceFactory>()
                  .Setup(c => c.BuildAll<IHandleAsync<DownloadFailedEvent>>())
                  .Returns(new List<IHandleAsync<DownloadFailedEvent>>());

            Mocker.GetMock<IServiceFactory>()
                  .Setup(c => c.BuildAll<IHandleAsync<IEvent>>())
                  .Returns(new List<IHandleAsync<IEvent>>());

            Mocker.GetMock<IServiceFactory>()
                  .Setup(c => c.Build<IMainDatabase>())
                  .Returns(mainDb);
        }

        private static DownloadFailedEvent FailedEvent(string downloadId)
        {
            return new DownloadFailedEvent
            {
                SeriesId = 1,
                EpisodeIds = new List<int> { 2 },
                DownloadId = downloadId,
                SourceTitle = "Some.Release",
                DownloadClient = "TestClient",
                Message = "Failed"
            };
        }

        [Test]
        public void should_write_history_row_for_blank_download_id_without_throwing()
        {
            var subject = Mocker.Resolve<EventAggregator>();
            var failed = FailedEvent(null);

            // Must not throw for a legacy/blank-id grabbed row.
            subject.PublishEvent(failed);

            Db.All<EpisodeHistory>().Should().ContainSingle();
            Db.All<DownloadHistory>().Should().BeEmpty();
        }

        [Test]
        public void should_write_both_rows_for_a_download_id()
        {
            var subject = Mocker.Resolve<EventAggregator>();
            var failed = FailedEvent("download-id");

            subject.PublishEvent(failed);

            Db.All<EpisodeHistory>().Should().ContainSingle();
            Db.All<DownloadHistory>().Where(h => h.EventType == DownloadHistoryEventType.DownloadFailed)
              .Should().ContainSingle();
        }
    }
}
