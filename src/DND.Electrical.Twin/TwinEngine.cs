using System.Globalization;
using System.Text;
using System.Text.Json;
using DND.Electrical.Domain;
using MQTTnet;

namespace DND.Electrical.Twin;

public enum TwinQuality { Good, Stale, Bad, NotConnected }

/// <summary>A point value. <see cref="Simulated"/> is true for every value produced by the simulator and is shown in the UI.</summary>
public sealed record TwinValue(Guid PointId, double? Value, TwinQuality Quality, DateTimeOffset Timestamp, bool Simulated, string? Error = null);

public interface ITwinDataSource : IAsyncDisposable
{
    TwinSourceKind Kind { get; }
    Task<IReadOnlyList<TwinValue>> ReadAsync(IReadOnlyList<TwinPoint> points, CancellationToken ct);
}

/// <summary>
/// Built-in signal simulator for demonstrations and HMI/alarm tests without field connections. Produces a
/// deterministic sine between SimMin and SimMax per point; all values are flagged <c>Simulated</c>.
/// </summary>
public sealed class SimulatedSource(Func<DateTimeOffset>? clock = null) : ITwinDataSource
{
    public TwinSourceKind Kind => TwinSourceKind.Simulator;

    public Task<IReadOnlyList<TwinValue>> ReadAsync(IReadOnlyList<TwinPoint> points, CancellationToken ct)
    {
        var now = (clock ?? (() => DateTimeOffset.UtcNow))();
        var t = now.ToUnixTimeMilliseconds() / 1000.0;
        IReadOnlyList<TwinValue> values = points.Select(p =>
        {
            var phase = (p.Id.GetHashCode() & 0xFFFF) / 65535.0 * 2 * Math.PI;
            var mid = (p.SimMin + p.SimMax) / 2; var amp = (p.SimMax - p.SimMin) / 2;
            var v = mid + amp * Math.Sin(2 * Math.PI * t / Math.Max(1, p.SimPeriodS) + phase);
            return new TwinValue(p.Id, Math.Round(v, 4), TwinQuality.Good, now, Simulated: true);
        }).ToList();
        return Task.FromResult(values);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Reads points from one Modbus TCP server (endpoint host:port/unit).</summary>
public sealed class ModbusSource : ITwinDataSource
{
    private readonly string _host; private readonly int _port; private readonly byte _unit;
    private ModbusTcpClient? _client;
    public TwinSourceKind Kind => TwinSourceKind.ModbusTcp;

    public ModbusSource(string endpoint)
    {
        var slash = endpoint.Split('/');
        var hp = slash[0].Split(':');
        _host = hp[0]; _port = hp.Length > 1 ? int.Parse(hp[1], CultureInfo.InvariantCulture) : 502;
        _unit = slash.Length > 1 ? byte.Parse(slash[1], CultureInfo.InvariantCulture) : (byte)1;
    }

    public async Task<IReadOnlyList<TwinValue>> ReadAsync(IReadOnlyList<TwinPoint> points, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            if (_client is null || !_client.Connected)
            {
                if (_client is not null) await _client.DisposeAsync().ConfigureAwait(false);
                _client = new ModbusTcpClient(_unit);
                await _client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException)
        {
            _client = null;
            return points.Select(p => new TwinValue(p.Id, null, TwinQuality.NotConnected, now, false, $"{_host}:{_port}: {ex.Message}")).ToList();
        }
        var result = new List<TwinValue>();
        foreach (var p in points)
        {
            try
            {
                var a = ModbusAddress.Parse(p.Address);
                double raw = a.Table switch
                {
                    'H' => a.Decode(await _client.ReadHoldingRegistersAsync(a.Offset, a.Registers, ct).ConfigureAwait(false)),
                    'I' => a.Decode(await _client.ReadInputRegistersAsync(a.Offset, a.Registers, ct).ConfigureAwait(false)),
                    'C' => (await _client.ReadCoilsAsync(a.Offset, 1, ct).ConfigureAwait(false))[0] ? 1 : 0,
                    _ => (await _client.ReadDiscreteInputsAsync(a.Offset, 1, ct).ConfigureAwait(false))[0] ? 1 : 0,
                };
                result.Add(new TwinValue(p.Id, raw * p.Scale + p.Offset, TwinQuality.Good, DateTimeOffset.UtcNow, false));
            }
            catch (Exception ex) when (ex is IOException or FormatException or OperationCanceledException or ArgumentOutOfRangeException)
            {
                result.Add(new TwinValue(p.Id, null, TwinQuality.Bad, DateTimeOffset.UtcNow, false, ex.Message));
                if (ex is IOException and not ModbusException) { await _client.DisposeAsync().ConfigureAwait(false); _client = null; break; }
            }
        }
        foreach (var p in points.Where(p => result.All(r => r.PointId != p.Id))) result.Add(new TwinValue(p.Id, null, TwinQuality.NotConnected, now, false, "connection lost"));
        return result;
    }

    public async ValueTask DisposeAsync() { if (_client is not null) await _client.DisposeAsync().ConfigureAwait(false); }
}

/// <summary>
/// MQTT subscriber (MQTTnet): subscribes to the point topics and keeps the last value per topic. Payloads are a
/// number or JSON with a numeric "value" property. Values older than <see cref="StaleAfter"/> are reported Stale.
/// </summary>
public sealed class MqttSource : ITwinDataSource
{
    private readonly string _host; private readonly int _port;
    private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
    private readonly Dictionary<string, (double value, DateTimeOffset at)> _last = new(StringComparer.Ordinal);
    private readonly HashSet<string> _subscribed = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    public TwinSourceKind Kind => TwinSourceKind.Mqtt;
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(5);

    public MqttSource(string endpoint)
    {
        var hp = endpoint.Split(':');
        _host = hp[0]; _port = hp.Length > 1 ? int.Parse(hp[1], CultureInfo.InvariantCulture) : 1883;
        _client.ApplicationMessageReceivedAsync += e =>
        {
            var text = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            if (TryParse(text, out var v)) lock (_gate) _last[e.ApplicationMessage.Topic] = (v, DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        };
    }

    public static bool TryParse(string payload, out double value)
    {
        if (double.TryParse(payload.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("value", out var v) && v.TryGetDouble(out value)) return true;
        }
        catch (JsonException) { }
        value = 0;
        return false;
    }

    public async Task<IReadOnlyList<TwinValue>> ReadAsync(IReadOnlyList<TwinPoint> points, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            if (!_client.IsConnected)
                await _client.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(_host, _port).WithClientId($"dnd-twin-{Guid.NewGuid():N}").WithCleanSession().Build(), ct).ConfigureAwait(false);
            var missing = points.Select(p => p.Address).Where(t => !_subscribed.Contains(t)).Distinct().ToList();
            if (missing.Count > 0)
            {
                var b = new MqttClientSubscribeOptionsBuilder();
                foreach (var t in missing) b.WithTopicFilter(t);
                await _client.SubscribeAsync(b.Build(), ct).ConfigureAwait(false);
                foreach (var t in missing) _subscribed.Add(t);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return points.Select(p => new TwinValue(p.Id, null, TwinQuality.NotConnected, now, false, $"{_host}:{_port}: {ex.Message}")).ToList();
        }
        lock (_gate)
            return points.Select(p => _last.TryGetValue(p.Address, out var x)
                ? new TwinValue(p.Id, x.value * p.Scale + p.Offset, now - x.at > StaleAfter ? TwinQuality.Stale : TwinQuality.Good, x.at, false)
                : new TwinValue(p.Id, null, TwinQuality.Stale, now, false, "no message received yet")).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected) await _client.DisconnectAsync().ConfigureAwait(false);
        _client.Dispose();
    }
}

public sealed record ActiveAlarm(AlarmDefinition Alarm, TwinPoint Point, double Value, bool Simulated);

/// <summary>
/// Polls the twin points per source, keeps a bounded history per point and evaluates the project's analog alarm
/// definitions (with deadband) on the live values. OPC UA is not implemented: such points report NotConnected.
/// </summary>
public sealed class TwinEngine : IAsyncDisposable
{
    private readonly ElectricalProject _p;
    private readonly Func<TwinPoint, ITwinDataSource?> _factory;
    private readonly Dictionary<string, ITwinDataSource> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, Queue<TwinValue>> _history = new();
    private readonly HashSet<Guid> _active = new();
    public int HistoryLength { get; set; } = 600;
    public IReadOnlyDictionary<Guid, TwinValue> Latest => _latest;
    private readonly Dictionary<Guid, TwinValue> _latest = new();

