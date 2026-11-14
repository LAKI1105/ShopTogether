using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
namespace ShopTogether {
    internal static class ScannerIntent {
        internal static void RecordScan(GameItem item) {
            if (item == null || !item.IsTag("LIQUID_CONTAINER_TAG")) { AuthorityIntent.Refuse("~SMC095~"); return; }
            WaterHelper.SetLastScanResult(item);
        }
        static GameItem last; static int purity; static float next;
        static float nextReadout;
        internal static void RefreshOpen(WorldReplica world) {
            if(Time.realtimeSinceStartup<nextReadout)return;nextReadout=Time.realtimeSinceStartup+.5f;
            var water=WaterScannerUIManager.Instance;
            if(water!=null && water.panel.activeSelf && water.currentWaterContainer!=null) {
                if(world.Id(water.currentWaterContainer)<=0 || (water.currentWaterContainer is GameItemElement && ((GameItemElement)water.currentWaterContainer).IsDestroyed()))water.Close();
                else water.RefreshUI();
            }
            var chemical=ChemScannerUIManager.Instance;
            if(chemical!=null && chemical.panel.activeSelf && chemical.currentScannedItem!=null) {
                if(world.Id(chemical.currentScannedItem)<=0 || (chemical.currentScannedItem is GameItemElement && ((GameItemElement)chemical.currentScannedItem).IsDestroyed()))chemical.Close();
                else chemical.RefreshUI();
            }
            var plant=PlantAnalyserUIManager.Instance;
            if(plant!=null && plant.UIPanel.activeSelf && plant.currentItem!=null) {
                if(world.Id(plant.currentItem)<=0 || (plant.currentItem is GameItemElement && ((GameItemElement)plant.currentItem).IsDestroyed())){plant.HideUI();plant.Reset();}
                else { plant.Refresh();if(!plant.currentItem.IsTag("GROWING_TAG"))plant.body2TMP.text=""; }
            }
        }
        internal static void Observe(WaterScannerUIManager ui) {
            if (!ReplicaSession.IsReplica || !ui.panel.activeSelf || ui.currentWaterContainer == null) return;
            var item = ui.currentWaterContainer; int current = WaterHelper.GetWaterPurity(item);
            if (last == item && current == purity && Time.realtimeSinceStartup < next) return;
            last = item; purity = current; next = Time.realtimeSinceStartup + 5f;
            RemoteActions.Forward(RemoteActions.Adapter("RecordScan",1),null,new object[] { item });
        }
        internal static GameItem Item(object ui) {
            var chemical = ui as ChemScannerUIManager; if (chemical != null) return chemical.currentScannedItem;
            var water = ui as WaterScannerUIManager; if (water != null) return water.currentWaterContainer;
            var strip = ui as WaterStripUIManager; return strip != null ? strip.currentWaterContainer : null;
        }
    }
    [HarmonyPatch]
    internal static class ScannerButtonIntent {
        static IEnumerable<MethodBase> TargetMethods() {
            foreach (var type in new[] { typeof(WaterScannerUIManager), typeof(ChemScannerUIManager), typeof(WaterStripUIManager) })
                foreach (string name in new[] { "OnMatch", "OnConfront" }) yield return AccessTools.Method(type,name);
        }
        [HarmonyPriority(Priority.First)]
        static bool Prefix(object __instance, MethodBase __originalMethod) {
            if (!ReplicaSession.IsReplica) return true;
            if (!LocalControl.Focused) return false;
            var item = ScannerIntent.Item(__instance);
            string action = __originalMethod.Name == "OnMatch" ? "MatchLabel" : item != null && item.IsTag("CHEM_ITEM_TAG") ? "ConfrontChem" : "ConfrontWater";
            RemoteActions.Forward(RemoteActions.Adapter(action,1), null, new object[] { item });
            return false;
        }
    }
    [HarmonyPatch]
    internal static class ScannerHintControls {
        static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(WaterScannerUIManager), "RefreshUI");
            yield return AccessTools.Method(typeof(ChemScannerUIManager), "RefreshUI");
            yield return AccessTools.Method(typeof(WaterStripUIManager), "Open");
        }
        static void Postfix(object __instance) {
            if (!ReplicaSession.IsReplica || ScannerIntent.Item(__instance) == null) return;
            foreach (string field in new[] { "confrontButton", "matchButton" }) {
                var button = AccessTools.Field(__instance.GetType(),field).GetValue(__instance) as Button;
                if (button != null) { button.gameObject.SetActive(true); button.interactable = true; }
            }
            var water = __instance as WaterScannerUIManager; if (water != null) ScannerIntent.Observe(water);
        }
    }
}
