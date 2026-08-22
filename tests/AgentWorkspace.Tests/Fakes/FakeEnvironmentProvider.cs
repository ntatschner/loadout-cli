using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Tests.Fakes;

/// <summary>
/// A scripted environment.
/// <para>
/// This is what makes the three platform path layouts testable from any host.
/// Without it, the macOS and Linux layouts could only ever be verified on
/// macOS and Linux, which would leave the most divergent part of the launcher
/// covered on one platform at a time.
/// </para>
/// </summary>
public sealed class FakeEnvironmentProvider : IEnvironmentProvider
{
    private readonly Dictionary<string, string> _variables;

    public FakeEnvironmentProvider(
        string homeDirectory,
        Dictionary<string, string>? variables = null,
        string machineName = "TEST-MACHINE")
    {
        HomeDirectory = homeDirectory;
        MachineName = machineName;
        _variables = variables ?? [];
    }

    public string HomeDirectory { get; }

    public string MachineName { get; }

    public IReadOnlyList<string> PathDirectories { get; init; } = [];

    public IReadOnlyList<string> ExecutableExtensions { get; init; } = [string.Empty];

    public string? GetVariable(string name) =>
        _variables.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}
