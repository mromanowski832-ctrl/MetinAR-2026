using System;
using System.Collections.Generic;
using System.Text;

namespace MetinAR.Map.Osm
{
    public sealed class MvtFormatException : Exception
    {
        public MvtFormatException(string message) : base(message)
        {
        }
    }

    public enum MvtGeometryType
    {
        Unknown = 0,
        Point = 1,
        LineString = 2,
        Polygon = 3
    }

    /// <summary>One ring / line / point list in integer tile-extent coordinates.</summary>
    public sealed class MvtRing
    {
        public List<int> Xs { get; } = new List<int>();

        public List<int> Ys { get; } = new List<int>();

        public int Count => Xs.Count;

        /// <summary>Surveyor's formula in tile coordinates (y down). Positive = exterior ring per MVT spec 2.1.</summary>
        public double SignedArea()
        {
            double sum = 0;
            int n = Xs.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                sum += (double)Xs[j] * Ys[i] - (double)Xs[i] * Ys[j];
            }

            return sum / 2.0;
        }
    }

    public sealed class MvtFeature
    {
        public ulong Id { get; set; }

        public MvtGeometryType Type { get; set; }

        public Dictionary<string, object> Properties { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>Command-decoded parts: points (one per MoveTo), line parts, or polygon rings in order.</summary>
        public List<MvtRing> Parts { get; } = new List<MvtRing>();
    }

    public sealed class MvtLayer
    {
        public string Name { get; set; } = string.Empty;

        public uint Version { get; set; } = 1;

        public uint Extent { get; set; } = 4096;

        public List<MvtFeature> Features { get; } = new List<MvtFeature>();
    }

    /// <summary>
    /// Project-owned, dependency-free Mapbox Vector Tile (spec v2.1) decoder: protobuf wire format,
    /// layers / keys / values / features, zig-zag geometry commands (MoveTo, LineTo, ClosePath).
    /// Input must already be decompressed (gzip is handled by the HTTP layer).
    /// Malformed data throws <see cref="MvtFormatException"/>; it never reads outside the buffer.
    /// </summary>
    public static class MvtReader
    {
        public static List<MvtLayer> Read(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
            {
                throw new MvtFormatException("Tile is gzip-compressed; decompress before decoding.");
            }

            var layers = new List<MvtLayer>();
            var r = new Pb(data, 0, data.Length);
            while (r.HasMore)
            {
                ulong key = r.Varint();
                int field = (int)(key >> 3);
                int wire = (int)(key & 7);
                if (field == 3 && wire == 2)
                {
                    Pb sub = r.Sub();
                    layers.Add(ReadLayer(sub));
                }
                else
                {
                    r.Skip(wire);
                }
            }

            return layers;
        }

        private static MvtLayer ReadLayer(Pb r)
        {
            var layer = new MvtLayer();
            var keys = new List<string>();
            var values = new List<object>();
            var rawFeatures = new List<Pb>();
            while (r.HasMore)
            {
                ulong key = r.Varint();
                int field = (int)(key >> 3);
                int wire = (int)(key & 7);
                switch (field)
                {
                    case 15 when wire == 0:
                        layer.Version = (uint)r.Varint();
                        break;
                    case 1 when wire == 2:
                        layer.Name = r.String();
                        break;
                    case 2 when wire == 2:
                        rawFeatures.Add(r.Sub());
                        break;
                    case 3 when wire == 2:
                        keys.Add(r.String());
                        break;
                    case 4 when wire == 2:
                        values.Add(ReadValue(r.Sub()));
                        break;
                    case 5 when wire == 0:
                        layer.Extent = (uint)r.Varint();
                        break;
                    default:
                        r.Skip(wire);
                        break;
                }
            }

            if (layer.Extent == 0)
            {
                throw new MvtFormatException("Layer extent must be > 0.");
            }

            foreach (Pb fr in rawFeatures)
            {
                layer.Features.Add(ReadFeature(fr, keys, values));
            }

            return layer;
        }

        private static object ReadValue(Pb r)
        {
            object value = null;
            while (r.HasMore)
            {
                ulong key = r.Varint();
                int field = (int)(key >> 3);
                int wire = (int)(key & 7);
                switch (field)
                {
                    case 1 when wire == 2:
                        value = r.String();
                        break;
                    case 2 when wire == 5:
                        value = (double)BitConverter.ToSingle(BitConverter.GetBytes(r.Fixed32()), 0);
                        break;
                    case 3 when wire == 1:
                        value = BitConverter.Int64BitsToDouble((long)r.Fixed64());
                        break;
                    case 4 when wire == 0:
                        value = (double)(long)r.Varint();
                        break;
                    case 5 when wire == 0:
                        value = (double)r.Varint();
                        break;
                    case 6 when wire == 0:
                        value = (double)ZigZag(r.Varint());
                        break;
                    case 7 when wire == 0:
                        value = r.Varint() != 0;
                        break;
                    default:
                        r.Skip(wire);
                        break;
                }
            }

            return value;
        }

        private static MvtFeature ReadFeature(Pb r, List<string> keys, List<object> values)
        {
            var f = new MvtFeature();
            List<uint> tags = null;
            List<uint> geometry = null;
            while (r.HasMore)
            {
                ulong key = r.Varint();
                int field = (int)(key >> 3);
                int wire = (int)(key & 7);
                switch (field)
                {
                    case 1 when wire == 0:
                        f.Id = r.Varint();
                        break;
                    case 2 when wire == 2:
                        tags = r.PackedUInt32();
                        break;
                    case 3 when wire == 0:
                        ulong t = r.Varint();
                        f.Type = t <= 3 ? (MvtGeometryType)t : MvtGeometryType.Unknown;
                        break;
                    case 4 when wire == 2:
                        geometry = r.PackedUInt32();
                        break;
                    default:
                        r.Skip(wire);
                        break;
                }
            }

            if (tags != null)
            {
                for (int i = 0; i + 1 < tags.Count; i += 2)
                {
                    int k = (int)tags[i];
                    int v = (int)tags[i + 1];
                    if (k < 0 || k >= keys.Count || v < 0 || v >= values.Count)
                    {
                        throw new MvtFormatException("Feature tag index out of range.");
                    }

                    f.Properties[keys[k]] = values[v];
                }
            }

            if (geometry != null)
            {
                DecodeGeometry(geometry, f);
            }

            return f;
        }

        private static void DecodeGeometry(List<uint> g, MvtFeature f)
        {
            int x = 0;
            int y = 0;
            int i = 0;
            MvtRing current = null;
            while (i < g.Count)
            {
                uint cmdInt = g[i++];
                uint cmd = cmdInt & 0x7;
                uint count = cmdInt >> 3;
                if (cmd == 1 || cmd == 2)
                {
                    if ((long)i + (long)count * 2 > g.Count)
                    {
                        throw new MvtFormatException("Geometry command parameters exceed buffer.");
                    }

                    for (uint c = 0; c < count; c++)
                    {
                        x += ZigZag32(g[i++]);
                        y += ZigZag32(g[i++]);
                        if (cmd == 1)
                        {
                            current = new MvtRing();
                            f.Parts.Add(current);
                        }
                        else if (current == null)
                        {
                            throw new MvtFormatException("LineTo before MoveTo.");
                        }

                        current.Xs.Add(x);
                        current.Ys.Add(y);
                    }
                }
                else if (cmd == 7)
                {
                    // ClosePath: ring is implicitly closed; nothing to add.
                    if (current == null)
                    {
                        throw new MvtFormatException("ClosePath before MoveTo.");
                    }
                }
                else
                {
                    throw new MvtFormatException("Unknown geometry command " + cmd);
                }
            }
        }

        private static long ZigZag(ulong n) => (long)(n >> 1) ^ -(long)(n & 1);

        private static int ZigZag32(uint n) => (int)(n >> 1) ^ -(int)(n & 1);

        /// <summary>Bounds-checked protobuf reader over a byte segment.</summary>
        private struct Pb
        {
            private readonly byte[] _buf;
            private int _pos;
            private readonly int _end;

            public Pb(byte[] buf, int start, int end)
            {
                _buf = buf;
                _pos = start;
                _end = end;
            }

            public bool HasMore => _pos < _end;

            public ulong Varint()
            {
                ulong result = 0;
                int shift = 0;
                while (true)
                {
                    if (_pos >= _end)
                    {
                        throw new MvtFormatException("Truncated varint.");
                    }

                    byte b = _buf[_pos++];
                    result |= (ulong)(b & 0x7f) << shift;
                    if ((b & 0x80) == 0)
                    {
                        return result;
                    }

                    shift += 7;
                    if (shift > 63)
                    {
                        throw new MvtFormatException("Varint too long.");
                    }
                }
            }

            public uint Fixed32()
            {
                Need(4);
                uint v = (uint)(_buf[_pos] | _buf[_pos + 1] << 8 | _buf[_pos + 2] << 16 | _buf[_pos + 3] << 24);
                _pos += 4;
                return v;
            }

            public ulong Fixed64()
            {
                Need(8);
                ulong lo = (uint)(_buf[_pos] | _buf[_pos + 1] << 8 | _buf[_pos + 2] << 16 | _buf[_pos + 3] << 24);
                ulong hi = (uint)(_buf[_pos + 4] | _buf[_pos + 5] << 8 | _buf[_pos + 6] << 16 | _buf[_pos + 7] << 24);
                _pos += 8;
                return lo | hi << 32;
            }

            public Pb Sub()
            {
                int len = Length();
                var sub = new Pb(_buf, _pos, _pos + len);
                _pos += len;
                return sub;
            }

            public string String()
            {
                int len = Length();
                string s = Encoding.UTF8.GetString(_buf, _pos, len);
                _pos += len;
                return s;
            }

            public List<uint> PackedUInt32()
            {
                Pb sub = Sub();
                var list = new List<uint>();
                while (sub.HasMore)
                {
                    list.Add((uint)sub.Varint());
                }

                return list;
            }

            public void Skip(int wire)
            {
                switch (wire)
                {
                    case 0:
                        Varint();
                        break;
                    case 1:
                        Need(8);
                        _pos += 8;
                        break;
                    case 2:
                        _pos += Length();
                        break;
                    case 5:
                        Need(4);
                        _pos += 4;
                        break;
                    default:
                        throw new MvtFormatException("Unsupported wire type " + wire);
                }
            }

            private int Length()
            {
                ulong len = Varint();
                if (len > (ulong)(_end - _pos))
                {
                    throw new MvtFormatException("Length-delimited field exceeds buffer.");
                }

                return (int)len;
            }

            private void Need(int n)
            {
                if (_end - _pos < n)
                {
                    throw new MvtFormatException("Truncated fixed-width field.");
                }
            }
        }
    }
}
