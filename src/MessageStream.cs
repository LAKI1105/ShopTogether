using System;
using System.Collections.Generic;

namespace ShopTogether {
    /// <summary>
    /// Turns a message-based link (Steam peer-to-peer) into the byte stream the game protocol reads:
    /// outgoing data is cut into pieces no larger than the link allows, incoming pieces are read back in order.
    /// Pure logic, no Steam or Unity, so it is covered by the standalone tests.
    /// </summary>
    internal sealed class MessageStream {
        internal const int Piece = 256 * 1024;
        internal const long MaxQueued = 64L * 1024 * 1024;
        readonly Func<byte[], int, bool> send;
        readonly Queue<byte[]> inbound = new Queue<byte[]>();
        readonly byte[] piece = new byte[Piece];
        byte[] current; int offset; long queued;
        internal bool Failed, Closed;

        internal MessageStream(Func<byte[], int, bool> send) { this.send = send; }

        internal void Arrive(byte[] data) {
            if (Closed || data == null || data.Length == 0) return;
            queued += data.Length;
            if (queued > MaxQueued) { Failed = true; return; }   // the protocol guard allows far less; this only bounds memory
            inbound.Enqueue(data);
        }

        /// <summary>-1 nothing yet, 0 closed, otherwise bytes copied.</summary>
        internal int Receive(byte[] destination) {
            if (Failed || Closed) return 0;
            if (current == null) { if (inbound.Count == 0) return -1; current = inbound.Dequeue(); offset = 0; }
            int n = Math.Min(destination.Length, current.Length - offset);
            Buffer.BlockCopy(current, offset, destination, 0, n);
            offset += n; queued -= n;
            if (offset >= current.Length) current = null;
            return n;
        }

        // Over the internet Steam's send buffer fills up during a big update and refuses more for a while.
        // That is back-pressure, not a broken link: keep the rest and hand it over as Steam accepts it.
        internal const long MaxOutbound = 256L * 1024 * 1024;
        internal const double StallLimit = 90;     // seconds without Steam accepting anything
        readonly Queue<byte[]> outbound = new Queue<byte[]>();
        int outOffset; long outQueued; double stalledSince = -1;
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        internal Func<double> Now = () => clock.Elapsed.TotalSeconds;
        internal bool Pending { get { return outbound.Count > 0; } }

        internal void Send(byte[] packet) {
            if (Failed || Closed) throw new InvalidOperationException("~SMC131~");
            if (packet == null || packet.Length == 0) return;
            if (outQueued + packet.Length > MaxOutbound) { Failed = true; throw new InvalidOperationException("~SMC152~"); }
            outbound.Enqueue(packet); outQueued += packet.Length;
            Flush();
        }

        /// <summary>Hands queued data to the link until it refuses; called again every frame.</summary>
        internal void Flush() {
            if (Failed || Closed) return;
            while (outbound.Count > 0) {
                var packet = outbound.Peek();
                int n = Math.Min(Piece, packet.Length - outOffset);
                Buffer.BlockCopy(packet, outOffset, piece, 0, n);
                if (!send(piece, n)) {
                    double now = Now();
                    if (stalledSince < 0) stalledSince = now;
                    else if (now - stalledSince >= StallLimit) Failed = true;
                    return;
                }
                stalledSince = -1; outOffset += n; outQueued -= n;
                if (outOffset >= packet.Length) { outbound.Dequeue(); outOffset = 0; }
            }
        }

        internal void Close() { Closed = true; inbound.Clear(); current = null; outbound.Clear(); outQueued = 0; }
    }
}
