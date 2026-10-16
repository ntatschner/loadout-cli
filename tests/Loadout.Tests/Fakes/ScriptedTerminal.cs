using System.Text;
using System.Threading.Channels;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Tests.Fakes;

/// <summary>A pseudo-terminal whose output the test writes and whose input the test reads.</summary>
public sealed class ScriptedTerminal : IPseudoTerminal
{
    private readonly Channel<byte[]> _output = Channel.CreateUnbounded<byte[]>();
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<byte> _written = [];

    public const int ChildId = 4242;

    public int ProcessId { get; private set; }

    public string? Refuse { get; init; }

    public List<(int, int)> Sizes { get; } = [];

    public bool Disposed { get; private set; }

    public byte[] Written
    {
        get { lock (_written) { return [.. _written]; } }
    }

    public string Typed => Encoding.UTF8.GetString(Written);

    public void Say(string text) => _output.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

    public void Exit(int code)
    {
        _output.Writer.TryComplete();
        _exit.TrySetResult(code);
    }

    /// <summary>What it was asked to start, once it has been.</summary>
    public ProcessRequest? Request { get; private set; }

    public Task<OperationResult> StartAsync(ProcessRequest request, int columns, int rows, CancellationToken ct = default)
    {
        Request = request;

        return Task.FromResult(Refuse is null ? Started() : OperationResult.Fail(Refuse));
    }

    private OperationResult Started()
    {
        ProcessId = ChildId;

        return OperationResult.Ok();
    }

    public Task<OperationResult> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        lock (_written)
        {
            _written.AddRange(data.ToArray());
        }

        return Task.FromResult(OperationResult.Ok());
    }

    public async Task<OperationResult<int>> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        try
        {
            var chunk = await _output.Reader.ReadAsync(ct);
            chunk.CopyTo(buffer);

            return OperationResult<int>.Ok(chunk.Length);
        }
        catch (ChannelClosedException)
        {
            return OperationResult<int>.Ok(0);
        }
    }

    public OperationResult Resize(int columns, int rows)
    {
        Sizes.Add((columns, rows));

        return OperationResult.Ok();
    }

    public async Task<OperationResult<int>> WaitForExitAsync(CancellationToken ct = default) =>
        OperationResult<int>.Ok(await _exit.Task.WaitAsync(ct));

    public void Dispose()
    {
        Disposed = true;
        Exit(-1);
    }
}
