using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// Settings a player changes on an item through the game's own controls (machine panels, hopper and outlet
    /// windows, the context-menu pin, a recipe picked in a machine's window…) are stored as tags on that item. On a
    /// friend's machine those changes would only be local and the next sync would put them back, so every tag a
    /// friend's click changes on a shop item is sent to the host, which writes the same value. The host does not need
    /// that item's window open. Tags that decide ownership or money are never taken from a friend.
    /// </summary>
    internal static class MachineSettings {
        static readonly HashSet<string> denied = new HashSet<string> { "IS_OWNED_TAG", "STORAGE_COORDINATOR_PRESETS_STRING", "AUTO_BUTCHER_LAST_COUNT_INT", "AUTO_BUTCHER_LAST_ROE_INT" };
        static readonly string[] deniedParts = { "SHOPTOGETHER_", "OWNED", "STOLEN", "PRICE", "CASH", "CREDIT", "VALUE" };
        /// <summary>Ranges the game's own controls allow, re-checked on the host for the settings we know.</summary>
        static readonly Dictionary<string, int[]> ranges = new Dictionary<string, int[]>();
        static MachineSettings() {
            foreach (var page in new[] { "CAGE", "TANK" }) {
                string p = "AUTO_BUTCHER_" + page + "_";
                ranges[p + "HARVEST_INT"] = new[] { 0, 20 };
                foreach (var s in new[] { "WEIGHT_MIN_INT", "WEIGHT_MAX_INT", "AGE_MIN_INT", "AGE_MAX_INT" }) ranges[p + s] = new[] { 0, 1000000 };
                ranges[p + "KEEP_FEMALES_INT"] = new[] { 0, 100 };
                ranges[p + "KEEP_MALES_INT"] = new[] { 0, 100 };
            }
            ranges["CURRENT_TARGET_TEMP"] = new[] { 26, 160 };
            ranges["FEED_DISPENSER_TARGET_PERCENT_INT"] = new[] { 0, 100 };
            ranges["MARKING_PEN_MODE_INT"] = new[] { 0, 20 };
            ranges["HOPPER_ORDER_INT"] = new[] { 1, 15 };
            ranges["HOPPER_KEEP_INT"] = new[] { 0, 20 };
        }
        internal static bool Allowed(string tag) {
            if (string.IsNullOrEmpty(tag) || tag.Length > 128 || denied.Contains(tag)) return false;
            foreach (var part in deniedParts) if (tag.IndexOf(part, StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        /// <summary>Above zero while the local player is clicking, typing, dragging a slider or ticking a box.</summary>
        internal static int Input;
        /// <summary>Above zero while a machine button runs here first; its tag changes wait for the verdict.</summary>
        internal static int Hold;

        struct Change { public GameItem item; public string tag, packed; }
        static readonly List<Change> pending = new List<Change>(), held = new List<Change>();
        static float nextFlush;

        static string Pack(TagState s) {
            var c = CultureInfo.InvariantCulture;
            return (s.valueEnabled ? "1" : "0") + "|" + s.valueInt.ToString(c) + "|" + s.valueFloat.ToString("R", c) + "|" + s.valueLong.ToString(c) + "|" +
                   s.valueDouble.ToString("R", c) + "|" + (s.valueBool ? "1" : "0") + "|" + (s.valueString ?? "");
        }

        /// <summary>Friend side: a click just changed a tag on an item.</summary>
        internal static void Changed(GameItem item, string tag) {
            if (Input <= 0 || item == null || !Allowed(tag) || !ReplicaSession.IsReplica || WorldReplica.Applying || !LocalControl.Focused) return;
            var session = Connection.Current;
            if (session == null || session.World.Id(item) == 0 || item.state == null) return;
            var c = new Change { item = item, tag = tag, packed = Pack(item.state.GetTag(tag)) };
            if (c.packed.Length > 4000) return;
            var list = Hold > 0 ? held : pending;
            // A slider being dragged changes many times a second; only the latest value is sent.
            for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i].item, item) && list[i].tag == tag) { list[i] = c; return; }
            if (list.Count < 128) list.Add(c);
        }

        /// <summary>Friend side: the machine button that was holding changes back ran on the host instead (drop them),
        /// or only changed settings here (send them).</summary>
        internal static void Settle(bool ranOnHost) { if (Hold > 0) return; if (!ranOnHost) foreach (var c in held) Merge(c); held.Clear(); }
        static void Merge(Change c) { for (int i = 0; i < pending.Count; i++) if (ReferenceEquals(pending[i].item, c.item) && pending[i].tag == c.tag) { pending[i] = c; return; } pending.Add(c); }

        /// <summary>Friend side, every frame: sends what changed, at most ten times a second.</summary>
        internal static void Flush() {
            if (pending.Count == 0 || Time.realtimeSinceStartup < nextFlush) return;
            nextFlush = Time.realtimeSinceStartup + .1f;
            var send = new List<Change>(pending); pending.Clear();
            foreach (var c in send) if (c.item != null) RemoteActions.Forward(RemoteActions.Adapter("SetTag", 3), null, new object[] { c.item, c.tag, c.packed });
        }
        internal static void Reset() { pending.Clear(); held.Clear(); Input = 0; Hold = 0; }

        /// <summary>Host side: one setting a friend's click changed.</summary>
        internal static void SetTag(GameItem item, string tag, string packed) {
            if (item == null || !Allowed(tag) || packed == null || packed.Length > 4000) return;
            var parts = packed.Split(new[] { '|' }, 7);
            if (parts.Length != 7) return;
            var c = CultureInfo.InvariantCulture;
            int i; float f; long l; double d;
            if (!int.TryParse(parts[1], NumberStyles.Integer, c, out i) || !float.TryParse(parts[2], NumberStyles.Float, c, out f) || !long.TryParse(parts[3], NumberStyles.Integer, c, out l) || !double.TryParse(parts[4], NumberStyles.Float, c, out d)) return;
            if (float.IsNaN(f) || float.IsInfinity(f) || double.IsNaN(d) || double.IsInfinity(d)) return;
            int[] range;
            if (ranges.TryGetValue(tag, out range)) i = Math.Max(range[0], Math.Min(range[1], i));
            bool enabled = parts[0] == "1", b = parts[5] == "1"; string s = parts[6];
            if (!enabled) { item.DisableTag(tag); return; }
            item.ModifyTag(tag, t => { t.Enable(); t.valueString = s; t.valueInt = i; t.valueFloat = f; t.valueLong = l; t.valueDouble = d; t.valueBool = b; });
        }
    }

    /// <summary>The places where a machine-panel control reacts to the player's own mouse: a player action, so tag
    /// changes are sent and forwarded game functions run on the host.</summary>
    [HarmonyPatch]
    internal static class MachineryInputScope {
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;
        static IEnumerable<MethodBase> TargetMethods() {
            foreach (var m in new MethodBase[] {
                typeof(MachineryRadioButton).GetMethod("OnSelect", BF),
                typeof(MachinerySlider).GetMethod("SetFromPointer", BF),
                typeof(MachineryRangeSlider).GetMethod("SetFromPointer", BF) })
                if (m != null) yield return m;
        }
        static void Prefix(ref bool __state) { __state = LocalControl.Focused; MachineSettings.Input++; if (__state) LocalControl.depth++; }
        static Exception Finalizer(Exception __exception, bool __state) { if (MachineSettings.Input > 0) MachineSettings.Input--; if (__state) LocalControl.depth--; return __exception; }
    }

    /// <summary>
    /// A button on a machine's own panel (blender start, bottle printer, wine press, chem finisher output, butcher,
    /// cassette player… and machines added by other mods). On a friend's machine it runs here first: if it only
    /// changed settings or already asked the host for something, that is all. If it tried to change the shop itself
    /// (make or remove items, move money), the press is repeated on the host's copy of the same machine instead.
    /// </summary>
    [HarmonyPatch(typeof(MachineryButton), "OnSelect")]
    internal static class MachineButtonPress {
        internal sealed class Press { public GameItem item; public int index; public string label; public int blocked, sent; public bool user; }
        static readonly FieldInfo callbackField = typeof(MachineryButton).GetField("callback", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static List<MachineryButton> Buttons(PixelWindow window) {
            var list = new List<MachineryButton>();
            if (window == null || window.handler == null) return list;
            foreach (var node in window.handler.GetComponentsInChildren<TreeNodeRender>(true)) { var b = node.element as MachineryButton; if (b != null && !list.Contains(b)) list.Add(b); }
            return list;
        }

        static GameItem Owner(MachineryButton button, Connection session) {
            if (button.handler == null) return null;
            var t = button.handler.transform;
            foreach (var item in session.World.Items.Values) {
                var w = item != null ? item.contentWindow : null;
                if (w != null && w.handler != null && w.IsVisible() && t.IsChildOf(w.handler.transform)) return item;
            }
            return null;
        }

        static void Prefix(MachineryButton __instance, ref Press __state) {
            __state = null;
            var session = Connection.Current;
            bool user = LocalControl.Focused;
            if (user) { LocalControl.depth++; MachineSettings.Input++; }
            if (session == null || !ReplicaSession.IsReplica || WorldReplica.Applying) { __state = new Press { user = user }; return; }
            var item = Owner(__instance, session);
            int index = item != null ? Buttons(item.contentWindow).IndexOf(__instance) : -1;
            __state = new Press { user = user, item = index >= 0 ? item : null, index = index, label = __instance.text ?? "", blocked = LocalControl.BlockedWrites, sent = Connection.Outgoing };
            if (__state.item != null) MachineSettings.Hold++;
        }

        static Exception Finalizer(Exception __exception, Press __state) {
            if (__state == null) return __exception;
            if (__state.user) { LocalControl.depth--; if (MachineSettings.Input > 0) MachineSettings.Input--; }
            if (__state.item != null) {
                MachineSettings.Hold--;
                bool host = __state.user && LocalControl.BlockedWrites > __state.blocked && Connection.Outgoing == __state.sent;
                MachineSettings.Settle(host);
                if (host) RemoteActions.Forward(RemoteActions.Adapter("PressButton", 3), null, new object[] { __state.item, __state.index, __state.label });
            }
            return __exception;
        }

        /// <summary>Host side: the same button on the host's copy of the machine.</summary>
        internal static void PressButton(GameItem item, int index, string label) {
            if (item == null || item.contentWindow == null || index < 0 || label == null || callbackField == null) return;
            var buttons = Buttons(item.contentWindow);
            // Labels are localized on each computer; the bounded native button index identifies the action.
            if (index >= buttons.Count || label.Length > 200) return;
            var callback = callbackField.GetValue(buttons[index]) as Action;
            if (callback != null) callback();
        }
    }

    [HarmonyPatch]
    internal static class ItemTagChanged {
        static IEnumerable<MethodBase> TargetMethods() {
            yield return typeof(GameItem).GetMethod("ModifyTag", new[] { typeof(string), typeof(Action<TagState>), typeof(bool) });
            yield return typeof(GameItem).GetMethod("EnableTag", new[] { typeof(string), typeof(bool) });
            yield return typeof(GameItem).GetMethod("DisableTag", new[] { typeof(string), typeof(bool) });
        }
        static void Postfix(GameItem __instance, string __0) { if (MachineSettings.Input > 0) MachineSettings.Changed(__instance, __0); }
    }
}
