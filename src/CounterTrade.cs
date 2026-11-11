using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ShopTogether {
    internal static class CounterTrades {
        internal static bool Allowed(int owner) {
            var map = MapUIManager.Instance;
            return owner <= 0 || map == null || map.Current == null ||
                OutingKits.FriendMayGo(map.Current.Id) || OutingKits.IsJoined(owner);
        }

        // The local tablet selects an item locally. Send that item with the actual trade action,
        // then let the host's native trade checks decide the price, money and outcome.
        internal static void CounterAction(int action, GameItem item) {
            var session = Connection.Current;
            var ui = NegociationUIManager.Instance;
            if (session == null || ui == null || action < 0 || action > 3 ||
                !Allowed(RemoteActions.CurrentOwner) || !NativeActions.ShopAvailable(session, RemoteActions.CurrentOwner)) return;
            if (item != null) {
                var at = session.World.Address(item.parentInventory);
                if (at == null || (at.root != 2 && at.root != 3 && at.root != OutingKits.CounterRoot)) return;
            }
            if (action == 0 && item == null) return;
            ModReflect.Set(ui, "selectedItem", item);
            ModReflect.Set(ui, "currentNegociatedItem", item);
            switch (action) {
                case 0: ui.OnOfferAcceptClick(); break;
                case 1: ui.OnBuyAllClick(); break;
                case 2: ui.OnSellAllClick(); break;
                case 3: ui.OnConfirmPanelYes(); break;
            }
        }
    }

    [HarmonyPatch]
    internal static class ClientCounterTrade {
        static readonly string[] Actions = { "OnOfferAcceptClick", "OnBuyAllClick", "OnSellAllClick", "OnConfirmPanelYes" };
        static IEnumerable<MethodBase> TargetMethods() { foreach (var name in Actions) yield return AccessTools.Method(typeof(NegociationUIManager), name); }
        [HarmonyPriority(Priority.First)]
        static bool Prefix(NegociationUIManager __instance, MethodBase __originalMethod) {
            if (!ReplicaSession.ClientStore) return true;
            if (__originalMethod.Name == "OnOfferAcceptClick" && ShopActions.FriendAcceptsSpecialDeal()) return true;
            if (ReplicaSession.IsReplica && LocalControl.UserAction)
                RemoteActions.Forward(RemoteActions.Adapter("CounterAction", 2), null, new object[] {
                    Array.IndexOf(Actions, __originalMethod.Name), ModReflect.Get<GameItem>(__instance, "currentNegociatedItem") });
            return false;
        }
    }
}
