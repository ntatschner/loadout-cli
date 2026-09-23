namespace Loadout.Core.Teams;

/// <summary>
/// Telling a run in another process to stop, hold, or read something.
/// </summary>
/// <remarks>
/// <para>
/// A run is a command the daemon started, so nothing can reach into it. The
/// same channel its questions use works in the other direction: files in the
/// run's own directory, which it looks at between rounds.
/// </para>
/// <para>
/// Between rounds, and that is the contract rather than a limitation being
/// apologised for. A node is a headless agent mid-turn; the only ways to end
/// that sooner are to kill it, which loses the turn and the money spent on it,
/// or to ask it to stop, which it cannot hear. So a stop lands when the turn
/// it is in comes back, and the page says so rather than pretending otherwise.
/// </para>
/// <para>
/// Everything here fails quiet. A run that cannot read its own directory is a
/// run with worse problems, and a control that threw would turn a missing file
/// into a dead run.
/// </para>
/// </remarks>
public static class RunControl
{
    /// <summary>Asked to end after the round it is in.</summary>
    public const string StopFile = "control-stop";

    /// <summary>Asked to hold before the next round until this goes.</summary>
    public const string PauseFile = "control-pause";

    /// <summary>Things said to the lead, oldest first, one per file.</summary>
    public const string MessagePrefix = "control-message-";

    /// <summary>What the run may now spend, in USD, when somebody has changed it.</summary>
    public const string BudgetFile = "control-budget";

    /// <summary>
    /// The budget somebody set while the run was going, or null for the
    /// team's own.
    /// </summary>
    /// <remarks>
    /// Read at the same check the team's budget is, between rounds, so a raise
    /// lands before the next lead turn and a run that already stopped on its
    /// budget is not reached by one.
    /// </remarks>
    public static decimal? Budget(string directory)
    {
        try
        {
            var path = Path.Combine(directory, BudgetFile);

            return File.Exists(path)
                && decimal.TryParse(
                    File.ReadAllText(path).Trim(),
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var usd)
                && usd > 0
                    ? usd
                    : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Sets what the run may spend from its next round on.</summary>
    public static Task SetBudgetAsync(string directory, decimal usd, CancellationToken ct = default) =>
        WriteAsync(
            directory,
            BudgetFile,
            usd.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            ct);

    /// <summary>Whether somebody has asked this run to stop.</summary>
    public static bool Stopped(string directory) => There(directory, StopFile);

    /// <summary>Whether somebody has asked this run to hold.</summary>
    public static bool Paused(string directory) => There(directory, PauseFile);

    /// <summary>Asks a run to end after the round it is in.</summary>
    public static Task StopAsync(string directory, CancellationToken ct = default) =>
        WriteAsync(directory, StopFile, "stop", ct);

    /// <summary>Asks a run to hold before its next round.</summary>
    public static Task PauseAsync(string directory, CancellationToken ct = default) =>
        WriteAsync(directory, PauseFile, "pause", ct);

    /// <summary>Takes back a stop, for a run that is being picked up again.</summary>
    public static void ClearStop(string directory) => Remove(directory, StopFile);

    /// <summary>Lets a held run carry on.</summary>
    public static void Resume(string directory) => Remove(directory, PauseFile);

    /// <summary>Leaves something for the lead to read at the start of its next round.</summary>
    public static Task SayAsync(string directory, string message, CancellationToken ct = default) =>
        WriteAsync(
            directory,

            // Named by the clock so they are read in the order they were said.
            $"{MessagePrefix}{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..4]}.txt",
            message,
            ct);

    /// <summary>
    /// Everything said to the lead since the last time this was asked, and
    /// takes it, so the same message is never given twice.
    /// </summary>
    public static IReadOnlyList<string> TakeMessages(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var said = new List<string>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, MessagePrefix + "*.txt")
                .OrderBy(name => name, StringComparer.Ordinal))
            {
                try
                {
                    var text = File.ReadAllText(file);

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        said.Add(text.Trim());
                    }

                    // Taken, not read: a message handed to the lead twice
                    // would read as somebody repeating themselves crossly.
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Being written as it was read. Next round gets it.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }

        return said;
    }

    private static bool There(string directory, string name)
    {
        try
        {
            return File.Exists(Path.Combine(directory, name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task WriteAsync(string directory, string name, string what, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(Path.Combine(directory, name), what, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Remove(string directory, string name)
    {
        try
        {
            var path = Path.Combine(directory, name);

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
