using System;
using System.Collections.Generic;
using Steamworks;

namespace ShopTogether {
    /// <summary>
    /// Steam peer-to-peer: Steam finds the route between two players (direct when possible, otherwise through
    /// Valve's relay servers), so nobody has to open router ports. Steam authenticates who is on the other end
    /// and encrypts the traffic; the room password and the protocol guard still apply on top.
    /// Only the old, fully managed ISteamNetworking API is used: no native memory access is needed.
    /// </summary>
    internal static class SteamRelay {
        const int Channel = 47;
        const int MaxPacket = 1024 * 1024;
        static Callback<P2PSessionRequest_t> requestCallback;
        static Callback<P2PSessionConnectFail_t> failCallback;
        static readonly Dictionary<ulong, SteamChannel> open = new Dictionary<ulong, SteamChannel>();
        static readonly Queue<SteamChannel> arrivals = new Queue<SteamChannel>();
        static readonly byte[] scratch = new byte[MaxPacket];
        static Func<ulong, bool> admit;               // host: decides whether a new friend may connect
        static ulong expectedHost;                     // friend: the only Steam account accepted
        internal static string LastError = "";

        // While the game shuts down, asking SteamManager would recreate it and try to start Steam a second time.
        internal static bool ShuttingDown;
        internal static bool Available { get { if (ShuttingDown) return false; try { return SteamManager.Initialized; } catch { return false; } } }
        internal static ulong LocalId { get { try { return Available ? SteamUser.GetSteamID().m_SteamID : 0; } catch { return 0; } } }

        /// <summary>A regular person's Steam account in the public universe.</summary>
        internal static bool ValidPerson(ulong id) { return (id >> 56) == 1 && ((id >> 52) & 0xF) == 1 && (id & 0xFFFFFFFF) != 0; }

        static void Hook() {
            if (requestCallback != null) return;
            requestCallback = Callback<P2PSessionRequest_t>.Create(OnRequest);
            failCallback = Callback<P2PSessionConnectFail_t>.Create(OnFail);
            SteamNetworking.AllowP2PPacketRelay(true);
        }

        internal static void Host(Func<ulong, bool> admission) { Hook(); admit = admission; expectedHost = 0; }
        internal static void StopHosting() { admit = null; arrivals.Clear(); }

        internal static SteamChannel Connect(ulong host) {
            if (!Available) throw new InvalidOperationException("~SMC187~");
            if (!ValidPerson(host) || host == LocalId) throw new InvalidOperationException("~SMC139~");
            Hook(); expectedHost = host;
            var channel = new SteamChannel(host);
            open[host] = channel;
            return channel;
        }

        internal static SteamChannel NextArrival() { return arrivals.Count > 0 ? arrivals.Dequeue() : null; }

        static void OnRequest(P2PSessionRequest_t request) {
            ulong id = request.m_steamIDRemote.m_SteamID;
            if (!ValidPerson(id)) return;
            if (expectedHost != 0) { if (id == expectedHost) SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote); return; }
            if (admit == null || open.ContainsKey(id) || !admit(id)) return;
            SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
            var channel = new SteamChannel(id);
            open[id] = channel; arrivals.Enqueue(channel);
        }

        static void OnFail(P2PSessionConnectFail_t failure) {
            SteamChannel channel;
            if (open.TryGetValue(failure.m_steamIDRemote.m_SteamID, out channel)) { channel.Failed = true; LastError = "~SMC189~" + failure.m_eP2PSessionError + "）"; }
        }

        /// <summary>Called every frame: moves arrived Steam packets into their connection.</summary>
        internal static void Poll() {
            if (requestCallback == null || !Available) return;
            uint size;
            for (int i = 0; i < 64 && SteamNetworking.IsP2PPacketAvailable(out size, Channel); i++) {
                uint read; CSteamID from;
                if (size > MaxPacket) { SteamNetworking.ReadP2PPacket(scratch, (uint)scratch.Length, out read, out from, Channel); Fail(from.m_SteamID); continue; }
                if (!SteamNetworking.ReadP2PPacket(scratch, (uint)scratch.Length, out read, out from, Channel)) break;
                SteamChannel channel;
                if (!open.TryGetValue(from.m_SteamID, out channel)) continue;   // never accepted: dropped
                var data = new byte[read]; Buffer.BlockCopy(scratch, 0, data, 0, (int)read);
                channel.Arrive(data);
            }
        }
        static void Fail(ulong id) { SteamChannel channel; if (open.TryGetValue(id, out channel)) channel.Failed = true; }

        internal static void Close(SteamChannel channel) {
            SteamChannel current;
            if (open.TryGetValue(channel.Remote, out current) && current == channel) open.Remove(channel.Remote);
            try { SteamNetworking.CloseP2PSessionWithUser(new CSteamID(channel.Remote)); } catch { }
        }

        internal static void CloseAll() {
            foreach (var channel in new List<SteamChannel>(open.Values)) channel.Dispose();
            open.Clear(); arrivals.Clear(); expectedHost = 0; admit = null;
        }

        internal static bool SendRaw(ulong remote, byte[] data, int length) {
            return SteamNetworking.SendP2PPacket(new CSteamID(remote), data, (uint)length, EP2PSend.k_EP2PSendReliable, Channel);
        }

        internal sealed class SteamChannel : IChannel {
            internal readonly ulong Remote;
            readonly MessageStream stream;
            const float Silence = 60f;                 // the other side always talks at least every few seconds
            float lastArrival = UnityEngine.Time.realtimeSinceStartup;
            internal SteamChannel(ulong remote) { Remote = remote; stream = new MessageStream((data, length) => SendRaw(remote, data, length)); }
            internal bool Failed { get { return stream.Failed; } set { stream.Failed = value; } }
            public bool Ready { get { return !stream.Closed; } }
            public bool Writing { get { stream.Flush(); if (stream.Failed) throw new InvalidOperationException("~SMC152~"); return stream.Pending; } }
            internal void Arrive(byte[] data) { lastArrival = UnityEngine.Time.realtimeSinceStartup; stream.Arrive(data); }
            public int Receive(byte[] destination) {
                int n = stream.Receive(destination);
                if (n < 0 && !stream.Pending && UnityEngine.Time.realtimeSinceStartup - lastArrival > Silence) return 0;   // Steam gives no close signal: silence means gone
                return n;
            }
            public void Send(byte[] packet) { stream.Send(packet); }
            public void Dispose() { if (stream.Closed) return; stream.Close(); Close(this); }
        }
    }
}
