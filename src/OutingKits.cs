using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    [HarmonyPatch(typeof(GameItem), "MayRemove")]
    internal static class VoidBackpackPickup {
        static bool Prefix(GameItem __instance, ref bool __result) {
            if (Connection.Current == null || !Connection.Current.Ready || __instance.parentInventory == null || !OutingKits.IsVoidBag(__instance)) return true;
            __result = false;
            return false;
        }
    }
    [HarmonyPatch(typeof(GameItem), "MaxNumRemove")]
    internal static class VoidBackpackTransfer {
        static bool Prefix(GameItem __instance, ref int __result) {
            if (Connection.Current == null || !Connection.Current.Ready || __instance.parentInventory == null || !OutingKits.IsVoidBag(__instance)) return true;
            __result = 0;
            return false;
        }
    }
    /// <summary>
    /// Night outings for friends. The host goes out exactly as in the base game. A friend can go to the dumping
    /// ground or the commissary on their own: each friend gets their own ground, two pockets and backpack slot, and
    /// their own scavenging attempts. Everything they bring back goes into the shared shop. Dr. Jackson and locations
    /// added by other mods require the host to arrive first; friends can then join that host-owned scene.
    /// Nobody can sleep while someone is still out.
    /// </summary>
    internal sealed class Kit {
        internal int slot, owner;           // owner: player number of the friend using it (0 = free)
        internal string location;           // where that friend is (null = in the shop)
        internal int attempts;              // scavenging attempts used at the current visit
        /// <summary>What is still in this kit besides an empty void backpack (things that did not fit in the shop).</summary>
        internal bool HasLeftovers {
            get {
                if (ground.childItems.Count > 0 || left.childItems.Count > 0 || right.childItems.Count > 0) return true;
                foreach (var item in backpack.childItems) if (!OutingKits.IsVoidBag(item) || OutingKits.Contents(item).Count > 0) return true;
                return false;
            }
        }
        internal GameGridInventory ground;
        internal GameSlotInventory left, backpack, right;
        internal PixelWindow window;
        internal IEnumerable<GameInventory> Pockets() { yield return backpack; yield return right; yield return left; }
    }

    internal static class OutingKits {
        internal const int Slots = 7;
        internal const int VanillaRoot = 40, CounterRoot = 44, KitRoot = 100;           // root numbers in the world replica
        internal static readonly string[] FriendPlaces = { "dumping_ground", "commissary" };

        static Kit[] kits;
        static EmporiumEntry builtFor;

        internal static bool FriendMayGo(string id) { return Array.IndexOf(FriendPlaces, id) >= 0; }

        /// <summary>A friend going out picks one of the shop's backpacks, but only borrows its look and size: they get an
        /// empty "void backpack" of the same kind, and the real one (with whatever is in it) stays in the shop.</summary>
        internal const string VoidTag = "SHOPTOGETHER_VOID_BAG";
        internal static bool IsVoidBag(GameItem item) { return item != null && item.IsTag(VoidTag); }
        /// <summary>The items directly inside a container (what is inside those comes along with them).</summary>
        internal static List<GameItem> Contents(GameItem bag) {
            var list = new List<GameItem>();
            foreach (var item in GraphUtils.FindAllChildrenType<GameItem>(bag))
                if (item != null && !ReferenceEquals(item, bag) && item.parentInventory != null && ReferenceEquals(GraphUtils.FindParentType<GameItem>(item.parentInventory), bag)) list.Add(item);
            return list;
        }

        internal static Kit[] All {
            get {
                var e = EmporiumEntry.Instance;
                if (e == null) return new Kit[0];
                if (kits == null || !ReferenceEquals(builtFor, e)) { builtFor = e; kits = new Kit[Slots]; for (int i = 0; i < Slots; i++) kits[i] = Build(i); }
                return kits;
            }
        }

        static Kit Build(int slot) {
            var kit = new Kit { slot = slot };
            kit.window = new PixelWindow(canMove: false, LocHelper.GetLocalizedUI("ui_afterhour_ground_title"), WindowStyle.Machinery);
            kit.ground = new GameGridInventory(12, 9);
            kit.left = new GameSlotInventory();
            kit.left.SetTooltipName("[" + LocHelper.GetLocalizedItem("item_expedition_box_label_pocket1") + "]");
            kit.left.SetBackgroundFadeSprite("Items/items_container", "pouch");
            kit.backpack = new GameSlotInventory(3, 3, stealUI: true);
            kit.backpack.SetBackgroundFadeSprite("Items/items_backpack2", "simple_backpack_medium");
            kit.right = new GameSlotInventory();
            kit.right.SetTooltipName("[" + LocHelper.GetLocalizedItem("item_expedition_box_label_pocket2") + "]");
            kit.right.SetBackgroundFadeSprite("Items/items_container", "pouch");
            ContainerHelper.AllowOnlyOwnedItems(kit.left);
            ContainerHelper.AllowOnlyTaggedItem(kit.backpack, "BACKPACK_TAG", ownedOnly: true);
            ContainerHelper.AllowOnlyOwnedItems(kit.right);
            kit.window.Attach(new MachineryPanelRow(
                MachineHelper.Panel(null, kit.ground, "ground"),
                MachineHelper.Panel(LocHelper.GetLocalizedItem("item_expedition_box_label_pocket1"), kit.left, "pocket1"),
                MachineHelper.Panel(LocHelper.GetLocalizedItem("item_expedition_box_label_backpack"), kit.backpack, "backpack"),
                MachineHelper.Panel(LocHelper.GetLocalizedItem("item_expedition_box_label_pocket2"), kit.right, "pocket2")));
            kit.ground.mayInventoryAddItemFunc = delegate (GameItem item, GameInventory inv) {
                if (!GeneralHelper.IsItemOwned(item)) return false;
                if (!GeneralHelper.IsHazardousWaste(item)) return true;
                var place = kit.location != null ? AfterhourLocation.Get(kit.location) : null;
                return place != null && place.AllowsHazardousWaste;
            };
            kit.ground.slotPriorityFunc = (GameItem _, GameInventory __, SlotMarker ___) => 4;
            kit.window.Validate();
            kit.window.Hide();
            return kit;
        }

        /// <summary>Extra roots of the world replica: the host's own outing inventories, then every friend kit.</summary>
        internal static IEnumerable<KeyValuePair<int, GameInventory>> Roots() {
            var e = EmporiumEntry.Instance;
            if (e == null) yield break;
            GameInventory[] vanilla = { e.afterhourInventory, e.afterhourPocketSlotInvLeft, e.afterhourPocketSlotInvBackpack, e.afterhourPocketSlotInvRight };
            for (int i = 0; i < vanilla.Length; i++) if (vanilla[i] != null) yield return new KeyValuePair<int, GameInventory>(VanillaRoot + i, vanilla[i]);
            if (e.backInvinvElementCounter != null) yield return new KeyValuePair<int, GameInventory>(CounterRoot, e.backInvinvElementCounter);   // the back counter at Dr. Jackson's
            foreach (var kit in All) {
                int b = KitRoot + kit.slot * 4;
                yield return new KeyValuePair<int, GameInventory>(b, kit.ground);
                yield return new KeyValuePair<int, GameInventory>(b + 1, kit.left);
                yield return new KeyValuePair<int, GameInventory>(b + 2, kit.backpack);
                yield return new KeyValuePair<int, GameInventory>(b + 3, kit.right);
            }
        }

        internal static Kit Of(int owner) { if (owner <= 0) return null; foreach (var k in All) if (k.owner == owner) return k; return null; }
        /// <summary>Someone is still out, or still holds things in their outing slots that are not part of the save yet.</summary>
        internal static bool AnyOut { get { foreach (var k in All) if (Waiting(k)) return true; return false; } }
        /// <summary>A friend who left the game does not hold up the night: what they could not put away is placed in the
        /// shop as soon as there is room (it is retried every few seconds).</summary>
        static bool Waiting(Kit k) {
            if (k.owner == 0) return false;
            if (k.location != null) return true;
            var session = Connection.Current;
            return k.HasLeftovers && (session == null || session.HasPeer(k.owner));
        }
        internal static bool FriendAt(int owner, string place) { var k = Of(owner); return k != null && k.location == place; }

        /// <summary>Host side: whether a player may touch items under this replica root.</summary>
        internal static bool MayUse(int owner, int root) {
            if (owner == 0) return true;
            bool with = joined.Contains(owner);
            if (root == VanillaRoot) return with;                                           // the host's ground: only friends who came along
            if (root > VanillaRoot && root < VanillaRoot + 4) return false;                 // the host's own pockets and backpack
            if (root >= KitRoot && root < KitRoot + Slots * 4) { var k = All[(root - KitRoot) / 4]; return k.owner == owner && (k.location != null || k.HasLeftovers); }
            // The shop counters hold Dr. Jackson's stock while the host is visiting him: only friends who came along.
            var here = MapUIManager.Instance != null ? MapUIManager.Instance.Current : null;
            if (root == 2 || root == 3 || root == CounterRoot) return here == null || !here.ShowsStoreCounters ? root != CounterRoot : with;
            return true;
        }

        /// <summary>Host side: every item and place a friend's drag or sort request touches must be theirs to use.</summary>
        internal static bool RequestAllowed(Connection session, int owner, Request request) {
            if (owner == 0 || request == null) return true;
            var world = session.World;
            Func<int, bool> item = id => {
                GameItem found; if (id <= 0 || !world.Items.TryGetValue(id, out found) || found == null) return true;   // unknown ids are refused later
                var address = world.Address(found.parentInventory);
                return address == null || MayUse(owner, address.root);
            };
            Func<InventoryAddress, bool> place = a => a == null || MayUse(owner, a.root);
            // A void backpack is only a borrowed shape: it can be filled and emptied, never moved anywhere.
            Func<int, bool> moved = id => { GameItem found; return id > 0 && world.Items.TryGetValue(id, out found) && IsVoidBag(found); };
            if (request.items != null) foreach (int id in request.items) if (moved(id)) return false;
            if (request.moves != null) foreach (var m in request.moves) if (m != null && moved(m.item)) return false;
            if (request.items != null) foreach (int id in request.items) if (!item(id)) return false;
            if (!item(request.target) || !place(request.inventory)) return false;
            if (request.moves != null) foreach (var m in request.moves) if (m != null && (!item(m.item) || !item(m.target) || !place(m.inventory))) return false;
            return true;
        }

        // ---------------- host side ----------------

        static string note;
        internal static bool Capturing;
        internal static void Note(string text) {
            if (string.IsNullOrEmpty(text)) return;
            text = StripTags(text);
            note = string.IsNullOrEmpty(note) ? text : note + "　" + text;
        }
        static string StripTags(string s) {
            var b = new StringBuilder(); bool tag = false;
            foreach (char c in s) { if (c == '<') tag = true; else if (c == '>') tag = false; else if (!tag) b.Append(c); }
            return b.ToString();
        }
        static void Hand() { if (!string.IsNullOrEmpty(note)) RemoteActions.PendingNote = note; note = null; }

        // Like the shopkeeper, each friend goes out once per night.
        static readonly HashSet<string> outTonight = new HashSet<string>();   // by Steam account / name, so rejoining does not reset it
        static int outDay = -1;
        // A friend without a Steam link or a name is still someone: fall back to their number in this session.
        static string Who(int owner) { var s = Connection.Current; if (s == null || owner <= 0) return null; return s.PeerIdentity(owner) ?? "P" + owner; }
        internal static string Me(Connection s) { return s == null ? null : s.MyIdentity ?? (s.PlayerId > 0 ? "P" + s.PlayerId : null); }
        static void NewNight() { int day = StoreStation.Instance != null ? StoreStation.Instance.dayCounter : -1; if (day != outDay) { outDay = day; outTonight.Clear(); } }

        /// <summary>Host side: a friend leaves the shop for a location, taking the backpack they chose (or none).</summary>
        internal static void Go(string place, GameItem pack) {
            int owner = RemoteActions.CurrentOwner;
            var map = MapUIManager.Instance; var store = PlayerStore.Instance; var e = EmporiumEntry.Instance;
            if (owner <= 0 || place == null) return;
            if (map == null || store == null || e == null) { Note("~SMC203~"); Hand(); return; }
            if (!FriendMayGo(place)) { Note("~SMC202~"); Hand(); return; }
            NewNight();
            if (store.storeState != PlayerStore.StoreState.CLOSED || !store.isAfterhoursIntroduced) { Note("~SMC203~"); Hand(); return; }
            if (HealthData.IsSeriouslyWounded() || StartingPerkContent.IsAgoraphobiaResting() || (StoreStation.Instance != null && StoreStation.Instance.storeEventManager.IsEventActive("crackdown"))) { Note("~SMC203~"); Hand(); return; }
            string who = Who(owner);
            if (who == null) { Note("~SMC243~"); Hand(); return; }
            if (Of(owner) != null && Of(owner).location != null) { Note("~SMC241~"); Hand(); return; }
            if (outTonight.Contains(who)) { Note("~SMC204~"); Hand(); return; }
            var location = AfterhourLocation.Get(place);
            if (location == null || !location.IsDiscovered() || !location.IsAvailable()) { Note("~SMC205~"); Hand(); return; }
            bool hunted = StoreClientData.IsButcherHunting();
            if (Of(owner) != null && Of(owner).HasLeftovers) { Note("~SMC223~"); Hand(); return; }
            if (pack != null) {
                if (!GeneralHelper.IsItemOwned(pack) || !pack.IsTag("BACKPACK_TAG") || pack.parentInventory == null) { Note("~SMC224~"); Hand(); return; }
                var session = Connection.Current; var address = session != null ? session.World.Address(pack.parentInventory) : null;
                if (address == null || address.root >= VanillaRoot) { Note("~SMC224~"); Hand(); return; }
                if (hunted && !GunHelper.HasGunInside(pack)) { Note(LocHelper.GetLocalizedMechanic("response_afterhours_need_gun", LocHelper.GetLocalizedClientName("name_the_butcher"))); Hand(); return; }
            } else if (hunted) { Note(LocHelper.GetLocalizedMechanic("response_afterhours_need_gun", LocHelper.GetLocalizedClientName("name_the_butcher"))); Hand(); return; }
            var kit = Of(owner);
            if (kit == null) { foreach (var k in All) if (k.owner == 0) { kit = k; break; } }
            if (kit == null) { Note("~SMC207~"); Hand(); return; }
            GameItem bag = null;
            if (pack != null) {
                bag = NewVoidBag(pack);
                if (bag == null) { Note("~SMC224~"); Hand(); return; }
                GraphUtils.TryAcceptAll(kit.backpack, bag);
                if (bag.parentInventory != kit.backpack) { bag.Destroy(); Note("~SMC224~"); Hand(); return; }
            }
            kit.owner = owner; kit.location = place; kit.attempts = 0; outTonight.Add(who);
            var state = store.GetAfterhourState(place);
            if (state != null) { state.visits++; state.lastVisitDay = StoreStation.Instance != null ? StoreStation.Instance.dayCounter : state.lastVisitDay; }
            store.healthData.wentToAfterhoursLastNight = true;
            if (StoreStation.Instance != null) store.lastAfterhourOutingDay = StoreStation.Instance.dayCounter;
        }

        // Friends who came along with the host to a host-only place (Dr. Jackson, other mods' locations).
        static readonly HashSet<int> joined = new HashSet<int>();
        internal static bool IsJoined(int owner) { return owner > 0 && joined.Contains(owner); }

        /// <summary>Host side: a friend in the shop joins the host where the host is. This is that friend's outing tonight.</summary>
        internal static void Join() {
            int owner = RemoteActions.CurrentOwner; var map = MapUIManager.Instance; var store = PlayerStore.Instance;
            if (owner <= 0) return;
            if (joined.Contains(owner) || (Of(owner) != null && Of(owner).location != null)) { Note("~SMC241~"); Hand(); return; }
            if (map == null || store == null || map.Current == null || map.IsInTransit || FriendMayGo(map.Current.Id)) { Note("~SMC215~"); Hand(); return; }
            NewNight();
            string who = Who(owner);
            if (who == null) { Note("~SMC243~"); Hand(); return; }
            if (outTonight.Contains(who)) { Note("~SMC204~"); Hand(); return; }
            joined.Add(owner); outTonight.Add(who);
        }
        /// <summary>Host side: a friend who came along goes back to the shop; the host stays.</summary>
        internal static void Unjoin() { joined.Remove(RemoteActions.CurrentOwner); }

        /// <summary>Host side: the friend comes back. Like the base game, what is carried comes home and what is left on
        /// the ground stays there (the Serpico storyline still sees food left at the dumping ground).</summary>
        internal static void Back() { var kit = Of(RemoteActions.CurrentOwner); if (kit != null && kit.location != null) Return(kit, false); else { Note("~SMC247~"); Hand(); } }

        /// <summary>Puts one thing a friend brought back into the shop, where the base game puts what the shopkeeper
        /// brings home: the back counter, then the shelves, then the showcase, then the front counter.</summary>
        internal static bool Home(GameItem item) {
            var e = EmporiumEntry.Instance;
            if (e == null || item == null) return false;
            var from = item.parentInventory;
            // While the host is at a place that uses the shop counters (Dr. Jackson's), they are that place's counter:
            // things coming home go to the shop's storage instead, where a friend can reach them.
            var here = MapUIManager.Instance != null ? MapUIManager.Instance.Current : null;
            if (here != null && here.ShowsStoreCounters) {
                GraphUtils.TryAccept(e.invElement, item);
                return item.parentInventory != from;
            }
            e.TryAddToPlayerInv(item);
            if (item.parentInventory != from) return true;
            GraphUtils.TryAcceptAll(e.frontInvinvElement, item);
            return item.parentInventory != from;
        }

        /// <summary>An empty, owned backpack of the same kind as the chosen one: the borrowed shape, never its contents.</summary>
        internal static GameItem NewVoidBag(GameItem pack) {
            var bag = pack != null ? ItemSpawner.Spawn(pack.identifier) : null;
            if (bag == null) return null;
            foreach (var inside in Contents(bag)) { if (inside.parentInventory != null) inside.parentInventory.Expel(inside); inside.Destroy(); }
            GeneralHelper.SetItemOwned(bag, true);
            bag.EnableTag(VoidTag);
            return bag;
        }

        /// <summary>Empties a void backpack into the shop and removes it; false when something did not fit (it stays in the bag).</summary>
        internal static bool Unpack(GameItem bag, GameInventory holder) {
            foreach (var inside in Contents(bag)) Home(inside);
            if (Contents(bag).Count == 0) { holder.Expel(bag); bag.Destroy(); return true; }
            return false;
        }

        /// <summary>Host side: what the friend carries (backpack, pockets) returns to the shop and the empty void backpack
        /// disappears. What is left on the ground stays behind, as in the base game, unless the friend was brought back
        /// without choosing to leave (keepGround). Serpico and the waste count see the dumping ground first.
        /// Nothing carried is thrown away for lack of room: what did not fit stays in the friend's outing slots, which
        /// they see in the shop and empty by hand.</summary>
        static void Return(Kit kit, bool keepGround) {
            string place = kit.location;
            var location = place != null ? AfterhourLocation.Get(place) : null;
            bool left = false;
            try {
                foreach (var item in new List<GameItem>(kit.backpack.childItems)) {
                    if (!IsVoidBag(item)) { if (!Home(item)) left = true; continue; }
                    if (!Unpack(item, kit.backpack)) left = true;
                }
                foreach (var pocket in new GameInventory[] { kit.left, kit.right }) foreach (var item in new List<GameItem>(pocket.childItems)) { if (IsVoidBag(item)) { if (!Unpack(item, pocket)) left = true; } else if (!Home(item)) left = true; }
                // Disconnect/session shutdown is recovery, not a deliberate delivery at the dumping ground.
                if (!keepGround && place == "dumping_ground") { bool feeding = SerpicoStoryline.IsFoodWindowOpen(); SerpicoStoryline.OnLeaveDumpingGround(kit.ground); DumpingGround.FriendLeaves(kit.ground, feeding); }
                if (!keepGround) {
                    // As in the base game, whatever is left on the ground stays behind (a dropped bag with what is in it).
                    // Waste left at the dumping ground was counted above.
                    foreach (var item in new List<GameItem>(kit.ground.childItems)) {
                        foreach (var inside in GraphUtils.FindAllChildrenType<GameItem>(item)) if (!ReferenceEquals(inside, item) && inside.parentInventory != null) { inside.parentInventory.Expel(inside); inside.Destroy(); }
                        kit.ground.Expel(item); item.Destroy();
                    }
                } else foreach (var item in new List<GameItem>(kit.ground.childItems)) {
                    // Brought back without choosing to leave (disconnected, session ended): nothing they found is lost.
                    if (IsVoidBag(item)) { if (!Unpack(item, kit.ground)) left = true; continue; }
                    if (!Home(item)) left = true;
                }
            } catch (Exception error) { Debug.LogWarning("[ShopTogether] friend return: " + error); left = true; }
            bool wasOut = place != null;
            kit.location = null; kit.attempts = 0;
            if (!left) { kit.owner = 0; return; }
            if (wasOut) {
                Note("~SMC225~");
                if (StoreUIManager.Instance != null && Connection.Current != null) StoreUIManager.Instance.Notify(Loc.Text(Connection.Current.NameOf(kit.owner) + "~SMC226~"));
            }
        }

        /// <summary>Host side: one search at the dumping ground for a friend, with their own attempts and ground.</summary>
        internal static void Scavenge() {
            var kit = Of(RemoteActions.CurrentOwner); var e = EmporiumEntry.Instance; var store = PlayerStore.Instance;
            if (kit == null || kit.location != "dumping_ground" || e == null || store == null) { Note("~SMC247~"); Hand(); return; }
            // The game reads the shopkeeper's ground, pockets and attempts; for this search they are the friend's own
            // (tools they carry and their loot work exactly as in the base game).
            var ground = e.afterhourInventory; var left = e.afterhourPocketSlotInvLeft; var pack = e.afterhourPocketSlotInvBackpack; var right = e.afterhourPocketSlotInvRight;
            int attempts = store.scavengingAttempts, wounds = store.healthData.woundState, before = kit.attempts;
            Capturing = true;
            try {
                e.afterhourInventory = kit.ground; e.afterhourPocketSlotInvLeft = kit.left; e.afterhourPocketSlotInvBackpack = kit.backpack; e.afterhourPocketSlotInvRight = kit.right;
                store.scavengingAttempts = kit.attempts;
                if (!ScavHelper.CanScavenge()) Note(store.healthData.isWoundedFresh ? LocHelper.GetLocalizedMechanic("afterhour_attempts_wounded") : "~SMC208~");
                else ScavHelper.ScavengeDumpingGrounds();
                kit.attempts = store.scavengingAttempts;
            } finally {
                e.afterhourInventory = ground; e.afterhourPocketSlotInvLeft = left; e.afterhourPocketSlotInvBackpack = pack; e.afterhourPocketSlotInvRight = right;
                store.scavengingAttempts = attempts; Capturing = false;
            }
            if (kit.attempts != before && Connection.Current != null) Connection.Current.SyncGroundSoon(kit.ground);
            Hand();
            // The body is shared: the host is told when a friend got hurt.
            if (store.healthData.woundState > wounds && StoreUIManager.Instance != null && Connection.Current != null)
                StoreUIManager.Instance.Notify(Loc.Text(Connection.Current.NameOf(RemoteActions.CurrentOwner) + "~SMC209~"), "sec");
        }

        /// <summary>Host side: things bought or found for a friend land on that friend's ground.</summary>
        internal static GameGridInventory GroundFor(int owner) { var k = Of(owner); return k != null && k.location != null ? k.ground : null; }

        /// <summary>Host side, every frame: a friend who disconnected comes home with everything (nothing is lost).</summary>
        internal static void HostTick(Connection session) {
            // Whoever came along returns with the host, or when they disconnect.
            if (MapUIManager.Instance == null || MapUIManager.Instance.Current == null) joined.Clear();
            else joined.RemoveWhere(id => !session.HasPeer(id));
            if (kits == null) return;
            bool retry = Time.realtimeSinceStartup >= nextRetry;
            if (retry) nextRetry = Time.realtimeSinceStartup + 5f;
            foreach (var k in kits) {
                if (k.owner == 0) continue;
                if (k.location == null && !k.HasLeftovers) {
                    // Back home and emptied by hand: the void backpack goes, the slot is free.
                    foreach (var bag in new List<GameItem>(k.backpack.childItems)) if (IsVoidBag(bag)) { k.backpack.Expel(bag); bag.Destroy(); }
                    k.owner = 0; continue;
                }
                if (!session.HasPeer(k.owner) && (k.location != null || retry)) Return(k, true);
            }
        }
        static float nextRetry;
        internal static void ReturnEveryone() { if (kits == null) return; foreach (var k in kits) if (k.owner != 0) Return(k, true); }

        internal static string OutNames(Connection session) {
            var names = new List<string>();
            foreach (var k in All) if (Waiting(k)) names.Add((session != null ? session.NameOf(k.owner) : "~SMC210~") + (k.location == null ? "~SMC236~" : ""));
            return string.Join("、", names.ToArray());
        }

        internal static void Capture(PhaseState state) {
            if (kits == null) return;
            NewNight(); state.outTonight = new List<string>(outTonight).ToArray(); state.joined = new List<int>(joined).ToArray();
            state.kitOwners = new int[Slots]; state.kitPlaces = new string[Slots]; state.kitLeft = new int[Slots];
            int max = 0; try { max = ScavHelper.GetMaxScavAttempts(); } catch { }
            foreach (var k in kits) { state.kitOwners[k.slot] = k.owner; state.kitPlaces[k.slot] = k.location; state.kitLeft[k.slot] = Math.Max(0, max - k.attempts); }
        }

        internal static void Reset() { joined.Clear(); outTonight.Clear(); outDay = -1; foreach (var k in kits ?? new Kit[0]) { k.owner = 0; k.location = null; k.attempts = 0; try { k.window.Hide(); } catch { } } }

        // ---------------- friend side ----------------

        static readonly MethodInfo showStage = typeof(MapUIManager).GetMethod("ShowStage", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo hideStage = typeof(MapUIManager).GetMethod("HideStage", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo openPanel = typeof(MapUIManager).GetMethod("OpenLocationPanel", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo closePanel = typeof(MapUIManager).GetMethod("CloseLocationPanel", BindingFlags.NonPublic | BindingFlags.Instance);

        static string here;          // where this friend's screen is
        internal static int MyLeft;  // scavenging attempts left for this friend
        internal static bool WentOut; // this friend already went out tonight
        internal static string Here { get { return here; } }
        internal static Kit Mine { get { var s = Connection.Current; return s == null ? null : Of(s.PlayerId); } }

        internal static void Follow(PhaseState s) {
            var session = Connection.Current;
            if (session == null || !ReplicaSession.SceneReady || EmporiumEntry.Instance == null || MapUIManager.Instance == null || s.kitOwners == null || s.kitPlaces == null || s.kitLeft == null) return;
            var all = All; string place = null; int left = 0;
            for (int i = 0; i < Slots && i < s.kitOwners.Length && i < s.kitPlaces.Length && i < s.kitLeft.Length; i++) {
                all[i].owner = s.kitOwners[i]; all[i].location = s.kitPlaces[i];
                if (s.kitOwners[i] == session.PlayerId && s.kitOwners[i] != 0) { place = s.kitPlaces[i]; left = s.kitLeft[i]; }
            }
            bool leftChanged = left != MyLeft; MyLeft = left;
            string me = Me(session);
            WentOut = s.outTonight != null && me != null && Array.IndexOf(s.outTonight, me) >= 0;
            if (place != here) {
                string leaving = here; here = place;
                var ui = StoreUIManager.Instance;
                Action change = () => { if (leaving != null) Visual(() => Leave(leaving)); if (place != null) Visual(() => Enter(place)); };
                if (ui != null) ui.StartBlackscreenSequence(true, change, null); else change();
            } else if (leftChanged && here == "dumping_ground") Visual(() => MapUIManager.Instance.UpdateScavUI());

            // Along with the host at a host-only place.
            bool with = place == null && s.location != null && s.joined != null && Array.IndexOf(s.joined, session.PlayerId) >= 0;
            string target = with ? s.location : null;
            if (target != joinedAt) {
                string leaving = joinedAt; joinedAt = target; mirrorSig = null;
                var ui = StoreUIManager.Instance; var snapshot = s;
                Action change = () => { if (leaving != null) Visual(() => LeaveHost(leaving)); if (target != null && joinedAt == target) Visual(() => JoinHost(snapshot)); };
                if (ui != null) ui.StartBlackscreenSequence(true, change, null); else change();
            } else if (joinedAt != null && !s.native && s.panelSig != mirrorSig) { var snapshot = s; Visual(() => Mirror(snapshot)); }

            // Back in the shop with things that did not fit: this friend's outing slots stay on screen until emptied.
            foreach (var kit in all) if (kit.owner != session.PlayerId && kit.window.IsVisible()) Visual(() => kit.window.Hide());
            var mine = Mine;
            bool leftovers = place == null && joinedAt == null && mine != null && mine.HasLeftovers;
            if (place == null && joinedAt == null && mine != null && leftovers != mine.window.IsVisible()) Visual(() => {
                if (leftovers) { mine.window.Show(); mine.window.Validate(); mine.window.Center(); mine.window.ToFront(true); } else mine.window.Hide();
            });

            // While the host is out, the visitor at the host's counter (Dr. Jackson) is not standing in this shop.
            if (s.location != null && StoreUIManager.Instance != null && StoreUIManager.Instance.clientGameObject != null && StoreUIManager.Instance.clientGameObject.gameObject.activeSelf)
                Visual(() => StoreUIManager.Instance.HideClient());
        }

        static string joinedAt, mirrorSig;
        internal static string Joined { get { return joinedAt; } }
        internal const string MirrorWindow = "shop_together.outing";
        static readonly MethodInfo ensureTablet = typeof(DrJacksonLocation).GetMethod("EnsureSheetTablet", BindingFlags.NonPublic | BindingFlags.Instance);

        static void JoinHost(PhaseState s) {
            var location = AfterhourLocation.Get(s.location); var map = MapUIManager.Instance; var e = EmporiumEntry.Instance;
            if (TransitUI.IsOpen) TransitUI.Close();
            map.CloseUI(false);
            if (PaperUIManager.Instance != null) PaperUIManager.Instance.CloseAllPaperUI();
            e.CloseItemWindowUIWindow();
            e.OnLeaveStore();
            if (location != null && showStage != null) showStage.Invoke(map, new object[] { location });
            if (s.bag) e.ShowAfterhourInv();            // the host's ground (synced); pockets stay the host's
            if (location != null && s.native && location.PanelObject != null) location.PanelObject.SetActive(true);
            if (location != null && location.ShowsStoreCounters) {
                e.frontInvWindow.Show(); e.backInvWindow.Show(); e.Validate(true);
                map.SetCreditDisplayPositionToDrJackson();
                if (StoreUIManager.Instance != null) StoreUIManager.Instance.storeCreditDisplay.creditDisplayText.gameObject.SetActive(true);
                if (location is DrJacksonLocation) { PlayerStore.Instance.playerLocation=PlayerStore.PlayerLocation.INVENTOR; if(ensureTablet != null) ensureTablet.Invoke(location, null); }
            }
            if (!s.native) Mirror(s);
            if (StoreShutterButton.Instance != null) StoreShutterButton.Instance.blockerGameObject.transform.parent.gameObject.SetActive(false);
            AudioManager.Instance.Play2("open_door");
        }

        static void LeaveHost(string place) {
            var location = AfterhourLocation.Get(place); var map = MapUIManager.Instance; var e = EmporiumEntry.Instance;
            if (CustomUIManager.Instance != null) CustomUIManager.Instance.CloseWindow(MirrorWindow);
            if (NegociationUIManager.Instance != null && NegociationUIManager.Instance.IsOpen) NegociationUIManager.Instance.CloseUI();
            if (location != null && location.PanelObject != null) location.PanelObject.SetActive(false);
            if (hideStage != null) hideStage.Invoke(map, null);
            e.HideAfterhourInv();
            if (location != null && location.ShowsStoreCounters) map.ResetCreditDisplayPosition();
            if (PaperUIManager.Instance != null) PaperUIManager.Instance.CloseAllPaperUI();
            e.CloseItemWindowUIWindow();
            if (StoreShutterButton.Instance != null) StoreShutterButton.Instance.blockerGameObject.transform.parent.gameObject.SetActive(true);
            e.OnArriveStore(fromMap: true);
            e.RestoreStoreUIAfterReturn();
            e.Validate(true);
            AudioManager.Instance.Play2("close_door");
        }

        /// <summary>A location panel built in code (other mods) is shown as the host's texts and buttons; a button press
        /// is done on the host's own panel.</summary>
        static void Mirror(PhaseState s) {
            mirrorSig = s.panelSig;
            if (CustomUIManager.Instance == null) return;
            CustomUIManager.Instance.CloseWindow(MirrorWindow);
            var builder = CustomUIManager.Window(MirrorWindow, Loc.Text(string.IsNullOrEmpty(s.title) ? s.location : s.title), "meta");
            string returnText = null;
            try { returnText = LocHelper.GetLocalizedUI("afterhour_return_action"); } catch { }
            int buttonIndex = 0;
            if (s.lines != null && s.buttons != null && s.enabled != null)
                for (int i = 0; i < s.lines.Length && i < s.buttons.Length && i < s.enabled.Length; i++) {
                    string text = s.lines[i] ?? "";
                    if (!s.buttons[i]) { builder.AddLabel(Loc.Text(text)); continue; }
                    int index = buttonIndex++;
                    string sig = s.panelSig;
                    if (!s.enabled[i]) builder.AddLabel(Loc.Text("（" + text + "）"));
                    else if (returnText != null && text == returnText) builder.AddButton(Loc.Text(text), () => MapUIManager.Instance.Return());
                    else builder.AddButton(Loc.Text(text), () => RemoteActions.Forward(RemoteActions.Adapter("Press", 2), null, new object[] { sig, index }));
                }
            builder.SetPosition(MapUIManager.Instance.panelPosition);
            builder.Show();
        }

        static void Visual(Action action) {
            bool was = WorldReplica.Applying; WorldReplica.Applying = true;
            try { action(); } catch (Exception e) { Debug.LogWarning("[ShopTogether] friend outing view: " + e.Message); } finally { WorldReplica.Applying = was; }
        }

        static void Enter(string place) {
            shown = place;
            var location = AfterhourLocation.Get(place); var map = MapUIManager.Instance; var e = EmporiumEntry.Instance;
            if (TransitUI.IsOpen) TransitUI.Close();
            map.CloseUI(false);
            if (PaperUIManager.Instance != null) PaperUIManager.Instance.CloseAllPaperUI();
            e.CloseItemWindowUIWindow();
            e.OnLeaveStore();
            if (location != null && showStage != null) showStage.Invoke(map, new object[] { location });
            var kit = Mine;
            if (kit != null && (location == null || location.UsesAfterhourBag)) {
                kit.window.Show(); kit.window.Validate();
                if (e.afterhourWindow != null) kit.window.rectTransform.anchoredPosition = e.afterhourWindow.rectTransform.anchoredPosition;
            }
            if (location != null && openPanel != null) openPanel.Invoke(map, new object[] { location });
            if (place == "dumping_ground") map.UpdateScavUI();
            if (StoreShutterButton.Instance != null) StoreShutterButton.Instance.blockerGameObject.transform.parent.gameObject.SetActive(false);
            AudioManager.Instance.Play2("open_door");
        }

        static string shown;   // the place this friend's screen shows
        static void Leave(string place) {
            shown = null;
            var map = MapUIManager.Instance; var e = EmporiumEntry.Instance;
            if (place == "commissary" && CommissaryUIManager.Instance != null) CommissaryUIManager.Instance.CloseUI();
            if (closePanel != null) closePanel.Invoke(map, null);
            if (hideStage != null) hideStage.Invoke(map, null);
            foreach (var k in All) k.window.Hide();
            if (PaperUIManager.Instance != null) PaperUIManager.Instance.CloseAllPaperUI();
            e.CloseItemWindowUIWindow();
            if (StoreShutterButton.Instance != null) StoreShutterButton.Instance.blockerGameObject.transform.parent.gameObject.SetActive(true);
            e.OnArriveStore(fromMap: true);
            e.RestoreStoreUIAfterReturn();
            // Returning runs later in the blackscreen callback, after Follow may already have shown leftovers.
            // Reopen after the native UI cleanup so a completed transition cannot hide the recovery panel.
            var ownKit = Mine;
            if (ownKit != null && ownKit.HasLeftovers) { ownKit.window.Show(); ownKit.window.Validate(); ownKit.window.Center(); ownKit.window.ToFront(true); }
            AudioManager.Instance.Play2("close_door");
        }

        /// <summary>Friend: the screen still shows a place the host no longer has this friend at (a missed update). Back to the shop.</summary>
        internal static bool LeaveStale() {
            if (here != null || joinedAt != null || shown == null || MapUIManager.Instance == null || EmporiumEntry.Instance == null) return false;
            string at = shown; Visual(() => Leave(at)); return true;
        }

        internal static void ResetView() {
            if (here != null && EmporiumEntry.Instance != null && MapUIManager.Instance != null) Visual(() => Leave(here));
            if (joinedAt != null && EmporiumEntry.Instance != null && MapUIManager.Instance != null) { string at = joinedAt; joinedAt = null; Visual(() => LeaveHost(at)); }
            here = null; joinedAt = null; mirrorSig = null; MyLeft = 0; shown = null;
            if (kits != null) foreach (var k in kits) { k.owner = 0; k.location = null; }
        }
    }

    // ---------------- host-side hooks ----------------

    /// <summary>While a friend's action runs on the host, its messages and sounds go to that friend instead.</summary>
    [HarmonyPatch(typeof(StoreUIManager), "Notify")]
    internal static class FriendNotify {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(string message) { if (!OutingKits.Capturing) return true; OutingKits.Note(message); return false; }
    }
    [HarmonyPatch(typeof(AudioManager), "Play2")]
    internal static class FriendSound {
        [HarmonyPriority(Priority.First)]
        static bool Prefix() { return !OutingKits.Capturing; }
    }

    /// <summary>Host side: a friend's purchase at the commissary lands on that friend's own ground.</summary>
    [HarmonyPatch(typeof(EmporiumEntry), "TryAddToAfterhourInv")]
    internal static class FriendAfterhourAdd {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(GameItem gameItem) {
            var session = Connection.Current;
            if (session == null || !session.IsHost || RemoteActions.CurrentOwner <= 0) return true;
            var ground = OutingKits.GroundFor(RemoteActions.CurrentOwner);
            if (ground != null) GraphUtils.TryAccept(ground, gameItem);
            else if (EmporiumEntry.Instance != null) EmporiumEntry.Instance.TryAddToPlayerInv(gameItem);   // never lost
            return false;
        }
    }

    /// <summary>
    /// Host side, while playing together: the host goes out with a void backpack too. The chosen backpack only lends its
    /// look and size and stays in the shop with everything in it; what the host carries back comes into the shop and the
    /// void backpack disappears. A void backpack never becomes a shop item.
    /// </summary>
    [HarmonyPatch(typeof(EmporiumEntry), "TransferBackpackToInv")]
    internal static class HostVoidBackpack {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(EmporiumEntry __instance) {
            var session = Connection.Current;
            if (session == null || !session.IsHost) return true;
            var pack = __instance.selectedBackpack; __instance.selectedBackpack = null;
            var slot = __instance.afterhourPocketSlotInvBackpack;
            if (slot == null) return false;
            // A void backpack still holding what did not fit last time: empty it into the shop first.
            foreach (var old in new List<GameItem>(slot.childItems)) if (OutingKits.IsVoidBag(old)) OutingKits.Unpack(old, slot);
            if (pack == null || pack.parentInventory == null || slot.childItems.Count > 0) return false;
            var bag = OutingKits.NewVoidBag(pack);
            if (bag == null) return false;
            GraphUtils.TryAcceptAll(slot, bag);
            if (bag.parentInventory != slot) bag.Destroy();
            return false;
        }
    }
    [HarmonyPatch(typeof(EmporiumEntry), "TransferAfterhourToInv")]
    internal static class HostVoidBackpackBack {
        static bool pendingGround;
        internal static bool HasLeftovers {
            get {
                var e = EmporiumEntry.Instance;
                if (!pendingGround || e == null) return false;
                if (e.afterhourInventory.childItems.Count > 0 || e.afterhourPocketSlotInvBackpack.childItems.Count > 0 || e.afterhourPocketSlotInvLeft.childItems.Count > 0 || e.afterhourPocketSlotInvRight.childItems.Count > 0) return true;
                pendingGround = false; return false;
            }
        }
        [HarmonyPriority(Priority.First)]
        static bool Prefix(EmporiumEntry __instance, bool countsDumpedWaste) {
            var session = Connection.Current;
            if (session == null || !session.IsHost) return true;
            foreach (var holder in new GameInventory[] { __instance.afterhourPocketSlotInvBackpack, __instance.afterhourPocketSlotInvLeft, __instance.afterhourPocketSlotInvRight })
                if (holder != null) foreach (var bag in new List<GameItem>(holder.childItems)) if (OutingKits.IsVoidBag(bag)) OutingKits.Unpack(bag, holder);
            // A void backpack dropped on the ground stays behind with whatever is in it, like anything left there.
            if (__instance.afterhourInventory != null)
                foreach (var bag in new List<GameItem>(__instance.afterhourInventory.childItems)) if (OutingKits.IsVoidBag(bag)) {
                    foreach (var inside in OutingKits.Contents(bag)) { if (inside.parentInventory != null) inside.parentInventory.Expel(inside); inside.Destroy(); }
                    __instance.afterhourInventory.Expel(bag); bag.Destroy();
                }
            // Then the base-game return: pockets and backpack come home, the ground stays behind (waste on the dumping
            // ground counted as dumped). What stays behind is destroyed here rather than only taken out of the ground.
            foreach (var item in new List<GameItem>(__instance.afterhourInventory.childItems)) {
                if (countsDumpedWaste && item.identifier == "nuclear_waste") PlayerStore.GetStoreClientData().nuclearWasteDumped++;
                foreach (var inside in GraphUtils.FindAllChildrenType<GameItem>(item)) if (!ReferenceEquals(inside, item) && inside.parentInventory != null) { inside.parentInventory.Expel(inside); inside.Destroy(); }
                __instance.afterhourInventory.Expel(item); item.Destroy();
            }
            return true;
        }
        static void Postfix(EmporiumEntry __instance) {
            var session = Connection.Current;
            if (session == null || !session.IsHost) return;
            // The native return can leave carried items in their outing slots when the shop is full.
            // Keep the sleep guard active until the host has moved every remaining item into the shop.
            pendingGround = __instance.afterhourInventory.childItems.Count > 0 ||
                __instance.afterhourPocketSlotInvBackpack.childItems.Count > 0 ||
                __instance.afterhourPocketSlotInvLeft.childItems.Count > 0 ||
                __instance.afterhourPocketSlotInvRight.childItems.Count > 0;
            if (pendingGround && StoreUIManager.Instance != null) StoreUIManager.Instance.Notify(Loc.Text("~SMC225~"));
        }
    }
    [HarmonyPatch(typeof(MapUIManager), "OnReturning")]
    internal static class HostAfterhourReturnWindow {
        static void Postfix() {
            var session = Connection.Current;
            if (session != null && session.IsHost && HostVoidBackpackBack.HasLeftovers && EmporiumEntry.Instance != null)
                EmporiumEntry.Instance.ShowAfterhourInv();
        }
    }
    [HarmonyPatch(typeof(EmporiumEntry), "TryAddToPlayerInv")]
    internal static class VoidBagStaysOut {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(GameItem gameItem) { return !OutingKits.IsVoidBag(gameItem); }
    }

    /// <summary>Host side: nobody sleeps while someone is still out.</summary>
    [HarmonyPatch(typeof(EndOfDayUIManager), "OnEndDayButtonClick")]
    internal static class HostSleepWaits {
        [HarmonyPriority(Priority.First)]
        static bool Prefix() {
            var session = Connection.Current;
            if (session == null || !session.IsHost) return true;
            if (HostVoidBackpackBack.HasLeftovers) { if (StoreUIManager.Instance != null) StoreUIManager.Instance.Notify(Loc.Text("~SMC225~")); return false; }
            if (!OutingKits.AnyOut) return true;
            if (StoreUIManager.Instance != null) StoreUIManager.Instance.Notify(Loc.Text("~SMC211~" + OutingKits.OutNames(session) + "~SMC212~"));
            return false;
        }
    }

    // ---------------- friend-side interception ----------------

    [HarmonyPatch(typeof(ScavHelper), "ScavengeDumpingGrounds")]
    internal static class FriendScavenge {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(out int __state) {
            __state = int.MinValue;
            if (!ReplicaSession.ClientStore || WorldReplica.Applying) {
                var session = Connection.Current;
                if (session != null && session.IsHost && !OutingKits.Capturing && PlayerStore.Instance != null) __state = PlayerStore.Instance.scavengingAttempts;
                return true;
            }
            if (!LocalControl.UserAction) return false;
            if (ReplicaSession.IsReplica && OutingKits.Here == "dumping_ground") RemoteActions.Forward(RemoteActions.Adapter("Scavenge", 0), null, new object[0]);
            else RemoteActions.Notice("~SMC247~");
            return false;
        }
        static void Postfix(int __state) {
            var session = Connection.Current;var store = PlayerStore.Instance;var entry = EmporiumEntry.Instance;
            if (__state != int.MinValue && session != null && session.IsHost && !OutingKits.Capturing && store != null && entry != null && store.scavengingAttempts != __state)
                session.SyncGroundSoon(entry.afterhourInventory);
        }
    }

    /// <summary>Friend side: the shop door counts this friend's own outing tonight, not the shopkeeper's.</summary>
    [HarmonyPatch]
    internal static class FriendDoor {
        static IEnumerable<MethodBase> TargetMethods() { yield return typeof(StoreDoor).GetMethod("OnClick"); yield return typeof(StoreDoor).GetMethod("GetTooltip"); }
        static void Prefix(ref int __state) {
            __state = int.MinValue;
            var store = PlayerStore.Instance;
            if (!ReplicaSession.ClientStore || store == null) return;
            __state = store.visitLeftTonight; store.visitLeftTonight = OutingKits.WentOut ? 0 : 1;
        }
        static void Postfix(int __state) { if (__state != int.MinValue && PlayerStore.Instance != null) PlayerStore.Instance.visitLeftTonight = __state; }
    }

    /// <summary>Friend side: along with the host at Dr. Jackson's, the counters sit where his place puts them.</summary>
    [HarmonyPatch]
    internal static class FriendCounterPlace {
        static IEnumerable<MethodBase> TargetMethods() { yield return typeof(EmporiumEntry).GetMethod("GetFrontInvPosition"); yield return typeof(EmporiumEntry).GetMethod("GetBackInvPosition"); }
        static bool Prefix(MethodBase __originalMethod, int newCanvasWidth, int newCanvasHeight, ref Vector2 __result) {
            if (!ReplicaSession.ClientStore || OutingKits.Joined == null) return true;
            var location = AfterhourLocation.Get(OutingKits.Joined);
            if (location == null) return true;
            Vector2? place = __originalMethod.Name == "GetFrontInvPosition" ? location.GetFrontCounterPosition(newCanvasWidth, newCanvasHeight) : location.GetBackCounterPosition(newCanvasWidth, newCanvasHeight);
            if (!place.HasValue) return true;
            __result = place.Value; return false;
        }
    }

    [HarmonyPatch(typeof(ScavHelper), "GetScavTimeLeft")]
    internal static class FriendScavLeft {
        static bool Prefix(ref int __result) {
            if (!ReplicaSession.ClientStore || OutingKits.Here == null) return true;
            __result = OutingKits.MyLeft; return false;
        }
    }
}
