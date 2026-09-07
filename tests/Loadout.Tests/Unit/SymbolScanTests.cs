using FluentAssertions;
using Loadout.Core.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// One grammar per language, read a line at a time.
/// </summary>
/// <remarks>
/// Each test is a small file in one language and the symbols a person skimming
/// it would list: the declarations, at their lines, with the first sentence of
/// whatever documents them. The negatives matter as much — control flow that
/// has the shape of a method, a private thing the language marks as such — because
/// a scan that invents a declaration is worse than one that misses it.
/// </remarks>
public sealed class SymbolScanTests
{
    private static IReadOnlyList<Symbol> Scan(string file, params string[] lines) =>
        [.. SymbolScan.InFile(lines, file)];

    private static (SymbolKind Kind, string Name, int Line, string Summary) Row(Symbol symbol) =>
        (symbol.Kind, symbol.Name, symbol.Line, symbol.Summary);

    [Fact]
    public void The_language_comes_from_the_extension_and_an_unknown_one_is_skipped()
    {
        SymbolLanguages.For("src/a.PY")!.Id.Should().Be("python");
        SymbolLanguages.For("src/a.tsx")!.Id.Should().Be("typescript");
        SymbolLanguages.For("src/a.cob").Should().BeNull();
        SymbolLanguages.For("Makefile").Should().BeNull();

        Scan("src/a.cob", "IDENTIFICATION DIVISION.").Should().BeEmpty();
    }

    [Fact]
    public void Every_language_has_a_specialist_style_id_and_a_name()
    {
        SymbolLanguages.All.Select(l => l.Id).Should().OnlyHaveUniqueItems();
        SymbolLanguages.All.SelectMany(l => l.Extensions).Should().OnlyHaveUniqueItems();
        SymbolLanguages.SpecialistIds.Should().Contain("language.csharp").And.Contain("language.go");
        SymbolLanguages.Names.Should().Contain("Rust").And.Contain("PowerShell");
    }

    [Fact]
    public void TypeScript_declarations_functions_arrows_and_methods()
    {
        var symbols = Scan(
            "src/app.ts",
            "/**",
            " * A widget. It turns.",
            " * @remarks none",
            " */",
            "export class Widget {",
            "  private count = 0;",
            "  /** Turns it once. */",
            "  public turn(times: number): void {",
            "    if (times > 0) {",
            "    }",
            "    for (const x of []) {",
            "    }",
            "  }",
            "  get size() {",
            "    return 1;",
            "  }",
            "}",
            "export interface Shape { area(): number }",
            "export type Id = string;",
            "export async function load(id: Id): Promise<Shape> {",
            "}",
            "const helper = (a: number) => a + 1;",
            "export const ready = async () => {",
            "};",
            "widget.turn(1);");

        symbols.Select(Row).Should().Equal(
            (SymbolKind.Type, "Widget", 5, "A widget."),
            (SymbolKind.Member, "turn", 8, "Turns it once."),
            (SymbolKind.Member, "size", 14, string.Empty),
            (SymbolKind.Type, "Shape", 18, string.Empty),
            (SymbolKind.Type, "Id", 19, string.Empty),
            (SymbolKind.Member, "load", 20, string.Empty),
            (SymbolKind.Member, "helper", 22, string.Empty),
            (SymbolKind.Member, "ready", 23, string.Empty));

        symbols.Should().OnlyContain(symbol => symbol.Language == "typescript");
    }

    [Fact]
    public void A_call_with_a_callback_is_not_a_method()
    {
        // describe("thing", function () { has a method's shape, and a test
        // suite would index every case. A parameter list holds no string and
        // no function; either says call.
        Scan(
            "src/app.test.ts",
            "describe(\"thing\", function () {",
            "  beforeEach(function() {",
            "  });",
            "  it(\"works\", (done) => {",
            "  });",
            "  it('also', async function () {",
            "  });",
            "  $(function () {",
            "  });",
            "});",
            "class Real {",
            "  method(a: number, b = 'x') {",
            "  }",
            "  other(cb: () => void) {",
            "  }",
            "}")
            .Select(Row).Should().Equal(
                (SymbolKind.Type, "Real", 11, string.Empty));
    }

    [Fact]
    public void A_one_line_def_takes_no_docstring_from_the_block_below()
    {
        Scan(
            "pkg/widget.py",
            "class Widget(Base): pass",
            "def name(self): return self._name",
            "def save(self):",
            "    \"\"\"Persist to disk.\"\"\"",
            "def kind(self) -> dict[str, int]:  # trailing comment",
            "    \"\"\"Says the kind.\"\"\"",
            "def load(",
            "    self,",
            "    id: str = ':',",
            ") -> None:",
            "    'Loads one.'")
            .Select(Row).Should().Equal(
                (SymbolKind.Type, "Widget", 1, string.Empty),
                (SymbolKind.Member, "name", 2, string.Empty),
                (SymbolKind.Member, "save", 3, "Persist to disk."),
                (SymbolKind.Member, "kind", 5, "Says the kind."),
                (SymbolKind.Member, "load", 7, "Loads one."));
    }

