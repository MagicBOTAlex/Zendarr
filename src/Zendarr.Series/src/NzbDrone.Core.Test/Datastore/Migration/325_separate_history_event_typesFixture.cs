using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class separate_history_event_typesFixture : MigrationTest<separate_history_event_types>
    {
        [Test]
        public void should_move_only_movie_events_to_the_new_domain()
        {
            var db = WithMigrationTestDb(c =>
            {
                foreach (var mediaType in new[] { 0, 1 })
                {
                    foreach (var eventType in new[] { 0, 1, 3, 4, 6, 7, 8, 9 })
                    {
                        c.Insert.IntoTable("History").Row(new
                        {
                            MediaType = mediaType,
                            EventType = eventType,
                            SourceTitle = "test",
                            Date = DateTime.UtcNow,
                            Quality = "{}",
                            Data = "{}",
                            Languages = "[]"
                        });
                    }
                }
            });

            var rows = db.Query<HistoryEvent>("SELECT \"MediaType\", \"EventType\" FROM \"History\"").ToList();
            rows.Where(r => r.MediaType == 0).Select(r => r.EventType)
                .Should().BeEquivalentTo(new[] { 0, 1, 3, 4, 6, 7, 8, 9 });
            rows.Where(r => r.MediaType == 1).Select(r => r.EventType)
                .Should().BeEquivalentTo(new[] { 100, 101, 103, 104, 106, 107, 108, 109 });
        }

        [Test]
        public void should_map_legacy_deprecated_movie_events_to_defined_events()
        {
            // Legacy 2 (SeriesFolderImported) and 5 (EpisodeFileDeleted) have no member in
            // the movie 100-range; they must not survive as 102/105.
            var db = WithMigrationTestDb(c =>
            {
                foreach (var mediaType in new[] { 0, 1 })
                {
                    foreach (var eventType in new[] { 2, 5 })
                    {
                        c.Insert.IntoTable("History").Row(new
                        {
                            MediaType = mediaType,
                            EventType = eventType,
                            SourceTitle = "test",
                            Date = DateTime.UtcNow,
                            Quality = "{}",
                            Data = "{}",
                            Languages = "[]"
                        });
                    }
                }
            });

            var rows = db.Query<HistoryEvent>("SELECT \"MediaType\", \"EventType\" FROM \"History\"").ToList();
            rows.Where(r => r.MediaType == 0).Select(r => r.EventType)
                .Should().BeEquivalentTo(new[] { 2, 5 });
            rows.Where(r => r.MediaType == 1).Select(r => r.EventType)
                .Should().BeEquivalentTo(new[] { 106, 107 });
        }

        private class HistoryEvent
        {
            public int MediaType { get; set; }
            public int EventType { get; set; }
        }
    }
}
