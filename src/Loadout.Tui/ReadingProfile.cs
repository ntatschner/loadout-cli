using Loadout.Models.Configuration;
using Spectre.Console;

namespace Loadout.Tui;

/// <summary>
/// How the person reading has asked to be asked, or null when they have asked
/// for nothing.
/// </summary>
/// <remarks>
/// <para>
/// The prompts live on both sides of the line: the setup wizard and the
/// onboarding questions are here, and four more are in the commands. Both need
/// the same answer to "is this person using a screen reader", so it is carried
/// as its own thing rather than as the command line's own type, which nothing
/// down here can see.
/// </para>
/// <para>
/// Null is the ordinary case and means the prompts behave exactly as they
/// always have.
/// </para>
/// </remarks>
/// <param name="Profile">The settings, with any preset already applied.</param>
public sealed record ReadingProfile(AccessibilitySettings? Profile)
{
    /// <summary>Nobody asked for anything.</summary>
    public static ReadingProfile None { get; } = new((AccessibilitySettings?)null);

    /// <summary>
    /// Whether a menu should be numbered rather than driven with arrow keys.
    /// </summary>
    /// <remarks>
    /// A profile that refuses redraws has already refused arrow menus, whether
    /// or not it said so: an arrow menu draws its list once and then repaints
    /// the line the cursor is on, over and over, which is the thing being
    /// refused. So this follows either setting.
    /// </remarks>
    public bool Numbered =>
        Profile is { } profile
        && (string.Equals(profile.Display.Menus, "numbered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile.Display.Redraw, "never", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether to ring the terminal bell when an answer is wanted.</summary>
    public bool Bell => Profile?.Display.Bell == true;

    /// <summary>
    /// Whether a screen may redraw itself on a timer without being asked.
    /// </summary>
    /// <remarks>
    /// A screen that repaints every couple of seconds is the thing the redraw
    /// setting exists to refuse: a screen reader is handed the whole list again
    /// on every pass and never reaches the end of it. Reduced motion asks for
    /// the same thing in different words, so either answer is enough, and a
    /// screen that cannot refresh itself offers the same key to do it once.
    /// </remarks>
    public bool MayRefreshItself =>
        Profile is not { } profile
        || (!string.Equals(profile.Display.Redraw, "never", StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.Display.Motion, "full", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Asks the person to choose one of a set of things.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An arrow-key menu where that is what somebody wants, and a numbered list
    /// where it is not. The second exists because the first cannot be heard: it
    /// draws its options once and then repaints the line the cursor is on, so a
    /// screen reader is handed the same row over and over and never the shape
    /// of the list.
    /// </para>
    /// <para>
    /// The same list either way, in the same order, with the same labels. What
    /// changes is how it is read out and how it is answered, which is the whole
    /// of the accessible-mode promise here.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">What is being chosen between.</typeparam>
    /// <param name="console">Where to ask.</param>
    /// <param name="title">The question, as a sentence.</param>
    /// <param name="choices">The options, in the order they should be offered.</param>
    /// <param name="label">What to call each one.</param>
    /// <param name="pageSize">How many an arrow-key menu shows at once.</param>
    public T Ask<T>(
        IAnsiConsole console,
        string title,
        IReadOnlyList<T> choices,
        Func<T, string> label,
        int pageSize = 10)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(label);

        if (choices.Count == 0)
        {
            throw new ArgumentException("A question with no answers cannot be asked.", nameof(choices));
        }

        if (!Numbered)
        {
            var prompt = new SelectionPrompt<T>()
                .Title(title)
                .PageSize(Math.Max(3, pageSize))
                .UseConverter(choice => Markup.Escape(label(choice)));

            prompt.AddChoices(choices);

            Ring(console);

            return console.Prompt(prompt);
        }

        console.MarkupLine(Markup.Escape(title));

        for (var i = 0; i < choices.Count; i++)
        {
            console.MarkupLine($"  {i + 1}. {Markup.Escape(label(choices[i]))}");
        }

        Ring(console);

        // Bounded by the list rather than validated afterwards, so somebody who
        // types 9 for a list of four is told immediately and asked again,
        // rather than being given the fourth thing quietly.
        var chosen = console.Prompt(
            new TextPrompt<int>($"Enter a number (1 to {choices.Count}):")
                .Validate(number => number >= 1 && number <= choices.Count
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"Enter a number between 1 and {choices.Count}.")));

        return choices[chosen - 1];
    }

    /// <summary>
    /// Asks the person to choose any number of things, including none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arrow-key form of this is worse to listen to than the single
    /// choice, not better: it draws a list, moves a cursor through it and
    /// toggles a marker in place, so what a screen reader is given is a row
    /// repainting itself and no sense of what is now selected.
    /// </para>
    /// <para>
    /// The numbered form asks once and takes the numbers typed back. Nothing
    /// is selected unless somebody says so, which is the same promise the
    /// arrow-key form makes with its "nothing is registered unless you pick
    /// it".
    /// </para>
    /// </remarks>
    public IReadOnlyList<T> AskMany<T>(
        IAnsiConsole console,
        string title,
        IReadOnlyList<T> choices,
        Func<T, string> label,
        int pageSize = 10)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(label);

        if (choices.Count == 0)
        {
            return [];
        }

        if (!Numbered)
        {
            var prompt = new MultiSelectionPrompt<T>()
                .Title(title)
                .NotRequired()
                .PageSize(Math.Max(3, pageSize))
                .MoreChoicesText("[dim](move up and down for more)[/]")
                .InstructionsText("[dim]Nothing is chosen unless you pick it.[/]")
                .UseConverter(choice => Markup.Escape(label(choice)));

            prompt.AddChoices(choices);

            Ring(console);

            return console.Prompt(prompt);
        }

        console.MarkupLine(Markup.Escape(title));

        for (var i = 0; i < choices.Count; i++)
        {
            console.MarkupLine($"  {i + 1}. {Markup.Escape(label(choices[i]))}");
        }

        Ring(console);

        var typed = console.Prompt(
            new TextPrompt<string>($"Enter the numbers you want, separated by commas, or nothing for none:")
                .AllowEmpty()
                .Validate(given => Numbers(given, choices.Count) is not null
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"Enter numbers between 1 and {choices.Count}, separated by commas.")));

        return [.. (Numbers(typed, choices.Count) ?? []).Select(number => choices[number - 1])];
    }

