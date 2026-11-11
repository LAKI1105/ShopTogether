using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// The storage coordinator keeps its hotkey presets on the item itself, so they are part of the shop save.
    /// The host keeps the game's own list; every friend gets a separate list on the same coordinator, stored in the
    /// host's save under that friend's Steam account (or typed name for direct connections). Each player sets their
    /// own keys and container groups, and finds them again the next time they join this shop.
    /// </summary>
    internal static class CoordinatorPresets {
        internal const int MaxJson = 30000;
        const string Prefix = "SHOPTOGETHER_COORDINATOR_PRESETS_";
        const int MaxPresets = 60, MaxItems = 300, MaxName = 80, MaxTabs = 120;

        [Serializable] internal sealed class Box { public List<MachineStorageCoordinator.Preset> presets = new List<MachineStorageCoordinator.Preset>(); }

        internal static string TagFor(string identity) { return identity == null ? null : Prefix + identity; }

        static Box Parse(string json) {
            if (string.IsNullOrEmpty(json) || json.Length > MaxJson) return null;
            try { return JsonUtility.FromJson<Box>(json); } catch { return null; }
        }

        /// <summary>Keeps only what the game's own editor could have produced.</summary>
        internal static string Clean(string json) {
            var box = Parse(json);
            if (box == null || box.presets == null) return null;
            var clean = new Box();
            foreach (var p in box.presets) {
                if (p == null || clean.presets.Count >= MaxPresets) continue;
                if (p.keyCode < 0 || p.keyCode > 1000 || !Enum.IsDefined(typeof(KeyCode), p.keyCode)) p.keyCode = 0;
                p.name = p.name ?? ""; if (p.name.Length > MaxName) p.name = p.name.Substring(0, MaxName);
                if (p.itemIds == null) p.itemIds = new List<int>();
                if (p.itemIds.Count > MaxItems) p.itemIds = p.itemIds.GetRange(0, MaxItems);
                if (p.itemTabs == null) p.itemTabs = new List<MachineStorageCoordinator.ItemTabs>();
                p.itemTabs.RemoveAll(t => t == null || t.tabs == null || t.tabs.Length > MaxTabs || !p.itemIds.Contains(t.itemId));
                clean.presets.Add(p);
            }
            string result = JsonUtility.ToJson(clean);
            return result.Length <= MaxJson ? result : null;
        }

        /// <summary>Friend side: this player's own list on the coordinator (empty until they make one).</summary>
        internal static List<MachineStorageCoordinator.Preset> Load(GameItem item, string identity) {
            string tag = TagFor(identity);
            var box = tag != null && item.IsTag(tag) ? Parse(item.GetTagReadonly(tag).GetString()) : null;
            return box != null && box.presets != null ? box.presets : new List<MachineStorageCoordinator.Preset>();
        }

        /// <summary>Host side: a friend saved their list. The friend is identified by the connection, never by what they send.</summary>
        internal static void Save(GameItem item, string json) {
            var session = Connection.Current;
            if (item == null || session == null || !session.IsHost || !item.IsTag("STORAGE_COORDINATOR_TAG")) return;
            string tag = TagFor(session.PeerIdentity(RemoteActions.CurrentOwner));
            string clean = Clean(json);
            if (tag == null || clean == null) return;
            item.ModifyTag(tag, s => s.Enable().SetString(clean));
        }

        internal static bool Friend { get { var s = Connection.Current; return s != null && s.IsClient && ReplicaSession.ClientStore; } }
    }

    [HarmonyPatch(typeof(MachineStorageCoordinator), "LoadPresets")]
    internal static class FriendLoadPresets {
        static bool Prefix(GameItem item, ref List<MachineStorageCoordinator.Preset> __result) {
            if (!CoordinatorPresets.Friend || item == null) return true;
            __result = CoordinatorPresets.Load(item, Connection.Current.MyIdentity);
            return false;
        }
    }

    [HarmonyPatch(typeof(MachineStorageCoordinator), "SavePresets")]
    internal static class FriendSavePresets {
        static bool Prefix(GameItem item, List<MachineStorageCoordinator.Preset> presets) {
            if (!CoordinatorPresets.Friend) return true;
            if (item == null || WorldReplica.Applying || !ReplicaSession.IsReplica || !LocalControl.Focused) return false;
            string tag = CoordinatorPresets.TagFor(Connection.Current.MyIdentity);
            string json = CoordinatorPresets.Clean(JsonUtility.ToJson(new CoordinatorPresets.Box { presets = presets ?? new List<MachineStorageCoordinator.Preset>() }));
            if (tag == null || json == null) { RemoteActions.Notice("~SMC200~"); return false; }
            // Shown right away here; the host stores the same text and the next sync confirms it.
            item.ModifyTag(tag, s => s.Enable().SetString(json));
            RemoteActions.Forward(RemoteActions.Adapter("Save", 2), null, new object[] { item, json });
            return false;
        }
    }
}
