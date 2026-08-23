using System.Runtime.Versioning;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;

namespace Loadout.Platform.MacOS;

/// <summary>
/// Terminal emulators on macOS (spec section 42).
/// <para>
/// Detection differs from the other two platforms because macOS applications
/// are bundles under Applications rather than binaries on PATH. Terminal and
/// iTerm2 in particular install no PATH entry at all, so looking only at PATH
/// would report that a Mac has no terminal installed.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOSTerminalProvider : TerminalProviderBase
{
    private const string OpenTool = "/usr/bin/open";

    public MacOSTerminalProvider(IProcessLauncher processes, IExecutableResolver resolver)
        : base(processes, resolver)
    {
    }

    /// <inheritdoc />
    protected override IReadOnlyList<TerminalCandidate> Candidates =>
    [
        new("ghostty", "Ghostty", "ghostty"),
        new("wezterm", "WezTerm", "wezterm"),
        new("kitty", "kitty", "kitty"),
        new("alacritty", "Alacritty", "alacritty"),
    ];

    private static IReadOnlyList<BundleCandidate> BundleCandidates =>
    [
        new("iterm2", "iTerm2", "iTerm.app"),
        new("warp", "Warp", "Warp.app"),
        new("ghostty", "Ghostty", "Ghostty.app"),
        new("wezterm", "WezTerm", "WezTerm.app"),
        new("kitty", "kitty", "kitty.app"),
        new("alacritty", "Alacritty", "Alacritty.app"),
        new("terminal", "Terminal", "Utilities/Terminal.app"),
    ];

    /// <inheritdoc />
    public override IReadOnlyList<TerminalDescriptor> DetectAvailable()
    {
        var found = new List<TerminalDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Bundles first: on macOS the bundle is the real installation, and a
        // CLI shim on PATH is the exception rather than the rule.
        foreach (var bundle in BundleCandidates)
        {
            foreach (var root in ApplicationRoots())
            {
                var path = Path.Combine(root, bundle.BundleRelativePath);
                if (Directory.Exists(path) && seen.Add(bundle.Id))
                {
                    found.Add(new TerminalDescriptor(bundle.Id, bundle.DisplayName, path));
                    break;
                }
            }
        }

        foreach (var descriptor in base.DetectAvailable())
        {
            if (seen.Add(descriptor.Id))
            {
                found.Add(descriptor);
            }
        }

        return found;
    }

    /// <inheritdoc />
    public override async Task<OperationResult> LaunchInNewWindowAsync(
        TerminalDescriptor terminal,
        ProcessRequest request,
        CancellationToken ct = default)
    {
        if (!terminal.ExecutablePath.EndsWith(".app", StringComparison.Ordinal))
        {
            var arguments = new List<string> { "-e", request.Executable };
            arguments.AddRange(request.Arguments);

            var direct = await Processes.RunAsync(
                new ProcessRequest(terminal.ExecutablePath, arguments, request.WorkingDirectory, request.Environment),
                TimeSpan.FromSeconds(30),
                ct).ConfigureAwait(false);

            return direct.Succeeded && direct.Value?.Succeeded == true
                ? OperationResult.Ok()
                : OperationResult.Fail(direct.Error ?? $"{terminal.DisplayName} could not be started.");
        }

        // A bundle is opened, not executed. Only the target is passed: open
        // has no way to hand arguments to an arbitrary terminal application
        // that every one of them interprets the same way, so the caller passes
        // a runtime script path as the executable.
        var result = await Processes.RunAsync(
            new ProcessRequest(OpenTool, ["-a", terminal.ExecutablePath, request.Executable]),
            TimeSpan.FromSeconds(30),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult.Fail(result.Error ?? $"{terminal.DisplayName} could not be started.");
        }

        return result.Value.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(
                $"{terminal.DisplayName} could not be opened: {result.Value.StandardError.Trim()}");
    }

    private static IReadOnlyList<string> ApplicationRoots()
    {
        var roots = new List<string> { "/Applications", "/System/Applications" };

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            // A per-user Applications folder is a normal macOS install target
            // and needs no administrator rights, so it is checked first.
            roots.Insert(0, Path.Combine(home, "Applications"));
        }

        return roots;
    }

    private sealed record BundleCandidate(string Id, string DisplayName, string BundleRelativePath);
}
