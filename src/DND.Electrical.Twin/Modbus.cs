using System.Buffers.Binary;
using System.Net.Sockets;

namespace DND.Electrical.Twin;

public sealed class ModbusException(byte function, byte code) : IOException($"Modbus exception {code} ({Describe(code)}) for function 0x{function:X2}.")
{
    public byte Function { get; } = function;
    public byte Code { get; } = code;
    private static string Describe(byte c) => c switch { 1 => "illegal function", 2 => "illegal data address", 3 => "illegal data value", 4 => "server device failure", 6 => "server busy", 10 => "gateway path unavailable", 11 => "gateway target failed to respond", _ => "unknown" };
}

/// <summary>
/// Modbus TCP client (Modbus Application Protocol v1.1b3 over TCP, MBAP header). Supports read coils (0x01),
/// read discrete inputs (0x02), read holding registers (0x03), read input registers (0x04), write single register
/// (0x06) and write multiple registers (0x10). One request at a time per connection.
/// </summary>
public sealed class ModbusTcpClient : IAsyncDisposable
{
    private readonly TcpClient _tcp = new() { NoDelay = true };
    private NetworkStream? _stream;
    private ushort _transaction;
    private readonly SemaphoreSlim _lock = new(1, 1);
    public byte UnitId { get; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    public ModbusTcpClient(byte unitId = 1) => UnitId = unitId;

    public async Task ConnectAsync(string host, int port = 502, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        await _tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
        _stream = _tcp.GetStream();
    }

    public bool Connected => _tcp.Connected;

    public async Task<ushort[]> ReadHoldingRegistersAsync(ushort start, ushort count, CancellationToken ct = default) => Words(await RequestAsync(0x03, ReadPdu(start, count, 125), ct).ConfigureAwait(false), count);
    public async Task<ushort[]> ReadInputRegistersAsync(ushort start, ushort count, CancellationToken ct = default) => Words(await RequestAsync(0x04, ReadPdu(start, count, 125), ct).ConfigureAwait(false), count);
    public async Task<bool[]> ReadCoilsAsync(ushort start, ushort count, CancellationToken ct = default) => Bits(await RequestAsync(0x01, ReadPdu(start, count, 2000), ct).ConfigureAwait(false), count);
    public async Task<bool[]> ReadDiscreteInputsAsync(ushort start, ushort count, CancellationToken ct = default) => Bits(await RequestAsync(0x02, ReadPdu(start, count, 2000), ct).ConfigureAwait(false), count);

    public async Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken ct = default)
    {
        var pdu = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(pdu, address); BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(2), value);
        var resp = await RequestAsync(0x06, pdu, ct).ConfigureAwait(false);
        if (!resp.AsSpan().SequenceEqual(pdu)) throw new IOException("Modbus write single register: response does not echo the request.");
    }

    public async Task WriteMultipleRegistersAsync(ushort start, IReadOnlyList<ushort> values, CancellationToken ct = default)
    {
        if (values.Count is 0 or > 123) throw new ArgumentOutOfRangeException(nameof(values), "1 to 123 registers per request.");
        var pdu = new byte[5 + values.Count * 2];
        BinaryPrimitives.WriteUInt16BigEndian(pdu, start); BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(2), (ushort)values.Count); pdu[4] = (byte)(values.Count * 2);
        for (var i = 0; i < values.Count; i++) BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(5 + i * 2), values[i]);
        var resp = await RequestAsync(0x10, pdu, ct).ConfigureAwait(false);
        if (resp.Length != 4 || BinaryPrimitives.ReadUInt16BigEndian(resp) != start || BinaryPrimitives.ReadUInt16BigEndian(resp.AsSpan(2)) != values.Count)
            throw new IOException("Modbus write multiple registers: unexpected response.");
    }

    private static byte[] ReadPdu(ushort start, ushort count, int max)
    {
        if (count == 0 || count > max) throw new ArgumentOutOfRangeException(nameof(count), $"1 to {max} items per request.");
        var pdu = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(pdu, start); BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(2), count);
        return pdu;
    }

    private static ushort[] Words(byte[] resp, int count)
    {
        if (resp.Length < 1 || resp[0] != count * 2 || resp.Length != 1 + count * 2) throw new IOException("Modbus: unexpected byte count in register response.");
        var w = new ushort[count];
        for (var i = 0; i < count; i++) w[i] = BinaryPrimitives.ReadUInt16BigEndian(resp.AsSpan(1 + 2 * i));
        return w;
    }

    private static bool[] Bits(byte[] resp, int count)
    {
        if (resp.Length < 1 || resp[0] != (count + 7) / 8) throw new IOException("Modbus: unexpected byte count in bit response.");
        var b = new bool[count];
        for (var i = 0; i < count; i++) b[i] = (resp[1 + i / 8] & (1 << (i % 8))) != 0;
        return b;
    }

    /// <summary>Sends one request PDU and returns the response data (PDU without the function code).</summary>
    private async Task<byte[]> RequestAsync(byte function, byte[] data, CancellationToken ct)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected.");
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            var tid = ++_transaction;
            var frame = new byte[7 + 1 + data.Length];
            BinaryPrimitives.WriteUInt16BigEndian(frame, tid);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(2 + data.Length));
            frame[6] = UnitId; frame[7] = function;
            data.CopyTo(frame, 8);
            await _stream.WriteAsync(frame, cts.Token).ConfigureAwait(false);

            var header = new byte[7];
            await _stream.ReadExactlyAsync(header, cts.Token).ConfigureAwait(false);
            var rtid = BinaryPrimitives.ReadUInt16BigEndian(header);
            var len = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
            if (BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2)) != 0 || len < 2 || len > 254) throw new IOException("Modbus: invalid MBAP header.");
            var pdu = new byte[len - 1];
            await _stream.ReadExactlyAsync(pdu, cts.Token).ConfigureAwait(false);
            if (rtid != tid) throw new IOException($"Modbus: transaction id mismatch ({rtid} ≠ {tid}).");
            if (pdu[0] == (function | 0x80)) throw new ModbusException(function, pdu.Length > 1 ? pdu[1] : (byte)0);
            if (pdu[0] != function) throw new IOException($"Modbus: unexpected function 0x{pdu[0]:X2} in response.");
            return pdu[1..];
        }
        finally { _lock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null) await _stream.DisposeAsync().ConfigureAwait(false);
        _tcp.Dispose();
        _lock.Dispose();
    }
}

