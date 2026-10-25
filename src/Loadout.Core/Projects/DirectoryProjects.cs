using System.Collections.Concurrent;
using System.Text.Json;
using Loadout.Models.Projects;

namespace Loadout.Core.Projects;

/// <summary>
/// Which registered project each directory a transcript names belongs to,
/// remembered while the registered projects stay the same.
/// </summary>
/// <remarks>
/// <para>
/// Working that out is a repository question - where its root is, whether it
/// carries a project's mark, what its remote is - and each is a Git process.
/// A month of transcripts can name well over a hundred directories, most of
/// them worktrees and throwaway checkouts that match nothing, and asking about
/// every one on every read was most of what 'usage' waited for once the
/// transcripts themselves were no longer being read again. The session list
/// asks the same question of the same directories.
/// </para>
/// <para>
/// What a directory belongs to changes when a project is registered, removed,
/// moved or given another remote, and the answers are dropped whenever the
/// list of projects says any of that happened. It can also change on the
/// repository's side - a remote added to a checkout nothing claimed - which
/// no list shows, so an answer is also asked again once it is a week old.
/// Kept in memory, shared by everything in the process that asks, and on
/// disk so the command line benefits as well.
/// </para>
/// </remarks>
internal sealed class DirectoryProjects
{
    /// <summary>How long an answer is trusted when nothing visible says it changed.</summary>
    internal static readonly TimeSpan Kept = TimeSpan.FromDays(7);

    private const string FileName = "directories.json";

    /// <summary>
    /// The answers this process has, whoever asks: static because in the
    /// daemon each command gets services of its own.
    /// </summary>
    internal static readonly DirectoryProjects Shared = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, Answer> _answers;
    private readonly Lock _gate = new();
    private string _projects = string.Empty;
    private string? _loadedFrom;
    private bool _changed;

    internal DirectoryProjects() =>
        _answers = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    /// The project a directory belongs to, remembered or asked.
    /// </summary>
    /// <param name="directory">The directory a transcript named.</param>
    /// <param name="now">The time, for how old an answer is.</param>
    /// <param name="ask">Works it out, returning the slug or null for none.</param>
    /// <param name="ct">Stops the asking.</param>
    internal async Task<string?> SlugAsync(
        string directory, DateTimeOffset now, Func<string, CancellationToken, Task<string?>> ask, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ask);

        if (_answers.TryGetValue(directory, out var known) && now - known.At < Kept)
        {
            return known.Slug;
        }

        var slug = await ask(directory, ct).ConfigureAwait(false);

        _answers[directory] = new Answer(slug, now);
        Volatile.Write(ref _changed, true);

        return slug;
    }

    /// <summary>
    /// Takes the registered projects as they are now, dropping every answer
    /// when they are not what the answers were given against.
    /// </summary>
    /// <param name="projects">Every registered project.</param>
    /// <param name="kept">The readings directory, or null to keep nothing on disk.</param>
    internal void Against(IReadOnlyList<ProjectResolution> projects, string? kept)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var now = Fingerprint(projects);

        lock (_gate)
        {
            if (kept is not null && !string.Equals(_loadedFrom, kept, StringComparison.Ordinal))
            {
                _loadedFrom = kept;
                Load(kept);
            }

            if (!string.Equals(_projects, now, StringComparison.Ordinal))
            {
                _answers.Clear();
                _projects = now;
                Volatile.Write(ref _changed, true);
            }
        }
    }

    /// <summary>Writes the answers to disk, when anything changed since they were loaded or last written.</summary>
    internal void Save(string? kept)
    {
        if (kept is null || !Volatile.Read(ref _changed))
        {
            return;
        }

        lock (_gate)
        {
            Volatile.Write(ref _changed, false);

            var path = Path.Combine(kept, FileName);
            var temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

            try
            {
                Directory.CreateDirectory(kept);

                File.WriteAllText(temporary, JsonSerializer.Serialize(
                    new Stored(_projects, new Dictionary<string, Answer>(_answers, _answers.Comparer)), Json));

                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception left) when (left is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    /// <summary>Drops everything held in memory, as a new process starts with nothing.</summary>
    internal void Forget()
    {
        lock (_gate)
        {
            _answers.Clear();
            _projects = string.Empty;
            _loadedFrom = null;
            Volatile.Write(ref _changed, false);
        }
    }

    /// <summary>Where the answers are kept between runs, under the state directory.</summary>
    internal static string Under(string state) => Path.Combine(state, "attribution");

    /// <summary>
    /// The project each directory belongs to, as a function that remembers,
    /// for a caller about to ask about several.
    /// </summary>
    /// <param name="projects">The registered projects, read once for the whole batch.</param>
    /// <param name="kept">Where answers are kept between runs, or null for nowhere.</param>
    /// <param name="ct">Stops the asking.</param>
    internal static async Task<Func<string, Task<string?>>> AskingAsync(
        IProjectService projects, string? kept, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projects);

        async Task<string?> AskAsync(string directory, CancellationToken token)
        {
            var result = await projects.ResolveFromDirectoryAsync(directory, token).ConfigureAwait(false);

            return result.Succeeded ? result.Value?.Entry.Slug : null;
        }

        // Remembered answers only hold while the projects are the ones they
        // were given against; without the list, every directory is asked.
        var listed = await projects.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return directory => AskAsync(directory, ct);
        }

        Shared.Against(listed.Value!, kept);

        var now = DateTimeOffset.UtcNow;

        return async directory =>
        {
            var slug = await Shared.SlugAsync(directory, now, AskAsync, ct).ConfigureAwait(false);

            Shared.Save(kept);

            return slug;
        };
    }

    /// <summary>What about the registered projects decides which directory is whose.</summary>
    /// <remarks>
    /// Their names, where they are and their remotes. Not when they were last
    /// launched, which changes all the time and decides nothing here.
    /// </remarks>
    internal static string Fingerprint(IReadOnlyList<ProjectResolution> projects) =>
        string.Join(
            '\n',
            projects
                .Select(project => $"{project.Entry.Slug}\t{project.LocalPath}\t{project.Entry.Remote}")
                .Order(StringComparer.Ordinal));

    private void Load(string kept)
    {
        try
        {
            var path = Path.Combine(kept, FileName);

            if (!File.Exists(path)
                || JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), Json) is not { Answers: { } answers } stored)
            {
                return;
            }

            _projects = stored.Projects ?? string.Empty;

            foreach (var (directory, answer) in answers)
            {
                _answers.TryAdd(directory, answer);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Only a cache: every directory is asked about again.
        }
    }

    private sealed record Answer(string? Slug, DateTimeOffset At);

    private sealed record Stored(string? Projects, Dictionary<string, Answer>? Answers);
}
