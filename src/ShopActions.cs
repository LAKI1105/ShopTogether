using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// Player actions found in the full audit that need more than a plain forward: actions whose arguments are game
    /// objects, actions that pay inside the UI before calling the forwarded part, and checks a friend's screen cannot
    /// make on its own. Each runs on the host, which re-checks what the game's own button would have checked.
    /// </summary>
    internal static class ShopActions {
        static bool Friend { get { return ReplicaSession.ClientStore && !WorldReplica.Applying; } }
        static bool Act { get { return ReplicaSession.IsReplica && LocalControl.Focused; } }
        static void Send(string name, params object[] args) { RemoteActions.Forward(RemoteActions.Adapter(name, args.Length), null, args); }

        // ---------------- inspection panel: cigarettes, stamps, injectors ----------------
        internal const int CigaretteConfront = 1, CigaretteTalk = 2, StampConfront = 3, StampTalk = 4, InjectorConfront = 5;

        internal static int Issues(InspectionUIManager ui) {
            return (ui.toggle1.isOn ? 1 : 0) | (ui.toggle2.isOn ? 2 : 0) | (ui.toggle3.isOn ? 4 : 0) | (ui.toggle4.isOn ? 8 : 0) | (ui.toggle5.isOn ? 16 : 0);
        }
        /// <summary>Host side: same as ticking the issues on the inspection panel and pressing that button.</summary>
        internal static void Inspect(GameItem item, int kind, int issues) {
            var ui = InspectionUIManager.Instance;
            if (item == null) { Refuse("~SMC095~"); return; }
            if (kind < 1 || kind > 5 || issues < 0 || issues > 31) { Refuse("~SMC305~"); return; }
            if (ui == null || PlayerStore.Instance == null || PlayerStore.Instance.currentClientInstance == null || GeneralHelper.IsItemOwned(item) || item.IsTag("ALREADY_CONFRONTED_TAG") || InspectionUIManager.IsConfrontLocked()) { Refuse("~SMC247~"); return; }
            string tag = kind <= 2 ? "CIGARETTE_TAG" : kind <= 4 ? "FOOD_STAMP_TAG" : "INS_INJECTOR_TAG";
            if (!item.IsTag(tag) || issues == 0) { Refuse("~SMC346~"); return; }
            string category = kind <= 2 ? "CATEGORY_GENUINE_CIGARETTE" : kind <= 4 ? "CATEGORY_GENUINE_STAMP" : "CATEGORY_GENUINE_INJECTOR";
            var feature = item.FindItemFeatureByCategory(category);
            if (feature == null) { Refuse("~SMC346~"); return; }
            // The host chooses confront/talk using its native current condition.

            ui.OnInspectItem(item);
            ui.toggle1.isOn = (issues & 1) != 0; ui.toggle2.isOn = (issues & 2) != 0; ui.toggle3.isOn = (issues & 4) != 0;
            ui.toggle4.isOn = (issues & 8) != 0; ui.toggle5.isOn = (issues & 16) != 0;
            if (kind == InjectorConfront && feature.GetCurrentItemCondition().identifier != "genuineInjector") { Refuse("~SMC346~"); return; }
            ui.OnConfront();
        }

        // ---------------- water strip / scanners: match the label, confront a chemical ----------------
        /// <summary>Host side: the "match" button of the water strip, water scanner or chemical scanner.</summary>
        internal static void MatchLabel(GameItem item) {
            if (item == null) { Refuse("~SMC095~"); return; }
            if (item.IsTag("LIQUID_CONTAINER_TAG") && WaterScannerUIManager.CanMatch(item)) {
                var feature = WaterFeatureHelper.GetWaterFeature(item);
                if (feature == null) { Refuse("~SMC247~"); return; }
                feature.ExposeFeature(lowkey: true);
                WaterFeatureHelper.OnWaterLabelUpdated(item);
            } else if (item.IsTag("CHEM_ITEM_TAG") && ChemScannerUIManager.Instance != null && ChemScannerUIManager.Instance.CanMatchChem(item)) {
                var feature = ChemicalProductHelper.GetChemicalFeature(item);
                if (feature == null) { Refuse("~SMC247~"); return; }
                feature.ExposeFeature(lowkey: true);
                ChemicalProductHelper.GradeProduct(item);
            } else Refuse("~SMC247~");
        }
        /// <summary>Host side: the "confront" button of the chemical scanner.</summary>
        internal static void ConfrontChem(GameItem item) {
            if (item == null || DialogUIManager.Instance == null || InspectionUIManager.IsConfrontLocked() || !ChemScannerUIManager.CanConfrontChem(item)) { RemoteActions.PendingNote = "~SMC247~"; return; }
            var feature = ChemicalProductHelper.GetChemicalFeature(item);
            if (feature != null && DialogUIManager.Instance.StartDialogue(feature.GetExposeDialog())) InspectableHelper.OnConfrontSuccess(item); else RemoteActions.PendingNote = "~SMC247~";
        }

        // ---------------- features found by looking (scale, water tests, ingots…) ----------------
        /// <summary>Host side: a friend found out something about an item.</summary>
        internal static void Discover(GameItem item, int index, string category) {
            if (item == null || item.itemFeatures == null || index < 0 || index >= item.itemFeatures.Count) { Refuse("~SMC095~"); return; }
            var feature = item.itemFeatures[index];
            if (feature == null || feature.category != category) { Refuse("~SMC305~"); return; }
            if (!feature.IsFeatureDiscovered()) feature.DiscoverFeature();
        }
        static readonly Dictionary<ItemFeature, float> sentDiscoveries = new Dictionary<ItemFeature, float>();
        internal static void Discovered(ItemFeature feature) {
            var session = Connection.Current;
            if (feature == null || session == null || !ReplicaSession.IsReplica || WorldReplica.Applying) return;
            float last; if (sentDiscoveries.TryGetValue(feature, out last) && Time.realtimeSinceStartup - last < 3f) return;
            foreach (var item in session.World.Items.Values) {
                if (item == null || item.itemFeatures == null) continue;
                int index = item.itemFeatures.IndexOf(feature);
                if (index < 0) continue;
                if (sentDiscoveries.Count > 512) sentDiscoveries.Clear();
                sentDiscoveries[feature] = Time.realtimeSinceStartup;
                Send("Discover", item, index, feature.category ?? "");
                return;
            }
        }

        // ---------------- the Wild network: the price is paid on the host ----------------
        /// <summary>Host side: buy a Wild network upgrade, paid with favour first and cash for the rest.</summary>
        internal static void BuyWild(string id) {
            var store = PlayerStore.Instance; NetworkUpgrade upgrade;
            if (store == null || id == null || store.networkUpgrade == null || !store.networkUpgrade.TryGetValue(id, out upgrade) || upgrade == null || !upgrade.IsReady() || upgrade.IsUnlocked()) { Refuse("~SMC247~"); return; }
            int cost = upgrade.GetCost();
            if (cost < 0 || (long)cost > (long)store.playerCash + store.wildFavor) { Refuse("~SMC232~"); return; }
            if(cost>store.wildFavor&&!DialoguePayment.AllowSpending())return;
            int cashSpent = 0;
            if (store.wildFavor >= cost) store.wildFavor -= cost;
            else { int rest = cost - store.wildFavor; store.wildFavor = 0; store.playerCash -= rest; cashSpent = rest; }
            RunStats.OnWildUpgradeBought(cashSpent);
            using (HostNativeScope.Begin()) upgrade.Unlock();
            EmporiumAnalytics.TrackWildUpgradePurchased(id);
            if (EmporiumAudioPlayer.Instance != null) EmporiumAudioPlayer.Instance.PlayRegister();
        }
        static void Refuse(string text) { RemoteActions.PendingNote = text; }

        // ---------------- counter deals that are not a plain buy or sell ----------------
        static bool PlainTrade(StoreClient.ClientIntent intent) { return intent == StoreClient.ClientIntent.BUY || intent == StoreClient.ClientIntent.SELL || intent == StoreClient.ClientIntent.SELLNBUY; }
        internal static bool FriendAcceptsSpecialDeal() {
            var store = PlayerStore.Instance;
            var client = store != null && store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null;
            return client != null && !PlainTrade(client.clientIntent);
        }
        /// <summary>Host side: the accept button for barter, information, gunsmith and other special deals.</summary>
        internal static GameItem SelectedDealItem() {
            var store = PlayerStore.Instance;
            var c = store != null && store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null;
            return c != null && c.clientIntent == StoreClient.ClientIntent.GUNSMITH ? store.currentNegociatedItem : null;
        }
        internal static void AcceptDeal(GameItem selected) {
            var ui = NegociationUIManager.Instance; var store = PlayerStore.Instance;
            var client = store != null && store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null;
            if (ui == null || client == null || PlainTrade(client.clientIntent)) { Refuse("~SMC247~"); return; }
            CounterTrades.SpecialAction(selected);
        }

        // ---------------- inventory sort undo ----------------
        internal static readonly HashSet<GameInventory> sortedHere = new HashSet<GameInventory>();
        internal static void UndoSort(GameInventory inventory) { var grid = inventory as GameGridInventory; var session = Connection.Current; if (grid == null || session == null || !InventoryActions.Available(session, Math.Max(0, RemoteActions.CurrentOwner), grid) || !InventorySortHelper.CanUndo(grid)) { Refuse("~SMC328~"); return; } InventorySortHelper.Undo(grid); }

        // ---------------- appraisal items allowed, mind reprogrammer ----------------
        internal static void SetAppraisalAllowed(string typeId, bool allowed) {
            var store = PlayerStore.Instance; var type = typeId != null ? AppraisalItemRegistry.Get(typeId) : null;
            if (store == null || type == null) { Refuse("~SMC247~"); return; }
            int rank = AppraisalRankHelper.GetRank();
            if (!AppraisalItemRegistry.CanBlock(type, rank) || (!allowed && AppraisalItemRegistry.IsLastAllowed(type, rank))) { Refuse("~SMC125~"); return; }
            var list = store.appraisalBlacklist ?? (store.appraisalBlacklist = new List<string>());
            list.Remove(type.id);
            if (!allowed) list.Add(type.id);
        }
        internal static void ApplyPerk(GameItem item, string perkId) {
            if (item == null || string.IsNullOrEmpty(perkId) || !item.IsTag("MIND_REPROGRAMMER_TAG") || !GeneralHelper.IsItemOwned(item)) { Refuse("~SMC317~"); return; }
            StartingPerk perk = null;
            foreach (var candidate in MindReprogrammerHelper.GetEditablePerks())
                if (candidate != null && candidate.id == perkId) { perk = candidate; break; }
            if (perk != null && MindReprogrammerHelper.CanApply(item, perk)) MindReprogrammerHelper.Apply(item, perk); else Refuse("~SMC125~");
        }

        // ---------------- counter scale, exposing a feature, browsing upgrades ----------------
        /// <summary>Host side: the counter scale. It weighs the one item on the counter, as in the base game.</summary>
        internal static void Weigh() {
            var e = EmporiumEntry.Instance;
            if (e == null || e.backInvinvElement == null || e.backInvinvElement.childItems.Count != 1) { RemoteActions.PendingNote = "~SMC304~"; return; }
            var feature = e.backInvinvElement.childItems[0].FindItemFeatureByCategory("weight");
            if (feature == null) { RemoteActions.PendingNote = "~SMC125~"; return; }
            if (!feature.IsFeatureDiscovered()) feature.DiscoverFeature();
        }
        /// <summary>Host side: a friend used a negotiation tool or a notebook entry on a feature of an item ("public" is
        /// the notebook's own button for a publicly hidden feature). The host makes the game's match: the tool must be
        /// the one the feature needs, a notebook entry must be this same feature.</summary>
        internal static void Expose(GameItem item, int index, string tool, int noteIndex) {
            var feature = item != null && item.itemFeatures != null && index >= 0 && index < item.itemFeatures.Count ? item.itemFeatures[index] : null;
            if (feature == null || tool == null || !feature.CanExposeFeature() || feature.IsExposed() || InspectionUIManager.IsConfrontLocked()) { RemoteActions.PendingNote = "~SMC247~"; return; }
            bool direct = tool == "public";
            bool match = direct ? feature.GetPublicHidden() : tool == "notebook" ? noteIndex == index : tool == feature.GetToolNeeded();
            if (!match) { RemoteActions.PendingNote = "~SMC307~"; return; }
            var dialog = direct ? null : feature.GetExposeDialog();
            if (dialog == null) { feature.ExposeFeature(); return; }
            if (DialogUIManager.Instance == null || !DialogUIManager.Instance.StartDialogue(dialog)) RemoteActions.PendingNote = "~SMC247~";
        }
        /// <summary>Host side: a friend pressed the shutter to close. The host checks the day as the game's button does;
        /// with customers still waiting it asks the friend first (the game's own question), then closes.</summary>
        internal static void ShutterIntent() {
            var store = PlayerStore.Instance;
            if (store == null) { AuthorityIntent.Refuse("~SMC126~"); return; }
            if (store.storeState == PlayerStore.StoreState.MORNING) {
                string reason = DayGuards.Open();
                if (reason != null) { AuthorityIntent.Refuse(reason); return; }
                StoreShutterButton.Instance.OpenShutter();
            } else if (store.storeState == PlayerStore.StoreState.OPEN) CloseShop();
            else AuthorityIntent.Refuse("~SMC126~");
        }
        internal static void CloseShop() {
            string refused = DayGuards.Close();
            if (refused != null) { RemoteActions.PendingNote = refused; return; }
            var store = PlayerStore.Instance; var g = GenericConfirmationUIManager.Instance;
            if (store.storeClientManager != null && store.storeClientManager.clientStack.Count > 0 && g != null) {
                g.OpenPanel("~SMC309~", "~SMC310~", "~SMC311~", delegate {
                    string later = DayGuards.Close();
                    if (later != null) { RemoteActions.PendingNote = later; return; }
                    StoreShutterButton.Instance.EndAndClose();
                });
                return;
            }
            StoreShutterButton.Instance.EndAndClose();
        }
        /// <summary>Host side: a friend looked at the upgrade network, which resets the game's upgrade reminder.</summary>
        internal static void BrowseUpgrades() { if (PlayerStore.Instance != null) PlayerStore.Instance.daySinceLastBrowseUpgrade = 0; }

        static bool FeatureOwner(ItemFeature feature, out GameItem owner, out int index) {
            owner = null; index = -1; var session = Connection.Current;
            if (feature == null || session == null) return false;
            foreach (var item in session.World.Items.Values) {
                if (item == null || item.itemFeatures == null) continue;
                int i = item.itemFeatures.IndexOf(feature);
                if (i >= 0) { owner = item; index = i; return true; }
            }
            return false;
        }

        /// <summary>Friend: clicking a feature with a negotiation tool or a notebook entry selected sends what was used.</summary>
        [HarmonyPatch(typeof(ItemFeatureUI), "OnFeatureUIClick")]
        internal static class FriendExposeTool {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(ItemFeatureUI __instance) {
                if (!Friend) return true;
                GameItem item; int index;
                if (!Act || __instance.itemFeature == null || RTSController.previousSelectedObjects.Count == 0 || RTSController.previousSelectedObjects[0] == null) return false;
                if (!FeatureOwner(__instance.itemFeature, out item, out index)) return false;
                var picked = RTSController.previousSelectedObjects[0];
                NegociationElement tool; NotebookElement note;
                if (picked.TryGetComponent<NegociationElement>(out tool)) { if (tool.IsNegociationTool()) Send("Expose", item, index, tool.GetNegociationToolID() ?? "", -1); }
                else if (picked.TryGetComponent<NotebookElement>(out note)) {
                    GameItem noteItem; int noteIndex;
                    Send("Expose", item, index, "notebook", FeatureOwner(note.itemFeature, out noteItem, out noteIndex) && noteItem == item ? noteIndex : -1);
                }
                return false;
            }
        }
        [HarmonyPatch(typeof(ItemFeature), "ExposeFeature")]
        internal static class FriendExposeFeature {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(ItemFeature __instance) {
                if (!Friend) return true;
                GameItem item; int index;
                if (Act && LocalControl.UserAction && FeatureOwner(__instance, out item, out index)) Send("Expose", item, index, "public", -1);
                return false;
            }
        }
        [HarmonyPatch(typeof(StoreScale), "OnClick")]
        internal static class FriendScale {
            [HarmonyPriority(Priority.First)]
            static bool Prefix() { if (!Friend) return true; if (Act && LocalControl.UserAction) Send("Weigh"); return false; }
        }
        [HarmonyPatch]
        internal static class FriendBrowseUpgrades {
            static IEnumerable<MethodBase> TargetMethods() {
                var a = AccessTools.Method(typeof(WildNetworkUI), "Open"); if (a != null) yield return a;
                var b = AccessTools.Method(typeof(WildUIManager), "OpenUI"); if (b != null) yield return b;
            }
            static void Postfix() { if (Friend && Act) Send("BrowseUpgrades"); }
        }

        // ---------------- counter buttons: the host checks what the base game's button checks ----------------
        [HarmonyPatch(typeof(StoreShutterButton), "OnClick")]
        internal static class FriendShutter {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(StoreShutterButton __instance) {
                if (!Friend) {
                    var session = Connection.Current;
                    if (session == null || !session.IsHost || !session.Ready || !LocalControl.UserAction || NativeActions.Executing || RemoteActions.CurrentOwner > 0) return true;
                    RemoteActions.PendingNote = null; ShutterIntent();
                    string reason = RemoteActions.TakeNote(); if (reason != null) RemoteActions.Notice(reason);
                    return false;
                }
                if (LocalControl.UserAction) Send("ShutterIntent");
                return false;
            }
        }
        [HarmonyPatch(typeof(StoreDismissButton), "OnClick")]
        internal static class FriendDismiss {
            [HarmonyPriority(Priority.First)]
            static bool Prefix() {
                if (!Friend) {
                    var session = Connection.Current;
                    if (session != null && session.IsHost && session.Ready && LocalControl.UserAction && RemoteActions.CurrentOwner <= 0) { string reason = RemoteActions.DismissGuard(); if (reason != null) { RemoteActions.Notice(reason); return false; } }
                    return true;
                }
                if (!LocalControl.UserAction || PlayerStore.Instance == null) return false;
                PlayerStore.Instance.TryPlayerDismissCurrentClient(false);
                return false;
            }
        }
        [HarmonyPatch(typeof(StoreSecButton), "OnClick")]
        internal static class FriendAlarm {
            [HarmonyPriority(Priority.First)]
            static bool Prefix() {
                if (!Friend) {
                    var session = Connection.Current;
                    if (session != null && session.IsHost && session.Ready && LocalControl.UserAction && RemoteActions.CurrentOwner <= 0) { string reason = RemoteActions.AlarmGuard(); if (reason != null) { RemoteActions.Notice(reason); return false; } }
                    return true;
                }
                if (!LocalControl.UserAction || PlayerStore.Instance == null) return false;
                PlayerStore.Instance.OnCustomerReported();
                return false;
            }
        }

        internal static void Reset() { sortedHere.Clear(); sentDiscoveries.Clear(); }

        // =============== friend-side interception ===============

        [HarmonyPatch]
        internal static class InspectButtons {
            static IEnumerable<MethodBase> TargetMethods() {
                foreach (var name in new[] { "OnCigaretteConfront", "OnCigaretteTalk", "OnStampConfront", "OnStampTalk", "OnInjectorConfront" })
                    yield return typeof(InspectionUIManager).GetMethod(name);
            }
            [HarmonyPriority(Priority.First)]
            static bool Prefix(MethodBase __originalMethod, InspectionUIManager __instance, GameItem ___currentInspectingItem) {
                if (!Friend) return true;
                if (!Act || ___currentInspectingItem == null) return false;
                int kind = __originalMethod.Name == "OnCigaretteConfront" ? CigaretteConfront : __originalMethod.Name == "OnCigaretteTalk" ? CigaretteTalk : __originalMethod.Name == "OnStampConfront" ? StampConfront : __originalMethod.Name == "OnStampTalk" ? StampTalk : InjectorConfront;
                Send("Inspect", ___currentInspectingItem, kind, Issues(__instance));
                __instance.CloseUI();
                return false;
            }
        }

        [HarmonyPatch(typeof(WaterStripUIManager), "OnMatch")]
        internal static class StripMatch {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(WaterStripUIManager __instance) {
                if (!Friend) return true;
                if (Act && __instance.currentWaterContainer != null) Send("MatchLabel", __instance.currentWaterContainer);
                return false;
            }
        }
        [HarmonyPatch(typeof(WaterScannerUIManager), "MatchWaterLabel")]
        internal static class ScannerWaterMatch {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(GameItem waterContainer, UnityEngine.UI.Button matchButton) {
                if (!Friend) return true;
                if (Act && waterContainer != null) { Send("MatchLabel", waterContainer); if (matchButton != null) matchButton.gameObject.SetActive(false); }
                return false;
            }
        }
        [HarmonyPatch(typeof(ChemScannerUIManager), "MatchChemLabel")]
        internal static class ScannerChemMatch {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(GameItem chemContainer, UnityEngine.UI.Button matchButton) {
                if (!Friend) return true;
                if (Act && chemContainer != null) { Send("MatchLabel", chemContainer); if (matchButton != null) matchButton.gameObject.SetActive(false); }
                return false;
            }
        }
        [HarmonyPatch(typeof(ChemScannerUIManager), "ConFrontChem")]
        internal static class ScannerChemConfront {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(GameItem associatedItem, UnityEngine.UI.Button confrontButton) {
                if (!Friend) return true;
                if (Act && associatedItem != null) { Send("ConfrontChem", associatedItem); if (confrontButton != null) confrontButton.gameObject.SetActive(false); }
                return false;
            }
        }

        [HarmonyPatch]
        internal static class FeatureDiscovered {
            static IEnumerable<MethodBase> TargetMethods() { yield return typeof(ItemFeature).GetMethod("DiscoverFeature"); yield return typeof(ItemFeature).GetMethod("SetFeatureDiscovered"); }
            static void Postfix(ItemFeature __instance) { if (Friend && __instance.IsFeatureDiscovered()) Discovered(__instance); }
        }

        [HarmonyPatch]
        internal static class WildPurchase {
            static readonly FieldInfo selected = typeof(WildNetworkUI).GetField("selectedId", BindingFlags.NonPublic | BindingFlags.Static);
            static IEnumerable<MethodBase> TargetMethods() {
                var a = typeof(WildNetworkUI).GetMethod("OnUseClicked", BindingFlags.NonPublic | BindingFlags.Static); if (a != null) yield return a;
                yield return typeof(WildUIManager).GetMethod("OnUnlockClicked");
            }
            [HarmonyPriority(Priority.First)]
            static bool Prefix(object __instance) {
                var manager = __instance as WildUIManager;
                string id = manager != null ? manager.currentSelectedId : (selected != null ? selected.GetValue(null) as string : null);
                if (!Friend) {
                    var s = Connection.Current;
                    if (s == null || !s.IsHost || !s.Ready || WorldReplica.Applying) return true;
                    RemoteActions.PendingNote = null;
                    BuyWild(id);
                    if (RemoteActions.PendingNote != null) RemoteActions.Notice(RemoteActions.TakeNote());
                    if (manager != null && id != null) manager.OnElementClicked(id);
                    else AccessTools.Method(typeof(WildNetworkUI), "RefreshAll").Invoke(null, null);
                    return false;
                }
                if (Act && !string.IsNullOrEmpty(id)) Send("BuyWild", id);
                return false;
            }
        }

        /// <summary>
        /// Closing the Wild after the reminder customer sent you there sends that customer away; on a friend's screen
        /// the game's own dismissal is local (and blocked), so the host is asked to do it.
        /// </summary>
        [HarmonyPatch]
        internal static class WildReminderLeaves {
            static IEnumerable<MethodBase> TargetMethods() {
                yield return AccessTools.Method(typeof(WildUIManager), "CloseUI");
                yield return AccessTools.Method(typeof(WildNetworkUI), "OnWindowClosed");
            }
            static void Postfix() {
                if (!Friend || !Act || !LocalControl.UserAction) return;
                var store = PlayerStore.Instance; var current = store != null ? store.currentClientInstance : null;
                var blueprint = current != null ? current.GetClientBlueprint() : null;
                if (blueprint != null && blueprint.identifier == "wild_upgrade_reminder") store.TryPlayerDismissCurrentClient(false);
            }
        }

        [HarmonyPatch(typeof(NegociationUIManager), "OnOfferAcceptClick")]
        internal static class SpecialDeal {
            [HarmonyPriority(Priority.First)]
            static bool Prefix() {
                if (!Friend || !FriendAcceptsSpecialDeal()) return true;   // a plain buy or sell goes through the shop actions
                if (Act) Send("AcceptDeal", SelectedDealItem());
                return false;
            }
        }

        internal static void RememberSmartSort(GameGridInventory inventory){if(inventory!=null)sortedHere.Add(inventory);}
        [HarmonyPatch(typeof(InventorySortHelper), "Sort")]
        internal static class RememberSort { static void Postfix(GameGridInventory __0) { if (ReplicaSession.ClientStore && __0 != null) sortedHere.Add(__0); } }
        [HarmonyPatch(typeof(InventorySortHelper), "CanUndo")]
        internal static class FriendCanUndo {
            [HarmonyPriority(Priority.First + 200)]
            static bool Prefix(GameGridInventory inventory, ref bool __result) { if (!ReplicaSession.ClientStore) return true; __result = inventory != null && sortedHere.Contains(inventory); return false; }
        }
        [HarmonyPatch(typeof(InventorySortHelper), "Undo")]
        internal static class FriendUndo {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(GameGridInventory inventory, ref bool __result) {
                if (!Friend) return true;
                __result = false;
                if (Act && inventory != null && sortedHere.Remove(inventory)) { Send("UndoPage", inventory,StoragePagesIntent.Page(inventory)); __result = true; }
                return false;
            }
        }

        [HarmonyPatch]
        internal static class AppraisalAllowed {
            static MethodBase TargetMethod() { return typeof(AppraisalItemsWindow).GetMethod("SetAllowed", BindingFlags.NonPublic | BindingFlags.Static); }
            static bool Prepare() { return TargetMethod() != null; }
            [HarmonyPriority(Priority.First)]
            static bool Prefix(AppraisalItemType type, bool allowed) { if (!Friend) return true; if (Act && type != null) Send("SetAppraisalAllowed", type.id, allowed); return false; }
        }

        [HarmonyPatch(typeof(MindReprogrammerHelper), "Apply")]
        internal static class Reprogram {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(GameItem item, StartingPerk definition) { if (!Friend) return true; if (Act && item != null && definition != null) Send("ApplyPerk", item, definition.id); return false; }
        }

        /// <summary>Conversations are the host's: a friend's screen only mirrors them, it never starts one.</summary>
        [HarmonyPatch(typeof(DialogUIManager), "StartDialogue")]
        internal static class NoLocalDialogue {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(ref bool __result) { if (!Friend) return true; __result = false; return false; }
        }
        [HarmonyPatch(typeof(DialoguePhoneUIManager), "StartDialogue")]
        internal static class NoLocalPhoneDialogue {
            [HarmonyPriority(Priority.First)]
            static bool Prefix() { return !Friend; }
        }
    }
}
