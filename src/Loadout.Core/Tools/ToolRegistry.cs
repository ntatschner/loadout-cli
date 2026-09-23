using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Core.Security;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Models.Tools;
using Loadout.Platform.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Loadout.Core.Tools;

/// <summary>A tool as somebody asking about it sees it.</summary>
/// <param name="Record">Its head.</param>
/// <param name="Active">The active version's manifest, or null where there is none or it no longer matches.</param>
/// <param name="Versions">Every known-good version and its standing: known-good, tampered or missing.</param>
public sealed record ToolShown(
    ToolRecord Record,
    ToolVersion? Active,
    IReadOnlyDictionary<string, string> Versions);

/// <summary>A submission to the catalogue's inbox.</summary>
/// <param name="Kind">candidate, idea, bug or lesson.</param>
/// <param name="Text">What it says.</param>
/// <param name="Tool">The tool it is about, or null.</param>
/// <param name="By">Who sent it.</param>
/// <param name="Run">The run it came from, where there was one.</param>
/// <param name="Script">A candidate's script, where there is one, for the overlap check.</param>
/// <param name="Capabilities">A candidate's search terms.</param>
/// <param name="Summary">A candidate's one-line summary.</param>
public sealed record ToolSubmission(
    string Kind,
    string Text,
    string? Tool = null,
    string? By = null,
    string? Run = null,
    string? Script = null,
    IReadOnlyList<string>? Capabilities = null,
    string? Summary = null);

/// <summary>What a submission became.</summary>
/// <param name="Id">The inbox item.</param>
/// <param name="Overlapping">Active tools it overlaps, which it should extend rather than duplicate.</param>
public sealed record ToolSubmitted(string Id, IReadOnlyList<string> Overlapping);

/// <summary>What a verify came to.</summary>
/// <param name="Ruling">Whether the harness was allowed to run.</param>
/// <param name="Because">Why, in a sentence.</param>
/// <param name="Gate">What the gate found, or null where it did not run.</param>
public sealed record ToolVerification(RemedyRuling Ruling, string Because, ToolGateResult? Gate);

/// <summary>Who is promoting, and why the version exists.</summary>
/// <param name="Because">lesson, requirement, bug, idea, nomination or consolidation.</param>
/// <param name="Source">The submission or run it came from.</param>
/// <param name="Actor">Who asked.</param>
/// <param name="Run">The run, where there is one.</param>
/// <param name="Owner">The owning team, for a new tool.</param>
/// <param name="Kind">The kind, for a new tool.</param>
/// <param name="Summary">The summary, for a new tool or to replace it.</param>
/// <param name="Capabilities">The search terms, for a new tool or to replace them.</param>
public sealed record ToolPromotionRequest(
    string Because,
    string Source,
    string? Actor = null,
    string? Run = null,
    string? Owner = null,
    string? Kind = null,
    string? Summary = null,
    IReadOnlyList<string>? Capabilities = null);

/// <summary>The machine's shared catalogue of tools.</summary>
public interface IToolRegistry
{
    /// <summary>Where the catalogue is kept.</summary>
    string Root();

    /// <summary>Tools matching any of the words, best first. Deprecated and retired only with <paramref name="all" />.</summary>
    IReadOnlyList<ToolRecord> Search(string words, bool all = false);

    /// <summary>One tool, its active version and the standing of each version.</summary>
    OperationResult<ToolShown> Show(string name);

    /// <summary>Puts a candidate, idea, bug or lesson in the inbox, after the screens.</summary>
    OperationResult<ToolSubmitted> Submit(ToolSubmission submission);

    /// <summary>
    /// Files the nominator's candidate: screened for secrets and audited, but
    /// not for genericity, because a nomination quotes where it was seen.
    /// Not on the command line or MCP; only <see cref="ToolNominator" /> calls it.
    /// </summary>
    /// <param name="submission">What was found.</param>
    /// <param name="key">What makes it this nomination, recorded so it is filed once.</param>
    OperationResult<ToolSubmitted> Nominate(ToolSubmission submission, string key);

    /// <summary>Records one use.</summary>
    OperationResult RecordUsage(ToolUsage usage);

    /// <summary>The audit log, oldest first.</summary>
    IReadOnlyList<ToolAuditEntry> Audit(string? tool = null, DateTimeOffset? since = null);

    /// <summary>
    /// Whether a draft can be read at all: under drafts/, with a manifest and
    /// the script it names. What a dry run of verify or promote checks, so a
    /// preview refuses what the real run would.
    /// </summary>
    OperationResult CheckDraft(string draft);

    /// <summary>Runs a draft's harness and the regression gate, where this machine allows it.</summary>
    Task<OperationResult<ToolVerification>> VerifyAsync(
        string draft,
        ToolTestConsent consent,
        CancellationToken ct = default);

    /// <summary>Writes a verified draft into its version directory, once, and makes it active.</summary>
    OperationResult<ToolVersion> Promote(string draft, ToolPromotionRequest request);

    /// <summary>Moves the active version to another known-good one.</summary>
    OperationResult SetActive(string name, string version);

