using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ShopTogether {
    // A friend's errors also land in the host's Player.log, so a problem on another PC can be read from the host's log.
    // Display-only work on the friend (cursors, panels, notices) is wrapped here: if it fails it is reported, not a disconnect.
    internal static class Diagnostics {
        internal const byte Message = 44;
        internal const int Limit = 2048;
        static readonly Queue<string> waiting = new Queue<string>();
        static readonly Dictionary<string, int> seen = new Dictionary<string, int>();
        static float nextSend;

        static bool watching;
        /// <summary>Friend: errors the game itself logs (outside the mod's own code) are also sent to the host's log.</summary>
        internal static void Watch() {
            if (watching) return; watching = true;
            Application.logMessageReceived += (message, stack, type) => {
                if (type != LogType.Exception && type != LogType.Error) return;
                var session = Connection.Current; if (session == null || (!session.IsClient && !session.IsHost)) return;
                string first = (stack ?? "").Split('\n')[0].Trim();
                string key = "log|" + message;
                int count; seen.TryGetValue(key, out count); seen[key] = count + 1;
                if (seen.Count > 128) seen.Clear();
                if (count > 0) return;                     // the same error every frame is kept once
                string detail=type + ": " + message + (first.Length > 0 ? " @ " + first : "");
                IssueReport.Add("game " + detail);
                if (!session.IsClient || waiting.Count >= 16) return;
                waiting.Enqueue(IssueReport.Sanitize(detail));
            };
        }

        internal static void Soft(string where, Action action) {
            try { action(); } catch (Exception e) { Report(where, e); }
        }

        internal static void Report(string where, Exception e) {
            if (e == null) return;
            string key = where + "|" + e.GetType().Name + "|" + e.Message;
            int count; seen.TryGetValue(key, out count); seen[key] = count + 1;
            if (seen.Count > 128) seen.Clear();
            if (count > 0) return;                         // the same failure every frame is logged once
            string text = where + ": " + e.GetType().Name + " " + e.Message + " @ " + Frames(e);
            Debug.LogWarning("[ShopTogether] " + text);
            IssueReport.Add(where + ": " + e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e));
            var session = Connection.Current; if (session != null && session.IsClient && waiting.Count < 16) waiting.Enqueue(IssueReport.Sanitize(text));
        }

        /// <summary>One report a second; everything still waiting (up to a few) when the session is ending.</summary>
        internal static void Flush(Connection connection, bool ending = false) {
            if (waiting.Count == 0 || (!ending && Time.realtimeSinceStartup < nextSend)) return;
            nextSend = Time.realtimeSinceStartup + 1;
            var batch = new List<string>();
            if (ending) { var all = new List<string>(waiting); waiting.Clear(); batch.Add(all[all.Count - 1]); for (int i = 0; i < all.Count - 1 && batch.Count < 4; i++) batch.Add(all[i]); }   // why it ended goes first
            else batch.Add(waiting.Dequeue());
            foreach (var text in batch) {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                if (bytes.Length > Limit) Array.Resize(ref bytes, Limit);
                connection.SendToHost(Message, bytes);
            }
        }

        internal static void FromFriend(string name, byte[] data) {
            var text = new StringBuilder();
            foreach (char c in Encoding.UTF8.GetString(data)) text.Append(char.IsControl(c) ? ' ' : c);
            Debug.LogWarning("[ShopTogether] friend " + name + " reported: " + text);
            IssueReport.Add("friend: " + text);
        }

        internal static void Reset() { waiting.Clear(); seen.Clear(); nextSend = 0; }

        static string FirstFrame(Exception e) { string t = (e.StackTrace ?? "").Split('\n')[0].Trim(); return t.Length > 0 ? t : "?"; }

        static string Frames(Exception e) {
            string trace = e.StackTrace ?? "";
            var lines = trace.Split('\n');
            var kept = new StringBuilder();
            for (int i = 0; i < lines.Length && i < 4; i++) { if (kept.Length > 0) kept.Append(" <- "); kept.Append(lines[i].Trim()); }
            return kept.ToString();
        }
    }
}
