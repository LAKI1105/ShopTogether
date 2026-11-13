using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// Friend-side safety nets found in playtests:
    /// the vending machine and water fountain outside the shop open their own panel on a friend's screen too,
    /// an item held on the mouse is put back before the host's update moves or replaces it (the host sleeping and
    /// starting the next day resends the whole shop), and the game's "world clicks paused while a choice window is
    /// open" switch never stays on after that window is gone.
    /// </summary>
    internal static class FriendGuards {
        static readonly FieldInfo dragItem = AccessTools.Field(typeof(ItemMouseDragHandler), "currentItem");
        static readonly FieldInfo dragging = AccessTools.Field(typeof(ItemMouseDragHandler), "isDragging");

        internal static GameItem Dragged(bool whileDragging = true) {
            var handler = ItemMouseDragHandler.current;
            if (handler == null || dragItem == null || dragging == null || (whileDragging && !(bool)dragging.GetValue(handler))) return null;
            return dragItem.GetValue(handler) as GameItem;
        }

        /// <summary>Before an update is applied: if it moves, replaces or removes what this player holds, put it back first.</summary>
        internal static void BeforeApply(WorldReplica world, WorldDelta delta) {
            if (!ReplicaSession.ClientStore || delta == null) return;
            var held = Dragged(); if (held == null) return;
            int id = world.Id(held); if (id <= 0) return;
            bool touched = delta.full;
            if (!touched && delta.removed != null) touched = Array.IndexOf(delta.removed, id) >= 0;
            if (!touched && delta.items != null) {
                var here = world.Address(held.parentInventory);
                foreach (var r in delta.items) if (r != null && r.id == id) { touched = r.parent == null || here == null || r.parent.Key != here.Key || r.node == null || r.node.unitCount != held.unitCount; break; }
            }
            if (!touched) return;
            ItemMouseDragHandler.current.Cancel();
            if (delta.full) NativeActions.CancelLocal();
            RemoteActions.Notice("~SMC269~");
        }

        /// <summary>Every frame while connected: the world-click pause belongs to the choice windows only.</summary>
        internal static void Tick() {
            if (!Interactable.isPaused && !TooltipContainer.isPaused) return;
            var e = EmporiumEntry.Instance; if (e == null) return;
            bool open = (e.responseWindow != null && e.responseWindow.IsVisible()) || (e.responseWindowClosable != null && e.responseWindowClosable.IsVisible());
            if (open) return;
            Interactable.isPaused = false; TooltipContainer.isPaused = false;
            Diagnostics.Report("click pause", new InvalidOperationException("world clicks were still paused with no choice window open; resumed"));
        }

        /// <summary>Friend: the vending machine or fountain outside the shop was clicked.</summary>
        internal static bool OpenVending(bool fountain) {
            if (!ReplicaSession.ClientStore) return true;
            var e = EmporiumEntry.Instance; if (e == null) return false;
            var slot = fountain ? e.vendingFountainElement : e.vendingMachineElement;
            var item = slot != null ? slot.childItem : null;
            var window = item != null ? item.contentWindow : null;
            if (window == null) {
                RemoteActions.Notice("~SMC268~");
                Diagnostics.Report(fountain ? "fountain click" : "vending click", new InvalidOperationException(slot == null ? "no slot" : item == null ? "slot is empty" : "machine has no panel"));
                var session = Connection.Current; if (session != null) session.SendToHost(24, new byte[0]);
                return false;
            }
            try {
                if (window.isLocked) Diagnostics.Report(fountain ? "fountain click" : "vending click", new InvalidOperationException("panel was locked"));
                window.isLocked = false;
                window.Show(); window.Validate(); window.Center(); window.ToFront(true);
            } catch (Exception x) { Diagnostics.Report(fountain ? "fountain panel" : "vending panel", x); }
            return false;
        }
    }

    [HarmonyPatch(typeof(StoreVendingFountain), "OnClick")]
    internal static class FriendFountainClick {
        [HarmonyPriority(Priority.First)] static bool Prefix() { return FriendGuards.OpenVending(true); }
    }

    [HarmonyPatch(typeof(StoreVendingMachine), "OnClick")]
    internal static class FriendVendingClick {
        [HarmonyPriority(Priority.First)] static bool Prefix() { return FriendGuards.OpenVending(false); }
    }

    /// <summary>
    /// Friend: the game drops a held item that ended up in no container by destroying it. A shared item is never
    /// destroyed here (the host decides); it is reported and the whole shop is asked for again, which puts it back.
    /// </summary>
    [HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
    internal static class FriendDragEnd {
        static void Prefix(ref GameItem __state) { __state = ReplicaSession.ClientStore ? FriendGuards.Dragged(false) : null; }
        static void Postfix(GameItem __state) {
            if (__state == null || __state.parentInventory != null) return;
            var session = Connection.Current; if (session == null || session.World.Id(__state) <= 0) return;
            Diagnostics.Report("drag end", new InvalidOperationException(__state.identifier + " was left outside every container"));
            RemoteActions.Notice("~SMC269~");
            session.SendToHost(24, new byte[0]);
        }
    }
}
