using FluentAssertions;
using Loadout.Core.Capabilities;
using Loadout.Models.Capabilities;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a repository's own files say about it: which workflows are CI, which
/// release, which only call a shared workflow, and what kinds of project it holds.
/// </summary>
public sealed class LocalProbesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-probes-" + Guid.NewGuid().ToString("N"));

    public LocalProbesTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("on: pull_request\njobs:\n  b:\n    runs-on: x\n    steps: []\n", true, false)]
    [InlineData("on: [push, pull_request]\njobs:\n  b:\n    steps: []\n", true, false)]
    [InlineData("on:\n  push:\n    branches: [main]\njobs:\n  b:\n    steps: []\n", true, false)]
    [InlineData("on:\n  push:\n    tags: ['v*']\njobs:\n  b:\n    steps: []\n", false, true)]
    [InlineData("on:\n  release:\n    types: [published]\njobs:\n  b:\n    steps: []\n", false, true)]
    [InlineData("on: schedule\njobs:\n  b:\n    steps: []\n", false, false)]
    public void A_workflow_is_ci_or_release_by_what_it_runs_on(string text, bool ci, bool release)
    {
        var read = LocalProbes.Read("w.yml", text)!;

        read.Ci.Should().Be(ci);
        read.Release.Should().Be(release);
    }

    [Fact]
    public void A_workflow_run_by_hand_releases_only_when_its_name_says_so()
    {
        LocalProbes.Read("publish.yml", "on: workflow_dispatch\njobs:\n  b:\n    steps: []\n")!.Release.Should().BeTrue();
        LocalProbes.Read("w.yml", "name: Release\non: workflow_dispatch\njobs:\n  b:\n    steps: []\n")!.Release.Should().BeTrue();
        LocalProbes.Read("tidy.yml", "on: workflow_dispatch\njobs:\n  b:\n    steps: []\n")!.Release.Should().BeFalse();
    }

    [Fact]
    public void A_workflow_whose_every_job_calls_a_shared_one_is_delegated()
    {
        var calling = LocalProbes.Read("ci.yml", "on: pull_request\njobs:\n  b:\n    uses: org/shared/.github/workflows/ci.yml@v1\n")!;
        var mixed = LocalProbes.Read(
            "ci.yml", "on: pull_request\njobs:\n  a:\n    uses: org/shared/.github/workflows/ci.yml@v1\n  b:\n    steps: []\n")!;

        calling.Delegated.Should().BeTrue();
        calling.Calls.Should().Equal("org/shared/.github/workflows/ci.yml@v1");
        mixed.Delegated.Should().BeFalse("a job with steps of its own does work here");
    }

    [Theory]
    [InlineData("on: [push\n")]
    [InlineData("- just\n- a list\n")]
    public void Text_that_cannot_be_a_workflow_is_not_one(string text) => LocalProbes.Read("w.yml", text).Should().BeNull();

    [Fact]
    public void Stacks_come_nearest_the_root_first_and_a_tie_goes_to_the_fixed_order()
    {
        Write("package.json", "{}");
        Write("App.slnx", "<Solution />");
        Write("tools/pyproject.toml", string.Empty);
        Write("node_modules/x/Cargo.toml", string.Empty);

        LocalProbes.Stacks(_root).Select(stack => (stack.Name, stack.Manifest)).Should().Equal(
            ("dotnet", "App.slnx"),
            ("node", "package.json"),
            ("python", "tools/pyproject.toml"));
    }

    [Fact]
    public void A_stack_no_workflow_appears_to_build_is_noted_only_when_there_is_more_than_one()
    {
        var stacks = new List<CapabilityStack> { new() { Name = "dotnet" }, new() { Name = "node" } };
        var dotnetOnly = LocalProbes.Read("ci.yml", "on: pull_request\njobs:\n  b:\n    steps:\n      - run: dotnet test\n")!;

        LocalProbes.Unbuilt(stacks, [dotnetOnly]).Should().Equal("node");
        LocalProbes.Unbuilt([stacks[0]], [dotnetOnly]).Should().BeEmpty();
    }

    [Fact]
    public void A_file_that_is_not_a_workflow_is_named_rather_than_counted()
    {
        Write(".github/workflows/ci.yml", "on: pull_request\njobs:\n  b:\n    steps: []\n");
        Write(".github/workflows/broken.yml", "on: [push\n");
        Write(".github/workflows/readme.md", "not yaml");

        var (workflows, unreadable) = LocalProbes.Workflows(_root);

        workflows.Select(one => one.File).Should().Equal("ci.yml");
        unreadable.Should().Equal("broken.yml");
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
