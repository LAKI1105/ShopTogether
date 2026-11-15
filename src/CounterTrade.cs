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
            internal StoreClient customer; internal int budget, total, token; internal float until;
            internal List<GameItem> items; internal List<long> values; internal List<string> stamps;
        }
        static int saleSerial;
        static readonly Dictionary<int, PendingSale> pending = new Dictionary<int, PendingSale>();
        static readonly MethodInfo sellable = AccessTools.Method(typeof(NegociationUIManager), "GetSellableBackInvItems");
        internal static void Reset() { pending.Clear(); }
        static StoreClient Customer() {
            var store = PlayerStore.Instance;
            return store != null && store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null;
        }
        internal static bool UsesBuySelection { get { var c = Customer(); return c != null && c.isMultiBuyDisabled; } }
        static List<GameItem> Sellable(StoreClient c) {
            return c == null || sellable == null ? null : sellable.Invoke(null, new object[] { c }) as List<GameItem>;
        }
        internal static bool BeginConfirmation(int owner, NegociationUIManager ui, out int budget, out int total) {
            pending.Remove(owner);
            budget = total = 0; var customer = Customer(); var items = Sellable(customer);
            if (owner < 0 || customer == null || items == null || items.Count == 0 || customer.isBusinessComplete || NegociationUIManager.IsTradeLocked()) return false;
            if (!PriceRange(items, false)) return false;
            budget = customer.GetBudget(); total = ui.GetMultiSellValue();
            var values = new List<long>(); var stamps = new List<string>();
            foreach (var item in items) { values.Add(item.GetNegociatedValue()); stamps.Add(AuthorityIntent.TreeStamp(Connection.Current, item)); }
            if (++saleSerial <= 0) saleSerial = 1;
            pending[owner] = new PendingSale { token = saleSerial, customer = customer, budget = budget, total = total,
                items = new List<GameItem>(items), values = values, stamps = stamps, until = Time.realtimeSinceStartup + 300f };
            return true;
        }
        internal static void ConfirmSale(Connection session, int owner, NegociationUIManager ui) {
            // Each player confirms what they were shown; if the deal changed meanwhile they are told to look again.
            PendingSale p; if (!pending.TryGetValue(owner, out p)) { RemoteActions.PendingNote = "~SMC301~"; return; } pending.Remove(owner);
            var customer = Customer(); var items = Sellable(customer);
            if (customer != p.customer || customer == null || customer.isBusinessComplete || NegociationUIManager.IsTradeLocked() ||
                Time.realtimeSinceStartup > p.until || customer.GetBudget() != p.budget || ui.GetMultiSellValue() != p.total ||
                items == null || items.Count != p.items.Count) { RemoteActions.PendingNote = "~SMC301~"; return; }
            for (int i = 0; i < items.Count; i++) {
                int id = session.World.Id(items[i]);
                if (items[i] != p.items[i] || items[i].GetNegociatedValue() != p.values[i] || AuthorityIntent.TreeStamp(session, items[i]) != p.stamps[i] || id <= 0) { RemoteActions.PendingNote = "~SMC301~"; return; }
                if (!session.Leases.Available(owner, id, Time.realtimeSinceStartup)) { RemoteActions.PendingNote = "~SMC290~"; return; }
            }
            ui.SellMultiple(items, true);
        }
        // Everyone is where the host is (in the shop, or along at Dr. Jackson's counter), so the counter is everyone's.
        internal static bool Allowed(int owner) { return true; }

        // The local tablet selects an item locally. Send that item with the actual trade action,
        // then let the host's native trade checks decide the price, money and outcome.
        internal static void CounterAction(int action, GameItem item) {
            var session = Connection.Current;
            var ui = NegociationUIManager.Instance;
            if (session == null || ui == null || action < 0 || action > 4) { RemoteActions.PendingNote = "~SMC305~"; return; }
            int owner = RemoteActions.CurrentOwner;
            // Every refusal is told to the friend who pressed the button; a silent no-op looks like "friends cannot trade".
            if (action >= 3 && owner > 0) { RemoteActions.PendingNote = "~SMC301~"; return; }
            if (action == 4) { pending.Remove(Math.Max(0, owner)); return; }
            if (!Allowed(owner)) { Refuse(action, "~SMC289~", "outing"); return; }
            // Bulk native methods check their selected batch before the first effect.
            // Unrelated goods on the counter must not block a purchase or sale.
            if (!CustomerReady(action)) return;
            if (action == 0 && item != null) {
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
                if (store != null) store.currentNegociatedItem = previousStore != null && previousStore.parentInventory == EmporiumEntry.Instance.backInvinvElement ? previousStore : null;
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
        internal static void SpecialAction(GameItem selected) {
            var session = Connection.Current; int owner = RemoteActions.CurrentOwner;
            if (session == null || NegociationUIManager.Instance == null) return;
            if (!Allowed(owner)) { Refuse(0, "~SMC289~", "outing"); return; }
            if (!CustomerReady(0)) return;
            var before = Snapshot();
            var store = PlayerStore.Instance; var previous = store.currentNegociatedItem;
            bool gun = Customer().clientIntent == StoreClient.ClientIntent.GUNSMITH;
            if (gun && selected != null && selected.parentInventory != EmporiumEntry.Instance.backInvinvElement) { Refuse(0, "~SMC291~", "gun not on the back counter"); return; }
            try {
                if (gun) store.currentNegociatedItem = selected;
                NegociationUIManager.Instance.OnOfferAcceptClick();
            } finally {
                if (gun) store.currentNegociatedItem = previous != null && previous.parentInventory == EmporiumEntry.Instance.backInvinvElement ? previous : null;
            }
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

        // The game waits for the introduction only for barter offers and information dealers; ordinary buyers and sellers
        // are never "introduced", and requiring it refused every ordinary sale from friends.
        internal static bool StillTalking(StoreClient c) {
            if (c.clientIntent == StoreClient.ClientIntent.UNDEFINED) return true;
            return !c.isIntroduced && (c.clientIntent == StoreClient.ClientIntent.BARTER || c.clientIntent == StoreClient.ClientIntent.INFORMATION_DEALER);
        }

        internal static bool CustomerReady(int action) {
            if (ProbablyMultiplayerCompat.VisitActive) { Refuse(action, "~SMC271~", "human visitor trade uses its own panel"); return false; }
            if (DialogueIntent.Committing) { Refuse(action, "~SMC353~", "dialogue delivery pending"); return false; }
            var store = PlayerStore.Instance; var customer = Customer();
            if (store == null || store.currentClientInstance == null) { Refuse(action, "~SMC292~", "no customer at the counter"); return false; }
            if (NegociationUIManager.IsTradeLocked()) { Refuse(action, "~SMC293~", "trade locked (arrest or shootout)"); return false; }
            if (customer == null || customer.isBusinessComplete) { Refuse(action, "~SMC294~", "customer finished"); return false; }
            if (StillTalking(customer)) { Refuse(action, "~SMC295~", "customer still talking"); return false; }
            return true;
        }

        internal static bool OfferAvailable(NegociationUIManager ui) {
            var s = Connection.Current; var store = PlayerStore.Instance; var c = Customer();
            int owner = Math.Max(0, RemoteActions.CurrentOwner);
            if (c == null) return AuthorityIntent.Refuse("~SMC292~");
            if (c.clientIntent == StoreClient.ClientIntent.BARTER)
                return (c.barterOffer != null && c.barterOffer.IsBarterAcceptable() || AuthorityIntent.Refuse("~SMC357~")) && DialogueIntent.Available(s, owner); // Both offered and received goods change ownership.
            if (c.clientIntent == StoreClient.ClientIntent.INFORMATION_DEALER)
                return DialoguePayment.AllowSpending() && (store.CanPay(c.serviceCost) || AuthorityIntent.Refuse("~SMC296~"));
            var item = c.clientIntent == StoreClient.ClientIntent.GUNSMITH ? GunOrderData.ResolveOrderGun() : store.currentNegociatedItem;
            if (item == null) return AuthorityIntent.Refuse("~SMC291~");
            if (c.clientIntent != StoreClient.ClientIntent.GUNSMITH && !GeneralHelper.IsItemOwned(item) && !DialoguePayment.AllowSpending()) return false;
            if (c.clientIntent == StoreClient.ClientIntent.GUNSMITH && (c.gunOrderData == null || !c.gunOrderData.IsAccepted())) return AuthorityIntent.Refuse("~SMC358~");
            if (!DialogueIntent.MayChange(s, item) || !AuthorityIntent.FreeTree(s, owner, item)) return false;
            if (c.clientIntent == StoreClient.ClientIntent.GUNSMITH) { long reward = c.gunOrderData.GetTotalReward(item); if (reward < 0 || (long)store.playerCash + reward > int.MaxValue) return AuthorityIntent.Refuse("~SMC356~"); }
            if (c.clientIntent != StoreClient.ClientIntent.GUNSMITH && !PriceRange(new[] { item }, !GeneralHelper.IsItemOwned(item))) return false;
            if (c.clientIntent != StoreClient.ClientIntent.GUNSMITH && GeneralHelper.IsItemOwned(item) && !c.IsClientBuyingThisItem(item)) return AuthorityIntent.Refuse("~SMC359~");
            if (c.clientIntent != StoreClient.ClientIntent.GUNSMITH && !GeneralHelper.IsItemOwned(item) && !store.CanBuyItemFromTable())
                return AuthorityIntent.Refuse(item.GetNegociatedValue() > store.playerCash ? "~SMC296~" : "~SMC297~");
            return true;
        }
        internal static bool PriceRange(IEnumerable<GameItem> items, bool buying) {
            long total = 0;
            foreach (var item in items) {
                if (!AuthorityIntent.Live(item)) return AuthorityIntent.Refuse("~SMC095~");
                long value = item.GetNegociatedValue();
                if (value < 0 || value > int.MaxValue || total > int.MaxValue - value) return AuthorityIntent.Refuse("~SMC356~");
                total += value;
            }
            var store = PlayerStore.Instance; var c = Customer();
            if (buying) return total <= store.playerCash || AuthorityIntent.Refuse("~SMC296~");
            long income = c != null && c.useClientBudget ? Math.Min(total, Math.Max(0, c.GetBudget())) : total;
            return (long)store.playerCash + income <= int.MaxValue || AuthorityIntent.Refuse("~SMC356~");
        }
        internal static bool BatchAvailable(int action, NegociationUIManager ui) {
            var c = Customer(); List<GameItem> items;
            if (action == 1) {
                if (!DialoguePayment.AllowSpending()) return false;
                if (c.isRegularSellDisable) return AuthorityIntent.Refuse("~SMC247~");
                items = new List<GameItem>();
                foreach (var item in EmporiumEntry.Instance.backInvinvElement.childItems) if (!GeneralHelper.IsItemOwned(item)) items.Add(item);
                if (c.isMultiBuyDisabled && items.Count > 1) {
                    var selected = ModReflect.Get<GameItem>(ui, "selectedItem");
                    items = new List<GameItem> { selected != null && items.Contains(selected) ? selected : items[0] };
                }
            } else items = Sellable(c);
            if (items == null || items.Count == 0) return AuthorityIntent.Refuse(action == 1 ? "~SMC360~" : "~SMC361~");
            return PriceRange(items, action == 1);
        }
        internal static int ConfirmationToken(int owner) { PendingSale p; return pending.TryGetValue(owner, out p) ? p.token : 0; }
        internal static void ResolveConfirmation(int token, bool accept) {
            int owner = Math.Max(0, RemoteActions.CurrentOwner); PendingSale p;
            if (token <= 0 || !pending.TryGetValue(owner, out p) || p.token != token) { AuthorityIntent.Refuse("~SMC301~"); return; }
            if (!accept) { pending.Remove(owner); return; }
            if (!CustomerReady(3)) { pending.Remove(owner); return; }
            using (HostNativeScope.Begin()) ConfirmSale(Connection.Current, owner, NegociationUIManager.Instance);
        }
        internal static void BindConfirmation(NegociationUIManager ui, int token) {
            var frame = ModReflect.Get<NegotiationTabletFrame>(ui, "frame");
            if (frame == null || frame.confirmButtonHost == null || token <= 0) return;
            TabletUI.ClearChildren(frame.confirmButtonHost);
            Action<bool> answer = accept => {
                HideConfirmation(ui);
                if (ReplicaSession.ClientStore) RemoteActions.Forward(RemoteActions.Adapter("ResolveConfirmation", 2), null, new object[] { token, accept });
                else { RemoteActions.PendingNote = null; ResolveConfirmation(token, accept); if (RemoteActions.PendingNote != null) RemoteActions.Notice(RemoteActions.TakeNote()); }
            };
            TabletUI.BigButton(frame, frame.confirmButtonHost, 57f, 0f, LocHelper.GetLocalizedMainMenu("yes"), () => answer(true), 200f);
            TabletUI.BigButton(frame, frame.confirmButtonHost, 273f, 0f, LocHelper.GetLocalizedMainMenu("no"), () => answer(false), 200f);
        }
        internal static void CancelConfirmation(int owner) { pending.Remove(owner); }
        internal static void HideConfirmation(NegociationUIManager ui) {
            var frame = ModReflect.Get<NegotiationTabletFrame>(ui, "frame");
            if (frame != null && frame.confirmRoot != null) frame.confirmRoot.SetActive(false);
        }

        /// <summary>What a trade changes: money, the customer's state and what lies on the counters.</summary>
        static string Snapshot() {
            var store = PlayerStore.Instance; var e = EmporiumEntry.Instance; var c = Customer();
            return (store != null ? store.playerCash : -1) + "|" + (c != null ? c.isBusinessComplete + "/" + c.GetBudget() : "-") + "|" +
                (e != null && e.backInvinvElement != null ? e.backInvinvElement.childItems.Count : -1) + "|" +
                (e != null && e.frontInvinvElement != null ? e.frontInvinvElement.childItems.Count : -1) + "|" +
                (e != null && e.backInvinvElement != null ? OwnedCount(e.backInvinvElement) : -1) + "|" + CounterContents();
        }
        static string CounterContents() {
            var s=Connection.Current;var e=EmporiumEntry.Instance;if(s==null||e==null)return "";
            var b=new System.Text.StringBuilder();
            foreach(var item in e.backInvinvElement.childItems)b.Append(AuthorityIntent.TreeStamp(s,item)).Append(';');
            return b.ToString(); // Reinspection changes features without exchanging cash or removing goods.
        }
        static int OwnedCount(GameInventory inventory) { int n = 0; foreach (var item in inventory.childItems) if (item != null && GeneralHelper.IsItemOwned(item)) n++; return n; }

        static string Why(int action, GameItem item, out string reason) {
            var store = PlayerStore.Instance;
            if (store == null || store.currentClientInstance == null) { reason = "no customer at the counter"; return "~SMC292~"; }
            if (NegociationUIManager.IsTradeLocked()) { reason = "trade locked (arrest or shootout)"; return "~SMC293~"; }
            var c = Customer();
            if (c == null || c.isBusinessComplete) { reason = "customer finished"; return "~SMC294~"; }
            if (StillTalking(c)) { reason = "customer still talking"; return "~SMC295~"; }
            if (action == 1) {
                long sum = 0; var e = EmporiumEntry.Instance;
                if (e != null && e.backInvinvElement != null) foreach (var x in e.backInvinvElement.childItems) if (x != null && !GeneralHelper.IsItemOwned(x)) sum += x.GetNegociatedValue();
                if (sum > store.playerCash) { reason = "not enough cash " + sum + " > " + store.playerCash; return "~SMC296~"; }
            }
            reason = "the game made no change"; return "~SMC297~";
        }
    }

    // BuyItem is void and another mod may decline it. Do not run the native
    // deal-accepted callback merely because the outer tablet method returned.
    [HarmonyPatch]
    internal static class CounterPurchaseOutcome {
        internal sealed class Purchase {
            internal Purchase previous;internal List<GameItem> items;internal bool reported;
            internal int Bought { get { int count=0;foreach(var item in items)if(item!=null&&GeneralHelper.IsItemOwned(item))count++;return count; } }
        }
        static Purchase current;
        static IEnumerable<MethodBase> TargetMethods(){yield return AccessTools.Method(typeof(NegociationUIManager),"BuyItem");yield return AccessTools.Method(typeof(NegociationUIManager),"BuyMultipleItem");yield return AccessTools.Method(typeof(NegociationUIManager),"OnWholesaleClick");}
        [HarmonyPriority(Priority.First+125)]
        static void Prefix(MethodBase __originalMethod,object[] __args,out Purchase __state){
            __state=null;var s=Connection.Current;if(s==null||!s.IsHost||!s.Ready||WorldReplica.Applying)return;
            var items=__originalMethod.Name=="OnWholesaleClick"?EmporiumEntry.Instance.GetAllNonOwnedItem():__args.Length>0?__args[0] as IEnumerable<GameItem>:new[]{PlayerStore.Instance.currentNegociatedItem};
            var selected=new List<GameItem>();if(items!=null)foreach(var item in items)if(item!=null&&!GeneralHelper.IsItemOwned(item))selected.Add(item);
            __state=new Purchase{previous=current,items=selected};current=__state;
        }
        internal static bool AcceptDialogue(){
            if(current==null||current.Bought>0)return true;
            Report(current);return false;
        }
        static void Report(Purchase state){
            if(state.reported)return;state.reported=true;
            if(RemoteActions.PendingNote==null)RemoteActions.PendingNote=state.Bought==0?"~SMC366~":"~SMC367~";
            if(RemoteActions.CurrentOwner<0)RemoteActions.Notice(RemoteActions.PendingNote);
        }
        static Exception Finalizer(Exception __exception,Purchase __state){
            if(__state==null)return __exception;
            try{var s=Connection.Current;if(s!=null){foreach(var item in __state.items)s.World.MarkDirty(item);s.SyncStateSoon();}
                if(__exception==null&&__state.Bought<__state.items.Count)Report(__state);
            }finally{current=__state.previous;}
            return __exception;
        }
    }
    [HarmonyPatch(typeof(NegociationUIManager),"PlayAcceptDealDialogueOnce")]
    internal static class CounterPurchaseAcceptance {
        static bool Prefix(ref bool __result){if(CounterPurchaseOutcome.AcceptDialogue())return true;__result=true;return false;}
    }

    // The tablet uses SellMultiple/BuyMultipleItem directly, bypassing the
    // older PlayerStore.*FromTable hooks. Check the entire selected batch before
    // the native loop can charge money or remove its first item.
    [HarmonyPatch]
    internal static class HostCounterCommitGuard {
        static int batchDepth;
        static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(NegociationUIManager), "SellMultiple");
            yield return AccessTools.Method(typeof(NegociationUIManager), "BuyMultipleItem");
            yield return AccessTools.Method(typeof(PlayerStore), "SellItem");
            yield return AccessTools.Method(typeof(PlayerStore), "BuyItem");
        }
        [HarmonyPriority(Priority.First + 200)]
        static bool Prefix(object __0, MethodBase __originalMethod, ref bool __state) {
            __state = false;
            if (batchDepth > 0 && __originalMethod.DeclaringType == typeof(PlayerStore)) return true;
            var s = Connection.Current;
            if (s == null || !s.IsHost || !s.Ready || WorldReplica.Applying) return true;
            // Native scripted effects retain their flow; UI and relayed commits
            // both pass here while their requesting player is still known.
            if (RemoteActions.CurrentOwner < 0 && !LocalControl.UserAction && !NativeActions.Executing) return true;
            int owner = Math.Max(0, RemoteActions.CurrentOwner);
            var one = __0 as GameItem;
            var items = one != null ? new[] { one } : __0 as IEnumerable<GameItem>;
            if (items == null) return true;
            if (!CounterTrades.PriceRange(items, __originalMethod.Name == "BuyItem" || __originalMethod.Name == "BuyMultipleItem")) { if (RemoteActions.CurrentOwner < 0) RemoteActions.Notice(RemoteActions.PendingNote); return false; }
            foreach (var item in items) {
                if (DialogueIntent.MayChange(s, item) && AuthorityIntent.FreeTree(s, owner, item)) continue;
                if (RemoteActions.CurrentOwner < 0) RemoteActions.Notice(RemoteActions.PendingNote ?? "~SMC096~");
                return false;
            }
            __state = __originalMethod.DeclaringType == typeof(NegociationUIManager);
            if (__state) batchDepth++;
            return true;
        }
        static Exception Finalizer(Exception __exception, bool __state) {
            if (__state) batchDepth--;
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class ClientCounterTrade {
        static readonly string[] Actions = { "OnOfferAcceptClick", "OnBuyAllClick", "OnSellAllClick", "OnConfirmPanelYes", "OnConfirmPanelNo" };
        static IEnumerable<MethodBase> TargetMethods() { foreach (var name in Actions) yield return AccessTools.Method(typeof(NegociationUIManager), name); }
        [HarmonyPriority(Priority.First + 200)]
        static bool Prefix(NegociationUIManager __instance, MethodBase __originalMethod) {
            if (!ReplicaSession.ClientStore) {
                var s = Connection.Current;
                if (s == null || !s.IsHost || !s.Ready || WorldReplica.Applying) return true;
                int action = Array.IndexOf(Actions, __originalMethod.Name);
                if (action == 4) { CounterTrades.CancelConfirmation(Math.Max(0, RemoteActions.CurrentOwner)); return true; }
                bool local = RemoteActions.CurrentOwner < 0;
                if (local) RemoteActions.PendingNote = null;
                bool allowed = CounterTrades.CustomerReady(action);
                if (allowed && action == 0) allowed = CounterTrades.OfferAvailable(__instance);
                if (allowed && (action == 1 || action == 2)) allowed = CounterTrades.BatchAvailable(action, __instance);
                if (allowed && action == 3) {
                    using (HostNativeScope.Begin()) CounterTrades.ConfirmSale(s, Math.Max(0, RemoteActions.CurrentOwner), __instance);
                    allowed = false;
                }
                if (action == 3) CounterTrades.HideConfirmation(__instance);
                if (local && RemoteActions.PendingNote != null) RemoteActions.Notice(RemoteActions.TakeNote());
                return allowed;
            }
            if (__originalMethod.Name == "OnOfferAcceptClick" && ShopActions.FriendAcceptsSpecialDeal()) return true;
            if (ReplicaSession.IsReplica && LocalControl.UserAction)
                RemoteActions.Forward(RemoteActions.Adapter("CounterAction", 2), null, new object[] {
                    Array.IndexOf(Actions, __originalMethod.Name), __originalMethod.Name == "OnBuyAllClick" ? ModReflect.Get<GameItem>(__instance, "selectedItem") : __originalMethod.Name == "OnOfferAcceptClick" ? ModReflect.Get<GameItem>(__instance, "currentNegociatedItem") : null });
            if (__originalMethod.Name == "OnConfirmPanelNo") return true;
            if (__originalMethod.Name == "OnConfirmPanelYes") {
                var frame = ModReflect.Get<NegotiationTabletFrame>(__instance, "frame");
                if (frame != null && frame.confirmRoot != null) frame.confirmRoot.SetActive(false);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(NegociationUIManager), "OpenConfirmPanel")]
    internal static class HostSaleConfirmation {
        static bool Prefix(NegociationUIManager __instance) {
            var s = Connection.Current;
            if (s == null || !s.IsHost || !s.Ready || WorldReplica.Applying || PanelRelay.Capturing) return true;
            int budget, total;
            if (CounterTrades.BeginConfirmation(0, __instance, out budget, out total)) return true;
            CounterTrades.CancelConfirmation(0);
            CounterTrades.HideConfirmation(__instance);
            RemoteActions.Notice("~SMC301~");
            return false;
        }
        static void Postfix(NegociationUIManager __instance) {
            var s = Connection.Current;
            if (s != null && s.IsHost && s.Ready && !PanelRelay.Capturing)
                CounterTrades.BindConfirmation(__instance, CounterTrades.ConfirmationToken(0));
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
            if (ShopActions.FriendAcceptsSpecialDeal()) RemoteActions.Forward(RemoteActions.Adapter("AcceptDeal",1),null,new object[]{ShopActions.SelectedDealItem()});
            else RemoteActions.Forward(RemoteActions.Adapter("CounterAction",2),null,new object[]{customer.clientIntent==StoreClient.ClientIntent.SELL?1:2,null});
        }
    }
}
