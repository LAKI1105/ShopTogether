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
                foreach (var r in delta.items) if (r != null && r.id == id) {
                    // Only a move, a split/merge or a replacement ends the drag. A plain data change (value, freshness,
                    // charge...) does not: the drop sends the newest version anyway. Ending drags on every change made
                    // anything with a running timer impossible to drag on a friend's screen.
                    touched = r.parent == null || here == null || r.parent.Key != here.Key || r.node == null || r.node.unitCount != held.unitCount || r.node.identifier != held.identifier;
                    break;
                }
            }
            if (!touched) return;
            ItemMouseDragHandler.current.Cancel();
            if (delta.full) NativeActions.CancelLocal();
            RemoteActions.Notice("~SMC269~");
        }

        /// <summary>Every frame while connected: the world-click pause belongs to the choice windows only.</summary>
        internal static void Tick() {
            if (Interactable.isPaused || TooltipContainer.isPaused) {
                var e = EmporiumEntry.Instance;
                bool open = e != null && ((e.responseWindow != null && e.responseWindow.IsVisible()) || (e.responseWindowClosable != null && e.responseWindowClosable.IsVisible()));
                if (e != null && !open) {
                    Interactable.isPaused = false; TooltipContainer.isPaused = false;
                    Diagnostics.Report("click pause", new InvalidOperationException("world clicks were still paused with no choice window open; resumed"));
                }
            }
            Diagnostics.Soft("vending click", WatchVendingClicks);
        }

        // A press on the vending machine or fountain that the game's own click handling never delivered (seen on
        // friends' screens): find out what took the click, report it, and open the machine's window anyway unless a
        // visible window was in front of it.
        static int pressFrame = -1, deliveredFrame = -1; static bool pressFountain, deliveredFountain;
        internal static PixelWindow Watched; static float watchedUntil;

        static void WatchVendingClicks() {
            if (!ReplicaSession.ClientStore || !ReplicaSession.SceneReady) { pressFrame = -1; return; }
            if (pressFrame >= 0 && Time.frameCount > pressFrame + 1) {
                bool fountain = pressFountain; int frame = pressFrame; pressFrame = -1;
                if (deliveredFrame < frame || deliveredFountain != fountain) {
                    string blocker; bool covered = Blocker(fountain, out blocker);
                    Diagnostics.Report(fountain ? "fountain click" : "vending click", new InvalidOperationException("press not delivered; paused=" + Interactable.isPaused + " top=" + blocker));
                    if (!covered && !Interactable.isPaused) OpenVending(fountain);
                }
            }
            if (!Input.GetMouseButtonDown(0) || !LocalControl.Focused || Dragged() != null) return;
            bool? hit = Hit();
            if (hit == null) return;
            pressFrame = Time.frameCount; pressFountain = hit.Value;
        }

        /// <summary>Which machine (true = fountain) the mouse is over, by its picture on screen.</summary>
        static bool? Hit() {
            var camera = Camera.main; if (camera == null) return null;
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            if (Over(StoreVendingFountain.Instance != null ? StoreVendingFountain.Instance.gameObject : null, ray)) return true;
            if (Over(StoreVendingMachine.Instance != null ? StoreVendingMachine.Instance.gameObject : null, ray)) return false;
            return null;
        }
        static bool Over(GameObject machine, Ray ray) {
            if (machine == null || !machine.activeInHierarchy) return false;
            foreach (var picture in machine.GetComponentsInChildren<SpriteRenderer>()) {
                if (picture == null || !picture.enabled || picture.sprite == null) continue;
                var b = picture.bounds; b.Expand(new Vector3(0f, 0f, 0.2f));   // a flat picture still needs depth to be hit
                if (b.IntersectRay(ray)) return true;
            }
            return false;
        }
        /// <summary>What is first under the mouse; covered = a visible window or button is in front of the machine.</summary>
        static bool Blocker(bool fountain, out string name) {
            name = "nothing";
            var events = UnityEngine.EventSystems.EventSystem.current; if (events == null) { name = "no event system"; return false; }
            var pointer = new UnityEngine.EventSystems.PointerEventData(events) { position = Input.mousePosition };
            var hits = new List<UnityEngine.EventSystems.RaycastResult>(); events.RaycastAll(pointer, hits);
            if (hits.Count == 0) return false;
            var top = hits[0].gameObject; name = Path(top);
            var machine = fountain ? (StoreVendingFountain.Instance != null ? StoreVendingFountain.Instance.gameObject : null) : (StoreVendingMachine.Instance != null ? StoreVendingMachine.Instance.gameObject : null);
            if (machine != null && top != null && top.transform.IsChildOf(machine.transform)) return false;
            // Something else that takes clicks (a customer, a button) is in front: that click was meant for it.
            if (top != null && (top.GetComponentInParent<Interactable>() != null ||
                UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerDownHandler>(top) != null ||
                UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(top) != null)) return true;
            var graphic = top != null ? top.GetComponent<UnityEngine.UI.Graphic>() : null;
            if (graphic == null) return false;
            float alpha = graphic.color.a; try { alpha *= graphic.canvasRenderer.GetInheritedAlpha(); } catch { }
            return alpha > 0.05f;
        }
        static string Path(GameObject o) {
            if (o == null) return "?";
            string path = o.name; var t = o.transform.parent;
            for (int i = 0; i < 4 && t != null; i++, t = t.parent) path = t.name + "/" + path;
            return path;
        }

        /// <summary>Friend: the vending machine or fountain outside the shop was clicked.</summary>
        internal static bool OpenVending(bool fountain) {
            if (!ReplicaSession.ClientStore) return true;
            deliveredFrame = Time.frameCount; deliveredFountain = fountain;
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
                Watched = window; watchedUntil = Time.realtimeSinceStartup + 1f;
                if (!window.IsVisible()) Diagnostics.Report(fountain ? "fountain panel" : "vending panel", new InvalidOperationException("shown but not visible"));
            } catch (Exception x) { Diagnostics.Report(fountain ? "fountain panel" : "vending panel", x); }
            return false;
        }

        /// <summary>The vending window was hidden right after it was opened, not by this player: report who did it.</summary>
        internal static void Hiding(PixelWindow window) {
            if (window == null || window != Watched) return;
            Watched = null;
            if (Time.realtimeSinceStartup > watchedUntil || LocalControl.UserAction) return;
            Diagnostics.Report("vending panel", new InvalidOperationException("closed right after opening by " + new System.Diagnostics.StackTrace(2, false).ToString().Replace("\n", " | ")));
        }
    }

    [HarmonyPatch(typeof(PixelWindow), "Hide")]
    internal static class FriendVendingHide {
        static void Prefix(PixelWindow __instance) { if (FriendGuards.Watched != null) FriendGuards.Hiding(__instance); }
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
