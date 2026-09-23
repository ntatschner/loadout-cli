using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// Nothing in the launcher decides for itself what its borders are made of.
/// </summary>
/// <remarks>
/// <para>
/// A profile asking for no box drawing has to be answered everywhere or it is
/// not answered at all: a console font without the box-drawing block draws
/// nothing legible from any of it, and one window still framed is one window
/// full of replacement characters.
/// </para>
/// <para>
/// This exists because the first pass at it changed four dialogs and left
/// thirteen frames naming a style literally, which nothing noticed - there is
/// no screen reader on this machine and the screens were never drawn under the
/// profile. A rule a person has to remember for each new view is a rule that
/// lasts until the next view.
/// </para>
/// </remarks>
public sealed class LineStyleTests
{
    [Fact]
    public void No_view_names_the_lines_it_is_drawn_with()
    {
        var offenders = Directory
            .GetFiles(Path.Combine(Root(), "src", "Loadout.Tui"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !string.Equals(
                Path.GetFileName(file), "LauncherTheme.cs", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file) is var text
                && (text.Contains("LineStyle.Rounded", StringComparison.Ordinal)
                    || text.Contains("LineStyle.Single", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();

        // None is left alone: it is already the plainest thing there is, and a
        // view that wants no border under any profile is asking for something
        // the theme has no opinion about.
        offenders.Should().BeEmpty(
            "a view that names its own line style keeps drawing boxes for somebody "
            + "whose profile asked for none; ask LauncherTheme.Lines or .Inner instead");
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        // A directory in an ordinary checkout, a file in a git worktree.
        while (directory is not null && !Path.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "The repository root could not be found from the test output directory.");
    }
}
