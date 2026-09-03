namespace Loadout.Models.Agents;

/// <summary>
/// A user-defined agent, configured rather than compiled in (spec section 88).
/// <para>
/// Lets a new tool be adopted without waiting for a purpose-built adapter.
/// Placeholders in arguments and environment values are expanded at launch:
/// <c>${REPOSITORY_PATH}</c>, <c>${WORKSPACE_PATH}</c>,
/// <c>${RUNTIME_DIRECTORY}</c>, <c>${COMPILED_CONTEXT_FILE}</c>,
/// <c>${PROJECT_SLUG}</c> and <c>${PROJECT_NAME}</c>.
/// </para>
/// </summary>
public sealed class GenericAgentDefinition
{
    /// <summary>Display name. Defaults to the configuration key when unset.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Executable name resolved on PATH, or an absolute path.</summary>
    public string Executable { get; set; } = string.Empty;

    /// <summary>Arguments, with placeholders expanded, before any passthrough arguments.</summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>Environment variables set on the child process only.</summary>
    public Dictionary<string, string> Environment { get; set; } = [];

    /// <summary>
    /// How this agent's transcripts are laid out, when it writes any.
    /// </summary>
    /// <remarks>
    /// Optional, and an agent without it still launches — it simply never
    /// appears in a session listing, because nothing knows where to look. This
    /// is the difference between an agent that can be started and one that is
    /// first-class.
    /// </remarks>
    public TranscriptFormat? Transcripts { get; set; }
}