    public TwinEngine(ElectricalProject project, Func<TwinPoint, ITwinDataSource?>? factory = null)
    {
        _p = project;
        _factory = factory ?? DefaultFactory;
    }

    private static ITwinDataSource? DefaultFactory(TwinPoint p) => p.Source switch
    {
        TwinSourceKind.Simulator => new SimulatedSource(),
        TwinSourceKind.ModbusTcp => new ModbusSource(p.Endpoint),
        TwinSourceKind.Mqtt => new MqttSource(p.Endpoint),
        _ => null,
    };

    public IReadOnlyList<TwinValue> History(Guid pointId) => _history.TryGetValue(pointId, out var q) ? q.ToList() : Array.Empty<TwinValue>();

    public async Task<IReadOnlyList<TwinValue>> PollAsync(CancellationToken ct = default)
    {
        var all = new List<TwinValue>();
        foreach (var g in _p.TwinPoints.GroupBy(p => $"{p.Source}|{p.Endpoint}"))
        {
            var first = g.First();
            if (!_sources.TryGetValue(g.Key, out var src))
            {
                var created = _factory(first);
                if (created is null)
                {
                    all.AddRange(g.Select(p => new TwinValue(p.Id, null, TwinQuality.NotConnected, DateTimeOffset.UtcNow, false, $"{p.Source} connector is not available in this version.")));
                    continue;
                }
                _sources[g.Key] = src = created;
            }
            all.AddRange(await src.ReadAsync(g.ToList(), ct).ConfigureAwait(false));
        }
        foreach (var v in all)
        {
            _latest[v.PointId] = v;
            if (!_history.TryGetValue(v.PointId, out var q)) _history[v.PointId] = q = new Queue<TwinValue>();
            q.Enqueue(v);
            while (q.Count > HistoryLength) q.Dequeue();
        }
        return all;
    }

