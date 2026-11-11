using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    internal static class CounterTrades {
        sealed class PendingSale {
            internal StoreClient customer; internal int budget, total; internal float until;
            internal List<GameItem> items; internal List<long> values;
        }
        static readonly Dictionary<int, PendingSale> pending = new Dictionary<int, PendingSale>();
        static readonly MethodInfo sellable = AccessTools.Method(typeof(NegociationUIManager), "GetSellableBackInvItems");
        internal static void Reset() { pending.Clear(); }
        static StoreClient Customer() {
            var store = PlayerStore.Instance;
            return store != null && store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null;
        }
        static List<GameItem> Sellable(StoreClient c) {
            return c == null || sellable == null ? null : sellable.Invoke(null, new object[] { c }) as List<GameItem>;
        }
        internal static bool BeginConfirmation(int owner, NegociationUIManager ui, out int budget, out int total) {
            budget = total = 0; var customer = Customer(); var items = Sellable(customer);
            if (owner <= 0 || customer == null || items == null || items.Count == 0 || customer.isBusinessComplete || NegociationUIManager.IsTradeLocked()) return false;
            budget = customer.GetBudget(); total = ui.GetMultiSellValue();
            var values = new List<long>(); foreach (var item in items) values.Add(item.GetNegociatedValue());
            pending[owner] = new PendingSale { customer = customer, budget = budget, total = total,
                items = items, values = values, until = Time.realtimeSinceStartup + 300f };
            return true;
        }
        static void ConfirmSale(Connection session, int owner, NegociationUIManager ui) {
            PendingSale p; if (!pending.TryGetValue(owner, out p)) return; pending.Remove(owner);
            var customer = Customer(); var items = Sellable(customer);
            if (customer != p.customer || customer == null || customer.isBusinessComplete || NegociationUIManager.IsTradeLocked() ||
                Time.realtimeSinceStartup > p.until || customer.GetBudget() != p.budget || ui.GetMultiSellValue() != p.total ||
                items == null || items.Count != p.items.Count) return;
            for (int i = 0; i < items.Count; i++) {
                int id = session.World.Id(items[i]);
                if (items[i] != p.items[i] || items[i].GetNegociatedValue() != p.values[i] || id <= 0 ||
                    !session.Leases.Available(owner, id, Time.realtimeSinceStartup)) return;
            }
            ui.SellMultiple(items, true);
        }
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
            if (session == null || ui == null || action < 0 || action > 4 ||
                !Allowed(RemoteActions.CurrentOwner) || !NativeActions.ShopAvailable(session, RemoteActions.CurrentOwner)) return;
            if (action == 4) { pending.Remove(RemoteActions.CurrentOwner); return; }
            if (item != null) {
                var at = session.World.Address(item.parentInventory);
                if (at == null || (at.root != 2 && at.root != 3 && at.root != OutingKits.CounterRoot)) return;
            }
            if (action == 0 && item == null) return;
            var store = PlayerStore.Instance;
            if (action == 0 && (store == null || EmporiumEntry.Instance == null || item.parentInventory != EmporiumEntry.Instance.backInvinvElement)) return;
            var previousStore = store != null ? store.currentNegociatedItem : null;
            var previousItem = ModReflect.Get<GameItem>(ui, "currentNegociatedItem");
            var previousSelection = ModReflect.Get<GameItem>(ui, "selectedItem");
            ModReflect.Set(ui, "selectedItem", item);
            ModReflect.Set(ui, "currentNegociatedItem", item);
            try {
                if (action == 0) store.currentNegociatedItem = item;
                switch (action) {
                    case 0: ui.OnOfferAcceptClick(); break;
                    case 1: ui.OnBuyAllClick(); break;
                    case 2: ui.OnSellAllClick(); break;
                    case 3: ConfirmSale(session, RemoteActions.CurrentOwner, ui); break;
                }
            } finally {
                // A friend's selection must not replace the host's own selection after their action.
                if (store != null && previousStore != null && previousStore.parentInventory == EmporiumEntry.Instance.backInvinvElement) store.currentNegociatedItem = previousStore;
                ModReflect.Set(ui, "currentNegociatedItem", previousItem != null && previousItem.parentInventory == EmporiumEntry.Instance.backInvinvElement ? previousItem : null);
                ModReflect.Set(ui, "selectedItem", previousSelection != null && previousSelection.parentInventory == EmporiumEntry.Instance.backInvinvElement ? previousSelection : null);
            }
        }
    }

    [HarmonyPatch]
    internal static class ClientCounterTrade {
        static readonly string[] Actions = { "OnOfferAcceptClick", "OnBuyAllClick", "OnSellAllClick", "OnConfirmPanelYes", "OnConfirmPanelNo" };
        static IEnumerable<MethodBase> TargetMethods() { foreach (var name in Actions) yield return AccessTools.Method(typeof(NegociationUIManager), name); }
        [HarmonyPriority(Priority.First)]
        static bool Prefix(NegociationUIManager __instance, MethodBase __originalMethod) {
            if (!ReplicaSession.ClientStore) return true;
            if (__originalMethod.Name == "OnOfferAcceptClick" && ShopActions.FriendAcceptsSpecialDeal()) return true;
            if (ReplicaSession.IsReplica && LocalControl.UserAction)
                RemoteActions.Forward(RemoteActions.Adapter("CounterAction", 2), null, new object[] {
                    Array.IndexOf(Actions, __originalMethod.Name), ModReflect.Get<GameItem>(__instance, "currentNegociatedItem") });
            if (__originalMethod.Name == "OnConfirmPanelNo") return true;
            if (__originalMethod.Name == "OnConfirmPanelYes") {
                var frame = ModReflect.Get<NegotiationTabletFrame>(__instance, "frame");
                if (frame != null && frame.confirmRoot != null) frame.confirmRoot.SetActive(false);
            }
            return false;
        }
    }
}
