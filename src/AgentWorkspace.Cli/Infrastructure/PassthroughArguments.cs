namespace AgentWorkspace.Cli.Infrastructure;

/// <summary>
/// Holds the arguments that followed a bare <c>--</c> on the command line
/// (spec section 36).
/// <para>
/// These are split off before the command-line parser ever sees them and
/// carried here untouched. Letting the parser near them would risk it
/// interpreting a flag meant for the agent — <c>--json</c> or <c>--verbose</c>
/// are plausible on both sides — and the spec is explicit that the launcher
/// must not parse or alter anything after the separator.
/// </para>
/// </summary>
public sealed class PassthroughArguments
{
    public PassthroughArguments(IReadOnlyList<string> arguments) => Arguments = arguments;

    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// Splits argv at the first bare <c>--</c>. The separator itself is
    /// discarded; everything before it is the launcher's, everything after is
    /// the agent's.
    /// </summary>
    public static (string[] Launcher, string[] Passthrough) Split(string[] args)
    {
        var separator = Array.IndexOf(args, "--");

        return separator < 0
            ? (args, [])
            : (args[..separator], args[(separator + 1)..]);
    }
}
