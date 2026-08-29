using Loadout.Models.Projects;

namespace Loadout.Core.Instructions;

/// <summary>
/// The instruction files a project loads on every launch, whatever the task.
/// </summary>
/// <remarks>
/// <para>
/// Held in one place because two callers were deriving it separately and only
/// one of them was right. The rules commands walked the manifest to find the
/// always-loaded files; the doctor check counted rules alone, so a project
/// whose instructions had grown to sixty kilobytes but declared no rules was
/// told its instruction layer was a comfortable size. The number that matters
/// is what a session pays before it has done anything, and that is this list.
/// </para>
/// <para>
/// Order matches what the compiler reads: global context, then project
/// context, then the default agent's own instructions. Existence is not
/// checked here — a file the manifest names and the disk does not have is a
/// finding rather than something to quietly drop, and the callers differ on
/// what to do about it.
/// </para>
/// </remarks>
public static class CoreInstructions
{
    /// <summary>
    /// Paths the compiler would read for this project, in order.
    /// </summary>
    /// <param name="manifest">The project definition supplying the file lists.</param>
    /// <param name="workspaceRoot">Local path to the workspace clone.</param>
    /// <param name="slug">Project being described.</param>
    public static IReadOnlyList<string> PathsFor(
        ProjectManifest manifest,
        string workspaceRoot,
        string slug)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var projectRoot = Path.Combine(workspaceRoot, "projects", slug);

        var paths = manifest.Context.Global
            .Select(relative => Path.Combine(workspaceRoot, ToNative(relative)))
            .Concat(manifest.Context.Project
                .Select(relative => Path.Combine(projectRoot, ToNative(relative))))
            .ToList();

        var agent = manifest.Agents.Default;

        if (!string.IsNullOrWhiteSpace(agent))
        {
            var agentInstructions = Path.Combine(projectRoot, "agents", agent, "instructions.md");

            // Only when it exists. Most projects have none, and reporting its
            // absence as a defect would be noise on every one of them.
            if (File.Exists(agentInstructions))
            {
                paths.Add(agentInstructions);
            }
        }

        return paths;
    }

    /// <summary>
    /// The largest of those files that is actually on disk, with its size, or
    /// null when the project has none. This is the one worth naming: telling
    /// somebody their instruction layer is large without saying which file it
    /// is leaves them to go and measure it themselves.
    /// </summary>
    public static (string Path, long Bytes)? Largest(
        ProjectManifest manifest,
        string workspaceRoot,
        string slug) =>
        PathsFor(manifest, workspaceRoot, slug)
            .Where(File.Exists)
            .Select(path => (Path: path, Bytes: new FileInfo(path).Length))
            .OrderByDescending(file => file.Bytes)
            .Select(file => ((string, long)?)file)
            .FirstOrDefault();

    private static string ToNative(string relative) =>
        relative.Replace('/', Path.DirectorySeparatorChar);
}
