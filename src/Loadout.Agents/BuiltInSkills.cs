using System.Reflection;

namespace Loadout.Agents;

/// <summary>
/// The skills the launcher ships, and writing them out where an agent can be
/// handed them.
/// </summary>
/// <remarks>
/// <para>
/// Embedded rather than laid on disk at install time, for the same reasons the
/// specialist library is: adding one needs content and not a source change,
/// they cannot be tampered with on a machine, and a path in a resource name
/// cannot escape the directory it is written into.
/// </para>
/// <para>
/// A skill is a directory, not a file. Anything shipped beside the
/// <c>SKILL.md</c> — a script the skill tells the session to run, a template it
/// fills in — travels with it, because a skill without them fails at the first
/// instruction it gives.
/// </para>
/// </remarks>
internal static class BuiltInSkills
{
    private const string Prefix = "Loadout.Agents.Skills.";

    /// <summary>
    /// Every resource that belongs to a skill, keyed by the skill's name.
    /// </summary>
    /// <remarks>
    /// A resource name flattens the path with dots, so the file's own extension
    /// cannot be told from a directory separator by looking. The layout is
    /// fixed and shallow — <c>Skills/&lt;name&gt;/&lt;file&gt;</c> — so the
    /// name is the first segment and everything after it is the file, with the
    /// last dot put back as the extension.
    /// </remarks>
    private static readonly Lazy<IReadOnlyDictionary<string, List<(string Resource, string File)>>> Shipped =
        new(Read);

    /// <summary>The names of the skills that ship, for a caller deciding whether there is anything to do.</summary>
    internal static IReadOnlyCollection<string> Names => (IReadOnlyCollection<string>)Shipped.Value.Keys;

    private static IReadOnlyDictionary<string, List<(string, string)>> Read()
    {
        var found = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in typeof(BuiltInSkills).Assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = resource[Prefix.Length..];
            var split = rest.IndexOf('.', StringComparison.Ordinal);

            if (split <= 0)
            {
                continue;
            }

            var name = rest[..split];
            var remainder = rest[(split + 1)..];

            // The last dot is the extension; any before it were directory
            // separators, which this layout does not have but which a resource
            // name would spell the same way.
            var dot = remainder.LastIndexOf('.');

            var file = dot <= 0
                ? remainder
                : remainder[..dot].Replace('.', '/') + remainder[dot..];

            if (!found.TryGetValue(name, out var files))
            {
                files = [];
                found[name] = files;
            }

            files.Add((resource, file));
        }

        // A directory with no SKILL.md in it is not a skill, however many other
        // files came with it.
        return found
            .Where(pair => pair.Value.Any(entry =>
                entry.Item2.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Writes one shipped skill into a directory of its own.</summary>
    internal static void WriteTo(string name, string destination)
    {
        if (!Shipped.Value.TryGetValue(name, out var files))
        {
            return;
        }

        var assembly = typeof(BuiltInSkills).Assembly;

        foreach (var (resource, file) in files)
        {
            var path = Path.Combine(destination, file.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var stream = assembly.GetManifestResourceStream(resource);

            if (stream is null)
            {
                continue;
            }

            using var target = File.Create(path);

            stream.CopyTo(target);
        }
    }
}
