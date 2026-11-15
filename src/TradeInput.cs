using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine.UI;

namespace ShopTogether {
    [HarmonyPatch]
    internal static class TradeIntentButtons {
        static IEnumerable<MethodBase> TargetMethods() {
            foreach (var name in new[] { "BuildBuying", "BuildSelling", "BuildBarter", "BuildInfoDealer", "BuildGunOrder" })
                yield return AccessTools.Method(typeof(NegociationUIManager), name);
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            foreach (var code in instructions) {
                var method = code.operand as MethodInfo;
                if (method != null && method.Name == "SetInteractable" && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(bool)) {
                    var ask = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AuthorityIntent), "AskDisplay"));
                    ask.labels.AddRange(code.labels); code.labels.Clear(); yield return ask;
                }
                yield return code;
            }
        }
    }
    [HarmonyPatch(typeof(WildNetworkUI), "ShowInfo")]
    internal static class WildIntentButton {
        static void Postfix(string id) {
            if (!ReplicaSession.ClientStore || string.IsNullOrEmpty(id)) return;
            var frame = AccessTools.Field(typeof(WildNetworkUI), "frame").GetValue(null);
            if (frame == null) return;
            var button = AccessTools.Field(frame.GetType(), "useButton").GetValue(frame) as Button;
            if (button != null) { button.gameObject.SetActive(true); button.interactable = true; }
        }
    }
    [HarmonyPatch(typeof(WildUIManager), "OnElementClicked")]
    internal static class LegacyWildIntentButton {
        static void Postfix(WildUIManager __instance) {
            if (!ReplicaSession.ClientStore || __instance.unlockButton == null) return;
            __instance.unlockButton.gameObject.SetActive(true); __instance.unlockButton.interactable = true;
        }
    }
    // The night exchange is native simulation, never an independent guest action.
    // CanAccept is checked before the caller grants reputation, not just before destruction.
    [HarmonyPatch(typeof(ExchangeBarter), "CanAccept")]
    internal static class ExchangeGoodsAvailable {
        static void Postfix(ExchangeBarter __instance, ref bool __result) {
            var s = Connection.Current;
            if (!__result || s == null || !s.IsHost || !s.Ready || WorldReplica.Applying) return;
            string previous = RemoteActions.PendingNote;
            try { foreach (var item in __instance.GetRequiredItems()) if (!AuthorityIntent.FreeTree(s, -1, item)) { __result = false; return; } }
            finally { RemoteActions.PendingNote = previous; }
        }
    }
}