    /// <summary>Deprecates a tool, with a replacement or a reason.</summary>
    OperationResult Deprecate(string name, string? replacement, string? reason);

    /// <summary>Retires a deprecated tool nobody has used lately.</summary>
    OperationResult Retire(string name);

    /// <summary>Whether a promoted version's files still match what was promoted.</summary>
    string Standing(string name, string version);

    /// <summary>Records that a refinement was considered and not worth making.</summary>
    OperationResult StandDown(string name, string reason, string? actor = null, string? run = null);

    /// <summary>Whether a tool is worth another look.</summary>
    bool NeedsRefining(string name);

    /// <summary>
    /// Every active tool whose active version is known-good and still matches
    /// what was promoted, with its script as it is now.
    /// </summary>
    IReadOnlyList<ToolOffered> Offerable();

    /// <summary>
    /// A promoted version's script, or null where it is missing or no longer
    /// matches what was promoted.
    /// </summary>
    string? ScriptOf(string name, string version);

    /// <summary>Records in the audit log that a person trusted a version, or took it back.</summary>
    void RecordTrust(string name, string version, bool revoked, string by);
}

/// <summary>An active tool a remediator could be offered.</summary>
/// <param name="Record">Its head.</param>
/// <param name="Version">The active version's manifest.</param>
/// <param name="Script">The script's text, as it is on disk now.</param>
public sealed record ToolOffered(ToolRecord Record, ToolVersion Version, string Script);

