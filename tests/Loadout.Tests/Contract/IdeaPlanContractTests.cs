using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// <c>loadout idea plan</c> through the real executable: the preview before
/// acceptance, held to by <c>idea accept --plan-hash</c>, then the written file,
/// then a plan whose file has gone.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class IdeaPlanContractTests
{
    private const string Id = "status-page";

    [BuiltCliFact]
    public async Task Idea_plan_shows_the_preview_then_the_written_file_then_says_when_it_has_gone()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_LOCAL"] = "1";

        var repository = Path.Combine(loadout.Home, "homelab");
        await Repository(repository);

        var added = await loadout.RunAsync("project", "add", repository, "--json");
        added.ExitCode.Should().Be(0, added.StandardError);
        var slug = added.Json().GetProperty("id").GetString()!;

        var status = await loadout.RunAsync("workspace", "status", "--json");
        var workspace = status.Json().GetProperty("localPath").GetString()!;
        await SeedAsync(workspace);

        var preview = await loadout.RunAsync("idea", "plan", Id, "--global", "--json", "--done-when", "every host is listed");
        preview.ExitCode.Should().Be(0, preview.StandardError);
        var read = preview.Json();
        read.GetProperty("state").GetString().Should().Be("preview");
        var markdown = read.GetProperty("markdown").GetString()!;
        var hash = read.GetProperty("hash").GetString()!;
        markdown.Should().Contain("# Status page").And.Contain("- every host is listed");

        var printed = await loadout.RunAsync("idea", "plan", Id, "--global", "--done-when", "every host is listed");
        printed.ExitCode.Should().Be(0, printed.StandardError);
        printed.StandardOutput.Should().Be(markdown, "the terminal prints the same document, unwrapped");

        var stale = await loadout.RunAsync(
            "idea", "accept", Id, "--global", "--to", slug, "--non-interactive", "--plan-hash", "0000000000000000");
        stale.ExitCode.Should().Be(12, "a plan that is not the one read is refused as changed since read");
        Directory.GetFiles(workspace, $"{Id}.md", SearchOption.AllDirectories).Should().BeEmpty();

        var accepted = await loadout.RunAsync(
            "idea", "accept", Id, "--global", "--to", slug, "--non-interactive",
            "--done-when", "every host is listed", "--plan-hash", hash);
        accepted.ExitCode.Should().Be(0, accepted.StandardError + accepted.StandardOutput);

        var written = await loadout.RunAsync("idea", "plan", Id, "--project", slug, "--json");
        written.ExitCode.Should().Be(0, written.StandardError);
        written.Json().GetProperty("state").GetString().Should().Be("written");
        written.Json().GetProperty("markdown").GetString().Should().Be(markdown);
        written.Json().GetProperty("changed").GetBoolean().Should().BeFalse();
        var path = written.Json().GetProperty("path").GetString()!;
        path.Should().Be($"projects/{slug}/ideas/{Id}.md");

        File.Delete(Path.Combine(workspace, path));

        var gone = await loadout.RunAsync("idea", "plan", Id, "--project", slug, "--json");
        gone.ExitCode.Should().Be(0, gone.StandardError);
        gone.Json().GetProperty("state").GetString().Should().Be("missing");

        var goneText = await loadout.RunAsync("idea", "plan", Id, "--project", slug);
        goneText.ExitCode.Should().NotBe(0);
        goneText.StandardError.Should().Contain("is not at").And.Contain(path);
    }

    /// <summary>
    /// An idea with a plan on the workspace-wide list, made by the real
    /// service elsewhere and copied in, because nothing but an agent's round
    /// gives an idea a plan from the command line.
    /// </summary>
    private static async Task SeedAsync(string workspace)
    {
        using var space = new TemporaryWorkspace();
        var place = new IdeaPlace(null, Id);

        (await space.Ideas.CaptureAsync(null, "Status page", "me", id: Id)).Succeeded.Should().BeTrue();
        (await space.Ideas.RecordReplyAsync(place, new IdeaReply
        {
            Contract = IdeaSchema.Version,
            Understanding = "A page showing what is up.",
            Plan = new IdeaReply.ReplyPlan
            {
                Title = "Status page",
                Layers =
                [
                    new IdeaReply.ReplyLayer
                    {
                        Name = "Storage",
                        Options = [new IdeaReply.ReplyOption { Title = "SQLite", Recommended = true }],
                    },
                ],
            },
        })).Succeeded.Should().BeTrue();

        var ideas = Path.Combine(workspace, "ideas");
        Directory.CreateDirectory(ideas);
        File.Copy(Path.Combine(space.Workspace.LocalPath, "ideas", $"{Id}.yaml"), Path.Combine(ideas, $"{Id}.yaml"));

        // The idea is also an entry on the workspace-wide list, which is what
        // accepting moves onto the project.
        File.Copy(Path.Combine(space.Workspace.LocalPath, "tasks.yaml"), Path.Combine(workspace, "tasks.yaml"), overwrite: true);
    }

    private static async Task Repository(string directory)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "README.md"), "# Homelab\n");

        await Git(directory, "init", "-q");
        await Git(directory, "remote", "add", "origin", "https://github.com/example/homelab.git");
        await Git(directory, "add", "-A");
        await Git(directory, "-c", "user.email=t@example.com", "-c", "user.name=T", "commit", "-q", "-m", "first");
    }

    private static async Task Git(string directory, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;

        await process.WaitForExitAsync();
    }
}