    /// <summary>Alarm states from the latest values (instrument-linked analog alarms; deadband applied on return to normal).</summary>
    public IReadOnlyList<ActiveAlarm> EvaluateAlarms()
    {
        var result = new List<ActiveAlarm>();
        foreach (var point in _p.TwinPoints.Where(p => p.InstrumentId is not null))
        {
            if (!_latest.TryGetValue(point.Id, out var v) || v.Value is not { } x || v.Quality != TwinQuality.Good) continue;
            foreach (var a in _p.Alarms.Where(a => a.InstrumentId == point.InstrumentId && a.SetPoint is not null))
            {
                var sp = a.SetPoint!.Value;
                var wasActive = _active.Contains(a.Id);
                var high = a.Type is AlarmType.High or AlarmType.HighHigh;
                var low = a.Type is AlarmType.Low or AlarmType.LowLow;
                bool on = high ? (wasActive ? x > sp - a.Deadband : x > sp) : low ? (wasActive ? x < sp + a.Deadband : x < sp) : false;
                if (on) { _active.Add(a.Id); result.Add(new ActiveAlarm(a, point, x, v.Simulated)); }
                else _active.Remove(a.Id);
            }
        }
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var s in _sources.Values) await s.DisposeAsync().ConfigureAwait(false);
        _sources.Clear();
    }
}