/// <summary>
/// The machine's shared catalogue of tools, in files under <see cref="Root" />.
/// </summary>
/// <remarks>
/// <para>
/// Files, like a team's remedies, because what is kept is a script somebody
/// may want to open and read. The rules that matter are enforced here rather
/// than trusted to whoever writes the files: a version directory is written
/// once, only a known-good version whose files still match can be active, and
/// a failed refinement cannot move what is active.
/// </para>
/// <para>
/// Nothing here decides whether anything runs. That is this machine's
/// configuration and a person's, as for remedies.
/// </para>
/// <para>
/// Precondition: a node may write only <c>Root()/drafts</c> and
/// <c>Root()/inbox</c>. The verify records, the audit log, the heads and the
/// version directories are safe only while that holds; a node given the whole
/// root could forge any of them.
/// </para>
/// </remarks>
public sealed partial class ToolRegistry : IToolRegistry
{
    /// <summary>How long a deprecated tool has to go unused before it can be retired.</summary>
    public static readonly TimeSpan RetireAfter = TimeSpan.FromDays(30);

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal) { "candidate", "idea", "bug", "lesson" };

    /// <summary>
    /// The directories the catalogue keeps beside its tools, which a tool of the
    /// same name would share: a tool called <c>drafts</c> would put its versions
    /// where agents can write.
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "drafts", "inbox", "verified", "versions" };

    private readonly IPlatformPaths _paths;
    private readonly ToolHarness _harness;
    private readonly TimeProvider _clock;
    private readonly Func<IEnumerable<string>> _known;

    /// <param name="paths">Where this machine keeps its state.</param>
    /// <param name="harness">Runs cases.</param>
    /// <param name="clock">What time it is.</param>
    /// <param name="known">
    /// Project slugs and team names a tool must not carry. By default, the
    /// team directories and the workspace's projects on this machine.
    /// </param>
    public ToolRegistry(
        IPlatformPaths paths,
        ToolHarness harness,
        TimeProvider clock,
        Func<IEnumerable<string>>? known = null)
    {
        _paths = paths;
        _harness = harness;
        _clock = clock;
        _known = known ?? KnownOnThisMachine;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The one place the location is decided. Whether the catalogue belongs to
    /// the machine or to the workspace is not settled yet, and moving it should
    /// be a change to this line and nothing else.
    /// </remarks>
    public string Root() => Path.Combine(_paths.Paths.State, "tools");

    /// <summary>How long a writer waits for <c>registry.lock</c> before giving up.</summary>
    internal TimeSpan LockWait { get; set; } = TimeSpan.FromSeconds(10);

    private string AuditFile => Path.Combine(Root(), "audit.jsonl");

    private string LockFile => Path.Combine(Root(), "registry.lock");

    private string DraftsRoot => Path.Combine(Root(), "drafts");

    private string VerifiedFile(string name, string version) =>
        Path.Combine(Root(), "verified", RemedyBook.Slug(name), version + ".json");

    private const string Busy =
        "Something else is writing the tool catalogue and holds registry.lock. Try again when it has finished.";

    private string ToolDirectory(string name) => Path.Combine(Root(), RemedyBook.Slug(name));

    private string HeadFile(string name) => Path.Combine(ToolDirectory(name), "tool.yaml");

    private string UsageFile(string name) => Path.Combine(ToolDirectory(name), "usage.jsonl");

    private string VersionDirectory(string name, string version) =>
        Path.Combine(ToolDirectory(name), "versions", version);

    /// <inheritdoc />
    public IReadOnlyList<ToolRecord> Search(string words, bool all = false)
    {
        var terms = Word().Matches(words ?? string.Empty).Select(one => one.Value.ToLowerInvariant()).ToList();

        return
        [
            .. Heads()
                .Where(one => all || one.Lifecycle is not (ToolLifecycle.Deprecated or ToolLifecycle.Retired))
                .Select(one => (Record: one, Score: Score(one, terms)))
                .Where(one => terms.Count == 0 || one.Score > 0)
                .OrderByDescending(one => one.Score)
                .ThenBy(one => one.Record.Name, StringComparer.Ordinal)
                .Select(one => one.Record),
        ];
    }

    /// <inheritdoc />
    public OperationResult<ToolShown> Show(string name)
    {
        if (ReadHead(name) is not { } head)
        {
            return OperationResult<ToolShown>.Fail($"There is no tool called '{name}'.", ExitCode.ProjectNotFound);
        }

        var versions = head.KnownGood.ToDictionary(one => one, one => Standing(name, one), StringComparer.Ordinal);

        return OperationResult<ToolShown>.Ok(new ToolShown(head, ActiveVersion(head), versions));
    }

    /// <inheritdoc />
    public OperationResult<ToolSubmitted> Submit(ToolSubmission submission) =>
        Put(submission, generic: true, key: null);

    /// <inheritdoc />
    public OperationResult<ToolSubmitted> Nominate(ToolSubmission submission, string key)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return Put(submission with { Kind = "candidate", By = "nominator" }, generic: false, key);
    }

    private OperationResult<ToolSubmitted> Put(ToolSubmission submission, bool generic, string? key)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var kind = (submission.Kind ?? string.Empty).Trim().ToLowerInvariant();

        if (!Kinds.Contains(kind))
        {
            return OperationResult<ToolSubmitted>.Fail(
                $"'{submission.Kind}' is not something the inbox takes: candidate, idea, bug or lesson.",
                ExitCode.InvalidArguments);
        }

        var everything = submission.Text + "\n" + submission.Summary + "\n" + submission.Script;

        // By type and never by value, for the reason the scanner gives.
        if (SecretScanner.Match(everything) is { Count: > 0 } secrets)
        {
            return OperationResult<ToolSubmitted>.Fail(
                "That looks like it contains a credential (" + string.Join(", ", secrets) + "), so it was not written.",
                ExitCode.PolicyViolation);
        }

        if (generic && kind == "candidate" && ToolGenericity.Check(everything, _known()) is { Count: > 0 } specific)
        {
            return OperationResult<ToolSubmitted>.Fail(
                "A candidate has to work for any project, and this carries one: "
                + string.Join("; ", specific) + ". Make each of them an input.",
                ExitCode.PolicyViolation);
        }

        var overlapping = new List<string>();

        if (submission.Script is { Length: > 0 } script)
        {
            var shape = new ToolShape(submission.Capabilities ?? [], submission.Summary ?? submission.Text, script);

            foreach (var (record, active) in ActiveShapes())
            {
                if (ToolOverlap.Score(shape, active).Overlaps)
                {
                    overlapping.Add(record.Name);
                }
            }
        }

        var now = _clock.GetUtcNow();
        var id = $"{now:yyyy-MM-dd}-{Guid.NewGuid():N}"[..19];
        var item = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["kind"] = kind,
            ["tool"] = submission.Tool,
            ["by"] = submission.By,
            ["run"] = submission.Run,
            ["at"] = now,
            ["text"] = submission.Text,
            ["summary"] = submission.Summary,
            ["capabilities"] = submission.Capabilities,
            ["script"] = submission.Script,
            ["extends"] = overlapping.Count > 0 ? overlapping : null,
        };

        try
        {
            var inbox = Path.Combine(Root(), "inbox");
            Directory.CreateDirectory(inbox);
            File.WriteAllText(Path.Combine(inbox, id + ".yaml"), Writer.Serialize(item));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<ToolSubmitted>.Fail($"That submission could not be written: {ex.Message}");
        }

        Record(
            key is null ? "submit" : "nominate",
            submission.Tool ?? string.Empty,
            null,
            submission.By,
            submission.Run,
            key is null ? $"{kind} {id}" : $"{kind} {id} key {key}");

        return OperationResult<ToolSubmitted>.Ok(new ToolSubmitted(id, overlapping));
    }

    /// <inheritdoc />
    public OperationResult RecordUsage(ToolUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        using var held = Lock();

        if (held is null)
        {
            return OperationResult.Fail(Busy);
        }

        if (ReadHead(usage.Tool) is not { } head)
        {
            return OperationResult.Fail($"There is no tool called '{usage.Tool}'.", ExitCode.ProjectNotFound);
        }

        if (!ToolOutcome.All.Contains(usage.Outcome))
        {
            return OperationResult.Fail(
                $"'{usage.Outcome}' is not an outcome: ok, failed or workaround.", ExitCode.InvalidArguments);
        }

        if (SecretScanner.Match(usage.Note) is { Count: > 0 } secrets)
        {
            return OperationResult.Fail(
                "That note looks like it contains a credential (" + string.Join(", ", secrets) + ").",
                ExitCode.PolicyViolation);
        }

        usage.At ??= _clock.GetUtcNow();

        try
        {
            Directory.CreateDirectory(ToolDirectory(head.Name));
            File.AppendAllText(UsageFile(head.Name), JsonSerializer.Serialize(usage, Json) + "\n");

            var all = Usage(head.Name);
            head.UsageSummary = new ToolUsageSummary
            {
                Runs = all.Count,
                Ok = all.Count(one => one.Outcome == ToolOutcome.Ok),
                Failed = all.Count(one => one.Outcome == ToolOutcome.Failed),
                Workaround = all.Count(one => one.Outcome == ToolOutcome.Workaround),
                Teams = all.Select(one => one.Team).Where(one => one.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            };

            WriteHead(head);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"That use could not be recorded: {ex.Message}");
        }

        Record("used", head.Name, usage.Version, usage.Team, usage.Run, usage.Outcome);

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolAuditEntry> Audit(string? tool = null, DateTimeOffset? since = null) =>
    [
        .. ToolAudit.Read(AuditFile).Where(one =>
            (tool is null || string.Equals(one.Tool, tool, StringComparison.OrdinalIgnoreCase))
            && (since is null || one.At >= since)),
    ];

    /// <inheritdoc />
    public OperationResult CheckDraft(string draft) =>
        ReadDraft(draft) is { Failed: true } read
            ? OperationResult.Fail(read.Error!, ExitCode.InvalidArguments)
            : OperationResult.Ok();

    /// <inheritdoc />
    public async Task<OperationResult<ToolVerification>> VerifyAsync(
        string draft,
        ToolTestConsent consent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consent);

        var read = ReadDraft(draft);

        if (read.Failed)
        {
            return OperationResult<ToolVerification>.Fail(read.Error!, ExitCode.InvalidArguments);
        }

        var (directory, version, script, scriptPath, cases) = read.Value!;

        // Checked here as well as at promotion, because the version names the
        // file the verify record is written to.
        if (!string.Equals(version.Name, RemedyBook.Slug(version.Name), StringComparison.Ordinal)
            || Reserved.Contains(version.Name)
            || !VersionShape().IsMatch(version.Version))
        {
            return OperationResult<ToolVerification>.Fail(
                $"'{version.Name}@{version.Version}' is not a tool name and version: lowercase and hyphens, then major.minor.",
                ExitCode.InvalidArguments);
        }

        if (ToolHarness.MissingClasses(cases) is { Count: > 0 } missing)
        {
            return OperationResult<ToolVerification>.Fail(
                "A version needs a case of every class, and this has none of: " + string.Join(", ", missing) + ".",
                ExitCode.InvalidArguments);
        }

        var may = ToolHarness.May(version, script, cases, consent);

        if (may.Ruling != RemedyRuling.Run)
        {
            Record("verify", version.Name, version.Version, null, null, $"held: {may.Because}");

            return OperationResult<ToolVerification>.Ok(new ToolVerification(may.Ruling, may.Because, null));
        }

        ToolVersion? active = null;
        IReadOnlyList<ToolCase> activeCases = [];

        if (ReadHead(version.Name) is { } head && ActiveVersion(head) is { } known)
        {
            active = known;
            activeCases = ReadCases(Path.Combine(VersionDirectory(head.Name, known.Version), "cases"));
        }

        var gate = await ToolPromotion.GateAsync(
            version,
            scriptPath,
            cases,
            active,
            activeCases,
            (path, one, token) => _harness.RunAsync(path, one, token),
            ct).ConfigureAwait(false);

        version.Status = gate.Passed ? ToolVersionStatus.Verified : ToolVersionStatus.Rejected;
        version.Tests = new ToolTestRecord
        {
            RanAt = _clock.GetUtcNow(),
            Passed = gate.Own.Count(one => one.Passed) + gate.Regression.Count(one => one.Passed),
            Failed = gate.Own.Count(one => !one.Passed) + gate.Regression.Count(one => !one.Passed),
            RegressionAgainst = active is null ? [] : [active.Version],
            Fingerprint = RemedyCeiling.Fingerprint(script),
            CasesFingerprint = CasesFingerprint(Path.Combine(directory, "cases")),
        };

        using (var held = Lock())
        {
            if (held is null)
            {
                return OperationResult<ToolVerification>.Fail(Busy);
            }

            // The draft's manifest is for whoever reads the draft. What
            // promotion believes is the record under verified/, outside the
            // drafts, because anything that can write a draft can write
            // "status: verified" into it.
            var proof = VerifiedFile(version.Name, version.Version);

            try
            {
                if (gate.Passed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(proof)!);
                    File.WriteAllText(proof, JsonSerializer.Serialize(
                        new VerifyRecord(version.Tests.Fingerprint, version.Tests.CasesFingerprint, version.Tests.RanAt),
                        Json));
                }
                else if (File.Exists(proof))
                {
                    File.Delete(proof);
                }

                File.WriteAllText(Path.Combine(directory, "manifest.yaml"), Writer.Serialize(version));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return OperationResult<ToolVerification>.Fail($"What verify found could not be written: {ex.Message}");
            }
        }

        Record(gate.Passed ? "verify" : "reject", version.Name, version.Version, null, null, gate.Because);

        return OperationResult<ToolVerification>.Ok(new ToolVerification(RemedyRuling.Run, gate.Because, gate));
    }

    /// <inheritdoc />
    public OperationResult<ToolVersion> Promote(string draft, ToolPromotionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Held from the first read to the last write, so two promotions of one
        // tool cannot both read the head and each write back only their own
        // version.
        using var held = Lock();

        return held is null ? OperationResult<ToolVersion>.Fail(Busy) : PromoteHeld(draft, request);
    }

    private OperationResult<ToolVersion> PromoteHeld(string draft, ToolPromotionRequest request)
    {
        var read = ReadDraft(draft);

        if (read.Failed)
        {
            return OperationResult<ToolVersion>.Fail(read.Error!, ExitCode.InvalidArguments);
        }

        var (directory, version, script, scriptPath, cases) = read.Value!;

        OperationResult<ToolVersion> Refuse(string why) =>
            OperationResult<ToolVersion>.Fail(why, ExitCode.PolicyViolation);

        if (!string.Equals(version.Name, RemedyBook.Slug(version.Name), StringComparison.Ordinal))
        {
            return Refuse($"'{version.Name}' is not a tool name: lowercase and hyphens only.");
        }

        if (Reserved.Contains(version.Name))
        {
            return Refuse($"'{version.Name}' is where the catalogue keeps its own files, so no tool can have it.");
        }

        if (!VersionShape().IsMatch(version.Version))
        {
            return Refuse($"'{version.Version}' is not a version: major.minor.");
        }

        // Written once. A version that has been promoted is what somebody
        // trusted, and writing over it would spend that trust on something else.
        if (Directory.Exists(VersionDirectory(version.Name, version.Version)))
        {
            return Refuse($"{version.Name}@{version.Version} has already been promoted, and a version is written once.");
        }

        if (ToolPromotion.Missing(version) is { Count: > 0 } missing)
        {
            return Refuse("The manifest is missing " + string.Join(", ", missing) + ".");
        }

        // The draft's own status is not evidence: the draft is writable by
        // whoever wrote it. Only the record verify leaves outside it is.
        if (ReadVerified(version.Name, version.Version) is not { } proof)
        {
            return Refuse($"{version.Name}@{version.Version} has not passed verify.");
        }

        var casesFingerprint = CasesFingerprint(Path.Combine(directory, "cases"));

        if (!string.Equals(proof.Script, RemedyCeiling.Fingerprint(script), StringComparison.Ordinal)
            || !string.Equals(proof.Cases, casesFingerprint, StringComparison.Ordinal))
        {
            return Refuse("The script or its cases have changed since they were verified. Verify again.");
        }

        if (ToolGenericity.Check(version, script, cases, _known()) is { Count: > 0 } specific)
        {
            return Refuse("This carries a project: " + string.Join("; ", specific) + ". Make each of them an input.");
        }

        var head = ReadHead(version.Name);

        if (head is not null && ActiveVersion(head) is { } previous
            && ToolCompatibility.Refusal(previous, version) is { } broken)
        {
            return Refuse(broken);
        }

        var shape = new ToolShape(
            request.Capabilities ?? head?.Capabilities ?? [],
            request.Summary ?? head?.Summary ?? version.Purpose,
            script);

        foreach (var (record, active) in ActiveShapes())
        {
            if (!string.Equals(record.Name, version.Name, StringComparison.Ordinal)
                && !version.Compatibility.Replaces.Contains(record.Name, StringComparer.OrdinalIgnoreCase)
                && ToolOverlap.Score(shape, active).Overlaps)
            {
                return Refuse(
                    $"This overlaps '{record.Name}'. Extend it, or name it in compatibility.replaces.");
            }
        }

        var target = VersionDirectory(version.Name, version.Version);
        var file = $"{version.Name}.v{version.Version}.ps1";

        // Written beside the target and moved into place, so a copy that fails
        // partway leaves no half a version where a whole one is expected.
        var staging = Path.Combine(Path.GetDirectoryName(target)!, "." + version.Version + "-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(Path.Combine(staging, "cases"));
            File.Copy(scriptPath, Path.Combine(staging, file));

            foreach (var one in Directory.EnumerateFiles(Path.Combine(directory, "cases"), "*.yaml"))
            {
                File.Copy(one, Path.Combine(staging, "cases", Path.GetFileName(one)));
            }

            version.Status = ToolVersionStatus.KnownGood;
            version.Script = file;
            version.Fingerprint = RemedyCeiling.Fingerprint(script);
            version.CasesFingerprint = casesFingerprint;
            File.WriteAllText(Path.Combine(staging, "manifest.yaml"), Writer.Serialize(version));
            Directory.Move(staging, target);

            head ??= new ToolRecord { Name = version.Name };
            head.Owner = request.Owner ?? head.Owner;
            head.Kind = request.Kind ?? head.Kind;
            head.Summary = request.Summary ?? (head.Summary.Length > 0 ? head.Summary : version.Purpose);
            head.Capabilities = request.Capabilities?.ToList() ?? head.Capabilities;
            head.KnownGood.Add(version.Version);
            head.Active = version.Version;

            if (head.Lifecycle == ToolLifecycle.Candidate)
            {
                head.Lifecycle = ToolLifecycle.Active;
            }

            head.Lineage.Add(new ToolLineage
            {
                Version = version.Version,
                Because = request.Because,
                Source = request.Source,
            });

            WriteHead(head);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // Named with a leading dot and a random suffix, so nothing reads it as a version.
            }

            return OperationResult<ToolVersion>.Fail($"That version could not be written: {ex.Message}");
        }

        // What Standing checks the version's files against, rather than the
        // version's own manifest, which sits beside the files it vouches for.
        Record("promote", version.Name, version.Version, request.Actor, request.Run, Promoted(version.Fingerprint, casesFingerprint));

        return OperationResult<ToolVersion>.Ok(version);
    }

    /// <inheritdoc />
    public OperationResult SetActive(string name, string version)
    {
        using var held = Lock();

        if (held is null)
        {
            return OperationResult.Fail(Busy);
        }

        if (ReadHead(name) is not { } head)
        {
            return OperationResult.Fail($"There is no tool called '{name}'.", ExitCode.ProjectNotFound);
        }

        if (!head.KnownGood.Contains(version, StringComparer.Ordinal))
        {
            return OperationResult.Fail(
                $"{name}@{version} never passed the gate, so it cannot be active.", ExitCode.PolicyViolation);
        }

        if (Standing(name, version) != ToolVersionStatus.KnownGood)
        {
            return OperationResult.Fail(
                $"{name}@{version} no longer matches what was promoted, so it cannot be active.",
                ExitCode.PolicyViolation);
        }

        head.Active = version;
        WriteHead(head);
        Record("activate", name, version, null, null, null);

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public OperationResult Deprecate(string name, string? replacement, string? reason)
    {
        using var held = Lock();

        if (held is null)
        {
            return OperationResult.Fail(Busy);
        }

        if (ReadHead(name) is not { } head)
        {
            return OperationResult.Fail($"There is no tool called '{name}'.", ExitCode.ProjectNotFound);
        }

        if (string.IsNullOrWhiteSpace(replacement) && string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Fail(
                "A tool is deprecated with a replacement or a reason, so whoever used it knows what to do instead.",
                ExitCode.InvalidArguments);
        }

        if (replacement is { Length: > 0 } instead
            && (string.Equals(instead, name, StringComparison.OrdinalIgnoreCase)
                || ReadHead(instead) is not { Lifecycle: not ToolLifecycle.Retired }))
        {
            return OperationResult.Fail(
                $"'{instead}' is not a tool anybody could use instead.", ExitCode.InvalidArguments);
        }

        head.Lifecycle = ToolLifecycle.Deprecated;
        head.Deprecated = new ToolDeprecation
        {
            Replacement = replacement ?? string.Empty,
            Reason = reason ?? string.Empty,
            At = _clock.GetUtcNow(),
        };

        WriteHead(head);
        Record("deprecate", name, head.Active, null, null, replacement is { Length: > 0 } ? "replacement " + replacement : reason);

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public OperationResult Retire(string name)
    {
        using var held = Lock();

        if (held is null)
        {
            return OperationResult.Fail(Busy);
        }

        if (ReadHead(name) is not { } head)
        {
            return OperationResult.Fail($"There is no tool called '{name}'.", ExitCode.ProjectNotFound);
        }

        if (head.Lifecycle != ToolLifecycle.Deprecated)
        {
            return OperationResult.Fail($"'{name}' is retired only once it is deprecated.", ExitCode.PolicyViolation);
        }

        var since = _clock.GetUtcNow() - RetireAfter;

        if (Usage(name).Any(one => one.At >= since))
        {
            return OperationResult.Fail(
                $"'{name}' has been used in the last {RetireAfter.TotalDays:0} days.", ExitCode.PolicyViolation);
        }

        head.Lifecycle = ToolLifecycle.Retired;
        WriteHead(head);
        Record("retire", name, head.Active, null, null, null);

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Recomputed on every read, because the files are on a disk a node with
    /// Bash can write to. A version that no longer matches is refused, never
    /// trusted. What the files are checked against is the fingerprint the
    /// promotion recorded in the audit log, not the version's own manifest:
    /// whoever can rewrite the script can rewrite the manifest beside it.
    /// </remarks>
    public string Standing(string name, string version)
    {
        var directory = VersionDirectory(name, version);
        var manifest = ReadYaml<ToolVersion>(Path.Combine(directory, "manifest.yaml"));

        if (manifest is null || manifest.Script is not { Length: > 0 })
        {
            return "missing";
        }

        var script = Path.Combine(directory, manifest.Script);
        // The first promotion, because a version is written once: the log is
        // only ever appended to, so a later "promote" line for the same
        // version is somebody vouching for files the gate never saw.
        var promoted = Audit(name).FirstOrDefault(one =>
            one.Action == "promote" && string.Equals(one.Version, version, StringComparison.Ordinal));
        var (scriptPrint, casesPrint) = ReadPromoted(promoted?.Note);

        if (scriptPrint is null
            || !File.Exists(script)
            || !string.Equals(RemedyCeiling.Fingerprint(File.ReadAllText(script)), scriptPrint, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(CasesFingerprint(Path.Combine(directory, "cases")), casesPrint, StringComparison.OrdinalIgnoreCase))
        {
            return ToolVersionStatus.Tampered;
        }

        return ToolVersionStatus.KnownGood;
    }

    /// <inheritdoc />
    public OperationResult StandDown(string name, string reason, string? actor = null, string? run = null)
    {
        if (ReadHead(name) is null)
        {
            return OperationResult.Fail($"There is no tool called '{name}'.", ExitCode.ProjectNotFound);
        }

        Record("stand-down", name, null, actor, run, reason);

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Two stand-downs in a row with nothing new between them mean the last two
    /// looks found nothing worth changing, and a third would find the same.
    /// Something new - a promotion, a submission about the tool, a use that
    /// failed or was worked around - starts the count again.
    /// </remarks>
    public bool NeedsRefining(string name)
    {
        if (ReadHead(name) is not { } head || head.Lifecycle == ToolLifecycle.Retired)
        {
            return false;
        }

        var standDowns = 0;

        foreach (var entry in Audit(head.Name).Reverse())
        {
            if (entry.Action == "stand-down")
            {
                standDowns++;
            }
            else if (IsSignal(entry))
            {
                break;
            }
        }

        return standDowns < 2;
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolOffered> Offerable()
    {
        var offered = new List<ToolOffered>();

        foreach (var head in Heads().Where(one => one.Lifecycle == ToolLifecycle.Active))
        {
            if (ActiveVersion(head) is not { } version)
            {
                continue;
            }

            try
            {
                offered.Add(new ToolOffered(
                    head,
                    version,
                    File.ReadAllText(Path.Combine(VersionDirectory(head.Name, version.Version), version.Script))));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A script that cannot be read is not offered: nobody could
                // tell whether it is the one that was agreed to.
            }
        }

        return offered;
    }

    /// <inheritdoc />
    public string? ScriptOf(string name, string version)
    {
        if (Standing(name, version) != ToolVersionStatus.KnownGood)
        {
            return null;
        }

        var directory = VersionDirectory(name, version);

        try
        {
            return ReadYaml<ToolVersion>(Path.Combine(directory, "manifest.yaml")) is { Script: { Length: > 0 } script }
                ? File.ReadAllText(Path.Combine(directory, script))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void RecordTrust(string name, string version, bool revoked, string by) =>
        Record(revoked ? "untrust" : "trust", name, version, by, null, null);

    private static bool IsSignal(ToolAuditEntry entry) =>
        entry.Action is "promote" or "submit"
        || (entry.Action == "used" && entry.Note is ToolOutcome.Failed or ToolOutcome.Workaround);

    private ToolVersion? ActiveVersion(ToolRecord head)
    {
        if (head.Active is not { Length: > 0 } active
            || !head.KnownGood.Contains(active, StringComparer.Ordinal)
            || Standing(head.Name, active) != ToolVersionStatus.KnownGood)
        {
            return null;
        }

        return ReadYaml<ToolVersion>(Path.Combine(VersionDirectory(head.Name, active), "manifest.yaml"));
    }

    private IEnumerable<(ToolRecord Record, ToolShape Shape)> ActiveShapes()
    {
        foreach (var head in Heads().Where(one => one.Lifecycle == ToolLifecycle.Active))
        {
            if (ActiveVersion(head) is { } version)
            {
                var script = Path.Combine(VersionDirectory(head.Name, version.Version), version.Script);

                yield return (head, new ToolShape(head.Capabilities, head.Summary, File.ReadAllText(script)));
            }
        }
    }

    private IEnumerable<ToolRecord> Heads()
    {
        if (!Directory.Exists(Root()))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(Root()).OrderBy(one => one, StringComparer.Ordinal))
        {
            if (ReadYaml<ToolRecord>(Path.Combine(directory, "tool.yaml")) is { } head)
            {
                yield return head;
            }
        }
    }

    private ToolRecord? ReadHead(string name) => ReadYaml<ToolRecord>(HeadFile(name));

    private void WriteHead(ToolRecord head)
    {
        Directory.CreateDirectory(ToolDirectory(head.Name));
        File.WriteAllText(HeadFile(head.Name), Writer.Serialize(head));
    }

    private List<ToolUsage> Usage(string name)
    {
        var file = UsageFile(name);

        if (!File.Exists(file))
        {
            return [];
        }

        var all = new List<ToolUsage>();

        foreach (var line in File.ReadLines(file).Where(one => one.Length > 0))
        {
            try
            {
                if (JsonSerializer.Deserialize<ToolUsage>(line, Json) is { } usage)
                {
                    all.Add(usage);
                }
            }
            catch (JsonException)
            {
                // A damaged line is one lost use, not a lost log.
            }
        }

        return all;
    }

    private void Record(string action, string tool, string? version, string? actor, string? run, string? note) =>
        ToolAudit.Append(AuditFile, new ToolAuditEntry(_clock.GetUtcNow(), action, tool, version, actor, run, note));

    private OperationResult<(string Directory, ToolVersion Version, string Script, string ScriptPath, IReadOnlyList<ToolCase> Cases)> ReadDraft(string draft)
    {
        // Only under drafts/, after the path is resolved, because verify
        // writes into a draft and nothing asked it to write anywhere else.
        var full = Path.GetFullPath(draft);

        if (!full.StartsWith(Path.GetFullPath(DraftsRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<(string, ToolVersion, string, string, IReadOnlyList<ToolCase>)>.Fail(
                $"A draft is read only from {DraftsRoot}, and {draft} is not in it.");
        }

        if (ReadYaml<ToolVersion>(Path.Combine(full, "manifest.yaml")) is not { } version)
        {
            return OperationResult<(string, ToolVersion, string, string, IReadOnlyList<ToolCase>)>.Fail(
                $"There is no readable manifest.yaml in {draft}.");
        }

        var scriptPath = Path.GetFullPath(Path.Combine(full, version.Script ?? string.Empty));

        if (version.Script is not { Length: > 0 }
            || !scriptPath.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(scriptPath))
        {
            return OperationResult<(string, ToolVersion, string, string, IReadOnlyList<ToolCase>)>.Fail(
                $"The manifest names no script beside it in {draft}.");
        }

        return OperationResult<(string, ToolVersion, string, string, IReadOnlyList<ToolCase>)>.Ok(
            (full, version, File.ReadAllText(scriptPath), scriptPath, ReadCases(Path.Combine(full, "cases"))));
    }

    /// <summary>What a passing verify leaves outside the drafts, for promotion to check.</summary>
    private sealed record VerifyRecord(string Script, string Cases, DateTimeOffset? At);

    private VerifyRecord? ReadVerified(string name, string version)
    {
        try
        {
            var file = VerifiedFile(name, version);

            return File.Exists(file) ? JsonSerializer.Deserialize<VerifyRecord>(File.ReadAllText(file), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string Promoted(string script, string cases) => $"script {script} cases {cases}";

    private static (string? Script, string? Cases) ReadPromoted(string? note)
    {
        var parts = (note ?? string.Empty).Split(' ');

        return parts is ["script", var script, "cases", var cases] ? (script, cases) : (null, null);
    }

    /// <summary>
    /// Takes <c>registry.lock</c>, waiting up to <see cref="LockWait" />, or
    /// returns null when something else holds it for longer.
    /// </summary>
    /// <remarks>
    /// Every read-modify-write of a head, and every promotion, holds it. Without
    /// it two writers each read the head, add their own change and write it
    /// back, and the first one's change is gone: twelve promotions at once lost
    /// versions from known_good before this was here.
    /// </remarks>
    private FileStream? Lock()
    {
        Directory.CreateDirectory(Root());
        var deadline = DateTime.UtcNow + LockWait;
        var wait = TimeSpan.FromMilliseconds(10);

        while (true)
        {
            try
            {
                return new FileStream(LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return null;
                }

                Thread.Sleep(wait);
                wait = TimeSpan.FromMilliseconds(Math.Min(wait.TotalMilliseconds * 2, 250));
            }
        }
    }

    private static List<ToolCase> ReadCases(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(directory, "*.yaml")
                .OrderBy(one => one, StringComparer.Ordinal)
                .Select(ReadYaml<ToolCase>)
                .OfType<ToolCase>(),
        ];
    }

    /// <summary>The fingerprint over a directory of cases, in name order.</summary>
    internal static string CasesFingerprint(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return RemedyCeiling.Fingerprint(string.Empty);
        }

        var text = string.Concat(
            Directory.EnumerateFiles(directory, "*.yaml")
                .OrderBy(one => Path.GetFileName(one), StringComparer.Ordinal)
                .Select(one => Path.GetFileName(one) + "\n" + File.ReadAllText(one) + "\n"));

        return RemedyCeiling.Fingerprint(text);
    }

    private static T? ReadYaml<T>(string file)
        where T : class
    {
        try
        {
            return File.Exists(file) ? Yaml.Deserialize<T>(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    private static int Score(ToolRecord record, List<string> terms)
    {
        var haystack = (record.Name + " " + record.Summary + " " + string.Join(' ', record.Capabilities)).ToLowerInvariant();

        return terms.Count(haystack.Contains);
    }

    private IEnumerable<string> KnownOnThisMachine()
    {
        var places = new[]
        {
            Path.Combine(_paths.Paths.State, "teams", "work"),
            Path.Combine(_paths.Paths.WorkspaceClone, "projects"),
        };

        return places.Where(Directory.Exists)
            .SelectMany(one => Directory.EnumerateDirectories(one))
            .Select(one => Path.GetFileName(one));
    }

    [GeneratedRegex(@"[A-Za-z0-9-]+", RegexOptions.None, 1000)]
    private static partial Regex Word();

    [GeneratedRegex(@"^\d+\.\d+$", RegexOptions.None, 1000)]
    private static partial Regex VersionShape();
}
