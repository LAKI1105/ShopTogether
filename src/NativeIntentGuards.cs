using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ShopTogether {
    // Native callbacks often assume the UI has already checked their arguments.
    // These checks run on the host for local clicks and relayed calls alike.
    [HarmonyPatch]
    internal static class NativeIntentGuards {
        internal static IEnumerable<MethodBase> TargetMethods() {
            foreach (var m in RemoteActions.Targets()) {
                var t = m.DeclaringType;
                if (t == typeof(MachineAutoButcher) || t == typeof(MachineCassettePlayer) || t == typeof(MachineDealMaker) ||
                    t == typeof(MachineFeedDispenser) || t == typeof(HopperHelper) || t == typeof(VendingMachineHelper) ||
                    t == typeof(StorageExpansionHelper) || t == typeof(GeneralHelper) || t == typeof(AppraisalServiceHelper) || t == typeof(BargainUIManager) ||
                    t == typeof(CommissaryData) || t == typeof(CommissaryUIManager) ||
                    t == typeof(NegociationUIManager)) yield return m;
            }
        }
        static bool Prefix(MethodBase __originalMethod, object __instance, object[] __args) {
            var s = Connection.Current;
            if (s == null || !s.IsHost || !s.Ready || WorldReplica.Applying) return true;
            // Automatic native ticks retain their native selection rules.
            if (RemoteActions.CurrentOwner < 0 && !LocalControl.UserAction && !NativeActions.Executing) return true;
            string reason = Check(__originalMethod, __instance, __args);
            if (reason == null) return true;
            RemoteActions.PendingNote = reason;
            if (RemoteActions.CurrentOwner < 0 && !NativeActions.Executing) RemoteActions.Notice(reason);
            return false;
        }
        internal static string Check(MethodBase method, object instance, object[] args) {
            var s = Connection.Current; var store = PlayerStore.Instance; var e = EmporiumEntry.Instance;
            int owner = Math.Max(0, RemoteActions.CurrentOwner);
            var type = method.DeclaringType; string name = method.Name;
            var item = args.Length > 0 ? args[0] as GameItem : null;
            if (store == null || e == null) return "~SMC081~";
            if (DialogueIntent.Committing) {
                foreach (var arg in args) if (arg is GameItem && !DialogueIntent.CanTouch(s, (GameItem)arg)) return "~SMC353~";
                if (type == typeof(BargainUIManager) || type == typeof(NegociationUIManager) || type == typeof(AppraisalServiceHelper)) return "~SMC353~";
            }
            if (type == typeof(CommissaryData) || type == typeof(CommissaryUIManager)) {
                if (!OutingKits.HostAt("commissary")) return "~SMC214~";
                if (name == "BuyItem") {
                    var data = instance as CommissaryData;
                    var product = data == null || args[0] == null ? null : data.GetItemData(args[0] as string);
                    if (product == null || product.StockCount <= 0) return "~SMC325~";
                    return data.redeemedCredit < product.price ? "~SMC324~" : null;
                }
                string tag = name == "OnRedeemAllLottery" ? "LOTTERY_IS_WINNING_TAG" : "FOOD_STAMP_TAG";
                var selected = e.GetAllOwnedItems().Where(x => x.IsTag(tag)).ToList();
                if (selected.Count == 0) return "~SMC348~";
                foreach (var selectedItem in selected) { if (!DialogueIntent.MayChange(s, selectedItem)) return "~SMC353~"; if (!AuthorityIntent.FreeTree(s, owner, selectedItem)) return "~SMC096~"; }
                return null;
            }
            if (type == typeof(NegociationUIManager)) {
                var customer = store.currentClientInstance == null ? null : store.currentClientInstance.GetClientBlueprint();
                if (customer == null || NegociationUIManager.IsTradeLocked()) return "~SMC247~";
                if (name == "OnInterogationButtonClick") return customer.interogationDialogue == null ? "~SMC247~" : null;
                if (name == "OnGlasseButtonClick") return customer.glasseDialogue == null ? "~SMC247~" : null;
                if (name == "OnLeftArrowButtonClick" || name == "OnRightArrowButtonClick")
                    return customer.barterOffer == null || !customer.barterOffer.UseOfferSets() || !customer.isIntroduced ? "~SMC247~" : null;
                if (!customer.isOfferWholesale) return "~SMC247~";
                int price = store.GetWholesalePrice();
                if (price < 0 || price > store.playerCash) return "~SMC343~";
                foreach (var goods in e.GetAllNonOwnedItem()) if (!AuthorityIntent.FreeTree(s, owner, goods)) return "~SMC096~";
                return null;
            }
            if (type == typeof(BargainUIManager)) {
                if (NegociationUIManager.IsTradeLocked()) return "~SMC247~";
                var can = AccessTools.Method(type, "Can" + name);
                if (can == null || !(bool)can.Invoke(instance, null)) return "~SMC349~";
                if (args.Length != 0) {
                    var values = AccessTools.Field(type, "PRICE_PERCENTS").GetValue(null) as int[];
                    if (!(args[0] is int) || values == null || !values.Contains((int)args[0])) return "~SMC349~";
                }
                foreach (var goods in e.GetBackInvItems()) if (!AuthorityIntent.FreeTree(s, owner, goods)) return "~SMC096~";
                return null;
            }
            if (item == null) return "~SMC095~";
            if (!AuthorityIntent.FreeTree(s, owner, item)) return "~SMC096~";
            if (type == typeof(AppraisalServiceHelper)) {
                var customer = store.currentClientInstance == null ? null : store.currentClientInstance.GetClientBlueprint();
                if (customer == null || customer.clientIntent != StoreClient.ClientIntent.APPRAISAL_SERVICE ||
                    !item.IsTag("APPRAISAL_ITEM") || GeneralHelper.IsItemOwned(item) || NegociationUIManager.IsTradeLocked()) return "~SMC247~";
                if (!(args[1] is int) || (int)args[1] < 0) return "~SMC305~";
                return null;
            }
            if (!GeneralHelper.IsItemOwned(item)) return "~SMC347~";
            if (type == typeof(GeneralHelper) && item.identifier == "dossier") return "~SMC347~";
            for (int i = 1; i < args.Length; i++) {
                if (!typeof(GameInventory).IsAssignableFrom(method.GetParameters()[i].ParameterType)) continue;
                var inventory = args[i] as GameInventory;
                if (inventory == null || inventory.GetParentItem() != item) return "~SMC347~";
            }
            string id = type == typeof(MachineAutoButcher) ? "auto_butcher" : type == typeof(MachineCassettePlayer) ? "cassette_player" :
                type == typeof(MachineDealMaker) ? "deal_maker" : type == typeof(MachineFeedDispenser) ? "livestock_feed_dispenser" :
                type == typeof(VendingMachineHelper) ? "vending_machine" : null;
            if (id != null && item.identifier != id) return "~SMC347~";
            if (type == typeof(HopperHelper) && !item.IsTag("HOPPER_INTAKE_TAG")) return "~SMC347~";
            if (type == typeof(StorageExpansionHelper)) {
                var tabs = AccessTools.Method(typeof(StorageExpansionHelper), "GetTabs").Invoke(null, new object[] { item }) as MachineryTabs;
                if (tabs == null || (int)args[1] < 0 || (int)args[1] >= tabs.count) return "~SMC305~";
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(GenericConfirmationUIManager), "OpenPanel")]
    internal static class HostConfirmationContent {
        static void Prefix(ref Action __3) {
            var session = Connection.Current; var item = PanelRelay.SourceItem;
            if (session != null && session.IsHost) PanelRelay.NotifiedDuringAction = true;
            if (session == null || !session.IsHost || !session.Ready || PanelRelay.Capturing || item == null || __3 == null) return;
            var parent = item.parentInventory; string stamp = AuthorityIntent.TreeStamp(session, item);
            var original = __3; bool used = false;
            __3 = () => {
                if (used || parent != item.parentInventory || stamp != AuthorityIntent.TreeStamp(session, item)) { RemoteActions.Notice("~SMC326~"); return; }
                used = true;
                if (!DialogueIntent.MayChange(session, item)) { RemoteActions.Notice("~SMC353~"); return; }
                if (!AuthorityIntent.FreeTree(session, 0, item)) { RemoteActions.Notice("~SMC096~"); return; }
                original();
            };
        }
    }

    [HarmonyPatch(typeof(BargainUIManager), "RefreshUI")]
    internal static class BargainIntentButtons {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            foreach (var code in instructions) {
                var method = code.operand as MethodInfo;
                if (method != null && method.Name == "SetInteractable" && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(bool)) {
                    var ask = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AuthorityIntent), "Ask"));
                    ask.labels.AddRange(code.labels); code.labels.Clear(); yield return ask;
                }
                yield return code;
            }
        }
    }
}
