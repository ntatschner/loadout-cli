namespace Loadout.Core.Starters;

/// <summary>
/// What a first project begins from: a handful of files, and a sentence on
/// what they are for.
/// </summary>
/// <param name="Id">What it is called on the command line.</param>
/// <param name="Name">What it is called in a question.</param>
/// <param name="Summary">One sentence somebody new to all of this can choose by.</param>
/// <param name="DefaultName">The project name offered when none is given.</param>
/// <param name="Page">
/// The file a browser opens to show the result, or null when there is nothing
/// to open yet, which is true of an idea nobody has described.
/// </param>
/// <param name="Files">Path, with forward slashes, to content. The README is written with the project's name.</param>
public sealed record Starter(
    string Id,
    string Name,
    string Summary,
    string DefaultName,
    string? Page,
    IReadOnlyDictionary<string, Func<string, string>> Files);

/// <summary>
/// The starters <c>loadout start</c> offers.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from <c>project new</c>'s templates, which copy one project's
/// conventions into another. A starter is something else: the first few files
/// of a project for somebody who has never had one, and nothing of anybody
/// else's.
/// </para>
/// <para>
/// Every starter needs only a browser. The first project is where somebody
/// finds out whether any of this works for them, and a missing runtime, a
/// package that would not install or a build step that failed would answer
/// that question wrongly. HTML and CSS open anywhere.
/// </para>
/// <para>
/// Compiled in rather than fetched, so a starter is the one that shipped with
/// this release and the guide works with no network.
/// </para>
/// </remarks>
public static class Starters
{
    /// <summary>The one offered first, and taken when nothing is said.</summary>
    public const string Default = "web-page";

    /// <summary>Every starter, in the order they are offered.</summary>
    public static IReadOnlyList<Starter> All { get; } =
    [
        new Starter(
            "web-page",
            "A web page",
            "One page about anything you like. Opens in your browser, nothing else to install.",
            "my-first-page",
            "index.html",
            new Dictionary<string, Func<string, string>>(StringComparer.Ordinal)
            {
                ["README.md"] = name => Readme(
                    name,
                    "A single web page. `index.html` is the page and `style.css` is how it looks. "
                    + "Open `index.html` in a browser to see it."),
                ["index.html"] = name => WebPage(name),
                ["style.css"] = _ => Style,
            }),

        new Starter(
            "checklist",
            "A checklist",
            "A list you can tick off: packing, chores, a plan for the week. Also just a browser page.",
            "my-checklist",
            "index.html",
            new Dictionary<string, Func<string, string>>(StringComparer.Ordinal)
            {
                ["README.md"] = name => Readme(
                    name,
                    "A checklist on a web page. `index.html` holds the list and `style.css` is how "
                    + "it looks. Open `index.html` in a browser to use it."),
                ["index.html"] = name => Checklist(name),
                ["style.css"] = _ => Style,
            }),

        new Starter(
            "own-idea",
            "My own idea",
            "An empty project and a README. The agent helps you work out what to build.",
            "my-project",
            null,
            new Dictionary<string, Func<string, string>>(StringComparer.Ordinal)
            {
                ["README.md"] = name => Readme(
                    name,
                    "Nothing here yet. The planning session works out what this project is, and "
                    + "writes it down in `PLAN.md`."),
            }),
    ];

    /// <summary>A starter by id, or null when there is none of that name.</summary>
    public static Starter? Find(string id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string Readme(string name, string what) =>
        $"# {name}\n\n{what}\n\nMade with `loadout start`.\n";

    private static string WebPage(string name) =>
        $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{Html(name)}}</title>
          <link rel="stylesheet" href="style.css">
        </head>
        <body>
          <main>
            <h1>{{Html(name)}}</h1>
            <p>This is your page. Everything you see here comes from <code>index.html</code>,
              and the way it looks comes from <code>style.css</code>.</p>
            <p>Change a word in <code>index.html</code>, save it, and reload this page to see it change.</p>
          </main>
        </body>
        </html>

        """;

    private static string Checklist(string name) =>
        $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{Html(name)}}</title>
          <link rel="stylesheet" href="style.css">
        </head>
        <body>
          <main>
            <h1>{{Html(name)}}</h1>
            <ul class="checklist">
              <li><label><input type="checkbox"> Open this page in a browser</label></li>
              <li><label><input type="checkbox"> Change one of these items in <code>index.html</code></label></li>
              <li><label><input type="checkbox"> Reload the page and see it change</label></li>
            </ul>
          </main>
        </body>
        </html>

        """;

    private const string Style =
        """
        body {
          font-family: system-ui, sans-serif;
          line-height: 1.5;
          margin: 0;
          background: #f7f7f5;
          color: #222;
        }

        main {
          max-width: 40rem;
          margin: 3rem auto;
          padding: 0 1rem;
        }

        h1 {
          font-size: 2rem;
        }

        .checklist {
          list-style: none;
          padding: 0;
        }

        .checklist li {
          padding: 0.25rem 0;
        }

        """;

    /// <summary>A project name made safe to put in a page, since it is typed by a person.</summary>
    private static string Html(string text) => System.Net.WebUtility.HtmlEncode(text);
}
