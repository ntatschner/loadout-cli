using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What answering a gate from the page types, field by field.
/// </summary>
/// <remarks>
/// A field the page sends that never reaches the line arrives as nothing, and
/// the page reports success. The contract test proves the parser accepts the
/// line; this proves each field is in it, which a valid line missing one would
/// pass.
/// </remarks>
public sealed class GateVerbTests
{
    private const string Run = "20261016-0840-3f1a";

    private static string Typed(RunAction action)
    {
        var (command, arguments) = DashboardActions.Maps(action);

        return string.Join(' ', new[] { command }.Concat(arguments));
    }

    [Fact]
    public void An_answer_types_the_answer_the_gate_and_who_by()
    {
        Typed(new RunAction(Run, "gate", Gate: "gate-5e1d9a20", Answer: "Approve"))
            .Should().Be($"team gate {Run} --gate gate-5e1d9a20 --answer Approve --by dashboard");
    }

    [Fact]
    public void A_brief_sent_instead_and_a_reason_are_both_typed()
    {
        Typed(new RunAction(Run, "gate", Gate: "gate-1", Answer: "yes", Reason: "- why", Instead: "- do this"))
            .Should().Be($"team gate {Run} --gate gate-1 --answer yes --by dashboard --instead=- do this --reason=- why");
    }

    [Fact]
    public void Think_again_types_the_command_the_gate_shows()
    {
        Typed(new RunAction(Run, "gate", Gate: "gate-5e1d9a20", ThinkAgain: true))
            .Should().Be($"team gate {Run} --gate gate-5e1d9a20 --think-again --by dashboard");
    }

    /// <remarks>
    /// Passed rather than dropped, so the command refuses the pair in its own
    /// words instead of this choosing which half somebody meant.
    /// </remarks>
    [Fact]
    public void Think_again_with_an_answer_passes_both_for_the_command_to_refuse()
    {
        Typed(new RunAction(Run, "gate", Gate: "gate-1", Answer: "Ratchet", ThinkAgain: true))
            .Should().Be($"team gate {Run} --gate gate-1 --think-again --answer Ratchet --by dashboard");
    }
}
