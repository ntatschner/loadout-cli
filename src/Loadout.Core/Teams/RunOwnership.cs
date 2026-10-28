using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams;

/// <summary>The process that holds, or last held, a run.</summary>
/// <param name="ProcessId">Its identifier.</param>
/// <param name="StartedAt">When that process started, which is what tells a reused identifier apart.</param>
/// <param name="Machine">The machine it ran on, for the message only.</param>
/// <param name="TakenAt">When it took the run, or last said it still had it.</param>
public sealed record RunOwner(int ProcessId, DateTimeOffset StartedAt, string Machine, DateTimeOffset TakenAt);

/// <summary>What asking about a run's owner found.</summary>
public abstract record OwnerReading
{
    private OwnerReading()
    {
    }

    /// <summary>A process holds the run now.</summary>
    /// <param name="Owner">
    /// Which, where it said so beside the lock: the lock itself cannot be read
    /// while it is held. Null for a run taken by a build that did not.
    /// </param>
    public sealed record Held(RunOwner? Owner = null) : OwnerReading;

    /// <summary>
    /// The run had an owner and nothing holds it now: the process that drove
    /// it has gone.
    /// </summary>
    /// <param name="LastOwner">Who last held it, or null where the record could not be read.</param>
    public sealed record Free(RunOwner? LastOwner) : OwnerReading;

    /// <summary>
    /// The run has never had an owner recorded, which is what a run started by
    /// a build from before owners were kept looks like.
    /// </summary>
    public sealed record NoRecord : OwnerReading;

    /// <summary>
    /// The owner file is there and could not be opened for a reason other
    /// than somebody holding it, such as a permission taken away. Neither held
    /// nor free: nothing about the run's owner can be said.
    /// </summary>
    /// <param name="Why">What opening it said.</param>
    public sealed record Unknown(string Why) : OwnerReading;
}

/// <summary>
/// Which process is driving a team run, held for as long as it does.
/// </summary>
/// <remarks>
/// <para>
/// A run's journal only says a run is going until it writes its finish, so a
/// coordinator that was killed leaves a run that reads as running for good.
/// The lock here is what tells the two apart: the operating system lets go of
/// it when the process ends, however it ends.
/// </para>
/// <para>
/// This machine only. A run directory is local, and a lock means nothing to
/// another machine.
/// </para>
/// </remarks>
public interface IRunOwnership
{
    /// <summary>
    /// Holds the run until what is returned is disposed. Retries for a moment,
    /// because somebody asking about the run holds it briefly, then refuses.
    /// </summary>
    OperationResult<IDisposable> Take(string runDirectory);

    /// <summary>
    /// Whether anything holds the run. Takes the lock and lets it go at once;
    /// never writes the record and never creates it.
    /// </summary>
    OwnerReading Read(string runDirectory);

    /// <summary>Rewrites the record of a run this process holds, as taken now.</summary>
    void Touch(IDisposable held);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// One file, <c>owner.lock</c>, opened with no sharing. On Windows that also
/// stops anyone reading it while it is held; on Unix .NET turns no sharing
/// into an exclusive flock, and anything else into a shared one that two
/// holders could both have. So the holder must open it unshared, and the
/// owner's record can only be read once it is free, which is the only time
/// it is wanted: "not responding, last driven by process N".
/// </para>
/// <para>
/// So the same record is also written beside it, as <c>owner.json</c>, which
/// anybody may read while the run is held. It is read only to name the holder
/// of a run that has gone quiet for a long time, so that somebody can end the
/// right process; whether the run is held is still the lock's to say.
/// </para>
/// <para>
/// The file is never deleted on close. One left behind with nothing holding
/// it is what says a run had an owner who died, as against a run that never
/// had one.
/// </para>
/// </remarks>
internal sealed class FileRunOwnership : IRunOwnership
{
    /// <summary>The file in a run's directory that is locked.</summary>
    public const string FileName = "owner.lock";

    /// <summary>The same record, readable while the lock is held.</summary>
    public const string SaidName = "owner.json";

    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(50);

    private readonly IProcessInspector _processes;
    private readonly TimeProvider _time;
    private readonly TimeSpan _patience;

    public FileRunOwnership(IProcessInspector processes, TimeProvider time)
        : this(processes, time, TimeSpan.FromSeconds(2))
    {
    }

    internal FileRunOwnership(IProcessInspector processes, TimeProvider time, TimeSpan patience)
    {
        _processes = processes;
        _time = time;
        _patience = patience;
    }

    /// <inheritdoc />
    public OperationResult<IDisposable> Take(string runDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);

        if (!Directory.Exists(runDirectory))
        {
            return OperationResult<IDisposable>.Fail($"There is no run at {runDirectory} to take.");
        }

