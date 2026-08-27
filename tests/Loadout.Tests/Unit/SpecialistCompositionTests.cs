using FluentAssertions;
using Loadout.Core.Context;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What actually reaches the agent, and in what order.
/// </summary>
/// <remarks>
/// Resolution decides what is relevant; composition decides what an agent
/// literally reads. The two can be right separately and still leave a gap
/// between them — a specialist selected and then never written into the file
/// would be reported as loaded in every explanation and be absent from the
/// session. These tests cover that gap.
/// </remarks>
public sealed class SpecialistCompositionTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _runtime;

    private readonly ContextCompiler _compiler = new(
        new NoOpFilePermissions(),
        new RuleService(),
        new MemoryService(TimeProvider.System));

    public SpecialistCompositionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-comp-" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "workspace");
        _runtime = Path.Combine(_root, "runtime");

        Directory.CreateDirectory(_runtime);
        Directory.CreateDirectory(Path.Combine(_workspace, "projects", "demo"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth failing a run over a temp directory.
        }
    }

    private static ProjectManifest Manifest() => new()
    {
        Slug = "demo",
        Name = "Demo",
    };

    private static SpecialistDocument Specialist(
        string id,
        SpecialistKind kind,
        string title,
        string body) =>
        new(id, kind, title, "A summary.", SpecialistActivation.None, body,
            System.Text.Encoding.UTF8.GetByteCount(body));

    private static EffectiveInstructions Effective(params SpecialistSelection[] selected) =>
        new("implement", selected, [], [],
            new InstructionContextBudget(
                selected.Sum(s => s.Specialist.Bytes),
                selected.Sum(s => s.Specialist.EstimatedTokens),
                12000,
                80));

    private static SpecialistSelection Chosen(
        SpecialistDocument document,
        SpecialistTrigger trigger = SpecialistTrigger.TaskSemantics,
        string reason = "task mentioned it") =>
        new(document, trigger, reason, 80);

    private async Task<string> CompileAsync(EffectiveInstructions? instructions)
    {
        var result = await _compiler.CompileAsync(
            Manifest(), _workspace, _runtime, "claude", null, null, instructions);

        result.Failed.Should().BeFalse(result.Error);

        return await File.ReadAllTextAsync(result.Value!.FilePath);
    }

    [Fact]
    public async Task A_selected_specialist_reaches_the_agent()
    {
        var text = await CompileAsync(Effective(
            Chosen(Specialist("language.csharp", SpecialistKind.Language, "C#",
                "Honour the nullable setting."))));

        text.Should().Contain("Honour the nullable setting.");
        text.Should().Contain("## C#");
    }

    [Fact]
    public async Task The_reason_travels_with_the_guidance()
    {
        var text = await CompileAsync(Effective(
            Chosen(
                Specialist("database.postgresql", SpecialistKind.Database, "PostgreSQL", "Read the plan."),
                SpecialistTrigger.Dependency,
                "Npgsql dependency declared")));

        // The compiled file is what somebody reads when an agent has behaved
        // oddly, and "why was this here" is the first question. Answering it in
        // the file means it can be answered without re-running anything.
        text.Should().Contain("database.postgresql");
        text.Should().Contain("Npgsql dependency declared");
    }

    [Fact]
    public async Task Specialists_are_written_in_composition_order()
    {
        var text = await CompileAsync(Effective(
            Chosen(Specialist("foundation.core", SpecialistKind.Foundation, "Core", "FOUNDATION-BODY")),
            Chosen(Specialist("language.csharp", SpecialistKind.Language, "C#", "LANGUAGE-BODY")),
            Chosen(Specialist("framework.dotnet", SpecialistKind.Framework, "NET", "FRAMEWORK-BODY"))));

        var foundation = text.IndexOf("FOUNDATION-BODY", StringComparison.Ordinal);
        var language = text.IndexOf("LANGUAGE-BODY", StringComparison.Ordinal);
        var framework = text.IndexOf("FRAMEWORK-BODY", StringComparison.Ordinal);

        // General to specific, so the narrower guidance is read last and its
        // exceptions land on top of the wider rule rather than under it.
        foundation.Should().BeLessThan(language);
        language.Should().BeLessThan(framework);
    }

    [Fact]
    public async Task Specialists_come_before_the_project_own_instructions()
    {
        var projectRoot = Path.Combine(_workspace, "projects", "demo", "context");

        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "architecture.md"), "PROJECT-BODY");

        var manifest = Manifest();

        manifest.Context.Project.Add("context/architecture.md");

        var result = await _compiler.CompileAsync(
            manifest, _workspace, _runtime, "claude", null, null,
            Effective(Chosen(Specialist(
                "language.csharp", SpecialistKind.Language, "C#", "LANGUAGE-BODY"))));

        var text = await File.ReadAllTextAsync(result.Value!.FilePath);

        // The specialist says what C# code should look like; the project says
        // how this codebase departs from that. The project has to be read last
        // or its departures never take effect.
        text.IndexOf("LANGUAGE-BODY", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("PROJECT-BODY", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Each_specialist_is_accounted_for_in_the_source_list()
    {
        var result = await _compiler.CompileAsync(
            Manifest(), _workspace, _runtime, "claude", null, null,
            Effective(
                Chosen(Specialist("language.csharp", SpecialistKind.Language, "C#", "One.")),
                Chosen(Specialist("function.testing", SpecialistKind.Function, "Testing", "Two."))));

        var sources = result.Value!.Sources.Select(s => s.WorkspaceRelativePath).ToList();

        // Provenance for specialists as for everything else, so the existing
        // byte accounting keeps working and covers the new layer too.
        sources.Should().Contain("specialists/language.csharp");
        sources.Should().Contain("specialists/function.testing");
        result.Value.TotalBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task An_overlap_between_specialists_is_written_down()
    {
        var instructions = new EffectiveInstructions(
            "implement",
            [Chosen(Specialist("framework.dotnet", SpecialistKind.Framework, "NET", "Body."))],
            [],
            [new InstructionConflict("C#", "framework.dotnet", "language.csharp", "narrower scope composes last")],
            new InstructionContextBudget(10, 3, 12000, 80));

        var text = await CompileAsync(instructions);

        // An override that happened silently is indistinguishable from an
        // instruction that was never there.
        text.Should().Contain("Where guidance overlaps");
        text.Should().Contain("framework.dotnet");
        text.Should().Contain("language.csharp");
    }

    [Fact]
    public async Task No_specialists_leaves_the_context_exactly_as_it_was()
    {
        var withNone = await CompileAsync(null);
        var withEmpty = await CompileAsync(new EffectiveInstructions(
            "implement", [], [], [], new InstructionContextBudget(0, 0, 12000, 80)));

        // Backward compatibility: a workspace that has never heard of
        // specialists must compile exactly the context it compiled before.
        withNone.Should().Be(withEmpty);
        withNone.Should().NotContain("specialist:");
    }

    [Fact]
    public async Task The_resolution_is_carried_on_the_result_for_reporting()
    {
        var instructions = Effective(
            Chosen(Specialist("language.csharp", SpecialistKind.Language, "C#", "Body.")));

        var result = await _compiler.CompileAsync(
            Manifest(), _workspace, _runtime, "claude", null, null, instructions);

        // So a caller can report what an agent was given without resolving a
        // second time and risking a different answer.
        result.Value!.Instructions.Should().BeSameAs(instructions);
    }
}
