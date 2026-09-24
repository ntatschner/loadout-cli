namespace Loadout.Models.Tools;

/// <summary>
/// One version of a tool: its manifest, <c>versions/&lt;v&gt;/manifest.yaml</c>,
/// written once when it is promoted and never again.
/// </summary>
/// <remarks>
/// The same shape serves a draft, which is freely rewritten until it is
/// promoted. What makes a version immutable is the store refusing to write
/// its directory twice, not anything on this class.
/// </remarks>
public sealed class ToolVersion
{
    /// <summary>The tool this is a version of.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>major.minor</c>.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>One of <see cref="ToolVersionStatus" />.</summary>
    public string Status { get; set; } = ToolVersionStatus.Draft;

    /// <summary>The script's file name, beside this manifest.</summary>
    public string Script { get; set; } = string.Empty;

    /// <summary>The script's fingerprint, as a remedy's is taken.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>The fingerprint over the cases, in name order.</summary>
    public string CasesFingerprint { get; set; } = string.Empty;

    /// <summary>What it is for, in one sentence naming no project.</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>What it takes.</summary>
    public List<ToolInput> Inputs { get; set; } = [];

    /// <summary>What it gives back.</summary>
    public ToolOutputs Outputs { get; set; } = new();

    /// <summary>What must be present for it to run.</summary>
    public List<string> Dependencies { get; set; } = [];

    /// <summary>What it will never do.</summary>
    public List<string> Constraints { get; set; } = [];

    /// <summary>What it does when it cannot do its job.</summary>
    public string ErrorBehaviour { get; set; } = string.Empty;

    /// <summary>How it is called, and what to expect.</summary>
    public List<ToolExample> Examples { get; set; } = [];

    /// <summary>The problem it was made to solve, described without the project.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>Where it runs, and whether it breaks what came before.</summary>
    public ToolCompatibilityInfo Compatibility { get; set; } = new();

    /// <summary>What the gate found. Written by the gate, not by an agent.</summary>
    public ToolTestRecord? Tests { get; set; }
}

/// <summary>The states a version passes through.</summary>
public static class ToolVersionStatus
{
    /// <summary>Being written.</summary>
    public const string Draft = "draft";

    /// <summary>Its harness and the regression gate passed, against the fingerprints recorded.</summary>
    public const string Verified = "verified";

    /// <summary>Failed the harness or the regression gate. Never promoted.</summary>
    public const string Rejected = "rejected";

    /// <summary>Promoted.</summary>
    public const string KnownGood = "known-good";

    /// <summary>Promoted, but its files no longer match. Shown, never active.</summary>
    public const string Tampered = "tampered";
}

/// <summary>One input a tool takes.</summary>
public sealed class ToolInput
{
    /// <summary>The parameter name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>path, int, string, and so on.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Whether a caller must give it.</summary>
    public bool Required { get; set; }

    /// <summary>What it is when not given, or null for nothing.</summary>
    public string? Default { get; set; }

    /// <summary>What it means.</summary>
    public string Describe { get; set; } = string.Empty;
}

/// <summary>What a tool gives back.</summary>
public sealed class ToolOutputs
{
    /// <summary>What it writes to standard output.</summary>
    public string Stdout { get; set; } = string.Empty;

    /// <summary>Each exit code and what it means.</summary>
    public Dictionary<int, string> Exit { get; set; } = [];
}

/// <summary>One example call.</summary>
public sealed class ToolExample
{
    /// <summary>The command.</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>What it should do.</summary>
    public string Expect { get; set; } = string.Empty;
}

/// <summary>Where a version runs, and what it breaks.</summary>
public sealed class ToolCompatibilityInfo
{
    /// <summary>windows, linux, macos.</summary>
    public List<string> Platforms { get; set; } = [];

    /// <summary>The interpreter it needs.</summary>
    public string Shell { get; set; } = string.Empty;

    /// <summary>Whether it deliberately breaks the previous version's callers.</summary>
    public bool Breaks { get; set; }

    /// <summary>What a caller of the previous version has to change.</summary>
    public string Migration { get; set; } = string.Empty;

    /// <summary>Previous known-good cases this version deliberately no longer passes.</summary>
    public List<string> RetiredCases { get; set; } = [];

    /// <summary>Tools or versions this one consolidates.</summary>
    public List<string> Replaces { get; set; } = [];
}

/// <summary>What the gate found when it last ran.</summary>
public sealed class ToolTestRecord
{
    /// <summary>When.</summary>
    public DateTimeOffset? RanAt { get; set; }

    /// <summary>Cases that passed, the version's own and the regression set.</summary>
    public int Passed { get; set; }

    /// <summary>Cases that failed.</summary>
    public int Failed { get; set; }

    /// <summary>The known-good versions whose cases were rerun.</summary>
    public List<string> RegressionAgainst { get; set; } = [];

    /// <summary>The script's fingerprint the run was made against.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>The cases' fingerprint the run was made against.</summary>
    public string CasesFingerprint { get; set; } = string.Empty;
}
