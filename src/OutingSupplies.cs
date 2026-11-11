using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// In a shared shop nobody has to carry the shop's tools: a flashlight, screwdriver, wire cutter, welder or metal
    /// scanner that the shop owns helps everyone who is out (the base game only counts what the shopkeeper carries).
    /// The best metal scanner the shop owns is used. Only while playing together.
    /// </summary>
    internal static class OutingSupplies {
        static bool Together { get { var s = Connection.Current; return s != null && (s.IsHost || s.IsClient) && s.Ready; } }

        static readonly Dictionary<string, KeyValuePair<float, GameItem>> cache = new Dictionary<string, KeyValuePair<float, GameItem>>();
        /// <summary>The shop's own item of this kind (the best one for the metal scanner), looked up at most once a second.</summary>
        internal static GameItem InShop(string identifier) {
            var e = EmporiumEntry.Instance;
            if (e == null || identifier == null) return null;
            KeyValuePair<float, GameItem> hit;
            if (cache.TryGetValue(identifier, out hit) && Time.realtimeSinceStartup - hit.Key < 1f && (hit.Value == null || hit.Value.parentInventory != null)) return hit.Value;
            GameItem best = null; int bestScore = int.MinValue;
            foreach (var item in e.GetAllOwnedItems(false, false)) {
                if (item == null || item.identifier != identifier) continue;
                int score = identifier == "metal_scanner" ? item.GetTagReadonly("SCAV_SCANNER_DOUBLE_CHANCE").GetInt() : 0;
                if (best == null || score > bestScore) { best = item; bestScore = score; }
            }
            cache[identifier] = new KeyValuePair<float, GameItem>(Time.realtimeSinceStartup, best);
            return best;
        }

        // ---------------- bandages ----------------

        /// <summary>The shop's healing items a wounded shopkeeper can use now, cheapest first.</summary>
        internal static List<GameItem> Bandages() {
            var list = new List<GameItem>();
            var e = EmporiumEntry.Instance;
            if (e == null) return list;
            foreach (var item in e.GetAllOwnedItems(false, false))
                if (item != null && item.identifier != null && item.identifier.Contains("bandage") && item.MayActivateSlotItem() && item.CanActivateSlotItem()) list.Add(item);
            list.Sort((a, b) => a.unitValue.CompareTo(b.unitValue));
            return list;
        }

        /// <summary>Host side: use the cheapest bandage the shop has (the wound is everyone's, so anyone may).</summary>
        internal static void UseBandage() {
            var list = Bandages();
            if (list.Count == 0) { RemoteActions.PendingNote = "~SMC227~"; return; }
            list[0].ActivateSlotItem();
            cache.Clear();
        }

        const string Window = "shop_together.bandage";
        static int shownCount = -1;
        static float nextCheck;
        static bool Outside {
            get {
                var s = Connection.Current;
                if (s == null) return false;
                if (s.IsHost) return MapUIManager.Instance != null && MapUIManager.Instance.Current != null;
                return OutingKits.Here != null || OutingKits.Joined != null;
            }
        }

        /// <summary>Every frame on every machine: while out with an open wound, a button uses one of the shop's bandages.</summary>
        internal static void Tick() {
            if (Time.realtimeSinceStartup < nextCheck || CustomUIManager.Instance == null) return;
            nextCheck = Time.realtimeSinceStartup + .3f;
            if (!Together || EmporiumEntry.Instance == null) return;
            var store = PlayerStore.Instance;
            int count = Together && Outside && store != null && store.healthData != null && !store.healthData.isWoundStable ? Bandages().Count : -1;
            if (count == shownCount) return;
            shownCount = count;
            CustomUIManager.Instance.CloseWindow(Window);
            if (count < 0) return;
            var builder = CustomUIManager.Window(Window, Loc.Text("~SMC228~"), "meta");
            if (count == 0) builder.AddLabel(Loc.Text("~SMC227~"));
            else builder.AddButton(Loc.Text("~SMC229~" + count + "~SMC230~"), Press);
            var map = MapUIManager.Instance;
            if (map != null) builder.SetPosition(map.panelPosition + new Vector2(0f, -260f));
            builder.Show();
        }
        static void Press() {
            var s = Connection.Current;
            if (s == null) return;
            shownCount = -2;   // redraw once the count changes
            if (s.IsHost) UseBandage();
            else RemoteActions.Forward(RemoteActions.Adapter("UseBandage", 0), null, new object[0]);
        }
        internal static void Reset() { cache.Clear(); shownCount = -1; if (CustomUIManager.Instance != null) CustomUIManager.Instance.CloseWindow(Window); }

        [HarmonyPatch(typeof(EmporiumEntry), "IsAfterhourHaveOwnedItems")]
        internal static class ShopTools {
            static void Postfix(string identifier, ref bool __result) { if (!__result && Together && InShop(identifier) != null) __result = true; }
        }
        [HarmonyPatch(typeof(EmporiumEntry), "GetAfterhourItemById")]
        internal static class ShopTool {
            static void Postfix(string identifier, ref GameItem __result) {
                if (!Together) return;
                var shop = InShop(identifier);
                if (shop == null) return;
                if (__result == null || (identifier == "metal_scanner" && shop.GetTagReadonly("SCAV_SCANNER_DOUBLE_CHANCE").GetInt() > __result.GetTagReadonly("SCAV_SCANNER_DOUBLE_CHANCE").GetInt())) __result = shop;
            }
        }
    }
}
