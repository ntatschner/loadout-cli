using System.Text.Json;

namespace Loadout.Agents.Teams.Steps;

/// <summary>The run's record: one JSON line per thing that happened, appended as it happens.</summary>
internal sealed class JournalWriter
{
    private static readonly JsonSerializerOptions JournalLine = new() { WriteIndented = false };

    private readonly string _path;
    private readonly string _run;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JournalWriter(string path, string run, TimeProvider time)
    {
        _path = path;
        _run = run;
        _time = time;
    }

    /// <summary>The run's directory, which is where everything else goes.</summary>
    public string Directory => Path.GetDirectoryName(_path) ?? string.Empty;

    /// <summary>When it is, by whatever clock the run is keeping.</summary>
    public DateTimeOffset Now => _time.GetUtcNow();

    /// <summary>
    /// Appends one line to a file beside the journal, and never fails a run
    /// over it.
    /// </summary>
    public async Task AppendAsync(string file, string line, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await File.AppendAllTextAsync(Path.Combine(Directory, file), line + "\n", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Records one thing that happened.</summary>
    /// <param name="kind">What happened.</param>
    /// <param name="node">Which node, or null for the run itself.</param>
    /// <param name="data">Whatever that kind carries.</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="at">
    /// When it happened, where that is not now. An event harvested out of a
    /// side file happened when the file says, not when it was read: the
    /// permission decisions are folded in at the end of a node's turn, and
    /// dating them at the fold put two of them minutes late and after the
    /// node had already ended.
    /// </param>
    public async Task WriteAsync(
        string kind,
        string? node,
        object? data,
        CancellationToken ct,
        DateTimeOffset? at = null)
    {
        var line = JsonSerializer.Serialize(
            new { at = at ?? _time.GetUtcNow(), run = _run, node, kind, data }, JournalLine);

        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await File.AppendAllTextAsync(_path, line + "\n", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A journal that cannot be written must not stop the run; the
            // outcome still carries the result.
        }
        finally
        {
            _lock.Release();
        }
    }
}
