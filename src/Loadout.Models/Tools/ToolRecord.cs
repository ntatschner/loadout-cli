namespace Loadout.Models.Tools;

/// <summary>
/// One tool in the machine's shared catalogue: the head, kept in
/// <c>tool.yaml</c>, which is the only part of a tool that changes after it is
/// written.
/// </summary>
/// <remarks>
/// <para>
/// Mutable with settable properties because it deserialises from YAML, like a
/// remedy record. What runs is never decided here: the versions under it are
/// written once, and trust lives in this machine's configuration.
/// </para>
/// </remarks>
public sealed class ToolRecord
{
    /// <summary>The name it is searched and asked for by. Lowercase, hyphenated, unique.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The team that maintains it.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>The sort of task, which is what a machine sets a rule against.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>What it does, in a sentence somebody searching would recognise.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Search terms, and half of what overlap is measured on.</summary>
    public List<string> Capabilities { get; set; } = [];

    /// <summary>Where it is in its life. One of <see cref="ToolLifecycle" />.</summary>
    public string Lifecycle { get; set; } = ToolLifecycle.Candidate;

    /// <summary>
    /// The version in use, which is only ever one in <see cref="KnownGood" />
    /// whose files still match what was promoted.
    /// </summary>
    public string? Active { get; set; }

    /// <summary>Every version that passed the gate. Never removed from.</summary>
    public List<string> KnownGood { get; set; } = [];

    /// <summary>Present only when the tool has been deprecated.</summary>
    public ToolDeprecation? Deprecated { get; set; }

    /// <summary>Every version, and what caused it.</summary>
    public List<ToolLineage> Lineage { get; set; } = [];

    /// <summary>Recomputed from the usage log; never edited by hand.</summary>
    public ToolUsageSummary UsageSummary { get; set; } = new();
}

/// <summary>The states a tool passes through.</summary>
public static class ToolLifecycle
{
    /// <summary>Submitted, never promoted.</summary>
    public const string Candidate = "candidate";

    /// <summary>Has a known-good version in use.</summary>
    public const string Active = "active";

    /// <summary>Still usable, with a replacement or a reason not to.</summary>
    public const string Deprecated = "deprecated";

    /// <summary>No longer offered. Nothing on disk is removed.</summary>
    public const string Retired = "retired";
}

/// <summary>Why a tool is deprecated.</summary>
public sealed class ToolDeprecation
{
    /// <summary>The tool to use instead, or empty.</summary>
    public string Replacement { get; set; } = string.Empty;

    /// <summary>Why, where there is no replacement or as well as one.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>When.</summary>
    public DateTimeOffset? At { get; set; }
}

/// <summary>What caused one version.</summary>
public sealed class ToolLineage
{
    /// <summary>The version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>lesson, requirement, bug, idea, nomination or consolidation.</summary>
    public string Because { get; set; } = string.Empty;

    /// <summary>The submission or run it came from.</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>How a tool has fared, counted from its usage log.</summary>
public sealed class ToolUsageSummary
{
    /// <summary>Every recorded use.</summary>
    public int Runs { get; set; }

    /// <summary>Uses that worked.</summary>
    public int Ok { get; set; }

    /// <summary>Uses that failed.</summary>
    public int Failed { get; set; }

    /// <summary>Uses that needed working around.</summary>
    public int Workaround { get; set; }

    /// <summary>Distinct teams, counted rather than named.</summary>
    public int Teams { get; set; }
}