/// <summary>Parsed Modbus point address: table, zero-based offset and data type.</summary>
public sealed record ModbusAddress(char Table, ushort Offset, string Type)
{
    /// <summary>Formats: HR:100, IR:5:f32, CO:3, DI:7, HR:20:f32sw (word-swapped float), types i16 | u16 | f32 | f32sw.</summary>
    public static ModbusAddress Parse(string text)
    {
        var p = text.Split(':', StringSplitOptions.TrimEntries);
        if (p.Length < 2) throw new FormatException($"Modbus address '{text}': expected TABLE:offset[:type].");
        var table = p[0].ToUpperInvariant() switch { "HR" => 'H', "IR" => 'I', "CO" => 'C', "DI" => 'D', _ => throw new FormatException($"Unknown Modbus table '{p[0]}' (HR, IR, CO, DI).") };
        if (!ushort.TryParse(p[1], out var off)) throw new FormatException($"Invalid Modbus offset '{p[1]}'.");
        var type = p.Length > 2 ? p[2].ToLowerInvariant() : table is 'C' or 'D' ? "bool" : "u16";
        if (type is not ("i16" or "u16" or "f32" or "f32sw" or "bool")) throw new FormatException($"Unknown Modbus data type '{type}'.");
        return new ModbusAddress(table, off, type);
    }

    public ushort Registers => Type is "f32" or "f32sw" ? (ushort)2 : (ushort)1;

    public double Decode(ReadOnlySpan<ushort> w) => Type switch
    {
        "i16" => (short)w[0],
        "u16" => w[0],
        "f32" => BitConverter.Int32BitsToSingle((w[0] << 16) | w[1]),
        "f32sw" => BitConverter.Int32BitsToSingle((w[1] << 16) | w[0]),
        _ => w[0],
    };
}
