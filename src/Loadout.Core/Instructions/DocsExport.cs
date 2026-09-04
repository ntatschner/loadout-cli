using System.Text;

namespace Loadout.Core.Instructions;

/// <summary>Which document to produce.</summary>
public enum DocsExportType
{
    /// <summary>Every symbol, with its file, line and summary.</summary>
    Reference,

    /// <summary>The architecture and module map, for somebody who will change the code.</summary>
    Technical,

    /// <summary>A scaffold for somebody to write, ordered by what a reader wants to do.</summary>
    UserGuide,

    /// <summary>A compact index for a machine, plus an llms.txt digest.</summary>
    MachineIndex,
}

/// <summary>
/// Turns a scan of a codebase into a document, of four kinds.
/// </summary>
/// <remarks>
/// <para>
/// The four are not equally derivable, and pretending otherwise is how this
/// ships looking finished. The reference and the machine index fall out of the
/// code: they are always true, always dull, and never need a person. The
/// technical guide is half-derived and half the prose already sitting in the
/// doc comments. The user guide is barely derivable at all, because what
/// somebody wants to <em>do</em> is not in the source.
/// </para>
/// <para>
/// So the user guide is emitted as a scaffold that says it is one. Generating
/// it from symbols would produce something that reads like documentation,
/// teaches nobody anything, and — worst of the three — looks finished enough
/// that nobody writes the real thing.
/// </para>
/// </remarks>
public static class DocsExport
{
    /// <summary>Writes one document from a scanned codebase.</summary>
    /// <param name="type">Which document.</param>
    /// <param name="symbols">What the scan found.</param>
    /// <param name="projectName">What to call the thing being documented.</param>
    /// <param name="frontMatter">
    /// Whether to prefix the YAML header a static site generator reads.
    /// </param>
    public static string Write(
        DocsExportType type,
        IReadOnlyList<Symbol> symbols,
        string projectName,
        bool frontMatter = false)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        var document = Body(type, symbols, projectName);

