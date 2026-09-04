using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Core.Agents;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which model a launch asks for, and where that decision comes from.
/// </summary>
/// <remarks>
/// Nothing here infers anything. Choosing a model from how hard the work looks
/// would mean reading difficulty out of token counts, which is a guess wearing
/// a metric's clothes. This carries out a choice somebody wrote down, and says
/// nothing when they wrote none.
/// </remarks>
public sealed class ModelPolicyTests
{
    private static ProjectManifest Manifest(
        string model = "",
        params (string Mode, string Model)[] byMode)
    {
        var manifest = new ProjectManifest { Slug = "demo", Name = "Demo" };

        manifest.Agents.Model = model;

        foreach (var (mode, name) in byMode)
        {
            manifest.Agents.ModelByMode[mode] = name;
        }

        return manifest;
    }

    [Fact]
    public void A_project_with_no_model_leaves_the_agent_alone()
    {
        // The common case, and it has to stay silent: a launcher passing a flag
        // nobody asked for is its own kind of surprise.
        ModelPolicy.For(Manifest(), "implement").Should().BeNull();
        ModelPolicy.For(null, "implement").Should().BeNull();
    }

    [Fact]
    public void The_project_model_is_used_when_the_mode_names_none()
    {
        ModelPolicy.For(Manifest("big-model"), "implement").Should().Be("big-model");
        ModelPolicy.For(Manifest("big-model"), mode: null).Should().Be("big-model");
    }

    [Fact]
    public void A_mode_that_names_a_model_beats_the_project_default()
    {
        // The whole reason for pinning per mode. Review is cheaper work than
        // implement, and if the project default won here the entry would be
        // decorative.
        ModelPolicy.For(Manifest("big-model", ("review", "small-model")), "review")
            .Should().Be("small-model");

        ModelPolicy.For(Manifest("big-model", ("review", "small-model")), "implement")
            .Should().Be("big-model");
    }

    [Fact]
    public void A_blank_entry_is_the_same_as_no_entry()
    {
        ModelPolicy.For(Manifest("big-model", ("review", "   ")), "review")
            .Should().Be("big-model");

        ModelPolicy.For(Manifest("   "), "review").Should().BeNull();
    }

    [Fact]
    public async Task The_pinned_model_reaches_the_agent()
    {
        var invocation = await BuildAsync("big-model", advertisesModel: true);

        invocation.Arguments.Should().ContainInOrder("--model", "big-model");
    }

    [Fact]
    public async Task A_build_that_cannot_be_told_says_so_rather_than_running_the_wrong_one()
    {
        var invocation = await BuildAsync("big-model", advertisesModel: false);

        // Starting on a different model than the project asked for, without
        // saying so, is the gap this refuses to let disappear.
        invocation.Arguments.Should().NotContain("--model");
        invocation.Warnings.Should().NotBeNull();
        invocation.Warnings!.Should().Contain(w => w.Contains("big-model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_typed_after_the_dashes_still_wins()
    {
        var invocation = await BuildAsync(
            "big-model", advertisesModel: true, passthrough: ["--model", "typed-model"]);

        // The manifest ends the retyping; it does not take the choice away.
        // Everything after -- goes last and untouched, so the agent's own
        // last-one-wins reading of its flags leaves the typed model in force.
        var arguments = invocation.Arguments;

        arguments.Should().ContainInOrder("--model", "big-model", "--model", "typed-model");
        arguments[^1].Should().Be("typed-model");
    }

    private static async Task<AgentInvocation> BuildAsync(
        string model,
        bool advertisesModel,
        IReadOnlyList<string>? passthrough = null)
    {
        var help = advertisesModel
            ? "Usage: claude [options]\n  --model <name>\n"
            : "Usage: claude [options]\n  --verbose\n";

        var adapter = new ClaudeAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "claude")),
            new StubProcessLauncher(help),
            []);

        var context = new AgentLaunchContext(
            new ProjectResolution(
                new ProjectRegistryEntry { Slug = "demo", Name = "Demo" },
                Path.GetTempPath(),
                null,
                0,
                false),
            Path.GetTempPath(),
            Path.GetTempPath(),
            null,
            passthrough ?? [],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            model);

        var result = await adapter.BuildInvocationAsync(context);

        result.Succeeded.Should().BeTrue(result.Error ?? "the invocation has to build");

        return result.Value!;
    }
}
