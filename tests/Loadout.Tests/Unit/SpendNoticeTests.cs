using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Statusline;
using Loadout.Core.Usage;
using Loadout.Models.Configuration;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The written-down answer the status line reads instead of working it out.
/// </summary>
/// <remarks>
/// The line is redrawn several times a minute and the scan behind this figure
/// takes seconds, so the two are deliberately separated: something else fills
/// the file, and the line only ever reads it.
/// </remarks>
public sealed class SpendNoticeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static (SpendNoticeStore Store, string Home) Fresh()
    {
        var home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        Directory.CreateDirectory(home);

        var permissions = new NoOpFilePermissions();

        // Built the way the integration tests build one: a real paths
        // implementation over a fake environment, so the layout under test is
        // the layout that ships rather than one invented for the test.
        var paths = new Loadout.Platform.Linux.LinuxPaths(
            new FakeEnvironmentProvider(
                Path.Combine(home, "home"),
                new Dictionary<string, string>
                {
                    ["XDG_CONFIG_HOME"] = Path.Combine(home, "config"),
                    ["XDG_DATA_HOME"] = Path.Combine(home, "data"),
                    ["XDG_STATE_HOME"] = Path.Combine(home, "state"),
                    ["XDG_CACHE_HOME"] = Path.Combine(home, "cache"),
                }),
            permissions,
            new Loadout.Models.Platform.HostPlatform(
                Loadout.Models.Platform.HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));

        paths.EnsureDirectoriesExist();

        return (new SpendNoticeStore(paths, new YamlStore(permissions)), home);
    }

    [Fact]
    public async Task Nothing_written_yet_reads_as_no_answer()
    {
        var (store, home) = Fresh();

        try
        {
            (await store.ReadAsync("demo")).Should().BeNull();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task What_was_written_comes_back_with_when_it_was_worked_out()
    {
        var (store, home) = Fresh();

        try
        {
            await store.WriteAsync("demo", ["today: 12,000 tokens against a threshold of 10,000"]);

            var read = await store.ReadAsync("demo");

            read.Should().NotBeNull();
            read!.Lines.Should().ContainSingle().Which.Should().Contain("12,000");

            // The time is part of the record rather than an afterthought: a
            // figure about spending that cannot say how old it is invites being
            // read as live, and this one never is.
            read.ComputedUtc.Should().NotBe(default);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task An_empty_answer_is_recorded_rather_than_leaving_the_old_one()
    {
        var (store, home) = Fresh();

        try
        {
            await store.WriteAsync("demo", ["today: over"]);
            await store.WriteAsync("demo", []);

            // Nothing crossed is a real answer. Leaving the previous one in
            // place would keep flagging a threshold that is no longer crossed,
            // which is how a warning stops meaning anything.
            (await store.ReadAsync("demo"))!.Lines.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task A_cache_that_cannot_be_read_is_no_answer_rather_than_a_failure()
    {
        var (store, home) = Fresh();

        try
        {
            await store.WriteAsync("demo", ["today: over"]);

            var file = Directory.EnumerateFiles(home, "demo.yaml", SearchOption.AllDirectories).Single();

            await File.WriteAllTextAsync(file, "this is not: [ yaml");

            // This sits on the path that draws somebody's prompt. Nothing about
            // spending is worth a prompt failing to render.
            (await store.ReadAsync("demo")).Should().BeNull();

            // And a file that parses perfectly well but carries no time is no
            // answer either. Without that check it comes back as a real one
            // stamped in the year one, which would then be reported as an
            // hour, a day and two thousand years out of date.
            await File.WriteAllTextAsync(file, "schema_version: 1" + Environment.NewLine
                + "lines:" + Environment.NewLine + "  - \"today: over\"" + Environment.NewLine);

            (await store.ReadAsync("demo")).Should().BeNull();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void Only_one_caller_gets_to_start_a_refresh()
    {
        var (store, home) = Fresh();

        try
        {
            var after = TimeSpan.FromMinutes(15);

            store.ClaimRefresh("demo", Now, after).Should().BeTrue();

            // The whole point of claiming. The status line runs several times a
            // minute, and without this every one of them would see the same
            // stale file and start its own two-second scan.
            store.ClaimRefresh("demo", Now, after).Should().BeFalse();
            store.ClaimRefresh("demo", Now.AddMinutes(14), after).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void A_refresh_can_be_claimed_again_once_the_window_has_passed()
    {
        var (store, home) = Fresh();

        try
        {
            var after = TimeSpan.FromMinutes(15);

            store.ClaimRefresh("demo", Now, after).Should().BeTrue();
            store.ClaimRefresh("demo", Now.AddMinutes(16), after).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void One_project_s_claim_is_not_another_s()
    {
        var (store, home) = Fresh();

        try
        {
            var after = TimeSpan.FromMinutes(15);

            store.ClaimRefresh("demo", Now, after).Should().BeTrue();
            store.ClaimRefresh("other", Now, after).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void The_segment_appears_only_when_there_is_something_to_say()
    {
        var settings = new StatuslineSettings { Colour = false };

        string Render(SpendNotice? notice) =>
            StatuslineRenderer.Render(
                new StatuslineInputs(null, "demo", null, null, notice), settings);

        Render(null).Should().NotContain("spend");
        Render(new SpendNotice { ComputedUtc = Now }).Should().NotContain("spend");

        Render(new SpendNotice { ComputedUtc = Now, Lines = ["today: over"] })
            .Should().Contain("spend");
    }

    [Fact]
    public void The_segment_can_be_turned_off()
    {
        var line = StatuslineRenderer.Render(
            new StatuslineInputs(
                null,
                "demo",
                null,
                null,
                new SpendNotice { ComputedUtc = Now, Lines = ["today: over"] }),
            new StatuslineSettings { Colour = false, ShowSpend = false });

        line.Should().NotContain("spend");
    }
}
