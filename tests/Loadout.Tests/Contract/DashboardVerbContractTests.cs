using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Every button on the dashboard, run against the real parser.
/// </summary>
/// <remarks>
/// <para>
/// The page implements nothing: each control types the command a person would
/// have typed. That is only worth anything while the command lines it types are
/// ones the parser accepts, and nothing checked that they were.
/// </para>
/// <para>
/// The cost of not checking, the first time a button was added after the rule
/// was written down: "Forget it" passed <c>--yes</c> to <c>team runs remove</c>,
/// which never asks and therefore does not declare the option. Spectre refused
/// the line, the command did not run, and the page reported "ended with exit
/// code 1" — a failure that looks like the run being undeletable rather than
/// like a typo three files away.
/// </para>
/// <para>
/// Run against the built binary, because what is being asserted is what the
/// parser does with these strings, and the parser lives in a process.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class DashboardVerbContractTests
{
    /// <summary>
    /// Every verb, filled in as the page fills them.
    /// </summary>
    /// <remarks>
    /// The optional fields are all given values, because half of them change
    /// the command line: a "name" with a room renames and one without clears,
    /// and only one of those two shapes would be covered by leaving it null.
    /// The run identifier is a real shape and belongs to nothing, which is the
    /// point — this asserts the line parses, not that it finds anything.
    /// </remarks>
    public static TheoryData<string> Verbs
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var verb in DashboardActions.Verbs)
            {
                data.Add(verb);
            }

            return data;
        }
    }

    private static RunAction Asking(string verb) => new(
        "20260101-0000-0000",
        verb,
        Gate: "gate-1",
        Answer: "no",
        Reason: "not this time",
        Message: "something to say",
        Room: "The Back Office",
        Node: "implementer/1",
        Instead: "do the other thing",
        Budget: "40");

    [BuiltCliTheory]
    [MemberData(nameof(Verbs))]
    public async Task Every_verb_the_page_can_send_is_a_command_line_the_parser_accepts(string verb)
    {
        var (command, arguments) = DashboardActions.Maps(Asking(verb));

        command.Should().NotBeEmpty($"the page can send '{verb}', so something has to answer it");

        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(
            [.. command.Split(' '), .. arguments, "--non-interactive", "--dry-run"]);

        var everything = run.StandardOutput + run.StandardError;

        // Whether it succeeds depends on that run existing, which a throwaway
        // home has not got. What must never happen is the line being rejected
        // before the command sees it.
        everything.Should().NotContain(
            "Unknown option",
            $"'{verb}' types '{command} {string.Join(' ', arguments)}', and every option in it "
            + "has to be one that command declares");

        everything.Should().NotContain("Unexpected option", $"'{verb}' types a line the parser refused");
        everything.Should().NotContain("Unknown command", $"'{verb}' names a command that does not exist");
    }

    /// <summary>
    /// The two schedule verbs, filled in as the sheet fills them.
    /// </summary>
    /// <remarks>
    /// Every optional field is given a value, because each one becomes an
    /// option on the line and an option the command does not declare is the
    /// exact failure this file exists for. The command refuses the combination
    /// - --every, --at and --on are one-of, and the sheet only ever sends one -
    /// but refusing it is the command doing its job, and the line still has to
    /// parse before it can refuse anything.
    /// </remarks>
    public static TheoryData<string> ScheduleVerbs => ["add", "remove"];

    [BuiltCliTheory]
    [MemberData(nameof(ScheduleVerbs))]
    public async Task Every_schedule_verb_is_a_command_line_the_parser_accepts(string verb)
    {
        var (command, arguments) = DashboardActions.Plans(new ScheduleAction(
            verb,
            "nightly",
            Team: "docs-crew",
            Goal: "check the docs against the code",
            Project: "loadout-cli",
            Every: "2h",
            At: "23:00",
            On: "commit",
            Autonomy: "supervised"));

        command.Should().NotBeEmpty($"the sheet can send '{verb}', so something has to answer it");

        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync([.. command.Split(' '), .. arguments, "--dry-run"]);

        var everything = run.StandardOutput + run.StandardError;

        everything.Should().NotContain(
            "Unknown option",
            $"'{verb}' types '{command} {string.Join(' ', arguments)}', and every option in it "
            + "has to be one that command declares");

        everything.Should().NotContain("Unexpected option", $"'{verb}' types a line the parser refused");
        everything.Should().NotContain("Unknown command", $"'{verb}' names a command that does not exist");
    }

    /// <summary>
    /// Clearing out runs in a batch, filled in as the pane fills it.
    /// </summary>
    /// <remarks>
    /// Every field at once, and then each on its own, because each becomes an
    /// option and the command refuses an ask that names none of them - which
    /// it should, and which would hide an option it does not declare if every
    /// case sent all of them.
    /// </remarks>
    public static TheoryData<PruneAction> Clearings =>
    [
        new PruneAction("failed", "30d", 20, IncludeUnmerged: true),
        new PruneAction(Outcome: "needs-decision"),
        new PruneAction(OlderThan: "12h"),
        new PruneAction(Keep: 0),
    ];

    [BuiltCliTheory]
    [MemberData(nameof(Clearings))]
    public async Task Every_clear_out_the_pane_can_ask_for_is_a_line_the_parser_accepts(
        PruneAction asking)
    {
        var (command, arguments) = DashboardActions.Clears(asking);

        command.Should().NotBeEmpty();

        using var loadout = new LoadoutProcess();

        // --dry-run last, so it is this test and not the mapping that keeps
        // the command from deleting anything. The mapping sends --yes, which
        // is the page's own question already answered.
        var run = await loadout.RunAsync([.. command.Split(' '), .. arguments, "--dry-run"]);

        var everything = run.StandardOutput + run.StandardError;

        everything.Should().NotContain(
            "Unknown option",
            $"the pane types '{command} {string.Join(' ', arguments)}', and every option in it "
            + "has to be one that command declares");

        everything.Should().NotContain("Unexpected option", "the pane types a line the parser refused");
        everything.Should().NotContain("Unknown command", "the pane names a command that does not exist");

        // The one thing a clear-out must never do is be read as "everything":
        // an ask that names a condition has to get past the command's own
        // guard against saying nothing.
        everything.Should().NotContain("Say what to take");
    }

    /// <summary>
    /// The calibration: a verb nothing answers has to be reported as one.
    /// </summary>
    /// <remarks>
    /// Without this the test above passes just as happily on a mapping that
    /// returned an empty command for everything, having asserted nothing about
    /// any of them.
    /// </remarks>
    [Fact]
    public void A_verb_nothing_answers_maps_to_nothing()
    {
        DashboardActions.Maps(Asking("reticulate")).Command.Should().BeEmpty();

        DashboardActions.Plans(new ScheduleAction("reticulate", "nightly")).Command
            .Should().BeEmpty();
    }
}
