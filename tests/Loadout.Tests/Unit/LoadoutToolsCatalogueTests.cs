using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The catalogue's MCP calls, against a real catalogue in a temporary state
/// directory, because what matters is what reaches its files.
/// </summary>
public sealed class LoadoutToolsCatalogueTests : IDisposable
{
    private readonly ToolStoreFixture _store = new();

    public void Dispose() => _store.Dispose();

    /// <summary>
    /// Only the catalogue is real. The rest are not reached on this path.
    /// </summary>
    private static LoadoutTools Tools(Loadout.Core.Tools.IToolRegistry catalogue) =>
        new(
            instructions: null!,
            memory: null!,
            workspace: null!,
            projects: null!,
            tasks: null!,
            git: null!,
            symbols: null!,
            runs: null!,
            TimeProvider.System,
            new LoadoutToolScope(null),
            catalogue);

    [Fact]
    public void loadout_tools_submit_screens_secrets()
    {
        var (registry, _) = _store.Registry();
        var token = "ghp_" + new string('c', 36);

        var answer = Tools(registry).ToolsSubmit(
            "lesson",
            "Cloning needed a token, so I used " + token + " and it worked.");

        answer.Should().NotStartWith("Submitted");
        answer.Should().NotContain(token);

        var inbox = Path.Combine(registry.Root(), "inbox");

        (Directory.Exists(inbox) ? Directory.EnumerateFiles(inbox, "*", SearchOption.AllDirectories) : [])
            .Should().BeEmpty("a submission carrying a credential is refused, not stored");

        if (Directory.Exists(registry.Root()))
        {
            foreach (var file in Directory.EnumerateFiles(registry.Root(), "*", SearchOption.AllDirectories))
            {
                File.ReadAllText(file).Should().NotContain(token);
            }
        }
    }

    [Fact]
    public void A_clean_submission_reaches_the_inbox()
    {
        var (registry, _) = _store.Registry();

        var answer = Tools(registry).ToolsSubmit("idea", "A tool that clears a named cache directory.");

        answer.Should().StartWith("Submitted as ");
        Directory.EnumerateFiles(Path.Combine(registry.Root(), "inbox"), "*", SearchOption.AllDirectories)
            .Should().NotBeEmpty();
    }
}
