using System.Text.Json;
using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Agents.Codex;
using Loadout.Core.Instructions;
using Loadout.Models.Agents;
using Loadout.Models.Configuration;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether a person's profile reaches the agent's own interface.
/// </summary>
/// <remarks>
/// <para>
/// The second piece of the accessibility work, and the one a screen-reader
/// user needs most: the guidance in the compiled context changes what an
/// agent writes, and this changes what it draws. A spinner is re-read aloud
/// on every frame whatever the prose says.
/// </para>
/// <para>
/// The help text is a stub, so these say the same thing on a runner with no
/// agent installed as on a workstation with one - and the build that does
/// not advertise the flag is as important a case as the one that does.
/// </para>
/// </remarks>
public sealed class AccessibilityAdapterTests : IDisposable
{
    private const string WithScreenReader = """
        Usage: claude [options] [command] [prompt]
          --settings <file-or-json>           Path to a settings JSON file or a JSON string
          --model <model>                     Model for the current session
          --ax-screen-reader                  Optimise output for a screen reader
        """;

    private const string WithoutScreenReader = """
        Usage: claude [options] [command] [prompt]
          --settings <file-or-json>           Path to a settings JSON file or a JSON string
          --model <model>                     Model for the current session
        """;

    private readonly string _runtime =
        Path.Combine(Path.GetTempPath(), "loadout-adapter-" + Guid.NewGuid().ToString("N"));

    public AccessibilityAdapterTests() => Directory.CreateDirectory(_runtime);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_runtime, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static AccessibilitySettings Profile(string preset) =>
        AccessibilityProfile.Resolve(new AccessibilitySettings { Preset = preset });

    private async Task<AgentInvocation> ClaudeAsync(
        string help,
        AccessibilitySettings? profile,
        HeadlessOptions? headless = null)
    {
        var adapter = new ClaudeAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "claude")),
            new StubProcessLauncher(help),
            []);

        var result = await adapter.BuildInvocationAsync(Context(profile, headless));

        result.Succeeded.Should().BeTrue(result.Error ?? "the invocation has to build");

        return result.Value!;
    }

    private AgentLaunchContext Context(AccessibilitySettings? profile, HeadlessOptions? headless) =>
        new(
            new ProjectResolution(
                new ProjectRegistryEntry { Slug = "demo", Name = "Demo" },
                Path.GetTempPath(),
                null,
                0,
                false),
            Path.GetTempPath(),
            _runtime,
            null,
            [],
            Headless: headless,
            Accessibility: profile);

    [Fact]
    public async Task A_person_who_has_set_nothing_gets_the_launch_they_always_got()
    {
        var invocation = await ClaudeAsync(WithScreenReader, profile: null);

        invocation.Arguments.Should().NotContain("--ax-screen-reader");
        invocation.Arguments.Should().NotContain("--settings");
        invocation.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task A_profile_that_wants_no_redraws_gets_the_agents_own_mode()
    {
        var invocation = await ClaudeAsync(WithScreenReader, Profile(AccessibilityPresets.ScreenReader));

        invocation.Arguments.Should().Contain("--ax-screen-reader");
        invocation.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task A_build_without_the_mode_says_so_rather_than_animating_quietly()
    {
        // The case that matters most. Somebody who set the profile and got a
        // spinner anyway would have nothing at all to go on.
        var invocation = await ClaudeAsync(WithoutScreenReader, Profile(AccessibilityPresets.ScreenReader));

        invocation.Arguments.Should().NotContain("--ax-screen-reader");

        invocation.Warnings.Should().Contain(w => w.Contains("--ax-screen-reader"))
            .Which.Should().Contain("Everything Loadout prints follows your profile");
    }

    [Fact]
    public async Task A_node_is_left_alone()
    {
        // A node has no terminal to animate, no spinner to re-read and
        // nobody to hear a bell.
        var invocation = await ClaudeAsync(
            WithScreenReader,
            Profile(AccessibilityPresets.ScreenReader),
            new HeadlessOptions());

        invocation.Arguments.Should().NotContain("--ax-screen-reader");

        // And nothing in the settings it is handed either. A node gets one of
        // those for its hooks, so the check has to be what is inside it.
        var index = invocation.Arguments.ToList().IndexOf("--settings");

        if (index >= 0)
        {
            var text = await File.ReadAllTextAsync(invocation.Arguments[index + 1]);

            text.Should().NotContain("prefersReducedMotion");
            text.Should().NotContain("terminal_bell");
        }
    }

    [Fact]
    public async Task The_profile_reaches_the_settings_the_agent_is_handed()
    {
        var invocation = await ClaudeAsync(WithScreenReader, Profile(AccessibilityPresets.ScreenReader));

        var index = invocation.Arguments.ToList().IndexOf("--settings");

        index.Should().BeGreaterThan(-1, "a person with a profile gets a settings file even with no project one");

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(invocation.Arguments[index + 1]));
        var root = document.RootElement;

        root.GetProperty("prefersReducedMotion").GetBoolean().Should().BeTrue();
        root.GetProperty("spinnerTipsEnabled").GetBoolean().Should().BeFalse();
        root.GetProperty("preferredNotifChannel").GetString().Should().Be("terminal_bell");
    }

    [Fact]
    public async Task A_profile_that_only_slows_motion_says_only_that()
    {
        var invocation = await ClaudeAsync(WithScreenReader, Profile(AccessibilityPresets.LowVision));

        invocation.Arguments.Should().NotContain("--ax-screen-reader", "nothing asked for no redraws");

        var index = invocation.Arguments.ToList().IndexOf("--settings");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(invocation.Arguments[index + 1]));

        document.RootElement.GetProperty("prefersReducedMotion").GetBoolean().Should().BeTrue();
        document.RootElement.TryGetProperty("preferredNotifChannel", out _)
            .Should().BeFalse("nobody asked for a bell");
    }

    [Fact]
    public async Task The_theme_is_left_alone()
    {
        // Claude has colour-blind-safe themes in a light and a dark variant,
        // and which of the two somebody wants is not knowable from here.
        // Choosing one would flip the colours of a terminal already set up
        // the way they like it.
        var invocation = await ClaudeAsync(WithScreenReader, Profile(AccessibilityPresets.ColourBlind));

        var index = invocation.Arguments.ToList().IndexOf("--settings");

        if (index >= 0)
        {
            var text = await File.ReadAllTextAsync(invocation.Arguments[index + 1]);

            text.Should().NotContain("theme");
        }
    }

    [Fact]
    public async Task Codex_turns_its_animations_off_and_says_what_it_cannot_do()
    {
        var adapter = new CodexAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "codex")),
            new StubProcessLauncher("Usage: codex [options]\n  --model <model>"),
            []);

        var result = await adapter.BuildInvocationAsync(
            Context(Profile(AccessibilityPresets.ScreenReader), headless: null));

        result.Succeeded.Should().BeTrue(result.Error);

        result.Value!.Arguments.Should().ContainInOrder("-c", "tui.animations=false");

        result.Value.Warnings.Should().Contain(w => w.Contains("no screen-reader mode"));
    }

    [Fact]
    public async Task Codex_is_left_alone_when_nobody_asked()
    {
        var adapter = new CodexAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "codex")),
            new StubProcessLauncher("Usage: codex [options]\n  --model <model>"),
            []);

        var result = await adapter.BuildInvocationAsync(Context(profile: null, headless: null));

        result.Value!.Arguments.Should().NotContain("tui.animations=false");
    }
}
