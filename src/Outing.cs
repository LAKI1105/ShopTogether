using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShopTogether {
    // ------------------------------------------------------------------------------------------
    // The day flow shared by everyone: opening and closing the shop, the end-of-day services, sleeping,
    // going out at night and what happens at the location. The host's game runs all of it; friends see
    // the same shutter, the same location and the same location panel, and their clicks go to the host.
    // ------------------------------------------------------------------------------------------

    [Serializable] internal sealed class PhaseState {
        public bool away; public string reason;
        public int day;
        public bool shutterClosed, frontShown, onMap;
        public string location; public bool native, bag;
        public string title, panelSig;
        public string[] lines; public bool[] buttons, enabled;
        public int[] kitOwners, kitLeft, joined; public string[] kitPlaces, outTonight;   // friends out on their own, or with the host   // friends out on their own
        public PhoneView phone;                               // the shop phone
        public bool body, survived; public int shots;   // the counter shotgun: body on the floor, shots fired
    }

    internal static class HostPhase {
        internal static bool Away;
        internal static string Text = "";
        internal static PhaseState Last;
#if LAN_PROBE
        internal static bool ProbeAway;
#endif
        static readonly FieldInfo shutterClosedField = typeof(StoreShutterButton).GetField("isClosed", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Host side: null when friends can act, otherwise why they must wait a moment.</summary>
        internal static string Busy(bool ready) {
#if LAN_PROBE
            if (ProbeAway) return "房主正在移動，請稍候…";
#endif
            if (!ready) return "~SMC081~";
            try {
                if (MapUIManager.Instance != null && MapUIManager.Instance.IsInTransit) return "~SMC079~";
                if (StoreUIManager.Instance != null && StoreUIManager.Instance.savingTMP != null && StoreUIManager.Instance.savingTMP.gameObject.activeSelf) return "~SMC052~";
            } catch { }
            return null;
        }

        internal static bool ShutterClosed() {
            var b = StoreShutterButton.Instance;
            return b == null || shutterClosedField == null || (bool)shutterClosedField.GetValue(b);
        }

        /// <summary>Host side: everything a friend's screen needs to follow the host.</summary>
        internal static PhaseState Capture(bool ready) {
            var state = new PhaseState { reason = Busy(ready) };
            state.away = state.reason != null;
            if (state.reason == null) state.reason = "";
            if (!ready) return state;
            try {
                state.day = StoreStation.Instance != null ? StoreStation.Instance.dayCounter : 0;
                state.shutterClosed = ShutterClosed();
                state.frontShown = EmporiumEntry.Instance != null && EmporiumEntry.Instance.frontInvWindow != null && EmporiumEntry.Instance.frontInvWindow.IsVisible();
                state.onMap = TransitUI.IsOpen;
                var location = MapUIManager.Instance != null ? MapUIManager.Instance.Current : null;
                if (location != null) {
                    state.location = location.Id;
                    state.native = location.PanelObject != null;
                    state.bag = location.UsesAfterhourBag;
                    if (!state.native) OutingPanel.Capture(state, location);
                }
                ShotgunView.Capture(state);
                OutingKits.Capture(state);
                PhoneShare.Capture(state);
            } catch (Exception e) { Debug.LogWarning("[ShopTogether] phase capture: " + e.Message); }
            return state;
        }

        /// <summary>Friend side. Returns true when the host just came back from a pause (time to refresh everything).</summary>
        internal static bool Apply(PhaseState state) {
            if (state == null) return false;
            bool wasAway = Away;
            Away = state.away;
            Text = string.IsNullOrEmpty(state.reason) ? "" : (state.reason.Length > 120 ? state.reason.Substring(0, 120) : state.reason);
            try { FriendView.Follow(state); } catch (Exception e) { Debug.LogWarning("[ShopTogether] follow host: " + e.Message); }
            try { OutingKits.Follow(state); } catch (Exception e) { Debug.LogWarning("[ShopTogether] own outing: " + e.Message); }
            try { PhoneShare.Follow(state); } catch (Exception e) { Debug.LogWarning("[ShopTogether] phone: " + e.Message); }
            try { ShotgunView.Follow(state); } catch (Exception e) { Debug.LogWarning("[ShopTogether] shotgun view: " + e.Message); }
            Last = state;
            return wasAway && !Away;
        }

        internal static void Reset() { Away = false; Text = ""; Last = null; FriendView.Reset(); ShotgunView.Reset(); OutingKits.ResetView(); PhoneShare.Reset(); }
    }

    /// <summary>A location panel built from code (also by other mods) is sent as its visible text and buttons.</summary>
    internal static class OutingPanel {
        internal const string HostWindow = "afterhour_location";
        const int MaxLines = 40, MaxText = 200;

        static RectTransform Root() {
            var ui = CustomUIManager.Instance;
            var window = ui != null ? ui.GetWindow(HostWindow) : null;
            return window != null && window.IsAlive ? window.ContentRoot : null;
        }

        /// <summary>Visible buttons and texts of the host's panel, in screen order.</summary>
        static void Read(RectTransform root, List<string> lines, List<bool> isButton, List<bool> enabled, List<Button> buttons) {
            foreach (var t in root.GetComponentsInChildren<Transform>(false)) {
                if (lines.Count >= MaxLines) break;
                var button = t.GetComponent<Button>();
                if (button != null) {
                    var label = button.GetComponentInChildren<TextMeshProUGUI>(false);
                    lines.Add(Clip(label != null ? label.text : "…")); isButton.Add(true); enabled.Add(button.interactable); if (buttons != null) buttons.Add(button);
                    continue;
                }
                var text = t.GetComponent<TextMeshProUGUI>();
                if (text == null || string.IsNullOrEmpty(text.text) || text.GetComponentInParent<Button>() != null) continue;
                lines.Add(Clip(text.text)); isButton.Add(false); enabled.Add(true);
            }
        }
        static string Clip(string s) { s = s ?? ""; return s.Length > MaxText ? s.Substring(0, MaxText) : s; }

        static string Signature(string location, List<string> lines, List<bool> isButton) {
            var b = new StringBuilder(location);
            for (int i = 0; i < lines.Count; i++) b.Append('\n').Append(isButton[i] ? 'B' : 'T').Append(lines[i]);
            return Fingerprint.Hash(b.ToString()).ToString("X");
        }

        internal static void Capture(PhaseState state, AfterhourLocation location) {
            var root = Root();
            if (root == null) return;
            var lines = new List<string>(); var isButton = new List<bool>(); var enabled = new List<bool>();
            Read(root, lines, isButton, enabled, null);
            try { state.title = Clip(location.GetPanelTitle()); } catch { state.title = location.Id; }
            state.lines = lines.ToArray(); state.buttons = isButton.ToArray(); state.enabled = enabled.ToArray();
            state.panelSig = Signature(location.Id, lines, isButton);
        }

        /// <summary>Host side: press button number <paramref name="index"/> if the panel is still the one the friend saw.</summary>
        internal static void Press(string signature, int index) {
            var location = MapUIManager.Instance != null ? MapUIManager.Instance.Current : null;
            var root = Root();
            if (location == null || root == null) return;
            var lines = new List<string>(); var isButton = new List<bool>(); var enabled = new List<bool>(); var buttons = new List<Button>();
            Read(root, lines, isButton, enabled, buttons);
            if (Signature(location.Id, lines, isButton) != signature || index < 0 || index >= buttons.Count) return;
            var button = buttons[index];
            if (button != null && button.interactable && button.gameObject.activeInHierarchy) button.onClick.Invoke();
        }
    }

    /// <summary>Host-side helpers a friend's click is turned into.</summary>
    internal static class OutingAdapters {
        internal static void TravelTo(string id) {
            var map = MapUIManager.Instance;
            if (map == null || map.isOutside || map.IsInTransit || id == null || id.Length > 128) return;
            var location = AfterhourLocation.Get(id);
            if (location == null || !location.IsAvailable()) return;
            if (PlayerStore.Instance == null || PlayerStore.Instance.storeState == PlayerStore.StoreState.OPEN) return;
            map.Travel(location);
        }

        internal static void Press(string signature, int index) { if (OutingKits.IsJoined(RemoteActions.CurrentOwner)) OutingPanel.Press(signature, index); }

        /// <summary>Same as ticking a night service in the end-of-day panel.</summary>
        internal static void ToggleService(string id, bool on) {
            if (PlayerStore.Instance == null || id == null) return;
            foreach (var service in PlayerStore.Instance.storeService) {
                if (service == null || service.id != id) continue;
                if (!service.unlocked || service.isMandatory || service.IsOnCooldown()) { RemoteActions.PendingNote = "~SMC247~"; return; }
                service.isUsing = on;
                var panel = EndOfDayUIManager.Instance;
                if (panel != null && panel.IsOpen) panel.InitUI();
                return;
            }
        }

        /// <summary>Same as ticking the issues in the inspection panel and pressing confront.</summary>
        internal static void Confront(GameItem item, int issues) {
            var ui = InspectionUIManager.Instance;
            if (ui == null || item == null || (issues & 31) == 0) return;
            ui.OnInspectItem(item);
            ui.toggle1.isOn = (issues & 1) != 0; ui.toggle2.isOn = (issues & 2) != 0; ui.toggle3.isOn = (issues & 4) != 0;
            ui.toggle4.isOn = (issues & 8) != 0; ui.toggle5.isOn = (issues & 16) != 0;
            ui.OnConfront();
        }

        /// <summary>Same as the confront button of the water strip or scanner.</summary>
        internal static void ConfrontWater(GameItem item) {
            if (item == null || DialogUIManager.Instance == null) return;
            var feature = WaterFeatureHelper.GetWaterFeature(item);
            if (feature == null || !feature.CanExposeFeature() || InspectionUIManager.IsConfrontLocked() || GeneralHelper.IsItemOwned(item) || feature.IsExposed() || feature.IsFeatureMatch()) { RemoteActions.PendingNote = "~SMC247~"; return; }
            if (DialogUIManager.Instance.StartDialogue(feature.GetExposeDialog())) InspectableHelper.OnConfrontSuccess(item); else RemoteActions.PendingNote = "~SMC247~";
        }
    }

    /// <summary>Host-side re-check of the conditions the game's own buttons check before a friend's click runs.</summary>
    internal static class DayGuards {
        static PlayerStore Store { get { return PlayerStore.Instance; } }
        static bool Away { get { return MapUIManager.Instance != null && (MapUIManager.Instance.isOutside || MapUIManager.Instance.IsInTransit); } }
        static bool ShutterBusy { get { return Store.lockShutterButtonFlag > 0 || Store.isClientBeingArrested || Store.isHandlingShootout; } }

        internal static string Open() {
            if (Store == null || StoreShutterButton.Instance == null || Away) return "~SMC126~";
            if (ShutterBusy || Store.storeState != PlayerStore.StoreState.MORNING || !Store.CanStartDay()) return "~SMC126~";
            return null;
        }
        internal static string Close() {
            if (Store == null || StoreShutterButton.Instance == null || Away) return "~SMC123~";
            if (ShutterBusy || Store.storeState != PlayerStore.StoreState.OPEN || !(Store.CanEndDay() || Store.devMode)) return "~SMC123~";
            return null;
        }
        internal static string Sleep() {
            if (Store == null || EndOfDayUIManager.Instance == null || Away || Store.storeState == PlayerStore.StoreState.OPEN) return "~SMC076~";
            if (HostVoidBackpackBack.HasLeftovers) return "~SMC225~";
            if (OutingKits.AnyOut) return "~SMC211~" + OutingKits.OutNames(Connection.Current) + "~SMC212~";
            int cost = 0;
            foreach (var service in Store.storeService) if (service != null && service.unlocked && service.isUsing) cost += service.cost;
            if (cost > 0 && cost > Store.playerCash) return "~SMC176~";
            return null;
        }
        internal static string Return() {
            var map = MapUIManager.Instance;
            return map == null || !map.isOutside || map.IsInTransit ? "~SMC124~" : null;
        }
        internal static string Outside() {
            var map = MapUIManager.Instance;
            return map == null || !map.isOutside || map.IsInTransit ? "~SMC078~" : null;
        }
    }

    /// <summary>Friend side: shows on this screen what the host's screen shows. Visual only; the world comes from sync.</summary>
    internal static class FriendView {
        internal const string MirrorWindow = "shop_together.outing";
        static string shownLocation, shownSig;
        static int shownDay = -1;
        internal static string ShownLocation { get { return shownLocation; } }

        internal static void Reset() { shownLocation = null; shownSig = null; shownDay = -1; }

        static void Visual(Action action) {
            bool was = WorldReplica.Applying; WorldReplica.Applying = true;
            try { action(); } catch (Exception e) { Debug.LogWarning("[ShopTogether] friend view: " + e.Message); } finally { WorldReplica.Applying = was; }
        }

        internal static void Follow(PhaseState s) {
            if (!ReplicaSession.ClientStore || !ReplicaSession.SceneReady || EmporiumEntry.Instance == null || s.away) return;

            // A new day began on the host: close this machine's end-of-day panel and map.
            if (shownDay >= 0 && s.day != shownDay) Visual(() => {
                if (EndOfDayUIManager.Instance != null && EndOfDayUIManager.Instance.IsOpen) EndOfDayUIManager.Instance.CloseUI();
                if (TransitUI.IsOpen && shownLocation == null) MapUIManager.Instance.CloseUI();
            });
            shownDay = s.day;

            // Friends no longer follow the host out: they go out on their own (OutingKits). The host-only places
            // (Dr. Jackson, other mods' locations) are not shown on a friend's screen.
            // In the shop: the same shutter and counter as the host.
            if (s.location == null && OutingKits.Here == null && OutingKits.Joined == null && !s.onMap && !TransitUI.IsOpen) Visual(() => {
                if (StoreShutterButton.Instance != null && HostPhase.ShutterClosed() != s.shutterClosed) StoreShutterButton.Instance.ToggleShutterDoor();
                var front = EmporiumEntry.Instance.frontInvWindow;
                if (front != null && front.IsVisible() != s.frontShown) { if (s.frontShown) EmporiumEntry.Instance.ShowFrontArea(); else EmporiumEntry.Instance.HideFrontArea(); }
            });
        }

    }

    // ---------------- friend-side interception of the day flow ----------------

    [HarmonyPatch(typeof(MapUIManager), "Travel")]
    internal static class FriendTravel {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(AfterhourLocation location) {
            if (!ReplicaSession.ClientStore || WorldReplica.Applying) return true;
            // Every refusal says why: a click that silently does nothing looks like a broken door.
            if (location == null) return false;
            if (!ReplicaSession.IsReplica) { RemoteActions.Notice("~SMC242~"); return false; }
            if (OutingKits.Here != null || OutingKits.Joined != null) { RemoteActions.Notice("~SMC241~"); return false; }
            if (!OutingKits.FriendMayGo(location.Id)) {
                // Dr. Jackson and other mods' places: only together with the host, while the host is there.
                if (EmporiumEntry.Instance != null) EmporiumEntry.Instance.selectedBackpack = null;   // coming along shares the host's ground
                var host = HostPhase.Last;
                if (host != null && !host.away && host.location == location.Id) RemoteActions.Forward(RemoteActions.Adapter("Join", 0), null, new object[0]);
                else RemoteActions.Notice("~SMC216~");
                return false;
            }
            var e = EmporiumEntry.Instance;
            GameItem pack = e != null ? e.selectedBackpack : null;
            if (e != null) e.selectedBackpack = null;
            RemoteActions.Forward(RemoteActions.Adapter("Go", 2), null, new object[] { location.Id, pack });
            return false;
        }
    }

    [HarmonyPatch(typeof(MapUIManager), "Return")]
    internal static class FriendReturn {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(MapUIManager __instance) {
            if (!ReplicaSession.ClientStore || WorldReplica.Applying) return true;
            if (OutingKits.Joined != null) { if (ReplicaSession.IsReplica && LocalControl.Focused) RemoteActions.Forward(RemoteActions.Adapter("Unjoin", 0), null, new object[0]); return false; }
            var kit = OutingKits.Mine;
            if (!ReplicaSession.IsReplica) { RemoteActions.Notice("~SMC242~"); return false; }
            if (OutingKits.Here == null) { if (!OutingKits.LeaveStale() && LocalControl.UserAction) RemoteActions.Notice("~SMC247~"); return false; }   // clear a stale view, or explain why there is nowhere to return from
            if (kit == null) { RemoteActions.Notice("~SMC242~"); return false; }
            Action go = () => RemoteActions.Forward(RemoteActions.Adapter("Back", 0), null, new object[0]);
            if (kit.ground.childItems.Count == 0 || GenericConfirmationUIManager.Instance == null) { go(); return false; }
            string warning = null;
            try { var location = AfterhourLocation.Get(OutingKits.Here); if (location != null) warning = location.GetLeaveWarning(); } catch { }
            if (string.IsNullOrEmpty(warning)) warning = LocHelper.GetLocalizedMechanic("afterhour_items_lost_warning");
            GenericConfirmationUIManager.Instance.OpenPanel(warning, LocHelper.GetLocalizedMechanic("afterhour_confirm_leave"), LocHelper.GetLocalizedMechanic("afterhour_confirm_cancel"),
                delegate { GenericConfirmationUIManager.Instance.ClosePanel(); go(); }, delegate { GenericConfirmationUIManager.Instance.ClosePanel(); });
            return false;
        }
    }

    [HarmonyPatch(typeof(EndOfDayUIManager), "OnToggled")]
    internal static class FriendService {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(StoreService storeService, bool on) {
            if (!ReplicaSession.ClientStore || WorldReplica.Applying) return true;
            if (ReplicaSession.IsReplica && storeService != null && LocalControl.Focused) RemoteActions.Forward(RemoteActions.Adapter("ToggleService", 2), null, new object[] { storeService.id, on });
            return false;
        }
    }

    [HarmonyPatch(typeof(InspectionUIManager), "OnConfront")]
    internal static class FriendConfront {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(InspectionUIManager __instance, GameItem ___currentInspectingItem) {
            if (!ReplicaSession.ClientStore || WorldReplica.Applying) return true;
            if (!ReplicaSession.IsReplica || ___currentInspectingItem == null || !LocalControl.Focused) return false;
            int issues = (__instance.toggle1.isOn ? 1 : 0) | (__instance.toggle2.isOn ? 2 : 0) | (__instance.toggle3.isOn ? 4 : 0) | (__instance.toggle4.isOn ? 8 : 0) | (__instance.toggle5.isOn ? 16 : 0);
            if (issues == 0) return false;
            RemoteActions.Forward(RemoteActions.Adapter("Confront", 2), null, new object[] { ___currentInspectingItem, issues });
            return false;
        }
    }

    [HarmonyPatch(typeof(WaterStripUIManager), "OnConfront")]
    internal static class FriendWaterStrip {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(GameItem ___currentWaterContainer) {
            if (!ReplicaSession.ClientStore || WorldReplica.Applying) return true;
            if (ReplicaSession.IsReplica && ___currentWaterContainer != null && LocalControl.Focused) RemoteActions.Forward(RemoteActions.Adapter("ConfrontWater", 1), null, new object[] { ___currentWaterContainer });
            return false;
        }
    }

    [HarmonyPatch(typeof(WaterScannerUIManager), "ConfrontWater")]
    internal static class FriendWaterScanner {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(GameItem waterContainer) {
            if (!ReplicaSession.ClientStore || WorldReplica.Applying) return true;
            if (ReplicaSession.IsReplica && waterContainer != null && LocalControl.Focused) RemoteActions.Forward(RemoteActions.Adapter("ConfrontWater", 1), null, new object[] { waterContainer });
            return false;
        }
    }
}
