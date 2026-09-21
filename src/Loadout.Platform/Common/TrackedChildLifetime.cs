using System.Diagnostics;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Common;

/// <summary>
/// Remembers the launcher's children and stops them on the way out.
/// </summary>
/// <remarks>
/// <para>
/// The portable half of the promise, and the weaker half: it covers a
/// process asked to stop, which is Ctrl+C, a closed terminal that sends a
/// signal, and an ordinary return from main. It cannot cover a process
/// terminated outright, because nothing in the process runs at that point.
/// So it reports itself as unenforced, and where a platform can do better it
/// is wrapped by something that does.
/// </para>
/// <para>
/// A child is remembered by identifier and start time together: identifiers
/// are reused, and killing a number that has since come round again would
/// stop a stranger's process.
/// </para>
/// </remarks>
public class TrackedChildLifetime : IChildLifetime, IDisposable
{
    private readonly List<(int Id, DateTime StartedAt)> _children = [];
    private readonly Lock _lock = new();
    private bool _stopped;

    public TrackedChildLifetime()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAll();
        Console.CancelKeyPress += (_, _) => StopAll();
    }

    /// <inheritdoc />
    public virtual bool IsEnforced => false;

    /// <inheritdoc />
    public virtual string Detail =>
        "children are stopped when the launcher exits or is interrupted, and not when it is terminated outright";

    /// <inheritdoc />
    public virtual void Adopt(int processId)
    {
        try
        {
            using var child = Process.GetProcessById(processId);

            Remember(processId, child.StartTime);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Already gone. There is nothing to be responsible for.
        }
    }

    /// <summary>
    /// Takes responsibility for a child whose start time has already been
    /// read, so a caller that has the process in hand need not look it up
    /// again.
    /// </summary>
    protected void Remember(int processId, DateTime startedAt)
    {
        lock (_lock)
        {
            _children.Add((processId, startedAt));
        }
    }

    /// <summary>
    /// Stops the children now. Disposing this says the launcher is finished
    /// with them, which is the same thing its exit says.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc cref="Dispose()" />
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAll();
        }
    }

    /// <summary>Stops every child still running. Safe to call more than once.</summary>
    protected void StopAll()
    {
        List<(int Id, DateTime StartedAt)> children;

        lock (_lock)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            children = [.. _children];
        }

        foreach (var (id, startedAt) in children)
        {
            try
            {
                using var child = Process.GetProcessById(id);

                // The same number wearing a different process is exactly what
                // this check is for.
                if (child.StartTime != startedAt || child.HasExited)
                {
                    continue;
                }

                child.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Gone between the check and the kill, or not ours to stop.
                // Either way there is nothing useful to do from an exit
                // handler, and throwing there takes the shutdown with it.
            }
        }
    }
}
