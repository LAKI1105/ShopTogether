using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ShopTogether {
    internal static class DialogueIntent {
        static readonly FieldInfo scheduled = AccessTools.Field(typeof(DialogUIManager), "scheduledEndActionLines");
        static bool Pending(Dialogue line) { return line != null && line.endAction != null && !line.isEndActionExecuted; }
        internal static bool Committing {
            get {
                var ui = DialogUIManager.Instance; if (ui == null) return false;
                if (Pending(ui.currentDialog)) return true;
                var lines = scheduled == null ? null : scheduled.GetValue(ui) as List<Dialogue>;
                if (lines != null) foreach (var line in lines) if (Pending(line)) return true;
                return false;
            }
        }
        static bool Counter(InventoryAddress a) { return a != null && (a.root == 2 || a.root == 3 || a.root == OutingKits.CounterRoot); }
        internal static bool CanTouch(Connection s, GameItem item) { return item == null || !Committing || !Counter(s.World.Address(item.parentInventory)); }
        internal static bool CanTouchInventory(Connection s, GameInventory inventory) { return !Committing || !Counter(s.World.Address(inventory)); }
        // Adapters that select their own items have no item IDs in the request.
        // Recheck each selected item before any payment, destruction or transfer.
        internal static bool MayChange(Connection s, GameItem item) {
            return s == null || !s.IsHost || !s.Ready || CanTouch(s, item) || AuthorityIntent.Refuse("~SMC353~");
        }
        internal static bool RequestAllowed(Connection s, Request request) {
            if (!Committing || request.kind == RequestKind.ShopAction) return true;
            if (request.items != null) foreach (int id in request.items) { GameItem item; if (s.World.Items.TryGetValue(id, out item) && !CanTouch(s, item)) return false; }
            GameItem target; if (s.World.Items.TryGetValue(request.target, out target) && !CanTouch(s, target)) return false;
            if (Counter(request.inventory)) return false;
            if (request.moves != null) foreach (var move in request.moves) {
                if (move == null) continue;
                if (Counter(move.inventory) || s.World.Items.TryGetValue(move.target, out target) && !CanTouch(s, target)) return false;
            }
            return true;
        }
        internal static bool Available(Connection s, int owner) {

            var e = EmporiumEntry.Instance; if (e == null) return AuthorityIntent.Refuse("~SMC081~");
            foreach (var inventory in new[] { e.frontInvinvElement, e.backInvinvElement })
                foreach (var item in inventory.childItems) if (!AuthorityIntent.FreeTree(s, owner, item)) return false;
            return true;
        }
    }
}
