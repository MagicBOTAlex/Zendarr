using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Messaging.Commands
{
    [TestFixture]
    public class CommandRepositoryFixture : DbTest<CommandRepository, CommandModel>
    {
        [Test]
        public void end_should_persist_the_command_result()
        {
            var command = Subject.Insert(new CommandModel
            {
                Name = "RefreshMonitoredDownloads",
                Body = new RefreshMonitoredDownloadsCommand(),
                Priority = CommandPriority.Normal,
                Trigger = CommandTrigger.Unspecified,
                Status = CommandStatus.Started,
                Result = CommandResult.Unknown,
                QueuedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow
            });

            command.Status = CommandStatus.Completed;
            command.Result = CommandResult.Successful;
            command.EndedAt = DateTime.UtcNow;
            command.Duration = TimeSpan.FromSeconds(1);

            Subject.End(command);

            Subject.Get(command.Id).Result.Should().Be(CommandResult.Successful);
        }
    }
}
