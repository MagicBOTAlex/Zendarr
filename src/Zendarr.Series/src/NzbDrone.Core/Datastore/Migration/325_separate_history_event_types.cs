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
            // Legacy movie values used the old shared MovieHistoryEventType enum, so the
            // default mapping is +100. Two values were deprecated placeholders and have no
            // movie enum member in the new 100-range: 2 (SeriesFolderImported) and
            // 5 (EpisodeFileDeleted). A plain +100 would leave 102/105, which are undefined
            // and make HistoryController.TryParseEventType reject the rows, so fold them onto
            // the closest defined movie events instead.
            //
            // Old movie value -> new movie value (media type 1 only):
            //   0 Unknown                -> 100 Unknown
            //   1 Grabbed                -> 101 Grabbed
            //   2 SeriesFolderImported   -> 107 MovieFolderImported  (folded; undefined as 102)
            //   3 DownloadFolderImported -> 103 DownloadFolderImported
            //   4 DownloadFailed         -> 104 DownloadFailed
            //   5 EpisodeFileDeleted     -> 106 MovieFileDeleted     (folded; undefined as 105)
            //   6 MovieFileDeleted       -> 106 MovieFileDeleted
            //   7 MovieFolderImported    -> 107 MovieFolderImported
            //   8 MovieFileRenamed       -> 108 MovieFileRenamed
            //   9 DownloadIgnored        -> 109 DownloadIgnored
            //   other 0-9 values         -> +100 (kept distinct for diagnostics)
            Execute.Sql(@"UPDATE ""History"" SET ""EventType"" = CASE ""EventType""
                              WHEN 2 THEN 107
                              WHEN 5 THEN 106
                              ELSE ""EventType"" + 100
                          END
                          WHERE ""MediaType"" = 1 AND ""EventType"" BETWEEN 0 AND 9");
        }
    }
}
