namespace Loadout.Models.Tools;

/// <summary>One recorded use of a tool, a line of <c>usage.jsonl</c>.</summary>
public sealed class ToolUsage
{
    /// <summary>The tool.</summary>
    public string Tool { get; set; } = string.Empty;

    /// <summary>The version used.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>One of <see cref="ToolOutcome" />.</summary>
    public string Outcome { get; set; } = ToolOutcome.Ok;

    /// <summary>The team that used it, counted but never shown.</summary>
    public string Team { get; set; } = string.Empty;

    /// <summary>The run it was used in, where there was one.</summary>
    public string Run { get; set; } = string.Empty;

    /// <summary>What happened, in the user's words.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>When.</summary>
    public DateTimeOffset? At { get; set; }
}

/// <summary>How a use went.</summary>
public static class ToolOutcome
{
    /// <summary>It worked.</summary>
    public const string Ok = "ok";

    /// <summary>It did not.</summary>
    public const string Failed = "failed";

    /// <summary>It worked once something was done around it.</summary>
    public const string Workaround = "workaround";

    /// <summary>Every outcome.</summary>
    public static IReadOnlyList<string> All { get; } = [Ok, Failed, Workaround];
}
