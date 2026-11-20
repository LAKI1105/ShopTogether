using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// Night outings are the base game's: one outing per night, and everyone goes. Anyone may open the door, choose the
    /// backpack and the place, or come back; the host's game does it and every screen goes along, showing the same place,
    /// ground, pockets and backpack. What anyone does there (moving loot, the location's buttons, searching the dumping
    /// ground) is done by the host's game.
    /// </summary>
    internal static class OutingKits {
        internal const int VanillaRoot = 40, CounterRoot = 44;           // root numbers in the world replica

        /// <summary>Extra roots of the world replica: the outing ground, pockets and backpack, and Dr. Jackson's counter.</summary>
        internal static IEnumerable<KeyValuePair<int, GameInventory>> Roots() {
            var e = EmporiumEntry.Instance;
            if (e == null) yield break;
            GameInventory[] vanilla = { e.afterhourInventory, e.afterhourPocketSlotInvLeft, e.afterhourPocketSlotInvBackpack, e.afterhourPocketSlotInvRight };
            for (int i = 0; i < vanilla.Length; i++) if (vanilla[i] != null) yield return new KeyValuePair<int, GameInventory>(VanillaRoot + i, vanilla[i]);
            if (e.backInvinvElementCounter != null) yield return new KeyValuePair<int, GameInventory>(CounterRoot, e.backInvinvElementCounter);
        }

        static bool HostOut { get { var map = MapUIManager.Instance; return map != null && map.Current != null; } }

        /// <summary>Host side: whether a player may touch items under this replica root. The outing slots are only in use
        /// while the host is out; then everyone may handle them, as on the host's own screen.</summary>
        internal static bool MayUse(int owner, int root) {
            if (owner == 0) return true;
            if (root >= VanillaRoot && root <= CounterRoot) return HostOut;
            return true;
        }

        /// <summary>Host side: every item and place a friend's drag or sort request touches must be usable now.</summary>
        internal static bool RequestAllowed(Connection session, int owner, Request request) {
            if (owner == 0 || request == null) return true;
            var world = session.World;
            Func<int, bool> item = id => {
                GameItem found; if (id <= 0 || !world.Items.TryGetValue(id, out found) || found == null) return true;   // unknown ids are refused later
                var address = world.Address(found.parentInventory);
                return address == null || MayUse(owner, address.root);
            };
            Func<InventoryAddress, bool> place = a => a == null || MayUse(owner, a.root);
            if (request.items != null) foreach (int id in request.items) if (!item(id)) return false;
            if (!item(request.target) || !place(request.inventory)) return false;
            if (request.moves != null) foreach (var m in request.moves) if (m != null && (!item(m.item) || !item(m.target) || !place(m.inventory))) return false;
            return true;
        }

        /// <summary>Host side: whether the host is at this place right now.</summary>
        internal static bool HostAt(string place) { var map = MapUIManager.Instance; return map != null && map.Current != null && map.Current.Id == place; }

        /// <summary>Host side: a friend's search at the dumping ground is the host's own search (same attempts, same ground).</summary>
        internal static void Scavenge() {
            if (!HostAt("dumping_ground") || MapUIManager.Instance.IsInTransit) { RemoteActions.PendingNote = "~SMC247~"; return; }
            if (!ScavHelper.CanScavenge()) { RemoteActions.PendingNote = PlayerStore.Instance != null && PlayerStore.Instance.healthData.isWoundedFresh ? LocHelper.GetLocalizedMechanic("afterhour_attempts_wounded") : "~SMC208~"; return; }
            ScavHelper.ScavengeDumpingGrounds();
            var e = EmporiumEntry.Instance;
            if (e != null && Connection.Current != null) Connection.Current.SyncGroundSoon(e.afterhourInventory);
        }

        /// <summary>Host, once per session: earlier versions sent players out with an empty "void backpack" copy. Any copy
        /// still in the save is taken apart (what is in it goes into the shop) so it does not become a free backpack.</summary>
        internal static void RemoveOldVoidBags(Connection session) {
            if (cleaned || session == null || !session.IsHost || !session.Ready || EmporiumEntry.Instance == null) return;
            cleaned = true;
            var bags = new List<GameItem>();
            foreach (var item in session.World.Items.Values) if (item != null && item.IsTag(OldVoidTag)) bags.Add(item);
            foreach (var bag in bags) {
                foreach (var inside in GraphUtils.FindAllChildrenType<GameItem>(bag)) {
                    if (inside == null || ReferenceEquals(inside, bag) || inside.parentInventory == null || !ReferenceEquals(GraphUtils.FindParentType<GameItem>(inside.parentInventory), bag)) continue;
                    inside.parentInventory.Expel(inside);
                    EmporiumEntry.Instance.TryAddToPlayerInv(inside);
                }
                if (bag.parentInventory != null) bag.parentInventory.Expel(bag);
                bag.Destroy();
            }
            if (bags.Count > 0) Debug.Log("[ShopTogether] removed " + bags.Count + " void backpack copies left by an earlier version");
        }
        const string OldVoidTag = "SHOPTOGETHER_VOID_BAG";
        static bool cleaned;
        internal static void ResetHost() { cleaned = false; }

        /// <summary>Host side: anyone may take everyone out, with the same checks as the game's own door and backpack
        /// choice. The backpack is one of the shop's.</summary>
        internal static void GoOut(string place, GameItem pack) {
            var map = MapUIManager.Instance; var store = PlayerStore.Instance; var e = EmporiumEntry.Instance; var station = StoreStation.Instance;
            if (map == null || store == null || e == null || station == null || place == null) { RemoteActions.PendingNote = "~SMC203~"; return; }
            if (map.Current != null || map.IsInTransit) { RemoteActions.PendingNote = "~SMC079~"; return; }
            bool door = store.devMode || (!HealthData.IsSeriouslyWounded() && !station.storeEventManager.IsEventActive("crackdown") && !StartingPerkContent.IsAgoraphobiaResting() &&
                store.isAfterhoursIntroduced && store.storeState == PlayerStore.StoreState.CLOSED && store.visitLeftTonight > 0);
            if (!door) { RemoteActions.PendingNote = "~SMC203~"; return; }
            var location = AfterhourLocation.Get(place);
            if (location == null || !location.IsDiscovered() || !location.IsAvailable()) { RemoteActions.PendingNote = "~SMC205~"; return; }
            string needGun = LocHelper.GetLocalizedMechanic("response_afterhours_need_gun", LocHelper.GetLocalizedClientName("name_the_butcher"));
            if (pack != null) {
                var session = Connection.Current; var at = session != null ? session.World.Address(pack.parentInventory) : null;
                if (!GeneralHelper.IsItemOwned(pack) || !pack.IsTag("BACKPACK_TAG") || at == null || (at.root >= VanillaRoot && at.root <= CounterRoot)) { RemoteActions.PendingNote = "~SMC224~"; return; }
                if (!AuthorityIntent.FreeTree(session, Math.Max(0, RemoteActions.CurrentOwner), pack)) return;
                if (StoreClientData.IsButcherHunting() && !GunHelper.HasGunInside(pack)) { RemoteActions.PendingNote = needGun; return; }
            } else if (StoreClientData.IsButcherHunting()) { RemoteActions.PendingNote = needGun; return; }
            e.selectedBackpack = pack;
            map.Travel(location);
        }

        // Capture the warning and affected inventory on the host. A later confirmation
        // may not discard a different ground or move an item held by another player.
        internal static string GroundStamp() {
            var e = EmporiumEntry.Instance; var session = Connection.Current;
            if (e == null || e.afterhourInventory == null) return "";
            var b = new System.Text.StringBuilder();
            foreach (var item in e.afterhourInventory.childItems) b.Append(AuthorityIntent.TreeStamp(session, item)).Append(';');
            return b.ToString();
        }
        internal static bool ReturnAvailable() {
            var map = MapUIManager.Instance; var session = Connection.Current;
            if (map == null || map.Current == null || map.IsInTransit) return AuthorityIntent.Refuse("~SMC124~");
            if (DialogueIntent.Committing) return AuthorityIntent.Refuse("~SMC353~");
            foreach (var root in Roots()) foreach (var item in root.Value.childItems)
                if (!AuthorityIntent.FreeTree(session, Math.Max(0, RemoteActions.CurrentOwner), item)) return false;
            if (map.Current.ShowsStoreCounters) {
                foreach (var inventory in new[] { EmporiumEntry.Instance.frontInvinvElement, EmporiumEntry.Instance.backInvinvElement })
                    foreach (var item in inventory.childItems)
                        if (!AuthorityIntent.FreeTree(session, Math.Max(0, RemoteActions.CurrentOwner), item)) return false;
            }
            return true;
        }
        internal static void ReturnHome() {
            if (!ReturnAvailable()) return;
            var map = MapUIManager.Instance; var location = map.Current;
            string stamp = GroundStamp();
            Action leave = () => {
                if (map.Current != location || stamp != GroundStamp()) { AuthorityIntent.Refuse("~SMC326~"); if (RemoteActions.CurrentOwner < 0) RemoteActions.Notice("~SMC326~"); return; }
                if (ReturnAvailable()) map.ReturnNow();
            };
            if (EmporiumEntry.Instance.afterhourInventory.childItems.Count == 0) { leave(); return; }
            var confirm = GenericConfirmationUIManager.Instance;
            if (confirm == null) { AuthorityIntent.Refuse("~SMC326~"); return; }
            string warning = location.GetLeaveWarning();
            if (string.IsNullOrEmpty(warning)) warning = LocHelper.GetLocalizedMechanic("afterhour_items_lost_warning");
            confirm.OpenPanel(warning, LocHelper.GetLocalizedMechanic("afterhour_confirm_leave"), LocHelper.GetLocalizedMechanic("afterhour_confirm_cancel"),
                () => { confirm.ClosePanel(); leave(); }, () => confirm.ClosePanel());
        }

        // ---------------- friend side: showing where the host is ----------------

        static readonly MethodInfo showStage = typeof(MapUIManager).GetMethod("ShowStage", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo hideStage = typeof(MapUIManager).GetMethod("HideStage", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo openPanel = typeof(MapUIManager).GetMethod("OpenLocationPanel", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo closePanel = typeof(MapUIManager).GetMethod("CloseLocationPanel", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo ensureTablet = typeof(DrJacksonLocation).GetMethod("EnsureSheetTablet", BindingFlags.NonPublic | BindingFlags.Instance);

        static string shownAt, mirrorSig;
        /// <summary>Friend: the place this screen shows (the host's), or null in the shop.</summary>
        internal static string Joined { get { return shownAt; } }
        internal const string MirrorWindow = "shop_together.outing";

        internal static void Follow(PhaseState s) {
            if (!ReplicaSession.SceneReady || EmporiumEntry.Instance == null || MapUIManager.Instance == null || s.away) return;
            string target = s.location;
            if (target != shownAt) {
                string leaving = shownAt; shownAt = target; mirrorSig = null;
                var ui = StoreUIManager.Instance; var snapshot = s;
                Action change = () => { if (leaving != null) Visual(() => Leave(leaving)); if (target != null && shownAt == target) Visual(() => Enter(snapshot)); };
                if (ui != null) ui.StartBlackscreenSequence(true, change, null); else change();
            } else if (shownAt != null && !s.native && s.panelSig != mirrorSig) { var snapshot = s; Visual(() => Mirror(snapshot)); }
            else if (shownAt == "dumping_ground") Visual(() => MapUIManager.Instance.UpdateScavUI());

            // While the host is out, the visitor at the host's counter (Dr. Jackson) is not standing in this shop.
            if (s.location != null && StoreUIManager.Instance != null && StoreUIManager.Instance.clientGameObject != null && StoreUIManager.Instance.clientGameObject.gameObject.activeSelf && (AfterhourLocation.Get(s.location) == null || !AfterhourLocation.Get(s.location).ShowsStoreCounters))
                Visual(() => StoreUIManager.Instance.HideClient());
        }

        static void Enter(PhaseState s) {
            var location = AfterhourLocation.Get(s.location); var map = MapUIManager.Instance; var e = EmporiumEntry.Instance;
            if (TransitUI.IsOpen) TransitUI.Close();
            map.CloseUI(false);
            if (e.responseWindow != null && e.responseWindow.IsVisible()) e.HideResponseWindow();   // a backpack choice still open here
            if (GenericConfirmationUIManager.Instance != null) GenericConfirmationUIManager.Instance.ClosePanel();
            e.selectedBackpack = null;
            if (PaperUIManager.Instance != null) PaperUIManager.Instance.CloseAllPaperUI();
            e.CloseItemWindowUIWindow();
            e.OnLeaveStore();
            if (location != null && showStage != null) showStage.Invoke(map, new object[] { location });
            if (s.bag) e.ShowAfterhourInv();            // the host's ground, pockets and backpack (synced)
            if (location != null && s.native && openPanel != null) openPanel.Invoke(map, new object[] { location });
            if (location != null && location.ShowsStoreCounters) {
                e.frontInvWindow.Show(); e.backInvWindow.Show(); e.Validate(true);
                map.SetCreditDisplayPositionToDrJackson();
                if (StoreUIManager.Instance != null) StoreUIManager.Instance.storeCreditDisplay.creditDisplayText.gameObject.SetActive(true);
                if (location is DrJacksonLocation) { PlayerStore.Instance.playerLocation = PlayerStore.PlayerLocation.INVENTOR; if (ensureTablet != null) ensureTablet.Invoke(location, null); }
            }
            if (s.location == "dumping_ground") map.UpdateScavUI();
            if (!s.native) Mirror(s);
            if (StoreShutterButton.Instance != null) StoreShutterButton.Instance.blockerGameObject.transform.parent.gameObject.SetActive(false);
            AudioManager.Instance.Play2("open_door");
        }

        static void Leave(string place) {
            var location = AfterhourLocation.Get(place); var map = MapUIManager.Instance; var e = EmporiumEntry.Instance;
            if (CustomUIManager.Instance != null) CustomUIManager.Instance.CloseWindow(MirrorWindow);
            if (NegociationUIManager.Instance != null && NegociationUIManager.Instance.IsOpen) NegociationUIManager.Instance.CloseUI();
            if (place == "commissary" && CommissaryUIManager.Instance != null) CommissaryUIManager.Instance.CloseUI();
            if (closePanel != null) closePanel.Invoke(map, null);
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
                    if (returnText != null && text == returnText) builder.AddButton(Loc.Text(text), () => MapUIManager.Instance.Return());   // the warning here, the return on the host
                    else builder.AddButton(Loc.Text(text), () => RemoteActions.Forward(RemoteActions.Adapter("Press", 2), null, new object[] { sig, index }));
                }
            builder.SetPosition(MapUIManager.Instance.panelPosition);
            builder.Show();
        }

        static void Visual(Action action) {
            bool was = WorldReplica.Applying; WorldReplica.Applying = true;
            try { action(); } catch (Exception e) { Debug.LogWarning("[ShopTogether] friend outing view: " + e.Message); } finally { WorldReplica.Applying = was; }
        }

        internal static void ResetView() {
            if (shownAt != null && EmporiumEntry.Instance != null && MapUIManager.Instance != null) { string at = shownAt; Visual(() => Leave(at)); }
            shownAt = null; mirrorSig = null;
        }
    }

    // ---------------- host side ----------------

    /// <summary>Host side: the dumping-ground ground is sent to friends right after a search, not at the next sweep.</summary>
    [HarmonyPatch(typeof(ScavHelper), "ScavengeDumpingGrounds")]
    internal static class SearchSyncsGround {
        internal struct SearchState { internal int attempts; internal bool scoped, executing; }
        static void Prefix(out SearchState __state) {
            var session = Connection.Current;
            __state = new SearchState { attempts = PlayerStore.Instance != null ? PlayerStore.Instance.scavengingAttempts : int.MinValue,
                scoped = session != null && session.IsHost && session.Ready, executing = NativeActions.Executing };
            // Native scavenging creates loot inside the host's button/input scope.
            // Finish that authoritative operation before routing item moves again:
            // the new loot has no world ID until it has reached the ground.
            if (__state.scoped) NativeActions.Executing = true;
        }
        static void Postfix(SearchState __state) {
            var session = Connection.Current; var store = PlayerStore.Instance; var entry = EmporiumEntry.Instance;
            if (session != null && session.IsHost && store != null && entry != null && __state.attempts != int.MinValue && store.scavengingAttempts != __state.attempts) session.SyncGroundSoon(entry.afterhourInventory);
        }
        static Exception Finalizer(Exception __exception, SearchState __state) { if (__state.scoped) NativeActions.Executing = __state.executing; return __exception; }
    }

    // ---------------- friend side: only the host goes out and comes back ----------------

    /// <summary>
    /// Friend: every outing button is sent to the host, which checks it with the game's own rules and does it; the
    /// result reaches everyone through the shared state. Nothing here decides on its own or drops a click: the screens on
    /// the way (door choice, backpack choice, map, "things left on the ground") are this player's own, and every step
    /// that ends somewhere either sends or tells the player why it cannot.
    /// </summary>
    internal static class FriendOuting {
        internal static bool Mine { get { return ReplicaSession.ClientStore && !WorldReplica.Applying; } }

        /// <summary>Sends, or says why it cannot. Only the player's own clicks are sent.</summary>
        internal static void Send(string action, params object[] args) {
            if (!LocalControl.UserAction) return;
            if (!ReplicaSession.IsReplica) { RemoteActions.Notice("~SMC242~"); return; }
            RemoteActions.Forward(RemoteActions.Adapter(action, args.Length), null, args);
        }

        internal static void Depart(AfterhourLocation location) {
            var e = EmporiumEntry.Instance;
            GameItem pack = e != null ? e.selectedBackpack : null;
            // Keep the selection until arrival. A refused departure must not turn the next attempt
            // into an intentional "no backpack" request. Enter clears it after host confirmation.
            if (location == null) return;
            try { if (MapUIManager.Instance != null) MapUIManager.Instance.CloseUI(); } catch (Exception x) { Debug.LogWarning("[ShopTogether] map: " + x.Message); }
            Send("GoOut", location.Id, pack);
        }

        /// <summary>The backpack choice, as the game shows it; whether the choice may go out is the host's decision.</summary>
        internal static void ChooseBackpack() {
            var e = EmporiumEntry.Instance; if (e == null) return;
            var response = new StoreResponse { identifier = "afterhours_response", title = LocHelper.GetLocalizedMechanic("response_afterhours_title"), choices = new List<(GameItem, Action)>(), closable = true };
            response.choices.Add(EmporiumEntry.PrepChoice(DirectoryMaster.Item("plastic_bag"), LocHelper.GetLocalizedMechanic("response_afterhours_no_backpack"), false, "", null, null, () => Pick(null)));
            foreach (var backpack in e.GetAllOwnedItemsByTag("BACKPACK_TAG")) {
                var pack = backpack;
                var picture = DirectoryMaster.Item("");
                picture.SetSpriteAndShape(pack.spriteAtlasPath, pack.spritePath);
                picture.SetValue(pack.GetCurrentValue(useRetailMarkup: false, withChild: true, includeEvents: true, forceMarkup: false, 0L));
                response.choices.Add(EmporiumEntry.PrepChoice(picture, LocHelper.GetLocalizedMechanic("response_afterhours_select_backpack", pack.name), true, "", null, null, () => Pick(pack)));
            }
            e.ShowResponseWindow(response);
        }
        static void Pick(GameItem pack) {
            var e = EmporiumEntry.Instance; if (e == null) return;
            e.selectedBackpack = pack;
            e.HideResponseWindow();
            MapUIManager.Instance.OpenUI();
        }

        /// <summary>The host decides whether leaving requires a warning.</summary>
        internal static void Return() { Send("ReturnHome"); }

    }

    /// <summary>Friend: the door opens this player's own choice screens; the host checks whether everyone may go out.</summary>
    [HarmonyPatch(typeof(StoreDoor), "OnClick")]
    internal static class FriendDoor {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(StoreDoor __instance) { if (!FriendOuting.Mine) return true; if (LocalControl.UserAction) __instance.Clicked(); return false; }
    }
    [HarmonyPatch(typeof(StoreResponse), "ShowAfterhourResponse")]
    internal static class FriendBackpackChoice {
        [HarmonyPriority(Priority.First)]
        static bool Prefix() { if (!FriendOuting.Mine) return true; FriendOuting.ChooseBackpack(); return false; }
    }
    /// <summary>Friend: the map's go button and any other way of travelling are sent to the host.</summary>
    [HarmonyPatch]
    internal static class FriendDepart {
        static readonly FieldInfo selected = AccessTools.Field(typeof(TransitUI), "selected");
        static IEnumerable<MethodBase> TargetMethods() { yield return AccessTools.Method(typeof(TransitUI), "OnTravelClicked"); yield return AccessTools.Method(typeof(MapUIManager), "Travel"); }
        [HarmonyPriority(Priority.First)]
        static bool Prefix(MethodBase __originalMethod, object[] __args) {
            if (!FriendOuting.Mine) return true;
            var location = __originalMethod.Name == "Travel" ? (__args.Length > 0 ? __args[0] as AfterhourLocation : null) : (selected != null ? selected.GetValue(null) as AfterhourLocation : null);
            FriendOuting.Depart(location);
            return false;
        }
    }
    /// <summary>Host: going out with a backpack someone else is holding waits until they put it down, as with any item.
    /// (A friend's departure already had its backpack checked when the request arrived.)</summary>
    [HarmonyPatch(typeof(MapUIManager), "Travel")]
    internal static class HostDepartBackpack {
        static bool Prefix() {
            var session = Connection.Current; var e = EmporiumEntry.Instance;
            if (session == null || !session.IsHost || !session.Ready || RemoteActions.CurrentOwner > 0 || e == null || e.selectedBackpack == null) return true;
            if (AuthorityIntent.FreeTree(session, 0, e.selectedBackpack)) return true;
            RemoteActions.Notice("~SMC096~");
            return false;
        }
    }
    /// <summary>Friend: every way back (each place's leave button, the code-built panels, mods) is sent to the host.</summary>
    [HarmonyPatch]
    internal static class FriendComeBack {
        static IEnumerable<MethodBase> TargetMethods() { yield return AccessTools.Method(typeof(MapUIManager), "Return"); yield return AccessTools.Method(typeof(MapUIManager), "ReturnNow"); }
        [HarmonyPriority(Priority.First)]
        static bool Prefix(MethodBase __originalMethod) {
            if (!FriendOuting.Mine) {
                var session = Connection.Current;
                if (session != null && session.IsHost && session.Ready) {
                    if (__originalMethod.Name == "Return" && LocalControl.UserAction && RemoteActions.CurrentOwner < 0) {
                        RemoteActions.PendingNote = null; OutingKits.ReturnHome();
                        string note = RemoteActions.TakeNote(); if (note != null) RemoteActions.Notice(note);
                        return false;
                    }
                    if (__originalMethod.Name == "ReturnNow" && !OutingKits.ReturnAvailable()) {
                        if (RemoteActions.CurrentOwner < 0) RemoteActions.Notice(RemoteActions.TakeNote());
                        return false;
                    }
                }
                return true;
            }
            if (__originalMethod.Name == "Return") FriendOuting.Return(); else FriendOuting.Send("ReturnHome");
            return false;
        }
    }
    /// <summary>Friend: the dumping ground's search button searches with the host.</summary>
    [HarmonyPatch(typeof(ScavHelper), "ScavengeDumpingGrounds")]
    internal static class FriendScavenge {
        [HarmonyPriority(Priority.First)]
        static bool Prefix() { if (!FriendOuting.Mine) return true; FriendOuting.Send("Scavenge"); return false; }
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
}