        return frontMatter ? FrontMatter(type, projectName) + document : document;
    }

    /// <summary>
    /// The header a static site generator reads before the Markdown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Docusaurus and MkDocs both take plain Markdown, so this is the only
    /// thing standing between the export and dropping straight into either.
    /// Title and sidebar position and nothing else: every field beyond those
    /// is a generator's own, and guessing at them is how a file works in one
    /// tool and breaks in the next.
    /// </para>
    /// <para>
    /// The title is quoted because a project can be called anything, and a
    /// name with a colon in it turns a YAML document into a parse error at the
    /// other end.
    /// </para>
    /// </remarks>
    internal static string FrontMatter(DocsExportType type, string projectName)
    {
        var (title, position) = type switch
        {
            DocsExportType.Reference => ($"{projectName} reference", 3),
            DocsExportType.Technical => ($"How {projectName} is put together", 2),
            DocsExportType.UserGuide => ($"{projectName}: a guide", 1),
            _ => ($"{projectName} symbol index", 4),
        };

        return new StringBuilder()
            .AppendLine("---")
            .AppendLine($"title: \"{title.Replace("\"", "'", StringComparison.Ordinal)}\"")
            .AppendLine($"sidebar_position: {position}")
            .AppendLine("---")
            .AppendLine()
            .ToString();
    }

    private static string Body(
        DocsExportType type,
        IReadOnlyList<Symbol> symbols,
        string projectName)
    {
        return type switch
        {
            DocsExportType.Reference => Reference(symbols, projectName),
            DocsExportType.Technical => Technical(symbols, projectName),
            DocsExportType.UserGuide => UserGuide(symbols, projectName),
            _ => MachineIndex(symbols, projectName),
        };
    }

    private static string Reference(IReadOnlyList<Symbol> symbols, string name)
    {
        var text = new StringBuilder()
            .AppendLine($"# {name} reference")
            .AppendLine()
            .AppendLine(
                "Every public type and member, with where it is. Derived from a lexical scan of "
                + "the source: where it is wrong it has left something out rather than invented "
                + "it, so treat this as an index rather than an authority.")
            .AppendLine();

        foreach (var file in symbols.GroupBy(symbol => symbol.File).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.AppendLine($"## `{file.Key}`").AppendLine();

            foreach (var symbol in file)
            {
                text.AppendLine($"- **{symbol.Name}** — `{file.Key}:{symbol.Line}`");
                text.AppendLine($"  `{symbol.Signature}`");

                if (symbol.Summary.Length > 0)
                {
                    text.AppendLine($"  {symbol.Summary}");
                }
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static string Technical(IReadOnlyList<Symbol> symbols, string name)
    {
        var text = new StringBuilder()
            .AppendLine($"# {name}: how it is put together")
            .AppendLine()
            .AppendLine(
                "The module map is derived. Every sentence under a type is one already written "
                + "in its doc comment: the summary, then the opening paragraph of its remarks, "
                + "which is where this codebase puts the decision. What follows that paragraph "
                + "in the source is the evidence and the history, and it stays there. Nothing "
                + "here was composed for this document, so where a type explains itself badly, "
                + "it explains itself badly here too.")
            .AppendLine();

        foreach (var module in Modules(symbols))
        {
            text.AppendLine($"## {module.Key}").AppendLine();

            var described = module
                .Where(symbol => symbol.Kind == SymbolKind.Type && symbol.Summary.Length > 0)
                .ToList();

            if (described.Count == 0)
            {
                // Said rather than left blank. An empty section reads as a
                // module that does nothing; this one reads as a module nobody
                // has written about, which is the true and more useful thing.
                text.AppendLine("No type in this module carries a summary.").AppendLine();

                continue;
            }

            foreach (var symbol in described)
            {
                text.AppendLine($"### {symbol.Name}").AppendLine();
                text.AppendLine(symbol.Summary).AppendLine();

                if (symbol.Reasoning.Length > 0)
                {
                    // The opening paragraph of the remarks, which is where the
                    // decision is. What follows it in the source is the
                    // evidence and the history, which belong where somebody
                    // changing the code will meet them.
                    text.AppendLine(symbol.Reasoning).AppendLine();
                }
            }
        }

        return text.ToString();
    }

    private static string UserGuide(IReadOnlyList<Symbol> symbols, string name)
    {
        var text = new StringBuilder()
            .AppendLine($"# {name}: a guide")
            .AppendLine()
            .AppendLine("> **This is a scaffold, not a document.**")
            .AppendLine("> ")
            .AppendLine(
                "> What somebody wants to *do* is not in the source, so nothing below was "
                + "written for you. The headings come from the shape of the code, which is a "
                + "starting order and not a good one — a real guide is ordered by task, and the "
                + "code is ordered by module.")
            .AppendLine("> ")
            .AppendLine(
                "> Rewrite the headings as things a reader wants to achieve, delete the ones "
                + "that are only internals, and replace every TODO. Publishing this as it "
                + "stands would give somebody a document that reads like documentation and "
                + "teaches them nothing.")
            .AppendLine();

        foreach (var module in Modules(symbols))
        {
            text.AppendLine($"## {module.Key}").AppendLine();
            text.AppendLine("TODO: what does somebody use this for, and when?").AppendLine();

            foreach (var symbol in module
                .Where(symbol => symbol.Kind == SymbolKind.Type && symbol.Summary.Length > 0)
                .Take(8))
            {
                text.AppendLine($"- {symbol.Name}: {symbol.Summary}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static string MachineIndex(IReadOnlyList<Symbol> symbols, string name)
    {
        var text = new StringBuilder()
            .AppendLine($"# {name}")
            .AppendLine()
            .AppendLine(
                "> A map of this codebase for a reader that is not a person. The digest below "
                + "says where things are; the index after it says where each name is.")
            .AppendLine();

        // The digest half, in the shape llms.txt asks for: what the modules are
        // and what each holds, so a session can pick a file to open instead of
        // reading the tree. Written first because it is the part worth reading
        // when the index is too long to spend tokens on.
        text.AppendLine("## Modules").AppendLine();

        foreach (var module in Modules(symbols))
        {
            var types = module.Where(symbol => symbol.Kind == SymbolKind.Type).ToList();

            text.AppendLine(
                $"- `{module.Key}` — {types.Count} type(s): "
                + string.Join(", ", Named(types).Take(12))
                + (Named(types).Count > 12 ? ", and more" : string.Empty));
        }

        text.AppendLine().AppendLine("## Index").AppendLine();

        // Deliberately flat and uniform. This one is not read by a person, so
        // the grouping and prose that help elsewhere are only tokens here.
        foreach (var symbol in symbols)
        {
            text.AppendLine(
                $"{symbol.Name}\t{symbol.Kind.ToString().ToLowerInvariant()}\t"
                + $"{symbol.File}\t{symbol.Line}");
        }

        return text.ToString();
    }

    /// <summary>
    /// Distinct type names in a module, in order.
    /// </summary>
    /// <remarks>
    /// Nested types repeat: every command in this codebase carries its own
    /// Settings, so a raw list reads "Settings, ThingCommand, Settings,
    /// OtherCommand, Settings". The repetition says nothing and costs the
    /// tokens this digest exists to save.
    /// </remarks>
    private static List<string> Named(IEnumerable<Symbol> types) =>
    [
        .. types
            .Select(symbol => symbol.Name)
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>
    /// Symbols grouped by the directory that holds them.
    /// </summary>
    /// <remarks>
    /// The directory is the closest thing to a module a lexical scan can see,
    /// and in practice it is what people mean anyway: a namespace that does not
    /// match its folder is rare, and where it happens the folder is still where
    /// somebody would go looking.
    /// </remarks>
    private static IEnumerable<IGrouping<string, Symbol>> Modules(IReadOnlyList<Symbol> symbols) =>
        symbols
            .GroupBy(symbol =>
            {
                var directory = Path.GetDirectoryName(symbol.File)?.Replace('\\', '/');

                return directory is { Length: > 0 } ? directory : "(root)";
            })
            .OrderBy(group => group.Key, StringComparer.Ordinal);
}
