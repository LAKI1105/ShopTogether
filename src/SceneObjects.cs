using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    [Serializable] internal sealed class LampReadout {
        public string path;
        public int state, clicks;
        public float cooldown;
        public bool broken, light, low, high;
    }
    internal static class SceneObjects {
        static string Path(Transform t) {
            var parts = new List<string>();
            while (t != null) { parts.Add(t.name + "#" + t.GetSiblingIndex()); t = t.parent; }
            parts.Reverse(); return string.Join("/", parts);
        }
        static IEnumerable<StoreLamp> Lamps() {
            foreach (var lamp in Resources.FindObjectsOfTypeAll<StoreLamp>())
                if (lamp != null && lamp.gameObject.scene.IsValid()) yield return lamp;
        }
        internal static LampReadout[] Capture() {
            var values = new List<LampReadout>();
            foreach (var lamp in Lamps()) values.Add(new LampReadout {
                path = Path(lamp.transform), state = ModReflect.Get<int>(lamp, "currentState"), clicks = ModReflect.Get<int>(lamp, "clickCount"),
                cooldown = ModReflect.Get<float>(lamp, "clickCooldownTimer"), broken = ModReflect.Get<bool>(lamp, "isBroken"),
                light = lamp.light != null && lamp.light.activeSelf, low = lamp.low != null && lamp.low.activeSelf, high = lamp.high != null && lamp.high.activeSelf
            });
            return values.ToArray();
        }
        internal static void Apply(LampReadout[] values) {
            if (values == null) return;
            foreach (var lamp in Lamps()) foreach (var r in values) if (r != null && Path(lamp.transform) == r.path) {
                AccessTools.Field(typeof(StoreLamp), "currentState").SetValue(lamp, r.state);
                AccessTools.Field(typeof(StoreLamp), "clickCount").SetValue(lamp, r.clicks);
                AccessTools.Field(typeof(StoreLamp), "clickCooldownTimer").SetValue(lamp, r.cooldown);
                AccessTools.Field(typeof(StoreLamp), "isBroken").SetValue(lamp, r.broken);
                if (lamp.light != null) lamp.light.SetActive(r.light);
                if (lamp.low != null) lamp.low.SetActive(r.low);
                if (lamp.high != null) lamp.high.SetActive(r.high);
                break;
            }
        }
        internal static void LampClick(string path) {
            foreach (var lamp in Lamps()) if (Path(lamp.transform) == path && lamp.gameObject.activeInHierarchy) {
                if (ModReflect.Get<bool>(lamp, "isBroken")) { AuthorityIntent.Refuse("~SMC350~"); return; }
                lamp.OnClick(); return;
            }
            AuthorityIntent.Refuse("~SMC095~");
        }
        internal static void DebugBellClick(string path) {
            if (PlayerStore.Instance == null || !PlayerStore.Instance.devMode) { AuthorityIntent.Refuse("~SMC351~"); return; }
            foreach (var bell in Resources.FindObjectsOfTypeAll<StoreDebugBell>())
                if (bell != null && bell.gameObject.scene.IsValid() && bell.gameObject.activeInHierarchy && Path(bell.transform) == path) { bell.OnClick(); return; }
            AuthorityIntent.Refuse("~SMC095~");
        }
        [HarmonyPatch(typeof(StoreLamp), "OnClick")]
        internal static class Click {
            static bool Prefix(StoreLamp __instance) {
                if (!ReplicaSession.ClientStore) return true;
                if (ReplicaSession.IsReplica && LocalControl.UserAction)
                    RemoteActions.Forward(RemoteActions.Adapter("LampClick", 1), null, new object[] { Path(__instance.transform) });
                return false;
            }
        }
        [HarmonyPatch(typeof(StoreLamp), "Update")]
        internal static class Clock { static bool Prefix() { return !ReplicaSession.ClientStore; } }
        [HarmonyPatch(typeof(StoreDebugBell), "OnClick")]
        internal static class DebugBell {
            static bool Prefix(StoreDebugBell __instance) {
                if (!ReplicaSession.ClientStore) return true;
                if (LocalControl.UserAction) RemoteActions.Forward(RemoteActions.Adapter("DebugBellClick", 1), null, new object[] { Path(__instance.transform) });
                return false;
            }
        }
    }
}
