namespace AgentWorkspace.Models.Projects;

/// <summary>
/// One environment variable handed to the agent process (spec section 53).
/// <para>
/// A binding holds either a literal value or a reference such as
/// <c>anthropic/default</c> that the secret provider resolves at launch. The
/// reference is what gets committed; the value never is, which is the whole
/// point of spec section 52.
/// </para>
/// </summary>
public sealed class EnvironmentBinding
{
    /// <summary>Reference resolved through the configured secret provider.</summary>
    public string? Secret { get; set; }

    /// <summary>A literal, non-sensitive value. Never use this for a credential.</summary>
    public string? Value { get; set; }

    /// <summary>
    /// Whether the launch should fail when the reference cannot be resolved.
    /// Optional bindings let a project declare a credential that only some
    /// machines need.
    /// </summary>
    public bool Required { get; set; } = true;
}
