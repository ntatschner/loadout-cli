using System.Drawing;
using System.Text;
using Loadout.Core.Sessions;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Diagnostics;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Loadout.Core.Projects;
using Loadout.Tui;
using Loadout.Tui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Views;
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

    /// <summary>
    /// The launch sheet with a task typed and a mode chosen, so the preview
    /// below it says what that session would be given.
    /// </summary>
    /// <remarks>
    /// Through a session rather than <see cref="Draw"/>, because the preview
    /// arrives after the task is typed and a single draw shows the sheet
    /// before it has anything to say.
    /// </remarks>
    [DocumentationImageFact]
    public void The_launch_sheet_is_drawn_for_the_documentation()
    {
        static SpecialistSelection Chosen(string id, SpecialistKind kind, string title, string reason, int bytes) =>
            new(
                new SpecialistDocument(id, kind, title, "summary", SpecialistActivation.None, "body", bytes),
                SpecialistTrigger.RepositoryEvidence,
                reason,
                60);

        SpecialistSelection[] selected =
        [
            Chosen("foundation.change-safety", SpecialistKind.Foundation, "Change safety", "always applies", 1400),
            Chosen("foundation.verification", SpecialistKind.Foundation, "Verification", "always applies", 900),
            Chosen("mode.investigate", SpecialistKind.Mode, "Investigate", "investigate mode", 800),
            Chosen("language.csharp", SpecialistKind.Language, "C#", "212 .cs files", 1500),
            Chosen("framework.dotnet", SpecialistKind.Framework, ".NET", "Microsoft.Extensions. dependency declared", 1100),
            Chosen("function.debugging", SpecialistKind.Function, "Debugging", "task mentions \"gives up\"", 900),
        ];

        var preview = new EffectiveInstructions(
            "investigate",
            selected,
            [],
            [],
            new InstructionContextBudget(
                selected.Sum(s => s.Specialist.Bytes),
                selected.Sum(s => s.Specialist.EstimatedTokens),
                12_000,
                80));

        LaunchOptionsDialog? sheet = null;

        using var session = TuiSession.Start(
            app => sheet = new LaunchOptionsDialog(
                Project("starstats", "StarStats", available: true),
                ["claude", "codex"],
                app,
                new LaunchSheetSources(
                    (_, _, _) => Task.FromResult(LaunchChoices.None),
                    (_, _) => Task.FromResult<EffectiveInstructions?>(preview)),
                previewDelay: TimeSpan.Zero),
            Width,
            Height);

        session.Type("the upload retries twice then gives up");

        // The mode picker, found by a mode the preview never names. Index
        // zero is "let the task decide", so three is investigate.
        Views(sheet!).OfType<ListView>()
            .Single(list => list.Source?.ToList() is { } items && items.Cast<object?>()
                .Any(item => item?.ToString()?.Contains("advise", StringComparison.Ordinal) == true))
            .SelectedItem = 3;

        Write("launch-sheet", session.ScreenShowing("Debugging"));
    }

    /// <summary>The launcher's Tools, Team runs screen with one run going.</summary>
    [DocumentationImageFact]
    public void The_team_runs_screen_is_drawn_for_the_documentation()
    {
        var noon = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

        IReadOnlyList<RunSummary> runs =
        [
            new RunSummary(
                "20260923-1110-3f1a",
                Directory: Path.Combine("state", "teams", "runs", "20260923-1110-3f1a"),
                Team: "docs-crew",
                Goal: "Bring the storefront README up to date with the new checkout flow",
                Autonomy: "supervised",
                Started: noon.AddMinutes(-50),
                Finished: null,
                Ended: null,
                CostUsd: 2.45m,
                Rounds: 2,
                Nodes:
                [
                    new RunNode("lead", "role.project-lead", "done", 10, 0.70m, noon.AddMinutes(-18),
                        Started: noon.AddMinutes(-50)),
                    new RunNode("writer", "role.docs-writer", "working", 14, 1.12m, noon.AddMinutes(-2),
                        Started: noon.AddMinutes(-48), Doing: "Rewriting the checkout section"),
                    new RunNode("checker", "role.docs-checker", "done", 9, 0.63m, noon.AddMinutes(-22),
                        Started: noon.AddMinutes(-30)),
                ],
                Merged: [],
                Branches: [],
                Project: "storefront",
                BudgetUsd: 10m),
        ];

        using var session = TuiSession.Start(
            app => new TeamsWindow(runs, _ => Task.FromResult(runs), live: false, app),
            Width,
            Height);

        // Nothing touched: the screen opens on the first run with its nodes.
        var drawn = session.ScreenShowing("writer");

        drawn.Should().Contain("writer", "a picture of the team runs screen should show a run's nodes");

        Write("team-runs", drawn);
    }

    private static IEnumerable<Terminal.Gui.ViewBase.View> Views(Terminal.Gui.ViewBase.View root)
    {
        foreach (var child in root.SubViews)
        {
            yield return child;

            foreach (var inner in Views(child))
            {
                yield return inner;
            }
        }
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

        // What the launcher does after Init, and what TuiSession does for
        // the other screen tests. Without it the pictures depended on which
        // tests had run first in the process: drawn alone they showed
        // shadowed buttons in brackets the console font cannot draw, and
        // drawn after the workflow tests they showed the launcher as it is.
        ConsoleGlyphs.MakeLegible();
        LauncherTheme.Apply();

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

        // white-space as well as xml:space. SVG 2 deprecated xml:space and
        // Chrome ignores it, so every run of spaces collapsed to one: columns
        // slid left, box borders landed mid-line, and the launcher's detail
        // panel was drawn underneath its project list. A rule on the text
        // itself rather than on the group, because the browser's own
        // stylesheet sets white-space on text and that beats inheriting it.
        svg.AppendLine("""<style>text { white-space: pre; }</style>""");
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
