using System.Text;
using Loadout.Core.Instructions;
using Loadout.Models.Configuration;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// Whether Loadout's own output is being written for somebody who asked for
/// it to be written differently, and what that changes.
/// </summary>
/// <remarks>
/// <para>
/// The profile already changes what an agent writes and what it draws. This is
/// the third place it acts: what the launcher itself prints. A person using a
/// screen reader whose agent is quiet and whose launcher still spins has been
/// helped with the smaller half of their session.
/// </para>
/// <para>
/// Nothing is detected. A person turns it on, by flag, by environment
/// variable or in their configuration, and the first line of output says which
/// profile is active so they can see that it took.
/// </para>
/// </remarks>
/// <param name="IsOn">Whether anything here applies.</param>
/// <param name="Name">The profile's name, for the line that says it took.</param>
/// <param name="Profile">The settings, with the preset already applied.</param>
public sealed record AccessibleMode(bool IsOn, string Name, AccessibilitySettings Profile)
{
    /// <summary>The flag a person types to turn this on for one command.</summary>
    public const string Flag = "--accessible";

    /// <summary>The variable that turns it on for a shell.</summary>
    public const string Variable = "LOADOUT_ACCESSIBLE";

    /// <summary>Nobody asked for anything.</summary>
    public static AccessibleMode Off { get; } = new(false, AccessibilityPresets.None, new AccessibilitySettings());

    /// <summary>
    /// What a person asked for, from the nearest place they said it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flag, then environment variable, then configuration, which is the order
    /// every other launcher of this kind uses and the order people expect: the
    /// thing typed just now beats the thing set for this shell, which beats
    /// the thing set once and forgotten.
    /// </para>
    /// <para>
    /// <paramref name="settings"/> is read only when the first two say
    /// nothing, so a command that was told on the command line never waits on
    /// a file.
    /// </para>
    /// </remarks>
    /// <param name="arguments">The command line, as typed.</param>
    /// <param name="variable">What the environment says, or null.</param>
    /// <param name="settings">The configured profile, or null when it was not read.</param>
    public static AccessibleMode Resolve(
        IReadOnlyList<string> arguments,
        string? variable,
        AccessibilitySettings? settings)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (Flagged(arguments) is { } flagged)
        {
            return From(flagged);
        }

        if (variable is { Length: > 0 } asked && !IsNo(asked))
        {
            return From(asked);
        }

