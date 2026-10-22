using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The question <c>idea add --to</c> asks at a keyboard: Refine now?, with Yes
/// as what Enter gives.
/// </summary>
/// <remarks>
/// Asked on the console every command shares, over a real Spectre console,
/// because the test console's input never refuses and its Confirm would answer
/// whatever the command asked. Enter alone is the only key pressed, so the
/// answer comes from the default and nothing else.
/// </remarks>
public sealed class IdeaAddRefinePromptTests
{
    [Fact]
    public void Enter_alone_refines_and_the_question_shows_yes_as_the_default()
    {
        var (console, said) = Answering(string.Empty);

        IdeaAddCommand.AskToRefine(console).Should().BeTrue("Enter takes the default, which is Yes");
        said.ToString().Should().Contain("Refine now?").And.Contain("(y)");
    }

    [Fact]
    public void No_leaves_the_note_for_a_later_round()
    {
        var (console, _) = Answering("n");

        IdeaAddCommand.AskToRefine(console).Should().BeFalse();
    }

    private static (IAnsiConsole Console, StringWriter Said) Answering(string typed)
    {
        var said = new StringWriter();
        var real = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(said),
            Interactive = InteractionSupport.Yes,

            // Spectre marks a console non-interactive when it sees a CI
            // server, which would make this console never interactive on CI.
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
        });

        var keys = new Spectre.Console.Testing.TestConsoleInput();
        keys.PushTextWithEnter(typed);

        return (new AnsweringConsole(real, keys), said);
    }
}
