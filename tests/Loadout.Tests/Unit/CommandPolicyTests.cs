using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Core.Policies;
using Loadout.Models.Policies;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which commands an agent may run, and which half of that answer is allowed to
/// travel between people.
/// </summary>
/// <remarks>
/// Denial is shared, because tightening is safe to hand somebody: the worst a
/// bad entry does is stop something that should have run, visibly, on the
/// machine of whoever hits it. Pre-approval is machine-local, because it removes
/// an approval prompt — and a file that travels between people is a file that
/// can remove somebody else's.
/// </remarks>
public sealed class CommandPolicyTests
{
    [Fact]
    public void A_denied_command_overrules_a_local_pre_approval()
    {
        var resolved = CommandPolicy.Resolve(
            denied: ["git push"],
            preApproved: ["npm test", "git push"]);

        // The whole rule. Any other answer would make the shared half advisory,
        // and a security control a local file can switch off is not one.
        resolved.PreApproved.Should().Equal("npm test");
        resolved.Overruled.Should().Equal("git push");
    }

    [Fact]
    public void A_denial_covers_the_command_and_its_arguments()
    {
        var resolved = CommandPolicy.Resolve(
            denied: ["git push"],
            preApproved: ["git push --force"]);

        // Denying 'git push' and still pre-approving 'git push --force' would
        // be worse than not denying it at all.
        resolved.PreApproved.Should().BeEmpty();
        resolved.Overruled.Should().Equal("git push --force");
    }

    [Fact]
    public void A_denial_matches_whole_words_rather_than_characters()
    {
        var resolved = CommandPolicy.Resolve(
            denied: ["rm"],
            preApproved: ["rmdir build", "rm -rf node_modules"]);

        // Prefix matching on raw characters would take 'rmdir' too, and a
        // command mysteriously refused is a hard thing to trace back to here.
        resolved.PreApproved.Should().Equal("rmdir build");
        resolved.Overruled.Should().Equal("rm -rf node_modules");
    }

    [Fact]
    public void Extra_spacing_does_not_get_a_command_past_a_denial()
    {
        var resolved = CommandPolicy.Resolve(
            denied: ["git  push"],
            preApproved: ["git push"]);

        // A rule that can be evaded by typing two spaces is not a rule.
        resolved.Denied.Should().Equal("git push");
        resolved.PreApproved.Should().BeEmpty();
    }

    [Fact]
    public void Nothing_configured_denies_and_approves_nothing()
    {
        var resolved = CommandPolicy.Resolve(null, null);

        resolved.Denied.Should().BeEmpty();
        resolved.PreApproved.Should().BeEmpty();
        resolved.Overruled.Should().BeEmpty();
    }

    [Fact]
    public void A_command_becomes_a_specifier_that_covers_its_arguments()
    {
        // 'Bash(git push)' would match the bare word and nothing else, so a
        // denial written that way would fail open on every real invocation.
        ClaudeAdapter.Specifiers(["git push"]).Should().Equal("Bash(git push:*)");
    }

    [Fact]
    public void A_specifier_somebody_wrote_by_hand_is_left_alone()
    {
        // Wrapping it again produces Bash(Bash(...):*), which matches nothing —
        // and matching nothing is the worst way for a denial to be wrong.
        ClaudeAdapter.Specifiers(["Bash(git push:*)", "Read(/etc/**)"])
            .Should().Equal("Bash(git push:*)", "Read(/etc/**)");
    }

    [Fact]
    public async Task A_shared_profile_cannot_pre_approve_tools()
    {
        var invocation = await BuildAsync(new SecurityProfile
        {
            AllowedTools = { "Bash", "WebFetch" },
        });

        // The hole this decision closes. --allowed-tools pre-approves rather
        // than restricts, so honouring it from a workspace file would switch
        // off the approval prompts of everyone who clones that workspace.
        invocation.Arguments.Should().NotContain("--allowed-tools");
        invocation.Warnings.Should().NotBeNull();
        invocation.Warnings!.Should().Contain(w => w.Contains("allowed_tools", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_denied_command_reaches_claude_as_a_denial()
    {
        var invocation = await BuildAsync(new SecurityProfile
        {
            DeniedCommands = { "git push" },
            DisallowedTools = { "WebFetch" },
        });

        invocation.Arguments.Should().ContainInOrder(
            "--disallowed-tools", "WebFetch,Bash(git push:*)");
    }

    [Fact]
    public async Task A_machine_local_pre_approval_reaches_claude()
    {
        var invocation = await BuildAsync(new SecurityProfile(), preApproved: ["npm test"]);

        invocation.Arguments.Should().ContainInOrder("--allowed-tools", "Bash(npm test:*)");
    }

    /// <summary>
    /// Builds an invocation without needing Claude on this machine.
    /// </summary>
    /// <remarks>
    /// The resolver and the help probe are both stubbed. A test that used the
    /// real ones would pass here, where Claude is installed, and on CI it would
    /// either fail or — worse — pass because the invocation was never built and
    /// every assertion about its arguments was vacuous.
    /// </remarks>
    private static async Task<AgentInvocation> BuildAsync(
        SecurityProfile profile,
        IReadOnlyList<string>? preApproved = null)
    {
        const string Help = """
            Usage: claude [options]
              --allowed-tools <tools>
              --disallowed-tools <tools>
              --permission-mode <mode>
            """;

        var adapter = new ClaudeAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "claude")),
            new StubProcessLauncher(Help),
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
            [],
            null,
            null,
            null,
            profile,
            null,
            null,
            preApproved);

        var result = await adapter.BuildInvocationAsync(context);

        result.Succeeded.Should().BeTrue(result.Error ?? "the invocation has to build");

        return result.Value!;
    }
}