    [Fact]
    public void Python_reads_docstrings_below_and_skips_underscored_names()
    {
        var symbols = Scan(
            "pkg/widget.py",
            "class Widget(Base):",
            "    \"\"\"A widget.",
            "",
            "    Longer description.",
            "    \"\"\"",
            "",
            "    def turn(self, times: int) -> None:",
            "        \"\"\"",
            "        Turns it once.",
            "        \"\"\"",
            "        pass",
            "",
            "    def _hidden(self):",
            "        pass",
            "",
            "    async def load(",
            "        self,",
            "        id: str,",
            "    ) -> None:",
            "        'Loads one.'",
            "",
            "def helper(): pass",
            "class _Private: pass");

        symbols.Select(Row).Should().Equal(
            (SymbolKind.Type, "Widget", 1, "A widget."),
            (SymbolKind.Member, "turn", 7, "Turns it once."),
            (SymbolKind.Member, "load", 16, "Loads one."),
            (SymbolKind.Member, "helper", 22, string.Empty));
    }

    [Fact]
    public void Go_reads_exported_names_and_the_comment_directly_above()
    {
        var symbols = Scan(
            "pkg/widget.go",
            "package widget",
            "",
            "// Widget turns. It is the whole point.",
            "type Widget struct {",
            "}",
            "",
            "type hidden struct{}",
            "",
            "// Turn turns it once.",
            "//",
            "// Not twice.",
            "func (w *Widget) Turn(times int) error {",
            "\treturn nil",
            "}",
            "",
            "func helper() {}",
            "",
            "// A stray comment.",
            "",
            "func New() *Widget { return nil }",
            "type Shape interface{ Area() float64 }");

        symbols.Select(Row).Should().Equal(
            (SymbolKind.Type, "Widget", 4, "Widget turns."),
            (SymbolKind.Member, "Turn", 12, "Turn turns it once."),
            (SymbolKind.Member, "New", 20, string.Empty),
            (SymbolKind.Type, "Shape", 21, string.Empty));
    }

    [Fact]
    public void Rust_reads_pub_items_and_triple_slash_docs()
    {
        var symbols = Scan(
            "src/lib.rs",
            "//! Crate docs are not a symbol's.",
            "",
            "/// A widget that turns.",
            "#[derive(Debug)]",
            "pub struct Widget;",
            "",
            "struct Hidden;",
            "",
            "impl Widget {",
            "    /// Turns it once. Never twice.",
            "    pub fn turn(&self) {}",
            "    fn hidden(&self) {}",
            "    pub(crate) async fn load() {}",
            "}",
            "",
            "pub trait Shape { fn area(&self) -> f64; }",
            "pub enum Kind { A, B }",
            "pub mod inner {}");

        symbols.Select(Row).Should().Equal(
            (SymbolKind.Type, "Widget", 5, "A widget that turns."),
            (SymbolKind.Member, "turn", 11, "Turns it once."),
            (SymbolKind.Member, "load", 13, string.Empty),
            (SymbolKind.Type, "Shape", 16, string.Empty),
            (SymbolKind.Type, "Kind", 17, string.Empty),
            (SymbolKind.Type, "inner", 18, string.Empty));
    }

    [Fact]
    public void Java_reads_non_private_members_and_javadoc()
    {
        var symbols = Scan(
            "src/Widget.java",
            "package demo;",
            "",
            "/**",
            " * A widget.",
            " *",
            " * <p>It turns.",
            " */",
            "@Service",
            "public class Widget extends Base implements Shape {",
            "    private int count;",
            "",
            "    /** Turns it once. */",
            "    @Override",
            "    public void turn(int times) throws IOException {",
            "        if (times > 0) {",
            "        }",
            "        return;",
            "    }",
            "",
            "    private void hidden() {}",
            "",
            "    static <T> List<T> of(T item) {",
            "        return new ArrayList<>();",
            "    }",
            "",
            "    abstract Shape shape();",
            "}",
            "",
            "interface Shape { double area(); }",
            "public record Point(int x, int y) {}",
            "public enum Kind { A, B }");

        symbols.Select(Row).Should().Equal(
            (SymbolKind.Type, "Widget", 9, "A widget."),
            (SymbolKind.Member, "turn", 14, "Turns it once."),
            (SymbolKind.Member, "of", 22, string.Empty),
            (SymbolKind.Member, "shape", 26, string.Empty),
            (SymbolKind.Type, "Shape", 29, string.Empty),
            (SymbolKind.Type, "Point", 30, string.Empty),
            (SymbolKind.Type, "Kind", 31, string.Empty));
    }

