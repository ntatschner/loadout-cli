using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Results;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// One batch of task changes through the command line, the agents' tool and
/// Studio's route, each starting from the same lists, and all three saying the
/// same thing about every change.
/// </summary>
/// <remarks>
/// The point of one service under all three was that they cannot drift apart,
/// and this is what would notice if they did: a field one surface reads and
/// another drops, a default one applies and another does not.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class TaskApplyParityContractTests
{
    /// <summary>
    /// Two lists, one change on the second refused, so the first list is
    /// changed and the second is held back whole.
    /// </summary>
    private const string Batch = """
        [
          {"op": "add", "title": "Write the release notes"},
          {"op": "declare", "id": "write-the-release-notes", "state": "doing"},
          {"op": "add", "project": "-", "title": "Look at the fonts", "note": "someday"},
          {"op": "remove", "project": "-", "id": "not-there"}
        ]
        """;

    [BuiltCliFact]
    public async Task The_command_line_the_tool_and_the_route_say_the_same_about_one_batch()
    {
        var command = await ByCommandAsync();
        var tool = await ByToolAsync();
        var route = await ByRouteAsync();

        Said(tool).Should().Equal(Said(command), "the agents' tool and the command line take one batch alike");
        Said(route).Should().Equal(Said(command), "Studio's route and the command line take one batch alike");

        Said(command).Select(one => one.Applied).Should().Equal(true, true, false, false);
        Said(command)[2].HeldBack.Should().BeTrue();
    }

    private static async Task<JsonElement> ByCommandAsync()
    {
        using var loadout = new LoadoutProcess();
        var slug = await RegisteredAsync(loadout);
        var file = Path.Combine(loadout.Home, "batch.json");

        await File.WriteAllTextAsync(file, Batch);

        var applied = await loadout.RunAsync("task", "apply", file, "--project", slug, "--json");

        applied.ExitCode.Should().Be(2, "a change was refused, and the exit code is the refusal's: " + applied.StandardError);

        return applied.Json();
    }

    private static async Task<JsonElement> ByToolAsync()
    {
        using var loadout = new LoadoutProcess();
        var slug = await RegisteredAsync(loadout);
        using var session = await McpServerContractTests.StartAsync(loadout.Talking("mcp", "serve", "--project", slug));

        var answer = await session.RequestAsync("tools/call", new
        {
            name = "loadout_task_apply",
            arguments = new { operations = Batch },
        });

        var text = answer.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <remarks>
    /// In this process, over a workspace of its own, because a server started
    /// as a command waits for its person. The project is named as the
    /// command line's registration names it, so every reason reads the same.
    /// </remarks>
    private static async Task<JsonElement> ByRouteAsync()
    {
        using var space = new TemporaryWorkspace();
        await space.ProjectAsync(Name);

        using var stopping = new CancellationTokenSource();
        using var server = new DashboardServer(new Unrun()) { Tasks = space.Tasks };

        server.Start(0).Succeeded.Should().BeTrue();
        _ = server.ListenAsync(stopping.Token);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-Loadout-Token", server.Token);

        using var answered = await client.PostAsync(
            $"http://127.0.0.1:{server.Port}{MemoryApi.Root}/{Name}/tasks:batch",
            new StringContent(Batch, Encoding.UTF8, "application/json"));

        var body = await answered.Content.ReadFromJsonAsync<JsonElement>();
        await stopping.CancelAsync();

        return body;
    }

    /// <summary>What a batch's results say, without who said so or when, which each surface says differently on purpose.</summary>
    /// <remarks>
    /// A missing property is read as null: the command line's JSON leaves
    /// nulls out, as all of its JSON does, and the tool's and the route's
    /// write them.
    /// </remarks>
    private static List<Result> Said(JsonElement batch) =>
        [.. batch.GetProperty("results").EnumerateArray().Select(one => new Result(
            Text(one, "op")!,
            Text(one, "project"),
            Text(one, "id")!,
            one.GetProperty("applied").GetBoolean(),
            one.GetProperty("heldBack").GetBoolean(),
            Text(one, "error"),
            one.GetProperty("code").GetInt32(),
            one.TryGetProperty("task", out var task) && task.ValueKind == JsonValueKind.Object
                ? $"{Text(task, "state")} {Text(task, "title")} {Text(task, "note")}"
                : null))];

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record Result(string Op, string? Project, string Id, bool Applied, bool HeldBack, string? Error, int Code, string? Task);

    private const string Name = "parity-repository";

    private static async Task<string> RegisteredAsync(LoadoutProcess loadout)
    {
        var repository = Path.Combine(loadout.Home, Name);

        Directory.CreateDirectory(repository);

        await GitAsync(repository, "init");
        await File.WriteAllTextAsync(Path.Combine(repository, "readme.md"), Name);
        await GitAsync(repository, "add", "-A");
        await GitAsync(repository, "-c", "user.email=t@example.com", "-c", "user.name=T", "commit", "-m", "first");

        var added = await loadout.RunAsync("project", "add", repository, "--json");

        added.ExitCode.Should().Be(0, added.StandardError);
        var slug = added.Json().GetProperty("id").GetString()!;
        slug.Should().Be(Name, "the route's workspace names its project the same, so the reasons read the same");

        return slug;
    }

    private static async Task GitAsync(string directory, params string[] arguments)
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
        process.ExitCode.Should().Be(0, await process.StandardError.ReadToEndAsync());
    }

    private sealed class Unrun : IRunJournal
    {
        public IReadOnlyList<string> List(int limit = 20) => [];

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) =>
            OperationResult<IReadOnlyList<RunEvent>>.Fail("no such run");

        public OperationResult<RunSummary> Summarise(string runId) => OperationResult<RunSummary>.Fail("no such run");

        public string DirectoryOf(string runId) => Path.Combine(Path.GetTempPath(), runId);

        public OperationResult<RunForgotten> Forget(string runId, bool force = false) =>
            OperationResult<RunForgotten>.Fail("no such run");
    }
}
