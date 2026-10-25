using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Tests.Fakes;
using Loadout.Tests.Platform;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The owner lock on a run directory, on real files.
/// </summary>
/// <remarks>
/// Real files because the lock's meaning is the operating system's: a double
/// would only repeat what the test believes about sharing and flock.
/// </remarks>
public sealed class RunOwnershipTests : IDisposable
{
    private readonly string _run = Path.Combine(Path.GetTempPath(), "loadout-owner-" + Guid.NewGuid().ToString("N"));

    private readonly FakeProcessInspector _processes = new()
    {
        CurrentProcessId = 777,
        CurrentProcessStartedAt = new DateTimeOffset(2026, 10, 23, 9, 30, 0, TimeSpan.Zero),
    };

    public RunOwnershipTests() => Directory.CreateDirectory(_run);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_run, recursive: true);
        }
        catch (IOException)
        {
            // Left in the temp directory; nothing reads it.
        }
    }

    [Fact]
    public void Take_writes_pid_start_and_machine()
    {
        using (Ownership().Take(_run).Value!)
        {
        }

        using var record = JsonDocument.Parse(File.ReadAllText(Path.Combine(_run, "owner.lock")));

        record.RootElement.GetProperty("pid").GetInt32().Should().Be(777);
        record.RootElement.GetProperty("startedAt").GetDateTimeOffset().Should().Be(_processes.CurrentProcessStartedAt);
        record.RootElement.GetProperty("machine").GetString().Should().Be(Environment.MachineName);
    }

    [Fact]
    public void Read_while_held_reports_held()
    {
        // A second open of the file in this same process conflicts on both
        // platforms: Windows by sharing, Unix because flock belongs to the
        // open file description, not the process.
        using var held = new FileStream(Path.Combine(_run, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        Ownership().Read(_run).Should().BeOfType<OwnerReading.Held>();
    }

    [Fact]
    public void Read_after_release_reports_free_with_the_owner()
    {
        var ownership = Ownership();

        ownership.Take(_run).Value!.Dispose();

        var reading = ownership.Read(_run).Should().BeOfType<OwnerReading.Free>().Subject;

        reading.LastOwner.Should().NotBeNull();
        reading.LastOwner!.ProcessId.Should().Be(777);
        reading.LastOwner.StartedAt.Should().Be(_processes.CurrentProcessStartedAt);
        reading.LastOwner.Machine.Should().Be(Environment.MachineName);
    }

    [Fact]
    public void Read_with_no_file_reports_no_record_and_creates_nothing()
    {
        Ownership().Read(_run).Should().BeOfType<OwnerReading.NoRecord>();

        Directory.EnumerateFileSystemEntries(_run).Should().BeEmpty("asking about a run must not make it look owned");
    }

    [Fact]
    public void Take_while_held_is_refused_after_its_deadline()
    {
        var patience = TimeSpan.FromMilliseconds(300);

        using var held = new FileStream(Path.Combine(_run, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var waited = Stopwatch.StartNew();
        var taken = new FileRunOwnership(_processes, TimeProvider.System, patience).Take(_run);
        waited.Stop();

        taken.Succeeded.Should().BeFalse();
        taken.ExitCode.Should().Be(ExitCode.PolicyViolation);
        waited.Elapsed.Should().BeGreaterThanOrEqualTo(patience, "somebody looking holds the lock for a moment, so a take waits before refusing");
    }

    [Fact]
    public void Take_while_another_take_holds_is_refused()
    {
        // The runner's own take against close's take, as both really happen,
        // rather than a raw stream standing in for either.
        using var first = Ownership().Take(_run).Value!;

        var second = new FileRunOwnership(_processes, TimeProvider.System, TimeSpan.FromMilliseconds(200)).Take(_run);

        second.Succeeded.Should().BeFalse("one process already drives the run");
        second.ExitCode.Should().Be(ExitCode.PolicyViolation);
    }

    [DeniableFact]
    public void Read_of_an_owner_file_that_cannot_be_opened_is_unknown()
    {
        var path = Path.Combine(_run, "owner.lock");

        Ownership().Take(_run).Value!.Dispose();
        Unreadable(path, true);

        try
        {
            FluentActions.Invoking(() => File.OpenRead(path).Dispose())
                .Should().Throw<UnauthorizedAccessException>("the fixture has to be a file this process cannot open");

            var reading = Ownership().Read(_run).Should().BeOfType<OwnerReading.Unknown>(
                "a denial is not somebody holding the run, and taking it for one keeps the run live for good").Subject;

            reading.Why.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            Unreadable(path, false);
        }
    }

    /// <summary>Takes away, or gives back, this process's right to read a file.</summary>
    private static void Unreadable(string path, bool denied)
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new FileInfo(path);
            var security = info.GetAccessControl();
            var rule = new System.Security.AccessControl.FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                System.Security.AccessControl.FileSystemRights.ReadData,
                System.Security.AccessControl.AccessControlType.Deny);

            if (denied)
            {
                security.AddAccessRule(rule);
            }
            else
            {
                security.RemoveAccessRule(rule);
            }

            info.SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(path, denied ? UnixFileMode.None : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private FileRunOwnership Ownership() => new(_processes, TimeProvider.System);
}
