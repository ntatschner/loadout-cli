namespace Loadout.Core.Agents;

/// <summary>
/// How to start this launcher again from a shell on this machine.
/// </summary>
/// <remarks>
/// <para>
/// The shipped launcher is one executable and its process path is the
/// answer. A development build is run as <c>dotnet loadout.dll</c>, whose
/// process path is <c>dotnet.exe</c>, and a command naming that alone starts
/// the host with nothing to run — which is what the first install of the
/// after-edit hook wrote. The host case is told apart by name and the
/// assembly is found in the application directory, since inside a
/// single-file build the assembly reports no location of its own and that
/// build never reaches the host case anyway.
/// </para>
/// <para>
/// Always quoted. The string goes to a shell, and on Windows that shell is
/// Git Bash, which reads an unquoted backslash as an escape.
/// </para>
/// </remarks>
public static class LauncherInvocation
{
    /// <summary>The quoted command prefix for this launcher, or null when it cannot be worked out.</summary>
    public static string? Current() =>
        Parts() is not ({ Length: > 0 } command, var prefix)
            ? null
            : string.Join(' ', new[] { command }.Concat(prefix).Select(one => "\"" + one + "\""));

    /// <summary>
    /// The same answer split into an executable and the arguments that have to
    /// come before any of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A shell wants one quoted string and <see cref="Current"/> gives it one.
    /// Anything declaring a process to something else - an MCP server entry, a
    /// scheduled task, a service - wants the executable and its arguments
    /// apart, because that is how those formats are written. Building the
    /// second from the first means unquoting a string somebody just quoted.
    /// </para>
    /// <para>
    /// Both come from here so they cannot disagree. They already had:
    /// <c>SelfServerConfig</c> declared the launcher's own MCP server using
    /// <c>Environment.ProcessPath</c> alone, which under the development host
    /// is <c>dotnet.exe</c> - a real file, so the guard for "cannot find
    /// myself" passed - and wrote a server whose command was the host and whose
    /// first argument was <c>mcp</c>. The agent started, the server did not,
    /// and every team run from a development build lost the tool its lead
    /// answers permission questions with. The lead died in seconds saying
    /// <c>mcp__loadout__loadout_permission not found</c>, which reads as a
    /// broken permission harness rather than as a path.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The executable and the arguments to put in front, or null where this
    /// launcher cannot say how it was started.
    /// </returns>
    public static (string Command, IReadOnlyList<string> Prefix)? Parts() =>
        From(
            Environment.ProcessPath,
            System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name,
            AppContext.BaseDirectory,
            File.Exists);

    /// <summary>
    /// The decision on its own, told what is running rather than asking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separated because the branch that matters cannot otherwise be reached by
    /// a test. The host case happens when the process is <c>dotnet</c>, and a
    /// test does not choose what runs it: under <c>dotnet test</c> the process
    /// is <c>testhost.exe</c>, so a test calling <see cref="Parts"/> takes the
    /// shipped-launcher branch and passes without touching the one that was
    /// wrong.
    /// </para>
    /// <para>
    /// That is not a hypothetical. The first test written for this fix did
    /// exactly that and passed, and a probe printing what it had actually
    /// resolved was what said so.
    /// </para>
    /// </remarks>
    /// <param name="processPath">What is running, as the operating system reports it.</param>
    /// <param name="assemblyName">The entry assembly's name, without an extension.</param>
    /// <param name="baseDirectory">Where the application's files are.</param>
    /// <param name="exists">Whether a path is there, so a test need not write files.</param>
    internal static (string Command, IReadOnlyList<string> Prefix)? From(
        string? processPath,
        string? assemblyName,
        string baseDirectory,
        Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);

        if (processPath is not { Length: > 0 } process)
        {
            return null;
        }

        if (!Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return (process, []);
        }

        if (assemblyName is not { Length: > 0 })
        {
            return null;
        }

        var entry = Path.Combine(baseDirectory, assemblyName + ".dll");

        return exists(entry) ? (process, new[] { entry }) : null;
    }
}
