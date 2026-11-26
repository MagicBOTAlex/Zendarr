using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(324)]
    public class unique_episode_file_path : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Keep the newest record and preserve all references before removing duplicates.
            // NULL paths are left alone: they do not identify a physical file.
            foreach (var table in new[] { "Episodes", "ExtraFiles", "SubtitleFiles", "MetadataFiles", "TranscodeJobs" })
            {
                Execute.Sql($@"UPDATE ""{table}""
                    SET ""EpisodeFileId"" = (
                        SELECT MAX(""keeper"".""Id"") FROM ""EpisodeFiles"" AS ""current""
                        JOIN ""EpisodeFiles"" AS ""keeper""
                          ON ""keeper"".""SeriesId"" = ""current"".""SeriesId""
                         AND ""keeper"".""RelativePath"" = ""current"".""RelativePath""
                        WHERE ""current"".""Id"" = ""{table}"".""EpisodeFileId""
                    )
                    WHERE ""EpisodeFileId"" IN (
                        SELECT ""Id"" FROM ""EpisodeFiles""
                        WHERE ""SeriesId"" IS NOT NULL AND ""RelativePath"" IS NOT NULL
                    )");
            }

            Execute.Sql(@"DELETE FROM ""EpisodeFiles""
                WHERE ""SeriesId"" IS NOT NULL AND ""RelativePath"" IS NOT NULL
                  AND ""Id"" NOT IN (
                      SELECT MAX(""Id"") FROM ""EpisodeFiles""
                      WHERE ""SeriesId"" IS NOT NULL AND ""RelativePath"" IS NOT NULL
                      GROUP BY ""SeriesId"", ""RelativePath""
                  )");

            Create.Index("IX_EpisodeFiles_SeriesId_RelativePath")
                .OnTable("EpisodeFiles")
                .OnColumn("SeriesId").Ascending()
                .OnColumn("RelativePath").Ascending()
                .WithOptions().Unique();
        }
    }
}
