using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    // StoreUI reorders itself inside OnEnable/OnDisable. When CanvasPaperUI is
    // toggled, Unity is still traversing that parent's children and rejects it.
    // Preserve the lifecycle work; perform only the reorder after the traversal.
    internal static class PaperPanelOrder {
        sealed class Pending { internal Transform panel, parent; internal bool last; }
        static readonly List<Pending> pending = new List<Pending>();
        static void Last(Transform panel) { Arrange(panel, true); }
        static void First(Transform panel) { Arrange(panel, false); }
        static void Arrange(Transform panel, bool last) {
            if (panel == null || Diagnostics.Quitting) return;
            var s = Connection.Current;
            // Leaving a room clears IsHost/IsClient before unloading the shop.
            // Its panels still need the deferred order during that teardown.
            if (s == null) {
                if (last) panel.SetAsLastSibling(); else panel.SetAsFirstSibling();
                return;
            }
            pending.RemoveAll(p => p.panel == panel || p.panel == null);
            pending.Add(new Pending { panel = panel, parent = panel.parent, last = last });
        }
        internal static void Flush() {
            if (!Diagnostics.Quitting) foreach (var p in pending) {
                if (p.panel == null || p.parent == null || p.panel.parent != p.parent) continue;
                if (p.last) p.panel.SetAsLastSibling(); else p.panel.SetAsFirstSibling();
            }
            pending.Clear();
        }
        [HarmonyPatch]
        internal static class Lifecycle {
            static IEnumerable<MethodBase> TargetMethods() {
                yield return AccessTools.Method(typeof(StoreUI), "OnEnable");
                yield return AccessTools.Method(typeof(StoreUI), "OnDisable");
            }
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
                var last = AccessTools.Method(typeof(Transform), "SetAsLastSibling");
                var first = AccessTools.Method(typeof(Transform), "SetAsFirstSibling");
                foreach (var code in instructions) {
                    if (code.Calls(last) || code.Calls(first)) {
                        bool toLast = code.Calls(last);
                        code.opcode = OpCodes.Call;
                        code.operand = (toLast ? AccessTools.Method(typeof(PaperPanelOrder), "Last") : AccessTools.Method(typeof(PaperPanelOrder), "First"));
                    }
                    yield return code;
                }
            }
        }
    }
}
