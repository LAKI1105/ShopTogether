using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    [Serializable] internal sealed class WidgetReadout {
        public string kind, text;
        public float value, high, min, max, step;
        public int selected;
        public bool disabled, hidden;
    }
    [Serializable] internal sealed class MachineReadout {
        public int item;
        public WidgetReadout[] widgets;
    }

    // Capture the host's already-computed presentation. Never invoke a simulation callback
    // to refresh a guest's panel: even a blocked write can consume RNG or trigger effects.
    internal sealed class HostReadouts {
        readonly Dictionary<int, MachineReadout> records = new Dictionary<int, MachineReadout>();
        readonly Dictionary<int, string> signatures = new Dictionary<int, string>();
        float next;

        static List<PixelElement> Widgets(GameItem item) {
            var result = new List<PixelElement>();
            if (item == null || item.contentWindow == null || item.contentWindow.handler == null) return result;
            foreach (var node in item.contentWindow.handler.GetComponentsInChildren<TreeNodeRender>(true)) {
                var e = node.element;
                if ((e is MachineryText || e is MachineryButton || e is MachineryProgressBar ||
                    e is MachineryRadioList || e is MachineryRadioButton || e is MachinerySlider || e is MachineryRangeSlider) && !result.Contains(e)) result.Add(e);
            }
            return result;
        }

        static WidgetReadout Read(PixelElement element) {
            var r = new WidgetReadout { kind = element.GetType().FullName };
            var selectable = element as SelectableElement;
            if (selectable != null) r.hidden = selectable.isHidden;
            var text = element as MachineryText;
            var button = element as MachineryButton;
            var progress = element as MachineryProgressBar;
            var radio = element as MachineryRadioList;
            var toggle = element as MachineryRadioButton;
            var slider = element as MachinerySlider;
            var range = element as MachineryRangeSlider;
            if (text != null) r.text = text.GetText();
            else if (button != null) { r.text = button.text; r.disabled = button.isDisabled; }
            else if (progress != null) r.value = progress.value;
            else if (radio != null) r.selected = radio.selected;
            else if (toggle != null) { r.selected = toggle.isOn ? 1 : 0; r.disabled = toggle.isDisabled; }
            else if (slider != null) { r.value = slider.value; r.min = slider.min; r.max = slider.max; r.step = slider.step; r.disabled = slider.isDisabled; }
            else if (range != null) { r.value = range.low; r.high = range.high; r.min = range.min; r.max = range.max; r.step = range.step; r.disabled = range.isDisabled; }
            return r;
        }

        internal MachineReadout[] Capture(WorldReplica world, bool full) {
            if (!full && Time.realtimeSinceStartup < next) return null;
            next = Time.realtimeSinceStartup + .5f;
            var changed = new List<MachineReadout>();
            foreach (var pair in world.Items) {
                var widgets = Widgets(pair.Value);
                if (widgets.Count == 0 && !records.ContainsKey(pair.Key)) continue;
                var r = new MachineReadout { item = pair.Key, widgets = widgets.ConvertAll(Read).ToArray() };
                string json = WireJson.ToJson(r), previous;
                if (full || !signatures.TryGetValue(pair.Key, out previous) || previous != json) changed.Add(r);
                records[pair.Key] = r; signatures[pair.Key] = json;
            }
            foreach (int id in new List<int>(records.Keys)) if (!world.Items.ContainsKey(id)) { records.Remove(id); signatures.Remove(id); }
            return changed.ToArray();
        }

        internal void Receive(MachineReadout[] incoming, bool full, int[] removed) {
            if (full) records.Clear();
            if (removed != null) foreach (int id in removed) records.Remove(id);
            if (incoming != null) foreach (var r in incoming) if (r != null && r.item > 0 && r.widgets != null) records[r.item] = r;
        }

        internal void ApplyOpen(WorldReplica world) {
            foreach (var pair in records) {
                GameItem item;
                if (!world.Items.TryGetValue(pair.Key, out item) || item == null || item.contentWindow == null || !item.contentWindow.IsVisible()) continue;
                var widgets = Widgets(item); var values = pair.Value.widgets;
                // A layout rebuilt from a different upgrade must wait for its matching world update.
                if (widgets.Count != values.Length) continue;
                bool same = true;
                for (int i = 0; i < widgets.Count; i++) if (values[i] == null || widgets[i].GetType().FullName != values[i].kind) { same = false; break; }
                if (!same) continue;
                for (int i = 0; i < widgets.Count; i++) {
                    var e = widgets[i]; var r = values[i];
                    var selectable = e as SelectableElement;
                    if (selectable != null) selectable.SetHidden(r.hidden);
                    var text = e as MachineryText; var button = e as MachineryButton;
                    var progress = e as MachineryProgressBar; var radio = e as MachineryRadioList; var toggle = e as MachineryRadioButton;
                    var slider = e as MachinerySlider; var range = e as MachineryRangeSlider;
                    if (text != null) { if (text.GetText() != r.text) text.SetText(r.text); }
                    else if (button != null) { if (button.text != r.text) button.SetText(r.text); button.SetDisabled(r.disabled); }
                    else if (progress != null) progress.SetValue(r.value);
                    else if (radio != null) radio.Select(r.selected, false);
                    else if (toggle != null) { toggle.SetOn(r.selected != 0, false); toggle.SetDisabled(r.disabled); }
                    else if (slider != null) { slider.SetRange(r.min, r.max, r.step); slider.SetValue(r.value, false); slider.SetDisabled(r.disabled); }
                    else if (range != null) { range.SetRange(r.min, r.max, r.step); range.SetValues(r.value, r.high, false); range.SetDisabled(r.disabled); }
                }
            }
        }
    }
    // This native callback otherwise substitutes "N/A" whenever the host has closed
    // the plate's window. Keep its ordinary once-per-second execution and only widen
    // this presentation condition; the heating reaction still runs exactly once.
    [HarmonyPatch]
    internal static class HeatingReadoutVisibility {
        static IEnumerable<MethodBase> TargetMethods() {
            foreach (var type in typeof(MachineHeatingPlate).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (method.Name.Contains("b__")) yield return method;
        }
        static bool Visible(bool visible) { var s = Connection.Current; return visible || s != null && s.IsHost && s.Ready; }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) {
            foreach (var instruction in code) {
                yield return instruction;
                if (Equals(instruction.operand, AccessTools.Method(typeof(PixelWindow), "IsVisible")))
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(HeatingReadoutVisibility), "Visible"));
            }
        }
    }
}
