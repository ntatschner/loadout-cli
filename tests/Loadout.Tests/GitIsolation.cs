using System.Runtime.CompilerServices;

namespace Loadout.Tests;

/// <summary>
/// Keeps the test run out of the developer's own Git configuration.
/// <para>
/// The launcher shells out to the real <c>git</c>, and <c>git</c> does not care
/// what a fake environment provider says: it reads the configuration belonging
/// to whoever is running the process. Any test exercising global settings was
/// therefore writing to the machine's real one, which is how a test run came to
/// point <c>core.excludesFile</c> at a temporary directory and quietly turn off
/// the very protection the feature exists to provide.
/// </para>
/// <para>
/// Redirecting the global and system files makes that impossible rather than
/// unlikely. Every fixture sets its committer identity per repository already,
/// so nothing depends on the real configuration being present.
/// </para>
/// </summary>
internal static class GitIsolation
{
    [ModuleInitializer]
    internal static void Isolate()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "loadout-test-git-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        // Honoured by Git 2.32 and later. Anything older would fall through to
        // the real files, so the minimum is worth knowing rather than assuming.
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", Path.Combine(root, "config"));
        Environment.SetEnvironmentVariable("GIT_CONFIG_SYSTEM", Path.Combine(root, "system"));

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not worth failing a run over.
            }
        };
    }
}
