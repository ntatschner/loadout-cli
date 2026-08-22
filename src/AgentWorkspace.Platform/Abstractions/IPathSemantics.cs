namespace AgentWorkspace.Platform.Abstractions;

/// <summary>
/// Compares and canonicalises filesystem paths correctly for the volume they
/// live on (spec section 84).
/// <para>
/// Case sensitivity cannot be inferred from the operating system. APFS ships
/// both case-sensitive and case-insensitive, and NTFS has a per-directory
/// case-sensitivity flag, so the only correct answer comes from probing the
/// volume. Unicode matters too: macOS has historically stored filenames
/// decomposed, so the same visible name can arrive in NFD from one source and
/// NFC from another and compare unequal, which would register one repository
/// as two projects.
/// </para>
/// <para>
/// Every path comparison in core goes through this interface. A direct
/// string.Equals with OrdinalIgnoreCase anywhere in core is a defect.
/// </para>
/// </summary>
public interface IPathSemantics
{
    /// <summary>
    /// Whether two paths refer to the same location, accounting for the
    /// volume's case sensitivity, Unicode normalisation, separators, trailing
    /// separators and symbolic links.
    /// </summary>
    bool PathsEqual(string left, string right);

    /// <summary>
    /// Canonical form of a path for use as a dictionary key: absolute, link-
    /// resolved, normalised, and case-folded only where the volume is
    /// genuinely case-insensitive.
    /// </summary>
    string Canonicalise(string path);

    /// <summary>
    /// Whether the volume containing this path treats names case-insensitively.
    /// Determined by probing and cached per volume root. Surfaced by doctor so
    /// a surprising answer is visible rather than mysterious.
    /// </summary>
    bool IsCaseInsensitive(string path);
}
