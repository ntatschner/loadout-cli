namespace Loadout.Models;

/// <summary>
/// Which layer decided an effective value.
/// <para>
/// Several settings are resolved by falling through a chain, and until the
/// answer says where it came from, "why is it launching Codex?" has no answer
/// short of reading the source. The chain is real and useful; what was missing
/// was any way to see it.
/// </para>
/// </summary>
public enum SettingSource
{
    /// <summary>Nothing set it; a built-in default was used.</summary>
    BuiltIn,

    /// <summary>The shared configuration, which travels between machines.</summary>
    SharedConfiguration,

    /// <summary>This machine's own configuration.</summary>
    MachineConfiguration,

    /// <summary>The project's entry in the registry.</summary>
    ProjectRegistry,

    /// <summary>The project's manifest in the workspace.</summary>
    ProjectManifest,

    /// <summary>Given on the command line, which beats everything else.</summary>
    CommandLine,
}

/// <summary>
/// An effective value and the layer that decided it.
/// </summary>
/// <typeparam name="T">What was resolved.</typeparam>
/// <param name="Value">The value in force.</param>
/// <param name="Source">Which layer it came from.</param>
public sealed record Resolved<T>(T Value, SettingSource Source)
{
    /// <summary>How to describe where this came from, in a sentence.</summary>
    public string Explanation => Source switch
    {
        SettingSource.CommandLine => "given on the command line",
        SettingSource.ProjectManifest => "set on this project in the workspace",
        SettingSource.ProjectRegistry => "set on this project in the registry",
        SettingSource.MachineConfiguration => "set for this machine",
        SettingSource.SharedConfiguration => "your default, shared between machines",
        _ => "the built-in default",
    };
}
