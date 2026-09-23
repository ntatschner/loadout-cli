using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loadout.Core.Tools;

/// <summary>One line of the audit log.</summary>
/// <param name="At">When.</param>
/// <param name="Action">submit, verify, reject, promote, activate, used, deprecate, retire, stand-down.</param>
/// <param name="Tool">The tool, or empty for a submission about none.</param>
/// <param name="Version">The version, where there is one.</param>
/// <param name="Actor">Who: a node, a person, the gate.</param>
/// <param name="Run">The run, where there is one.</param>
/// <param name="Note">What happened, never a secret.</param>
public sealed record ToolAuditEntry(
    DateTimeOffset At,
    string Action,
    string Tool,
    string? Version = null,
    string? Actor = null,
    string? Run = null,
    string? Note = null);

/// <summary>
/// The registry's append-only log: everything that changed it, and who did.
/// </summary>
/// <remarks>
/// A line per event, so an interrupted write costs one line and not the file,
/// and so reading it back needs no parser beyond one line at a time. Activity
/// goes here and not into any team's brief, which is what keeps it from
/// flooding everybody else's context.
/// </remarks>
public static class ToolAudit
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Adds one entry to the end of the log.</summary>
    public static void Append(string file, ToolAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.AppendAllText(file, JsonSerializer.Serialize(entry, Json) + "\n");
    }

    /// <summary>Every entry, oldest first. A line that cannot be read is skipped, not fatal.</summary>
    public static IReadOnlyList<ToolAuditEntry> Read(string file)
    {
        if (!File.Exists(file))
        {
            return [];
        }

        var entries = new List<ToolAuditEntry>();

        foreach (var line in File.ReadLines(file))
        {
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<ToolAuditEntry>(line, Json) is { } entry)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // One damaged line is one lost event, and the rest still read.
            }
        }

        return entries;
    }
}
