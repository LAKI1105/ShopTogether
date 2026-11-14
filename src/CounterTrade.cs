using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
            if (session == null || ui == null || action < 0 || action > 4) return;
            int owner = RemoteActions.CurrentOwner;
            // Every refusal is told to the friend who pressed the button; a silent no-op looks like "friends cannot trade".
            if (!Allowed(owner)) { Refuse(action, "~SMC289~", "outing"); return; }
            if (!NativeActions.ShopAvailable(session, owner)) { Refuse(action, "~SMC290~", "counter item held by someone else"); return; }
            if (action == 4) { pending.Remove(owner); return; }
            if (!CustomerReady(action)) return;
            if (item != null) {
                var at = session.World.Address(item.parentInventory);
                if (at == null || (at.root != 2 && at.root != 3 && at.root != OutingKits.CounterRoot)) { Refuse(action, "~SMC291~", "item not on the counter"); return; }
            }
            if (action == 0 && item == null) { Refuse(action, "~SMC291~", "no item"); return; }
            var store = PlayerStore.Instance;
            if (action == 0 && (store == null || EmporiumEntry.Instance == null || item.parentInventory != EmporiumEntry.Instance.backInvinvElement)) { Refuse(action, "~SMC291~", "item not on the back counter"); return; }
            var before = Snapshot();
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
            // Nothing changed and the game said nothing: tell the friend the likely reason and log the details.
            if (action <= 2 && RemoteActions.PendingNote == null && !PanelRelay.NotifiedDuringAction && before == Snapshot()) {
                string reason; string note = Why(action, item, out reason);
                Refuse(action, note, reason);
            }
        }

        // Barter and other special offers use a different adapter but can also consume counter items.
        // Apply the same ownership and location checks before letting the host perform the deal.
        internal static void SpecialAction() {
            var session = Connection.Current; int owner = RemoteActions.CurrentOwner;
            if (session == null || NegociationUIManager.Instance == null) return;
            if (!Allowed(owner)) { Refuse(0, "~SMC289~", "outing"); return; }
            if (!NativeActions.ShopAvailable(session, owner)) { Refuse(0, "~SMC290~", "counter item held by someone else"); return; }
            if (!CustomerReady(0)) return;
            var before = Snapshot();
            NegociationUIManager.Instance.OnOfferAcceptClick();
            if (RemoteActions.PendingNote == null && !PanelRelay.NotifiedDuringAction && before == Snapshot()) {
                string reason; string note = Why(0, null, out reason); Refuse(0, note, reason);
            }
        }

        static void Refuse(int action, string note, string reason) {
            RemoteActions.PendingNote = note;
            var c = Customer();
            Diagnostics.Report("counter trade", new InvalidOperationException("action " + action + " from friend " + RemoteActions.CurrentOwner + " not done: " + reason +
                (c != null ? "; intent=" + c.clientIntent + " introduced=" + c.isIntroduced + " complete=" + c.isBusinessComplete : "; no customer") +
                "; cash=" + (PlayerStore.Instance != null ? PlayerStore.Instance.playerCash : -1)));
        }

        static bool CustomerReady(int action) {
            var store = PlayerStore.Instance; var customer = Customer();
            if (store == null || store.currentClientInstance == null) { Refuse(action, "~SMC292~", "no customer at the counter"); return false; }
            if (NegociationUIManager.IsTradeLocked()) { Refuse(action, "~SMC293~", "trade locked (arrest or shootout)"); return false; }
            if (customer == null || customer.isBusinessComplete) { Refuse(action, "~SMC294~", "customer finished"); return false; }
            if (!customer.isIntroduced || customer.clientIntent == StoreClient.ClientIntent.UNDEFINED) { Refuse(action, "~SMC295~", "customer still talking"); return false; }
            return true;
        }

        /// <summary>What a trade changes: money, the customer's state and what lies on the counters.</summary>
        static string Snapshot() {
            var store = PlayerStore.Instance; var e = EmporiumEntry.Instance; var c = Customer();
            return (store != null ? store.playerCash : -1) + "|" + (c != null ? c.isBusinessComplete + "/" + c.GetBudget() : "-") + "|" +
                (e != null && e.backInvinvElement != null ? e.backInvinvElement.childItems.Count : -1) + "|" +
                (e != null && e.frontInvinvElement != null ? e.frontInvinvElement.childItems.Count : -1) + "|" +
                (e != null && e.backInvinvElement != null ? OwnedCount(e.backInvinvElement) : -1);
        }
        static int OwnedCount(GameInventory inventory) { int n = 0; foreach (var item in inventory.childItems) if (item != null && GeneralHelper.IsItemOwned(item)) n++; return n; }

        static string Why(int action, GameItem item, out string reason) {
            var store = PlayerStore.Instance;
            if (store == null || store.currentClientInstance == null) { reason = "no customer at the counter"; return "~SMC292~"; }
            if (NegociationUIManager.IsTradeLocked()) { reason = "trade locked (arrest or shootout)"; return "~SMC293~"; }
            var c = Customer();
            if (c == null || c.isBusinessComplete) { reason = "customer finished"; return "~SMC294~"; }
            if (!c.isIntroduced || c.clientIntent == StoreClient.ClientIntent.UNDEFINED) { reason = "customer still talking"; return "~SMC295~"; }
            if (action == 1) {
                long sum = 0; var e = EmporiumEntry.Instance;
                if (e != null && e.backInvinvElement != null) foreach (var x in e.backInvinvElement.childItems) if (x != null && !GeneralHelper.IsItemOwned(x)) sum += x.GetNegociatedValue();
                if (sum > store.playerCash) { reason = "not enough cash " + sum + " > " + store.playerCash; return "~SMC296~"; }
            }
            reason = "the game made no change"; return "~SMC297~";
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

    // A disabled tablet button normally swallows the click before the host can explain why it is unavailable.
    // Forward only the two meaningful blocked cases; the host still checks the complete transaction.
    [HarmonyPatch(typeof(Button), "OnPointerClick")]
    internal static class ClientDisabledTradeNotice {
        static void Postfix(Button __instance, PointerEventData eventData) {
            if (!ReplicaSession.ClientStore || !ReplicaSession.SceneReady || WorldReplica.Applying || !LocalControl.Focused ||
                __instance == null || __instance.interactable || eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            var ui = NegociationUIManager.Instance; var store = PlayerStore.Instance;
            if (ui == null || store == null || store.currentClientInstance == null) return;
            var button = ModReflect.Get<TabletButton>(ui, "primaryButton");
            if (button == null || button.button != __instance) return;
            var customer = store.currentClientInstance.GetClientBlueprint(); if (customer == null) return;
            bool talking = !customer.isIntroduced || customer.clientIntent == StoreClient.ClientIntent.UNDEFINED;
            bool poor = false;
            if (customer.clientIntent == StoreClient.ClientIntent.SELL && EmporiumEntry.Instance != null && EmporiumEntry.Instance.backInvinvElement != null) {
                long total=0; foreach (var item in EmporiumEntry.Instance.backInvinvElement.childItems)
                    if (item != null && !GeneralHelper.IsItemOwned(item)) total += item.GetNegociatedValue();
                poor = total > store.playerCash;
            }
            if (!talking && !poor) return;
            if (ShopActions.FriendAcceptsSpecialDeal()) RemoteActions.Forward(RemoteActions.Adapter("AcceptDeal",0),null,new object[0]);
            else RemoteActions.Forward(RemoteActions.Adapter("CounterAction",2),null,new object[]{customer.clientIntent==StoreClient.ClientIntent.SELL?1:2,null});
        }
    }
}
