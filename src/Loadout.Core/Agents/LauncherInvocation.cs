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
    public static string? Current()
    {
        if (Environment.ProcessPath is not { Length: > 0 } process)
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(process);

        if (!name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return "\"" + process + "\"";
        }

        var assembly = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;

        if (assembly is not { Length: > 0 })
        {
            return null;
        }

        var entry = Path.Combine(AppContext.BaseDirectory, assembly + ".dll");

        return File.Exists(entry) ? "\"" + process + "\" \"" + entry + "\"" : null;
    }
}
