using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// The outing backpack starts empty, so nothing can be carried to the dumping ground. Two things the base game
    /// does by leaving items there get a button instead: nuclear waste, and food for Serpico while he is waiting for it.
    /// The button puts the shop's own items on the presser's ground; leaving the dumping ground then works as in the
    /// base game (the waste is counted as dumped, Serpico takes the food).
    /// </summary>
    internal static class DumpingGround {
        const string Place = "dumping_ground", Window = "shop_together.dumping_ground";
        const int SerpicoGoal = 2000;

        static bool IsWaste(GameItem item) { return item != null && item.identifier == "nuclear_waste"; }
        internal static bool IsFood(GameItem item) { return item != null && item.IsGameItemType("FOOD") && item.IsTag("CALORIE_VALUE_TAG"); }
        static int Kcal(GameItem item) { return item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt() * Mathf.Max(1, item.unitCount); }

        /// <summary>The ground of whoever is acting: the host's own, or a friend's while they are at the dumping ground.</summary>
        static GameInventory Ground(int owner) {
            if (owner > 0) { var kit = OutingKits.Of(owner); return kit != null && kit.location == Place ? kit.ground : null; }
            var map = MapUIManager.Instance; var e = EmporiumEntry.Instance;
            return map != null && map.Current != null && map.Current.Id == Place && !map.IsInTransit && e != null ? e.afterhourInventory : null;
        }

        /// <summary>The shop's own items of a kind that nobody is holding right now (never anything already outside).</summary>
        static List<GameItem> InShop(int owner, Func<GameItem, bool> kind) {
            var list = new List<GameItem>(); var e = EmporiumEntry.Instance; var session = Connection.Current;
            if (e == null) return list;
            float now = Time.realtimeSinceStartup;
            foreach (var item in e.GetAllOwnedItems(false, false)) {
                if (!kind(item) || item.parentInventory == null) continue;
                if (session != null && session.IsHost) { int id = session.World.Id(item); if (id > 0 && !session.Leases.Available(owner, id, now)) continue; }
                list.Add(item);
            }
            return list;
        }

        static int FoodOn(GameInventory ground) {
            int kcal = 0;
            if (ground != null) foreach (var item in GraphUtils.FindAllChildrenType<GameItem>(ground)) if (IsFood(item)) kcal += Kcal(item);
            return kcal;
        }
        static int StillNeeded(GameInventory ground) {
            // Food placed by another player is already reserved for the same host-owned story.
            // Count every active dumping-ground inventory once before deciding what to bring next.
            var grounds = new HashSet<GameInventory>();
            if (ground != null) grounds.Add(ground);
            var e = EmporiumEntry.Instance; var map = MapUIManager.Instance; var session = Connection.Current;
            bool hostHere = session != null && session.IsClient ? HostPhase.Last != null && HostPhase.Last.location == Place : map != null && map.Current != null && map.Current.Id == Place;
            if (e != null && hostHere && e.afterhourInventory != null) grounds.Add(e.afterhourInventory);
            foreach (var kit in OutingKits.All) if (kit.owner > 0 && kit.location == Place && kit.ground != null) grounds.Add(kit.ground);
            int reserved = 0; foreach (var reservedGround in grounds) reserved += FoodOn(reservedGround);
            return Mathf.Max(0, SerpicoGoal - PlayerStore.GetStoreClientData().serpicoFoodKcal - reserved);
        }

        // The goal is small (2000 kcal). Keep one immutable path per reachable total below it, then
        // retain the least total meeting the goal. Binary groups let stacks be split without a loop per unit.
        sealed class FoodChoice {
            internal FoodChoice previous;
            internal GameItem item;
            internal int units;
        }
        static Dictionary<GameItem,int> SelectFood(List<GameItem> food, int need, out long total) {
            var paths = new FoodChoice[need];
            paths[0] = new FoodChoice();
            FoodChoice best = null; long bestTotal = long.MaxValue;
            foreach (var item in food) {
                int per = item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt();
                if (per <= 0) continue;
                int remaining = Math.Min(Math.Max(1, item.unitCount), (int)(((long)need + per - 1) / per));
                for (int group = 1; remaining > 0; group = Math.Min(group * 2, 2048)) {
                    int count = Math.Min(group, remaining); remaining -= count;
                    long value = (long)per * count;
                    for (int sum = need - 1; sum >= 0; --sum) {
                        var previous = paths[sum]; if (previous == null) continue;
                        long next = sum + value;
                        if (next >= need) {
                            if (next < bestTotal) { bestTotal = next; best = new FoodChoice { previous = previous, item = item, units = count }; }
                        } else if (paths[(int)next] == null) paths[(int)next] = new FoodChoice { previous = previous, item = item, units = count };
                    }
                }
            }
            if (best == null) {
                int reachable = need - 1; while (reachable > 0 && paths[reachable] == null) reachable--;
                total = reachable; best = paths[reachable];
            } else total = bestTotal;
            var selected = new Dictionary<GameItem,int>();
            for (var choice = best; choice != null && choice.item != null; choice = choice.previous) {
                int existing; selected.TryGetValue(choice.item, out existing); selected[choice.item] = existing + choice.units;
            }
            return selected;
        }

        // ---------------- host side (a friend's press arrives here through a remote action) ----------------

        internal static void DumpWaste() {
            int owner = RemoteActions.CurrentOwner; var ground = Ground(owner);
            if (ground == null) { Say(owner, "~SMC247~"); return; }
            var waste = InShop(owner, IsWaste);
            if (waste.Count == 0) { Say(owner, "~SMC261~"); return; }
            int moved = 0; bool full = false;
            foreach (var item in waste) { int amount = Math.Max(1, item.unitCount); int remaining = GraphUtils.TryAcceptAll(ground, item, amount); moved += amount - remaining; if (remaining > 0) full = true; }
            Say(owner, moved == 0 ? "~SMC262~" : full ? "~SMC255~~SMC260~" : "~SMC255~");
        }

        internal static void FeedSerpico() {
            int owner = RemoteActions.CurrentOwner; var ground = Ground(owner);
            if (ground == null || !SerpicoStoryline.IsFoodWindowOpen()) { Say(owner, "~SMC247~"); return; }
            int need = StillNeeded(ground);
            if (need <= 0) { Say(owner, "~SMC265~"); return; }
            var food = InShop(owner, IsFood);
            if (food.Count == 0) { Say(owner, "~SMC261~"); return; }
            // The cheapest food for its calories goes first, the way a player would pick it.
            food.Sort((a, b) => ((double)a.GetValue() / Math.Max(1, Kcal(a))).CompareTo((double)b.GetValue() / Math.Max(1, Kcal(b))));
            long selectedCalories; var selected = SelectFood(food, need, out selectedCalories);
            if (selected.Count == 0) { Say(owner, "~SMC261~"); return; }
            bool full = false; int moved = 0;
            foreach (var pair in selected) {
                int remaining = GraphUtils.TryAcceptAll(ground, pair.Key, pair.Value);
                moved += pair.Value - remaining; if (remaining > 0) full = true;
            }
            string outcome = moved == 0 ? "~SMC262~" : full ? "~SMC259~~SMC260~" : selectedCalories < need ? "~SMC259~~SMC264~" : selectedCalories > need ? "~SMC259~~SMC263~" : "~SMC259~";
            Say(owner, outcome);
        }

        static void Say(int owner, string text) {
            if (owner > 0) RemoteActions.PendingNote = text;
            else if (StoreUIManager.Instance != null) StoreUIManager.Instance.Notify(Loc.Text(text));
        }

        /// <summary>Host side, a friend leaving the dumping ground: as in the base game, the waste left there counts as
        /// dumped and is gone, and food Serpico took is gone (everything else they left still comes home).</summary>
        internal static void FriendLeaves(GameInventory ground, bool serpicoTookFood) {
            if (ground == null) return;
            foreach (var item in new List<GameItem>(ground.childItems)) {
                if (IsWaste(item)) { PlayerStore.GetStoreClientData().nuclearWasteDumped++; ground.Expel(item); item.Destroy(); }
            }
            if (!serpicoTookFood) return;
            RemoveDonatedFood(ground);
        }
        internal static void RemoveDonatedFood(GameInventory ground) {
            foreach (var item in new List<GameItem>(GraphUtils.FindAllChildrenType<GameItem>(ground)))
                if (IsFood(item) && item.parentInventory != null) { item.parentInventory.Expel(item); item.Destroy(); }
        }

        // ---------------- every player's screen ----------------

        static string shownSig; static float nextCheck;
        internal static void Tick() {
            if (Time.realtimeSinceStartup < nextCheck || CustomUIManager.Instance == null) return;
            nextCheck = Time.realtimeSinceStartup + .4f;
            var s = Connection.Current;
            if (s == null || !(s.IsHost || s.IsClient) || !s.Ready || EmporiumEntry.Instance == null || PlayerStore.Instance == null) { Close(); return; }
            var map = MapUIManager.Instance;
            bool here = s.IsHost ? map != null && map.Current != null && map.Current.Id == Place && !map.IsInTransit : OutingKits.Here == Place;
            if (!here) { Close(); return; }
            // A friend's destination is known before its arrival transition finishes; opening here
            // too early lets the game's transition close the controls while their signature stays cached.
            var storeUI = StoreUIManager.Instance;
            if (storeUI != null && storeUI.blackscreen != null && storeUI.blackscreen.gameObject.activeInHierarchy) { Close(); return; }
            int waste = InShop(s.IsHost ? 0 : s.PlayerId, IsWaste).Count;
            bool feeding = SerpicoStoryline.IsFoodWindowOpen();
            var mine = s.IsHost ? Ground(0) : OutingKits.Mine != null ? OutingKits.Mine.ground : null;
            int need = feeding ? StillNeeded(mine) : 0;
            string sig = waste + "|" + feeding + "|" + need;
            if (sig == shownSig) return;
            CustomUIManager.Instance.CloseWindow(Window);
            if (waste == 0 && !feeding) { shownSig = sig; return; }
            shownSig = sig;
            var builder = CustomUIManager.Window(Window, Loc.Text("~SMC253~"), "meta");
            if (waste > 0) builder.AddButton(Loc.Text("~SMC254~") + " (" + waste + ")", () => Press("DumpWaste"));
            if (feeding) {
                builder.AddLabel(LocHelper.GetLocalizedClientName("name_serpico") + Loc.Text("~SMC257~") + need + Loc.Text("~SMC258~"));
                builder.AddButton(Loc.Text("~SMC256~"), () => Press("FeedSerpico"));
            }
            if (map != null) builder.SetPosition(map.panelPosition + new Vector2(0f, -520f));
            builder.Show();
        }
        static void Press(string action) {
            var s = Connection.Current;
            if (s == null) return;
            shownSig = null;   // redraw with the new counts
            if (s.IsHost) { if (action == "DumpWaste") DumpWaste(); else FeedSerpico(); }
            else RemoteActions.Forward(RemoteActions.Adapter(action, 0), null, new object[0]);
        }
        static void Close() {
            var ui = CustomUIManager.Instance;
            if (shownSig == null && (ui == null || !ui.IsOpen(Window))) return;
            shownSig = null;
            if (ui != null) ui.CloseWindow(Window);
        }
        internal static void Reset() { shownSig = null; if (CustomUIManager.Instance != null) CustomUIManager.Instance.CloseWindow(Window); }
    }

    // The host uses the game's own leave handler; remove only food the storyline actually counted.
    [HarmonyPatch(typeof(SerpicoStoryline), "OnLeaveDumpingGround")]
    internal static class HostSerpicoFood {
        static void Prefix(GameInventory ground, out bool __state) {
            var s = Connection.Current; var e = EmporiumEntry.Instance;
            __state = s != null && s.IsHost && e != null && ReferenceEquals(ground, e.afterhourInventory) && SerpicoStoryline.IsFoodWindowOpen();
        }
        static void Postfix(GameInventory ground, bool __state) { if (__state) DumpingGround.RemoveDonatedFood(ground); }
    }
}
