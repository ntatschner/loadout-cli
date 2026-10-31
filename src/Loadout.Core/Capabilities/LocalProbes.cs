using Loadout.Models.Capabilities;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Loadout.Core.Capabilities;

/// <summary>One workflow file, as far as the profile cares.</summary>
/// <param name="File">Its name, such as <c>ci.yml</c>.</param>
/// <param name="Ci">It runs on a pull request, or on a push that is not only of tags.</param>
/// <param name="Release">It runs on a release, on a pushed tag, or by hand under a name that says release or publish.</param>
/// <param name="Calls">The shared workflow each of its jobs calls, when every job calls one; empty when any job has steps of its own.</param>
/// <param name="Text">Its text, for guessing which stacks it builds.</param>
public sealed record WorkflowFile(string File, bool Ci, bool Release, IReadOnlyList<string> Calls, string Text)
{
    /// <summary>Every job calls a shared workflow, so the work happens elsewhere.</summary>
    public bool Delegated => Calls.Count > 0;
}

/// <summary>What the repository's own files say: its workflows and the kinds of project in it.</summary>
/// <remarks>
/// Reading only, and only files: nothing here runs a command, so a profile can
/// be built at the start of any run without side effects or a network.
/// </remarks>
public static class LocalProbes
{
    /// <summary>The stacks, in the order that breaks a tie between two found at the same depth.</summary>
    public static readonly IReadOnlyList<string> StackOrder = ["dotnet", "node", "python", "powershell", "go", "rust"];

