using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    // UI checks are hints on a replica. Only these input handlers may bypass them;
    // the game's predicates themselves remain unchanged for host validation.
    internal static class AuthorityIntent {
        internal static bool Friend { get { return ReplicaSession.IsReplica && !NativeActions.Executing; } }
        internal static bool Ask(bool available) { return Friend || available; }
        internal static bool Disabled(SelectableElement element) { return !(Friend && (element is MachineryButton || element is MachineryRadioButton)) && element.isDisabled; }
        internal static bool Refuse(string reason) { RemoteActions.PendingNote = reason; return false; }
        internal static bool Free(Connection s, int owner, int id) {
            return s.Leases.Available(owner, id, Time.realtimeSinceStartup) || Refuse("~SMC096~");
        }
        internal static bool Live(GameItem item) {
            return item != null && item.parentInventory != null && (!(item is GameItemElement) || !((GameItemElement)item).IsDestroyed());
        }
        internal static bool FreeTree(Connection s,int owner,GameItem item) {
            if(item==null)return Refuse("~SMC095~");
            if(s==null||!s.IsHost||!s.Ready)return true;
            if(!Live(item))return Refuse("~SMC095~");
            if(!Free(s,owner,s.World.Id(item)))return false;
            foreach(var child in item.FindAllChildItems())if(!Free(s,owner,s.World.Id(child)))return false;
            return true;
        }
        internal static string TreeStamp(Connection session, GameItem item) {
            if (session == null || item == null) return null;
            var b = new System.Text.StringBuilder();
            b.Append(Fingerprint.Item(session.World, session.World.Id(item), item));
            foreach (var child in item.FindAllChildItems())
                b.Append(';').Append(Fingerprint.Item(session.World, session.World.Id(child), child));
            return b.ToString();
        }
        internal static bool Target(GameItem item, GameItem target,int page=0) {
            if (item == null || target == null) return Refuse("~SMC095~");
            var behaviour = ItemBehaviourManager.current;
            bool native = item.MayTarget(target) && item.CanTarget(target);
            bool extra = behaviour != null && behaviour.MayTarget(item, target) && behaviour.CanTarget(item, target);
            if (!native && !extra) return Refuse("~SMC316~");
            if(native && CargoIntent.IsController(item))return CargoIntent.Target(item,target,page);
            if (extra) behaviour.Target(item, target);
            if (item.MayTarget(target) && item.CanTarget(target)) item.Target(target);
            return true;
        }
    }

    [HarmonyPatch]
    internal static class IntentInputChecks {
        static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(ItemContextHandler), "TriggerButton");
            yield return AccessTools.Method(typeof(ItemSelectHandler), "OnEventPress");
            yield return AccessTools.Method(typeof(CommissaryElement), "OnBuyClick");
            yield return AccessTools.Method(typeof(ItemMultiSelectHandler), "EndGroupDrag");
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) {
            foreach (var instruction in code) {
                yield return instruction;
                var field = instruction.operand as FieldInfo;
                var method = instruction.operand as MethodInfo;
                bool context = instruction.opcode == OpCodes.Ldfld && field != null && field.DeclaringType == typeof(ItemContextHandler) &&
                    new HashSet<string> { "canActivate", "canConfigure", "canToggle", "canUnload", "canPin", "canSort", "canUndoSort" }.Contains(field.Name);
                bool use = (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && method != null && method.ReturnType == typeof(bool) &&
                    (method.DeclaringType == typeof(CommissaryUIManager) && method.Name == "CanBuy" || method.DeclaringType == typeof(ItemMultiSelectHandler) && method.Name == "IsFormationValid" || method.DeclaringType == typeof(GameItem) &&
                    (method.Name == "MayTarget" || method.Name == "CanTarget" || method.Name == "MayActivateSlotItem" || method.Name == "CanActivateSlotItem"));
                if (context || use) yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AuthorityIntent), "Ask"));
            }
        }
    }

    [HarmonyPatch]
    internal static class MachineIntentChecks {
        static IEnumerable<MethodBase> TargetMethods() {
            foreach (string name in new[] { "OnEventPress", "OnEventUpdate", "UpdateHold" }) yield return AccessTools.Method(typeof(SelectableElementHandler), name);
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) {
            var getter = AccessTools.PropertyGetter(typeof(SelectableElement), "isDisabled");
            foreach (var instruction in code) {
                if (Equals(instruction.operand, getter)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(AuthorityIntent), "Disabled"); }
                yield return instruction;
            }
        }
    }

    [HarmonyPatch(typeof(ItemBehaviourManager), "Target")]
    internal static class FriendTargetBehaviour {
        static bool Prefix() { return !ReplicaSession.IsReplica; }
    }

    [HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
    internal static class DragIntent {
        [HarmonyPriority(Priority.First)]
        static void Prefix(ref bool cancelled, GameItemElement ___currentItem, int ___currentRemove,
            ref GameItem ___lastItem, ref GameInventory ___lastInventory, ref SlotMarker ___lastSlot) {
            if (!ReplicaSession.ClientStore || WorldReplica.Applying || ___currentItem == null) return;
            var item = ___currentItem; var target = ___lastItem; var inventory = ___lastInventory;
            // Clear the native destinations even on cancellation: EndDrag's cancelled flag does
            // not guard all of its placement branches. Let it perform presentation cleanup only.
            ___lastItem = null; ___lastInventory = null; ___lastSlot = null;
            bool wasCancelled = cancelled; cancelled = true;
            if (wasCancelled || !LocalControl.UserAction) return;
            var s = Connection.Current;
            if (s == null || !s.Ready) { RemoteActions.Notice("~SMC242~"); return; }
            int id = s.World.Id(item); long version; s.World.Versions.TryGetValue(id, out version);
            if (id <= 0) { RemoteActions.Notice("~SMC095~"); return; }
            if (target != null) {
                if (NativeActions.ViewTool(item)) { if (item.MayTarget(target) && item.CanTarget(target)) item.Target(target); else RemoteActions.Notice("~SMC316~"); return; }
                s.Submit(new Request { kind = RequestKind.AutoPlace, operation = 1, items = new[] { id }, target = s.World.Id(target),index=CargoIntent.Page(item,target),
                    moves = new[] { new Placement { item = id, count = ___currentRemove, version = version } } });
                return;
            }
            if (inventory == null) {
                inventory = MachineryTabs.FindDropInventoryAt(Input.mousePosition, item.fakeBackground, item);
                if (inventory != null) GraphUtils.TryAcceptAll(inventory, item, ___currentRemove);
                return;
            }
            var slot = inventory.TryInventorySlot(item, ___currentRemove, item.GetFakeRotatorCenteredPosition(), item.tempShape, item.modifiedState);
            var shape = slot != null ? slot.itemGridShape : null;
            if (inventory is GameGridInventory) shape = (GridShape)AccessTools.Method(typeof(GameGridInventory), "GetGridPosition").Invoke(inventory, new object[] { item.GetFakeRotatorCenteredPosition(), item.tempShape });
            if (shape == null) { RemoteActions.Notice("~SMC111~"); return; }
            s.Submit(new Request { kind = RequestKind.Move, items = new[] { id }, moves = new[] {
                new Placement { item = id, count = ___currentRemove, version = version, inventory = s.World.Address(inventory),
                    x = shape.minX, y = shape.minY, rotation = shape.orientation, flip = shape.flipped } } });
        }
    }

    [HarmonyPatch(typeof(EndOfDayUIManager), "BuildServiceRow")]
    internal static class ServiceIntentRows {
        static void Postfix(EndOfDayUIManager __instance) {
            if (!AuthorityIntent.Friend) return;
            var rows = ModReflect.Get<List<(StoreService service, TabletCheckbox checkbox)>>(__instance, "rows");
            if (rows == null) return;
            foreach (var row in rows) if (row.checkbox != null) row.checkbox.SetInteractable(true);
        }
    }

    [HarmonyPatch(typeof(TabletCheckbox), "Toggle")]
    internal static class ServiceIntentToggle {
        static bool Prefix(TabletCheckbox __instance) {
            if (!AuthorityIntent.Friend || EndOfDayUIManager.Instance == null) return true;
            var rows = ModReflect.Get<List<(StoreService service, TabletCheckbox checkbox)>>(EndOfDayUIManager.Instance, "rows");
            if (rows == null) return true;
            foreach (var row in rows) if (row.checkbox == __instance) {
                RemoteActions.Forward(RemoteActions.Adapter("ToggleService", 2), null, new object[] { row.service.id, !row.service.isUsing });
                return false;
            }
            return true;
        }
    }
}
