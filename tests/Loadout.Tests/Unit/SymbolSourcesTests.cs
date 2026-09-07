using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Projects;
using Loadout.Platform.Abstractions;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Where the files come from, what a project can say about reading them,
/// and the tagger for the languages the table does not read.
/// </summary>
public sealed class SymbolSourcesTests : IDisposable
{
    private readonly string _root;
    private readonly string _repository;
    private readonly string _cacheRoot;

    public SymbolSourcesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-sources-" + Guid.NewGuid().ToString("N"));
        _repository = Path.Combine(_root, "repo");
        _cacheRoot = Path.Combine(_root, "cache");

        Directory.CreateDirectory(_repository);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a run over a temp directory.
        }
    }

    private sealed class FixedLister(IReadOnlyList<string>? files) : ISymbolSourceLister
    {
        public Task<IReadOnlyList<string>?> ListAsync(string repositoryPath, CancellationToken ct = default) =>
            Task.FromResult(files);
    }

    private sealed class FixedTagger(IReadOnlyList<Symbol>? symbols) : ISymbolTagger
    {
        public List<IReadOnlyList<string>> Asked { get; } = [];

        public Task<IReadOnlyList<Symbol>?> TagAsync(
            string repositoryPath,
            IReadOnlyList<string> files,
            CancellationToken ct = default)
        {
            Asked.Add(files);

            return Task.FromResult(symbols is null
                ? null
                : (IReadOnlyList<Symbol>?)symbols.Where(s => files.Contains(s.File)).ToList());
        }
    }

    private sealed class FixedResolver(string? path) : IExecutableResolver
    {
        public string? Resolve(string name, IReadOnlyList<string>? additionalPaths = null) =>
            name == "ctags" ? path : null;

        public IReadOnlyList<string> StandardSearchPaths => [];
    }

    private void WriteSource(string relative, params string[] lines)
    {
        var path = Path.Combine(_repository, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }

    [Fact]
    public void Options_come_from_the_manifest_and_a_bad_pattern_costs_only_its_language()
    {
        var options = SymbolScanOptions.From(new ProjectSymbols
        {
            Ignore = ["generated/**", string.Empty],
            Extensions = { ["pyw"] = "python", [".cshtml"] = "csharp" },
            Languages =
            [
                new()
                {
                    Id = "elixir",
                    Name = "Elixir",
                    Extensions = ["ex", ".exs"],
                    Types = @"^\s*defmodule\s+(?<name>[\w.]+)",
                    Members = @"^\s*def(?:p)?\s+(?<name>\w+)",
                    Docs = "hash",
                },
                new() { Id = "broken", Extensions = [".brk"], Members = "(unclosed" },
                new() { Id = "nomembers", Extensions = [".nm"] },
            ],
        });

        options.Ignore.Should().Equal("generated/**");
        options.Excludes("generated/api/client.ts").Should().BeTrue();
        options.Excludes("src/client.ts").Should().BeFalse();

        options.LanguageFor("tools/run.pyw")!.Id.Should().Be("python");
        options.LanguageFor("Views/Index.cshtml")!.Id.Should().Be("csharp");
        options.LanguageFor("lib/app.ex")!.Name.Should().Be("Elixir");
        options.LanguageFor("test/app_test.exs")!.Id.Should().Be("elixir");
        options.LanguageFor("x.brk").Should().BeNull("its pattern does not compile");
        options.LanguageFor("x.nm").Should().BeNull("it names no member pattern");
        options.LanguageFor("src/Program.cs")!.Id.Should().Be("csharp", "the table still applies");

        var elixir = SymbolScan.InFile(
            ["defmodule App.Widget do", "  @doc \"Turns it.\"", "  # Turns it once.", "  def turn(x), do: x", "  defp hidden, do: 1", "end"],
            "lib/widget.ex",
            options).ToList();

        elixir.Select(s => (s.Kind, s.Name, s.Line)).Should().Equal(
            (SymbolKind.Type, "App.Widget", 1),
            (SymbolKind.Member, "turn", 4),
            (SymbolKind.Member, "hidden", 5));

        SymbolScanOptions.From(new ProjectSymbols
        {
            Languages = [new() { Id = "x", Extensions = [".x"], Members = "(?<name>\\w+)", Docs = "docstring_below" }],
        }).Languages.Single().Docs.Should().Be(DocStyle.DocstringBelow);

        SymbolScanOptions.From(null).Should().BeSameAs(SymbolScanOptions.Default);
    }

    [Fact]
    public void A_scan_reads_the_files_it_is_given_and_leaves_out_what_the_project_excludes()
    {
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");
        WriteSource("build/tool.ps1", "function Invoke-Build { }");
        WriteSource("generated/Client.cs", "public sealed class Client", "{", "}");
        WriteSource("stray/Stray.cs", "public sealed class Stray", "{", "}");

        var options = SymbolScanOptions.From(new ProjectSymbols { Ignore = ["generated/**"] });

        // The list decides: 'build' is on it, so its script is read despite
        // the walker's own rules; 'stray' is not on it, so it is not.
        var listed = SymbolScan.Scan(
            _repository,
            ["src/Widget.cs", "build/tool.ps1", "generated/Client.cs"],
            options);

        listed.Select(s => s.Name).Should().BeEquivalentTo(["Widget", "Invoke-Build"]);

        // No list: the walk, with the same exclusions on top.
        var walked = SymbolScan.Scan(_repository, null, options);

        walked.Select(s => s.Name).Should().BeEquivalentTo(["Widget", "Stray"]);

        SymbolScan.Unread(_repository, ["src/Widget.cs", "lib/app.hs", "README", "generated/x.hs"], options)
            .Should().Equal("lib/app.hs");
    }

    [Fact]
    public void Ctags_output_is_folded_onto_the_two_kinds_and_bad_lines_are_skipped()
    {
        var symbols = CtagsOutput.Parse(
            """
            {"_type": "ptag", "name": "JSON_OUTPUT_VERSION", "path": "1.0"}
            {"_type": "tag", "name": "Widget", "path": "lib/Widget.hs", "pattern": "/^data Widget/", "line": 3, "language": "Haskell", "kind": "constructor"}
            {"_type": "tag", "name": "Widget", "path": "lib\\Widget.hs", "pattern": "/^data Widget/", "line": 3, "language": "Haskell", "kind": "type"}
            {"_type": "tag", "name": "turn", "path": "lib/Widget.hs", "line": 7, "language": "Haskell", "kind": "function"}
            {"_type": "tag", "name": "x", "path": "lib/Widget.hs", "line": 8, "language": "Haskell", "kind": "local"}
            not json at all
            {"_type": "tag", "name": "App", "path": "lib/app.ex", "line": 1, "language": "Elixir", "kind": "module"}
            {"_type": "tag", "name": "noline", "path": "lib/app.ex", "language": "Elixir", "kind": "function"}
            """);

        symbols.Select(s => (s.Kind, s.Name, s.File, s.Line, s.Language)).Should().Equal(
            (SymbolKind.Type, "Widget", "lib/Widget.hs", 3, "haskell"),
            (SymbolKind.Member, "turn", "lib/Widget.hs", 7, "haskell"),
            (SymbolKind.Type, "App", "lib/app.ex", 1, "elixir"));
    }

    [Fact]
    public async Task The_tagger_is_absent_without_ctags_and_reads_the_files_it_is_given_with_it()
    {
        var launcher = new StubProcessLauncher(
            """{"_type": "tag", "name": "turn", "path": "lib/Widget.hs", "line": 7, "language": "Haskell", "kind": "function"}""");

        (await new CtagsTagger(launcher, new FixedResolver(null)).TagAsync(_repository, ["lib/Widget.hs"]))
            .Should().BeNull("nothing on the machine can tag");
        launcher.Requests.Should().BeEmpty();

        var tagger = new CtagsTagger(launcher, new FixedResolver(@"C:\tools\ctags.exe"));

        (await tagger.TagAsync(_repository, [])).Should().BeEmpty("nothing to tag is an answer");

        var tagged = await tagger.TagAsync(_repository, ["lib/Widget.hs", "lib/app.ex"]);

        tagged!.Single().Name.Should().Be("turn");

        var request = launcher.Requests.Single();

        request.Executable.Should().Be(@"C:\tools\ctags.exe");
        request.Arguments.Should().Contain("--output-format=json").And.Contain("-L");
        request.StandardInput.Should().Be("lib/Widget.hs\nlib/app.ex\n");
        request.WorkingDirectory.Should().Be(_repository);

        // Exuberant ctags exits with a usage error on the JSON option: no
        // tagger rather than a broken one.
        var old = new CtagsTagger(new StubProcessLauncher("ctags: Unknown option", exitCode: 1), new FixedResolver("ctags"));

        (await old.TagAsync(_repository, ["lib/Widget.hs"])).Should().BeNull();
    }

    [Fact]
    public async Task The_service_joins_the_table_and_the_tagger_by_file_and_never_counts_twice()
    {
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");
        WriteSource("lib/Widget.hs", "data Widget = Widget", "turn :: Widget -> Widget");
        WriteSource("notes/README", "no extension, never asked about");

        var tagger = new FixedTagger(
        [
            new(SymbolKind.Type, "Widget", "", "lib/Widget.hs", 1, "", "", "haskell"),
            new(SymbolKind.Member, "turn", "", "lib/Widget.hs", 2, "", "", "haskell"),
            new(SymbolKind.Type, "Never", "", "src/Widget.cs", 99, "", "", "haskell"),
        ]);

        var service = new SymbolIndexService(
            (_, _) => Task.FromResult<string?>("abc123"),
            new SymbolIndexCache(_cacheRoot),
            new FixedLister(["src/Widget.cs", "lib/Widget.hs", "notes/README"]),
            tagger);

        var symbols = await service.ScanAsync(_repository, "starstats");

        symbols.Select(s => (s.Name, s.Language)).Should().BeEquivalentTo(
        [
            ("Widget", "csharp"),
            ("Widget", "haskell"),
            ("turn", "haskell"),
        ]);

        // The tagger was asked about the file the table cannot read, and only that.
        tagger.Asked.Should().ContainSingle().Which.Should().Equal("lib/Widget.hs");

        // The lookup, the map and the refresh all see the tagged half.
        (await service.FindAsync(_repository, "starstats", "turn")).Value!.Matches
            .Should().ContainSingle().Which.Symbol.File.Should().Be("lib/Widget.hs");

        var digest = await service.DigestAsync(_repository, "starstats");

        // Most of the code first: two Haskell symbols to one C#.
        digest.Value!.Languages.Should().Equal("Haskell", "C#");
        digest.Value!.Text.Should().Contain("- `lib` — 1 type(s): Widget");

        var refreshed = await service.RefreshAsync(_repository, "starstats", ["lib/Widget.hs"]);

        refreshed.Value!.Files.Single().Should().Be(new SymbolRefreshedFile("lib/Widget.hs", 2, 2));
    }

    [Fact]
    public async Task Without_a_tagger_the_unread_files_are_simply_not_indexed()
    {
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");
        WriteSource("lib/Widget.hs", "data Widget = Widget");

        var service = new SymbolIndexService(
            (_, _) => Task.FromResult<string?>("abc123"),
            new SymbolIndexCache(_cacheRoot),
            new FixedLister(["src/Widget.cs", "lib/Widget.hs"]),
            tagger: null);

        (await service.ScanAsync(_repository, "starstats")).Select(s => s.Language).Should().Equal("csharp");

        var absent = new FixedTagger(null);
        var withAbsent = new SymbolIndexService(
            (_, _) => Task.FromResult<string?>("abc123"),
            new SymbolIndexCache(_cacheRoot),
            new FixedLister(["src/Widget.cs", "lib/Widget.hs"]),
            absent);

        (await withAbsent.ScanAsync(_repository, "starstats")).Should().HaveCount(1);
        absent.Asked.Should().ContainSingle();
    }
}
