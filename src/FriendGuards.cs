using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    // Native validation resets the held picture as well as the inventory picture. A host data update
    // must refresh the latter without losing the guest's drag position, rotation or picked-up count.
    [HarmonyPatch(typeof(GameItemElement), "ValidateSelect")]
    internal static class ReplicaHeldPicture {
        internal struct Picture {
            internal int count;
            internal Vector2 position, anchorMin, anchorMax, pivot;
            internal Vector3 scale, angles;
        }
        static void Prefix(GameItemElement __instance, ref bool updateBackground, out Picture __state) {
            __state = default(Picture);
            if (!ReplicaSession.ClientStore || !WorldReplica.Applying || !__instance.fakeBackground.image.enabled) return;
            int count = FriendGuards.DragCount(__instance); if (count <= 0) return;
            var rotator = __instance.fakeRotator.rectTransform;
            __state = new Picture { count = count, position = __instance.fakeMiddle.rectTransform.anchoredPosition,
                anchorMin = rotator.anchorMin, anchorMax = rotator.anchorMax, pivot = rotator.pivot,
                scale = rotator.localScale, angles = rotator.localEulerAngles };
            updateBackground = false;
        }
        static void Postfix(GameItemElement __instance, Picture __state) {
            if (__state.count <= 0 || __instance.IsDestroyed()) return;
            __instance.UpdateFakeReal(__state.count);
            var rotator = __instance.fakeRotator.rectTransform;
            rotator.anchorMin = __state.anchorMin; rotator.anchorMax = __state.anchorMax;
            rotator.pivot = __state.pivot; rotator.localScale = __state.scale; rotator.localEulerAngles = __state.angles;
            __instance.fakeMiddle.rectTransform.anchoredPosition = __state.position;
        }
    }

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
        static readonly FieldInfo dragCount = AccessTools.Field(typeof(ItemMouseDragHandler), "currentRemove");
        static readonly FieldInfo groupItems = AccessTools.Field(typeof(ItemMultiSelectHandler), "selectedItems");

        internal static int DragCount(GameItemElement item) {
            if (Dragged() == item) return (int)dragCount.GetValue(ItemMouseDragHandler.current);
            var group = ItemMultiSelectHandler.current;
            var selected = group == null ? null : groupItems.GetValue(group) as List<GameItemElement>;
            return group != null && group.IsGroupDragging && selected != null && selected.Contains(item) ? item.MaxNumRemove() : 0;
        }

        internal static GameItem Dragged(bool whileDragging = true) {
            var handler = ItemMouseDragHandler.current;
            if (handler == null || dragItem == null || dragging == null || (whileDragging && !(bool)dragging.GetValue(handler))) return null;
            return dragItem.GetValue(handler) as GameItem;
        }

        /// <summary>Before an update is applied: if it moves, replaces or removes what this player holds, put it back first.</summary>
        internal static void BeforeApply(WorldReplica world, WorldDelta delta) {
            if (!ReplicaSession.ClientStore || delta == null) return;
            var held = Dragged();
            var group = ItemMultiSelectHandler.current;
            bool groupDragging = group != null && group.IsGroupDragging;
            if (delta.full) {
                NativeActions.CancelLocal();
                if (held != null || groupDragging) RemoteActions.Notice("~SMC269~");
                return;
            }
            bool touched = HeldChanged(world, delta, held);
            if (!touched && groupDragging) {
                var selected = groupItems.GetValue(group) as List<GameItemElement>;
                if (selected != null) {
                    // Look up the held set once; do not scan a large update again for every selected item.
                    var heldItems = new Dictionary<int, GameItem>();
                    foreach (var item in selected) { int id = world.Id(item); if (id > 0) heldItems[id] = item; }
                    if (delta.removed != null) foreach (int id in delta.removed) if (heldItems.ContainsKey(id)) { touched = true; break; }
                    if (!touched && delta.items != null) foreach (var record in delta.items) {
                        GameItem item;
                        if (record != null && heldItems.TryGetValue(record.id, out item) && HeldChanged(world, record, item)) { touched = true; break; }
                    }
                }
            }
            if (!touched) return;
            NativeActions.CancelLocal();
            RemoteActions.Notice("~SMC269~");
        }

        static bool HeldChanged(WorldReplica world, WorldDelta delta, GameItem held) {
            if (held == null) return false;
            int id = world.Id(held); if (id <= 0) return false;
            bool touched = false;
            if (!touched && delta.removed != null) touched = Array.IndexOf(delta.removed, id) >= 0;
            if (!touched && delta.items != null) {
                foreach (var r in delta.items) if (r != null && r.id == id) {
                    // Only a move, a split/merge or a replacement ends the drag. A plain data change (value, freshness,
                    // charge...) does not: the drop sends the newest version anyway. Ending drags on every change made
                    // anything with a running timer impossible to drag on a friend's screen.
                    touched = HeldChanged(world, r, held);
                    break;
                }
            }
            return touched;
        }

        static bool HeldChanged(WorldReplica world, ItemRecord record, GameItem held) {
            var here = world.Address(held.parentInventory); var parent = record.parent;
            return parent == null || here == null || parent.root != here.root || parent.container != here.container || parent.ordinal != here.ordinal ||
                record.node == null || record.node.unitCount != held.unitCount || record.node.identifier != held.identifier;
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
                    string where = fountain ? "fountain click" : "vending click";
                    string detail = "press not delivered; paused=" + Interactable.isPaused + " top=" + blocker;
                    if (covered || Interactable.isPaused) Diagnostics.Note(where, detail + " (meant for what is in front)");
                    else {
                        OpenVending(fountain);
                        // Opened by the mod: handled, so it stays out of the player's problem report. What the game's
                        // click handling saw goes to the log, to find out why its own click never arrives on friends.
                        if (VendingWindow(fountain) != null && VendingWindow(fountain).IsVisible()) Diagnostics.Note(where, detail + "; opened by the mod; " + Probe(fountain));
                        else Diagnostics.Report(where, new InvalidOperationException(detail + "; window did not open; " + Probe(fountain)));
                    }
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
        static PixelWindow VendingWindow(bool fountain) {
            var e = EmporiumEntry.Instance; var slot = e == null ? null : fountain ? e.vendingFountainElement : e.vendingMachineElement;
            return slot != null && slot.childItem != null ? slot.childItem.contentWindow : null;
        }

        /// <summary>The machine's colliders and the scene's click raycasters, for the log.</summary>
        static string Probe(bool fountain) {
            var b = new System.Text.StringBuilder();
            try {
                var machine = fountain ? (StoreVendingFountain.Instance != null ? StoreVendingFountain.Instance.gameObject : null) : (StoreVendingMachine.Instance != null ? StoreVendingMachine.Instance.gameObject : null);
                if (machine == null) return "no machine";
                b.Append("layer=").Append(machine.layer).Append(" z=").Append(machine.transform.position.z.ToString("0.##"));
                int colliders = 0;
                foreach (var c in machine.GetComponentsInChildren<Component>(true)) {
                    if (c == null || c.GetType().Name.IndexOf("Collider", StringComparison.Ordinal) < 0) continue;
                    colliders++; var behaviour = c as Behaviour;
                    b.Append(" ").Append(c.GetType().Name).Append(behaviour == null || behaviour.enabled ? "+" : "-").Append(c.gameObject.activeInHierarchy ? "" : "(inactive)").Append(" L").Append(c.gameObject.layer);
                }
                if (colliders == 0) b.Append(" no collider");
                foreach (var r in UnityEngine.Object.FindObjectsOfType<UnityEngine.EventSystems.BaseRaycaster>()) {
                    var physics = r as UnityEngine.EventSystems.PhysicsRaycaster;
                    if (physics == null) continue;   // the UI raycasters are not the question here
                    b.Append(" rc:").Append(r.GetType().Name).Append(r.isActiveAndEnabled ? "+" : "-").Append(" mask=").Append(physics.eventMask.value).Append(" cam=").Append(r.eventCamera != null ? r.eventCamera.name : "none");
                }
            } catch (Exception e) { b.Append(" probe failed: ").Append(e.Message); }
            return b.ToString();
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
