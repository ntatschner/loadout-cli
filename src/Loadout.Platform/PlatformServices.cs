using System.Runtime.InteropServices;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Platform.Linux;
using Loadout.Platform.MacOS;
using Loadout.Platform.Unix;
using Loadout.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Loadout.Platform;

/// <summary>
/// The one place in the codebase that branches on the operating system.
/// <para>
/// Everything above this line depends on the abstractions only, which is what
/// makes the cross-platform contract of spec section 5 mechanically checkable:
/// an architecture test asserts that no other assembly references the Windows,
/// Linux or MacOS namespaces.
/// </para>
/// </summary>
public static class PlatformServices
{
    /// <summary>Selects and registers the implementations for the current platform.</summary>
    public static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
        var environment = new SystemEnvironmentProvider();
        var processes = new ProcessLauncher();
        var host = DetectHost(environment);

        var permissions = CreateFilePermissions();
        var resolver = new ExecutableResolver(environment, StandardSearchPaths(environment, host));
        var paths = CreatePaths(environment, permissions, host);
        var shell = CreateShellProvider(environment, resolver, host);
        var terminals = CreateTerminalProvider(processes, resolver);
        var clipboard = new CommandLineClipboardProvider(processes, resolver, ClipboardCandidates(host));
        var opener = new CommandLineApplicationLauncher(processes, resolver, OpenerName(host));
        var desktop = CreateDesktopIntegration(environment, processes, resolver);
        var secrets = CreateSecretProvider(processes, resolver);

        var capabilities = new PlatformCapabilities(
            () => Probe(host, secrets, clipboard, opener, desktop, terminals));

        services.AddSingleton(host);
        services.AddSingleton<IEnvironmentProvider>(environment);
        services.AddSingleton<IProcessLauncher>(processes);
        services.AddSingleton<IProcessInspector>(new ProcessInspector());
        services.AddSingleton<IFilePermissions>(permissions);
        services.AddSingleton<IExecutableResolver>(resolver);
        services.AddSingleton<IPlatformPaths>(paths);
        services.AddSingleton<IPathSemantics>(new PathSemantics());
        services.AddSingleton<IShellProvider>(shell);
        services.AddSingleton<ITerminalProvider>(terminals);
        services.AddSingleton<IClipboardProvider>(clipboard);
        services.AddSingleton<IApplicationLauncher>(opener);
        services.AddSingleton<IDesktopIntegration>(desktop);
        services.AddSingleton(secrets);
        services.AddSingleton<IPlatformCapabilities>(capabilities);

        // Transient: a pseudo-terminal owns a child process and its handles, so
        // one instance cannot be shared between two sessions the way the
        // stateless services above can.
        services.AddTransient<IPseudoTerminal>(_ => CreatePseudoTerminal());

