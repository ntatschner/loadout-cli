using System.Runtime.Versioning;
using FluentAssertions;
using Loadout.Platform.Windows;
using Xunit;

namespace Loadout.Tests.Platform;

/// <summary>
/// The command line a daemon in the background is started with on Windows.
/// </summary>
/// <remarks>
/// Built by hand because the process is started by hand, and Windows hands a
/// program one string that the program splits again. A path with a space in
/// it, or ending in a backslash, is where that goes wrong: the second quietly
/// swallows the argument after it.
/// </remarks>
public sealed class BackgroundStartTests
{
    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void Plain_arguments_are_left_as_they_are()
    {
        WindowsBackgroundStart.CommandLine(@"C:\loadout.exe", ["team", "daemon", "--port", "8080"])
            .Should().Be(@"C:\loadout.exe team daemon --port 8080");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void A_path_with_a_space_in_it_stays_one_argument()
    {
        WindowsBackgroundStart.CommandLine(
                @"C:\Program Files\Loadout\loadout.exe",
                ["--log", @"C:\Users\A Person\AppData\Local\Loadout\teams\daemon.log"])
            .Should().Be(
                @"""C:\Program Files\Loadout\loadout.exe"" --log ""C:\Users\A Person\AppData\Local\Loadout\teams\daemon.log""");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void A_trailing_backslash_does_not_escape_the_closing_quote()
    {
        WindowsBackgroundStart.CommandLine("x", [@"C:\A Folder\", "next"])
            .Should().Be(@"x ""C:\A Folder\\"" next");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void Quotes_and_empty_arguments_survive()
    {
        WindowsBackgroundStart.CommandLine("x", [@"say ""hi""", string.Empty])
            .Should().Be(@"x ""say \""hi\"""" """"");
    }
}
