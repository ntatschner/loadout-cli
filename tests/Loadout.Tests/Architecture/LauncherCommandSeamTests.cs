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
        var roots = Program.CommandNames();

        roots.Should().NotBeEmpty("the parser has to have registered something");

        // The whole path, not the first word of it. Checking only the root let
        // 'project new' be misspelt as 'project noooo' and still pass, because
        // 'project' is real — which is exactly the menu entry that fails only
        // when somebody picks it, the thing this test was written to stop.
        var paths = Program.RegisteredCommands()
            .Select(entry => entry.Path)
            .ToHashSet(StringComparer.Ordinal);

        paths.Should().NotBeEmpty("the catalogue has to have been filled");

        foreach (var path in LauncherCommands.All)
        {
            // A menu entry naming a command that does not exist fails only when
            // somebody picks it, which is the point at which it is most
            // annoying and least explicable.
            if (!path.Contains(' '))
            {
                roots.Should().Contain(path,
                    $"the launcher offers '{path}', so the parser must have it");

                continue;
            }

            paths.Should().Contain(path,
                $"the launcher offers '{path}', so the parser must have that exact command "
                + "— a real branch with an unreal sub-command is the case this misses otherwise");
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

    [Fact]
    public void Asking_for_the_command_names_is_what_fills_the_launcher_list()
    {
        // The launcher opens without the parser having been configured, so it
        // calls this to fill its command list. If asking for the names ever
        // stopped registering them, the palette would go empty again and the
        // only sign would be a user saying no commands are listed.
        //
        // This does not prove the call is still on that path. Nothing can: the
        // interactive path returns early when its output is redirected, which
        // is the only way a test reaches it. It proves the mechanism the fix
        // relies on.
        Program.CommandNames().Should().NotBeEmpty();

        Program.RegisteredCommands().Should().NotBeEmpty(
            "asking for the names configures a parser, and configuring a parser records the catalogue");
    }
}
