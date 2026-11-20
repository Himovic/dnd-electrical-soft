using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DND.Electrical.Domain;
using DND.Electrical.Twin;
using MQTTnet;
using MQTTnet.Server;

namespace DND.Electrical.IntegrationTests;

/// <summary>
/// Minimal Modbus TCP server used only to test the client against real sockets and frames
/// (functions 0x01–0x04, 0x06, 0x10; exception 0x02 for addresses outside the tables).
/// </summary>
internal sealed class TestModbusServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    public ushort[] Holding { get; } = new ushort[200];
    public ushort[] Input { get; } = new ushort[200];
    public bool[] Coils { get; } = new bool[64];
    public bool[] Discrete { get; } = new bool[64];
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public int Requests;

    public TestModbusServer()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await _listener.AcceptTcpClientAsync(_cts.Token); } catch { return; }
            _ = Task.Run(() => Serve(c));
        }
    }

    private async Task Serve(TcpClient client)
    {
        using var _ = client;
        var s = client.GetStream();
        var header = new byte[7];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await s.ReadExactlyAsync(header, _cts.Token);
                var len = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                var pdu = new byte[len - 1];
                await s.ReadExactlyAsync(pdu, _cts.Token);
                Interlocked.Increment(ref Requests);
                var resp = Handle(pdu);
                var frame = new byte[7 + resp.Length];
                header.AsSpan(0, 4).CopyTo(frame);
                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(resp.Length + 1));
                frame[6] = header[6];
                resp.CopyTo(frame, 7);
                await s.WriteAsync(frame, _cts.Token);
            }
        }
        catch { /* connection closed */ }
    }

    private byte[] Handle(byte[] pdu)
    {
        var f = pdu[0];
        ushort a = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1)), n = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
        byte[] Ex(byte code) => new[] { (byte)(f | 0x80), code };
        switch (f)
        {
            case 3 or 4:
            {
                var table = f == 3 ? Holding : Input;
                if (a + n > table.Length) return Ex(2);
                var r = new byte[2 + 2 * n]; r[0] = f; r[1] = (byte)(2 * n);
                for (var i = 0; i < n; i++) BinaryPrimitives.WriteUInt16BigEndian(r.AsSpan(2 + 2 * i), table[a + i]);
                return r;
            }
            case 1 or 2:
            {
                var table = f == 1 ? Coils : Discrete;
                if (a + n > table.Length) return Ex(2);
                var bytes = (n + 7) / 8; var r = new byte[2 + bytes]; r[0] = f; r[1] = (byte)bytes;
                for (var i = 0; i < n; i++) if (table[a + i]) r[2 + i / 8] |= (byte)(1 << (i % 8));
                return r;
            }
            case 6:
                if (a >= Holding.Length) return Ex(2);
                Holding[a] = n;
                return pdu;
            case 16:
            {
                if (a + n > Holding.Length) return Ex(2);
                for (var i = 0; i < n; i++) Holding[a + i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(6 + 2 * i));
                return pdu[..5];
            }
            default:
                return Ex(1);
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}

public class TwinTests
{
    private static (ushort hi, ushort lo) Float(float v) { var bits = BitConverter.SingleToInt32Bits(v); return ((ushort)(bits >> 16), (ushort)(bits & 0xFFFF)); }

    [Fact]
    public async Task Modbus_client_reads_and_writes_all_supported_functions()
    {
        await using var server = new TestModbusServer();
        server.Holding[10] = 1234; server.Input[3] = 65535;
        var (hi, lo) = Float(42.5f);
        server.Holding[20] = hi; server.Holding[21] = lo;
        server.Coils[5] = true; server.Discrete[9] = true;
        await using var c = new ModbusTcpClient(unitId: 7);
        await c.ConnectAsync("127.0.0.1", server.Port);
        Assert.Equal(new ushort[] { 1234 }, await c.ReadHoldingRegistersAsync(10, 1));
        Assert.Equal(new ushort[] { 65535 }, await c.ReadInputRegistersAsync(3, 1));
        var coils = await c.ReadCoilsAsync(0, 10);
        Assert.True(coils[5]); Assert.Equal(1, coils.Count(x => x));
        Assert.True((await c.ReadDiscreteInputsAsync(9, 1))[0]);
        var f = ModbusAddress.Parse("HR:20:f32");
        Assert.Equal(42.5, f.Decode(await c.ReadHoldingRegistersAsync(20, 2)), 6);
        await c.WriteSingleRegisterAsync(30, 777);
        Assert.Equal(777, server.Holding[30]);
        await c.WriteMultipleRegistersAsync(40, new ushort[] { 1, 2, 3 });
        Assert.Equal(new ushort[] { 1, 2, 3 }, server.Holding[40..43]);
        var ex = await Assert.ThrowsAsync<ModbusException>(() => c.ReadHoldingRegistersAsync(199, 5));
        Assert.Equal(2, ex.Code);
        // The connection stays usable after an exception response.
        Assert.Equal(new ushort[] { 1234 }, await c.ReadHoldingRegistersAsync(10, 1));
    }

