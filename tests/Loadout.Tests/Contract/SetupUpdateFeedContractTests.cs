using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// How setup is told where this machine should look for new versions.
/// </summary>
/// <remarks>
/// From outside the process, because the flags are the parser's business and a
/// test calling the wizard directly would not notice an option the command line
/// rejects. Provisioning a machine without answering prompts is the whole point
/// of the scripted path, so a flag that only works interactively is a flag that
/// does not work.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class SetupUpdateFeedContractTests
{
    [BuiltCliFact]
    public async Task Both_answers_at_once_are_refused()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(
            "setup", "--local-only", "--non-interactive",
            "--update-feed", "https://internal.example/feed.json", "--no-update-feed");

        var said = run.StandardOutput + run.StandardError;

        // A URL and "never check" are contradictory, and picking one silently
        // would leave somebody provisioning machines believing the other.
        run.ExitCode.Should().NotBe(0);
        said.Should().Contain("--no-update-feed");
    }

    [BuiltCliFact]
    public async Task Declining_is_written_to_this_machine_s_configuration()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(
            "setup", "--local-only", "--non-interactive", "--no-update-feed");

        run.ExitCode.Should().Be(0, run.StandardError);

        // The file, not the command's report. A flag that is parsed, validated
        // and then dropped on the floor passes every test that only reads what
        // was printed — which is exactly what the first version of this did.
        Configuration(loadout.Home).Should().Contain("source: off");
    }

    [BuiltCliFact]
    public async Task A_feed_of_your_own_is_written_as_given()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(
            "setup", "--local-only", "--non-interactive",
            "--update-feed", "https://internal.example/feed.json");

        run.ExitCode.Should().Be(0, run.StandardError);

        Configuration(loadout.Home).Should().Contain("https://internal.example/feed.json");
    }

    [BuiltCliFact]
    public async Task Saying_nothing_leaves_the_default_in_place()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("setup", "--local-only", "--non-interactive");

        run.ExitCode.Should().Be(0, run.StandardError);

        // Empty, deliberately: an empty setting means this project's own
        // releases, so a machine set up today follows the feed wherever it
        // moves. Writing today's URL into the file would freeze it.
        Configuration(loadout.Home).Should().NotContain("source: off");
        Configuration(loadout.Home).Should().NotContain("releases/latest/download");
    }

    /// <summary>This machine's configuration, wherever setup put it.</summary>
    private static string Configuration(string home) =>
        string.Concat(Directory
            .EnumerateFiles(home, "config.yaml", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

    [BuiltCliFact]
    public async Task The_flags_are_ones_the_parser_knows()
    {
        using var loadout = new LoadoutProcess();

        foreach (var arguments in new[]
        {
            new[] { "setup", "--no-update-feed", "--dry-run" },
            ["setup", "--update-feed", "https://internal.example/feed.json", "--dry-run"],
        })
        {
            var run = await loadout.RunAsync(arguments);

            var said = run.StandardOutput + run.StandardError;

            // --dry-run so this describes setup rather than running it. What
            // must never happen is the option being rejected: that is the
            // failure a new setting introduces by adding its own flag and
            // forgetting to accept it.
            said.Should().NotContain("Unknown option");
            said.Should().NotContain("Unexpected option");
        }
    }
}
