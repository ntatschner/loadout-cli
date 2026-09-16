using System.ComponentModel;
using Spectre.Console.Cli;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// Options accepted by every command (spec section 39).
/// <para>
/// Declared once on a shared base so the CLI surface stays uniform: a user who
/// learns that --json works on one command can rely on it working everywhere,
/// which is what makes the tool scriptable.
/// </para>
/// </summary>
public class GlobalSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Emit machine-readable JSON instead of formatted output.")]
    public bool Json { get; init; }

    [CommandOption("-q|--quiet")]
    [Description("Suppress informational output; errors are still reported.")]
    public bool Quiet { get; init; }

    [CommandOption("-v|--verbose")]
    [Description("Show additional detail.")]
    public bool Verbose { get; init; }

    [CommandOption("--debug")]
    [Description("Show diagnostic detail, including full exception text.")]
    public bool Debug { get; init; }

    /// <summary>
    /// Change nothing, whatever else was asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Preview-before-mutate was a rule the commands each expressed in their
    /// own vocabulary: --apply on six of them, --dry-run on one, --fix with
    /// --yes on two. All three are safe — none of them changes anything without
    /// being told to — but somebody who learns one has learned nothing about
    /// the others, and a script that wants "show me, do not touch it" had to
    /// know which of three spellings each command wanted.
    /// </para>
    /// <para>
    /// This is accepted everywhere and always means the same thing. It is
    /// additive: every existing spelling keeps working, and on a command that
    /// changes nothing anyway it is simply true already.
    /// </para>
    /// </remarks>
    [CommandOption("--dry-run")]
    [Description("Show what would happen and change nothing.")]
    public bool DryRun { get; init; }

    /// <summary>
    /// Write for somebody who asked for it to be written differently.
    /// </summary>
    /// <remarks>
    /// Declared here so the parser accepts it and the help lists it. What it
    /// changes is the console itself, which has to be right before the first
    /// thing is drawn on it, so the value is also read straight from the
    /// command line before the parser runs. The two agree on the spelling and
    /// nothing else depends on this property.
    /// </remarks>
    [CommandOption("--accessible [PRESET]")]
    [Description("Write output for a named accessibility profile: screen-reader, low-vision, colour-blind, dyslexia, adhd or plain-language.")]
    public Spectre.Console.Cli.FlagValue<string> Accessible { get; init; } = new();

    [CommandOption("--non-interactive")]
    [Description("Never prompt. Fails instead of asking a question.")]
    public bool NonInteractive { get; init; }

    [CommandOption("--offline")]
    [Description("Do not contact the network; use the cached workspace.")]
    public bool Offline { get; init; }

    [CommandOption("--no-sync")]
    [Description("Skip the workspace synchronisation for this invocation.")]
    public bool NoSync { get; init; }

    [CommandOption("--profile <PROFILE>")]
    [Description("Context profile to load, for example database or frontend.")]
    public string? Profile { get; init; }

    [CommandOption("--agent <AGENT>")]
    [Description("Agent to launch, overriding the project and global defaults.")]
    public string? Agent { get; init; }

    [CommandOption("--repo <PATH>")]
    [Description("Repository path to operate on, instead of the current directory.")]
    public string? Repo { get; init; }

    [CommandOption("--environment <ENVIRONMENT>")]
    [Description("Environment profile, for example development or production.")]
    public string? Environment { get; init; }

    /// <summary>
    /// Whether the command may prompt.
    /// <para>
    /// Redirected output means a pipe, a script or a CI job. Spec section 37
    /// requires that no menu appears unexpectedly there, so interactivity is
    /// withdrawn automatically rather than only when the user remembers the
    /// flag. JSON output implies the same.
    /// </para>
    /// </summary>
    public bool AllowsPrompting =>
        !NonInteractive
        && !Json
        && !Console.IsOutputRedirected
        && !Console.IsInputRedirected;
}
