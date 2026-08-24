namespace Loadout.Models.Agents;

/// <summary>
/// What was found when the launcher looked for an agent on this machine
/// (spec sections 65 to 67).
/// </summary>
/// <param name="Name">Adapter name, e.g. <c>claude</c> or <c>codex</c>.</param>
/// <param name="DisplayName">Human-facing name, e.g. <c>Claude Code</c>.</param>
/// <param name="IsInstalled">Whether an executable was located.</param>
/// <param name="ExecutablePath">Absolute path to the executable, or null when not installed.</param>
/// <param name="Version">Reported version string, or null when it could not be determined.</param>
/// <param name="Capabilities">
/// Probed capabilities (spec section 66). Detection is preferred over version
/// comparison: the spec's own invocation examples are marked "conceptual", so
/// the flag surface has to be confirmed against the installed CLI rather than
/// assumed from a version number.
/// </param>
public sealed record AgentDescriptor(
    string Name,
    string DisplayName,
    bool IsInstalled,
    string? ExecutablePath,
    string? Version,
    IReadOnlyDictionary<string, bool> Capabilities)
{
    public static AgentDescriptor NotInstalled(string name, string displayName) =>
        new(name, displayName, false, null, null, new Dictionary<string, bool>());

    /// <summary>True when the named capability was probed and found present.</summary>
    public bool Supports(string capability) =>
        Capabilities.TryGetValue(capability, out var value) && value;
}

/// <summary>Capability keys shared across adapters. Adapters may add their own.</summary>
public static class AgentCapabilities
{
    /// <summary>Settings can be supplied from a file outside the repository.</summary>
    public const string ExternalSettings = "external_settings";

    /// <summary>MCP servers can be supplied from files the launcher controls.</summary>
    public const string McpConfig = "mcp_config";

    /// <summary>A system prompt or instruction file can be supplied from outside the repository.</summary>
    public const string ExternalPrompt = "external_prompt";

    /// <summary>Extra source directories can be exposed to the agent.</summary>
    public const string AdditionalDirectories = "additional_directories";

    /// <summary>The agent's configuration home can be relocated by environment variable.</summary>
    public const string ExternalHome = "external_home";

    /// <summary>The agent can enforce its own sandboxing.</summary>
    public const string Sandboxing = "sandboxing";

    /// <summary>Previous sessions can be resumed (spec section 68).</summary>
    public const string SessionResume = "session_resume";
}
