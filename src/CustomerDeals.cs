using System;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace ShopTogether {
    [Serializable] internal sealed class BarterView { public string text; public int set, sets; public bool acceptable, free; }
    [Serializable] internal sealed class GunRequirementView { public int value, reward; public string id; public bool larger, caliber; }
    [Serializable] internal sealed class GunOrderView {
        public int tier, baseReward, serviceGun, serviceCondition; public string dialogText, serviceGunId; public bool best, service;
        public GunRequirementView[] mandatory, bonus;
    }

    /// <summary>
    /// Customer data that only exists on the host: what the customer buys when that is decided by the customer's own
    /// rules (water buyers, food buyers...), the barter offer and the gunsmith order. The host sends its answers;
    /// the friend's tablet reads them instead of its empty copy of the customer.
    /// </summary>
    internal static class CustomerDeals {
        const int MaxJudged = 256;
        internal static StoreClient Customer;
        static readonly HashSet<int> judged = new HashSet<int>(), buying = new HashSet<int>(), shallow = new HashSet<int>();
        internal static BarterOffer Barter; internal static bool BarterAcceptable;
        static string appliedGun;

        internal static void Reset() { Customer = null; judged.Clear(); buying.Clear(); shallow.Clear(); Barter = null; BarterAcceptable = false; appliedGun = null; }

        // ---------------- host ----------------
        internal static void Capture(StoreClient customer, WorldReplica world, StoreView view) {
            var entry = EmporiumEntry.Instance; var store = PlayerStore.Instance;
            if (customer == null || entry == null || store == null) return;
            var items = new List<GameItem>(); var seen = new HashSet<GameItem>();
            Action<GameItem> add = null;
            add = item => {
                if (item == null || items.Count >= MaxJudged || !seen.Add(item)) return;
                items.Add(item);
                if (item.contentWindow != null) foreach (var child in GraphUtils.FindAllChildrenType<GameItem>(item)) { if (items.Count >= MaxJudged) break; if (seen.Add(child)) items.Add(child); }
            };
            add(store.currentNegociatedItem);
            foreach (var item in entry.GetBackInvItems().ToArray()) add(item);
            var judgedIds = new List<int>(); var buyingIds = new List<int>(); var shallowIds = new List<int>();
            foreach (var item in items) {
                int id = world.Id(item); if (id <= 0) continue;
                bool full, contentsIgnored;
                try { full = customer.IsClientBuyingThisItem(item, false); contentsIgnored = customer.IsClientBuyingThisItem(item, true); } catch { continue; }
                judgedIds.Add(id); if (full) buyingIds.Add(id); if (contentsIgnored) shallowIds.Add(id);
            }
            view.judged = judgedIds.ToArray(); view.buying = buyingIds.ToArray(); view.buyingShallow = shallowIds.ToArray();

            var barter = customer.barterOffer;
            if (barter != null) {
                bool acceptable = false; try { acceptable = barter.IsBarterAcceptable(); } catch { }
                view.barter = new BarterView { text = barter.barterTextBody, set = barter.currentSetIndex, sets = barter.OfferSetCount(), acceptable = acceptable, free = barter.isFreeReward };
            }
            var order = customer.gunOrderData;
            if (order != null) {
                var gun = ModReflect.Get<GameItem>(order, "serviceGun");
                view.gunOrder = new GunOrderView {
                    tier = order.tier, baseReward = order.baseReward, dialogText = order.dialogText, best = order.isBestOrder, service = order.isServiceOrder,
                    serviceGunId = ModReflect.Get<string>(order, "serviceGunId"), serviceCondition = Convert.ToInt32(ModReflect.Get(order, "serviceGunCondition")),
                    serviceGun = world.Id(gun), mandatory = Requirements(order.mandatoryRequirements), bonus = Requirements(order.bonusRequirements) };
            }
        }
        static GunRequirementView[] Requirements(List<GunOrderRequirement> list) {
            var result = new List<GunRequirementView>();
            if (list != null) foreach (var r in list) if (r != null && result.Count < 32) result.Add(new GunRequirementView { value = r.value, reward = r.reward, id = r.id, larger = r.isMustBeLargerThan, caliber = r.isCaliber });
            return result.ToArray();
        }

        // ---------------- friend ----------------
        internal static void Apply(StoreClient customer, StoreView view, WorldReplica world, bool newCustomer) {
            if (newCustomer) { Barter = null; appliedGun = null; }
            Customer = customer;
            bool changed = Fill(judged, view.judged) | Fill(buying, view.buying) | Fill(shallow, view.buyingShallow);

            if (view.barter == null) { customer.barterOffer = null; Barter = null; }
            else {
                if (Barter == null || customer.barterOffer != Barter) { Barter = new BarterOffer(); customer.barterOffer = Barter; changed = true; }
                int sets = Math.Max(0, Math.Min(64, view.barter.sets));
                if (Barter.barterTextBody != (view.barter.text ?? "") || Barter.currentSetIndex != view.barter.set || Barter.OfferSetCount() != sets || BarterAcceptable != view.barter.acceptable) changed = true;
                Barter.barterTextBody = view.barter.text ?? ""; Barter.isFreeReward = view.barter.free; Barter.currentSetIndex = view.barter.set;
                Barter.offerSets = new List<Action>(new Action[sets]); BarterAcceptable = view.barter.acceptable;
            }

            string gun = view.gunOrder == null ? null : JsonConvert.SerializeObject(view.gunOrder);
            if (gun == null) { customer.gunOrderData = null; appliedGun = null; }
            else if (gun != appliedGun || customer.gunOrderData == null) {
                var g = view.gunOrder; var order = new GunOrderData {
                    tier = g.tier, baseReward = g.baseReward, dialogText = g.dialogText ?? "", isBestOrder = g.best, isServiceOrder = g.service,
                    mandatoryRequirements = Requirements(g.mandatory), bonusRequirements = Requirements(g.bonus) };
                ModReflect.Set(order, "serviceGunId", g.serviceGunId);
                ModReflect.Set(order, "serviceGunCondition", Enum.ToObject(typeof(ItemFeatureList.EquipmentCondition), g.serviceCondition));
                GameItem serviceGun; bool found = world.Items.TryGetValue(g.serviceGun, out serviceGun);
                ModReflect.Set(order, "serviceGun", found ? serviceGun : null);
                customer.gunOrderData = order; changed = true;
                // The gun may arrive later in this same update: look it up again with the next one.
                appliedGun = g.serviceGun != 0 && !found ? null : gun;
            }

            var ui = NegociationUIManager.Instance;
            if (changed && ui != null && ui.IsOpen) Diagnostics.Soft("tablet", () => ui.UpdateUI());
        }
        static List<GunOrderRequirement> Requirements(GunRequirementView[] views) {
            var result = new List<GunOrderRequirement>();
            if (views != null) foreach (var v in views) if (v != null && result.Count < 32) result.Add(new GunOrderRequirement { value = v.value, reward = v.reward, id = v.id, isMustBeLargerThan = v.larger, isCaliber = v.caliber });
            return result;
        }
        static bool Fill(HashSet<int> set, int[] ids) {
            var next = new HashSet<int>(); if (ids != null) for (int i = 0; i < ids.Length && i < MaxJudged; i++) next.Add(ids[i]);
            if (set.SetEquals(next)) return false;
            set.Clear(); set.UnionWith(next); return true;
        }

        internal static bool Answer(StoreClient customer, GameItem item, bool ignoreContents, out bool buys) {
            buys = false;
            if (!ReplicaSession.ClientStore || customer == null || customer != Customer || item == null) return false;
            var session = Connection.Current; if (session == null) return false;
            int id = session.World.Id(item); if (id <= 0 || !judged.Contains(id)) return false;
            buys = (ignoreContents ? shallow : buying).Contains(id); return true;
        }
    }

    /// <summary>Friend: whether the customer buys an item is the host's answer (the customer's own rules are not copied).</summary>
    [HarmonyPatch(typeof(StoreClient), "IsClientBuyingThisItem", new[] { typeof(GameItem), typeof(bool) })]
    internal static class ReplicaCustomerBuys {
        static bool Prefix(StoreClient __instance, GameItem __0, bool __1, ref bool __result) {
            bool buys; if (!CustomerDeals.Answer(__instance, __0, __1, out buys)) return true;
            __result = buys; return false;
        }
    }

    /// <summary>Friend: the barter offer on the tablet is the host's; switching offers and accepting run on the host.</summary>
    [HarmonyPatch(typeof(BarterOffer), "IsBarterAcceptable")]
    internal static class ReplicaBarterAcceptable {
        static bool Prefix(BarterOffer __instance, ref bool __result) { if (__instance == null || __instance != CustomerDeals.Barter) return true; __result = CustomerDeals.BarterAcceptable; return false; }
    }
    [HarmonyPatch(typeof(BarterOffer), "GetItemPlacedResponse")]
    internal static class ReplicaBarterResponse {
        static bool Prefix(BarterOffer __instance, ref string __result) { if (__instance == null || __instance != CustomerDeals.Barter) return true; __result = null; return false; }
    }
    [HarmonyPatch]
    internal static class ReplicaBarterLocal {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() {
            foreach (var name in new[] { "ShowSetByOffset", "ShowCurrentSet", "OnBarterAccepted", "InstanciateOffer" }) { var m = AccessTools.Method(typeof(BarterOffer), name); if (m != null) yield return m; }
        }
        static bool Prefix(BarterOffer __instance) { return __instance == null || __instance != CustomerDeals.Barter; }
    }

    /// <summary>
    /// Friend: when an item lands on the counter the game's reaction may read customer data that only the host has.
    /// That reaction is the host's anyway; a failure here is reported instead of breaking the item update.
    /// </summary>
    [HarmonyPatch(typeof(PlayerStore), "OnWeightedAreaChanged")]
    internal static class ReplicaCounterReaction {
        static Exception Finalizer(Exception __exception) {
            if (__exception == null || !ReplicaSession.ClientStore) return __exception;
            Diagnostics.Report("counter reaction", __exception); return null;
        }
    }
}
