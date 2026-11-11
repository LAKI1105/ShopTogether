using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// Mod setting "unlock Steam achievements while playing together". Achievements come from the game the host runs;
    /// with the setting on, every friend unlocks the same achievement on their own Steam account at the same moment.
    /// With it off, this computer unlocks nothing during a multiplayer session (playing alone is not affected).
    /// Each player decides for their own account.
    /// </summary>
    internal static class Achievements {
        internal const byte Message = 42;
        static ConfigEntry<bool> setting;
        static readonly MethodInfo unlock = typeof(AchievementTracker).GetMethod("Unlock", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo increment = typeof(AchievementTracker).GetMethod("IncrementCountAndUnlock", BindingFlags.NonPublic | BindingFlags.Instance);
        static bool relaying;

        internal static void Define() {
            try {
                setting = ModConfig.For("shop_together").Define("multiplayer_achievements", true, Loc.Text("~SMC220~"),
                    () => Loc.Text("~SMC221~"),
                    () => Loc.Text("~SMC222~"));
            } catch (Exception e) { Debug.LogWarning("[ShopTogether] achievement setting: " + e.Message); }
        }
        internal static bool Enabled { get { return setting == null || setting.Value; } }
        static bool Together { get { var s = Connection.Current; return s != null && (s.IsHost || s.IsClient); } }

        /// <summary>Friend side: the host just unlocked or counted towards an achievement.</summary>
        internal static void FromHost(byte[] data) {
            var tracker = AchievementTracker.Instance;
            if (!Enabled || tracker == null || data == null || data.Length > 512) return;
            var parts = Encoding.UTF8.GetString(data).Split('|');
            relaying = true;
            try {
                int threshold;
                if (parts.Length == 2 && parts[0] == "U" && Name(parts[1]) && unlock != null) unlock.Invoke(tracker, new object[] { parts[1] });
                else if (parts.Length == 4 && parts[0] == "S" && Name(parts[1]) && Name(parts[3]) && int.TryParse(parts[2], out threshold) && increment != null) increment.Invoke(tracker, new object[] { parts[1], threshold, parts[3] });
            } catch (Exception e) { Debug.LogWarning("[ShopTogether] achievement: " + e.Message); }
            finally { relaying = false; }
        }
        static bool Name(string s) { if (string.IsNullOrEmpty(s) || s.Length > 96) return false; foreach (char c in s) if (!(char.IsLetterOrDigit(c) || c == '_')) return false; return true; }

        static bool Allow(string text) {
            if (!Together) return true;
            var s = Connection.Current;
            if (s.IsHost && !relaying) s.BroadcastAchievement(text);
            if (!Enabled) return false;
            return s.IsHost || relaying;      // a friend's own copy of the game only unlocks what the host unlocked
        }

        [HarmonyPatch]
        internal static class UnlockGate {
            static MethodBase TargetMethod() { return unlock; }
            static bool Prepare() { return unlock != null; }
            [HarmonyPriority(Priority.First)]
            static bool Prefix(string apiName) { return Allow("U|" + apiName); }
        }
        [HarmonyPatch]
        internal static class CountGate {
            static MethodBase TargetMethod() { return increment; }
            static bool Prepare() { return increment != null; }
            [HarmonyPriority(Priority.First)]
            static bool Prefix(string statName, int threshold, string achApiName) { return Allow("S|" + statName + "|" + threshold + "|" + achApiName); }
        }
    }
}
