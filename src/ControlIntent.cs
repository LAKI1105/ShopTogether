using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ShopTogether {
    // Controls send their selected value, not the state writes produced by a local callback.
    internal static class ControlIntent {
        internal static List<PixelElement> Controls(GameItem item) {
            var result = new List<PixelElement>();
            if (item == null || item.contentWindow == null || item.contentWindow.handler == null) return result;
            foreach (var node in item.contentWindow.handler.GetComponentsInChildren<TreeNodeRender>(true))
                if ((node.element is MachineryRadioButton || node.element is MachinerySlider || node.element is MachineryRangeSlider) && !result.Contains(node.element)) result.Add(node.element);
            return result;
        }
        internal static bool Send(PixelElement control, float low, float high) {
            var session = Connection.Current;
            if (WorldReplica.Applying || MachineDisplay.Running) return false;
            if (session != null && session.IsHost && session.Ready && LocalControl.UserAction && !NativeActions.Executing && RemoteActions.CurrentOwner < 0) {
                foreach (var item in session.World.Items.Values) {
                    if (item == null || !Controls(item).Contains(control)) continue;
                    if (AuthorityIntent.Free(session, 0, session.World.Id(item))) return false;
                    RemoteActions.Notice(RemoteActions.TakeNote()); return true;
                }
            }
            if (!ReplicaSession.IsReplica) return false;
            if (session == null || !session.Ready) { RemoteActions.Notice("~SMC242~"); return true; }
            if (!LocalControl.Focused) return true;
            foreach (var item in session.World.Items.Values) {
                if (item == null || item.contentWindow == null || !item.contentWindow.IsVisible()) continue;
                int index = Controls(item).IndexOf(control);
                if (index < 0) continue;
                RemoteActions.Forward(RemoteActions.Adapter("SetControl", 5), null, new object[] { item, index, control.GetType().FullName, low, high });
                return true;
            }
            return false; // A non-world panel has its own action adapter.
        }
        internal static void SetControl(GameItem item, int index, string kind, float low, float high) {
            var controls = Controls(item);
            if (index < 0 || index >= controls.Count || controls[index].GetType().FullName != kind ||
                float.IsNaN(low) || float.IsInfinity(low) || float.IsNaN(high) || float.IsInfinity(high)) { AuthorityIntent.Refuse("~SMC305~"); return; }
            var radio = controls[index] as MachineryRadioButton;
            var slider = controls[index] as MachinerySlider;
            var range = controls[index] as MachineryRangeSlider;
            if (radio != null) {
                if (radio.isDisabled || low != 0f && low != 1f) { AuthorityIntent.Refuse("~SMC305~"); return; }
                radio.SetOn(low == 1f, true);
            } else if (slider != null) {
                if (slider.isDisabled || low < slider.min || low > slider.max) { AuthorityIntent.Refuse("~SMC305~"); return; }
                slider.SetValue(low, true);
            } else if (range != null) {
                if (range.isDisabled || low < range.min || high > range.max || low > high) { AuthorityIntent.Refuse("~SMC305~"); return; }
                range.SetValues(low, high, true);
            }
        }
    }
    [HarmonyPatch]
    internal static class SliderInputIntent {
        static IEnumerable<MethodBase> TargetMethods() {
            foreach (var type in new[] { typeof(MachinerySlider), typeof(MachineryRangeSlider) }) {
                var types = new List<Type> { type }; types.AddRange(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
                foreach (var owner in types) foreach (var method in owner.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (method.Name.Contains("b__")) yield return method;
            }
        }
        static bool Disabled(bool disabled) { return !AuthorityIntent.Friend && disabled; }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) {
            foreach (var instruction in code) {
                yield return instruction;
                var method = instruction.operand as MethodInfo;
                if (method != null && method.Name == "get_isDisabled" && (method.DeclaringType == typeof(MachinerySlider) || method.DeclaringType == typeof(MachineryRangeSlider)))
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SliderInputIntent), "Disabled"));
            }
        }
    }
    [HarmonyPatch(typeof(MachineryRadioButton), "OnSelect")]
    internal static class RadioClickIntent {
        static bool Prefix(MachineryRadioButton __instance) {
            if (!ControlIntent.Send(__instance, __instance.group != null || !__instance.isOn ? 1f : 0f, 0f)) return true;
            __instance.Deselect(); return false;
        }
    }
    [HarmonyPatch(typeof(MachineryRadioButton), "SetOn")]
    internal static class RadioIntent {
        static bool Prefix(MachineryRadioButton __instance, bool on, bool notify, ref MachineryRadioButton __result) {
            if (!notify || !ControlIntent.Send(__instance, on ? 1f : 0f, 0f)) return true;
            __result = __instance; return false;
        }
    }
    [HarmonyPatch(typeof(MachinerySlider), "SetValue")]
    internal static class SliderIntent {
        static bool Prefix(MachinerySlider __instance, float newValue, bool notify, ref MachinerySlider __result) {
            if (!notify || !ControlIntent.Send(__instance, newValue, 0f)) return true;
            __result = __instance; return false;
        }
    }
    [HarmonyPatch(typeof(MachineryRangeSlider), "SetValues")]
    internal static class RangeIntent {
        static bool Prefix(MachineryRangeSlider __instance, float newLow, float newHigh, bool notify, ref MachineryRangeSlider __result) {
            if (!notify || !ControlIntent.Send(__instance, newLow, newHigh)) return true;
            __result = __instance; return false;
        }
    }
    [HarmonyPatch(typeof(StoreRequestElement), "Init")]
    internal static class RequestIntentButton {
        static void Postfix(StoreRequestElement __instance) {
            if (ReplicaSession.ClientStore && __instance.acceptButton != null) __instance.acceptButton.interactable = true;
        }
    }
}
