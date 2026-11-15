using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ShopTogether {
    // A validated host operation may invoke native inventory placement and item
    // activation as effects. They are not fresh mouse gestures or new requests.
    internal sealed class HostNativeScope : IDisposable {
        readonly bool previous;
        HostNativeScope() { previous = NativeActions.Executing; NativeActions.Executing = true; }
        internal static HostNativeScope Begin() {
            var s = Connection.Current;
            return s != null && s.IsHost && s.Ready && !WorldReplica.Applying ? new HostNativeScope() : null;
        }
        public void Dispose() { NativeActions.Executing = previous; if (!previous) { var s=Connection.Current; if(s!=null&&s.IsHost)s.SyncStateSoon(); } }
    }
    [HarmonyPatch]
    internal static class HostTradeEffects {
        static IEnumerable<MethodBase> TargetMethods() {
            foreach (var name in new[] { "OnOfferAcceptClick", "OnBuyAllClick", "OnSellAllClick", "OnWholesaleClick", "BuyMultipleItem", "SellMultiple", "BuyItem", "SellItem" })
                yield return AccessTools.Method(typeof(NegociationUIManager), name);
            foreach (var name in new[] { "BuyItemFromTable", "SellItemFromTable", "BuyItem", "SellItem", "AcceptBarter" })
                yield return AccessTools.Method(typeof(PlayerStore), name);
            yield return AccessTools.Method(typeof(CommissaryData), "BuyItem");
            yield return AccessTools.Method(typeof(CommissaryUIManager), "OnRedeemAllButtonClick");
            yield return AccessTools.Method(typeof(CommissaryUIManager), "OnRedeemAllLottery");
            yield return AccessTools.Method(typeof(AppraisalServiceHelper), "OnEstimate");
            yield return AccessTools.Method(typeof(StoreRequest), "FillRequest");
            yield return AccessTools.Method(typeof(Dialogue), "ExecuteEndAction");
            yield return AccessTools.Method(typeof(DialogUIManager), "SelectChoice");
            yield return AccessTools.Method(typeof(DialogUIManager), "OnAdvancePressed");
            yield return AccessTools.Method(typeof(DialoguePhoneUIManager), "SelectChoice");
            yield return AccessTools.Method(typeof(DialoguePhoneUIManager), "OnAdvancePressed");
        }
        // Run after our validation/forwarding prefixes, before installed native
        // transaction callbacks. Finalizers restore the scope even on refusal/errors.
        [HarmonyPriority(Priority.First + 50)]
        static void Prefix(out HostNativeScope __state) { __state = HostNativeScope.Begin(); }
        static Exception Finalizer(Exception __exception, HostNativeScope __state) { if (__state != null) __state.Dispose(); return __exception; }
    }
    [HarmonyPatch]
    internal static class HostPhoneTradeInput {
        static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(DialoguePhoneUIManager), "SelectChoice");
            yield return AccessTools.Method(typeof(DialoguePhoneUIManager), "OnAdvancePressed");
        }
        [HarmonyPriority(Priority.First + 200)]
        static bool Prefix() {
            var s = Connection.Current;
            if (s == null || !s.IsHost || !s.Ready || WorldReplica.Applying || NativeActions.Executing || !LocalControl.UserAction) return true;
            if (DialogueIntent.Available(s, 0)) return true;
            RemoteActions.Notice(RemoteActions.TakeNote()); return false;
        }
    }

}
