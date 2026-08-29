using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>What a new project should be made from.</summary>
/// <param name="Name">Display name, which becomes the slug.</param>
/// <param name="Template">Project to copy conventions from, or null for a bare one.</param>
/// <param name="Path">Where to create it, or null for this machine's clone root.</param>
internal sealed record NewProjectRequest(string Name, string? Template = null, string? Path = null);

/// <summary>
/// Asks what to create, so starting a project does not mean leaving the
/// launcher.
/// </summary>
/// <remarks>
/// <para>
/// The launcher could open, launch and inspect projects but never bring one
/// into being: every route in started at a terminal. That made the screen a
/// strict subset of the command line for the one operation somebody performs
/// when they have just had an idea.
/// </para>
/// <para>
/// The template list is the projects already registered, because a new service
/// almost always resembles one that exists — and copying its conventions is the
/// entire reason to start from a template rather than from nothing.
/// </para>
/// </remarks>
internal sealed class NewProjectDialog : Window
{
    /// <summary>The first entry, which means no template rather than one so named.</summary>
    internal const string NoTemplate = "(nothing — start bare)";

    private readonly TextField _name;
    private readonly TextField _path;
    private readonly ListView _template;
    private readonly IReadOnlyList<string> _templates;
    private readonly IApplication _application;

    /// <summary>What was asked for, or null when the dialog was dismissed.</summary>
    internal NewProjectRequest? Chosen { get; private set; }

    internal NewProjectDialog(IReadOnlyList<string> projectSlugs, IApplication application)
    {
        ArgumentNullException.ThrowIfNull(projectSlugs);
        ArgumentNullException.ThrowIfNull(application);

        _application = application;
        _templates = [NoTemplate, .. projectSlugs];

        Title = "New project";
        Width = Dim.Percent(70);
        Height = 17;
        BorderStyle = LineStyle.Rounded;

        Add(new Label { X = 1, Y = 0, Text = "What is it called?" });

        _name = new TextField { X = 1, Y = 1, Width = Dim.Fill(1) };

        Add(new Label { X = 1, Y = 3, Text = "Model it on" });

        _template = new ListView { X = 1, Y = 4, Width = Dim.Fill(1), Height = 6 };
        _template.SetSource(new ObservableCollection<string>([.. _templates]));
        _template.SelectedItem = 0;

        Add(new Label
        {
            X = 1,
            Y = 10,
            Text = "Copies its instructions, rules and agent settings. Never its memory.",
        });

        Add(new Label { X = 1, Y = 12, Text = "Where (blank for the usual place)" });

        _path = new TextField { X = 1, Y = 13, Width = Dim.Fill(1) };

        var create = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "C_reate", IsDefault = true };
        var cancel = new Button { X = Pos.Right(create) + 2, Y = Pos.AnchorEnd(1), Text = "Cance_l" };

        create.Accepting += (_, e) => { e.Handled = true; Accept(); };
        cancel.Accepting += (_, e) => { e.Handled = true; _application.RequestStop(this); };

        // Enter on the name is enough. A project with a name and no template is
        // a complete request, and the rest are refinements.
        _name.Accepting += (_, e) => { e.Handled = true; Accept(); };

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(_name, _template, _path, create, cancel);

        _name.SetFocus();
    }

    private void Accept()
    {
        var typed = _name.Text?.Trim();

        // Nothing to create without one, and a dialog that closed on an empty
        // name would look like it had worked.
        if (string.IsNullOrWhiteSpace(typed))
        {
            _name.SetFocus();

            return;
        }

        // Index zero is "start bare", which means no template rather than a
        // project called that.
        var template = _template.SelectedItem is int index && index > 0 && index < _templates.Count
            ? _templates[index]
            : null;

        var where = _path.Text?.Trim();

        Chosen = new NewProjectRequest(
            typed,
            template,
            string.IsNullOrWhiteSpace(where) ? null : where);

        _application.RequestStop(this);
    }
}
