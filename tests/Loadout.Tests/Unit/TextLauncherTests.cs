using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Configuration;
using Loadout.Tui;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The launcher for somebody who cannot see a full-screen one.
/// </summary>
/// <remarks>
/// <para>
/// The full-screen launcher draws a list, moves a highlight through it and
/// repaints the rows it leaves. None of that is announced to anything: no
/// terminal toolkit has a screen-reader provider on Windows or macOS. It is
/// keyboard-operable and unreadable, and offering it to somebody using a
/// screen reader would be a promise the launcher cannot keep.
/// </para>
/// <para>
/// What matters here is that this is the same program, not a smaller one:
/// the same catalogue, and running a command hands it to the same parser the
/// command line uses.
/// </para>
/// </remarks>
public sealed class TextLauncherTests
{
    private static ReadingProfile For(string preset) =>
        new(AccessibilityProfile.Resolve(new AccessibilitySettings { Preset = preset }));

    private static TestConsole Console(params string[] typed)
    {
        var console = new TestConsole();

        console.Profile.Capabilities.Interactive = true;
        console.Profile.Width = 200;

        foreach (var line in typed)
        {
            console.Input.PushTextWithEnter(line);
        }

        return console;
    }

    [Fact]
    public void It_is_offered_only_to_somebody_who_asked_for_it()
    {
        var catalogue = new FakeCatalogue();

        new TextLauncher(catalogue, Console(), ReadingProfile.None).IsWanted.Should().BeFalse();

        new TextLauncher(catalogue, Console(), For(AccessibilityPresets.Adhd)).IsWanted
            .Should().BeFalse("nothing in that profile says the full-screen one cannot be seen");

        new TextLauncher(catalogue, Console(), For(AccessibilityPresets.ScreenReader)).IsWanted
            .Should().BeTrue();
    }

    [Fact]
    public async Task Every_command_is_offered_with_what_it_does()
    {
        var catalogue = new FakeCatalogue();
        var console = Console("3");

        await new TextLauncher(catalogue, console, For(AccessibilityPresets.ScreenReader)).RunAsync();

        console.Output.Should().Contain("doctor - Check this machine");
        console.Output.Should().Contain("project list - List the projects");
        console.Output.Should().Contain("Enter a number");
    }

    [Fact]
    public async Task Choosing_one_runs_it_through_the_same_parser()
    {
        // Not a second implementation of anything. The catalogue is the one
        // the full-screen launcher uses and it hands the command back to the
        // command line's own parser.
        var catalogue = new FakeCatalogue();
        var console = Console("1", "3");

        var code = await new TextLauncher(catalogue, console, For(AccessibilityPresets.ScreenReader)).RunAsync();

        catalogue.Ran.Should().Equal("doctor");
        code.Should().Be(0);
    }

    [Fact]
    public async Task A_command_that_needs_an_argument_is_explained_rather_than_guessed_at()
    {
        // What a command wants is the command's own business. A second parser
        // here would drift from the first the day somebody adds an option.
        var catalogue = new FakeCatalogue();
        var console = Console("2", "3");

        await new TextLauncher(catalogue, console, For(AccessibilityPresets.ScreenReader)).RunAsync();

        catalogue.Ran.Should().BeEmpty();
        console.Output.Should().Contain("needs a project");
        console.Output.Should().Contain("loadout project list <slug>");
    }

    [Fact]
    public async Task A_command_that_needs_a_screen_is_not_offered_at_all()
    {
        var catalogue = new FakeCatalogue();
        var console = Console("3");

        await new TextLauncher(catalogue, console, For(AccessibilityPresets.ScreenReader)).RunAsync();

        console.Output.Should().NotContain("launcher", "it needs a screen this person cannot read");
    }

    private sealed class FakeCatalogue : ICommandCatalogue
    {
        public List<string> Ran { get; } = [];

        public IReadOnlyList<CatalogueEntry> Commands =>
        [
            new("doctor", "Check this machine", null, Category: "Fix"),
            new(
                "project list",
                "List the projects",
                null,
                Category: "Projects",
                Example: "project list <slug>",
                RequiredArgument: "a project"),
            new("launcher", "The full-screen launcher", "it needs a screen", Category: "Start"),
        ];

        public Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            Ran.Add(path);

            return Task.FromResult(0);
        }
    }
}
