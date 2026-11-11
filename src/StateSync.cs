using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ShopTogether {
    /// <summary>
    /// The store's own data (cash, reputation, services...) is small, but the game also keeps a text copy of every
    /// inventory in it for saving (mainInvJSON and friends, several megabytes in a long run) plus the save system's
    /// feature buffer. Items are synced on their own, so those copies are left out of every update: blanked for the
    /// moment of serialization on the host, and left untouched on the friend's machine.
    /// </summary>
    internal static class StoreState {
        static readonly ES3Settings Settings = new ES3Settings(false) { prettyPrint = false };
        // Also kept per machine: the customer and item shown at this player's counter (built from the host's
        // view by StorePresentation), the UI click locks, and whether this player's wall poster is pulled up.
        static readonly HashSet<string> Local = new HashSet<string> { "currentClientInstance", "currentNegociatedItem", "lockAllFlag", "lockClientClickFlag", "lockStorePhysicalFlag", "lockAllInventoryFlag", "lockNextBellFlag", "lockShutterButtonFlag", "storePosterIsPulled", "playerLocation" };

        // Declared after Local: static fields start in source order and Heavy() reads Local.
        static readonly FieldInfo[] heavy = Heavy();

        static FieldInfo[] Heavy() {
            var list = new List<FieldInfo>();
            foreach (var f in typeof(PlayerStore).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                if (f.IsInitOnly || f.IsLiteral) continue;
                if ((f.FieldType == typeof(string) && f.Name.EndsWith("InvJSON", StringComparison.Ordinal)) || (f.Name == "savedItemFeatureList" && f.FieldType == typeof(List<ItemFeature>)) || Local.Contains(f.Name)) list.Add(f);
            }
            return list.ToArray();
        }

        static object Blank(FieldInfo f) {
            if (f.FieldType == typeof(List<ItemFeature>)) return new List<ItemFeature>();
            if (f.FieldType == typeof(int)) return 0;
            if (f.FieldType == typeof(bool)) return false;
            return null;   // references; a value-type field set to null becomes its default
        }

        internal static byte[] Serialize(PlayerStore store) {
            var saved = new object[heavy.Length];
            for (int i = 0; i < heavy.Length; i++) { saved[i] = heavy[i].GetValue(store); heavy[i].SetValue(store, Blank(heavy[i])); }
            try { return ES3.Serialize(store, Settings); }
            finally { for (int i = 0; i < heavy.Length; i++) heavy[i].SetValue(store, saved[i]); }
        }

        internal static void Apply(PlayerStore store, byte[] bytes) {
            var saved = new object[heavy.Length];
            for (int i = 0; i < heavy.Length; i++) saved[i] = heavy[i].GetValue(store);
            try { ES3.DeserializeInto(bytes, store, Settings); }
            finally { for (int i = 0; i < heavy.Length; i++) heavy[i].SetValue(store, saved[i]); }
        }

        internal static byte[] SerializeStation(StoreStation station) { return ES3.Serialize(station, Settings); }
        internal static void ApplyStation(StoreStation station, byte[] bytes) { ES3.DeserializeInto(bytes, station, Settings); }
    }

    /// <summary>Host: an item that just changed (moved, tag or shape changed) is compared first on the next update.</summary>
    [HarmonyPatch(typeof(GameItem), "ValidateShapeState")]
    internal static class ItemChanged {
        static void Postfix(GameItem __instance) {
            var s = Connection.Current;
            if (s != null && s.IsHost && !ReplicaSession.ClientStore) s.World.MarkDirty(__instance);
        }
    }
}
