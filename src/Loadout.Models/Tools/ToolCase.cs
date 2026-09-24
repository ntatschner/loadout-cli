namespace Loadout.Models.Tools;

/// <summary>One harness case, <c>cases/&lt;name&gt;.yaml</c>.</summary>
public sealed class ToolCase
{
    /// <summary>What it is called, which is how a migration names it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="ToolCaseClass" />.</summary>
    public string Class { get; set; } = string.Empty;

    /// <summary>
    /// The tool's inputs by name. Placeholders only for paths: <c>{tmp}</c>
    /// is the case's own fresh directory.
    /// </summary>
    public Dictionary<string, string> Args { get; set; } = [];

    /// <summary>What to create before the run.</summary>
    public ToolCaseSetup Setup { get; set; } = new();

    /// <summary>What must be true afterwards.</summary>
    public ToolCaseExpect Expect { get; set; } = new();
}

/// <summary>The four classes a version must cover.</summary>
public static class ToolCaseClass
{
    /// <summary>It does its job.</summary>
    public const string Success = "success";

    /// <summary>It cannot, and says so.</summary>
    public const string Failure = "failure";

    /// <summary>A boundary: empty, huge, already done.</summary>
    public const string Edge = "edge";

    /// <summary>It is given something it must refuse.</summary>
    public const string InvalidInput = "invalid-input";

    /// <summary>All four, in the order they are documented.</summary>
    public static IReadOnlyList<string> All { get; } = [Success, Failure, Edge, InvalidInput];
}

/// <summary>What a case creates first.</summary>
public sealed class ToolCaseSetup
{
    /// <summary>Files by path under <c>{tmp}</c>, and their content.</summary>
    public Dictionary<string, string> Files { get; set; } = [];
}

/// <summary>What a case checks.</summary>
public sealed class ToolCaseExpect
{
    /// <summary>The exit code, or null for any.</summary>
    public int? Exit { get; set; }

    /// <summary>A regular expression standard output must match, or empty.</summary>
    public string StdoutMatches { get; set; } = string.Empty;

    /// <summary>A regular expression standard error must match, or empty.</summary>
    public string StderrMatches { get; set; } = string.Empty;

    /// <summary>Paths that must not exist afterwards.</summary>
    public List<string> FilesAbsent { get; set; } = [];

    /// <summary>Paths that must exist afterwards.</summary>
    public List<string> FilesPresent { get; set; } = [];
}