        return AccessibilityProfile.IsSet(settings)
            ? new AccessibleMode(true, AccessibilityProfile.Describe(settings!), AccessibilityProfile.Resolve(settings))
            : Off;
    }

    /// <summary>The flag's value, or null when it was not given.</summary>
    /// <remarks>
    /// Read from the command line as typed rather than from the parsed
    /// settings, because what this changes is the console, and the console has
    /// to be right before the first thing is drawn on it. The parser sees the
    /// same flag and accepts it; this only has to agree with it.
    /// </remarks>
    private static string? Flagged(IReadOnlyList<string> arguments)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];

            if (string.Equals(argument, Flag, StringComparison.Ordinal))
            {
                // A preset may follow, or the next thing may be the command.
                var next = i + 1 < arguments.Count ? arguments[i + 1] : null;

                return next is { Length: > 0 } && AccessibilityPresets.All.Contains(next, StringComparer.OrdinalIgnoreCase)
                    ? next
                    : AccessibilityPresets.ScreenReader;
            }

            if (argument.StartsWith(Flag + "=", StringComparison.Ordinal))
            {
                return argument[(Flag.Length + 1)..];
            }
        }

        return null;
    }

    /// <summary>
    /// A named preset, or the safest reading of a word that is not one.
    /// </summary>
    /// <remarks>
    /// A person who typed <c>--accessible</c> with nothing after it, or
    /// <c>LOADOUT_ACCESSIBLE=1</c>, has asked for the strictest of these
    /// rather than for a debate about which. The screen-reader profile is the
    /// one that changes the most and the one whose absence hurts most.
    /// </remarks>
    private static AccessibleMode From(string name)
    {
        var preset = name.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" or "" => AccessibilityPresets.ScreenReader,
            var given when AccessibilityPresets.All.Contains(given, StringComparer.Ordinal) => given,
            _ => AccessibilityPresets.ScreenReader,
        };

        var settings = new AccessibilitySettings { Preset = preset };

        return new AccessibleMode(true, preset, AccessibilityProfile.Resolve(settings));
    }

    private static bool IsNo(string value) =>
        value.Trim().ToLowerInvariant() is "0" or "false" or "no" or "off" or AccessibilityPresets.None;

    /// <summary>
    /// Makes a console obey the profile, and obey NO_COLOR whatever the
    /// profile says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applied to the console every command shares, so no command has to know
    /// this exists. Colour and glyphs are capabilities Spectre already
    /// consults everywhere it draws, which is what makes one call here reach a
    /// table nobody has thought about.
    /// </para>
    /// <para>
    /// NO_COLOR is honoured whether or not anybody set a profile, and by
    /// switching escapes off rather than colour alone: bold is an escape too,
    /// and a terminal that was promised none should get none.
    /// </para>
    /// </remarks>
    public void Apply(IAnsiConsole console, string? noColour)
    {
        ArgumentNullException.ThrowIfNull(console);

        var capabilities = console.Profile.Capabilities;

        if (noColour is { Length: > 0 })
        {
            capabilities.ColorSystem = ColorSystem.NoColors;
            capabilities.Ansi = false;
        }

        if (!IsOn)
        {
            return;
        }

        switch (Profile.Display.Colour.ToLowerInvariant())
        {
            case "none":
                capabilities.ColorSystem = ColorSystem.NoColors;
                capabilities.Ansi = false;
                break;

            case "sixteen":
                // The sixteen are the only colours a person's own terminal
                // theme can remap, so they are the only ones somebody who
                // needs particular contrast can fix for themselves.
                capabilities.ColorSystem = ColorSystem.Legacy;
                break;
        }

        if (string.Equals(Profile.Display.Glyphs, "ascii", StringComparison.OrdinalIgnoreCase))
        {
            // Spectre picks a table's borders, a tree's branches and its
            // spinners from this, so one flag reaches every drawing in the
            // application, including the ones nobody remembered.
            capabilities.Unicode = false;

            console.Pipeline.Attach(new AsciiOnly());
        }

        // What redraws is what Spectre calls interactive: a status that
        // animates in place, a progress bar that rewrites its own line. That
        // switch is deliberately not thrown here, because the same capability
        // is what a selection prompt needs to move its own cursor, and
        // throwing it would turn every menu into an exception. It comes with
        // the numbered menus that replace them.
    }

    /// <summary>What to say once, so a person can see the profile took.</summary>
    public string Line =>
        $"Accessible output is on ({Name}). Change it with: loadout config set accessibility-preset <name>";

    /// <summary>
    /// Folds the characters a stock console cannot draw, and a screen reader
    /// reads one at a time, into ones it can.
    /// </summary>
    /// <remarks>
    /// The named few are what this application writes: an em dash, a middle
    /// dot, an ellipsis. Everything else is swept by range rather than by
    /// name, because most of what reaches this comes from data - a branch
    /// name, a commit subject, a line of somebody's log - and naming those in
    /// advance is not possible.
    /// </remarks>
    public static string Ascii(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var folded = new StringBuilder(text.Length);
        var changed = false;

        foreach (var character in text)
        {
            if (character < 128)
            {
                folded.Append(character);

                continue;
            }

            changed = true;

            switch (character)
            {
                case '—' or '–':
                    folded.Append('-');
                    break;

                case '…':
                    folded.Append("...");
                    break;

                case '·' or '•':
                    folded.Append('*');
                    break;

                case '‘' or '’':
                    folded.Append('\'');
                    break;

                case '“' or '”':
                    folded.Append('"');
                    break;

                case '✓' or '✔':
                    folded.Append("ok");
                    break;

                case '✗' or '✘' or '×':
                    folded.Append('x');
                    break;

                case >= '←' and <= '⇿':
                    folded.Append("->");
                    break;

                // Box drawing, blocks and braille: a border, a spinner or a
                // bar, read aloud one character at a time.
                case >= '─' and <= '▟':
                case >= '⠀' and <= '⣿':
                    folded.Append('-');
                    break;

                default:
                    // Letters somebody's name or a commit message is written
                    // in are left alone; a symbol nothing here draws is not.
                    if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
                    {
                        folded.Append(character);
                        changed = false;
                    }
                    else
                    {
                        folded.Append('?');
                    }

                    break;
            }
        }

        return changed || folded.Length != text.Length ? folded.ToString() : text;
    }

    /// <summary>Folds every drawing on its way to the console.</summary>
    private sealed class AsciiOnly : IRenderHook
    {
        public IEnumerable<IRenderable> Process(RenderOptions options, IEnumerable<IRenderable> renderables) =>
            renderables.Select(renderable => new Folded(renderable));

        private sealed class Folded : IRenderable
        {
            private readonly IRenderable _inner;

            public Folded(IRenderable inner) => _inner = inner;

            public Measurement Measure(RenderOptions options, int maxWidth) => _inner.Measure(options, maxWidth);

            public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
                _inner.Render(options, maxWidth)
                    .Select(segment => segment.IsControlCode
                        ? segment
                        : new Segment(Ascii(segment.Text), segment.Style));
        }
    }
}
