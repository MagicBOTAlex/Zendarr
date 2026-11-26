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
            Execute.Sql(@"UPDATE ""History"" SET ""EventType"" = ""EventType"" + 100
                          WHERE ""MediaType"" = 1 AND ""EventType"" BETWEEN 0 AND 9");
        }
    }
}
