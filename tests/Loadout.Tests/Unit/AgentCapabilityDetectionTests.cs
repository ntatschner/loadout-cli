using FluentAssertions;
using Loadout.Agents.Claude;
using Loadout.Platform.Common;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading an agent's help to find out what it can be asked to do.
/// </summary>
/// <remarks>
/// <para>
/// A capability missed here fails silently and completely. Claude Code 2.1
/// documents its file-based system prompt flag only inside another option's
/// description, and only as <c>--append-system-prompt[-file]</c> — brackets
/// meaning the suffix is optional. Looking for the plain spelling found
/// nothing, the capability was recorded as absent on a build that has it, the
/// launcher fell back to passing the context on the command line, a 39KB
/// context did not fit, and the agent was started with no instructions,
/// no specialists and no memory index at all.
/// </para>
/// <para>
/// Nothing failed. The session simply began knowing none of what the workspace
/// exists to tell it.
/// </para>
/// </remarks>
public sealed class AgentCapabilityDetectionTests
{
    /// <summary>
    /// The shape Claude Code 2.1 actually prints — the file form appears only
    /// in another option's prose, never as an entry of its own.
    /// </summary>
    private const string BracketedHelp = """
  --append-system-prompt <prompt>       Append a system prompt to the default
                                        system prompt
  --agents <json>                       Explicitly provide context via:
                                        --system-prompt[-file],
                                        --append-system-prompt[-file], --add-dir
""";

    /// <summary>The shape it would print if the flag had its own entry.</summary>
    private const string PlainHelp = """
  --append-system-prompt-file <path>    Append a system prompt read from a file
""";

    private const string WithoutIt = """
  --append-system-prompt <prompt>       Append a system prompt to the default
                                        system prompt
""";

    private static async Task<bool> DetectsFileFlagAsync(string help)
    {
        var adapter = new ClaudeAdapter(
            new ExecutableResolver(
                new FakeEnvironmentProvider(Path.GetTempPath())
                {
                    PathDirectories = Environment.GetEnvironmentVariable("PATH")?
                        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [],
                    ExecutableExtensions = OperatingSystem.IsWindows()
                        ? [".exe", ".cmd", ".bat"]
                        : [string.Empty],
                },
                []),
            new StubProcessLauncher(help),
            []);

        var descriptor = await adapter.DetectAsync();

        return descriptor.Supports("external_prompt_file");
    }

    [Fact]
    public async Task The_bracketed_spelling_counts()
    {
        // This is the one that was missed, on a build that had the flag.
        (await DetectsFileFlagAsync(BracketedHelp)).Should().BeTrue(
            "Claude Code writes it as --append-system-prompt[-file] and nowhere writes it plainly");
    }

    [Fact]
    public async Task The_plain_spelling_still_counts()
    {
        (await DetectsFileFlagAsync(PlainHelp)).Should().BeTrue();
    }

    [Fact]
    public async Task A_build_without_it_is_still_reported_as_without_it()
    {
        // Widening what counts must not make every build look capable. A
        // capability wrongly assumed present passes an argument the agent
        // rejects, which fails the launch outright.
        (await DetectsFileFlagAsync(WithoutIt)).Should().BeFalse();
    }
}
