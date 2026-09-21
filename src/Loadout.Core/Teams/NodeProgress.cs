using System.Text.Json;
using Loadout.Core.Security;

namespace Loadout.Core.Teams;

/// <summary>What a node says it is doing, in its own words.</summary>
/// <param name="At">When it said so.</param>
/// <param name="Node">Which node, stamped from its policy rather than taken on trust.</param>
/// <param name="Role">What it is playing.</param>
/// <param name="Step">Which piece of work it is on.</param>
/// <param name="Of">How many it expects, or 0 when it does not know.</param>
/// <param name="Doing">One present-tense sentence.</param>
public sealed record NodeSaid(
    DateTimeOffset At,
    string Node,
    string Role,
    int Step,
    int Of,
    string Doing)
{
    /// <summary>The line a person reads.</summary>
    public string Line =>
        Of > 0 ? $"{Step} of {Of}: {Doing}" : $"{Step}: {Doing}";
}

/// <summary>
/// A node's own account of what it is doing, beside the run's observation of
/// it.
/// </summary>
/// <remarks>
/// <para>
/// Two accounts on purpose, and neither corrects the other. The run already
/// writes what it <em>observes</em> — the tool a node just called and the one
/// thing that call was pointed at — which is precise and says nothing about
/// why. This is what the node says it is up to, which says why and may be
/// wrong. Somebody watching a run that has gone quiet needs both: a node
/// looping on one file and a node carefully reading forty look identical from
/// the outside, and only one of them is stuck.
/// </para>
/// <para>
/// A file per node rather than the journal, for the reason the permission
/// record has: the journal is appended to by the coordinator, this is written
/// by a different process, and two processes appending to one file is how a
/// record acquires half lines.
/// </para>
/// </remarks>
public static class NodeProgress
{
    /// <summary>Where a node writes what it says it is doing.</summary>
    public static string FileName(string node) => $"said-{NodePermissions.FileSafe(node)}.jsonl";

    /// <summary>The longest sentence kept, so one line stays one line.</summary>
    public const int Longest = 160;

    /// <summary>Records one line, redacted and cut to fit.</summary>
    public static async Task AppendAsync(
        string directory,
        NodeSaid said,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(said);

        Directory.CreateDirectory(directory);

        // Redacted here rather than by whoever draws it. A node describing its
        // own work in a sentence is exactly where a token pasted from a command
        // ends up, and this file is read by a screen and by the dashboard.
        var safe = said with { Doing = Cut(SecretRedactor.Redact(said.Doing)) };

        await File.AppendAllTextAsync(
            Path.Combine(directory, FileName(said.Node)),
            JsonSerializer.Serialize(safe) + "\n",
            ct).ConfigureAwait(false);
    }

    /// <summary>Everything a node said, oldest first. A line that will not parse is left out.</summary>
    public static IReadOnlyList<NodeSaid> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var said = new List<NodeSaid>();

        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    if (JsonSerializer.Deserialize<NodeSaid>(line) is { } one)
                    {
                        said.Add(one);
                    }
                }
                catch (JsonException)
                {
                    // Half a line, from a read that caught a write. The rest
                    // of the file is still worth having.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return said;
        }

        return said;
    }

    private static string Cut(string text) =>
        text.Length <= Longest ? text : text[..(Longest - 1)] + "…";
}
