using Spectre.Console;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// <c>--remote</c> and <c>--no-remote</c>, read as one choice for a launch.
/// </summary>
/// <remarks>
/// Two flags rather than one with a value, because the machine default sits
/// underneath: neither means "whatever the <c>remote-control</c> setting says",
/// which a single boolean cannot express.
/// </remarks>
public static class RemoteControlOption
{
    public const string OnDescription =
        "Start this session with the agent's own remote control on, whatever the remote-control setting says. "
        + "Refused if the agent cannot do it.";

    public const string OffDescription =
        "Start this session without remote control, whatever the remote-control setting says.";

    /// <summary>The launch's own choice, or null to take the machine's default.</summary>
    public static bool? From(bool remote, bool noRemote) =>
        remote ? true : noRemote ? false : null;

    /// <summary>Refuses both flags at once, which asks for two opposite things.</summary>
    public static ValidationResult Validate(bool remote, bool noRemote, Func<ValidationResult> otherwise) =>
        remote && noRemote
            ? ValidationResult.Error("Give --remote or --no-remote, not both.")
            : otherwise();
}
