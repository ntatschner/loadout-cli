using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Agents.Codex;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether a session the launcher has given its work starts on it, rather than
/// at an idle prompt waiting for somebody to type.
/// </summary>
/// <remarks>
/// Found by running onboarding for real: the session opened with the right
/// instructions and then sat there, because an interactive agent waits for a
/// first message and nothing sent one. The help text is a stub, so these hold
/// on a runner with no agent installed.
/// </remarks>
public sealed class OpeningPromptTests : IDisposable
{
    private const string ClaudeHelp = """
        Usage: claude [options] [command] [prompt]
          --add-dir <directories...>          Additional directories to allow tool access to
          --model <model>                     Model for the current session
        """;

    private const string ClaudeHelpWithoutPrompt = """
        Usage: claude [options] [command]
          --add-dir <directories...>          Additional directories to allow tool access to
        """;

    private const string CodexHelp = """
        Usage: codex [OPTIONS] [PROMPT]
               codex [OPTIONS] <COMMAND> [ARGS]
          -m, --model <MODEL>
        """;

    private readonly string _runtime =
        Path.Combine(Path.GetTempPath(), "loadout-opening-" + Guid.NewGuid().ToString("N"));

    public OpeningPromptTests() => Directory.CreateDirectory(_runtime);

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

    private AgentLaunchContext Context(string? prompt, IReadOnlyList<string>? passthrough = null) =>
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
            passthrough ?? [],
            OpeningPrompt: prompt);

    private async Task<AgentInvocation> ClaudeAsync(string help, AgentLaunchContext context)
    {
        var adapter = new ClaudeAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "claude")),
            new StubProcessLauncher(help),
            []);

        var result = await adapter.BuildInvocationAsync(context);

        result.Succeeded.Should().BeTrue(result.Error);

        return result.Value!;
    }

    [Fact]
    public async Task Claude_is_started_with_its_first_message_last_and_behind_a_double_dash()
    {
        // Behind -- because --add-dir takes several values, and a prompt
        // straight after it would be read as one more directory.
        var invocation = await ClaudeAsync(ClaudeHelp, Context("Onboard this project"));

        invocation.Arguments.TakeLast(2).Should().Equal("--", "Onboard this project");
        invocation.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Passthrough_arguments_that_already_end_the_options_get_no_second_double_dash()
    {
        var invocation = await ClaudeAsync(
            ClaudeHelp, Context("Onboard this project", ["--verbose", "--"]));

        invocation.Arguments.Count(a => a == "--").Should().Be(1);
        invocation.Arguments[^1].Should().Be("Onboard this project");
    }

    [Fact]
    public async Task A_session_with_no_first_message_is_started_as_it_always_was()
    {
        var invocation = await ClaudeAsync(ClaudeHelp, Context(prompt: null));

        invocation.Arguments.Should().NotContain("--");
    }

    [Fact]
    public async Task A_build_that_takes_no_prompt_says_what_to_type_instead_of_guessing()
    {
        var invocation = await ClaudeAsync(ClaudeHelpWithoutPrompt, Context("Onboard this project"));

        invocation.Arguments.Should().NotContain("Onboard this project");
        invocation.Warnings.Should().ContainSingle().Which.Should().Contain("Type: Onboard this project");
    }

    [Fact]
    public async Task Codex_is_started_with_its_first_message_too()
    {
        var adapter = new CodexAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "codex")),
            new StubProcessLauncher(CodexHelp),
            []);

        var result = await adapter.BuildInvocationAsync(Context("Onboard this project"));

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Arguments.TakeLast(2).Should().Equal("--", "Onboard this project");
    }
}
