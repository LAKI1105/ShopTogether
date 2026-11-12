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
            if (ui == null || item == null || kind < 1 || kind > 5 || GeneralHelper.IsItemOwned(item) || item.IsTag("ALREADY_CONFRONTED_TAG") || InspectionUIManager.IsConfrontLocked()) return;
            ui.OnInspectItem(item);
            ui.toggle1.isOn = (issues & 1) != 0; ui.toggle2.isOn = (issues & 2) != 0; ui.toggle3.isOn = (issues & 4) != 0;
            ui.toggle4.isOn = (issues & 8) != 0; ui.toggle5.isOn = (issues & 16) != 0;
            switch (kind) {
                case CigaretteConfront: ui.OnCigaretteConfront(); break;
                case CigaretteTalk: ui.OnCigaretteTalk(); break;
                case StampConfront: ui.OnStampConfront(); break;
                case StampTalk: ui.OnStampTalk(); break;
                case InjectorConfront: ui.OnInjectorConfront(); break;
            }
        }

        // ---------------- water strip / scanners: match the label, confront a chemical ----------------
        /// <summary>Host side: the "match" button of the water strip, water scanner or chemical scanner.</summary>
        internal static void MatchLabel(GameItem item) {
            if (item == null) return;
            if (item.IsTag("LIQUID_CONTAINER_TAG") && WaterScannerUIManager.CanMatch(item)) {
                var feature = WaterFeatureHelper.GetWaterFeature(item);
                if (feature == null) return;
                feature.ExposeFeature(lowkey: true);
                WaterFeatureHelper.OnWaterLabelUpdated(item);
            } else if (item.IsTag("CHEM_ITEM_TAG")) {
                var feature = ChemicalProductHelper.GetChemicalFeature(item);
                if (feature == null) return;
                feature.ExposeFeature(lowkey: true);
                ChemicalProductHelper.GradeProduct(item);
            }
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
            if (item == null || item.itemFeatures == null || index < 0 || index >= item.itemFeatures.Count) return;
            var feature = item.itemFeatures[index];
            if (feature != null && feature.category == category && !feature.IsFeatureDiscovered()) feature.DiscoverFeature();
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
            if (store == null || id == null || store.networkUpgrade == null || !store.networkUpgrade.TryGetValue(id, out upgrade) || upgrade == null || !upgrade.IsReady() || upgrade.IsUnlocked()) return;
            int cost = upgrade.GetCost();
            if (cost > store.playerCash + store.wildFavor) { Refuse("~SMC232~"); return; }
            int cashSpent = 0;
            if (store.wildFavor >= cost) store.wildFavor -= cost;
            else { int rest = cost - store.wildFavor; store.wildFavor = 0; store.playerCash -= rest; cashSpent = rest; }
            RunStats.OnWildUpgradeBought(cashSpent);
            upgrade.Unlock();
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
        internal static void AcceptDeal() {
            var ui = NegociationUIManager.Instance; var store = PlayerStore.Instance;
            var client = store != null && store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null;
            if (ui == null || client == null || PlainTrade(client.clientIntent)) return;
            ui.OnOfferAcceptClick();
        }

        // ---------------- inventory sort undo ----------------
        internal static readonly HashSet<GameInventory> sortedHere = new HashSet<GameInventory>();
        internal static void UndoSort(GameInventory inventory) { var grid = inventory as GameGridInventory; if (grid != null && InventorySortHelper.CanUndo(grid)) InventorySortHelper.Undo(grid); }

        // ---------------- appraisal items allowed, mind reprogrammer ----------------
        internal static void SetAppraisalAllowed(string typeId, bool allowed) {
            var store = PlayerStore.Instance; var type = typeId != null ? AppraisalItemRegistry.Get(typeId) : null;
            if (store == null || type == null) return;
            int rank = AppraisalRankHelper.GetRank();
            if (!AppraisalItemRegistry.CanBlock(type, rank) || (!allowed && AppraisalItemRegistry.IsLastAllowed(type, rank))) return;
            var list = store.appraisalBlacklist ?? (store.appraisalBlacklist = new List<string>());
            list.Remove(type.id);
            if (!allowed) list.Add(type.id);
        }
        internal static void ApplyPerk(GameItem item, string perkId) {
            if (item == null || string.IsNullOrEmpty(perkId) || !item.IsTag("MIND_REPROGRAMMER_TAG") || !GeneralHelper.IsItemOwned(item)) return;
            StartingPerk perk = null;
            foreach (var candidate in MindReprogrammerHelper.GetEditablePerks())
                if (candidate != null && candidate.id == perkId) { perk = candidate; break; }
            if (perk != null && MindReprogrammerHelper.CanApply(item, perk)) MindReprogrammerHelper.Apply(item, perk);
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
                if (Act && __instance.currentWaterContainer != null && WaterScannerUIManager.CanMatch(__instance.currentWaterContainer)) Send("MatchLabel", __instance.currentWaterContainer);
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
                if (!Friend) return true;
                if (!Act) return false;
                var manager = __instance as WildUIManager;
                string id = manager != null ? manager.currentSelectedId : (selected != null ? selected.GetValue(null) as string : null);
                if (!string.IsNullOrEmpty(id)) Send("BuyWild", id);
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
                if (Act) Send("AcceptDeal");
                return false;
            }
        }

        [HarmonyPatch(typeof(InventorySortHelper), "Sort")]
        internal static class RememberSort { static void Postfix(GameGridInventory __0) { if (ReplicaSession.ClientStore && __0 != null) sortedHere.Add(__0); } }
        [HarmonyPatch(typeof(InventorySortHelper), "CanUndo")]
        internal static class FriendCanUndo {
            static bool Prefix(GameGridInventory inventory, ref bool __result) { if (!ReplicaSession.ClientStore) return true; __result = inventory != null && sortedHere.Contains(inventory); return false; }
        }
        [HarmonyPatch(typeof(InventorySortHelper), "Undo")]
        internal static class FriendUndo {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(GameGridInventory inventory, ref bool __result) {
                if (!Friend) return true;
                __result = false;
                if (Act && inventory != null && sortedHere.Remove(inventory)) { Send("UndoSort", inventory); __result = true; }
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
