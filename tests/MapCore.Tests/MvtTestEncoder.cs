using System.Text;

namespace MapCore.Tests;

/// <summary>
/// Test-only Mapbox Vector Tile encoder used to SELF-GENERATE fixtures (no provider tile bytes in the repo).
/// </summary>
internal sealed class MvtTestEncoder
{
    private readonly List<(string Name, uint Extent, List<(int Type, List<(string, object)> Props, List<uint> Geometry)> Features)> _layers = new();

    public MvtTestEncoder Layer(string name, uint extent = 4096)
    {
        _layers.Add((name, extent, new()));
        return this;
    }

    public MvtTestEncoder Feature(int type, List<uint> geometry, params (string, object)[] props)
    {
        _layers[^1].Features.Add((type, props.ToList(), geometry));
        return this;
    }

    public static uint Cmd(uint id, uint count) => (id & 7) | (count << 3);

    public static uint Zz(int v) => (uint)((v << 1) ^ (v >> 31));

    /// <summary>Geometry for absolute integer rings/lines; polygon=true appends ClosePath per ring.</summary>
    public static List<uint> Geom(bool polygon, params (int X, int Y)[][] parts)
    {
        var g = new List<uint>();
        int cx = 0, cy = 0;
        foreach (var part in parts)
        {
            g.Add(Cmd(1, 1));
            g.Add(Zz(part[0].X - cx)); g.Add(Zz(part[0].Y - cy));
            cx = part[0].X; cy = part[0].Y;
            int n = part.Length - 1;
            if (n > 0)
            {
                g.Add(Cmd(2, (uint)n));
                for (int i = 1; i < part.Length; i++)
                {
                    g.Add(Zz(part[i].X - cx)); g.Add(Zz(part[i].Y - cy));
                    cx = part[i].X; cy = part[i].Y;
                }
            }

            if (polygon)
            {
                g.Add(Cmd(7, 1));
            }
        }

        return g;
    }

    public byte[] Build()
    {
        var tile = new List<byte>();
        foreach (var layer in _layers)
        {
            var l = new List<byte>();
            Varint(l, (15 << 3) | 0); Varint(l, 2);
            Bytes(l, 1, Encoding.UTF8.GetBytes(layer.Name));
            var keys = new List<string>();
            var values = new List<object>();
            var feats = new List<byte[]>();
            foreach (var f in layer.Features)
            {
                var fb = new List<byte>();
                var tags = new List<uint>();
                foreach (var (k, v) in f.Props)
                {
                    int ki = keys.IndexOf(k); if (ki < 0) { keys.Add(k); ki = keys.Count - 1; }
                    int vi = values.IndexOf(v); if (vi < 0) { values.Add(v); vi = values.Count - 1; }
                    tags.Add((uint)ki); tags.Add((uint)vi);
                }

                Packed(fb, 2, tags);
                Varint(fb, (3 << 3) | 0); Varint(fb, (ulong)f.Type);
                Packed(fb, 4, f.Geometry);
                feats.Add(fb.ToArray());
            }

            foreach (var fb in feats) Bytes(l, 2, fb);
            foreach (var k in keys) Bytes(l, 3, Encoding.UTF8.GetBytes(k));
            foreach (var v in values) Bytes(l, 4, Value(v));
            Varint(l, (5 << 3) | 0); Varint(l, layer.Extent);
            Bytes(tile, 3, l.ToArray());
        }

        return tile.ToArray();
    }

    private static byte[] Value(object v)
    {
        var b = new List<byte>();
        switch (v)
        {
            case string s: Bytes(b, 1, Encoding.UTF8.GetBytes(s)); break;
            case double d: Varint(b, (3 << 3) | 1); b.AddRange(BitConverter.GetBytes(d)); break;
            case float fl: Varint(b, (2 << 3) | 5); b.AddRange(BitConverter.GetBytes(fl)); break;
            case int i: Varint(b, (6 << 3) | 0); Varint(b, Zz(i)); break;
            case bool bo: Varint(b, (7 << 3) | 0); Varint(b, bo ? 1UL : 0UL); break;
            default: throw new ArgumentException("unsupported value");
        }

        return b.ToArray();
    }

    private static void Packed(List<byte> o, int field, List<uint> vals)
    {
        var p = new List<byte>();
        foreach (var v in vals) Varint(p, v);
        Bytes(o, field, p.ToArray());
    }

    private static void Bytes(List<byte> o, int field, byte[] data)
    {
        Varint(o, (ulong)((field << 3) | 2));
        Varint(o, (ulong)data.Length);
        o.AddRange(data);
    }

    private static void Varint(List<byte> o, ulong v)
    {
        while (v >= 0x80) { o.Add((byte)(v | 0x80)); v >>= 7; }
        o.Add((byte)v);
    }
}
