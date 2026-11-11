using System;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// When the host loads a save, the game also sets up the shop's furniture from the save: unlocked vending
    /// machines, the upgraded display case, bigger dossier/trash can, the tap, and the marketplace-request screen.
    /// A friend's machine never loads the save (it shows the host's shop), so the same setup is done here from
    /// the host's synced store data, and again whenever the host unlocks something. Visual setup only.
    /// </summary>
    internal static class ReplicaVisuals {
        static int showcaseLevel; static bool posterLoaded; static int tap = -1; static float next; static string requests = "";

        internal static void Reset() { showcaseLevel = 0; posterLoaded = false; tap = -1; next = 0; requests = ""; }

        internal static void Refresh(bool force) {
            if (!ReplicaSession.ClientStore || EmporiumEntry.Instance == null || PlayerStore.Instance == null) return;
            if (!force && Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 1f;
            var store = PlayerStore.Instance; var entry = EmporiumEntry.Instance;
            Try(() => { var ui = StoreUIManager.Instance; if (ui != null && ui.shotgun != null && ui.shotgun.activeSelf != store.isShootingUnlocked) ui.shotgun.SetActive(store.isShootingUnlocked); });
            Try(() => { if (StoreVendingMachine.Instance != null) { StoreVendingMachine.Instance.gameObject.SetActive(store.isVendingMachineUnlocked); if (store.isVendingMachineUnlocked) StoreVendingMachine.Instance.UpdateSprite(); } });
            Try(() => { if (StoreVendingFountain.Instance != null) { StoreVendingFountain.Instance.gameObject.SetActive(store.isVendingFountainUnlocked); if (store.isVendingFountainUnlocked) StoreVendingFountain.Instance.UpdateSprite(); } });
            Try(() => {
                int level = NetworkUpgrade.IsUnlocked("SHOWCASE_II") ? 2 : NetworkUpgrade.IsUnlocked("SHOWCASE_I") ? 1 : 0;
                if (level > showcaseLevel) { if (level == 2) entry.UpgradeDisplayCaseII(); else entry.UpgradeDisplayCaseI(); showcaseLevel = level; }
            });
            Try(() => { if (NetworkUpgrade.IsUnlocked("DOSSIER_UPGRADE")) entry.UpgradeDossier(); if (NetworkUpgrade.IsUnlocked("TRASHCAN_UPGRADE")) entry.UpgradeTrashcan(); });
            Try(() => { int t = EmporiumEntry.IsWaterTapUnlocked() ? 1 : 0; if (t != tap) { entry.ApplyFaucetState(t == 1); tap = t; } });
            Try(() => { if (StoreUIManager.Instance != null && StoreUIManager.Instance.pcScreenObject != null) StoreUIManager.Instance.pcScreenObject.SetActive(NetworkUpgrade.IsMarketplaceRequestsUnlocked()); });
            Try(() => { if (!posterLoaded && StorePoster.Instance != null) { posterLoaded = true; StorePoster.LoadStorePoster(); } });
            // Marketplace requests changed on the host (delivered, new, expired): refresh the PC list if it is open here.
            Try(() => {
                var b = new System.Text.StringBuilder(); if (store.storeRequests != null) foreach (var r in store.storeRequests) b.Append(RequestAdapters.Key(r)).Append('\n');
                string now = b.ToString(); if (now == requests) return; requests = now;
                if (PCUIManager.Instance != null && PCUIManager.Instance.panel != null && PCUIManager.Instance.panel.activeSelf) PCUIManager.Instance.OnChange();
            });
        }

        static void Try(Action action) {
            bool was = WorldReplica.Applying; WorldReplica.Applying = true;
            try { action(); } catch (Exception e) { Debug.LogWarning("[ShopTogether] shop setup on friend: " + e.Message); } finally { WorldReplica.Applying = was; }
        }
    }
}
