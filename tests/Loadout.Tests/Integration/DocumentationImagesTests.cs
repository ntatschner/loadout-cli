using System.Drawing;
using System.Text;
using Loadout.Core.Sessions;
using FluentAssertions;
using Loadout.Models.Diagnostics;
using Loadout.Models.Projects;
using Loadout.Core.Projects;
using Loadout.Tui;
using Loadout.Tui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Draws the launcher's screens and writes them into the documentation.
/// </summary>
/// <remarks>
/// <para>
/// The documentation described screens nobody could see. A table of keys and a
/// paragraph about the palette are accurate and tell a reader nothing about
/// what they are about to look at, and the launcher is the half of this program
/// that cannot be shown in a shell transcript.
/// </para>
/// <para>
/// Drawn through the same headless ANSI driver the tests assert on, so the
/// pictures come from the real widgets rather than from somebody's description
/// of them, and they are redrawn by re-running this rather than by taking a new
/// photograph. What it cannot show is the thing a photograph is for — the font,
/// and whether the terminal has a glyph for what was drawn. That is what
/// <c>build/screenshot-tui.ps1</c> exists for, and the two are complementary.
/// </para>
/// <para>
/// Every value here is invented. The screens render whatever they are given,
/// and given a real machine they would put somebody's project names and home
/// directory into a public repository.
/// </para>
/// <para>
/// Off unless <c>LOADOUT_DOCS_IMAGES=1</c>. A test that writes to the working
/// tree on every run turns an ordinary suite into a source of diffs.
/// </para>
/// </remarks>
public sealed class DocumentationImagesTests
{
    private const int Width = 108;
    private const int Height = 30;

    [DocumentationImageFact]
    public void The_command_palette_is_drawn_for_the_documentation()
    {
        Write("command-palette", Draw(app => new CommandPaletteDialog(
            [
                new CatalogueEntry("doctor", "Check this machine and say what is wrong", null),
                new CatalogueEntry("backup restore", "Put back what a command changed", null),
                new CatalogueEntry("instructions explain", "Say what a session would be given, and why", null),
                new CatalogueEntry("memory write", "Record a durable fact about a project", null),
                new CatalogueEntry("rules budget", "What the instruction layer costs a session", null),
                new CatalogueEntry(
                    "completion",
                    "Emit a shell completion script",
                    "it writes a script to standard output"),
            ],
            app)));
    }

    [DocumentationImageFact]
    public void The_problems_screen_is_drawn_for_the_documentation()
    {
        Write("problems", Draw(app => new ProblemsWindow(
            "starstats",
            [
                DiagnosticCheck.Warn("Repository", "Protection", "no pre-commit hook in this clone"),
                DiagnosticCheck.Warn("Instructions", "Budget", "18 KB loads on every session"),
                DiagnosticCheck.Ok("Agent", "claude", "found on PATH"),
            ],
            [
                new OfferedRemedy(
                    new Remedy(RemedyKind.InstallPreCommitHook, "Install the pre-commit hook"),
                    "would write .git/hooks/pre-commit"),
            ],
            app)));
    }

    [DocumentationImageFact]
    public void The_launcher_is_drawn_for_the_documentation()
    {
        Write("launcher", Draw(app => new LauncherWindow(
            [
                Project("starstats", "StarStats", available: true),
                Project("storefront", "storefront-web", available: true),
                Project("atlas", "atlas", available: false),
            ],
            here: null,
            workspaceState: "workspace clean",
            agents: ["claude", "codex"],
            overview: (project, _) => Task.FromResult<ProjectOverview?>(Overview(project)),
            showPalette: _ => { },
            recent: [],
            application: app)));
    }

    private static ProjectResolution Project(string slug, string name, bool available) =>
        new(
            new ProjectRegistryEntry
            {
                Id = slug,
                Slug = slug,
                Name = name,
                Remote = $"https://github.com/example/{slug}.git",
                DefaultAgent = "claude",
            },
            LocalPath: available ? $"/home/example/src/{slug}" : null,
            LastLaunchedUtc: null,
            LaunchCount: 0,
            Pinned: false);