    [Fact]
    public void Kotlin_and_Swift_read_their_declarations()
    {
        Scan(
            "src/Widget.kt",
            "/** A widget. */",
            "data class Widget(val id: Int) : Base() {",
            "    fun turn(times: Int) {}",
            "    private fun hidden() {}",
            "    suspend fun load(): Widget? = null",
            "}",
            "object Registry",
            "interface Shape",
            "fun String.shout() = uppercase()")
            .Select(Row).Should().Equal(
                (SymbolKind.Type, "Widget", 2, "A widget."),
                (SymbolKind.Member, "turn", 3, string.Empty),
                (SymbolKind.Member, "load", 5, string.Empty),
                (SymbolKind.Type, "Registry", 7, string.Empty),
                (SymbolKind.Type, "Shape", 8, string.Empty),
                (SymbolKind.Member, "shout", 9, string.Empty));

        Scan(
            "Sources/Widget.swift",
            "/// A widget.",
            "public struct Widget: Shape {",
            "    /// Turns it once.",
            "    public func turn(times: Int) {}",
            "    private func hidden() {}",
            "    static func make() -> Widget { Widget() }",
            "}",
            "protocol Shape {}",
            "extension Widget {}",
            "@MainActor final class Controller {}")
            .Select(Row).Should().Equal(
                (SymbolKind.Type, "Widget", 2, "A widget."),
                (SymbolKind.Member, "turn", 4, "Turns it once."),
                (SymbolKind.Member, "make", 6, string.Empty),
                (SymbolKind.Type, "Shape", 8, string.Empty),
                (SymbolKind.Type, "Widget", 9, string.Empty),
                (SymbolKind.Type, "Controller", 10, string.Empty));
    }

    [Fact]
    public void Ruby_PHP_and_C_read_their_declarations()
    {
        Scan(
            "lib/widget.rb",
            "# A widget.",
            "# It turns.",
            "class Widget < Base",
            "  # Turns it once.",
            "  def turn(times)",
            "  end",
            "  def self.build",
            "  end",
            "  def empty?",
            "  end",
            "end",
            "module Shapes; end")
            .Select(Row).Should().Equal(
                (SymbolKind.Type, "Widget", 3, "A widget."),
                (SymbolKind.Member, "turn", 5, "Turns it once."),
                (SymbolKind.Member, "build", 7, string.Empty),
                (SymbolKind.Member, "empty?", 9, string.Empty),
                (SymbolKind.Type, "Shapes", 12, string.Empty));

        Scan(
            "src/Widget.php",
            "<?php",
            "/** A widget. */",
            "final class Widget extends Base {",
            "    /** Turns it once. */",
            "    public function turn(int $times): void {}",
            "    private function hidden() {}",
            "    public static function &build() {}",
            "}",
            "interface Shape {}",
            "function helper() {}")
            .Select(Row).Should().Equal(
                (SymbolKind.Type, "Widget", 3, "A widget."),
                (SymbolKind.Member, "turn", 5, "Turns it once."),
                (SymbolKind.Member, "build", 7, string.Empty),
                (SymbolKind.Type, "Shape", 9, string.Empty),
                (SymbolKind.Member, "helper", 10, string.Empty));

        Scan(
            "src/widget.c",
            "#include <stdio.h>",
            "",
            "/* A widget. */",
            "struct widget {",
            "    int count;",
            "};",
            "typedef enum kind { A, B } kind_t;",
            "",
            "/** Turns it once. */",
            "static int widget_turn(struct widget *w, int times)",
            "{",
            "    if (times > 0) {",
            "        return 0;",
            "    }",
            "    printf(\"%d\", times);",
            "    return 1;",
            "}",
            "",
            "char *widget_name(const struct widget *w);",
            "int main(void) { return 0; }")
            .Select(Row).Should().Equal(
                (SymbolKind.Type, "widget", 4, "A widget."),
                (SymbolKind.Type, "kind", 7, string.Empty),
                (SymbolKind.Member, "widget_turn", 10, "Turns it once."),
                (SymbolKind.Member, "widget_name", 19, string.Empty),
                (SymbolKind.Member, "main", 20, string.Empty));
    }

