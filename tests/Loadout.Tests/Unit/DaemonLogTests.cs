using System.Text;
using FluentAssertions;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The file a daemon in the background writes to, and a terminal follows.
/// </summary>
/// <remarks>
/// The terminal showing it is the only view anybody has of a daemon with no
/// window, so a line shown twice, shown in halves or never shown is the whole
/// of what can go wrong here.
/// </remarks>
public sealed class DaemonLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-dlog-" + Guid.NewGuid().ToString("N"));

    private string Log => Path.Combine(_root, "teams", "daemon.log");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Only_the_daemon_command_is_read_for_its_log()
    {
        DaemonLog.Asked(["team", "daemon", "--log", "/tmp/d.log"]).Should().Be("/tmp/d.log");
        DaemonLog.Asked(["team", "daemon", "--port", "80", "--log", "/tmp/d.log"]).Should().Be("/tmp/d.log");

        // The same flag on another command is that command's business, and
        // turning its output into a file would lose it.
        DaemonLog.Asked(["team", "run", "--log", "/tmp/d.log"]).Should().BeNull();
        DaemonLog.Asked(["team", "daemon", "stop"]).Should().BeNull();
        DaemonLog.Asked(["team", "daemon", "--log"]).Should().BeNull("a flag with nothing after it names nothing");
        DaemonLog.Asked([]).Should().BeNull();
    }

    [Fact]
    public void What_is_written_is_shown_once_and_in_order()
    {
        using (var writer = DaemonLog.Open(Log))
        {
            writer.WriteLine("first");
        }

        var position = 0L;

        DaemonLog.Since(Log, ref position).Should().Be("first" + Environment.NewLine);
        DaemonLog.Since(Log, ref position).Should().BeEmpty("nothing new has been written");

        using (var writer = DaemonLog.Open(Log))
        {
            writer.WriteLine("second");
        }

        DaemonLog.Since(Log, ref position).Should().Be("second" + Environment.NewLine);
    }

    /// <summary>
    /// A line caught half-written would be shown in two pieces, with whatever
    /// the terminal prints between them in the middle.
    /// </summary>
    [Fact]
    public void A_line_still_being_written_waits_until_it_is_whole()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        File.WriteAllText(Log, "whole\nhal", new UTF8Encoding(false));

        var position = 0L;

        DaemonLog.Since(Log, ref position).Should().Be("whole\n");

        File.AppendAllText(Log, "f\n");

        DaemonLog.Since(Log, ref position).Should().Be("half\n");
    }

    [Fact]
    public void A_log_set_aside_is_read_again_from_its_start()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        File.WriteAllText(Log, "an old daemon said a great deal\n");

        var position = DaemonLog.Length(Log);

        File.WriteAllText(Log, "new\n");

        DaemonLog.Since(Log, ref position).Should().Be("new\n");
    }

    [Fact]
    public void A_log_that_is_not_there_says_nothing()
    {
        var position = 0L;

        DaemonLog.Since(Log, ref position).Should().BeEmpty();
        DaemonLog.Length(Log).Should().Be(0);
    }

    [Fact]
    public void Starting_again_keeps_what_the_last_daemon_said()
    {
        using (var writer = DaemonLog.Open(Log))
        {
            writer.WriteLine("its last words");
        }

        using (var writer = DaemonLog.Open(Log))
        {
            writer.WriteLine("the next one");
        }

        File.ReadAllText(Log).Should().Contain("its last words").And.Contain("the next one");
    }

    [Fact]
    public void A_log_past_its_limit_is_set_aside_with_one_kept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        File.WriteAllText(Log, new string('x', (int)DaemonLog.Limit + 1));

        using (var writer = DaemonLog.Open(Log))
        {
            writer.WriteLine("fresh");
        }

        File.ReadAllText(Log).Should().Be("fresh" + Environment.NewLine);
        new FileInfo(Path.Combine(_root, "teams", "daemon.previous.log")).Length
            .Should().Be(DaemonLog.Limit + 1);
    }

    /// <summary>
    /// The daemon writes while a terminal reads, and a restart has the next
    /// daemon open the file while the last one still holds it.
    /// </summary>
    [Fact]
    public void It_can_be_read_and_opened_again_while_it_is_held()
    {
        using var held = DaemonLog.Open(Log);

        held.WriteLine("held");

        var position = 0L;

        DaemonLog.Since(Log, ref position).Should().Be("held" + Environment.NewLine);

        using var second = DaemonLog.Open(Log);

        second.WriteLine("also");

        DaemonLog.Since(Log, ref position).Should().Be("also" + Environment.NewLine);
    }
}
