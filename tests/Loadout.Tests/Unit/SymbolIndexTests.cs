using FluentAssertions;
using Loadout.Core.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Where a name is declared, answered from an index that must never be wrong.
/// </summary>
/// <remarks>
/// The cache is keyed by commit, which is coarser than the tree an agent is
/// editing. So the tests here are less about the cache being used than about
/// it being unable to mislead: a file edited since the index was built is read
/// again, and a name the index has never seen is looked for in the tree before
/// it is reported missing.
/// </remarks>
public sealed class SymbolIndexTests : IDisposable
{
    private readonly string _root;
    private readonly string _repository;
    private readonly string _cacheRoot;

    public SymbolIndexTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-symbols-" + Guid.NewGuid().ToString("N"));
        _repository = Path.Combine(_root, "repo");
        _cacheRoot = Path.Combine(_root, "cache");

        Directory.CreateDirectory(_repository);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Not worth failing a run over a temp directory.
        }
    }

    private static Symbol Type(string name, string file, int line = 1) =>
        new(SymbolKind.Type, name, $"public sealed class {name}", file, line, string.Empty);

    private static Symbol Member(string name, string file, int line = 1) =>
        new(SymbolKind.Member, name, $"public void {name}()", file, line, string.Empty);

    [Fact]
    public void A_whole_name_outranks_a_prefix_which_outranks_a_fragment()
    {
        var symbols = new[]
        {
            Type("PreflightServiceTests", "tests/Preflight.cs"),
            Type("IPreflightService", "src/IPreflight.cs"),
            Type("PreflightService", "src/Preflight.cs"),
        };

        var found = SymbolSearch.Find(symbols, "preflightservice");

        found.Select(match => match.Symbol.Name).Should().Equal(
            "PreflightService", "PreflightServiceTests", "IPreflightService");

        found[0].Exact.Should().BeTrue();
        found[1].Exact.Should().BeFalse();
    }

    [Fact]
    public void A_type_outranks_a_member_of_the_same_name()
    {
        // A member called Scan exists in twenty places; the type is what
        // somebody meant when the query is ambiguous.
        var symbols = new[]
        {
            Member("Scan", "src/A.cs", 10),
            Type("Scan", "src/Z.cs", 3),
            Member("Scan", "src/B.cs", 4),
        };

        var found = SymbolSearch.Find(symbols, "Scan");

        found.Select(match => (match.Symbol.Kind, match.Symbol.File)).Should().Equal(
            (SymbolKind.Type, "src/Z.cs"),
            (SymbolKind.Member, "src/A.cs"),
            (SymbolKind.Member, "src/B.cs"));
    }

    [Fact]
    public void The_limit_is_honoured_and_an_empty_query_finds_nothing()
    {
        var symbols = Enumerable.Range(0, 20)
            .Select(i => Type($"Widget{i}", $"src/W{i}.cs"))
            .ToList();

        SymbolSearch.Find(symbols, "Widget", limit: 3).Should().HaveCount(3);
        SymbolSearch.Find(symbols, "   ").Should().BeEmpty();
        SymbolSearch.Find(symbols, "Widget", limit: 0).Should().BeEmpty();
    }

    [Fact]
    public async Task The_cache_answers_only_for_the_commit_it_was_built_at()
    {
        var cache = new SymbolIndexCache(_cacheRoot);

        var symbols = new[]
        {
            Type("Widget", "src/Widget.cs", 4) with { Summary = "A widget.", Language = "csharp" },
        };

        await cache.WriteAsync("starstats", _repository, "abc123", symbols);

        // What a lookup shows or ranks on comes back; the signature and the
        // remarks are for the exported documents, which scan afresh, and are
        // deliberately not carried.
        var read = await cache.ReadAsync("starstats", _repository, "abc123");

        read.Should().ContainSingle().Which.Should().Be(
            new Symbol(SymbolKind.Type, "Widget", string.Empty, "src/Widget.cs", 4, "A widget.", "", "csharp"));

        (await cache.ReadAsync("starstats", _repository, "def456")).Should().BeNull();
        (await cache.ReadAsync("other", _repository, "abc123")).Should().BeNull();
    }

    [Fact]
    public async Task A_torn_cache_reads_as_no_cache()
    {
        var cache = new SymbolIndexCache(_cacheRoot);

        Directory.CreateDirectory(Path.GetDirectoryName(cache.PathFor("starstats", _repository))!);
        await File.WriteAllTextAsync(cache.PathFor("starstats", _repository), "{\"head\":\"abc123\",\"symbols\":[");

        (await cache.ReadAsync("starstats", _repository, "abc123")).Should().BeNull();
    }

    [Fact]
    public async Task The_first_lookup_scans_and_the_second_reads_the_index()
    {
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");

        var service = Service("abc123");

        var first = await service.FindAsync(_repository, "starstats", "Widget");
        var second = await service.FindAsync(_repository, "starstats", "Widget");

        first.Succeeded.Should().BeTrue(first.Error);
        first.Value!.FromCache.Should().BeFalse();
        first.Value!.Head.Should().Be("abc123");
        first.Value!.Matches.Should().ContainSingle()
            .Which.Symbol.Should().BeEquivalentTo(new { File = "src/Widget.cs", Line = 1 });

        second.Value!.FromCache.Should().BeTrue();
        second.Value!.Rebuilt.Should().BeFalse();
        second.Value!.Matches.Should().ContainSingle()
            .Which.Symbol.File.Should().Be("src/Widget.cs");

        File.Exists(new SymbolIndexCache(_cacheRoot).PathFor("starstats", _repository)).Should().BeTrue();
    }

    [Fact]
    public async Task A_file_edited_since_the_index_was_built_is_read_again()
    {
        // The agent is editing exactly the files it asks about, and a line
        // number from the cache is wrong the moment a method above it grows.
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");

        var service = Service("abc123");

        await service.FindAsync(_repository, "starstats", "Widget");

        WriteSource("src/Widget.cs", "// moved down", "// and again", "public sealed class Widget", "{", "}");

        var found = await service.FindAsync(_repository, "starstats", "Widget");

        found.Value!.FromCache.Should().BeTrue();
        found.Value!.Matches.Should().ContainSingle()
            .Which.Symbol.Line.Should().Be(3);
    }

    [Fact]
    public async Task A_name_the_index_has_never_seen_is_looked_for_in_the_tree()
    {
        // The one thing a cache keyed by commit cannot know about is a file
        // added since. A miss is checked before it is reported as one.
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");

        var service = Service("abc123");

        await service.FindAsync(_repository, "starstats", "Widget");

        WriteSource("src/Gadget.cs", "public sealed class Gadget", "{", "}");

        var found = await service.FindAsync(_repository, "starstats", "Gadget");

        found.Value!.Rebuilt.Should().BeTrue();
        found.Value!.Matches.Should().ContainSingle()
            .Which.Symbol.File.Should().Be("src/Gadget.cs");

        // And the rebuilt index is what the next lookup reads.
        var again = await service.FindAsync(_repository, "starstats", "Gadget");

        again.Value!.FromCache.Should().BeTrue();
        again.Value!.Rebuilt.Should().BeFalse();
    }

    [Fact]
    public async Task A_renamed_symbol_is_dropped_rather_than_reported_at_its_old_line()
    {
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");

        var service = Service("abc123");

        await service.FindAsync(_repository, "starstats", "Widget");

        WriteSource("src/Widget.cs", "public sealed class Gizmo", "{", "}");

        var found = await service.FindAsync(_repository, "starstats", "Widget");

        // Nothing in the tree is called Widget any more, and the rescan
        // confirms it rather than the cache asserting it.
        found.Value!.Rebuilt.Should().BeTrue();
        found.Value!.Matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Outside_a_repository_every_lookup_is_a_scan_and_nothing_is_cached()
    {
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");

        var service = Service(head: null);

        var first = await service.FindAsync(_repository, "starstats", "Widget");
        var second = await service.FindAsync(_repository, "starstats", "Widget");

        first.Value!.Head.Should().BeNull();
        second.Value!.FromCache.Should().BeFalse();
        second.Value!.Matches.Should().ContainSingle();

        File.Exists(new SymbolIndexCache(_cacheRoot).PathFor("starstats", _repository)).Should().BeFalse();
    }

    [Fact]
    public async Task A_rescan_reads_the_tree_whatever_the_commit()
    {
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");

        var service = Service("abc123");

        await service.FindAsync(_repository, "starstats", "Widget");

        var found = await service.FindAsync(_repository, "starstats", "Widget", rescan: true);

        found.Value!.FromCache.Should().BeFalse();
    }

    [Fact]
    public async Task A_blank_name_and_a_missing_repository_are_refused()
    {
        var service = Service("abc123");

        (await service.FindAsync(_repository, "starstats", "  ")).Failed.Should().BeTrue();

        (await service.FindAsync(Path.Combine(_root, "nowhere"), "starstats", "Widget"))
            .Failed.Should().BeTrue();
    }

    [Fact]
    public async Task A_refresh_replaces_one_file_entries_and_leaves_the_rest()
    {
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");
        WriteSource("src/Gadget.cs", "public sealed class Gadget", "{", "}");

        var service = Service("abc123");

        await service.FindAsync(_repository, "starstats", "Widget");

        // The edit adds a type and a member; nothing else in the tree moves.
        WriteSource(
            "src/Widget.cs",
            "public sealed class Widget",
            "{",
            "    public void Turn() { }",
            "}",
            "public sealed class Gizmo { }");

        var refreshed = await service.RefreshAsync(
            _repository, "starstats", [Path.Combine(_repository, "src", "Widget.cs")]);

        refreshed.Succeeded.Should().BeTrue(refreshed.Error);
        refreshed.Value!.Built.Should().BeFalse();
        refreshed.Value!.Files.Should().ContainSingle()
            .Which.Should().Be(new SymbolRefreshedFile("src/Widget.cs", 1, 3));
        refreshed.Value!.Indexed.Should().Be(4);

        // What the index now says, read straight from the cache: the new name
        // is there without a rescan, and the untouched file is untouched.
        var cached = await new SymbolIndexCache(_cacheRoot).ReadAsync("starstats", _repository, "abc123");

        cached!.Select(symbol => (symbol.File, symbol.Name)).Should().BeEquivalentTo(
        [
            ("src/Gadget.cs", "Gadget"),
            ("src/Widget.cs", "Widget"),
            ("src/Widget.cs", "Turn"),
            ("src/Widget.cs", "Gizmo"),
        ]);

        var found = await service.FindAsync(_repository, "starstats", "Gizmo");

        found.Value!.FromCache.Should().BeTrue();
        found.Value!.Rebuilt.Should().BeFalse();
    }

    [Fact]
    public async Task A_refresh_reports_the_map_lines_that_changed_and_only_those()
    {
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");
        WriteSource("src/Gadget.cs", "public sealed class Gadget", "{", "}");
        WriteSource("tools/Run.cs", "public sealed class Run", "{", "}");

        var service = Service("abc123");

        await service.FindAsync(_repository, "starstats", "Widget");

        // A member added: the file changes, the map does not.
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "    public void Turn() { }", "}");

        var quiet = await service.RefreshAsync(_repository, "starstats", ["src/Widget.cs"]);

        quiet.Value!.Changes.Should().BeEmpty();

        // A type added: the directory's line reads differently, and the
        // untouched directory is not mentioned.
        WriteSource("src/Widget.cs", "public sealed class Widget { }", "public sealed class Gizmo { }");

        var changed = await service.RefreshAsync(_repository, "starstats", ["src/Widget.cs"]);

        changed.Value!.Changes.Should().ContainSingle()
            .Which.Should().Be(new SymbolMapChange(
                "src",
                "- `src` — 2 type(s): Gadget, Widget",
                "- `src` — 3 type(s): Gadget, Widget, Gizmo"));

        // The last type in a directory gone: the line is gone with it.
        File.Delete(Path.Combine(_repository, "tools", "Run.cs"));

        var gone = await service.RefreshAsync(_repository, "starstats", ["tools/Run.cs"]);

        gone.Value!.Changes.Should().ContainSingle()
            .Which.Should().Be(new SymbolMapChange("tools", "- `tools` — 1 type(s): Run", null));
    }

    [Fact]
    public async Task The_commit_is_read_from_the_git_directory_before_git_is_asked()
    {
        // A real repository laid out by hand, and a head reader that would be
        // the fallback: it is never reached, so the fallback answer is never
        // the key.
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");
        Directory.CreateDirectory(Path.Combine(_repository, ".git", "refs", "heads"));
        File.WriteAllText(Path.Combine(_repository, ".git", "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(
            Path.Combine(_repository, ".git", "refs", "heads", "main"),
            "c33ae1c1f74c8bfb2eef2b14a8762379b2ccfe23\n");

        var asked = 0;

        var service = new SymbolIndexService(
            (_, _) =>
            {
                asked++;

                return Task.FromResult<string?>("fallback");
            },
            new SymbolIndexCache(_cacheRoot));

        var found = await service.FindAsync(_repository, "starstats", "Widget");

        found.Value!.Head.Should().Be("c33ae1c1f74c8bfb2eef2b14a8762379b2ccfe23");
        asked.Should().Be(0);

        // And with the files gone, the fallback is what answers.
        Directory.Delete(Path.Combine(_repository, ".git"), recursive: true);

        (await service.FindAsync(_repository, "starstats", "Widget")).Value!.Head.Should().Be("fallback");
        asked.Should().Be(1);
    }

    [Fact]
    public async Task A_deleted_file_loses_its_entries_and_a_dry_run_changes_nothing()
    {
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");

        var service = Service("abc123");

        await service.FindAsync(_repository, "starstats", "Widget");
        File.Delete(Path.Combine(_repository, "src", "Widget.cs"));

        var preview = await service.RefreshAsync(_repository, "starstats", ["src/Widget.cs"], dryRun: true);

        preview.Value!.Files.Single().Should().Be(new SymbolRefreshedFile("src/Widget.cs", 1, 0));
        (await new SymbolIndexCache(_cacheRoot).ReadAsync("starstats", _repository, "abc123"))
            .Should().HaveCount(1, "a dry run writes nothing");

        var applied = await service.RefreshAsync(_repository, "starstats", ["src/Widget.cs"]);

        applied.Value!.Indexed.Should().Be(0);
        (await new SymbolIndexCache(_cacheRoot).ReadAsync("starstats", _repository, "abc123")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_refresh_with_no_index_builds_one_and_says_so()
    {
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");

        var service = Service("abc123");

        var refreshed = await service.RefreshAsync(_repository, "starstats", ["src/Widget.cs"]);

        refreshed.Value!.Built.Should().BeTrue();
        refreshed.Value!.Files.Single().After.Should().Be(1);

        (await service.FindAsync(_repository, "starstats", "Widget")).Value!.FromCache.Should().BeTrue();
    }

    [Fact]
    public async Task A_file_outside_the_repository_is_refused_and_no_repository_is_a_no_op()
    {
        WriteSource("src/Widget.cs", "public sealed class Widget", "{", "}");

        var outside = await Service("abc123").RefreshAsync(
            _repository, "starstats", [Path.Combine(_root, "elsewhere.cs")]);

        outside.Failed.Should().BeTrue();

        var none = await Service(head: null).RefreshAsync(_repository, "starstats", ["src/Widget.cs"]);

        none.Succeeded.Should().BeTrue();
        none.Value!.Head.Should().BeNull();
        none.Value!.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task An_index_from_an_older_scanner_is_not_trusted()
    {
        // The commit keys the index to a tree; the format keys it to a
        // scanner. An upgrade that reads another language would otherwise
        // keep answering from the old scan until the commit moved.
        var cache = new SymbolIndexCache(_cacheRoot);

        await cache.WriteAsync("starstats", _repository, "abc123", [Type("Widget", "src/Widget.cs")]);

        var path = cache.PathFor("starstats", _repository);
        var text = await File.ReadAllTextAsync(path);

        text.Should().Contain("\"Format\":" + SymbolIndexCache.Format);
        text.Should().Contain("\"Kind\":\"Type\"", "an enum stored by name survives a reorder");

        await File.WriteAllTextAsync(path, text.Replace("\"Format\":" + SymbolIndexCache.Format, "\"Format\":1"));

        (await cache.ReadAsync("starstats", _repository, "abc123")).Should().BeNull();
    }

    [Fact]
    public async Task Two_trees_of_one_project_keep_separate_indexes()
    {
        // A linked worktree and the primary checkout are one project at two
        // commits; one file between them would be rebuilt on every other
        // lookup.
        var other = Path.Combine(_root, "worktree");
        var cache = new SymbolIndexCache(_cacheRoot);

        cache.PathFor("starstats", _repository).Should().NotBe(cache.PathFor("starstats", other));

        await cache.WriteAsync("starstats", _repository, "abc123", [Type("Widget", "src/Widget.cs")]);
        await cache.WriteAsync("starstats", other, "def456", [Type("Gadget", "src/Gadget.cs")]);

        (await cache.ReadAsync("starstats", _repository, "abc123"))!.Single().Name.Should().Be("Widget");
        (await cache.ReadAsync("starstats", other, "def456"))!.Single().Name.Should().Be("Gadget");
    }

    [Fact]
    public async Task A_refresh_spelled_in_another_case_replaces_rather_than_duplicates()
    {
        // The index stores paths as the directory listing gave them; a hook
        // hands over the path as the agent typed it. On a case-insensitive
        // file system those can differ and still be one file.
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        WriteSource("src/Core/Widget.cs", "public sealed class Widget", "{", "}");

        var service = Service("abc123");

        await service.FindAsync(_repository, "starstats", "Widget");

        WriteSource("src/Core/Widget.cs", "public sealed class Widget", "{", "    public void Turn() { }", "}");

        var refreshed = await service.RefreshAsync(_repository, "starstats", ["SRC/core/widget.CS"]);

        refreshed.Value!.Files.Single().Should().Be(new SymbolRefreshedFile("src/Core/Widget.cs", 1, 2));
        refreshed.Value!.Changes.Should().BeEmpty("the directory is the same directory");

        var cached = await new SymbolIndexCache(_cacheRoot).ReadAsync("starstats", _repository, "abc123");

        cached!.Select(symbol => symbol.File).Distinct().Should().ContainSingle()
            .Which.Should().Be("src/Core/Widget.cs");
        cached.Should().HaveCount(2);
    }

    [Fact]
    public async Task Parallel_refreshes_of_different_files_all_land()
    {
        // An agent's parallel edits run their hooks in parallel. Without a
        // lock, two read-patch-write cycles interleave and one patch is lost.
        WriteSource("src/Seed.cs", "public sealed class Seed", "{", "}");

        var service = Service("abc123");

        await service.FindAsync(_repository, "starstats", "Seed");

        var files = Enumerable.Range(0, 8).Select(i => $"src/Added{i}.cs").ToList();

        foreach (var (file, i) in files.Select((file, i) => (file, i)))
        {
            WriteSource(file, $"public sealed class Added{i}", "{", "}");
        }

        var results = await Task.WhenAll(
            files.Select(file => service.RefreshAsync(_repository, "starstats", [file])));

        results.Should().OnlyContain(result => result.Succeeded);

        var cached = await new SymbolIndexCache(_cacheRoot).ReadAsync("starstats", _repository, "abc123");

        cached!.Select(symbol => symbol.Name).Should().BeEquivalentTo(
            ["Seed", .. Enumerable.Range(0, 8).Select(i => $"Added{i}")]);
    }

    [Fact]
    public async Task A_type_moved_to_a_new_file_is_found_rather_than_its_namesakes()
    {
        // The cache knows Widget in Old.cs and WidgetTests elsewhere. The
        // agent moves Widget to a new file without a hook. A lookup that
        // re-read Old.cs, found nothing, and settled for WidgetTests would
        // report the type gone.
        WriteSource("src/Old.cs", "public sealed class Widget", "{", "}");
        WriteSource("tests/WidgetTests.cs", "public sealed class WidgetTests", "{", "}");

        var service = Service("abc123");

        await service.FindAsync(_repository, "starstats", "Widget");

        File.Delete(Path.Combine(_repository, "src", "Old.cs"));
        WriteSource("src/New/Widget.cs", "public sealed class Widget", "{", "}");

        var found = await service.FindAsync(_repository, "starstats", "Widget");

        found.Value!.Rebuilt.Should().BeTrue();
        found.Value!.Matches.Select(match => match.Symbol.File).Should().Equal(
            "src/New/Widget.cs", "tests/WidgetTests.cs");
    }

    private SymbolIndexService Service(string? head) =>
        new((_, _) => Task.FromResult(head), new SymbolIndexCache(_cacheRoot));

    private void WriteSource(string relative, params string[] lines)
    {
        var path = Path.Combine(_repository, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }
}