        var path = Path.Combine(runDirectory, FileName);
        var waited = Stopwatch.StartNew();

        while (true)
        {
            FileStream file;

            try
            {
                file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (UnauthorizedAccessException ex)
            {
                return OperationResult<IDisposable>.Fail($"Could not take the run at {runDirectory}: {ex.Message}");
            }
            catch (IOException) when (waited.Elapsed < _patience)
            {
                // Most likely somebody asking whether the run is held, which
                // holds it for no longer than it takes to look.
                Thread.Sleep(Pause);
                continue;
            }
            catch (IOException)
            {
                return OperationResult<IDisposable>.Fail(
                    $"Another process is driving the run at {runDirectory}, so this one will not.",
                    ExitCode.PolicyViolation);
            }

            var hold = new Hold(file);

            try
            {
                Write(hold);
            }
            catch (IOException ex)
            {
                hold.Dispose();

                return OperationResult<IDisposable>.Fail($"Could not record the owner of the run at {runDirectory}: {ex.Message}");
            }

            return OperationResult<IDisposable>.Ok(hold);
        }
    }

    /// <inheritdoc />
    public OwnerReading Read(string runDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);

        var path = Path.Combine(runDirectory, FileName);

        if (!File.Exists(path))
        {
            return new OwnerReading.NoRecord();
        }

        try
        {
            // Unshared, so that on Unix this asks for an exclusive flock. Any
            // flock, shared or exclusive, is refused while the holder has its
            // exclusive one; unshared also refuses a reader that only shares.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            return new OwnerReading.Free(Parse(file));
        }
        catch (FileNotFoundException)
        {
            return new OwnerReading.NoRecord();
        }
        catch (DirectoryNotFoundException)
        {
            return new OwnerReading.NoRecord();
        }
        catch (IOException ex) when (IsLockConflict(ex))
        {
            return new OwnerReading.Held(Said(runDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not held, as far as anyone can tell, and not free either. Read as
            // held, a lasting denial would keep the run live for good and close
            // would refuse it with no way out; read as free, a run still going
            // could be ended. So it is neither, and says why.
            return new OwnerReading.Unknown(ex.Message);
        }
    }

    /// <summary>Whether opening the file failed because another open holds it.</summary>
    /// <remarks>
    /// Windows reports a sharing or lock violation (32 or 33) in the low word
    /// of the HResult. On Unix .NET puts the raw errno there, and a refused
    /// flock is EWOULDBLOCK, which is 35 on macOS and FreeBSD and 11 on Linux.
    /// </remarks>
    private static bool IsLockConflict(IOException ex) =>
        OperatingSystem.IsWindows()
            ? (ex.HResult & 0xFFFF) is 32 or 33
            : ex.HResult == (OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD() ? 35 : 11);

    /// <inheritdoc />
    public void Touch(IDisposable held)
    {
        if (held is not Hold hold)
        {
            throw new ArgumentException("Only a hold this returned can be touched.", nameof(held));
        }

        if (!hold.Released)
        {
            Write(hold);
        }
    }

    private void Write(Hold hold)
    {
        var record = new OwnerRecord(
            _processes.CurrentProcessId,
            _processes.CurrentProcessStartedAt.ToUniversalTime(),
            Environment.MachineName,
            _time.GetUtcNow());

        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);

        hold.File.SetLength(0);
        hold.File.Position = 0;
        hold.File.Write(bytes);
        hold.File.Flush(flushToDisk: true);

        // Beside it, for naming the holder while it holds. Best effort: the
        // lock is what matters, and a run is not refused for want of this.
        try
        {
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(hold.File.Name)!, SaidName), bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Who said they hold the run, or null where nothing beside the lock says.</summary>
    private static RunOwner? Said(string runDirectory)
    {
        try
        {
            using var file = new FileStream(
                Path.Combine(runDirectory, SaidName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return Parse(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The owner a record names, or null when it cannot be read.</summary>
    private static RunOwner? Parse(FileStream file)
    {
        try
        {
            var record = JsonSerializer.Deserialize<OwnerRecord>(file);

            return record is { Machine: not null }
                ? new RunOwner(record.Pid, record.StartedAt, record.Machine, record.TakenAt)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record OwnerRecord(
        [property: JsonPropertyName("pid")] int Pid,
        [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
        [property: JsonPropertyName("machine")] string? Machine,
        [property: JsonPropertyName("takenAt")] DateTimeOffset TakenAt);

    /// <summary>The open file; the lock goes when it is closed.</summary>
    private sealed class Hold(FileStream file) : IDisposable
    {
        public FileStream File { get; } = file;

        public bool Released { get; private set; }

        public void Dispose()
        {
            if (Released)
            {
                return;
            }

            Released = true;
            File.Dispose();
        }
    }
}
