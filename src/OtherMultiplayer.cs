using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// 深空串门 | Probably Multiplayer (Lemocuber): separate shops visit each other through that mod's own server, and
    /// a visitor appears as a customer at your counter. That cannot work inside a shared shop: on a friend's screen the
    /// shop is a copy of the host's, and a visit there would trade with that copy; on the host, its visitor blocks
    /// the shop's trades while friends are trying to use them.
    /// So while a shared shop is in use (the host has a friend connected, or this player is a friend), that mod is
    /// paused with its own pause switch, which it also uses after a failed visit. It is resumed when the shared shop
    /// ends. Nothing of it is changed otherwise; if its insides change in a later version, nothing happens here.
    /// </summary>
    internal static class OtherMultiplayer {
        internal const string ModId = "lemocuber_probablymultiplayer";
        static bool looked; static FieldInfo pausedField; static MethodInfo disconnect;
        static bool pausedByUs, noticeShown, alreadyPaused;

        static bool Find() {
            if (looked) return pausedField != null;
            looked = true;
            try {
                var runtime = AccessTools.TypeByName("ProbablyMultiplayer.MultiplayerRuntime");
                if (runtime == null) return false;
                var field = AccessTools.Field(runtime, "MultiplayerPaused");
                if (field == null || field.FieldType != typeof(bool) || !field.IsStatic) { Debug.LogWarning("[ShopTogether] Probably Multiplayer found, but its pause switch was not: it is left running."); return false; }
                pausedField = field;
                disconnect = AccessTools.Method(runtime, "Disconnect", new[] { typeof(bool), typeof(bool) });
            } catch (Exception e) { Debug.LogWarning("[ShopTogether] Probably Multiplayer check: " + e.Message); pausedField = null; }
            return pausedField != null;
        }

        static bool Paused { get { try { return (bool)pausedField.GetValue(null); } catch { return false; } } set { try { pausedField.SetValue(null, value); } catch { } } }

        /// <summary>Every frame: keep that mod paused while the shared shop is in use (it un-pauses itself when a save
        /// loads, which a friend's copy of the shop does), and resume it afterwards.</summary>
        internal static void Tick(Connection session) {
            if (!Find()) return;
            bool shared = session != null && (session.IsClient || (session.IsHost && session.HasFriends));
            if (shared) {
                if (!pausedByUs) alreadyPaused = Paused;   // it pauses itself after a failed visit: leave that as it was
                if (!Paused) {
                    // End its connection (and a visit in progress) the way it does itself, then hold it paused.
                    try { if (disconnect != null) disconnect.Invoke(null, new object[] { false, true }); } catch (Exception e) { Debug.LogWarning("[ShopTogether] pausing Probably Multiplayer: " + e.Message); }
                    Paused = true;
                    if (!pausedByUs) Debug.Log("[ShopTogether] Probably Multiplayer paused while the shop is shared.");
                }
                pausedByUs = true;
                if (!noticeShown && StoreUIManager.Instance != null && EmporiumEntry.Instance != null) { noticeShown = true; StoreUIManager.Instance.Notify(Loc.Text("~SMC266~")); }
            } else if (pausedByUs) {
                pausedByUs = false; noticeShown = false;
                if (alreadyPaused) return;
                Paused = false;
                Debug.Log("[ShopTogether] Probably Multiplayer resumed.");
                if (StoreUIManager.Instance != null && EmporiumEntry.Instance != null) StoreUIManager.Instance.Notify(Loc.Text("~SMC267~"));
            }
        }
    }
}
