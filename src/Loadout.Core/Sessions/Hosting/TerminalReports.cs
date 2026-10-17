using System.Text;
using System.Text.RegularExpressions;

namespace Loadout.Core.Sessions.Hosting;

/// <summary>
/// Input that a window's terminal wrote by itself rather than a person typing.
/// </summary>
/// <remarks>
/// <para>
/// A terminal answers what the program in it asks: where the cursor is, what
/// the terminal is, whether a mode is on, and, once the program asks for them,
/// that the window gained or lost focus. xterm.js sends those answers up the
/// same socket as keys, and they go to the agent as they must. But they are
/// not somebody answering a question, and counting them as typing cleared
/// "waiting for you" the moment a window reattached to a Claude Code
/// session, which asks for focus reports.
/// </para>
/// <para>
/// The mouse is the same: once the agent asks for mouse reports, scrolling
/// back through a question, or clicking into the terminal to start typing,
/// goes to it as a report, and neither is answering it. An agent's question
/// is answered from the keyboard.
/// </para>
/// <para>
/// Only a write made of nothing but such answers is left out. Arrow keys are
/// escape sequences too, and they are how somebody picks an option, so they
/// count.
/// </para>
/// </remarks>
public static partial class TerminalReports
{
    /// <summary>Whether everything in this write is the terminal answering rather than a person typing.</summary>
    public static bool IsOnlyReports(ReadOnlySpan<byte> data) =>
        !data.IsEmpty && OnlyReports().IsMatch(Encoding.Latin1.GetString(data));

    // Anything the mouse does, once the agent asks for mouse reports: in SGR
    // form (CSI < code;x;y M or m) and in the older form (CSI M and three
    // bytes). Then focus in and out (CSI I, CSI O);
    // cursor position, device attributes and
    // status (CSI ... R, c, n); a mode report (CSI ? ... $ y); and an OSC
    // answer such as a colour, ended by BEL or ST.
    [GeneratedRegex(@"^(?:\x1b\[<\d+;\d+;\d+[Mm]|\x1b\[M[^\x1b][^\x1b][^\x1b]|\x1b\[[IO]|\x1b\[[?>]?[\d;]*[Rcn]|\x1b\[\??[\d;]*\$y|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\))+$")]
    private static partial Regex OnlyReports();
}
