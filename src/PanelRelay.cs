using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    [Serializable] internal sealed class PanelCall { public int m; public int[] items; public string[] texts; public int[] numbers; }

    /// <summary>
    /// Reading panels (scanners, test strips, papers, guides, notes) belong to the player who used the item.
    /// When a friend's item action runs on the host, the panel it would open is not shown on the host's screen;
    /// the host sends which panel to open and the friend opens it on their own screen. Everything the action
    /// changes in the world still happens on the host only.
    /// </summary>
    internal static class PanelRelay {
        internal const byte Message = 43;
        internal static int Owner;
        internal static GameItem SourceItem;
        internal static bool SourceIsActivation;
        internal static bool Capturing { get { var s = Connection.Current; return Owner > 0 && s != null && s.IsHost; } }
        static MethodBase[] methods;
        internal static MethodBase[] Methods { get { if (methods == null) methods = Build(); return methods; } }
        static readonly List<PanelCall> waiting = new List<PanelCall>();
        static float due; static long seen;

        static MethodBase[] Build() {
            return new MethodBase[] {
                AccessTools.Method(typeof(ChemScannerUIManager), "Open"),
                AccessTools.Method(typeof(WaterScannerUIManager), "Open"),
                AccessTools.Method(typeof(PlantAnalyserUIManager), "OpenUI"),
                AccessTools.Method(typeof(WaterStripUIManager), "Open"),
                AccessTools.Method(typeof(RecordUIManager), "OpenUI"),
                AccessTools.Method(typeof(PaperUIManager), "OpenUI"),
                AccessTools.Method(typeof(PaperUIManager), "OpenContract"),
                AccessTools.Method(typeof(PaperUIManager), "OpenDeed"),
                AccessTools.Method(typeof(PaperUIManager), "OpenBook"),
                AccessTools.Method(typeof(PaperUIManager), "OpenSlipUI"),
                AccessTools.Method(typeof(PaperUIManager), "OpenGeneralSlipUI"),
                AccessTools.Method(typeof(PaperUIManager), "OpenCigaretteGuide"),
                AccessTools.Method(typeof(PaperUIManager), "OpenStampGuide"),
                AccessTools.Method(typeof(PaperUIManager), "OpenInjectorGuide"),
                AccessTools.Method(typeof(PaperUIManager), "OpenWantedPaper"),
                AccessTools.Method(typeof(PaperUIManager), "OpenSecCard"),
                AccessTools.Method(typeof(PaperUIManager), "OpenCigaretteColor"),
                AccessTools.Method(typeof(PaperUIManager), "OpenCodeBook"),
                AccessTools.Method(typeof(PaperUIManager), "OpenBrewerCard"),
                AccessTools.Method(typeof(PaperUIManager), "OpenMiningCard"),
                AccessTools.Method(typeof(PaperUIManager), "OpenChemCard"),
                AccessTools.Method(typeof(PaperUIManager), "OpenPharmaCard"),
                AccessTools.Method(typeof(PaperUIManager), "OpenCard"),
                AccessTools.Method(typeof(PaperUIManager), "OpenLotteryInfo"),
                AccessTools.Method(typeof(PaperUIManager), "OpenAugIdGuide"),
                AccessTools.Method(typeof(MultiPageUIManager), "OpenUI"),
                AccessTools.Method(typeof(HandnoteUIManager), "OpenPanel"),
                AccessTools.Method(typeof(RepublicLogoUIManager), "OpenPanel"),
                AccessTools.Method(typeof(NewsUIManager), "ToggleUI"),
                AccessTools.Method(typeof(LogUIManager), "Toggle"),
                AccessTools.Method(typeof(PaperUIManager), "ToggleJoeCard"),
                AccessTools.Method(typeof(ExchangeUIManager), "OpenPanel"),
                AccessTools.Method(typeof(ScratcherUIManager), "OpenUI"),
                AccessTools.Method(typeof(GenericConfirmationUIManager), "OpenPanelSingleButton"),
                AccessTools.Method(typeof(GenericConfirmationUIManager), "OpenPanel"),
                AccessTools.Method(typeof(StoreResponse), "ShowSupplyCrateResponse"),
                AccessTools.Method(typeof(EmporiumEntry), "HideResponseWindow"),
                AccessTools.Method(typeof(GenericConfirmationUIManager), "ClosePanel"),
                AccessTools.Method(typeof(StoreUIManager), "Notify"),
                AccessTools.Method(typeof(ReputationUIManager), "Notify"),
                AccessTools.Method(typeof(AudioManager), "Play"),
                AccessTools.Method(typeof(AudioManager), "Play2"),
                AccessTools.Method(typeof(AudioManager), "Play3"),
                AccessTools.Method(typeof(NegociationUIManager), "OpenConfirmPanel"),
                AccessTools.Method(typeof(WildNetworkUI), "Open"),
                AccessTools.Method(typeof(AppraisalItemsWindow), "Open", new[] { typeof(bool) }),
                AccessTools.Method(typeof(DialogSettingUIManager), "OpenUI"),
                // Machine windows that open when the machine itself is clicked (not a button inside its window).
                AccessTools.Method(typeof(HopperHelper), "OpenWindow", new[] { typeof(GameItem) }),
                AccessTools.Method(typeof(HopperHelper), "OpenOutletWindow", new[] { typeof(GameItem) }),
                AccessTools.Method(typeof(MachineStorageCoordinator), "OpenWindow", new[] { typeof(GameItem) }),
                AccessTools.Method(typeof(MindReprogrammerHelper), "OpenWindow", new[] { typeof(GameItem) }),
            };
        }

        // Host: a friend's action tried to open a panel. Keep it off the host's screen and tell the friend.
        // Methods past the reading panels: 36/37 only keep the friend's action off the host's screen,
        // 38-42 (notices and sounds) reach the friend and still play on the host.
        const int Scratcher = 32, Confirm = 34, Crate = 35, HideResponse = 36, CloseConfirm = 37, NoticeFirst = 38, NoticeLast = 42, BudgetConfirm = 43, Wild = 44;
        internal static bool BuildingCratePreview;
        internal static bool IsCratePreview(GameInventory inventory, GameItem item) {
            var s = Connection.Current; var e = EmporiumEntry.Instance;
            return s != null && s.IsClient && e != null && inventory != null && inventory == e.responseInventoryClosable && item != null && s.World.Id(item) == 0;
        }
        sealed class Pending { internal Action yes; internal float until; internal int token, itemId; internal GameItem item; internal GameInventory inventory; internal bool activation; }
        static readonly Dictionary<int, Pending> confirms = new Dictionary<int, Pending>();
        static int nextConfirm;
        internal static bool HasPending(int owner) { return confirms.ContainsKey(owner); }

        /// <summary>Friend pressed "yes" on a confirmation their own action asked for: run what the host kept for them.</summary>
        internal static void ConfirmPanel(int token) {
            int owner = RemoteActions.CurrentOwner; Pending p;
            if (owner <= 0 || !confirms.TryGetValue(owner, out p) || p.token != token) return;
            confirms.Remove(owner);
            if (p.yes == null || Time.realtimeSinceStartup > p.until) return;
            if (p.item != null) {
                var s = Connection.Current; GameItem current;
                if (s == null || !s.World.Items.TryGetValue(p.itemId, out current) || current != p.item || current.parentInventory != p.inventory ||
                    !s.Leases.Available(owner, p.itemId, Time.realtimeSinceStartup) ||
                    (p.activation && (!current.MayActivateSlotItem() || !current.CanActivateSlotItem()))) {
                    RemoteActions.PendingNote = "~SMC096~"; return;
                }
                var at = s.World.Address(current.parentInventory); if (at != null && !OutingKits.MayUse(owner, at.root)) { RemoteActions.PendingNote = "~SMC217~"; return; }
            }
            p.yes();
        }
        internal static void CancelPanel(int token) { int owner = RemoteActions.CurrentOwner; Pending p; if (owner > 0 && confirms.TryGetValue(owner, out p) && p.token == token) confirms.Remove(owner); }

        internal static bool Capture(MethodBase method, object[] args) {
            var s = Connection.Current; int index = -1;
            for (int i = 0; i < Methods.Length; i++) if (Methods[i] != null && method != null && Methods[i].DeclaringType == method.DeclaringType && Methods[i].Name == method.Name) { index = i; break; }
            if (s == null || index < 0 || args == null) return true;
            bool notice = index >= NoticeFirst && index <= NoticeLast;
            // A friend outside the shop already gets the messages and sounds of their outing another way.
            if (notice && OutingKits.Capturing) return true;
            if (index == HideResponse || index == CloseConfirm) return false;
            // Parts of the action that change the world still happen here, on the host.
            if (index == Scratcher && args.Length > 0 && args[0] is GameItem) ScratcherHelper.TryRerollForBetterLuck((GameItem)args[0]);
            if (index == Crate && args.Length > 0 && args[0] is GameItem) PreBuiltItemHelper.MarkCrateScanned((GameItem)args[0]);
            int confirmToken = 0;
            if (index == Confirm) {
                if (args.Length < 4) return false;
                foreach (int peer in new List<int>(confirms.Keys)) if (confirms[peer].until < Time.realtimeSinceStartup || !s.HasPeer(peer)) confirms.Remove(peer);
                if (++nextConfirm <= 0) nextConfirm = 1; confirmToken = nextConfirm;
                confirms[Owner] = new Pending { yes = args[3] as Action, until = Time.realtimeSinceStartup + 300f, token = confirmToken,
                    item = SourceItem, itemId = SourceItem != null ? s.World.Id(SourceItem) : 0,
                    inventory = SourceItem != null ? SourceItem.parentInventory : null, activation = SourceIsActivation };
            }
            var items = new List<int>(); var texts = new List<string>(); var numbers = new List<int>();
            // Wild's network: opening it counts as browsing on the host; the window itself opens for the friend.
            if (index == Wild && PlayerStore.Instance != null) PlayerStore.Instance.daySinceLastBrowseUpgrade = 0;
            if (index == BudgetConfirm) {
                int budget, total;
                if (!CounterTrades.BeginConfirmation(Owner, NegociationUIManager.Instance, out budget, out total)) return false;
                numbers.Add(budget); numbers.Add(total);
            }
            var parameters = method.GetParameters();
            for (int i = 0; i < args.Length; i++) {
                var a = args[i]; var type = i < parameters.Length ? parameters[i].ParameterType : null;
                if (type != null && typeof(Delegate).IsAssignableFrom(type)) continue;
                if (type == typeof(bool)) continue;
                if (type == typeof(GameItem)) { var item = a as GameItem; int id = item != null ? s.World.Id(item) : 0; if (item != null && id <= 0) return notice; items.Add(id); continue; }
                if (type == typeof(string)) { var text = a as string ?? ""; texts.Add(text.Length > 4000 ? text.Substring(0, 4000) : text); continue; }
                if (type == typeof(int)) { numbers.Add(a is int ? (int)a : 0); continue; }
                return notice;
            }
            if (index == Confirm) numbers.Add(confirmToken);
            var call = new PanelCall { m = index, items = items.ToArray(), texts = texts.ToArray(), numbers = numbers.ToArray() };
            byte[] bytes = Encoding.UTF8.GetBytes(WireJson.ToJson(call));
            if (bytes.Length <= 16384) s.SendTo(Owner, Message, bytes);
            return notice;
        }

        // Friend: wait until the host's change has arrived (a used strip shows its result), then open the panel.
        internal static void FromHost(byte[] data) {
            var call = WireJson.FromJson<PanelCall>(Encoding.UTF8.GetString(data));
            if (call == null || call.m < 0 || call.m >= Methods.Length || waiting.Count >= 16) return;
            var s = Connection.Current; if (s == null) return;
            if (waiting.Count == 0) { seen = s.World.Version; due = Time.realtimeSinceStartup + 1f; }
            waiting.Add(call);
        }

        internal static void Tick(Connection s) {
            if (waiting.Count == 0 || s == null || !s.IsClient) return;
            if (s.World.Version <= seen && Time.realtimeSinceStartup < due) return;
            var calls = waiting.ToArray(); waiting.Clear();
            foreach (var call in calls) { try { Open(s, call); } catch (Exception e) { Debug.LogWarning("[ShopTogether] panel: " + e.Message); } }
        }

        internal static void Reset() { waiting.Clear(); confirms.Clear(); CounterTrades.Reset(); CustomerDeals.Reset(); ScratchInteraction.Reset(); Owner = 0; SourceItem = null; SourceIsActivation = false; nextConfirm = 0; }

        static GameItem Item(Connection s, PanelCall c, int i) {
            if (c.items == null || i >= c.items.Length || c.items[i] <= 0) return null;
            GameItem item; return s.World.Items.TryGetValue(c.items[i], out item) && item != null && !(item is GameItemElement && ((GameItemElement)item).IsDestroyed()) ? item : null;
        }
        static string Text(PanelCall c, int i) { return c.texts != null && i < c.texts.Length && c.texts[i] != null ? c.texts[i] : ""; }
        static int Number(PanelCall c, int i) { return c.numbers != null && i < c.numbers.Length ? c.numbers[i] : 0; }

        static void Open(Connection s, PanelCall c) {
            GameItem a = Item(s, c, 0), b = Item(s, c, 1); var paper = PaperUIManager.Instance;
            switch (c.m) {
                case 0: if (a != null && b != null && ChemScannerUIManager.Instance != null) ChemScannerUIManager.Instance.Open(a, b); break;
                case 1: if (a != null && b != null && WaterScannerUIManager.Instance != null) WaterScannerUIManager.Instance.Open(a, b); break;
                case 2: if (a != null && PlantAnalyserUIManager.Instance != null) PlantAnalyserUIManager.Instance.OpenUI(a); break;
                case 3: if (a != null && WaterStripUIManager.Instance != null) WaterStripUIManager.Instance.Open(a); break;
                case 4: if (RecordUIManager.Instance != null) RecordUIManager.Instance.OpenUI(); break;
                case 25: if (MultiPageUIManager.Instance != null) MultiPageUIManager.Instance.OpenUI(Number(c, 0)); break;
                case 26: if (a != null && HandnoteUIManager.Instance != null) HandnoteUIManager.Instance.OpenPanel(a); break;
                case 27: if (RepublicLogoUIManager.Instance != null) RepublicLogoUIManager.Instance.OpenPanel(); break;
                case 28: if (NewsUIManager.Instance != null) NewsUIManager.Instance.ToggleUI(); break;
                case 29: if (a != null && LogUIManager.Instance != null) LogUIManager.Instance.Toggle(a); break;
                case 31: if (a != null && ExchangeUIManager.Instance != null) ExchangeUIManager.Instance.OpenPanel(a); break;
                case 32: if (a != null && ScratcherUIManager.Instance != null) ScratcherUIManager.Instance.OpenUI(a); break;
                case 33: { var g = GenericConfirmationUIManager.Instance; if (g != null) g.OpenPanelSingleButton(Text(c, 0), Text(c, 1), delegate { g.ClosePanel(); }); break; }
                case 34: {
                    var g = GenericConfirmationUIManager.Instance; if (g == null) break; int token = Number(c, 0);
                    g.OpenPanel(Text(c, 0), Text(c, 1), Text(c, 2),
                        delegate { g.ClosePanel(); var e = RemoteActions.Adapter("ConfirmPanel", 1); if (e != null) RemoteActions.Forward(e, null, new object[] { token }); },
                        delegate { g.ClosePanel(); var e = RemoteActions.Adapter("CancelPanel", 1); if (e != null) RemoteActions.Forward(e, null, new object[] { token }); });
                    break; }
                case 35: if (a != null) ShowCratePreview(a); break;
                case 38: if (StoreUIManager.Instance != null) StoreUIManager.Instance.Notify(Text(c, 0), Text(c, 1).Length > 0 ? Text(c, 1) : "default"); break;
                case 39: if (ReputationUIManager.Instance != null) ReputationUIManager.Instance.Notify(Text(c, 0), Text(c, 1).Length > 0 ? Text(c, 1) : "default"); break;
                case 40: if (AudioManager.Instance != null) AudioManager.Instance.Play(Text(c, 0)); break;
                case 41: if (AudioManager.Instance != null) AudioManager.Instance.Play2(Text(c, 0)); break;
                case 42: if (AudioManager.Instance != null) AudioManager.Instance.Play3(Text(c, 0)); break;
                case 44: WildNetworkUI.Open(); break;
                case 45: AppraisalItemsWindow.Open(); break;
                case 46: if (DialogSettingUIManager.Instance != null) DialogSettingUIManager.Instance.OpenUI(); break;
                case 47: if (a != null) HopperHelper.OpenWindow(a); break;
                case 48: if (a != null) HopperHelper.OpenOutletWindow(a); break;
                case 49: if (a != null) MachineStorageCoordinator.OpenWindow(a); break;
                case 50: if (a != null) MindReprogrammerHelper.OpenWindow(a); break;
                case 43: {
                    var ui = NegociationUIManager.Instance; if (ui == null) break;
                    ui.OpenConfirmPanel(); var frame = ModReflect.Get<NegotiationTabletFrame>(ui, "frame");
                    if (frame != null && frame.confirmText != null)
                        frame.confirmText.text = LocHelper.GetLocalizedUI("ui_nego_budget_warning", Number(c, 0), Number(c, 1) - Number(c, 0));
                    break; }
                default:
                    if (paper == null) return;
                    switch (c.m) {
                        case 5: paper.OpenUI(Text(c, 0), Text(c, 1), Text(c, 2), Number(c, 0)); break;
                        case 6: paper.OpenContract(); break;
                        case 7: paper.OpenDeed(); break;
                        case 8: if (a != null) paper.OpenBook(a); break;
                        case 9: if (a != null) paper.OpenSlipUI(a); break;
                        case 10: if (a != null) paper.OpenGeneralSlipUI(a); break;
                        case 11: paper.OpenCigaretteGuide(); break;
                        case 12: paper.OpenStampGuide(); break;
                        case 13: paper.OpenInjectorGuide(); break;
                        case 14: paper.OpenWantedPaper(); break;
                        case 15: paper.OpenSecCard(); break;
                        case 16: paper.OpenCigaretteColor(); break;
                        case 17: paper.OpenCodeBook(); break;
                        case 18: paper.OpenBrewerCard(); break;
                        case 19: paper.OpenMiningCard(); break;
                        case 20: paper.OpenChemCard(); break;
                        case 21: paper.OpenPharmaCard(); break;
                        case 22: if (Text(c, 0).Length > 0) paper.OpenCard(Text(c, 0), a); break;
                        case 23: paper.OpenLotteryInfo(); break;
                        case 24: paper.OpenAugIdGuide(); break;
                        case 30: paper.ToggleJoeCard(); break;
                    }
                    break;
            }
        }

        // Build presentation-only silhouettes from the already replicated items. Running their directory factories
        // on a guest could roll new scratch tickets or trigger other mod gameplay merely to display the scan.
        static void ShowCratePreview(GameItem crate) {
            var response = new StoreResponse { identifier = "supplyCrateResponse", title = "Supply Crate Contents", closable = true, choices = new List<(GameItem, Action)>() };
            foreach (var item in GraphUtils.FindChildrenType<GameItem>(crate)) {
                var icon = ItemDirectory.CreateEmptyItem(item.identifier);
                if (item.useSpriteFromMod) icon.SetSpriteAndShapeFromMod(item.spriteAtlasPath, item.spritePath);
                else icon.SetSpriteAndShape(item.spriteAtlasPath, item.spritePath);
                response.choices.Add(EmporiumEntry.PrepChoice(icon, "Scanned Item", false, "", null, null, delegate { }, true, false));
            }
            BuildingCratePreview = true;
            try { EmporiumEntry.Instance.ShowResponseWindow(response); } finally { BuildingCratePreview = false; }
        }
    }

    [HarmonyPatch]
    internal static class PanelCapture {
        static IEnumerable<MethodBase> TargetMethods() { foreach (var m in PanelRelay.Methods) if (m != null) yield return m; }
        [HarmonyPriority(Priority.First)]
        static bool Prefix(MethodBase __originalMethod, object[] __args) {
            if (!PanelRelay.Capturing) return true;
            return PanelRelay.Capture(__originalMethod, __args);
        }
    }

    /// <summary>A friend's copy only shows these; the host decides scratcher luck and marks scanned crates.</summary>
    [HarmonyPatch]
    internal static class FriendShowsOnly {
        static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(ScratcherHelper), "TryRerollForBetterLuck");
            yield return AccessTools.Method(typeof(PreBuiltItemHelper), "MarkCrateScanned");
        }
        static bool Prefix() { return !ReplicaSession.ClientStore; }
    }
}
