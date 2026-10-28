using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Loadout.Core.Diagnostics;

/// <summary>
/// Counts the files this process reads, when asked to with
/// <c>LOADOUT_READ_TRACE=1</c>, so a duplicate read is a number rather than a
/// guess.
/// </summary>
/// <remarks>
/// <para>
/// Each read site wraps its read in <see cref="Begin"/>. Off, that is one field
/// read and a null, so the calls can ship and stay where they are.
/// </para>
/// <para>
/// A read records the file's length and last-write time as they were when it
/// began, which is what tells a second read of a changed file from a second
/// read of the same one. A source read more often than it changes is the thing
/// being looked for, and <see cref="ReadSummary.ReadsPerChange"/> is that
/// figure directly.
/// </para>
/// <para>
/// A site that is not wrapped is not counted, so the summary is a floor on
/// what this process reads and never a ceiling.
/// </para>
/// </remarks>
public static class ReadLedger
{
    /// <summary>The variable that turns counting on.</summary>
    public const string Variable = "LOADOUT_READ_TRACE";

    /// <summary>How often a long-lived process writes its summary out.</summary>
    /// <remarks>
    /// The daemon is ended more often than it exits, and an exit handler never
    /// runs for a process that is killed; a summary a minute old is still a
    /// summary.
    /// </remarks>
    internal static readonly TimeSpan FlushEvery = TimeSpan.FromMinutes(1);

    private static Ledger? _current;
    private static Timer? _flushing;

    /// <summary>Whether this process is counting.</summary>
    public static bool IsOn => Volatile.Read(ref _current) is not null;

    /// <summary>Where summaries are written, under the launcher's state directory.</summary>
    public static string Directory(string state) => Path.Combine(state, "read-trace");

    /// <summary>
    /// Starts counting when <paramref name="asked"/> is "1", and arranges for
    /// the summary to be written as the process ends and every minute before.
    /// </summary>
    /// <param name="asked">The value of <see cref="Variable"/>.</param>
    /// <param name="state">The launcher's state directory.</param>
    /// <param name="label">What this process is, for the summary's name: the command it runs.</param>
    /// <param name="home">The user's home directory, shortened to <c>~</c> in the summary.</param>
    public static void StartIfAsked(string? asked, string state, string label, string? home = null)
    {
        if (asked != "1" || IsOn)
        {
            return;
        }

        var ledger = new Ledger(state, home, label, Environment.ProcessId, DateTimeOffset.UtcNow);

        if (Interlocked.CompareExchange(ref _current, ledger, null) is not null)
        {
            return;
        }

        _flushing = new Timer(_ => Flush(), null, FlushEvery, FlushEvery);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }

    /// <summary>Writes the summary out now, if counting.</summary>
    public static void Flush() => Volatile.Read(ref _current)?.Write();

    /// <summary>
    /// Starts counting one read of <paramref name="path"/>, or returns null
    /// when this process is not counting.
    /// </summary>
    /// <remarks>
    /// Used as <c>using var trace = ReadLedger.Begin(path);</c> around the
    /// read. The bytes counted are the file's length unless the site says
    /// otherwise through <see cref="ReadTrace.Read"/>, which a site reading
    /// only a tail should.
    /// </remarks>
    public static ReadTrace? Begin(
        string path,
        [CallerFilePath] string site = "",
        [CallerMemberName] string member = "") =>
        Volatile.Read(ref _current) is { } ledger ? new ReadTrace(ledger, path, Caller(site, member)) : null;

    /// <summary>Counts into <paramref name="ledger"/> instead of this process's own, for tests.</summary>
    internal static ReadTrace BeginInto(Ledger ledger, string path, string caller) => new(ledger, path, caller);