        return services;
    }

    // The analyser cannot infer that "not Windows and not macOS" means Linux,
    // and it is right not to: a future platform would silently take the Linux
    // branch. Each selector therefore names all three explicitly and refuses
    // anything else outright.
    private const string UnsupportedPlatformMessage =
        "Only Windows, Linux and macOS are supported.";

    /// <summary>
    /// Creates a pseudo-terminal for this platform.
    /// <para>
    /// ConPTY on Windows, posix_spawn into a pty on Linux and macOS. The two
    /// are genuinely
    /// different mechanisms rather than one with a compatibility shim, which is
    /// why the seam exists at all.
    /// </para>
    /// </summary>
    public static IPseudoTerminal CreatePseudoTerminal()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsPseudoTerminal();
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return new UnixPseudoTerminal();
        }

        throw new PlatformNotSupportedException(UnsupportedPlatformMessage);
    }

    /// <summary>Identifies the current machine, refusing anything outside the Tier-1 set.</summary>
    public static HostPlatform DetectHost(IEnvironmentProvider environment)
    {
        var os = OperatingSystem.IsWindows() ? HostOperatingSystem.Windows
            : OperatingSystem.IsMacOS() ? HostOperatingSystem.MacOS
            : OperatingSystem.IsLinux() ? HostOperatingSystem.Linux
            : throw new PlatformNotSupportedException(
                RuntimeInformation.OSDescription
                + " is not one of the supported platforms (Windows, Linux, macOS).");

        return new HostPlatform(
            os,
            RuntimeInformation.ProcessArchitecture,
            RuntimeInformation.OSDescription,
            environment.MachineName);
    }

    /// <summary>
    /// The file-permission implementation for this platform.
    /// <para>
    /// Public so a test can use the real one. Substituting a fake here is not a
    /// neutral simplification: Git ignores a hook that is not executable, so on
    /// Unix a fake that quietly succeeds turns a test proving the hook blocks
    /// into one that proves nothing.
    /// </para>
    /// </summary>
    public static IFilePermissions CreateFilePermissions() =>
        OperatingSystem.IsWindows()
            ? new WindowsFilePermissions()
            : new UnixFilePermissions();

    private static IPlatformPaths CreatePaths(
        IEnvironmentProvider environment,
        IFilePermissions permissions,
        HostPlatform host) => host.OperatingSystem switch
        {
            HostOperatingSystem.Windows => new WindowsPaths(environment, permissions, host),
            HostOperatingSystem.MacOS => new MacOSPaths(environment, permissions, host),
            _ => new LinuxPaths(environment, permissions, host),
        };

    private static IShellProvider CreateShellProvider(
        IEnvironmentProvider environment,
        IExecutableResolver resolver,
        HostPlatform host) => host.OperatingSystem == HostOperatingSystem.Windows
            ? new WindowsShellProvider(environment, resolver)
            : new UnixShellProvider(environment, resolver);

    private static ITerminalProvider CreateTerminalProvider(
        IProcessLauncher processes,
        IExecutableResolver resolver)
    {
        // Written as explicit OperatingSystem checks rather than a switch on
        // HostPlatform so the platform-compatibility analyser can see the
        // guard and accept the OS-annotated constructors.
        if (OperatingSystem.IsWindows())
        {
            return new WindowsTerminalProvider(processes, resolver);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSTerminalProvider(processes, resolver);
        }

        return new LinuxTerminalProvider(processes, resolver);
    }

    private static IDesktopIntegration CreateDesktopIntegration(
        IEnvironmentProvider environment,
        IProcessLauncher processes,
        IExecutableResolver resolver)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsDesktopIntegration(processes, resolver);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSDesktopIntegration();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxDesktopIntegration(environment);
        }

        throw new PlatformNotSupportedException(UnsupportedPlatformMessage);
    }

    private static ISecretProvider CreateSecretProvider(
        IProcessLauncher processes,
        IExecutableResolver resolver)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialProvider();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSKeychainProvider(processes);
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSecretServiceProvider(processes, resolver);
        }

        throw new PlatformNotSupportedException(UnsupportedPlatformMessage);
    }

    /// <summary>
    /// Directories searched after PATH (spec section 65).
    /// <para>
    /// Both Homebrew prefixes are listed for macOS because Apple Silicon
    /// installs under /opt/homebrew and Intel under /usr/local. Neither is
    /// hardcoded as the only valid location, and Homebrew is not required:
    /// these are additions to PATH, never a replacement for it.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> StandardSearchPaths(
        IEnvironmentProvider environment,
        HostPlatform host)
    {
        var home = environment.HomeDirectory;

        return host.OperatingSystem switch
        {
            HostOperatingSystem.Windows =>
            [
                Path.Combine(home, "AppData", "Local", "Programs"),
                Path.Combine(home, ".local", "bin"),
            ],

            HostOperatingSystem.MacOS =>
            [
                "/opt/homebrew/bin",
                "/usr/local/bin",
                "/usr/bin",
                "/bin",
                Path.Combine(home, ".local", "bin"),
            ],

            _ =>
            [
                "/usr/local/bin",
                "/usr/bin",
                "/bin",
                Path.Combine(home, ".local", "bin"),
            ],
        };
    }

    private static IReadOnlyList<ClipboardCommand> ClipboardCandidates(HostPlatform host) =>
        host.OperatingSystem switch
        {
            HostOperatingSystem.Windows => [new ClipboardCommand("clip", [])],

            HostOperatingSystem.MacOS => [new ClipboardCommand("pbcopy", [])],

            // Wayland first, then the two common X11 tools. A headless box has
            // none of them, which is reported rather than treated as an error.
            _ =>
            [
                new ClipboardCommand("wl-copy", []),
                new ClipboardCommand("xclip", ["-selection", "clipboard"]),
                new ClipboardCommand("xsel", ["--clipboard", "--input"]),
            ],
        };

    private static string OpenerName(HostPlatform host) => host.OperatingSystem switch
    {
        HostOperatingSystem.Windows => "explorer",
        HostOperatingSystem.MacOS => "open",
        _ => "xdg-open",
    };

    private static IReadOnlyList<CapabilityStatus> Probe(
        HostPlatform host,
        ISecretProvider secrets,
        CommandLineClipboardProvider clipboard,
        CommandLineApplicationLauncher opener,
        IDesktopIntegration desktop,
        ITerminalProvider terminals)
    {
        var statuses = new List<CapabilityStatus>();

        var secretAvailability = secrets.IsAvailableAsync().GetAwaiter().GetResult();
        statuses.Add(secretAvailability.Succeeded
            ? CapabilityStatus.Supported(PlatformCapability.NativeSecretStore, secrets.Name)
            : CapabilityStatus.Unsupported(
                PlatformCapability.NativeSecretStore,
                secretAvailability.Error ?? "The native secret store is unavailable."));

        // Named for what it actually uses. forkpty was abandoned because
        // forking a multi-threaded runtime leaves one live thread holding every
        // lock the others held.
        statuses.Add(CapabilityStatus.Supported(
            PlatformCapability.PseudoTerminal,
            host.IsUnix ? "posix_spawn" : "ConPTY"));

        statuses.Add(OperatingSystem.IsMacOS()
            ? CapabilityStatus.Unsupported(
                PlatformCapability.PseudoTerminalWindowSize,
                "macOS declares ioctl as variadic, and on Apple Silicon a variadic argument is passed on the stack while a fixed-signature P/Invoke passes it in a register, so the window size never reaches the kernel. Measured on CI: the call returns success and the child reads 62432x27811. The session works; only the size the agent is told about is wrong.")
            : CapabilityStatus.Supported(
                PlatformCapability.PseudoTerminalWindowSize,
                host.IsUnix ? "TIOCSWINSZ" : "ConPTY resize"));

        statuses.Add(host.IsUnix
            ? CapabilityStatus.Supported(PlatformCapability.UnixFilePermissions, "chmod mode bits")
            : CapabilityStatus.Unsupported(
                PlatformCapability.UnixFilePermissions,
                "Windows has no Unix mode bits; restricted ACLs are applied instead."));

        var desktopInstalled = desktop.IsInstalled();
        statuses.Add(desktopInstalled.Succeeded
            ? CapabilityStatus.Supported(
                PlatformCapability.DesktopIntegration,
                desktopInstalled.Value ? "installed" : "available, not installed")
            : CapabilityStatus.Unsupported(
                PlatformCapability.DesktopIntegration,
                desktopInstalled.Error ?? "Desktop integration is unavailable."));

        statuses.Add(opener.IsAvailable
            ? CapabilityStatus.Supported(PlatformCapability.FileManagerIntegration, OpenerName(host))
            : CapabilityStatus.Unsupported(
                PlatformCapability.FileManagerIntegration,
                OpenerName(host) + " was not found."));

        statuses.Add(clipboard.IsAvailable
            ? CapabilityStatus.Supported(
                PlatformCapability.Clipboard,
                clipboard.AvailableToolName ?? "available")
            : CapabilityStatus.Unsupported(
                PlatformCapability.Clipboard,
                "No clipboard tool was found. This is expected on a headless machine."));

        var detected = terminals.DetectAvailable();
        statuses.Add(detected.Count > 0
            ? CapabilityStatus.Supported(
                PlatformCapability.TerminalSpawning,
                string.Join(", ", detected.Select(t => t.DisplayName)))
            : CapabilityStatus.Unsupported(
                PlatformCapability.TerminalSpawning,
                "No terminal emulator was found. The launcher still works in the current terminal."));

        statuses.Add(terminals.IsRunningInTerminal
            ? CapabilityStatus.Supported(PlatformCapability.GraphicalSession, "interactive terminal attached")
            : CapabilityStatus.Unsupported(
                PlatformCapability.GraphicalSession,
                "Output is redirected or no terminal is attached, so prompts are suppressed."));

        return statuses;
    }
}
