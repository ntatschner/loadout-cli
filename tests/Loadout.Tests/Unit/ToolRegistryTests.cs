using FluentAssertions;
using Loadout.Models.Tools;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The rules the catalogue enforces itself rather than trusting whoever writes
/// its files: written once, active only when known-good, and never active once
/// the files stop matching.
/// </summary>
public sealed class ToolRegistryTests : IDisposable
{
    private const string Script = "param([string]$CachePath)\nGet-ChildItem $CachePath | Remove-Item\n";

    private readonly ToolStoreFixture _store = new();

    public void Dispose() => _store.Dispose();

    [Fact]
    public async Task Promote_refuses_an_existing_version_directory()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        var again = ToolStoreFixture.Manifest("free-cache", "1.0");
        var cases = ToolStoreFixture.Cases();
        var draft = _store.Draft(again, Script + "# changed\n", cases);
        (await registry.VerifyAsync(draft, ToolStoreFixture.Agreed(again, Script + "# changed\n", cases))).Succeeded.Should().BeTrue();

        var promoted = registry.Promote(draft, new("bug", "inbox/again"));

        promoted.Failed.Should().BeTrue();
        promoted.Error.Should().Contain("written once");
    }

    [Fact]
    public async Task SetActive_refuses_a_version_not_known_good()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        // A version directory that matches its own manifest but never went
        // through the gate: somebody copied it in by hand.
        var versions = Path.Combine(registry.Root(), "free-cache", "versions");
        Copy(Path.Combine(versions, "1.0"), Path.Combine(versions, "1.1"));

        var set = registry.SetActive("free-cache", "1.1");

        set.Failed.Should().BeTrue();
        registry.Show("free-cache").Value!.Record.Active.Should().Be("1.0");
    }

    [Fact]
    public async Task A_tampered_version_is_never_active()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());
        registry.Show("free-cache").Value!.Active.Should().NotBeNull();

        var script = Path.Combine(registry.Root(), "free-cache", "versions", "1.0", "free-cache.v1.0.ps1");
        File.AppendAllText(script, "Remove-Item -Recurse /\n");

        var shown = registry.Show("free-cache").Value!;

        shown.Active.Should().BeNull();
        shown.Versions["1.0"].Should().Be(ToolVersionStatus.Tampered);
        registry.SetActive("free-cache", "1.0").Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("purpose")]
    [InlineData("inputs")]
    [InlineData("outputs")]
    [InlineData("dependencies")]
    [InlineData("constraints")]
    [InlineData("error_behaviour")]
    [InlineData("examples")]
    [InlineData("origin")]
    public async Task Promote_refuses_a_manifest_missing_a_field(string field)
    {
        var (registry, _) = _store.Registry();
        var manifest = ToolStoreFixture.Manifest("free-cache", "1.0");

        switch (field)
        {
            case "purpose": manifest.Purpose = string.Empty; break;
            case "inputs": manifest.Inputs = []; break;
            case "outputs": manifest.Outputs = new ToolOutputs(); break;
            case "dependencies": manifest.Dependencies = []; break;
            case "constraints": manifest.Constraints = []; break;
            case "error_behaviour": manifest.ErrorBehaviour = string.Empty; break;
            case "examples": manifest.Examples = []; break;
            case "origin": manifest.Origin = string.Empty; break;
        }

        var cases = ToolStoreFixture.Cases();
        var draft = _store.Draft(manifest, Script, cases);
        await registry.VerifyAsync(draft, ToolStoreFixture.Agreed(manifest, Script, cases));

        var promoted = registry.Promote(draft, new("lesson", "inbox/test"));

        promoted.Failed.Should().BeTrue();
        promoted.Error.Should().Contain(field);
        Directory.Exists(Path.Combine(registry.Root(), "free-cache", "versions", "1.0")).Should().BeFalse();
    }

    [Fact]
    public async Task Deprecate_needs_a_replacement_or_a_reason()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        registry.Deprecate("free-cache", null, "  ").Failed.Should().BeTrue();
        registry.Show("free-cache").Value!.Record.Lifecycle.Should().Be(ToolLifecycle.Active);

        registry.Deprecate("free-cache", null, "Nothing uses a cache like this any more.").Succeeded.Should().BeTrue();
        registry.Show("free-cache").Value!.Record.Lifecycle.Should().Be(ToolLifecycle.Deprecated);
    }

    [Fact]
    public async Task The_times_a_tool_records_survive_being_written_and_read_back()
    {
        // Every date the catalogue writes went out as the struct's own
        // properties and came back as 0001-01-01: git-tree-clean@1.0 was
        // promoted saying its cases last ran at the start of the calendar.
        var now = new DateTimeOffset(2026, 9, 24, 18, 44, 0, TimeSpan.Zero);
        var (registry, _) = _store.Registry(clock: new At(now));
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        registry.Deprecate("free-cache", null, "Nothing uses a cache like this any more.").Succeeded.Should().BeTrue();
        var shown = registry.Show("free-cache").Value!;

        shown.Active!.Tests!.RanAt.Should().Be(now, "that is when verify ran the cases");
        shown.Record.Deprecated!.At.Should().Be(now, "that is when it was deprecated");

        // And written as one readable time, not the struct's properties, which
        // the reader copes with but nobody reading the file should have to.
        File.ReadAllText(Path.Combine(registry.Root(), "free-cache", "versions", "1.0", "manifest.yaml"))
            .Should().Contain("ran_at: 2026-09-24T18:44:00.0000000+00:00").And.NotContain("utc_date_time");
    }

    [Fact]
    public async Task A_manifest_written_with_the_old_form_of_a_time_still_reads()
    {
        var now = new DateTimeOffset(2026, 9, 24, 18, 44, 0, TimeSpan.Zero);
        var (registry, _) = _store.Registry(clock: new At(now));
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        // What versions promoted before the fix hold: the struct's properties,
        // of which only utc_date_time says which instant it means.
        var manifest = Path.Combine(registry.Root(), "free-cache", "versions", "1.0", "manifest.yaml");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(
            "ran_at: 2026-09-24T18:44:00.0000000+00:00",
            "ran_at:\n    date_time: 2026-09-24T19:44:00.0000000\n    utc_date_time: 2026-09-24T18:44:00.0000000Z\n    offset: 01:00:00",
            StringComparison.Ordinal));

        registry.Show("free-cache").Value!.Active!.Tests!.RanAt.Should().Be(now);
    }

    private sealed class At(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