    [Theory]
    [InlineData("HR:100", 'H', 100, "u16")]
    [InlineData("IR:5:f32sw", 'I', 5, "f32sw")]
    [InlineData("co:3", 'C', 3, "bool")]
    public void Modbus_addresses_parse(string text, char table, int offset, string type)
    {
        var a = ModbusAddress.Parse(text);
        Assert.Equal(table, a.Table); Assert.Equal(offset, a.Offset); Assert.Equal(type, a.Type);
        Assert.Equal(-2, ModbusAddress.Parse("HR:0:i16").Decode(new ushort[] { 0xFFFE }));
        var (hi, lo) = Float(-3.25f);
        Assert.Equal(-3.25, ModbusAddress.Parse("HR:0:f32sw").Decode(new[] { lo, hi }), 6);
        Assert.Throws<FormatException>(() => ModbusAddress.Parse("XX:1"));
    }

    [Fact]
    public async Task Twin_engine_reads_modbus_points_scales_values_and_evaluates_alarms()
    {
        await using var server = new TestModbusServer();
        var p = new ElectricalProject();
        var pt = new Instrument { Tag = "PT-1", RangeMax = 10, Unit = "bar" };
        p.Instruments.Add(pt);
        p.Alarms.Add(new AlarmDefinition { Tag = "PT-1-H", InstrumentId = pt.Id, Type = AlarmType.High, SetPoint = 8, Deadband = 0.5 });
        var point = new TwinPoint { Tag = "PT-1", InstrumentId = pt.Id, Source = TwinSourceKind.ModbusTcp, Endpoint = $"127.0.0.1:{server.Port}/1", Address = "IR:0", Scale = 0.01, Unit = "bar" };
        p.TwinPoints.Add(point);
        p.TwinPoints.Add(new TwinPoint { Tag = "XS-1", Source = TwinSourceKind.ModbusTcp, Endpoint = $"127.0.0.1:{server.Port}/1", Address = "DI:2" });
        await using var engine = new TwinEngine(p);

        server.Input[0] = 750; server.Discrete[2] = true;
        var values = await engine.PollAsync();
        Assert.Equal(7.5, values.Single(v => v.PointId == point.Id).Value!.Value, 9);
        Assert.All(values, v => { Assert.Equal(TwinQuality.Good, v.Quality); Assert.False(v.Simulated); });
        Assert.Empty(engine.EvaluateAlarms());

        server.Input[0] = 820; await engine.PollAsync();
        var alarm = Assert.Single(engine.EvaluateAlarms());
        Assert.Equal("PT-1-H", alarm.Alarm.Tag);
        server.Input[0] = 790; await engine.PollAsync();            // inside the deadband: stays active
        Assert.Single(engine.EvaluateAlarms());
        server.Input[0] = 740; await engine.PollAsync();            // below SP − deadband: clears
        Assert.Empty(engine.EvaluateAlarms());
        Assert.Equal(4, engine.History(point.Id).Count);
    }

