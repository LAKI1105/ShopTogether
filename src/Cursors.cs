using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// Everyone sees everyone else's mouse pointer, labelled and coloured per player. Positions are sent as
    /// fractions of the screen (about 15 times a second), so different window sizes still line up.
    /// Cursors are only shown; they never click anything on another machine.
    /// </summary>
    [Serializable] internal sealed class NameList { public int[] ids; public string[] names; public string group; public int[] activities; }

    /// <summary>
    /// What each player is busy with (bargaining, appraising, reading a reference book...), shown next to their
    /// pointer and in a short list. Only a number is sent; the text comes from this fixed list.
    /// </summary>
    internal static class Activity {
        internal static readonly string[] Labels = { "", "~SMC191~", "~SMC192~", "~SMC193~", "~SMC194~", "~SMC195~", "~SMC196~", "~SMC197~", "~SMC198~", "~SMC199~" };
        internal static bool Inspecting, Commissary, Labeler, Book, Paper;
        static readonly Dictionary<int, int> others = new Dictionary<int, int>();

        internal static int Current() {
            try {
                if (StoreUIManager.Instance != null && StoreUIManager.Instance.isMagnifyingMode) return 3;
                if ((NegociationUIManager.Instance != null && NegociationUIManager.Instance.IsOpen) || (BargainUIManager.Instance != null && BargainUIManager.Instance.IsOpen)) return 1;
                if (Inspecting) return 2;
                if (Book) return 4;
                if (Paper) return 5;
                if (Commissary) return 6;
                if (Labeler) return 7;
                if (TransitUI.IsOpen) return 8;
                if (EndOfDayUIManager.Instance != null && EndOfDayUIManager.Instance.IsOpen) return 9;
            } catch { }
            return 0;
        }
        internal static bool Valid(int code) { return code >= 0 && code < Labels.Length; }
        internal static void FromHost(NameList list) {
            others.Clear();
            if (list == null || list.ids == null || list.activities == null || list.activities.Length != list.ids.Length) return;
            for (int i = 0; i < list.ids.Length; i++) if (Valid(list.activities[i])) others[list.ids[i]] = list.activities[i];
        }
        internal static string Of(int id) { int c; return others.TryGetValue(id, out c) ? Labels[c] : ""; }
        internal static void Reset() { others.Clear(); Inspecting = Commissary = Labeler = Book = Paper = false; }
        static GUIStyle line;
        internal static void Draw(int self) {
            // Grows with the screen height, like the cursors, so it stays readable above 1080p.
            float k = Mathf.Max(1f, Screen.height / 1080f);
            int font = Mathf.RoundToInt(14 * k);
            if (line == null || line.fontSize != font) line = new GUIStyle(GUI.skin.label) { fontSize = font, wordWrap = false };
            float y = 92 * k; GUI.color = Color.white;
            foreach (var p in others) {
                if (p.Key == self || p.Value == 0) continue;
                GUI.Label(new Rect(Screen.width - 420 * k, y, 410 * k, 22 * k), Loc.Text(PlayerNames.Of(p.Key) + "：" + Labels[p.Value]), line); y += 22 * k;
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(InspectionUIManager), "OpenUI")] internal static class ActInspectOpen { static void Postfix() { Activity.Inspecting = true; } }
    [HarmonyLib.HarmonyPatch(typeof(InspectionUIManager), "OnInspectItem")] internal static class ActInspectItem { static void Postfix() { Activity.Inspecting = true; } }
    [HarmonyLib.HarmonyPatch(typeof(InspectionUIManager), "CloseUI")] internal static class ActInspectClose { static void Postfix() { Activity.Inspecting = false; } }
    [HarmonyLib.HarmonyPatch(typeof(CommissaryUIManager), "OpenUI")] internal static class ActShopOpen { static void Postfix() { Activity.Commissary = true; } }
    [HarmonyLib.HarmonyPatch(typeof(CommissaryUIManager), "CloseUI")] internal static class ActShopClose { static void Postfix() { Activity.Commissary = false; } }
    [HarmonyLib.HarmonyPatch(typeof(LabelerUIManager), "OpenUI")] internal static class ActLabelOpen { static void Postfix() { Activity.Labeler = true; } }
    [HarmonyLib.HarmonyPatch(typeof(LabelerUIManager), "CloseUI")] internal static class ActLabelClose { static void Postfix() { Activity.Labeler = false; } }
    [HarmonyLib.HarmonyPatch(typeof(PaperUIManager), "OpenBook")] internal static class ActBookOpen { static void Postfix() { if (PanelRelay.Capturing) return; Activity.Book = true; Activity.Paper = false; } }
    [HarmonyLib.HarmonyPatch(typeof(PaperUIManager), "OpenUI")] internal static class ActPaperOpen { static void Postfix() { if (PanelRelay.Capturing) return; if (!Activity.Book) Activity.Paper = true; } }
    [HarmonyLib.HarmonyPatch(typeof(PaperUIManager), "CloseUI")] internal static class ActPaperClose { static void Postfix() { Activity.Book = false; Activity.Paper = false; } }

    /// <summary>Names players typed for themselves. Shown as plain text only (no markup), at most 16 characters.</summary>
    internal static class PlayerNames {
        static readonly Dictionary<int, string> names = new Dictionary<int, string>();
        internal static bool Changed;
        internal static string Clean(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            var b = new System.Text.StringBuilder();
            foreach (char c in s.Trim()) { if (char.IsControl(c) || c == '<' || c == '>') continue; b.Append(c); if (b.Length >= 16) break; }
            return b.ToString().Trim();
        }
        internal static void FromHost(NameList list) {
            if (list == null || list.ids == null || list.names == null || list.ids.Length != list.names.Length || list.ids.Length > 8) return;
            names.Clear();
            for (int i = 0; i < list.ids.Length; i++) { string n = Clean(list.names[i]); if (list.ids[i] >= 0 && list.ids[i] <= 64 && n.Length > 0) names[list.ids[i]] = n; }
        }
        internal static string Of(int id) { string n; return names.TryGetValue(id, out n) ? n : (id == 0 ? "~SMC077~" : "~SMC102~" + id); }
        internal static IEnumerable<string> All() { return new List<string>(names.Values); }
        internal static void Reset() { names.Clear(); Changed = true; }
    }

    /// <summary>A small note next to the mouse when the item you reach for is being moved by someone else.</summary>
    internal static class ItemBusyHint {
        static string text = ""; static float until; static GUIStyle style;
        internal static bool Visible { get { return Time.realtimeSinceStartup <= until; } }
        internal static string Text { get { return Loc.Text(text); } }
        internal static void Show(int owner) {
            text = owner >= 0 ? "~SMC097~" + PlayerNames.Of(owner) + "~SMC007~" : "~SMC098~";
            until = Time.realtimeSinceStartup + 2f;
        }
        internal static void Draw() {
            if (Time.realtimeSinceStartup > until || Event.current.type != EventType.Repaint) return;
            int font = Mathf.RoundToInt(16 * Mathf.Max(1f, Screen.height / 1080f));
            if (style == null || style.fontSize != font) style = new GUIStyle(GUI.skin.box) { fontSize = font, wordWrap = false, padding = new RectOffset(10, 10, 6, 6) };
            string shown = Loc.Text(text);
            var size = style.CalcSize(new GUIContent(shown));
            var m = Input.mousePosition; float x = m.x + 18, y = Screen.height - m.y + 18;
            if (x + size.x > Screen.width) x = Screen.width - size.x - 4; if (y + size.y > Screen.height) y = Screen.height - size.y - 4;
            int depth = GUI.depth; GUI.depth = -1001; GUI.Box(new Rect(x, y, size.x, size.y), shown, style); GUI.depth = depth;
        }
    }

    internal static class Cursors {
        sealed class Pointer { internal float X, Y, Seen; }
        static readonly Dictionary<int, Pointer> others = new Dictionary<int, Pointer>();
        static float next, lastSentAt; static float lastX = -1, lastY = -1;
        static GUIStyle dot, label; static float styledAt;
        // Setting: how large other players' cursors and names are drawn here. Also follows the screen height,
        // so a 1440p or 4K screen does not shrink them.
        static ConfigEntry<float> scaleSetting;
        internal const float DefaultScale = 1.5f;
        internal static void DefineSettings() {
            try { scaleSetting = ModConfig.For("shop_together").Define("cursor_scale", DefaultScale, Loc.Text("~SMC220~"), () => Loc.Text("~SMC244~"), 0.5f, 4f, () => Loc.Text("~SMC245~")); }
            catch (Exception e) { Debug.LogWarning("[ShopTogether] cursor size setting: " + e.Message); }
        }
        internal static float Scale {
            get {
                float user = DefaultScale; try { if (scaleSetting != null) user = scaleSetting.Value; } catch { }
                if (float.IsNaN(user) || float.IsInfinity(user)) user = DefaultScale;
                return Mathf.Clamp(user, 0.5f, 4f) * Mathf.Max(1f, Screen.height / 1080f);
            }
        }
        static readonly Color[] colors = { new Color(1f, .78f, .2f), new Color(.35f, .85f, 1f), new Color(.55f, 1f, .45f), new Color(1f, .45f, .75f), new Color(.75f, .55f, 1f), new Color(1f, .55f, .3f), new Color(.3f, 1f, .85f), new Color(.95f, .95f, .95f) };

        internal static bool Seen(int id) { Pointer p; return others.TryGetValue(id, out p) && Time.realtimeSinceStartup - p.Seen < 3f; }

        static int lastActivity = -1; static float lastActivitySent;
        internal static void Reset() { others.Clear(); lastX = lastY = -1; next = 0; lastActivity = -1; Activity.Reset(); }

        static bool Own(out float x, out float y) {
            x = y = 0;
            if (!LocalControl.Focused || Screen.width <= 0 || Screen.height <= 0) return false;
            var p = Input.mousePosition;
            x = Mathf.Clamp01(p.x / Screen.width); y = Mathf.Clamp01(p.y / Screen.height);
            return true;
        }

        internal static void Tick(Connection c) {
            float now = Time.realtimeSinceStartup;
            if (!c.ShowCursors || now < next) return;
            next = now + 1f / 15f;
            float x, y; bool mine = Own(out x, out y);
            int doing = Activity.Current();
            if (doing != lastActivity || now - lastActivitySent > 3f) { if (c.IsClient) c.SendActivity(doing); else if (doing != lastActivity) PlayerNames.Changed = true; lastActivity = doing; lastActivitySent = now; }
            if (c.IsClient) {
                if (!mine || (Mathf.Abs(x - lastX) < .0005f && Mathf.Abs(y - lastY) < .0005f && now - lastSentAt < 1f)) return;
                lastX = x; lastY = y; lastSentAt = now;
                var data = new byte[8]; Buffer.BlockCopy(BitConverter.GetBytes(x), 0, data, 0, 4); Buffer.BlockCopy(BitConverter.GetBytes(y), 0, data, 4, 4);
                c.SendCursor(data);
                return;
            }
            // Host: everyone's pointer (its own as player 0) to everyone.
            var list = new List<KeyValuePair<int, Pointer>>();
            if (mine) list.Add(new KeyValuePair<int, Pointer>(0, new Pointer { X = x, Y = y }));
            foreach (var p in others) if (now - p.Value.Seen < 3f) list.Add(p);
            if (list.Count > 8) list.RemoveRange(8, list.Count - 8);
            var bytes = new byte[4 + 12 * list.Count];
            Buffer.BlockCopy(BitConverter.GetBytes(list.Count), 0, bytes, 0, 4);
            for (int i = 0; i < list.Count; i++) {
                Buffer.BlockCopy(BitConverter.GetBytes(list[i].Key), 0, bytes, 4 + 12 * i, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(list[i].Value.X), 0, bytes, 8 + 12 * i, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(list[i].Value.Y), 0, bytes, 12 + 12 * i, 4);
            }
            c.BroadcastCursors(bytes);
        }

        static bool Valid(float v) { return ProtocolGuard.Finite(v) && v >= 0f && v <= 1f; }

        internal static void FromFriend(int id, byte[] data) {
            if (data == null || data.Length != 8) return;
            float x = BitConverter.ToSingle(data, 0), y = BitConverter.ToSingle(data, 4);
            if (!Valid(x) || !Valid(y)) return;
            Pointer p; if (!others.TryGetValue(id, out p)) others[id] = p = new Pointer();
            p.X = x; p.Y = y; p.Seen = Time.realtimeSinceStartup;
        }

        internal static void FromHost(byte[] data, int self) {
            if (data == null || data.Length < 4) return;
            int count = BitConverter.ToInt32(data, 0);
            if (count < 0 || count > 8 || data.Length != 4 + 12 * count) return;
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < count; i++) {
                int id = BitConverter.ToInt32(data, 4 + 12 * i);
                float x = BitConverter.ToSingle(data, 8 + 12 * i), y = BitConverter.ToSingle(data, 12 + 12 * i);
                if (id == self || id < 0 || id > 64 || !Valid(x) || !Valid(y)) continue;
                Pointer p; if (!others.TryGetValue(id, out p)) others[id] = p = new Pointer();
                p.X = x; p.Y = y; p.Seen = now;
            }
        }

        internal static void Draw(Connection c) {
            if (c.ShowCursors) ItemBusyHint.Draw();
            if (!c.ShowCursors || others.Count == 0 || Event.current.type != EventType.Repaint) return;
            float k = Scale;
            if (dot == null || styledAt != k) {
                styledAt = k;
                dot = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26 * k), alignment = TextAnchor.MiddleCenter };
                label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * k), fontStyle = FontStyle.Bold, wordWrap = false };
            }
            float now = Time.realtimeSinceStartup; var old = GUI.color; int depth = GUI.depth; GUI.depth = -1000;
            foreach (var p in others) {
                if (now - p.Value.Seen > 3f) continue;
                float sx = p.Value.X * Screen.width, sy = (1f - p.Value.Y) * Screen.height;
                var color = colors[Math.Abs(p.Key) % colors.Length];
                string doing = Activity.Of(p.Key); string name = PlayerNames.Of(p.Key) + (doing.Length > 0 ? "・" + doing : "");
                string arrow = Loc.Text("➤"), shown = Loc.Text(name);
                GUI.color = new Color(0, 0, 0, .7f); GUI.Label(new Rect(sx - 11 * k, sy - 13 * k, 30 * k, 30 * k), arrow, dot); GUI.Label(new Rect(sx + 15 * k, sy + 9 * k, 260 * k, 24 * k), shown, label);
                GUI.color = color; GUI.Label(new Rect(sx - 13 * k, sy - 15 * k, 30 * k, 30 * k), arrow, dot); GUI.Label(new Rect(sx + 14 * k, sy + 8 * k, 260 * k, 24 * k), shown, label);
            }
            GUI.color = old; GUI.depth = depth;
        }
    }
}
