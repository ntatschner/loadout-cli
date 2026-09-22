using System.Diagnostics;
using System.Runtime.CompilerServices;
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
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAllQuietly();
        Console.CancelKeyPress += (_, _) => StopAllQuietly();
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

    /// <summary>
    /// Stops the children from a handler that is not allowed to throw.
    /// </summary>
    /// <remarks>
    /// An exception leaving <c>ProcessExit</c> is unhandled by definition:
    /// there is no frame above it to catch it, so a process that has already
    /// done what it was asked ends in a crash report anyway. Nothing this
    /// does is worth that, and an exit handler has nowhere to report it to.
    /// </remarks>
    internal void StopAllQuietly()
    {
        try
        {
            StopAll();
        }
        catch (Exception)
        {
            // Everything, deliberately: see above. The alternative is a
            // shutdown taken down by the thing tidying up after it.
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
            Stop(id, startedAt);
        }
    }

    /// <summary>Stops one child, if it is still the child that was adopted.</summary>
    /// <remarks>
    /// <para>
    /// Its own method, and never inlined, because this is the only code here
    /// that names <see cref="Process"/>. The JIT resolves the types a method
    /// names when it compiles the method, before a line of it runs, so while
    /// this lived inside <see cref="StopAll"/> every exit had to load
    /// System.Diagnostics.Process - including the exits with no children to
    /// stop, which is most of them.
    /// </para>
    /// <para>
    /// <c>loadout update</c> is where that mattered. A self-contained
    /// single-file build reads its assemblies back out of the bundle at its
    /// own path, at offsets it recorded when it started, and the update has
    /// just replaced that path with a different bundle - so an assembly not
    /// already loaded can no longer be loaded at all. Every successful
    /// update ended in an unhandled FileNotFoundException from the exit
    /// handler, after the work itself had succeeded. Reproduced by updating
    /// one published build into another; a replacement that happens to leave
    /// this assembly's slice where it was does not show it, which is why
    /// some updates looked fine.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected virtual void Stop(int processId, DateTime startedAt)
    {
        try
        {
            using var child = Process.GetProcessById(processId);

            // The same number wearing a different process is exactly what
            // this check is for.
            if (child.StartTime != startedAt || child.HasExited)
            {
                return;
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
