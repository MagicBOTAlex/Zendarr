using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(325)]
    public class separate_history_event_types : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Keep the old movie values distinguishable, including any unknown legacy
            // values in the original 0-9 range. Series history is left untouched.
            //
            // Legacy movie values 2 (SeriesFolderImported) and 5 (EpisodeFileDeleted) have
            // no movie enum member in the new 100-range. A plain +100 would leave 102/105,
            // which are undefined and make HistoryController.TryParseEventType reject the
            // rows, so fold them onto the closest defined movie events instead.
            Execute.Sql(@"UPDATE ""History"" SET ""EventType"" = CASE ""EventType""
                              WHEN 2 THEN 107
                              WHEN 5 THEN 106
                              ELSE ""EventType"" + 100
                          END
                          WHERE ""MediaType"" = 1 AND ""EventType"" BETWEEN 0 AND 9");
        }
    }
}
