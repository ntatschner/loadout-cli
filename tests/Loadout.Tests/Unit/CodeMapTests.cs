using FluentAssertions;
using Loadout.Core.Context;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The map of the code, inlined only where a project has asked to pay for it.
/// </summary>
/// <remarks>
/// A layer that costs a few thousand tokens on every launch has to be opt-in
/// and has to be priced. These tests cover both halves: that it is absent
/// until asked for and present once it is, and that the budget report shows it
/// as its own line either way.
/// </remarks>
public sealed class CodeMapTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _runtime;
    private readonly string _repository;

    private static readonly Symbol[] Symbols =
    [
        new(SymbolKind.Type, "Widget", "public sealed class Widget", "src/Core/Widget.cs", 3, "A widget.", "", "csharp"),
        new(SymbolKind.Member, "Turn", "public void Turn()", "src/Core/Widget.cs", 9, "", "", "csharp"),
        new(SymbolKind.Type, "Gadget", "public sealed class Gadget", "src/Core/Gadget.cs", 1, "", "", "csharp"),
        new(SymbolKind.Type, "App", "export class App", "web/app.ts", 1, "", "", "typescript"),
        new(SymbolKind.Member, "run", "def run()", "tools/run.py", 1, "", "", "python"),
    ];

    public CodeMapTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-map-" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "workspace");
        _runtime = Path.Combine(_root, "runtime");
        _repository = Path.Combine(_root, "repo");

        Directory.CreateDirectory(_runtime);
        Directory.CreateDirectory(_repository);
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

    private static ProjectManifest Manifest(bool codeMap) => new()
    {
        Slug = "demo",
        Name = "Demo",
        Context = { CodeMap = codeMap },
    };

    private async Task<(string Text, CompiledContext Result)> CompileAsync(
        bool codeMap,
        StubSymbolIndex? symbols,
        string? repository)
    {
        var compiler = new ContextCompiler(
            new NoOpFilePermissions(),
            new RuleService(),
            new MemoryService(TimeProvider.System),
            symbols);

        var result = await compiler.CompileAsync(
            Manifest(codeMap), _workspace, _runtime, "claude", repositoryPath: repository);

        result.Failed.Should().BeFalse(result.Error);

        return (await File.ReadAllTextAsync(result.Value!.FilePath), result.Value!);
    }

    [Fact]
    public async Task The_map_is_absent_until_the_project_asks_for_it()
    {
        var symbols = new StubSymbolIndex(Symbols);

        var (text, result) = await CompileAsync(codeMap: false, symbols, _repository);

        text.Should().NotContain("Where the code is");
        result.Sources.Should().NotContain(source => source.Heading == "Where the code is");

        // Not even asked for: a scan nobody will read is a scan not worth running.
        symbols.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task The_map_names_each_directory_and_its_types_once_asked_for()
    {
        var symbols = new StubSymbolIndex(Symbols);

        var (text, result) = await CompileAsync(codeMap: true, symbols, _repository);

        text.Should().Contain("## Where the code is");
        text.Should().Contain("- `src/Core` — 2 type(s): Widget, Gadget");
        text.Should().Contain("- `web` — 1 type(s): App");

        // A directory of members alone is still a place things are.
        text.Should().Contain("- `tools` — 0 type(s): ");

        // Provenance and the way to one exact answer travel with the map.
        text.Should().Contain("at abc1234");
        text.Should().Contain("5 symbol(s) in C#, Python and TypeScript");
        text.Should().Contain("loadout docs find <name>");

        symbols.Asked.Should().Equal(_repository);

        // Priced like every other source, so the budget can say what it cost.
        var source = result.Sources.Single(source => source.Heading == "Where the code is");

        source.WorkspaceRelativePath.Should().Be("symbols/demo");
        source.Bytes.Should().Be(
            System.Text.Encoding.UTF8.GetByteCount(SymbolDigest.Modules(Symbols)));
    }

    [Fact]
    public async Task Nothing_is_written_when_there_is_no_repository_or_nothing_to_map()
    {
        var (offMachine, _) = await CompileAsync(codeMap: true, new StubSymbolIndex(Symbols), null);
        var (empty, _) = await CompileAsync(codeMap: true, new StubSymbolIndex(), _repository);
        var (noService, _) = await CompileAsync(codeMap: true, null, _repository);

        // Absent without a word rather than an empty heading where the map
        // should be.
        offMachine.Should().NotContain("Where the code is");
        empty.Should().NotContain("Where the code is");
        noService.Should().NotContain("Where the code is");
    }

    [Fact]
    public void The_budget_prices_the_map_as_its_own_every_launch_line()
    {
        var instructions = new EffectiveInstructions(
            "implement", [], [], [], new InstructionContextBudget(0, 0, 12000, 80));

        var without = ContextBudget.From(instructions, 0, 0, 0);
        var with = ContextBudget.From(instructions, 0, 0, 0, codeMapBytes: 8000);

        // Shown at zero before it is switched on, so the decision can be made
        // with the figure in front of you.
        without.Layers.Should().Contain(layer => layer.Name == "Code map" && layer.EstimatedTokens == 0);

        var map = with.Layers.Single(layer => layer.Name == "Code map");

        map.EveryLaunch.Should().BeTrue();
        map.EstimatedTokens.Should().Be(2000);
        with.EveryLaunchTokens.Should().Be(2000);
    }

    [Fact]
    public void The_digest_lists_a_directory_once_and_caps_its_names()
    {
        var many = Enumerable.Range(0, 15)
            .Select(i => new Symbol(SymbolKind.Type, $"T{i}", "", "src/Big/File.cs", i + 1, ""))
            .Append(new Symbol(SymbolKind.Type, "T0", "", "src/Big/Other.cs", 1, ""))
            .Append(new Symbol(SymbolKind.Type, "Root", "", "Program.cs", 1, ""))
            .ToList();

        var text = SymbolDigest.Modules(many);

        text.Should().Contain("- `src/Big` — 15 type(s): T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, and more");
        text.Should().Contain("- `(root)` — 1 type(s): Root");
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2);
    }
}
