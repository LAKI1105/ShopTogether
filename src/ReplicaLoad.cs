using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// Friend side. The game builds an item's layout (upgrade buttons, cage and vending sizes, toolbox slots, storage-bay
    /// tabs, locks, machine sprites) from its saved state when a save is loaded: PlayerStore runs a list of load hooks and
    /// then the item's own onLoaded. A friend's copies come from the host's state, so the same hooks run here: once when a
    /// copy is created, and again (the ones that only rebuild the layout) whenever the host's state of an item changes,
    /// for example after the host upgrades a machine or a box. The host's data (state, shape, values, types, features,
    /// sprite) stays exactly as sent; only the layout follows it.
    /// </summary>
    internal static class ReplicaLoad {
        static readonly MethodInfo getTabs = AccessTools.Method(typeof(StorageExpansionHelper), "GetTabs");
        static readonly MethodInfo addTabs = AccessTools.Method(typeof(StorageExpansionHelper), "AddTabs");
        static readonly MethodInfo getKitTabs = AccessTools.Method(typeof(StorageExpansionHelper), "GetKitTabs");

        // onLoaded parts that are not a layout refresh: running them again on every change would stack handlers,
        // restart music or transform food a second time. They run once, when the copy is created.
        static readonly HashSet<string> onceOnly = new HashSet<string> { "FoodItemDirectory", "MachineCassettePlayer", "AquacultureHelper", "AquacultureItems", "HusbandryHelper" };

        /// <summary>What an item looked like before this update, for the hooks that only run on a change.</summary>
        internal struct Before { internal bool broken, workingLocked; internal int stage; }

        internal static Before Read(GameItem item) {
            var b = new Before();
            try { b.broken = item.IsTag("BROKEN_MACHINE_TAG"); b.workingLocked = item.IsTag("LOCKED_WORKING_TAG"); b.stage = item.IsTag("STANDARD_MACHINE_TAG") ? item.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt() : 0; } catch { }
            return b;
        }

        /// <summary>Storage-bay expansions add tab inventories. They must exist before the update's items are placed in them.</summary>
        internal static void Tabs(GameItem bay) {
            if (bay == null || getTabs == null || addTabs == null || getKitTabs == null) return;
            if (!bay.IsTag("STORAGE_EXPANSION_TABS_INT")) return;
            int want = Math.Min(StorageExpansionHelper.GetExtraTabs(bay), 4);
            if (want <= 0 || (int)getKitTabs.Invoke(null, new object[] { bay }) == 0) return;
            var tabs = getTabs.Invoke(null, new object[] { bay }) as MachineryTabs;
            if (tabs == null && !(bay.contentWindow != null && bay.contentWindow.childElement is GameGridInventory)) return;
            int have = tabs == null ? 0 : tabs.count - 1;
            if (want > have) addTabs.Invoke(null, new object[] { bay, want - have });
            else StorageExpansionHelper.InitTabNames(bay);   // tab names live in the state too
        }

        /// <summary>Runs after the update's items are placed, so hooks that look at an item's contents see them.</summary>
        internal static void Run(GameItem item, bool created, Before before) {
            if (item == null) return;
            // Keep the host's data; the hooks may only change the layout.
            var state = item.state != null ? item.state.Clone() : null;
            var shape = item.shape != null ? item.shape.Clone() : null;
            var modifiedShape = item.modifiedShape != null ? item.modifiedShape.Clone() : null;
            var types = item.itemTypes != null ? new List<string>(item.itemTypes) : null;
            var features = item.itemFeatures != null ? new List<ItemFeature>(item.itemFeatures) : null;
            int count = item.unitCount; long baseValue = item.unitBaseValue, value = item.unitValue, late = item.lateUnitValue, backup = item.backupUnitValue;
            string name = item.name, description = item.shortDescription, flavor = item.flavorText, atlas = item.spriteAtlasPath, sprite = item.spritePath;
            WorldReplica.Sandbox++; var was = WorldReplica.SandboxItem; bool permit = WorldReplica.SandboxMovePermit;
            WorldReplica.SandboxItem = null; WorldReplica.SandboxMovePermit = false;   // the hooks may not move, spawn or destroy anything
            try {
                if (created) {
                    Hook("upgrade", item, MachineHelper.LoadUpgrade);
                    Hook("lock", item, GeneralHelper.LoadLockItem);
                    Hook("produce", item, PlantTraitList.LoadEdibleProduce);
                    Hook("taste", item, PlantTraitList.LoadTasteFeature);
                    Hook("tool tag", item, GeneralHelper.LoadNonOwnedToolTag);
                    Hook("story tag", item, GeneralHelper.LoadImportantStoryItemTag);
                    Hook("waste tag", item, GeneralHelper.LoadHazardousWasteTag);
                    Hook("token tag", item, ScavHelper.LoadScavTokenTag);
                    Hook("locked container", item, LockHelper.LoadLockedContainer);
                    Hook("broken machine", item, MachineBrokenHelper.LoadBrokenMachine);
                    Hook("machine", item, MachineHelper.OnLoad);
                    Hook("turbo tag", item, MachineTurboBoosterAdv.LoadTurboBoostEligibleTag);
                    Hook("converter", item, CurrentConverterHelper.OnLoad);
                } else {
                    var after = Read(item);
                    if (after.stage != before.stage) Hook("upgrade", item, MachineHelper.LoadUpgrade);
                    Hook("lock", item, GeneralHelper.LoadLockItem);
                    if (item.IsTag("LOCKED_CONTAINER_TAG") && item.contentWindow != null && item.contentWindow.isLocked != item.GetTagReadonly("LOCK_STATUS").GetBool())
                        Hook("locked container", item, LockHelper.LoadLockedContainer);
                    if (after.broken && !before.broken) Hook("broken machine", item, MachineBrokenHelper.LoadBrokenMachine);
                    if (after.workingLocked != before.workingLocked) Hook("machine", item, after.workingLocked ? (Action<GameItem>)MachineHelper.LockModuleInv : MachineHelper.UnlockModuleInv);
                }
                Loaded(item, created);
            } finally {
                WorldReplica.Sandbox--; WorldReplica.SandboxItem = was; WorldReplica.SandboxMovePermit = permit;
                if (state != null) item.state = state;
                if (shape != null) item.shape = shape;
                if (modifiedShape != null) item.modifiedShape = modifiedShape;
                if (types != null) item.itemTypes = types;
                if (features != null) item.itemFeatures = features;
                item.unitCount = count; item.unitBaseValue = baseValue; item.unitValue = value; item.lateUnitValue = late; item.backupUnitValue = backup;
                item.name = name; item.shortDescription = description; item.flavorText = flavor;
                if (item.spriteAtlasPath != atlas || item.spritePath != sprite) item.SetSprite(atlas, sprite);
            }
            try { item.Validate(); if (item.contentWindow != null && item.contentWindow.IsVisible()) item.contentWindow.Validate(); } catch (Exception e) { Diagnostics.Report("layout " + item.identifier, e); }
        }

        static void Loaded(GameItem item, bool created) {
            var all = item.onLoaded; if (all == null) return;
            foreach (var part in all.GetInvocationList()) {
                var hook = part as Action<GameItem>; if (hook == null) continue;
                if (!created) {
                    string owner = Owner(part.Method);
                    // A wine bottle stops being a container once it ferments; after that its hook only stacks handlers.
                    if (owner == "WineHelper" ? item.contentWindow == null : onceOnly.Contains(owner)) continue;
                }
                try { hook(item); } catch (Exception e) { Diagnostics.Report("loaded " + item.identifier, e); }
            }
        }

        static string Owner(MethodInfo method) {
            var type = method != null ? method.DeclaringType : null;
            while (type != null && type.DeclaringType != null) type = type.DeclaringType;
            return type != null ? type.Name : "";
        }

        static void Hook(string what, GameItem item, Action<GameItem> hook) {
            try { hook(item); } catch (Exception e) { Diagnostics.Report("load " + what + " " + item.identifier, e); }
        }
    }
}
