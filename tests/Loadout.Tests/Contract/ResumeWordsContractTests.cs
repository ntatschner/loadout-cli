using FluentAssertions;
using Loadout.Core.Sessions;
using Loadout.Core.Sessions.Hosting;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Every shape of command line Studio's Resume builds is one the real parser
/// accepts.
/// </summary>
/// <remarks>
/// The first draft sent '--worktree=' for "the main checkout", the unit test
/// asserting the words passed, and the parser refuses an option with an
/// empty value ("Expected an option value"): every such resume would have
/// opened a terminal that failed at once. So the words are parsed here by the
/// built launcher. The conversation is one that does not exist, so each run
/// ends at finding it and nothing is ever started.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class ResumeWordsContractTests
{
    private const string Nobody = "00000000-0000-4000-8000-00000000dead";

    public static TheoryData<string> Sheets => new()
    {
        "nothing changed",
        "everything changed",
        "fresh with nothing",
        "empty fields",
        "a task with spaces and a leading dash",
    };

    private static (HostedLaunch Sheet, bool Fresh) Sheet(string name) => name switch
    {
        "nothing changed" => (new HostedLaunch(string.Empty), false),
        "everything changed" => (new HostedLaunch(string.Empty, "claude", "ship it", "ship-it", "review",
            Profile: "database", Worktree: "feature-x", Offline: true, NoSync: true), false),
        "fresh with nothing" => (new HostedLaunch(string.Empty), true),
        "empty fields" => (new HostedLaunch(string.Empty, Task: "", TaskId: "", Mode: "", Profile: "", Worktree: ""), true),
        "a task with spaces and a leading dash" => (new HostedLaunch(string.Empty, Task: "-v fix the build"), false),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [BuiltCliTheory]
    [MemberData(nameof(Sheets))]
    public async Task Studios_resume_words_parse(string name)
    {
        var (sheet, fresh) = Sheet(name);
        var values = new ResumeValues("alpha", "claude", Nobody, null, null, null, null, null, [], []);

        var words = HistoryApi.Words(values, sheet, fresh);

        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(["resume", .. words, "--dry-run"]);
        var said = run.StandardOutput + run.StandardError;

        said.Should().Contain($"No recorded session starts with '{Nobody}'",
            $"the words '{string.Join(' ', words)}' should parse and reach the lookup, not stop at the parser");
    }
}
