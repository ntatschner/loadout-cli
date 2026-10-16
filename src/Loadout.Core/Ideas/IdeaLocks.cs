using System.Globalization;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Ideas;

/// <summary>
/// Which ideas have a round running on this machine, and holding one while it
/// does.
/// </summary>
/// <remarks>
/// <para>
/// This machine only. A lock kept here can ask whether its process is still
/// running, so a round that was killed never holds its idea for good. One in
/// the workspace would be seen from every machine, but no machine can tell
/// whether a process on another has died, so a crashed round there would need
/// a time limit or somebody to clear it by hand. Two people refining the same
/// idea at once on two machines is rare, and the record's own write lock and
/// the later round winning is an acceptable answer to it.
/// </para>
/// <para>
/// The common case it is for is a terminal and a page on one PC: without it,
/// both can start a round on the same idea, each spending money, and the list
/// goes on saying the last round failed while a new one is already running.
/// </para>
/// </remarks>
public interface IIdeaLocks
{
    /// <summary>
    /// Holds the idea for a round, until what is returned is disposed. Refused
    /// while another round on it is running on this machine.
    /// </summary>
    OperationResult<IDisposable> Take(IdeaPlace place);

    /// <summary>Whether a round on the idea is running on this machine.</summary>
    bool Held(IdeaPlace place);
}

/// <inheritdoc />
internal sealed class IdeaLocks : IIdeaLocks
{
    private readonly IPlatformPaths _paths;
    private readonly IProcessInspector _processes;

    public IdeaLocks(IPlatformPaths paths, IProcessInspector processes)
    {
        _paths = paths;
        _processes = processes;
    }

    /// <inheritdoc />
    public OperationResult<IDisposable> Take(IdeaPlace place)
    {
        ArgumentNullException.ThrowIfNull(place);

        var path = PathOf(place);
        var mine = $"{_processes.CurrentProcessId.ToString(CultureInfo.InvariantCulture)} "
            + _processes.CurrentProcessStartedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Twice at most: once as it is, and once more after clearing a lock
            // whose round is no longer running.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(file))
                    {
                        writer.Write(mine);
                    }

                    return OperationResult<IDisposable>.Ok(new Release(path, mine));
                }
                catch (IOException) when (File.Exists(path))
                {
                    if (Holder(path) is { } holder && _processes.IsRunning(holder.Pid, holder.StartedAt))
                    {
                        return OperationResult<IDisposable>.Fail(
                            $"A round on {place.Id} is already running on this machine (process {holder.Pid}). "
                            + "Wait for it to finish: a second would be asked the same thing and cost as much again.",
                            ExitCode.InvalidArguments);
                    }

                    File.Delete(path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IDisposable>.Fail(
                $"Could not mark {place.Id} as having a round running: {ex.Message}");
        }

        return OperationResult<IDisposable>.Fail(
            $"Could not mark {place.Id} as having a round running: another round took it at the same moment.",
            ExitCode.InvalidArguments);
    }

    /// <inheritdoc />
    public bool Held(IdeaPlace place)
    {
        ArgumentNullException.ThrowIfNull(place);

        var path = PathOf(place);

        return File.Exists(path)
            && Holder(path) is { } holder
            && _processes.IsRunning(holder.Pid, holder.StartedAt);
    }

    private string PathOf(IdeaPlace place) =>
        Path.Combine(_paths.Paths.State, "idea-rounds", place.Project ?? "-", place.Id + ".lock");

    /// <summary>The process a lock names, or null when it cannot be read.</summary>
    private static (int Pid, DateTimeOffset StartedAt)? Holder(string path)
    {
        try
        {
            var parts = File.ReadAllText(path).Split(' ', 2, StringSplitOptions.TrimEntries);

            return parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
                && DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started)
                ? (pid, started)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Lets the idea go, unless another round has taken it since.</summary>
    private sealed class Release(string path, string mine) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
            {
                return;
            }

            _done = true;

            try
            {
                if (File.Exists(path) && File.ReadAllText(path) == mine)
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left behind, it names a process that is about to end, and
                // the next round clears it.
            }
        }
    }
}
