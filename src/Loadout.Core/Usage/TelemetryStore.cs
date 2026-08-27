using System.Globalization;
using System.Text;
using System.Text.Json;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Usage;

/// <summary>What an agent reported, kept on this machine.</summary>
public interface ITelemetryStore
{
    /// <summary>Where the numbers are kept, so a person can go and look.</summary>
    string Path { get; }

    /// <summary>Adds what one payload reported.</summary>
    Task AppendAsync(IReadOnlyList<TelemetrySample> samples, CancellationToken ct = default);

    /// <summary>Everything reported on or after a moment, oldest first.</summary>
    Task<OperationResult<IReadOnlyList<TelemetrySample>>> ReadAsync(
        DateTimeOffset since,
        CancellationToken ct = default);
}

/// <summary>
/// A file of reported numbers, one JSON object per line.
/// </summary>
/// <remarks>
/// <para>
/// JSON lines rather than a database because that is what the data is: an
/// append-only record of small independent facts, written by one process and
/// read occasionally. A file can also be looked at, moved and deleted by the
/// person whose machine it is without a tool, which matters for something that
/// records when they were working.
/// </para>
/// <para>
/// Nothing here is conversation content. A row is a count, a model name, a
/// session identifier and a time — no prompt, no response, no file path, no
/// account and no email address, none of which are read from the payload in
/// the first place.
/// </para>
/// </remarks>
public sealed class TelemetryStore : ITelemetryStore
{
    private readonly IFilePermissions _permissions;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public TelemetryStore(IPlatformPaths paths, IFilePermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _permissions = permissions;

        Path = System.IO.Path.Combine(paths.Paths.State, "usage", "reported.jsonl");
    }

    /// <inheritdoc />
    public string Path { get; }

    /// <inheritdoc />
    public async Task AppendAsync(
        IReadOnlyList<TelemetrySample> samples,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder();

        foreach (var sample in samples)
        {
            builder.AppendLine(JsonSerializer.Serialize(new Row(
                sample.When.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                sample.Session,
                sample.Metric,
                sample.Kind,
                sample.Model,
                sample.Value,
                sample.IsCumulative)));
        }

        // One writer at a time. The receiver handles requests concurrently, and
        // two half-written lines interleaved would corrupt both.
        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);

            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
                _permissions.RestrictDirectoryToCurrentUser(directory);
            }

            await File.AppendAllTextAsync(Path, builder.ToString(), ct).ConfigureAwait(false);

            // It records when somebody was working and on what. That is theirs.
            _permissions.RestrictToCurrentUser(Path);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<TelemetrySample>>> ReadAsync(
        DateTimeOffset since,
        CancellationToken ct = default)
    {
        if (!File.Exists(Path))
        {
            return OperationResult<IReadOnlyList<TelemetrySample>>.Ok([]);
        }

        var samples = new List<TelemetrySample>();

        try
        {
            using var reader = new StreamReader(File.OpenRead(Path));

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                Row? row;

                try
                {
                    row = JsonSerializer.Deserialize<Row>(line);
                }
                catch (JsonException)
                {
                    // The last line, if the receiver was stopped mid-write.
                    continue;
                }

                if (row is null
                    || !DateTimeOffset.TryParse(
                        row.When,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var when)
                    || when < since)
                {
                    continue;
                }

                samples.Add(new TelemetrySample(
                    when,
                    row.Session,
                    row.Metric,
                    row.Kind,
                    row.Model,
                    row.Value,
                    row.Cumulative));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<TelemetrySample>>.Fail(
                $"Could not read the usage store at {Path}: {ex.Message}");
        }

        return OperationResult<IReadOnlyList<TelemetrySample>>.Ok(samples);
    }

    /// <summary>One line of the file.</summary>
    private sealed record Row(
        string When,
        string Session,
        string Metric,
        string Kind,
        string Model,
        double Value,
        bool Cumulative);
}