    /// <summary>
    /// What the right-hand panel is for: the numbers a session would start
    /// with. Invented, but in the shape and range a real project produces —
    /// a picture of an empty panel would document nothing.
    /// </summary>
    private static ProjectOverview Overview(ProjectResolution project) =>
        new(
            project,
            Branch: "main",
            IsClean: true,
            AlwaysLoadedBytes: 11 * 1024,
            ScopedRules: 6,
            MemoryTopics: 9,
            PendingImports: 0,
            Protected: true,
            TrackedAgentFiles: 0);

    /// <summary>Builds a screen, draws it, and returns what was on it.</summary>
    private static string Draw(Func<IApplication, Terminal.Gui.Views.Runnable> build)
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var window = build(app);

        app.Begin(window);
        app.LayoutAndDraw();

        var screen = app.Driver?.ToString() ?? string.Empty;

        screen.Should().NotBeEmpty("a screen that draws nothing is not worth a picture");

        return screen;
    }

    /// <summary>
    /// Writes the screen as an SVG of a terminal.
    /// </summary>
    /// <remarks>
    /// SVG rather than PNG so it stays text: it diffs, it survives a rebase,
    /// and it does not commit a binary that nobody can review. One dark palette
    /// rather than two, because a terminal reads as a terminal on either of a
    /// reader's themes and a half-transparent one reads as neither.
    /// </remarks>
    private static void Write(string name, string screen)
    {
        const int cellWidth = 8;
        const int cellHeight = 17;
        const int pad = 16;
        const int chrome = 28;

        var lines = screen.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        var width = (Width * cellWidth) + (pad * 2);
        var height = (lines.Length * cellHeight) + (pad * 2) + chrome;

        var svg = new StringBuilder();

        svg.AppendLine(
            $"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="{Escape(name)} screen">""");
        svg.AppendLine($"""<rect width="{width}" height="{height}" rx="8" fill="#12151b"/>""");

        // Three dots, so a reader knows at a glance they are looking at a
        // window rather than at a diagram.
        svg.AppendLine("""<circle cx="24" cy="16" r="5" fill="#ff5f57"/>""");
        svg.AppendLine("""<circle cx="42" cy="16" r="5" fill="#febc2e"/>""");
        svg.AppendLine("""<circle cx="60" cy="16" r="5" fill="#28c840"/>""");

        svg.AppendLine(
            $"""<g font-family="Cascadia Mono,DejaVu Sans Mono,Consolas,Menlo,monospace" font-size="13" fill="#d7dae0" xml:space="preserve">""");

        for (var i = 0; i < lines.Length; i++)
        {
            var y = pad + chrome + (i * cellHeight);

            svg.AppendLine($"""<text x="{pad}" y="{y}">{Escape(lines[i].TrimEnd())}</text>""");
        }

        svg.AppendLine("</g>");
        svg.AppendLine("</svg>");

        var directory = Path.Combine(Repository(), "docs", "images");

        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory, name + ".svg"), svg.ToString());
    }

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string Repository()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository has to be findable from the tests");

        return root!.FullName;
    }
}

/// <summary>
/// A test that redraws a picture in the documentation.
/// </summary>
/// <remarks>
/// Off unless asked for. These write into the working tree, and a suite that
/// produces a diff every time it runs teaches everybody to ignore the diff.
/// Redraw them with:
/// <code>LOADOUT_DOCS_IMAGES=1 dotnet test --filter DocumentationImagesTests</code>
/// </remarks>
public sealed class DocumentationImageFactAttribute : Xunit.FactAttribute
{
    public DocumentationImageFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LOADOUT_DOCS_IMAGES") != "1")
        {
            Skip = "Set LOADOUT_DOCS_IMAGES=1 to redraw the documentation images.";
        }
    }
}
