using FluentAssertions;
using Loadout.Core.Tools;
using Loadout.Models.Teams;
using Loadout.Models.Tools;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The harness, through <see cref="StubProcessLauncher" /> so that no test
/// here needs pwsh on the machine.
/// </summary>
public sealed class ToolHarnessTests : IDisposable
{
    private const string Script = "param([string]$CachePath)\nGet-ChildItem $CachePath\n";

    private readonly ToolStoreFixture _store = new();

    public void Dispose() => _store.Dispose();

    [Fact]
    public async Task Verify_refuses_without_all_four_case_classes()
    {
        var (registry, launcher) = _store.Registry();
        var manifest = ToolStoreFixture.Manifest("free-cache", "1.0");
        var cases = ToolStoreFixture.Cases().Where(one => one.Class != ToolCaseClass.Edge).ToList();
        var draft = _store.Draft(manifest, Script, cases);

        var verified = await registry.VerifyAsync(draft, ToolStoreFixture.Agreed(manifest, Script, cases));

        verified.Failed.Should().BeTrue();
        verified.Error.Should().Contain(ToolCaseClass.Edge);
        launcher.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{tmp}/../escape")]
    [InlineData("{tmp}\\..\\escape")]
    [InlineData("../escape")]
    [InlineData("cache/../../escape")]
    public async Task An_argument_climbing_out_of_tmp_is_refused_without_running(string value)
    {
        var launcher = new StubProcessLauncher(string.Empty);
        var harness = new ToolHarness(launcher);

        var result = await harness.RunAsync(
            "tool.ps1",
            new ToolCase { Name = "a", Class = ToolCaseClass.Success, Args = new() { ["CachePath"] = value } });

        result.Passed.Should().BeFalse();
        result.Why.Should().Contain("..");
        launcher.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Cases_run_in_a_fresh_temp_directory()
    {
        var launcher = new StubProcessLauncher(string.Empty);
        var harness = new ToolHarness(launcher);
        var cases = new List<ToolCase>
        {
            new() { Name = "a", Class = ToolCaseClass.Success, Args = new() { ["CachePath"] = "{tmp}/cache" } },
            new() { Name = "b", Class = ToolCaseClass.Success, Args = new() { ["CachePath"] = "{tmp}/cache" } },
        };

        var results = await harness.RunAllAsync("tool.ps1", cases);

        results.Should().OnlyContain(one => one.Passed);
        var directories = launcher.Requests.Select(one => one.WorkingDirectory).ToList();
        directories.Should().HaveCount(2).And.OnlyHaveUniqueItems().And.NotContainNulls();
        launcher.Requests[0].Arguments.Should().Contain(directories[0] + "/cache");
        directories.Should().OnlyContain(one => !Directory.Exists(one));
    }

    [Fact]
    public async Task Running_is_held_for_a_person_by_default()
    {
        var (registry, launcher) = _store.Registry();
        var manifest = ToolStoreFixture.Manifest("free-cache", "1.0");
        var cases = ToolStoreFixture.Cases();
        var draft = _store.Draft(manifest, Script, cases);

        var verified = await registry.VerifyAsync(draft, new ToolTestConsent(null, null));

        verified.Value!.Ruling.Should().Be(RemedyRuling.Ask);
        verified.Value.Gate.Should().BeNull();
        launcher.Requests.Should().BeEmpty();
    }
}
