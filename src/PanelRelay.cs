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
            };
        }

        // Host: a friend's action tried to open a panel. Keep it off the host's screen and tell the friend.
        internal static void Capture(MethodBase method, object[] args) {
            var s = Connection.Current; int index = -1;
            for (int i = 0; i < Methods.Length; i++) if (Methods[i] != null && method != null && Methods[i].DeclaringType == method.DeclaringType && Methods[i].Name == method.Name) { index = i; break; }
            if (s == null || index < 0 || args == null) return;
            var items = new List<int>(); var texts = new List<string>(); var numbers = new List<int>();
            foreach (var a in args) {
                if (a == null) { items.Add(0); continue; }
                var item = a as GameItem; if (item != null) { int id = s.World.Id(item); if (id <= 0) return; items.Add(id); continue; }
                var text = a as string; if (text != null) { texts.Add(text.Length > 4000 ? text.Substring(0, 4000) : text); continue; }
                if (a is int) { numbers.Add((int)a); continue; }
                return;
            }
            var call = new PanelCall { m = index, items = items.ToArray(), texts = texts.ToArray(), numbers = numbers.ToArray() };
            byte[] bytes = Encoding.UTF8.GetBytes(WireJson.ToJson(call));
            if (bytes.Length <= 16384) s.SendTo(Owner, Message, bytes);
        }

        // Friend: wait until the host's change has arrived (a used strip shows its result), then open the panel.
        internal static void FromHost(byte[] data) {
            var call = WireJson.FromJson<PanelCall>(Encoding.UTF8.GetString(data));
            if (call == null || call.m < 0 || call.m >= Methods.Length || waiting.Count >= 4) return;
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

        internal static void Reset() { waiting.Clear(); Owner = 0; }

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
                    }
                    break;
            }
        }
    }

    [HarmonyPatch]
    internal static class PanelCapture {
        static IEnumerable<MethodBase> TargetMethods() { foreach (var m in PanelRelay.Methods) if (m != null) yield return m; }
        [HarmonyPriority(Priority.First)]
        static bool Prefix(MethodBase __originalMethod, object[] __args) {
            if (!PanelRelay.Capturing) return true;
            PanelRelay.Capture(__originalMethod, __args);
            return false;
        }
    }
}
