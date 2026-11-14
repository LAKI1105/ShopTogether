using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// Problems this mod ran into (its own reports, friends' reports on the host, game errors during a session) are kept
    /// in a short list that survives restarts. One button copies them, with personal details removed, and opens the
    /// mod's Workshop page so the player can paste them as a comment. Nothing is sent anywhere by itself.
    /// </summary>
    internal static class IssueReport {
        const ulong WorkshopId = 3837477869UL;
#if LAN_PROBE
        // A test process must never touch the player's real report. The runner assigns a unique, disposable key.
        static string Key { get { var key=Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_REPORT_KEY"); if(string.IsNullOrEmpty(key)||!key.StartsWith("shop_together.probe.",StringComparison.Ordinal)) throw new InvalidOperationException("Missing isolated report key"); return key; } }
#else
        const string Key = "shop_together.issue_report";
#endif
        const int MaxLines = 30, MaxComment = 990;
        static List<string> lines;
        static bool adding;

        static List<string> Lines() {
            if (lines != null) return lines;
            lines = new List<string>();
            try { string saved = PlayerPrefs.GetString(Key, ""); if (saved.Length > 0) lines.AddRange(saved.Split('\n')); } catch { }
            lines.RemoveAll(l => string.IsNullOrEmpty(l));
            return lines;
        }

        internal static int Count { get { return Lines().Count; } }
#if LAN_PROBE
        internal static void ProbeDeleteSaved() { try { PlayerPrefs.DeleteKey(Key); PlayerPrefs.Save(); } catch { } }
        internal static Action<string> ProbeClipboard;
        internal static Action<ulong> ProbeOpenPage;
#endif

        internal static void Add(string text) {
            if (adding || string.IsNullOrEmpty(text)) return;
            adding = true;   // never re-enter from the log callback
            try {
                text = Clean(text); if (text.Length > 300) text = text.Substring(0, 300);
                var list = Lines();
                list.Remove(text); list.Add(text);    // the same problem again moves to the end, once
                while (list.Count > MaxLines) list.RemoveAt(0);
                Save();
            } catch { } finally { adding = false; }
        }

        static void Save() {
            try { PlayerPrefs.SetString(Key, string.Join("\n", Lines().ToArray())); PlayerPrefs.Save(); } catch { }
        }

        /// <summary>Removes what identifies the player or their computer: user folders, addresses, Steam ids, names.</summary>
        static string Clean(string text) {
            var b = new StringBuilder(); foreach (char c in text) b.Append(char.IsControl(c) ? ' ' : c);
            text = b.ToString();
            // User names can contain spaces; consume the whole path component rather than stopping at whitespace.
            text = Regex.Replace(text, @"(?i)\b[A-Za-z]:[\\/]+Users[\\/]+[^\\/]+", "~");
            text = Regex.Replace(text, @"(?i)/(home|Users)/[^/\\]+", "~");
            text = Regex.Replace(text, @"\b7656\d{13}\b", "<steam id>");
            text = Regex.Replace(text, @"\[[0-9a-fA-F:.]+(?:%[^\]\s]+)?\](?::\d{1,5})?", "<address>");
            text = Regex.Replace(text, @"(?<![A-Za-z0-9_:])(?:[0-9a-fA-F]{0,4}:){2,}[0-9a-fA-F:.]{0,39}(?:%[A-Za-z0-9_.-]+)?(?![A-Za-z0-9_:])", "<address>");
            text = Regex.Replace(text, @"\b\d{1,3}(\.\d{1,3}){3}(:\d+)?\b", "<address>");
            foreach (var name in Names()) if (!string.IsNullOrWhiteSpace(name)) {
                string pattern=Regex.Escape(name.Trim());
                if(name.Trim().Length==1) pattern=@"(?<![\p{L}\p{N}_])"+pattern+@"(?![\p{L}\p{N}_])";
                text=Regex.Replace(text,pattern,"<player>",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            }
            return Regex.Replace(text, @"\s+", " ").Trim();
        }
        internal static string Sanitize(string text) { try { return Clean(text ?? ""); } catch { return "<redacted>"; } }

        static IEnumerable<string> Names() {
            var names = new List<string>();
            try { var s = Connection.Current; if (s != null && !string.IsNullOrEmpty(s.ConnectedNames)) foreach (var n in s.ConnectedNames.Split(new[] { ',', '、', '，' }, StringSplitOptions.RemoveEmptyEntries)) names.Add(n.Trim()); } catch { }
            try { names.AddRange(PlayerNames.All()); names.Add(Connection.MyName); } catch { }
            try { names.Add(Environment.UserName); } catch { }
            try { if (SteamManager.Initialized) names.Add(Steamworks.SteamFriends.GetPersonaName()); } catch { }
            return names;
        }

        static string Text() {
            var s = Connection.Current;
            string role = s == null ? "none" : s.IsHost ? "host" : s.IsClient ? "friend" : "none";
            var head = new StringBuilder();
            head.Append("[ShopTogether ").Append(Connection.BuildVersion).Append(" report] game ").Append(Application.version)
                .Append(" | ").Append(role).Append(" | mods ").Append(ModCount()).Append('\n');
            // Newest first, as many as fit in one Workshop comment.
            var list = Lines(); var body = new StringBuilder();
            for (int i = list.Count - 1; i >= 0; i--) {
                string line = "- " + Clean(list[i]) + "\n";
                if (head.Length + body.Length + line.Length > MaxComment) { if (body.Length == 0) body.Append(line.Substring(0, Math.Max(0, MaxComment - head.Length))); break; }
                body.Append(line);
            }
            return head.ToString() + body.ToString();
        }

        static int ModCount() { int n = 0; try { foreach (var m in ModLoader.Mods) if (m != null && m.IsEnabled) n++; } catch { } return n; }

        /// <summary>The button: copy the report, open the Workshop page, and start a fresh list.</summary>
        internal static void CopyAndOpen() {
            if (Count == 0) { Tell("~SMC300~"); return; }
            string report=Text();
#if LAN_PROBE
            if(ProbeClipboard!=null)ProbeClipboard(report);else GUIUtility.systemCopyBuffer=report;
#else
            GUIUtility.systemCopyBuffer=report;
#endif
            Tell("~SMC299~");
            try {
#if LAN_PROBE
                if(ProbeOpenPage!=null)ProbeOpenPage(WorkshopId);else ModWorkshop.OpenItemPage(WorkshopId);
#else
                ModWorkshop.OpenItemPage(WorkshopId);
#endif
            }
            catch (Exception error) { Diagnostics.Report("open workshop",error); return; }
            Lines().Clear(); Save();
            Connection.RedrawLauncher();
        }

        const string WindowId = "shop_together.issue_report";
        static void Tell(string text) {
            try {
                CustomUIManager.Window(WindowId, Loc.Text("~SMC298~"), CustomUIManager.LAYER_MENU).SetSize(460, 200).Center()
                    .AddLabel(Loc.Text(text)).AddButton(Loc.Text("~SMC182~"), () => CustomUIManager.Instance.CloseWindow(WindowId)).Show();
            } catch { RemoteActions.Notice(text); }
        }
    }
}
