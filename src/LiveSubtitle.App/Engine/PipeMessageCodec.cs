using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveSubtitle.Core;

namespace LiveSubtitle.App.Engine;

internal sealed class PipeMessageCodec
{
    private const int MaximumMessageBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly Stream stream;
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public PipeMessageCodec(Stream stream)
    {
        this.stream = stream;
    }

    public async Task WriteAsync(EngineMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        if (payload.Length > MaximumMessageBytes) throw new InvalidDataException("Engine message is too large.");
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<EngineMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        if (!await ReadExactlyOrEndAsync(header, cancellationToken).ConfigureAwait(false)) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumMessageBytes) throw new InvalidDataException("Invalid engine message length.");
        var payload = new byte[length];
        if (!await ReadExactlyOrEndAsync(payload, cancellationToken).ConfigureAwait(false))
        {
            throw new EndOfStreamException("The engine pipe ended inside a message.");
        }
        return JsonSerializer.Deserialize<EngineMessage>(payload, Options)
               ?? throw new InvalidDataException("The engine sent an empty message.");
    }

    private async Task<bool> ReadExactlyOrEndAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) return offset != 0
                ? throw new EndOfStreamException("The engine pipe ended unexpectedly.")
                : false;
            offset += read;
        }
        return true;
    }
}
