using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    // Guests render the host's captured widget values; no gameplay callback runs here.
    internal static class MachineDisplay {
        internal static bool Running;
        static float next;
        internal static void Tick(Connection s) {
            if (s == null || !s.IsClient || !s.Ready || !ReplicaSession.ClientStore || !ReplicaSession.SceneReady || EmporiumEntry.Instance == null || Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + .1f;
            Running = true;
            try { s.World.Readouts.ApplyOpen(s.World); HopperReadout.Tick(); ScannerIntent.RefreshOpen(s.World); }
            finally { Running = false; }
        }
    }

    [HarmonyPatch(typeof(GameItem), "ModifyTag", new[] { typeof(string), typeof(Action<TagState>), typeof(bool) })]
    internal static class DisplayOnlyModify {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(GameItem __instance, ref GameItem __result) { if (!MachineDisplay.Running && WorldReplica.Sandbox == 0) return true; __result = __instance; return false; }
    }

    [HarmonyPatch]
    internal static class DisplayOnlyToggle {
        static IEnumerable<MethodBase> TargetMethods() {
            yield return typeof(GameItem).GetMethod("EnableTag", new[] { typeof(string), typeof(bool) });
            yield return typeof(GameItem).GetMethod("DisableTag", new[] { typeof(string), typeof(bool) });
        }
        [HarmonyPriority(Priority.First)]
        static bool Prefix(ref bool __result) { if (!MachineDisplay.Running && WorldReplica.Sandbox == 0) return true; __result = false; return false; }
    }

    /// <summary>
    /// Host: temperatures, reactions and liquid transfers change tags without the game's item check
    /// (skipValidate), so the normal "item changed" signal never fires for them. Send such changes on the next
    /// update instead of waiting for the background comparison to reach the item, but only when the value
    /// actually changed (a container that has settled at room temperature is rewritten every second).
    /// </summary>
    internal struct TagSnapshot {
        internal bool valid, enabled, b; internal int i; internal float f; internal long l; internal double d; internal string s;
        internal static TagSnapshot Of(TagState t) { return t == null ? default(TagSnapshot) : new TagSnapshot { valid = true, enabled = t.valueEnabled, b = t.valueBool, i = t.valueInt, f = t.valueFloat, l = t.valueLong, d = t.valueDouble, s = t.valueString }; }
        internal bool Same(TagState t) { return valid && t != null && enabled == t.valueEnabled && b == t.valueBool && i == t.valueInt && f.Equals(t.valueFloat) && l == t.valueLong && d.Equals(t.valueDouble) && s == t.valueString; }
    }

    [HarmonyPatch(typeof(GameItem), "ModifyTag", new[] { typeof(string), typeof(Action<TagState>), typeof(bool) })]
    internal static class HostQuietModify {
        static bool Watch(bool skipValidate) { var s = Connection.Current; return skipValidate && s != null && s.IsHost && !ReplicaSession.ClientStore; }
        static void Prefix(GameItem __instance, string __0, bool __2, out TagSnapshot __state) {
            __state = default(TagSnapshot);
            if (!Watch(__2) || __instance == null || __instance.state == null || string.IsNullOrEmpty(__0)) return;
            try { __state = TagSnapshot.Of(__instance.state.GetTag(__0)); } catch { }
        }
        static void Postfix(GameItem __instance, string __0, bool __2, TagSnapshot __state) {
            if (!__state.valid || !Watch(__2)) return;
            try { var w = Connection.Current.World; if (!__state.Same(__instance.state.GetTag(__0)) && w.Id(__instance) > 0) w.MarkDirty(__instance); } catch { }
        }
    }

    [HarmonyPatch]
    internal static class HostQuietToggle {
        static IEnumerable<MethodBase> TargetMethods() {
            yield return typeof(GameItem).GetMethod("EnableTag", new[] { typeof(string), typeof(bool) });
            yield return typeof(GameItem).GetMethod("DisableTag", new[] { typeof(string), typeof(bool) });
        }
        static void Postfix(GameItem __instance, bool __1, bool __result) {
            var s = Connection.Current;
            if (__result && __1 && s != null && s.IsHost && !ReplicaSession.ClientStore && s.World.Id(__instance) > 0) s.World.MarkDirty(__instance);
        }
    }
}
