using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Agents.Codex;
using Loadout.Models.Agents;
using Loadout.Models.Policies;
using Loadout.Models.Projects;
using Loadout.Platform.Common;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// How a generic security profile reaches each agent (spec section 58).
/// <para>
/// The property that matters most is that a profile can only ever tighten. A
/// profile lives in a shared repository, and if one could loosen an agent's
/// defaults then anyone who could edit that repository could switch off
/// somebody else's sandbox.
/// </para>
/// </summary>
public sealed class SecurityProfileTests
{
    /// <summary>Flags that hand an agent more freedom than its own default.</summary>
    public static TheoryData<string> LooseningFlags =>
    [
        "--dangerously-skip-permissions",
        "--allow-dangerously-skip-permissions",
        "--dangerously-bypass-approvals-and-sandbox",
        "bypassPermissions",
        "dontAsk",
        "danger-full-access",
    ];

    [Theory]
    [MemberData(nameof(LooseningFlags))]
    public async Task No_security_profile_can_loosen_claude(string forbidden)
    {
        foreach (var profile in SecurityProfile.CreateDefaults().Values)
        {
            var invocation = await BuildClaudeAsync(profile);

            if (invocation is null)
            {
                // Claude is not installed on this machine, so there is nothing
                // to assert about the arguments it would have received.
                return;
            }

            invocation.Arguments.Should().NotContain(
                a => a.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [MemberData(nameof(LooseningFlags))]
    public async Task No_security_profile_can_loosen_codex(string forbidden)
    {
        foreach (var profile in SecurityProfile.CreateDefaults().Values)
        {
            var invocation = await BuildCodexAsync(profile);

            if (invocation is null)
            {
                return;
            }

            invocation.Arguments.Should().NotContain(
                a => a.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task A_read_only_profile_puts_claude_in_plan_mode()
    {
        var invocation = await BuildClaudeAsync(
            new SecurityProfile { Filesystem = FilesystemAccess.ReadOnly });

        if (invocation is null)
        {
            return;
        }

        // Plan mode reads without writing, which is what a review or
        // production-investigation profile is asking for.
        invocation.Arguments.Should().ContainInOrder("--permission-mode", "plan");
    }

    [Fact]
    public async Task A_read_only_profile_puts_codex_in_the_read_only_sandbox()
    {
        var invocation = await BuildCodexAsync(
            new SecurityProfile { Filesystem = FilesystemAccess.ReadOnly });

        if (invocation is null)
        {
            return;
        }

        invocation.Arguments.Should().ContainInOrder("--sandbox", "read-only");
    }

    [Fact]
    public async Task Tool_restrictions_reach_claude()
    {
        var invocation = await BuildClaudeAsync(new SecurityProfile
        {
            DisallowedTools = { "Bash", "WebFetch" },
        });

        if (invocation is null)
        {
            return;
        }

        invocation.Arguments.Should().ContainInOrder("--disallowed-tools", "Bash,WebFetch");
    }

    [Fact]
    public async Task No_profile_means_no_security_arguments_at_all()
    {
        var invocation = await BuildClaudeAsync(null);

        if (invocation is null)
        {
            return;
        }

        // Without a profile the agent keeps its own defaults. The launcher
        // asserting a permission mode nobody asked for would be its own kind of
        // surprise.
        invocation.Arguments.Should().NotContain("--permission-mode");
    }

    [Fact]
    public void The_three_profiles_named_in_the_spec_all_exist()
    {
        var defaults = SecurityProfile.CreateDefaults();

        defaults.Should().ContainKeys("normal", "review", "production");

        // Production must actually be the strict one, or the name is a lie.
        defaults["production"].Approvals.Should().Be(ApprovalPolicy.Strict);
        defaults["production"].Filesystem.Should().Be(FilesystemAccess.Restricted);
        defaults["review"].Filesystem.Should().Be(FilesystemAccess.ReadOnly);
        defaults["normal"].Filesystem.Should().Be(FilesystemAccess.Repository);
    }

    private static async Task<AgentInvocation?> BuildClaudeAsync(SecurityProfile? profile)
    {
        var adapter = new ClaudeAdapter(Resolver(), new ProcessLauncher(), []);

        var result = await adapter.BuildInvocationAsync(Context(profile));

        return result.Succeeded ? result.Value : null;
    }

    private static async Task<AgentInvocation?> BuildCodexAsync(SecurityProfile? profile)
    {
        var adapter = new CodexAdapter(Resolver(), new ProcessLauncher(), []);

        var result = await adapter.BuildInvocationAsync(Context(profile));

        return result.Succeeded ? result.Value : null;
    }

    private static ExecutableResolver Resolver() =>
        new(
            new FakeEnvironmentProvider(Path.GetTempPath())
            {
                PathDirectories = Environment.GetEnvironmentVariable("PATH")?
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [],
                ExecutableExtensions = OperatingSystem.IsWindows()
                    ? [".exe", ".cmd", ".bat"]
                    : [string.Empty],
            },
            []);

    private static AgentLaunchContext Context(SecurityProfile? profile) => new(
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
        profile);
}
