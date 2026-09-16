using System.Runtime.Versioning;
using FluentAssertions;
using Loadout.Platform.Unix;
using Loadout.Platform.Windows;
using Xunit;

namespace Loadout.Tests.Platform;

/// <summary>
/// What gets written so the daemon starts at login.
/// </summary>
/// <remarks>
/// <para>
/// The whole difficulty is one thing: the launcher quotes its own path because
/// it may contain spaces, and every one of these formats wants that path in a
/// different shape. A shortcut wants the program and its arguments apart, a
/// launch agent wants one element per argument, and a desktop entry wants the
/// line as it stands. Splitting on every space turns one path into three
/// arguments that do not exist.
/// </para>
/// <para>
/// <strong>Only the Windows entry has been logged into.</strong> These cover
/// what the Unix pair writes, which is a different claim from it working.
/// </para>
/// </remarks>
public sealed class AutostartTests
{
    private const string Spaced = @"""C:\Program Files\Loadout\loadout.exe"" team daemon";

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void A_quoted_program_with_a_space_in_it_stays_one_program()
    {
        var (target, arguments) = WindowsAutostart.Split(Spaced);

        target.Should().Be(@"C:\Program Files\Loadout\loadout.exe");
        arguments.Should().Be("team daemon");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void An_unquoted_program_splits_at_its_first_space()
    {
        // No quotes means no spaces in the program, by definition: the
        // launcher quotes whenever it has to.
        var (target, arguments) = WindowsAutostart.Split("/usr/bin/loadout team daemon");

        target.Should().Be("/usr/bin/loadout");
        arguments.Should().Be("team daemon");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void A_program_with_no_arguments_has_none_rather_than_an_empty_one()
    {
        WindowsAutostart.Split(@"""C:\loadout.exe""").Should().Be((@"C:\loadout.exe", string.Empty));
        WindowsAutostart.Split("loadout").Should().Be(("loadout", string.Empty));
    }

    [Fact]
    public void A_launch_agent_keeps_a_spaced_path_as_one_argument()
    {
        var plist = UnixAutostart.Plist(Spaced);

        plist.Should().Contain(@"<string>C:\Program Files\Loadout\loadout.exe</string>");
        plist.Should().Contain("<string>team</string>");
        plist.Should().Contain("<string>daemon</string>");
    }

    [Fact]
    public void A_launch_agent_runs_at_load_and_is_not_kept_alive()
    {
        var plist = UnixAutostart.Plist(Spaced);

        plist.Should().Contain("<key>RunAtLoad</key>");

        // A daemon that respawns after somebody stopped it is a daemon they
        // cannot stop.
        plist.Should().NotContain("KeepAlive");
    }

    [Fact]
    public void A_path_with_xml_in_it_cannot_break_the_plist()
    {
        // Unlikely and cheap to be right about. A path is not trusted input
        // just because it came from this process.
        UnixAutostart.Plist(@"""/home/a&b/<loadout>"" team daemon")
            .Should().Contain("<string>/home/a&amp;b/&lt;loadout&gt;</string>");
    }

    [Fact]
    public void A_desktop_entry_carries_the_command_line_as_it_stands()
    {
        var entry = UnixAutostart.Desktop(Spaced);

        entry.Should().Contain("Type=Application");
        entry.Should().Contain($"Exec={Spaced}");
        entry.Should().Contain("Terminal=false");
    }

    [Theory]
    [InlineData(@"""a b"" c", new[] { "a b", "c" })]
    [InlineData("a b c", new[] { "a", "b", "c" })]
    [InlineData("  a   b  ", new[] { "a", "b" })]
    [InlineData(@"""a b""", new[] { "a b" })]
    public void A_command_line_splits_into_the_words_a_shell_would_see(string command, string[] expected)
    {
        UnixAutostart.Words(command).Should().Equal(expected);
    }
}
