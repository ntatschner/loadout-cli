using FluentAssertions;
using Loadout.Cli;
using Loadout.Cli.Infrastructure;
using Loadout.Tui.Terminal;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// Holds the launcher and the command line to the same set of commands.
/// </summary>
/// <remarks>
/// <para>
/// The launcher is supposed to be a way into the command line rather than a
/// second implementation of it, and the only thing keeping that true is that
/// the strings on both sides agree. Nothing checked that they did.
/// </para>
/// <para>
/// The cost of not checking was a command palette in which every entry was
/// inert for three releases: it listed and described commands perfectly and ran
/// none of them, and every launcher test injected an empty catalogue, so the
/// path from a chosen row to the parser was exercised by nothing at all. These
/// tests do not prove a command runs — that needs a screen, and those tests
/// live beside the screens — but they do prove that what the launcher offers is
/// something the parser has heard of.
/// </para>
/// </remarks>
public sealed class LauncherCommandSeamTests
{
    /// <summary>
    /// The first word of a command path, which is what the parser dispatches
    /// on: "project clone" is the "project" command with an argument.
    /// </summary>
    private static string RootOf(string path) => path.Split(' ')[0];

    [Fact]
    public void Every_command_the_launcher_hard_codes_is_one_the_parser_knows()
    {
        var known = Program.CommandNames();

        known.Should().NotBeEmpty("the parser has to have registered something");

        foreach (var path in LauncherCommands.All)
        {
            // A menu entry naming a command that does not exist fails only when
            // somebody picks it, which is the point at which it is most
            // annoying and least explicable.
            known.Should().Contain(RootOf(path),
                $"the launcher offers '{path}', so the parser must have a '{RootOf(path)}' command");
        }
    }

    [Fact]
    public void Every_command_the_palette_offers_to_run_is_one_the_parser_knows()
    {
        var known = Program.CommandNames();
        var offered = Catalogue.Commands;

        offered.Should().NotBeEmpty("the palette exists to list the command line");

        foreach (var entry in offered.Where(e => e.TerminalOnly is null))
        {
            known.Should().Contain(RootOf(entry.Path),
                $"the palette offers to run '{entry.Path}' from the launcher");
        }
    }

    [Fact]
    public void The_palette_offers_more_than_a_handful()
    {
        // An empty or nearly empty catalogue would satisfy the tests above
        // while meaning the launcher had quietly stopped being a way into
        // anything. The exact number is not the point and will change; the
        // order of magnitude is.
        Catalogue.Commands.Should().HaveCountGreaterThan(20);
    }
}
