using System.Text;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// Text written at one end and read line by line at the other, in memory.
/// </summary>
/// <remarks>
/// What a child's standard output was to whoever read it, for a command run
/// inside this process instead: the reader waits for a line, and reads the end
/// once the writer is closed. Unbounded, because the reader caps what it keeps
/// and reads to the end regardless, as it did with a real pipe.
/// </remarks>
internal sealed class TextPipe
{
    private readonly StringBuilder _buffer = new();
    private readonly SemaphoreSlim _written = new(0);
    private bool _closed;

    internal TextPipe()
    {
        Writer = new End(this);
        Reader = new Start(this);
    }

    /// <summary>The end a command writes to.</summary>
    internal TextWriter Writer { get; }

    /// <summary>The end that is read.</summary>
    internal TextReader Reader { get; }

    /// <summary>Says there will be nothing more, so the reader reaches the end.</summary>
    internal void Close()
    {
        lock (_buffer)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
        }

        _written.Release();
    }

    private void Append(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        lock (_buffer)
        {
            // Written after the end is dropped, as a closed pipe would: a
            // command can leave something running that still writes.
            if (_closed)
            {
                return;
            }

            _buffer.Append(text);
        }

        _written.Release();
    }

    private async ValueTask<string?> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            lock (_buffer)
            {
                for (var i = 0; i < _buffer.Length; i++)
                {
                    if (_buffer[i] != '\n')
                    {
                        continue;
                    }

                    var line = _buffer.ToString(0, i > 0 && _buffer[i - 1] == '\r' ? i - 1 : i);

                    _buffer.Remove(0, i + 1);

                    return line;
                }

                if (_closed)
                {
                    if (_buffer.Length == 0)
                    {
                        return null;
                    }

                    var last = _buffer.ToString();

                    _buffer.Clear();

                    return last;
                }
            }

            await _written.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    private sealed class End(TextPipe pipe) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => pipe.Append(value.ToString());

        public override void Write(string? value) => pipe.Append(value ?? string.Empty);

        public override void Write(char[] buffer, int index, int count) => pipe.Append(new string(buffer, index, count));
    }

    private sealed class Start(TextPipe pipe) : TextReader
    {
        public override string? ReadLine() => pipe.ReadLineAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            pipe.ReadLineAsync(cancellationToken);

        public override Task<string?> ReadLineAsync() => pipe.ReadLineAsync(CancellationToken.None).AsTask();

        public override async Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            var all = new StringBuilder();

            while (await pipe.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                all.Append(line).Append('\n');
            }

            return all.ToString();
        }
    }
}
