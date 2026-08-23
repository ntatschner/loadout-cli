using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace Loadout.Core.Backups;

/// <summary>
/// Reduces a structured file to a map of key path to content digest.
/// <para>
/// Used to tell somebody what a restore is about to cost them. Restoring a
/// settings file whole silently discards every key written since the snapshot,
/// and nothing in a file-level backup can see that: the digests match what was
/// captured, the restore reports success, and a setting the person turned on
/// last week is simply gone.
/// </para>
/// <para>
/// Paths only, never values. A settings file can hold a credential or a command
/// line containing one, and a drift report is printed to a terminal and quite
/// possibly a log.
/// </para>
/// </summary>
public static class StructuredShape
{
    /// <summary>
    /// How deep to walk. Deep enough for any settings file, bounded so a
    /// pathological document cannot hang a restore.
    /// </summary>
    private const int MaximumDepth = 12;

    /// <summary>
    /// Separator used when flattening a node for hashing. A control character
    /// so no ordinary content can forge a boundary and make two different
    /// documents hash alike.
    /// </summary>
    private const char Separator = '\u001f';

    /// <summary>Whether this file has a shape worth comparing.</summary>
    public static bool IsStructured(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".json" or ".yaml" or ".yml";

    /// <summary>
    /// Key path to digest for every leaf and container in the file, or null
    /// when the file cannot be parsed.
    /// <para>
    /// Null rather than empty, and the caller treats it as "no comparison
    /// possible". An unparseable file reported as having no keys would look
    /// like every key had been dropped.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var shape = new Dictionary<string, string>(StringComparer.Ordinal);

            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var document = JsonDocument.Parse(
                    text,
                    new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                    });

                WalkJson(document.RootElement, string.Empty, shape, 0);
            }
            else
            {
                var stream = new YamlStream();
                using var reader = new StringReader(text);
                stream.Load(reader);

                if (stream.Documents.Count == 0)
                {
                    return null;
                }

                WalkYaml(stream.Documents[0].RootNode, string.Empty, shape, 0);
            }

            return shape;
        }
        catch (Exception ex) when (ex is JsonException
            or YamlDotNet.Core.YamlException
            or IOException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WalkJson(
        JsonElement element,
        string prefix,
        Dictionary<string, string> shape,
        int depth)
    {
        if (depth > MaximumDepth)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                shape[prefix] = Digest(element.GetRawText());

                foreach (var property in element.EnumerateObject())
                {
                    WalkJson(property.Value, Join(prefix, property.Name), shape, depth + 1);
                }

                break;

            case JsonValueKind.Array:
                // Elements are not walked individually. An array is ordered and
                // usually rewritten wholesale, so per-index paths would report
                // drift on every reordering and say nothing useful.
                shape[prefix] = Digest(element.GetRawText());
                break;

            default:
                shape[prefix] = Digest(element.GetRawText());
                break;
        }
    }

    private static void WalkYaml(
        YamlNode node,
        string prefix,
        Dictionary<string, string> shape,
        int depth)
    {
        if (depth > MaximumDepth)
        {
            return;
        }

        switch (node)
        {
            case YamlMappingNode mapping:
                shape[prefix] = Digest(Flatten(mapping));

                foreach (var pair in mapping.Children)
                {
                    var key = (pair.Key as YamlScalarNode)?.Value ?? pair.Key.ToString();
                    WalkYaml(pair.Value, Join(prefix, key ?? "?"), shape, depth + 1);
                }

                break;

            case YamlSequenceNode sequence:
                shape[prefix] = Digest(string.Join('\u001f', sequence.Children.Select(Flatten)));
                break;

            default:
                shape[prefix] = Digest(Flatten(node));
                break;
        }
    }

    /// <summary>
    /// A stable text form of a node, used only as digest input. It never
    /// reaches a report, so readability does not matter; stability does.
    /// </summary>
    private static string Flatten(YamlNode node) => node switch
    {
        YamlScalarNode scalar => scalar.Value ?? string.Empty,

        YamlSequenceNode sequence => string.Join('\u001f', sequence.Children.Select(Flatten)),

        YamlMappingNode mapping => string.Join(
            '',
            mapping.Children
                .OrderBy(p => (p.Key as YamlScalarNode)?.Value ?? string.Empty, StringComparer.Ordinal)
                .Select(p => ((p.Key as YamlScalarNode)?.Value ?? "?") + '\u001f' + Flatten(p.Value))),

        _ => string.Empty,
    };

    private static string Join(string prefix, string key) =>
        prefix.Length == 0 ? key : prefix + "." + key;

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