    /// <summary>The numbers in a typed answer, or null when one of them is not one.</summary>
    /// <remarks>
    /// Null rather than the ones that parsed, because somebody who typed
    /// "1, 3, 7" for a list of four meant something by the seven, and acting
    /// on the one and the three without saying so acts on half an intention.
    /// </remarks>
    private static IReadOnlyList<int>? Numbers(string typed, int count)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return [];
        }

        var numbers = new List<int>();

        foreach (var part in typed.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var number) || number < 1 || number > count)
            {
                return null;
            }

            if (!numbers.Contains(number))
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }

    /// <summary>
    /// Asks a yes or no question.
    /// </summary>
    /// <remarks>
    /// The ordinary confirmation already reads its answer as typed text, so
    /// what changes here is only the bell and the wording: the accepted
    /// answers are said rather than implied by a highlighted default.
    /// </remarks>
    public bool Confirm(IAnsiConsole console, string question, bool byDefault = true)
    {
        ArgumentNullException.ThrowIfNull(console);

        Ring(console);

        if (!Numbered)
        {
            return console.Confirm(question, byDefault);
        }

        var answer = console.Prompt(
            new TextPrompt<string>($"{Markup.Escape(question)} Answer y or n:")
                .DefaultValue(byDefault ? "y" : "n")
                .Validate(given => Words.Contains(given.Trim().ToLowerInvariant())
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Answer y or n.")));

        return answer.Trim().ToLowerInvariant() is "y" or "yes";
    }

    private static readonly HashSet<string> Words =
        new(StringComparer.Ordinal) { "y", "yes", "n", "no" };

    /// <summary>
    /// Rings the terminal bell where the person asked for one.
    /// </summary>
    /// <remarks>
    /// The one moment it is for: an answer is wanted and nothing more will
    /// happen until it arrives. A bell on anything else is noise, and a person
    /// who set this would switch it off again.
    /// </remarks>
    public void Ring(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        if (Bell)
        {
            console.Write("\a");
        }
    }
}
