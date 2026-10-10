using System.Text.Json;

namespace XrealScreen.Contracts;

/// <summary>What <see cref="JsonLineChannel.ReadAsync"/> returned.</summary>
public enum ReadOutcome
{
    /// <summary>A message was read.</summary>
    Message,

    /// <summary>The line was not valid JSON-RPC JSON, or was too long; the channel is still usable.</summary>
    Invalid,

    /// <summary>The other side closed the connection.</summary>
    EndOfStream,
}

/// <summary>Result of <see cref="JsonLineChannel.ReadAsync"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Message">The message when <paramref name="Outcome"/> is <see cref="ReadOutcome.Message"/>.</param>
/// <param name="Error">Why the line was invalid.</param>
public readonly record struct ReadResult(ReadOutcome Outcome, RpcMessage? Message = null, string? Error = null);

/// <summary>
/// Newline-delimited JSON over a stream: each message is one line of UTF-8 JSON ending in <c>\n</c>
/// (no newlines inside a message; JSON escapes them). Reads and writes may run on different threads;
/// writes are serialized internally.
/// </summary>
public sealed class JsonLineChannel : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly int _maxMessageBytes;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly byte[] _buffer;
    private int _start;
    private int _end;

    /// <param name="stream">A connected duplex stream (e.g. a named pipe).</param>
    /// <param name="maxMessageBytes">Longest accepted line.</param>
    public JsonLineChannel(Stream stream, int maxMessageBytes = ApiProtocol.MaxMessageBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMessageBytes, 16);
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _maxMessageBytes = maxMessageBytes;
        _buffer = new byte[maxMessageBytes + 1];
    }

    /// <summary>Reads the next message. Blank lines are skipped.</summary>
    public async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        bool discarding = false;
        while (true)
        {
            int newline = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
            if (newline >= 0)
            {
                var line = _buffer.AsMemory(_start, newline - _start);
                _start = newline + 1;
                if (discarding)
                {
                    return new ReadResult(ReadOutcome.Invalid, Error: $"Message longer than {_maxMessageBytes} bytes.");
                }

                if (IsBlank(line.Span))
                {
                    continue;
                }

                return Parse(line.Span);
            }

            // No complete line buffered: compact, then read more.
            if (_start > 0)
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }

            if (_end == _buffer.Length)
            {
                // Line too long: drop what we have and skip to the next newline.
                discarding = true;
                _end = 0;
            }

            int read = await _stream.ReadAsync(_buffer.AsMemory(_end), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return new ReadResult(ReadOutcome.EndOfStream);
            }

            _end += read;
        }
    }

    /// <summary>Writes <paramref name="message"/> as one line.</summary>
    public async ValueTask WriteAsync(RpcMessage message, CancellationToken cancellationToken)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, ApiJsonContext.Default.RpcMessage);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Disposes the stream.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }

    private static ReadResult Parse(ReadOnlySpan<byte> line)
    {
        try
        {
            var message = JsonSerializer.Deserialize(line, ApiJsonContext.Default.RpcMessage);
            return message is null
                ? new ReadResult(ReadOutcome.Invalid, Error: "Expected a JSON object.")
                : new ReadResult(ReadOutcome.Message, message);
        }
        catch (JsonException ex)
        {
            return new ReadResult(ReadOutcome.Invalid, Error: $"Invalid JSON: {ex.Message}");
        }
    }

    private static bool IsBlank(ReadOnlySpan<byte> line)
    {
        foreach (byte b in line)
        {
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r'))
            {
                return false;
            }
        }

        return true;
    }
}
