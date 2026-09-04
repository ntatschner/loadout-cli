using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The launcher answering an agent over MCP.
/// </summary>
/// <remarks>
/// <para>
/// Driven as a client drives it — a real handshake over stdin and stdout —
/// because that is the only thing that proves the protocol. Calling the tool
/// methods directly would prove they compute the right answer and nothing about
/// whether an agent could ever reach them.
/// </para>
/// <para>
/// The transport is the part most easily broken by accident: anything written
/// to standard output that is not a protocol message is a malformed frame, and
/// the session ends without saying why. A stray Console.WriteLine anywhere in
/// the startup path would fail these and nothing else.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class McpServerContractTests
{
    private sealed class Session : IDisposable
    {
        private readonly Process _process;
        private readonly System.Text.StringBuilder _errors = new();
        private int _id;

        internal Session(string executable)
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo(executable)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };

            _process.StartInfo.ArgumentList.Add("mcp");
            _process.StartInfo.ArgumentList.Add("serve");

            _process.Start();

            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (_errors)
                    {
                        _errors.AppendLine(e.Data);
                    }
                }
            };

            _process.BeginErrorReadLine();
        }

        /// <summary>
        /// How long a request may go unanswered before the test gives up.
        /// </summary>
        /// <remarks>
        /// A hang guard, not a performance budget. It sits above the thirty
        /// seconds the process launcher gives a child, so a single timed-out
        /// child still produces an answer this can assert on rather than
        /// racing the test's own deadline. Every tool here answers in well
        /// under a second; a call that reaches this bound is broken, and the
        /// point of the bound is that it says so instead of waiting forever.
        /// </remarks>
        private static readonly TimeSpan Answer = TimeSpan.FromSeconds(60);

        internal async Task<JsonElement> RequestAsync(string method, object? parameters = null)
        {
            var id = ++_id;

            Send(new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } });

            string? line;

            try
            {
                // Bounded, because an unbounded read here does not fail — it
                // waits. A request that went unanswered once left this reading
                // for ninety-three minutes: the test never reached its own
                // disposal, so the server it started was never killed, and the
                // whole run sat behind it with no output to say why. A hang
                // with no log is the worst failure a suite can have, and it is
                // worse than a wrong answer because nothing points at it.
                //
                // Awaited rather than blocked on. This read the answer with
                // GetAwaiter().GetResult(), which xUnit's own analyzer refuses
                // in a test method and did not see here because it sat in a
                // helper. Blocking a pool thread while the run has two of them
                // and other tests are spawning processes is how the bound above
                // stops helping: the token's own callback needs a pool thread
                // to fire, and if none is free the deadline passes unnoticed.
                using var cts = new CancellationTokenSource(Answer);

                line = await _process.StandardOutput.ReadLineAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"'{method}' did not answer within {Answer.TotalSeconds:N0} seconds. "
                    + $"Standard error so far: {Errors()}");
            }

            line.Should().NotBeNullOrWhiteSpace($"'{method}' has to answer");

            return JsonDocument.Parse(line!).RootElement.Clone();
        }

        /// <summary>What the server has said on standard error.</summary>
        /// <remarks>
        /// Drained continuously, not read at the end. Nothing was reading it at
        /// all, and a redirected pipe nobody empties is one the writer blocks on
        /// once it fills — so a server that became chatty would stop answering
        /// and look exactly like a hang. Keeping it also means a timeout can
        /// say what the server was complaining about instead of nothing.
        /// </remarks>
        private string Errors()
        {
            lock (_errors)
            {
                return _errors.Length == 0 ? "(nothing)" : _errors.ToString();
            }
        }

        /// <summary>A message with no reply expected, as the protocol defines it.</summary>
        internal void Notify(string method) =>
            Send(new { jsonrpc = "2.0", method });

        private void Send(object message)
        {
            _process.StandardInput.WriteLine(JsonSerializer.Serialize(message));
            _process.StandardInput.Flush();
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }

            _process.Dispose();
        }
    }

    private static async Task<Session> StartAsync()
    {
        var executable = LoadoutProcess.Executable;

        executable.Should().NotBeNull("the command line has to be built");

        var session = new Session(executable!);

        var initialize = await session.RequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "contract-test", version = "1" },
        });

        // Answered as a server, not as a command line that printed something.
        initialize.GetProperty("result").GetProperty("serverInfo")
            .GetProperty("name").GetString().Should().Be("loadout");

        session.Notify("notifications/initialized");

        return session;
    }

    [BuiltCliFact]
    public async Task It_speaks_the_protocol_and_names_its_tools()
    {
        using var session = await StartAsync();

        var tools = (await session.RequestAsync("tools/list"))
            .GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();

        tools.Should().Contain("loadout_specialist");
        tools.Should().Contain("loadout_effective_instructions");
        tools.Should().Contain("loadout_recall");
        tools.Should().Contain("loadout_remember");
        tools.Should().Contain("loadout_mode");
    }

    [BuiltCliFact]
    public async Task A_mode_can_be_changed_and_answers_with_the_posture()
    {
        using var session = await StartAsync();

        var answer = await session.RequestAsync("tools/call", new
        {
            name = "loadout_mode",
            arguments = new { mode = "investigate" },
        });

        var text = answer.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString() ?? string.Empty;

        // The posture itself, not a note saying it was changed. An agent that
        // is told "you are now investigating" and nothing else has been given
        // a label rather than a direction.
        text.Should().Contain("Reproduce first");
    }

    [BuiltCliFact]
    public async Task A_mode_that_does_not_exist_is_refused_rather_than_quietly_defaulted()
    {
        using var session = await StartAsync();

        var answer = await session.RequestAsync("tools/call", new
        {
            name = "loadout_mode",
            arguments = new { mode = "yolo" },
        });

        var text = answer.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString() ?? string.Empty;

        // The resolver falls back to the default for a name it does not know,
        // which is right when a person typed it and wrong here: an agent told
        // nothing would carry on believing it had switched.
        text.Should().Contain("no 'yolo' mode");
        text.Should().Contain("investigate");
    }

    [BuiltCliFact]
    public async Task It_hands_over_nothing_that_changes_the_machine()
    {
        using var session = await StartAsync();

        var tools = (await session.RequestAsync("tools/list"))
            .GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString() ?? string.Empty)
            .ToList();

        // A tool an agent can call unprompted is a decision taken with nobody
        // watching. Reading is safe and one screened fact is safe; pushing to a
        // remote, changing the machine and starting another agent are not, and
        // the way they get offered is by somebody adding a tool without asking
        // whether it belongs.
        tools.Should().NotContain(name =>
            name.Contains("save", StringComparison.OrdinalIgnoreCase)
            || name.Contains("push", StringComparison.OrdinalIgnoreCase)
            || name.Contains("launch", StringComparison.OrdinalIgnoreCase)
            || name.Contains("update", StringComparison.OrdinalIgnoreCase)
            || name.Contains("setup", StringComparison.OrdinalIgnoreCase));
    }

    [BuiltCliFact]
    public async Task A_tool_answers_with_what_the_command_would_say()
    {
        using var session = await StartAsync();

        var answer = await session.RequestAsync("tools/call", new
        {
            name = "loadout_specialist",
            arguments = new { id = "language.rust" },
        });

        var text = answer.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString() ?? string.Empty;

        // The whole file, as 'instructions show' would give it: the same call,
        // so an agent and a person are told the same thing about the same
        // specialist.
        text.Should().Contain("id: language.rust");
        text.Should().Contain("## Working rules");
    }

    [BuiltCliFact]
    public async Task Asking_for_something_that_is_not_there_is_answered_rather_than_thrown()
    {
        using var session = await StartAsync();

        var answer = await session.RequestAsync("tools/call", new
        {
            name = "loadout_specialist",
            arguments = new { id = "language.cobol" },
        });

        var text = answer.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString() ?? string.Empty;

        // An error frame would end the agent's turn; a sentence lets it carry
        // on and ask for something that exists.
        text.Should().Contain("no specialist");
    }

    [BuiltCliFact]
    public async Task A_tool_that_has_to_find_the_repository_answers_without_waiting_on_a_child()
    {
        using var session = await StartAsync();

        var clock = Stopwatch.StartNew();

        var answer = await session.RequestAsync("tools/call", new
        {
            name = "loadout_effective_instructions",
            arguments = new { },
        });

        clock.Stop();

        answer.TryGetProperty("result", out _).Should().BeTrue("the tool has to answer");

        // Whether a project is registered on this machine is not this test's
        // business — how long the answer takes is. Working out which project
        // this is means asking Git where the repository root is, and that is
        // the one thing the tools do that nothing else here does. The server
        // is spawned with its standard input held open by the client, and a
        // child that inherited that pipe never exited: the launcher killed it
        // at its thirty-second bound and every tool then answered as though
        // the project did not exist — correct-looking prose, no error, and an
        // agent told it had no instructions. Ten seconds is nowhere near the
        // two hundred milliseconds this takes and nowhere near thirty, so it
        // separates the two without standing in for a performance budget.
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }
}
