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
        public PhoneView phone;                               // the shop phone
        public VisitorView visitor;                           // the host's human visitor
        public bool body, survived; public int shots;   // the counter shotgun: body on the floor, shots fired
        public string ending; public int morning;       // the game's ending, and how many morning reports the host has seen
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
                SharedScreens.Capture(state);
                PhoneShare.Capture(state);
                VisitorShare.Capture(state);
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
            try { OutingKits.Follow(state); } catch (Exception e) { Debug.LogWarning("[ShopTogether] outing view: " + e.Message); }
            try { PhoneShare.Follow(state); } catch (Exception e) { Debug.LogWarning("[ShopTogether] phone: " + e.Message); }
            try { VisitorShare.Follow(state); } catch (Exception e) { Debug.LogWarning("[ShopTogether] visitor: " + e.Message); }
            try { ShotgunView.Follow(state); } catch (Exception e) { Debug.LogWarning("[ShopTogether] shotgun view: " + e.Message); }
            try { SharedScreens.Follow(state); } catch (Exception e) { Debug.LogWarning("[ShopTogether] shared screens: " + e.Message); }
            Last = state;
            return wasAway && !Away;
        }

        internal static void Reset() { Away = false; Text = ""; Last = null; FriendView.Reset(); ShotgunView.Reset(); SharedScreens.Reset(); OutingKits.ResetView(); PhoneShare.Reset(); VisitorShare.Reset(); }
    }

    /// <summary>
    /// Screens the host's game shows to the whole shop: the ending of the run, and the morning report after a night.
    /// Friends see the same screen; its buttons only close it on their own screen (leaving to the menu leaves the room).
    /// </summary>
    internal static class SharedScreens {
        static int endings, mornings;           // host: how many times each was shown
        static string ending = "";
        static bool primed; static string seenEnding; static int seenMorning;

        internal static void Capture(PhaseState state) { state.ending = endings > 0 ? endings + ":" + ending : ""; state.morning = mornings; }
        internal static void Reset() { endings = 0; mornings = 0; ending = ""; primed = false; seenEnding = null; seenMorning = 0; }

        [HarmonyPatch(typeof(PlayerStore), "ExecuteGameOver")]
        internal static class HostEnding { static void Postfix(string ending) { if (!ReplicaSession.ClientStore) { endings++; SharedScreens.ending = ending ?? ""; } } }
        [HarmonyPatch(typeof(StartOfDayUIManager), "OpenUI")]
        internal static class HostMorning { static void Postfix() { if (!ReplicaSession.ClientStore) mornings++; } }

        /// <summary>Friend side. What was already on screen when this player joined is not shown again.</summary>
        internal static void Follow(PhaseState state) {
            if (state == null || !ReplicaSession.SceneReady) return;
            if (!primed) { primed = true; seenEnding = state.ending ?? ""; seenMorning = state.morning; return; }
            if ((state.ending ?? "") != seenEnding) {
                seenEnding = state.ending ?? "";
                int cut = seenEnding.IndexOf(':');
                if (cut > 0) ShowEnding(seenEnding.Substring(cut + 1));
            }
            if (state.morning != seenMorning) {
                seenMorning = state.morning;
                ShowMorningReport();
            }
        }

        // Only the report is shown here: the game's function also fixes up the shop's state, which stays the host's.
        static void ShowMorningReport() {
            var ui = StartOfDayUIManager.Instance; var store = PlayerStore.Instance;
            if (ui == null || store == null) return;
            var state = store.storeState; int lockFlag = store.lockShutterButtonFlag;
            try { ui.ShowMorningReport(); } finally { store.storeState = state; store.lockShutterButtonFlag = lockFlag; }
        }

        // The same texts the game's ExecuteGameOver picks for each ending, in this player's language.
        static void ShowEnding(string id) {
            var ui = GameOverUIManager.Instance;
            if (ui == null) return;
            string reason = id, desc = id; bool keepPlaying = false;
            switch (id) {
                case "buyout": reason = "buyout"; desc = "buyout"; keepPlaying = true; break;
                case "buyout_mortgage": reason = "buyout"; desc = "buyout_mortgage"; keepPlaying = true; break;
                case "lou_bombed": reason = "bombed"; desc = "lou_bombed"; break;
                case "lou_bombed_debt": reason = "bombed"; desc = "lou_debt"; break;
                case "rent": case "arrest": case "execution": case "repo": case "mob": case "sec": case "hitman": case "sentinel":
                case "bombed": case "cartel_retired": case "vendor_permit": case "aug": case "reactor": break;
                default: reason = "rent"; desc = "rent"; break;
            }
            ui.SetEnding(id);
            ui.DisplayUI(LocHelper.GetLocalizedMechanic("mech_end_" + reason + "_reason"), LocHelper.GetLocalizedMechanic("mech_end_" + desc + "_desc"), keepPlaying);
        }
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
            if (location == null || root == null) { RemoteActions.PendingNote = "~SMC247~"; return; }
            var lines = new List<string>(); var isButton = new List<bool>(); var enabled = new List<bool>(); var buttons = new List<Button>();
            Read(root, lines, isButton, enabled, buttons);
            // The panel changed since the friend saw it (another click came first): say so; their screen updates.
            if (Signature(location.Id, lines, isButton) != signature || index < 0 || index >= buttons.Count) { RemoteActions.PendingNote = "~SMC247~"; return; }
            var button = buttons[index];
            if (button != null && button.interactable && button.gameObject.activeInHierarchy) button.onClick.Invoke(); else RemoteActions.PendingNote = "~SMC247~";
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

        internal static void Press(string signature, int index) { OutingPanel.Press(signature, index); }

        /// <summary>Same as ticking a night service in the end-of-day panel.</summary>
        internal static void ToggleService(string id, bool on) {
            if (PlayerStore.Instance == null || id == null) { RemoteActions.PendingNote = "~SMC247~"; return; }
            foreach (var service in PlayerStore.Instance.storeService) {
                if (service == null || service.id != id) continue;
                if (!service.unlocked) { RemoteActions.PendingNote = "~SMC247~"; return; }
                if (service.isMandatory) { RemoteActions.PendingNote = "~SMC322~"; return; }
                if (service.IsOnCooldown()) { RemoteActions.PendingNote = "~SMC323~"; return; }
                service.isUsing = on;
                var panel = EndOfDayUIManager.Instance;
                if (panel != null && panel.IsOpen) panel.InitUI();
                return;
            }
            RemoteActions.PendingNote = "~SMC247~";
        }

        /// <summary>Same as ticking the issues in the inspection panel and pressing confront.</summary>
        internal static void Confront(GameItem item, int issues) {
            int kind = item != null && item.IsTag("CIGARETTE_TAG") ? ShopActions.CigaretteConfront : item != null && item.IsTag("FOOD_STAMP_TAG") ? ShopActions.StampConfront : ShopActions.InjectorConfront;
            ShopActions.Inspect(item, kind, issues);
        }

        /// <summary>Same as the confront button of the water strip or scanner.</summary>
        internal static void ConfrontWater(GameItem item) {
            if (item == null || DialogUIManager.Instance == null) { RemoteActions.PendingNote = "~SMC095~"; return; }
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
            long cost = 0;
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

            // Out at night the screen goes along with the host (OutingKits). In the shop: the same shutter and counter as the host.
            if (s.location == null && OutingKits.Joined == null && !s.onMap && !TransitUI.IsOpen) Visual(() => {
                var shutter = StoreShutterButton.Instance;
                if (shutter != null) {
                    if (HostPhase.ShutterClosed() != s.shutterClosed) shutter.ToggleShutterDoor();
                    // The host's OpenShutter completion callback hides this raycast mask. Friends only replay the
                    // shutter animation, so they must hide the mask after the same animation actually finishes.
                    // Otherwise an invisible mask keeps intercepting the outdoor vending machines all day.
                    var mask = shutter.blockerMaskObject;
                    if (mask != null && !s.shutterClosed && mask.gameObject.activeSelf &&
                        shutter.shutterGameObject != null && shutter.blockerGameObject != null &&
                        shutter.shutterGameObject.transform.position == shutter.endPosition &&
                        shutter.blockerGameObject.transform.localPosition == shutter.blockerEndPosition)
                        mask.gameObject.SetActive(false);
                }
                var front = EmporiumEntry.Instance.frontInvWindow;
                if (front != null && front.IsVisible() != s.frontShown) { if (s.frontShown) EmporiumEntry.Instance.ShowFrontArea(); else EmporiumEntry.Instance.HideFrontArea(); }
            });
        }

    }

    // ---------------- friend-side interception of the day flow ----------------

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
