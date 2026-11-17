#if LAN_PROBE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    // Buffered, test-only tracing. No protocol fields, timing limits or queues are changed.
    // Use timestamps only within each process: Unity Mono can use different origins.
    internal static class LatencyTrace {
        const string Owner = "shop_together.latency-trace";
        static readonly List<string> events = new List<string>();
        static readonly Dictionary<byte[], string> packets = new Dictionary<byte[], string>();
        static Harmony patcher;
        static string sendingWorld;
        static long previousUpdate;
        static int previousGc;
        static double Ms(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }
        static void Note(string stage, string detail, long at, double elapsed = 0) {
            if (events.Count < 2000) events.Add(Ms(at).ToString("F3", CultureInfo.InvariantCulture) + " " + stage + " " + detail + " ms=" + elapsed.ToString("F3", CultureInfo.InvariantCulture) + " frame=" + Time.frameCount + " gc=" + GC.CollectionCount(0));
        }
        static string Name(RemoteCall call) {
            if (call == null || call.values == null) return null;
            foreach (var value in call.values) if (value != null && value.StartsWith("R139_PERF_", StringComparison.Ordinal)) return value + " seq=" + call.sequence;
            return null;
        }
        static string Name(WorldDelta delta) {
            if (delta == null || delta.items == null) return null;
            foreach (var item in delta.items) {
                TagState tag;
                if (item.node != null && item.node.itemState != null && item.node.itemState.dict.TryGetValue("CUSTOM_NAME_TAG", out tag) && tag.valueString != null && tag.valueString.StartsWith("R139_PERF_", StringComparison.Ordinal))
                    return tag.valueString + " version=" + delta.version;
            }
            return null;
        }
        static void Hook(MethodBase method, string before, string after) {
            if (method == null) throw new InvalidOperationException("Missing latency trace target");
            patcher.Patch(method, before == null ? null : new HarmonyMethod(typeof(LatencyTrace), before), after == null ? null : new HarmonyMethod(typeof(LatencyTrace), after));
        }
        internal static void Begin() {
            if (patcher != null) return;
            var lookup = Stopwatch.StartNew(); int absent = 0;
            foreach (var name in new[] { "CorpPresence.Company", "CorpPresence.CargoWarehouse", "ShopStoragePages.Pages", "BartenderMod.BarSession", "TrashOverflow", "CorpPresence.Runtime.LoadedItemRepair", "CorpPresence.Crates", "CorpPresence.Runtime.RuntimeUpdates" })
                if (AccessTools.TypeByName(name) == null) absent++;
            lookup.Stop();
            UnityEngine.Debug.Log("[SMC PERF] optional-mod-lookup absent=" + absent + " legacySweepMs=" + lookup.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
            events.Clear(); packets.Clear(); previousUpdate = 0; previousGc = GC.CollectionCount(0);
            patcher = new Harmony(Owner);
            Hook(AccessTools.Method(typeof(Connection), "SendRemote"), "RequestBefore", "RequestAfter");
            Hook(AccessTools.Method(typeof(RemoteActions), "Execute"), "ExecuteBefore", "ExecuteAfter");
            foreach (var method in typeof(WorldReplica).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
                if (method.Name == "Capture" && method.GetParameters().Length == 5) Hook(method, "ClockBefore", "CaptureAfter");
            Hook(AccessTools.Method(typeof(Connection), "SendWorld"), "SendWorldBefore", "SendWorldAfter");
            Hook(AccessTools.Method(typeof(Connection).GetNestedType("Peer", BindingFlags.NonPublic), "Send"), null, "Queued");
            Hook(AccessTools.Method(typeof(SecureChannel), "Send"), "WriteBefore", "WriteAfter");
            Hook(AccessTools.Method(typeof(WorldReplica), "Apply"), "ApplyBefore", "ApplyAfter");
            Hook(AccessTools.Method(typeof(Connection), "Update"), "UpdateBefore", "UpdateAfter");
            Hook(AccessTools.Method(typeof(Connection), "Pump"), "ClockBefore", "SlowAfter");
            Hook(AccessTools.Method(typeof(Connection), "Receive"), "ClockBefore", "SlowAfter");
            Hook(AccessTools.Method(typeof(WorldReplica), "RunLoads"), "ClockBefore", "SlowAfter");
            Hook(AccessTools.Method(typeof(WorldReplica), "Index"), "ClockBefore", "SlowAfter");
            Hook(AccessTools.Method(typeof(DriftCheck), "Step"), "ClockBefore", "SlowAfter");
            Hook(AccessTools.Method(typeof(StoreState), "Serialize"), "ClockBefore", "SlowAfter");
            Hook(AccessTools.Method(typeof(StoreState), "SerializeStation"), "ClockBefore", "SlowAfter");
            Hook(AccessTools.Method(typeof(MachineDisplay), "Tick"), "ClockBefore", "SlowAfter");
            Hook(AccessTools.Method(typeof(ModRuntimeAuthority), "Tick"), "ClockBefore", "SlowAfter");
        }
        internal static void End() {
            if (patcher == null) return;
            patcher.UnpatchAll(Owner); patcher = null;
            foreach (var line in events) UnityEngine.Debug.Log("[SMC PERF TRACE] " + line);
            events.Clear(); packets.Clear(); sendingWorld = null;
        }
        static void ClockBefore(out long __state) { __state = Stopwatch.GetTimestamp(); }
        static void SlowAfter(MethodBase __originalMethod, long __state) {
            double elapsed = Ms(Stopwatch.GetTimestamp() - __state);
            if (elapsed >= 10) Note("slow", __originalMethod.DeclaringType.Name + "." + __originalMethod.Name, __state, elapsed);
        }
        static void RequestBefore(RemoteCall call, out long __state) { __state = Stopwatch.GetTimestamp(); }
        static void RequestAfter(RemoteCall call, long __state) { var name = Name(call); if (name != null) Note("request-queued", name, __state, Ms(Stopwatch.GetTimestamp() - __state)); }
        static void ExecuteBefore(RemoteCall call, out long __state) { __state = Stopwatch.GetTimestamp(); var name = Name(call); if (name != null) Note("host-execute-start", name, __state); }
        static void ExecuteAfter(RemoteCall call, long __state) { var name = Name(call); if (name != null) Note("host-execute-end", name, Stopwatch.GetTimestamp(), Ms(Stopwatch.GetTimestamp() - __state)); }
        static void CaptureAfter(WorldDelta __result, long __state) { var name = Name(__result); double elapsed = Ms(Stopwatch.GetTimestamp() - __state); if (name != null || elapsed >= 25) Note("capture-end", name ?? "world", Stopwatch.GetTimestamp(), elapsed); }
        static void SendWorldBefore(WorldDelta delta, out long __state) { __state = Stopwatch.GetTimestamp(); sendingWorld = Name(delta); }
        static void SendWorldAfter(long __state) { double elapsed = Ms(Stopwatch.GetTimestamp() - __state); if (sendingWorld != null || elapsed >= 10) Note("world-queued", sendingWorld ?? "world", Stopwatch.GetTimestamp(), elapsed); sendingWorld = null; }
        static void Queued(object __instance, byte type, byte[] bytes) {
            string name = sendingWorld;
            if (type == 36) name = Name(WireJson.FromJson<RemoteCall>(System.Text.Encoding.UTF8.GetString(bytes)));
            else if (type != 20 && type != 26) return;
            if (name == null) return;
            var queue = AccessTools.Field(__instance.GetType(), "outgoing").GetValue(__instance) as Queue<byte[]>;
            byte[] last = null; foreach (var packet in queue) last = packet;
            if (last != null) packets[last] = (type == 36 ? "request " : "world ") + name;
        }
        static void WriteBefore(byte[] packet, out long __state) { __state = Stopwatch.GetTimestamp(); string name; if (packets.TryGetValue(packet, out name)) Note("tls-write-start", name, __state); }
        static void WriteAfter(byte[] packet, long __state) { string name; if (packets.TryGetValue(packet, out name)) { Note("tls-write-return", name, Stopwatch.GetTimestamp(), Ms(Stopwatch.GetTimestamp() - __state)); packets.Remove(packet); } }
        static void ApplyBefore(WorldDelta delta, out long __state) { __state = Stopwatch.GetTimestamp(); var name = Name(delta); if (name != null) Note("apply-start", name, __state); }
        static void ApplyAfter(WorldDelta delta, long __state) { var name = Name(delta); double elapsed = Ms(Stopwatch.GetTimestamp() - __state); if (name != null || elapsed >= 25) Note("apply-end", name ?? "world", Stopwatch.GetTimestamp(), elapsed); }
        static void UpdateBefore(out long __state) {
            __state = Stopwatch.GetTimestamp(); int gc = GC.CollectionCount(0);
            if (previousUpdate != 0 && Ms(__state - previousUpdate) >= 50) Note("update-gap", "collections=" + (gc - previousGc), __state, Ms(__state - previousUpdate));
            previousUpdate = __state; previousGc = gc;
        }
        static void UpdateAfter(long __state) { double elapsed = Ms(Stopwatch.GetTimestamp() - __state); if (elapsed >= 25) Note("update-work", "Connection.Update", __state, elapsed); }
    }
}
#endif
