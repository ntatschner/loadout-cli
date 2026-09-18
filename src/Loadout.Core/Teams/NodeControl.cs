namespace Loadout.Core.Teams;

/// <summary>
/// Saying something to one node while it is still working.
/// </summary>
/// <remarks>
/// <para>
/// The lead already reads messages between its rounds, which is the right
/// place for "change the plan". This is the other one: a worker has gone the
/// wrong way and is spending money doing it, and waiting for its turn to come
/// back means waiting for exactly the spend somebody is trying to stop.
/// </para>
/// <para>
/// A file rather than a socket, like everything else that reaches into a run.
/// Whoever runs the team owns the process; whoever asks writes a file, and the
/// run picks it up between the events it is already reading. Nothing has to
/// find a port, nothing stays connected, and a run that has gone leaves a file
/// nobody reads rather than a connection somebody is waiting on.
/// </para>
/// <para>
/// Read and deleted in one go, so a message is delivered once. A message that
/// arrived twice would be a message the node was told twice, which reads as
/// insistence rather than as a bug.
/// </para>
/// </remarks>
public static class NodeControl
{
    /// <summary>What a message to one node is called.</summary>
    /// <remarks>
    /// One file per node and per moment, so two messages in quick succession
    /// are two messages rather than one overwriting the other.
    /// </remarks>
    public static string FileFor(string node, string when) =>
        $"say-{NodePermissions.FileSafe(node)}-{when}.txt";

    /// <summary>Leaves something for a node to be told.</summary>
    public static async Task SayAsync(
        string directory,
        string node,
        string message,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            Path.Combine(directory, FileFor(node, now.ToUnixTimeMilliseconds().ToString(
                System.Globalization.CultureInfo.InvariantCulture))),
            message,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Everything left for one node, taken away as it is read.
    /// </summary>
    /// <remarks>
    /// Oldest first, because two things said in order were meant in order.
    /// </remarks>
    public static IReadOnlyList<string> Take(string directory, string node)
    {
        if (string.IsNullOrWhiteSpace(directory)
            || string.IsNullOrWhiteSpace(node)
            || !Directory.Exists(directory))
        {
            return [];
        }

        var mine = $"say-{NodePermissions.FileSafe(node)}-";
        var said = new List<(string Path, string Text)>();

        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, mine + "*.txt")
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                said.Add((path, File.ReadAllText(path)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A message that cannot be read is not worth stopping a run over,
            // and it stays on disk for the next look.
            return [];
        }

        foreach (var (path, _) in said)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left behind, and said again next time. Saying something twice
                // reads as insistence; losing it reads as the feature not
                // working.
            }
        }

        return [.. said.Select(one => one.Text).Where(text => text.Trim().Length > 0)];
    }
}
