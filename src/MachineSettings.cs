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
    [Serializable] internal sealed class SettingValue { public string tag,packed; }
    internal static class MachineSettings {
        static readonly HashSet<string> settings = new HashSet<string> {
            "PINNED_TAG", "SELECTED_CHEM_ID_TAG", "HOPPER_LINK_INT", "HOPPER_FILTER_STRING",
            "HOPPER_ON_ADD_BOOL", "HOPPER_ON_MOVE_BOOL"
        };
        /// <summary>Ranges the game's own controls allow, re-checked on the host for the settings we know.</summary>
        static readonly Dictionary<string, int[]> ranges = new Dictionary<string, int[]>();
        static MachineSettings() {
            foreach (var page in new[] { "CAGE", "TANK" }) {
                string p = "AUTO_BUTCHER_" + page + "_";
                ranges[p + "HARVEST_INT"] = new[] { 0, 3 };
                foreach (var s in new[] { "WEIGHT_MIN_INT", "WEIGHT_MAX_INT" }) ranges[p + s] = new[] { 100, page == "CAGE" ? 1000 : 5000 };
                foreach (var s in new[] { "AGE_MIN_INT", "AGE_MAX_INT" }) ranges[p + s] = new[] { 1, page == "CAGE" ? 30 : 100 };
                ranges[p + "KEEP_FEMALES_INT"] = new[] { 0, 10 };
                ranges[p + "KEEP_MALES_INT"] = new[] { 0, 10 };
            }
            ranges["CURRENT_TARGET_TEMP"] = new[] { 26, 160 };
            ranges["FEED_DISPENSER_TARGET_PERCENT_INT"] = new[] { 0, 100 };
            ranges["MARKING_PEN_MODE_INT"] = new[] { 0, AnimalMarkHelper.ERASER_MODE };
            ranges["HEATING_PLATE_STATE"] = new[] { 0, 2 };
            ranges["HOPPER_ORDER_INT"] = new[] { 1, 15 };
            ranges["HOPPER_KEEP_INT"] = new[] { 0, 20 };
            ranges["HOPPER_FILTER_MODE_INT"] = new[] { 0, 2 };
            ranges["HOPPER_COLOR_INT"] = new[] { 0, HopperHelper.COLOR_COUNT };
            foreach (var field in typeof(MachineAutoButcher).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.IsLiteral && field.FieldType == typeof(string)) { var key = (string)field.GetRawConstantValue(); if (key.StartsWith("AUTO_BUTCHER_",StringComparison.Ordinal) && key.EndsWith("_BOOL",StringComparison.Ordinal)) settings.Add(key); }
        }
        internal static bool Allowed(string tag) {
            return tag != null && (settings.Contains(tag) || ranges.ContainsKey(tag));
        }

        /// <summary>Above zero while the local player is clicking, typing, dragging a slider or ticking a box.</summary>
        internal static int Input;
        /// <summary>Above zero while a machine button runs here first; its tag changes wait for the verdict.</summary>
        internal static int Hold;
        /// <summary>Items a friend's machine button made on this screen while it was pressed (the host makes the real ones).</summary>
        internal static readonly List<GameItem> Created = new List<GameItem>();

        struct Change { public GameItem item; public string tag, packed; public TagState proposed; }
        static readonly List<Change> pending = new List<Change>(), held = new List<Change>();
        static float nextFlush;
        internal static TagState Proposal(GameItem item, string tag) {
            foreach (var c in held) if (c.item == item && c.tag == tag) return c.proposed.Clone();
            foreach (var c in pending) if (c.item == item && c.tag == tag) return c.proposed.Clone();
            return item.state.GetTag(tag).Clone();
        }

        static string Pack(TagState s) {
            var c = CultureInfo.InvariantCulture;
            return (s.valueEnabled ? "1" : "0") + "|" + s.valueInt.ToString(c) + "|" + s.valueFloat.ToString("R", c) + "|" + s.valueLong.ToString(c) + "|" +
                   s.valueDouble.ToString("R", c) + "|" + (s.valueBool ? "1" : "0") + "|" + (s.valueString ?? "");
        }

        /// <summary>Friend side: a click just changed a tag on an item.</summary>
        internal static void Changed(GameItem item, string tag, TagState proposed) {
            if (Input <= 0 || item == null || !ReplicaSession.IsReplica || WorldReplica.Applying || !LocalControl.Focused) return;
            var session = Connection.Current;
            if (session == null || session.World.Id(item) == 0 || item.state == null) return;
            var c = new Change { item = item, tag = tag, packed = Pack(proposed), proposed = proposed.Clone() };
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
            var batches=new Dictionary<GameItem,List<SettingValue>>();
            foreach(var c in send)if(c.item!=null){List<SettingValue> batch;if(!batches.TryGetValue(c.item,out batch)){batch=new List<SettingValue>();batches.Add(c.item,batch);}batch.Add(new SettingValue{tag=c.tag,packed=c.packed});}
            foreach(var batch in batches)RemoteActions.Forward(RemoteActions.Adapter("SetSettings",2),null,new object[]{batch.Key,WireJson.ToJson(batch.Value)});
        }
        internal static void Reset() { pending.Clear(); held.Clear(); Input = 0; Hold = 0; }

        /// <summary>Host side: one setting a friend's click changed.</summary>
        internal static void SetTag(GameItem item,string tag,string packed){SetSettings(item,WireJson.ToJson(new[]{new SettingValue{tag=tag,packed=packed}}));}
        internal static void SetSettings(GameItem item,string json){
            var values=WireJson.FromJson<SettingValue[]>(json);
            if(item==null||values==null||values.Length==0||values.Length>64){RemoteActions.PendingNote="~SMC305~";return;}
            var changes=new Dictionary<string,TagState>();
            foreach(var value in values){if(value==null||value.tag==null||changes.ContainsKey(value.tag)){RemoteActions.PendingNote="~SMC305~";return;}TagState next;Validate(item,value.tag,value.packed,out next);if(next==null)return;changes.Add(value.tag,next);}
            foreach(string page in new[]{"CAGE","TANK"})foreach(string metric in new[]{"WEIGHT","AGE"}){
                string prefix="AUTO_BUTCHER_"+page+"_"+metric+"_";string min=prefix+"MIN_INT",max=prefix+"MAX_INT";
                if(!changes.ContainsKey(min)&&!changes.ContainsKey(max))continue;
                TagState lower,upper;int lo=changes.TryGetValue(min,out lower)?lower.GetInt():item.GetTagReadonly(min).GetInt();int hi=changes.TryGetValue(max,out upper)?upper.GetInt():item.GetTagReadonly(max).GetInt();
                if(lo>hi){RemoteActions.PendingNote="~SMC344~";return;}
            }
            // Validate the complete proposal before the first write. Native validation observes the
            // completed pair rather than a half-applied clipboard or range control.
            foreach(var change in changes){var next=change.Value;item.ModifyTag(change.Key,t=>{t.Set(next);t.SetEnabled(next.valueEnabled);},true);}item.Validate();
        }
        static void Validate(GameItem item, string tag, string packed,out TagState result) {
            result=null;
            // Whatever is not taken is told to the player who changed it, and so is a number the host had to adjust.
            if (item == null || !Allowed(tag) || packed == null || packed.Length > 4000) { RemoteActions.PendingNote = "~SMC305~"; return; }
            var parts = packed.Split(new[] { '|' }, 7);
            if (parts.Length != 7) { RemoteActions.PendingNote = "~SMC305~"; return; }
            var c = CultureInfo.InvariantCulture;
            int i; float f; long l; double d;
            if (!int.TryParse(parts[1], NumberStyles.Integer, c, out i) || !float.TryParse(parts[2], NumberStyles.Float, c, out f) || !long.TryParse(parts[3], NumberStyles.Integer, c, out l) || !double.TryParse(parts[4], NumberStyles.Float, c, out d)) { RemoteActions.PendingNote = "~SMC305~"; return; }
            if (float.IsNaN(f) || float.IsInfinity(f) || double.IsNaN(d) || double.IsInfinity(d)) { RemoteActions.PendingNote = "~SMC305~"; return; }
            int[] range;
            if (ranges.TryGetValue(tag, out range)) { int asked = i; i = Math.Max(range[0], Math.Min(range[1], i)); if (i != asked) RemoteActions.PendingNote = "~SMC306~" + i.ToString(c); }
            bool enabled = parts[0] == "1", b = parts[5] == "1"; string s = parts[6];
            if (parts[0] != "0" && parts[0] != "1" || parts[5] != "0" && parts[5] != "1") { RemoteActions.PendingNote = "~SMC305~"; return; }
            if (!GeneralHelper.IsItemOwned(item)) { RemoteActions.PendingNote = "~SMC305~"; return; }
            if (tag.StartsWith("AUTO_BUTCHER_",StringComparison.Ordinal) && item.identifier!="auto_butcher" ||
                (tag=="CURRENT_TARGET_TEMP" || tag=="HEATING_PLATE_STATE") && item.identifier!="heating_plate" ||
                tag=="FEED_DISPENSER_TARGET_PERCENT_INT" && !item.IsTag("FEED_DISPENSER_TAG") ||
                tag=="MARKING_PEN_MODE_INT" && item.identifier!="marking_pen" ||
                tag=="SELECTED_CHEM_ID_TAG" && item.identifier!="chem_finisher" ||
                tag=="PINNED_TAG" && !ItemPinHelper.MayPin(item)) { RemoteActions.PendingNote="~SMC305~";return; }
            if (tag.StartsWith("HOPPER_",StringComparison.Ordinal) && !HopperHelper.IsIntake(item) && !(tag == "HOPPER_COLOR_INT" && HopperHelper.IsOutlet(item))) { RemoteActions.PendingNote = "~SMC305~"; return; }
            if (tag == "HOPPER_LINK_INT" && enabled && i != 0) {
                var session = Connection.Current; GameItem outlet = null;
                if (session != null) foreach (var candidate in session.World.Items.Values) if (candidate != null && candidate.GetUniqueID() == i) { outlet = candidate; break; }
                if (outlet == null || !HopperHelper.CanLink(item,outlet) || !AuthorityIntent.Free(session,Math.Max(0,RemoteActions.CurrentOwner),session.World.Id(outlet))) { RemoteActions.PendingNote = "~SMC305~"; return; }
            }
            if (tag == "HOPPER_FILTER_STRING") {
                var ids = new HashSet<string>(DirectoryMaster.GetIdentifierList<GameItem>());
                foreach (string id in s.Split(';')) if (id.Length > 0 && !ids.Contains(id)) { RemoteActions.PendingNote = "~SMC305~"; return; }
            }
            if (tag == "SELECTED_CHEM_ID_TAG" && enabled) {
                bool found = false;
                foreach (var recipe in ChemFinisherRecipe.chemFinisherRecipes) if (recipe.identifier == s) { found = true; break; }
                if (!found || item.identifier != "chem_finisher") { RemoteActions.PendingNote = "~SMC327~"; return; }
            }
            result=item.GetTagReadonly(tag).Clone();result.SetEnabled(enabled);
            if(!enabled)return;
            if (tag == "SELECTED_CHEM_ID_TAG" || tag.EndsWith("_STRING",StringComparison.Ordinal)) result.SetString(s);
            else if (tag.EndsWith("_BOOL",StringComparison.Ordinal)) result.SetBool(b);
            else if (tag != "PINNED_TAG") result.SetInt(i);
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
    /// cassette player… and machines added by other mods). World-machine callbacks run on the host. Local
    /// clipboard and volume controls remain personal; setting proposals are separately validated.
    /// No guest callback is used to simulate a world-machine press.
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

        static string ButcherLabel(string key) { try { return LocHelper.GetLocalizedMechanic(key); } catch { return null; } }
        static bool LocalVolume(GameItem item, MachineryButton button) {
            return item != null && item.identifier == "cassette_player" && button.icon != null && (button.icon.name == "up" || button.icon.name == "down");
        }
        static readonly FieldInfo clipInts = typeof(MachineAutoButcher).GetField("clipboardInts", BindingFlags.NonPublic | BindingFlags.Static);
        static readonly FieldInfo clipBools = typeof(MachineAutoButcher).GetField("clipboardBools", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>Friend: the butcher settings this player copied go to the host as setting changes.</summary>
        static void PasteToHost(GameItem machine) {
            var ints = clipInts != null ? clipInts.GetValue(null) as Dictionary<string, int> : null;
            var bools = clipBools != null ? clipBools.GetValue(null) as Dictionary<string, bool> : null;
            if (ints == null && bools == null) { RemoteActions.Notice("~SMC125~"); return; }
            var c = CultureInfo.InvariantCulture;
            var values=new List<SettingValue>();
            if(ints!=null)foreach(var kv in ints)values.Add(new SettingValue{tag=kv.Key,packed="1|"+kv.Value.ToString(c)+"|0|0|0|0|"});
            if(bools!=null)foreach(var kv in bools)values.Add(new SettingValue{tag=kv.Key,packed="1|0|0|0|0|"+(kv.Value?"1":"0")+"|"});
            RemoteActions.Forward(RemoteActions.Adapter("SetSettings",2),null,new object[]{machine,WireJson.ToJson(values)});
        }

        internal static GameItem Owner(MachineryButton button, Connection session) {
            if (button.handler == null) return null;
            var t = button.handler.transform;
            foreach (var item in session.World.Items.Values) {
                var w = item != null ? item.contentWindow : null;
                if (w != null && w.handler != null && w.IsVisible() && t.IsChildOf(w.handler.transform)) return item;
            }
            return null;
        }

        static bool Prefix(MachineryButton __instance, ref Press __state) {
            __state = null;
            var session = Connection.Current;
            bool user = LocalControl.Focused;
            if (user) { LocalControl.depth++; MachineSettings.Input++; }
            if (session == null || !session.Ready || !(session.IsHost || session.IsClient) || WorldReplica.Applying || NativeActions.Executing || RemoteActions.CurrentOwner > 0) { __state = new Press { user = user }; return !ReplicaSession.IsReplica; }
            var item = Owner(__instance, session);
            int index = item != null ? Buttons(item.contentWindow).IndexOf(__instance) : -1;
            // A shop machine's button runs on the host's copy of the machine; this screen does not try it first.
            // The butcher's "copy settings" only fills this player's own clipboard, and "paste" sends what this
            // player copied.
            if (item != null && index >= 0) {
                string label = __instance.text ?? "";
                // Buttons that only open a window or fill this player's clipboard stay on this screen.
                if (LocalVolume(item, __instance) || label == ButcherLabel("mech_auto_butcher_btn_copy") || label == ButcherLabel("mech_chem_finisher_btn_configure")) { __state = new Press { user = user }; return true; }
                __state = new Press { user = user };
                if (!user) return false;
                if (session.IsClient && label == ButcherLabel("mech_auto_butcher_btn_paste")) { PasteToHost(item); return false; }
                if (session.IsHost) {
                    if (!session.Leases.Available(0, session.World.Id(item), Time.realtimeSinceStartup)) RemoteActions.Notice("~SMC096~");
                    else { RemoteActions.PendingNote = null; NativeActions.Executing = true; try { PressButton(item, index, label); } finally { NativeActions.Executing = false; } string note = RemoteActions.TakeNote(); if (note != null) RemoteActions.Notice(note); }
                } else RemoteActions.Forward(RemoteActions.Adapter("PressButton", 3), null, new object[] { item, index, label });
                return false;
            }
            __state = new Press { user = user, item = index >= 0 ? item : null, index = index, label = __instance.text ?? "", blocked = LocalControl.BlockedWrites, sent = Connection.Outgoing };
            if (__state.item != null) { if (MachineSettings.Hold == 0) MachineSettings.Created.Clear(); MachineSettings.Hold++; }
            return true;
        }

        static Exception Finalizer(Exception __exception, Press __state, MachineryButton __instance) {
            // Native OnSelect calls Deselect after its callback. Forwarding skips that
            // method body, but Select has already set isSelected: without this cleanup
            // the next mouse release only deselects the button and sends no action.
            __instance.Deselect();
            if (__state == null) return __exception;
            if (__state.user) { LocalControl.depth--; if (MachineSettings.Input > 0) MachineSettings.Input--; }
            if (__state.item != null) {
                MachineSettings.Hold--;
                // The press reached a write this screen may not do, or it made an item here (a printer making a bottle
                // whose placement this screen's copy turned down): the host does it with its own data.
                bool made = MachineSettings.Hold == 0 && MachineSettings.Created.Count > 0;
                bool host = __state.user && (LocalControl.BlockedWrites > __state.blocked || made) && Connection.Outgoing == __state.sent;
                if (MachineSettings.Hold == 0) {
                    foreach (var orphan in MachineSettings.Created) if (orphan != null && orphan.parentInventory == null) { try { orphan.Destroy(); } catch { } }
                    MachineSettings.Created.Clear();
                }
                MachineSettings.Settle(host);
                if (host) RemoteActions.Forward(RemoteActions.Adapter("PressButton", 3), null, new object[] { __state.item, __state.index, __state.label });
            }
            return __exception;
        }

        /// <summary>Host side: the same button on the host's copy of the machine.</summary>
        internal static void PressButton(GameItem item, int index, string label) {
            if (item == null || item.contentWindow == null || index < 0 || label == null || callbackField == null) { RemoteActions.PendingNote = "~SMC247~"; return; }
            var buttons = Buttons(item.contentWindow);
            // Labels are localized on each computer; the bounded native button index identifies the action.
            if (index >= buttons.Count || label.Length > 200) { RemoteActions.PendingNote = "~SMC247~"; return; }
            if (LocalVolume(item, buttons[index])) { RemoteActions.PendingNote = "~SMC247~"; return; }
            // A button the host's copy shows as unavailable is not pressed for anyone.
            if (buttons[index].isDisabled) { RemoteActions.PendingNote = "~SMC125~"; return; }
            var callback = callbackField.GetValue(buttons[index]) as Action;
            if (callback == null) { RemoteActions.PendingNote = "~SMC247~"; return; }
            var session = Connection.Current;
            if (!DialogueIntent.MayChange(session, item)) return;
            string before = EffectStamp(session, item);
            PanelRelay.NotifiedDuringAction = false;
            var previousSource = PanelRelay.SourceItem; PanelRelay.SourceItem = item;
            try { callback(); } finally { PanelRelay.SourceItem = previousSource; }
            if (RemoteActions.PendingNote == null && !PanelRelay.NotifiedDuringAction && before == EffectStamp(session, item))
                RemoteActions.PendingNote = "~SMC321~";
        }

        // A callback may deliberately do nothing. Compare the machine's subtree and store
        // state, rather than declaring success merely because the delegate returned.
        static string EffectStamp(Connection session, GameItem item) {
            if (session == null) return "";
            var b = new System.Text.StringBuilder();
            var items = new List<GameItem> { item }; items.AddRange(GraphUtils.FindChildrenType<GameItem>(item, null, null));
            foreach (var child in items) if (child != null) b.Append(Fingerprint.Item(session.World, session.World.Id(child), child)).Append(';');
            b.Append(session.World.StoreHash(PlayerStore.Instance.saveSlotId)).Append(';').Append(session.World.StationHash());
            return b.ToString();
        }
    }

    [HarmonyPatch]
    internal static class ItemTagChanged {
        static IEnumerable<MethodBase> TargetMethods() {
            yield return typeof(GameItem).GetMethod("EnableTag", new[] { typeof(string), typeof(bool) });
            yield return typeof(GameItem).GetMethod("DisableTag", new[] { typeof(string), typeof(bool) });
        }
        // Propose a copy of the tag. The replica's actual tag is only written by world sync.
        static bool Prefix(GameItem __instance, string __0, object[] __args, MethodBase __originalMethod) { return Propose(__instance, __0, __args, __originalMethod.Name); }
        internal static bool Propose(GameItem __instance, string __0, object[] __args, string method) {
            var session = Connection.Current;
            if (session != null && session.IsHost && session.Ready && LocalControl.UserAction && !NativeActions.Executing && RemoteActions.CurrentOwner <= 0 &&
                !session.Leases.Available(0, session.World.Id(__instance), Time.realtimeSinceStartup)) { RemoteActions.Notice("~SMC096~"); return false; }
            if (!ReplicaSession.IsReplica || MachineDisplay.Running || WorldReplica.Sandbox > 0 || session == null || session.World.Id(__instance) <= 0) return true;
            if (MachineSettings.Input > 0 && LocalControl.Focused) {
                var proposed = MachineSettings.Proposal(__instance, __0);
                if (method == "ModifyTag") { var change = __args[1] as Action<TagState>; if (change != null) change(proposed); }
                else proposed.SetEnabled(method == "EnableTag");
                MachineSettings.Changed(__instance, __0, proposed);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(GameItem), "ModifyTag", new[] { typeof(string), typeof(Action<TagState>), typeof(bool) })]
    internal static class ProposeItemTag {
        static bool Prefix(GameItem __instance, string __0, object[] __args, ref GameItem __result) {
            if (ItemTagChanged.Propose(__instance, __0, __args, "ModifyTag")) return true;
            __result = __instance; return false;
        }
    }

    /// <summary>Friend: items made while a machine button is pressed are noted (see MachineButtonPress).</summary>
    [HarmonyPatch(typeof(DirectoryMaster), "Item", new[] { typeof(string), typeof(bool) })]
    internal static class MachineButtonMadeItem {
        static void Postfix(GameItem __result) {
            if (__result != null && MachineSettings.Hold > 0 && ReplicaSession.IsReplica && !WorldReplica.Applying && MachineSettings.Created.Count < 64) MachineSettings.Created.Add(__result);
        }
    }
}
