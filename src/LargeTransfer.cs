using System;
using System.IO;
using System.IO.Compression;

namespace ShopTogether {
    /// <summary>
    /// A big store (thousands of items) does not fit in one packet. Large world updates are compressed and sent
    /// as numbered pieces; the friend's machine puts them back together, with hard size limits on both the pieces
    /// and the unpacked result. Small updates are sent as they are.
    /// </summary>
    internal static class LargeTransfer {
        internal const int Header = 9;                       // transfer id (4) + piece index (2) + piece count (2) + inner type (1)
        internal const int Chunk = 512 * 1024;
        internal const int MaxPieces = 512;                  // 256 MB compressed at most
        internal const int MaxPayload = 256 * 1024 * 1024;   // unpacked limit
        const int Direct = 256 * 1024;
        static uint nextId;

        internal sealed class Packed { internal byte Type; internal byte[][] Parts; }

        internal static Packed Pack(byte inner, byte[] bytes) {
            if (bytes.Length <= Direct) return new Packed { Type = inner, Parts = new[] { bytes } };
            byte[] zipped;
            using (var output = new MemoryStream()) {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) gzip.Write(bytes, 0, bytes.Length);
                zipped = output.ToArray();
            }
            int count = (zipped.Length + Chunk - 1) / Chunk;
            if (count > MaxPieces) throw new InvalidOperationException("~SMC075~");
            uint id = ++nextId;
            var parts = new byte[count][];
            for (int i = 0; i < count; i++) {
                int offset = i * Chunk, length = Math.Min(Chunk, zipped.Length - offset);
                var part = new byte[Header + length];
                part[0] = (byte)(id >> 24); part[1] = (byte)(id >> 16); part[2] = (byte)(id >> 8); part[3] = (byte)id;
                part[4] = (byte)(i >> 8); part[5] = (byte)i; part[6] = (byte)(count >> 8); part[7] = (byte)count; part[8] = inner;
                Buffer.BlockCopy(zipped, offset, part, Header, length);
                parts[i] = part;
            }
            return new Packed { Type = 26, Parts = parts };
        }

        internal static void Deliver(Action<byte, byte[]> send, Packed packed) { foreach (var part in packed.Parts) send(packed.Type, part); }

        /// <summary>Friend side. Only world updates (20) and drift checks (32) may arrive in pieces.</summary>
        internal sealed class Assembler {
            uint id; int count, received; byte inner; byte[][] parts;

            internal void Reset() { parts = null; count = 0; received = 0; }

            internal byte[] Add(byte[] data, out byte innerType) {
                innerType = 0;
                if (data == null || data.Length <= Header || data.Length > Header + Chunk) throw new InvalidOperationException("~SMC031~");
                uint pid = (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);
                int index = data[4] << 8 | data[5], total = data[6] << 8 | data[7];
                byte type = data[8];
                if ((type != 20 && type != 32) || total < 1 || total > MaxPieces || index >= total) throw new InvalidOperationException("~SMC031~");
                if (parts == null || pid != id) { id = pid; count = total; received = 0; inner = type; parts = new byte[total][]; }
                if (total != count || type != inner || parts[index] != null) throw new InvalidOperationException("~SMC031~");
                var piece = new byte[data.Length - Header];
                Buffer.BlockCopy(data, Header, piece, 0, piece.Length);
                parts[index] = piece; received++;
                if (received < count) return null;
                byte[] whole;
                using (var zipped = new MemoryStream()) {
                    foreach (var p in parts) zipped.Write(p, 0, p.Length);
                    zipped.Position = 0;
                    whole = Unzip(zipped);
                }
                innerType = inner; Reset();
                return whole;
            }

            static byte[] Unzip(Stream source) {
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                using (var output = new MemoryStream()) {
                    var buffer = new byte[81920]; int n;
                    while ((n = gzip.Read(buffer, 0, buffer.Length)) > 0) {
                        if (output.Length + n > MaxPayload) throw new InvalidOperationException("~SMC075~");
                        output.Write(buffer, 0, n);
                    }
                    return output.ToArray();
                }
            }
        }
    }
}
