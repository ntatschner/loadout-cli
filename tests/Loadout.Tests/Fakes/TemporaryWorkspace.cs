using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Ideas;
using Loadout.Core.Tasks;
using Loadout.Core.Workspace;
using Loadout.Models.Platform;
using Loadout.Models.Projects;
using Loadout.Platform.Linux;

namespace Loadout.Tests.Fakes;

/// <summary>
/// A workspace on disk in a temporary directory, with the real task and idea
/// services over it, and launcher state kept inside the same tree.
/// </summary>
/// <remarks>
/// The Linux layout on every host, because it is driven entirely by the
/// injected environment and so is the same on the three CI legs.
/// </remarks>
internal sealed class TemporaryWorkspace : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-ws-" + Guid.NewGuid().ToString("N"));

    public TemporaryWorkspace()
    {
        var environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

        var permissions = new NoOpFilePermissions();

        Paths = new LinuxPaths(
            environment,
            permissions,
            new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));

        Paths.EnsureDirectoriesExist();

        Yaml = new YamlStore(permissions);
        Workspace = new WorkspaceManager(Paths, new FakeGit(_root), Yaml, TimeProvider.System);
        Tasks = new TaskService(Workspace, Yaml, TimeProvider.System);
        Ideas = new IdeaService(Workspace, Tasks, Yaml, TimeProvider.System);
        Dumps = new IdeaDumps(Workspace, Tasks, Ideas, Yaml, TimeProvider.System);

        // A workspace with a projects directory is one that exists.
        Directory.CreateDirectory(Path.Combine(Workspace.LocalPath, "projects"));
    }

    public LinuxPaths Paths { get; }

    public YamlStore Yaml { get; }

    public WorkspaceManager Workspace { get; }

    public TaskService Tasks { get; }

    public IdeaService Ideas { get; }

    public IdeaDumps Dumps { get; }

    /// <summary>Somewhere outside the workspace for a test to put a repository.</summary>
    public string Scratch(string name)
    {
        var path = Path.Combine(_root, "scratch", name);
        Directory.CreateDirectory(path);

        return path;
    }

    /// <summary>Registers a project in the workspace by writing its manifest.</summary>
    public async Task ProjectAsync(string slug) =>
        (await Yaml.SaveAsync(
            Path.Combine(Workspace.LocalPath, "projects", slug, "project.yaml"),
            new ProjectManifest { Slug = slug, Name = slug })).Succeeded.Should().BeTrue();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }
}