    /// <summary>The caller's file and member: <c>RunJournal.Read</c>.</summary>
    internal static string Caller(string site, string member)
    {
        // The path the compiler records is the build machine's, with either
        // separator, so the name is taken by hand rather than by Path.
        var cut = Math.Max(site.LastIndexOf('/'), site.LastIndexOf('\\'));
        var name = site[(cut + 1)..];

        if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^3];
        }

        return name.Length == 0 ? member : $"{name}.{member}";
    }

    /// <summary>
    /// The same path for every run, transcript or day: each segment holding a
    /// digit becomes <c>*</c>, keeping its extension.
    /// </summary>
    /// <remarks>
    /// Run ids, session ids and dates all hold digits, and the names of the
    /// things that hold them do not, so this groups the journals of a hundred
    /// runs as one source without a list of which directories are which.
    /// </remarks>
    internal static string Pattern(string shown)
    {
        var parts = shown.Split('/');

        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Any(char.IsAsciiDigit) && parts[i] != "<State>")
            {
                var extension = Path.GetExtension(parts[i]);
                parts[i] = extension.Length > 0 && !extension.Any(char.IsAsciiDigit) ? "*" + extension : "*";
            }
        }

        return string.Join('/', parts);
    }

    /// <summary>
    /// Folds patterns that differ in one directory name into one, with that
    /// name as <c>*</c>: what each pattern becomes.
    /// </summary>
    /// <remarks>
    /// A project's directory is named by its slug, which need hold no digit, so
    /// <see cref="Pattern"/> alone leaves every project's <c>tasks.yaml</c> as a
    /// source of its own. Folded from the deepest directory up, and never the
    /// file name, so two different files in one directory stay apart. It can
    /// fold two unrelated files that happen to share a name a level down; the
    /// files list beside the sources is always exact.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> Fold(IEnumerable<string> patterns)
    {
        var folded = patterns.Distinct(StringComparer.Ordinal).ToDictionary(one => one, one => one, StringComparer.Ordinal);

        if (folded.Count == 0)
        {
            return folded;
        }

        var deepest = folded.Keys.Max(one => one.Split('/').Length);

        for (var depth = deepest - 2; depth >= 0; depth--)
        {
            var groups = folded.Values
                .Distinct(StringComparer.Ordinal)
                .Select(one => (Pattern: one, Parts: one.Split('/')))
                .Where(one => one.Parts.Length > depth + 1 && one.Parts[depth] is not ("*" or "<State>" or "~" or ""))
                .GroupBy(one => string.Join('/', one.Parts.Select((part, at) => at == depth ? "*" : part)), StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .ToList();

            foreach (var group in groups)
            {
                var members = group.Select(one => one.Pattern).ToHashSet(StringComparer.Ordinal);

                foreach (var key in folded.Keys.Where(key => members.Contains(folded[key])).ToList())
                {
                    folded[key] = group.Key;
                }
            }
        }

        return folded;
    }

    /// <summary>One process's counts.</summary>
    internal sealed class Ledger
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        private readonly object _gate = new();
        private readonly Dictionary<string, Source> _sources = new(StringComparer.Ordinal);
        private readonly string _state;
        private readonly string? _home;
        private readonly string _label;
        private readonly int _process;
        private readonly DateTimeOffset _started;

        internal Ledger(string state, string? home, string label, int process, DateTimeOffset started)
        {
            _state = Path.GetFullPath(state);
            _home = home is null ? null : Path.GetFullPath(home);
            _label = label;
            _process = process;
            _started = started;
        }

        /// <summary>Where this process's summary is written.</summary>
        internal string Destination =>
            Path.Combine(ReadLedger.Directory(_state), $"{_started:yyyyMMdd-HHmmss}-{_process}-{Safe(_label)}.json");

        internal void Record(string path, string caller, long length, long writtenTicks, long bytes, TimeSpan took)
        {
            lock (_gate)
            {
                if (!_sources.TryGetValue(path, out var source))
                {
                    source = new Source();
                    _sources[path] = source;
                }

                source.Reads++;
                source.Bytes += bytes;
                source.Took += took;
                source.Versions.Add((length, writtenTicks));
                source.Callers[caller] = source.Callers.GetValueOrDefault(caller) + 1;
            }
        }

        internal LedgerSummary Summarise()
        {
            List<ReadSummary> files;

            lock (_gate)
            {
                files = _sources
                    .Select(pair => new ReadSummary(
                        Shown(pair.Key),
                        Pattern(Shown(pair.Key)),
                        1,
                        pair.Value.Reads,
                        pair.Value.Versions.Count,
                        pair.Value.Bytes,
                        Math.Round(pair.Value.Took.TotalMilliseconds, 2),
                        pair.Value.Callers.OrderByDescending(one => one.Value).ThenBy(one => one.Key, StringComparer.Ordinal)
                            .Select(one => new CallerCount(one.Key, one.Value)).ToList()))
                    .ToList();
            }

            var fold = Fold(files.Select(file => file.Pattern));

            files = files.Select(file => file with { Pattern = fold[file.Pattern] }).ToList();

            var patterns = files
                .GroupBy(file => file.Pattern, StringComparer.Ordinal)
                .Select(group => new ReadSummary(
                    group.Key,
                    group.Key,
                    group.Count(),
                    group.Sum(file => file.Reads),
                    group.Sum(file => file.Versions),
                    group.Sum(file => file.Bytes),
                    Math.Round(group.Sum(file => file.Milliseconds), 2),
                    group.SelectMany(file => file.Callers)
                        .GroupBy(one => one.Caller, StringComparer.Ordinal)
                        .Select(one => new CallerCount(one.Key, one.Sum(each => each.Reads)))
                        .OrderByDescending(one => one.Reads).ThenBy(one => one.Caller, StringComparer.Ordinal)
                        .ToList()))
                .OrderByDescending(pattern => pattern.ReadsPerChange)
                .ThenByDescending(pattern => pattern.Reads)
                .ThenBy(pattern => pattern.Pattern, StringComparer.Ordinal)
                .ToList();

            return new LedgerSummary(
                _label,
                _process,
                _started,
                DateTimeOffset.UtcNow,
                patterns,
                files.OrderBy(file => file.Path, StringComparer.Ordinal).ToList());
        }

        /// <summary>Writes the summary, replacing the last one; a failure is dropped, since tracing must not break what it watches.</summary>
        internal void Write()
        {
            var destination = Destination;
            var temporary = destination + ".tmp";

            try
            {
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllText(temporary, JsonSerializer.Serialize(Summarise(), Json));
                File.Move(temporary, destination, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private string Shown(string path)
        {
            var shown = path;

            if (shown.StartsWith(_state, StringComparison.OrdinalIgnoreCase))
            {
                shown = "<State>" + shown[_state.Length..];
            }
            else if (_home is not null && shown.StartsWith(_home, StringComparison.OrdinalIgnoreCase))
            {
                shown = "~" + shown[_home.Length..];
            }

            return shown.Replace('\\', '/');
        }

        private static string Safe(string label) =>
            new(label.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-').ToArray());

        private sealed class Source
        {
            internal int Reads;
            internal long Bytes;
            internal TimeSpan Took;
            internal readonly HashSet<(long Length, long Written)> Versions = [];
            internal readonly Dictionary<string, int> Callers = new(StringComparer.Ordinal);
        }
    }
}

/// <summary>One read being counted. Disposing it records the read.</summary>
public sealed class ReadTrace : IDisposable
{
    private readonly ReadLedger.Ledger _ledger;
    private readonly string _path;
    private readonly string _caller;
    private readonly long _length;
    private readonly long _written;
    private readonly long _began;
    private long? _bytes;
    private bool _done;

    internal ReadTrace(ReadLedger.Ledger ledger, string path, string caller)
    {
        _ledger = ledger;
        _caller = caller;

        try
        {
            var file = new FileInfo(path);
            _path = file.FullName;

            // A file that is not there is not read, so a site can wrap its
            // check for one without counting the checks.
            _done = !file.Exists;

            if (file.Exists)
            {
                _length = file.Length;
                _written = file.LastWriteTimeUtc.Ticks;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _path = path;
            _done = true;
        }

        _began = Stopwatch.GetTimestamp();
    }

    /// <summary>Says how many bytes were read, for a site that read less than the whole file.</summary>
    public void Read(long bytes) => _bytes = bytes;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _ledger.Record(_path, _caller, _length, _written, _bytes ?? _length, Stopwatch.GetElapsedTime(_began));
    }
}

/// <summary>What one process read, worst offender first.</summary>
/// <param name="Process">The command the process ran.</param>
/// <param name="Id">Its process id.</param>
/// <param name="Started">When counting began.</param>
/// <param name="Written">When this summary was made.</param>
/// <param name="Sources">Files grouped by <see cref="ReadLedger.Pattern"/>, most reads per change first.</param>
/// <param name="Files">Each file on its own.</param>
public sealed record LedgerSummary(
    string Process,
    int Id,
    DateTimeOffset Started,
    DateTimeOffset Written,
    IReadOnlyList<ReadSummary> Sources,
    IReadOnlyList<ReadSummary> Files);

/// <summary>The reads of one file, or of every file sharing a pattern.</summary>
/// <param name="Path">The file, with the state directory as <c>&lt;State&gt;</c> and home as <c>~</c>; the pattern itself for a group.</param>
/// <param name="Pattern">The path with each segment holding a digit as <c>*</c>.</param>
/// <param name="FileCount">How many files this covers.</param>
/// <param name="Reads">How many times they were read.</param>
/// <param name="Versions">How many distinct length and write-time pairs those reads saw, summed over the files.</param>
/// <param name="Bytes">Bytes read.</param>
/// <param name="Milliseconds">Time spent in the reads.</param>
/// <param name="Callers">Who read them, most reads first.</param>
public sealed record ReadSummary(
    string Path,
    string Pattern,
    int FileCount,
    int Reads,
    int Versions,
    long Bytes,
    double Milliseconds,
    IReadOnlyList<CallerCount> Callers)
{
    /// <summary>Reads for each version seen: above one, the same contents were read again.</summary>
    public double ReadsPerChange => Versions == 0 ? Reads : Math.Round((double)Reads / Versions, 2);
}

/// <summary>How many reads one caller made.</summary>
public sealed record CallerCount(string Caller, int Reads);