    /// <summary>How deep to look for manifests. A project's manifests sit near its root.</summary>
    private const int Depth = 4;

    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", "dist", "build", "out", "vendor", ".venv", "venv", "packages", "target",
    };

    /// <summary>The words in a workflow that say it builds a stack. A guess, so only ever a note.</summary>
    private static readonly Dictionary<string, string[]> BuildWords = new(StringComparer.Ordinal)
    {
        ["dotnet"] = ["dotnet", "setup-dotnet", "msbuild"],
        ["node"] = ["npm", "pnpm", "yarn", "setup-node", "npx"],
        ["python"] = ["pip", "setup-python", "pytest", "poetry", "uv "],
        ["powershell"] = ["pwsh", "invoke-pester", "shell: powershell"],
        ["go"] = ["setup-go", "go build", "go test"],
        ["rust"] = ["cargo"],
    };

    /// <summary>Every workflow under <c>.github/workflows</c>, and the files that could not be read as one.</summary>
    public static (IReadOnlyList<WorkflowFile> Workflows, IReadOnlyList<string> Unreadable) Workflows(string root)
    {
        var folder = Path.Combine(root, ".github", "workflows");

        if (!Directory.Exists(folder))
        {
            return ([], []);
        }

        var workflows = new List<WorkflowFile>();
        var unreadable = new List<string>();

        foreach (var path in Directory.EnumerateFiles(folder)
                     .Where(path => Path.GetExtension(path) is ".yml" or ".yaml")
                     .Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(path);

            if (Read(Path.GetFileName(path), text) is { } read)
            {
                workflows.Add(read);
            }
            else
            {
                unreadable.Add(Path.GetFileName(path));
            }
        }

        return (workflows, unreadable);
    }

    /// <summary>One workflow from its text, or null when it is not YAML a workflow could be.</summary>
    public static WorkflowFile? Read(string file, string text)
    {
        YamlMappingNode root;

        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));

            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
            {
                return null;
            }

            root = mapping;
        }
        catch (YamlException)
        {
            return null;
        }

        var events = Events(Child(root, "on"));
        var name = (Child(root, "name") as YamlScalarNode)?.Value ?? string.Empty;
        var said = $"{file} {name}";
        var byHand = events.ContainsKey("workflow_dispatch")
            && (said.Contains("release", StringComparison.OrdinalIgnoreCase) || said.Contains("publish", StringComparison.OrdinalIgnoreCase));

        // A push filtered to tags alone is a release trigger, not CI.
        var push = events.TryGetValue("push", out var pushed) ? pushed : null;
        var tagsOnly = push is YamlMappingNode filters && Child(filters, "tags") is not null
            && Child(filters, "branches") is null && Child(filters, "branches-ignore") is null;

        var ci = events.ContainsKey("pull_request") || events.ContainsKey("pull_request_target") || (events.ContainsKey("push") && !tagsOnly);
        var release = events.ContainsKey("release") || (push is YamlMappingNode withTags && Child(withTags, "tags") is not null) || byHand;

        var calls = new List<string>();
        var jobs = Child(root, "jobs") as YamlMappingNode;

        foreach (var job in jobs?.Children.Values ?? [])
        {
            if (job is YamlMappingNode body && Child(body, "uses") is YamlScalarNode { Value: { Length: > 0 } uses })
            {
                calls.Add(uses);
            }
            else
            {
                calls.Clear();

                break;
            }
        }

        return new WorkflowFile(file, ci, release, calls, text);
    }

    /// <summary>The kinds of project in the repository, the one nearest its root first.</summary>
    public static IReadOnlyList<CapabilityStack> Stacks(string root)
    {
        var nearest = new Dictionary<string, (int Depth, string Manifest)>(StringComparer.Ordinal);

        void Look(string folder, int depth)
        {
            IEnumerable<string> files;
            IEnumerable<string> folders;

            try
            {
                files = Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal).ToList();
                folders = Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return;
            }

            foreach (var file in files)
            {
                if (StackOf(Path.GetFileName(file)) is { } stack
                    && (!nearest.TryGetValue(stack, out var known) || depth < known.Depth))
                {
                    nearest[stack] = (depth, Path.GetRelativePath(root, file).Replace('\\', '/'));
                }
            }

            if (depth >= Depth)
            {
                return;
            }

            foreach (var child in folders.Where(child => !Skipped.Contains(Path.GetFileName(child))))
            {
                Look(child, depth + 1);
            }
        }

        Look(root, 0);

        return
        [
            .. nearest
                .OrderBy(one => one.Value.Depth)
                .ThenBy(one => StackOrder.ToList().IndexOf(one.Key))
                .Select(one => new CapabilityStack { Name = one.Key, Manifest = one.Value.Manifest }),
        ];
    }

    /// <summary>The stacks no workflow that does its own work appears to build, for a repository with more than one.</summary>
    public static IReadOnlyList<string> Unbuilt(IReadOnlyList<CapabilityStack> stacks, IReadOnlyList<WorkflowFile> workflows)
    {
        var building = workflows.Where(workflow => workflow.Ci && !workflow.Delegated).ToList();

        if (stacks.Count < 2 || building.Count == 0)
        {
            return [];
        }

        return
        [
            .. stacks
                .Select(stack => stack.Name)
                .Where(stack => !building.Any(workflow =>
                    BuildWords[stack].Any(word => workflow.Text.Contains(word, StringComparison.OrdinalIgnoreCase)))),
        ];
    }

    /// <summary>The stack a file is the manifest of, or null.</summary>
    internal static string? StackOf(string file)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();

        return file.ToLowerInvariant() switch
        {
            "global.json" => "dotnet",
            "package.json" => "node",
            "pyproject.toml" or "requirements.txt" or "setup.py" or "pipfile" => "python",
            "go.mod" => "go",
            "cargo.toml" => "rust",
            _ => extension switch
            {
                ".sln" or ".slnx" or ".csproj" or ".fsproj" or ".vbproj" => "dotnet",
                ".psd1" => "powershell",
                _ => null,
            },
        };
    }

    private static YamlNode? Child(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    /// <summary>The events a workflow runs on, each with what filters it, from any of the three ways <c>on</c> is written.</summary>
    private static Dictionary<string, YamlNode?> Events(YamlNode? on) => on switch
    {
        YamlScalarNode { Value: { } one } => new(StringComparer.Ordinal) { [one] = null },
        YamlSequenceNode many => many.Children.OfType<YamlScalarNode>()
            .Where(one => one.Value is not null)
            .DistinctBy(one => one.Value)
            .ToDictionary(one => one.Value!, _ => (YamlNode?)null, StringComparer.Ordinal),
        YamlMappingNode named => named.Children
            .Where(one => one.Key is YamlScalarNode { Value: not null })
            .ToDictionary(one => ((YamlScalarNode)one.Key).Value!, one => (YamlNode?)one.Value, StringComparer.Ordinal),
        _ => new(StringComparer.Ordinal),
    };
}
