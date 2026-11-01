using System.Text.Json;

namespace Loadout.Core.Delivery;

/// <summary>One release cut from this machine.</summary>
/// <param name="Project">The project's slug.</param>
/// <param name="Tag">The tag.</param>
/// <param name="Version">The version.</param>
/// <param name="Commit">The commit released.</param>
/// <param name="Recipe">How: tag-push or workflow.</param>
/// <param name="At">When.</param>
public sealed record ReleaseRecord(string Project, string Tag, string Version, string Commit, string Recipe, DateTimeOffset At);

/// <summary>
/// The releases cut from this machine, one JSON line each in
/// <c>&lt;State&gt;/delivery/releases.jsonl</c>, kept so a trigger rule can
/// answer a release and so there is a record of who released what.
/// </summary>
/// <remarks>
/// Appended to, never rewritten. A line that cannot be read is passed over:
/// the release itself has happened either way, and the tag is the authority.
/// </remarks>
public static class ReleaseLog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>The log's path under a state directory.</summary>
    public static string PathIn(string state) => Path.Combine(state, "delivery", "releases.jsonl");

    /// <summary>Adds a release to the log.</summary>
    public static void Append(string state, ReleaseRecord release)
    {
        var path = PathIn(state);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, JsonSerializer.Serialize(release, Options) + "\n");
    }

    /// <summary>Every release in the log, oldest first.</summary>
    public static IReadOnlyList<ReleaseRecord> Read(string state)
    {
        var path = PathIn(state);

        if (!File.Exists(path))
        {
            return [];
        }

        var releases = new List<ReleaseRecord>();

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                try
                {
                    if (line.Trim().Length > 0 && JsonSerializer.Deserialize<ReleaseRecord>(line, Options) is { } one)
                    {
                        releases.Add(one);
                    }
                }
                catch (JsonException)
                {
                    // A torn or hand-edited line; the others still count.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return releases;
        }

        return releases;
    }
}