    [Fact]
    public void PowerShell_reads_functions_and_the_synopsis_of_a_help_block()
    {
        Scan(
            "scripts/Widget.ps1",
            "<#",
            ".SYNOPSIS",
            "    Turns a widget.",
            ".DESCRIPTION",
            "    Longer text.",
            "#>",
            "function Invoke-Turn {",
            "    param([int]$Times)",
            "}",
            "",
            "# Builds one.",
            "function New-Widget { }",
            "class Widget { }",
            "filter Select-Widget { }")
            .Select(Row).Should().Equal(
                (SymbolKind.Member, "Invoke-Turn", 7, "Turns a widget."),
                (SymbolKind.Member, "New-Widget", 12, "Builds one."),
                (SymbolKind.Type, "Widget", 13, string.Empty),
                (SymbolKind.Member, "Select-Widget", 14, string.Empty));
    }

    [Fact]
    public void Shell_reads_functions_in_both_spellings_and_not_the_shebang()
    {
        Scan(
            "scripts/build.sh",
            "#!/usr/bin/env bash",
            "set -euo pipefail",
            "",
            "# Builds the thing.",
            "build() {",
            "  echo building",
            "}",
            "",
            "function publish {",
            "  echo publishing",
            "}",
            "",
            "#!not a function",
            "clean-up () {",
            "}")
            .Select(Row).Should().Equal(
                (SymbolKind.Member, "build", 5, "Builds the thing."),
                (SymbolKind.Member, "publish", 9, string.Empty),
                (SymbolKind.Member, "clean-up", 14, string.Empty));
    }

    [Fact]
    public void Terraform_names_a_resource_the_way_the_language_addresses_it()
    {
        Scan(
            "infra/main.tf",
            "# The bucket everything lands in.",
            "resource \"aws_s3_bucket\" \"artifacts\" {",
            "  bucket = var.name",
            "}",
            "data \"aws_caller_identity\" \"current\" {}",
            "module \"network\" {",
            "  source = \"./network\"",
            "}",
            "variable \"name\" {",
            "  type = string",
            "}",
            "output \"bucket_arn\" {",
            "  value = aws_s3_bucket.artifacts.arn",
            "}")
            .Select(Row).Should().Equal(
                (SymbolKind.Type, "aws_s3_bucket.artifacts", 2, "The bucket everything lands in."),
                (SymbolKind.Type, "aws_caller_identity.current", 5, string.Empty),
                (SymbolKind.Type, "network", 6, string.Empty),
                (SymbolKind.Member, "name", 9, string.Empty),
                (SymbolKind.Member, "bucket_arn", 12, string.Empty));
    }

    [Fact]
    public void SQL_reads_create_statements_whatever_their_case()
    {
        Scan(
            "db/schema.sql",
            "-- Everything a widget is.",
            "CREATE TABLE IF NOT EXISTS widgets (",
            "    id serial primary key",
            ");",
            "create or replace view active_widgets as select * from widgets;",
            "CREATE UNIQUE INDEX widgets_name_idx ON widgets (name);",
            "-- Turns one.",
            "CREATE OR REPLACE FUNCTION turn_widget(id int) RETURNS void AS $$",
            "$$ LANGUAGE sql;",
            "CREATE PROCEDURE dbo.Rebuild AS BEGIN END;",
            "select * from widgets;")
            .Select(Row).Should().Equal(
                (SymbolKind.Type, "widgets", 2, "Everything a widget is."),
                (SymbolKind.Type, "active_widgets", 5, string.Empty),
                (SymbolKind.Member, "widgets_name_idx", 6, string.Empty),
                (SymbolKind.Member, "turn_widget", 8, "Turns one."),
                (SymbolKind.Member, "dbo.Rebuild", 10, string.Empty));
    }

    [Fact]
    public void A_scan_of_a_tree_reads_every_language_it_knows_and_nothing_else()
    {
        var root = Path.Combine(Path.GetTempPath(), "loadout-scan-" + Guid.NewGuid().ToString("N"));

        try
        {
            Write(root, "src/Widget.cs", "public sealed class Widget", "{", "}");
            Write(root, "web/app.ts", "export class App {}");
            Write(root, "tools/run.py", "def run(): pass");
            Write(root, "notes/README.md", "# class NotCode");
            Write(root, "node_modules/dep/index.js", "export class Ignored {}");

            // A hidden directory is tooling, whatever it is called: one
            // project kept a virtual environment under .tmp and its index
            // filled with other people's packages.
            Write(root, ".tmp/py313/dep/mod.py", "class Hidden: pass");
            Write(root, "env/lib/site-packages/dep/mod.py", "class Installed: pass");

            var symbols = SymbolScan.Scan(root);

            symbols.Select(symbol => (symbol.Language, symbol.Name)).Should().BeEquivalentTo(
            [
                ("csharp", "Widget"),
                ("typescript", "App"),
                ("python", "run"),
            ]);

            symbols.Select(symbol => symbol.File).Should().OnlyContain(file => !file.Contains('\\'));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string root, string relative, params string[] lines)
    {
        var path = Path.Combine(root, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }
}
