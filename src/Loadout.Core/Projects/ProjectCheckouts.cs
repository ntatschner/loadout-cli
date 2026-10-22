using System.Collections.Concurrent;

namespace Loadout.Core.Projects;

/// <summary>
/// Another checkout of a project that this process works in, in place of the registered one.
/// </summary>
/// <remarks>
/// <para>
/// For a queued run, which works in a worktree of its own so that nothing it
/// does touches the person's checkout, and nothing they do touches it. Set by
/// <c>team run --work-in</c>, and read by <see cref="ProjectService"/> so that
/// everything a run resolves - its own record, the merge gate, every node it
/// launches - agrees on where the project is.
/// </para>
/// <para>
/// One per process, deliberately. The daemon serves runs from the dashboard
/// in its own process, so it never sets this: a queued run it starts is a
/// process of its own, and the override ends with it.
/// </para>
/// </remarks>
public interface IProjectCheckouts
{
    /// <summary>The checkout this process uses for a project, or null for its registered one.</summary>
    string? For(string slug);

    /// <summary>Works in <paramref name="path"/> for the project from now on, in this process.</summary>
    void Use(string slug, string path);
}

/// <inheritdoc />
public sealed class ProjectCheckouts : IProjectCheckouts
{
    private readonly ConcurrentDictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string? For(string slug) => _paths.TryGetValue(slug, out var path) ? path : null;

    /// <inheritdoc />
    public void Use(string slug, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _paths[slug] = Path.GetFullPath(path);
    }
}