    [Fact]
    public async Task Unreachable_and_unsupported_sources_report_quality_instead_of_values()
    {
        var p = new ElectricalProject();
        var free = new TcpListener(IPAddress.Loopback, 0); free.Start(); var port = ((IPEndPoint)free.LocalEndpoint).Port; free.Stop();
        p.TwinPoints.Add(new TwinPoint { Tag = "A", Source = TwinSourceKind.ModbusTcp, Endpoint = $"127.0.0.1:{port}", Address = "HR:0" });
        p.TwinPoints.Add(new TwinPoint { Tag = "B", Source = TwinSourceKind.OpcUa, Endpoint = "opc.tcp://localhost:4840", Address = "ns=2;s=x" });
        await using var engine = new TwinEngine(p);
        var v = await engine.PollAsync();
        Assert.All(v, x => { Assert.Null(x.Value); Assert.Equal(TwinQuality.NotConnected, x.Quality); Assert.NotNull(x.Error); });
        Assert.Contains(v, x => x.Error!.Contains("not available"));
    }

    [Fact]
    public async Task Simulator_values_are_deterministic_bounded_and_flagged()
    {
        var clock = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var src = new SimulatedSource(() => clock);
        var pts = Enumerable.Range(0, 20).Select(i => new TwinPoint { Tag = $"S{i}", SimMin = 10, SimMax = 20, SimPeriodS = 30 }).ToList();
        var a = await src.ReadAsync(pts, default);
        var b = await src.ReadAsync(pts, default);
        Assert.Equal(a.Select(x => x.Value), b.Select(x => x.Value));
        Assert.All(a, v => { Assert.True(v.Simulated); Assert.InRange(v.Value!.Value, 10, 20); });
    }

    [Fact]
    public async Task Mqtt_source_receives_numeric_and_json_payloads_from_a_broker()
    {
        var free = new TcpListener(IPAddress.Loopback, 0); free.Start(); var port = ((IPEndPoint)free.LocalEndpoint).Port; free.Stop();
        var options = new MqttServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointPort(port)
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).WithDefaultEndpointBoundIPV6Address(IPAddress.None).Build();
        using var broker = new MqttServerFactory().CreateMqttServer(options);
        await broker.StartAsync();
        try
        {
            var p = new ElectricalProject();
            var t1 = new TwinPoint { Tag = "TT-1", Source = TwinSourceKind.Mqtt, Endpoint = $"127.0.0.1:{port}", Address = "plant/tt1" };
            var t2 = new TwinPoint { Tag = "FT-2", Source = TwinSourceKind.Mqtt, Endpoint = $"127.0.0.1:{port}", Address = "plant/ft2", Scale = 2 };
            p.TwinPoints.AddRange(new[] { t1, t2 });
            await using var engine = new TwinEngine(p);
            var first = await engine.PollAsync();                       // connects and subscribes
            Assert.All(first, v => Assert.Equal(TwinQuality.Stale, v.Quality));

            using var pub = new MqttClientFactory().CreateMqttClient();
            await pub.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", port).Build());
            await pub.PublishStringAsync("plant/tt1", "21.5");
            await pub.PublishStringAsync("plant/ft2", "{\"value\": 3.25, \"unit\": \"m3/h\"}");
            IReadOnlyList<TwinValue> v = first;
            for (var i = 0; i < 50 && v.Any(x => x.Quality != TwinQuality.Good); i++) { await Task.Delay(50); v = await engine.PollAsync(); }
            Assert.Equal(21.5, v.Single(x => x.PointId == t1.Id).Value);
            Assert.Equal(6.5, v.Single(x => x.PointId == t2.Id).Value);
            await pub.DisconnectAsync();
        }
        finally { await broker.StopAsync(); }
        Assert.True(MqttSource.TryParse("{\"value\":1e3}", out var x) && x == 1000);
        Assert.False(MqttSource.TryParse("not a number", out _));
    }

    [Fact]
    public async Task Twin_points_persist()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-OG-009");
        p.TwinPoints.Add(new TwinPoint { Tag = "PT-301", InstrumentId = p.Instruments[0].Id, Source = TwinSourceKind.ModbusTcp, Endpoint = "10.0.0.5:502/3", Address = "HR:100:f32", Unit = "barg" });
        var path = env.PathFor("twin.dndx");
        await env.Store.SaveAsync(p, path);
        var l = await env.Store.LoadAsync(path);
        var t = Assert.Single(l.TwinPoints);
        Assert.Equal(TwinSourceKind.ModbusTcp, t.Source);
        Assert.Equal("HR:100:f32", t.Address);
    }
}
