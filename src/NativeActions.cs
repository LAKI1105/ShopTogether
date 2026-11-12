using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    internal static class ReplicaSession {
        internal static bool ClientStore,SceneReady;
        internal static WorldDelta Seed;
        internal static bool IsReplica {get{return ClientStore&&SceneReady&&!WorldReplica.Applying;}}
    }
    internal static class NativeActions {
        /// <summary>Tools that only open a panel to look at or rename an item; each player uses them on their own screen.</summary>
        internal static bool ViewTool(GameItem item){return item!=null&&(item.IsTag("MAGNIFYING_GLASS")||item.IsTag("LABELER_BLUE"));}
        internal static bool Executing;
        internal static readonly Dictionary<object,int[]> holders=new Dictionary<object,int[]>();
        internal static Connection Session {get{return Connection.Current;}}
        internal static bool Active {get{return Session!=null&&Session.Ready&&(Session.IsHost||Session.IsClient)&&!WorldReplica.Applying&&!Executing;}}
        internal static bool Retains(int id){foreach(var ids in holders.Values)if(Array.IndexOf(ids,id)>=0)return true;return false;}
        internal static bool Hold(object holder,int[] ids){
            if(!Active)return true;foreach(int id in ids)if(id<=0)return false;
            int[] previous;if(holders.TryGetValue(holder,out previous)){holders.Remove(holder);var release=new List<int>();foreach(int id in previous){if(Array.IndexOf(ids,id)>=0)continue;bool retained=false;foreach(var other in holders.Values)if(Array.IndexOf(other,id)>=0){retained=true;break;}if(!retained)release.Add(id);}if(release.Count>0)Session.Release(release.ToArray());}
            int busy=Session.BusyOwner(ids);if(busy>=0)ItemBusyHint.Show(busy);
            holders[holder]=ids;Session.Claim(ids);
            if(Session.IsHost)foreach(int id in ids)if(!Session.LocalLeases.ContainsKey(id)){holders.Remove(holder);return false;}
            return true;
        }
        internal static void Unhold(object holder){int[] ids;if(!holders.TryGetValue(holder,out ids))return;holders.Remove(holder);var released=new List<int>();foreach(int id in ids){bool retained=false;foreach(var other in holders.Values)if(Array.IndexOf(other,id)>=0){retained=true;break;}if(!retained)released.Add(id);}if(Session!=null&&released.Count>0)Session.Release(released.ToArray());}
        // A pick-up claim never outlives the mouse button: if a handler missed its reset, let the items go.
        internal static void Sweep(){if(holders.Count==0||Input.GetMouseButton(0)||Input.GetMouseButton(1))return;foreach(var holder in new List<object>(holders.Keys))if(holder is ItemMouseDragHandler)Unhold(holder);}
        internal static void CancelLocal(){
            if(ItemMouseDragHandler.current!=null)ItemMouseDragHandler.current.UpdateReset();
            if(ItemContextHandler.current!=null)ItemContextHandler.current.UpdateReset();
            if(ItemMultiSelectHandler.current!=null)ItemMultiSelectHandler.current.UpdateReset();
        }
        internal static void CancelItems(int[] ids){if(ids==null)return;foreach(var holder in new List<KeyValuePair<object,int[]>>(holders)){bool conflict=false;foreach(int id in ids)if(Array.IndexOf(holder.Value,id)>=0){conflict=true;break;}if(!conflict)continue;var input=holder.Key as InputActionHandler;var window=holder.Key as PixelWindow;if(input!=null)input.UpdateReset();else if(window!=null)window.Hide();Unhold(holder.Key);}}
        internal static Placement Describe(SlotMarker slot,int count){
            long version;Session.World.Versions.TryGetValue(Session.World.Id(slot.item),out version);
            var shape=slot.itemGridShape??slot.item.modifiedShape;
            // Same amount rule as SlotMarker.TryAcceptOnce: a negative amount means "everything this drop carries".
            int amount=count<0?slot.numTransfer:Math.Min(count,slot.numTransfer);amount=Math.Min(amount,slot.item.MaxNumRemove());
            return new Placement{item=Session.World.Id(slot.item),count=amount,x=shape.minX,y=shape.minY,rotation=shape.orientation,flip=shape.flipped,target=slot.targetItem!=null?Session.World.Id(slot.targetItem):0,inventory=Session.World.Address(slot.inventory),version=version};
        }
        internal static bool ShopAvailable(Connection session,int owner){var entry=EmporiumEntry.Instance;if(entry==null)return false;foreach(var inventory in new GameInventory[]{entry.backInvinvElement,entry.frontInvinvElement,StorePresentation.CustomerInventory()}){if(inventory==null)continue;foreach(var item in GraphUtils.FindChildrenType<GameItem>(inventory,null,null))if(!session.Leases.Available(owner,session.World.Id(item),Time.realtimeSinceStartup))return false;}return true;}
        internal static bool Execute(Connection session,int owner,Request request){
            if(!OutingKits.RequestAllowed(session,owner,request))return false;
            Executing=true;PanelRelay.Owner=owner;
            try {
                double now=Time.realtimeSinceStartup;
                if(request.kind==RequestKind.Move)return Move(session,owner,request,now);
                if(request.kind==RequestKind.Sort)return InventoryActions.Execute(session,owner,request);
                if(request.kind==RequestKind.AutoPlace){
                    if(request.items.Length!=1||request.moves==null||request.moves.Length!=1)return false;
                    var move=request.moves[0];GameItem item;long version;
                    if(!session.World.Items.TryGetValue(request.items[0],out item)||move.item!=request.items[0]||!session.Leases.Owns(owner,move.item,move.lease,now)||!session.World.Versions.TryGetValue(move.item,out version)||version!=move.version||move.count<1||move.count>item.MaxNumRemove())return false;
                    GraphNodeStorage destination;
                    if(request.target!=0){GameItem target;if(!session.World.Items.TryGetValue(request.target,out target)||!session.Leases.Available(owner,request.target,now)||!GeneralHelper.MayPlayerInsertInto(target))return false;destination=target;}
                    else{GameInventory inventory;if(request.inventory==null||!session.World.Inventories.TryGetValue(request.inventory.Key,out inventory)||!GeneralHelper.MayPlayerInsertInto(inventory)||request.inventory.container!=0&&!session.Leases.Available(owner,request.inventory.container,now))return false;destination=inventory;}
                    return GraphUtils.TryAcceptAll(destination,item,move.count)<move.count;
                }
                if(request.kind==RequestKind.ItemAction){
                    if(request.items.Length<1||request.items.Length>2)return false;
                    GameItem item=session.World.Items[request.items[0]];
                    PanelRelay.SourceItem=item;PanelRelay.SourceIsActivation=(ItemOperation)request.operation==ItemOperation.Activate;
                    if(!session.Leases.Owns(owner,session.World.Id(item),request.lease,now))return false;
                    switch((ItemOperation)request.operation){
                        case ItemOperation.Activate:if(!item.MayActivateSlotItem()||!item.CanActivateSlotItem())return false;item.ActivateSlotItem();return true;
                        case ItemOperation.Toggle:if(!item.MayToggleSlotItem()||!item.CanToggleSlotItem())return false;item.ToggleSlotItem();return true;
                        case ItemOperation.Configure:if(!item.MayConfigureSlotItem()||!item.CanConfigureSlotItem())return false;item.ConfigureSlotItem();return true;
                        case ItemOperation.Unload:if(item.IsTag("IS_UNLOAD_DISABLED_TAG")||item.contentWindow==null||item.parentInventory==null)return false;item.TryUnloadAll(true);return true;
                        // Selection/equipping is a local UI mode. Only the eventual Target changes the host world.
                        case ItemOperation.Use:case ItemOperation.Equip:return false;
                        case ItemOperation.Pin:ItemPinHelper.TogglePinned(item);return true;
                        case ItemOperation.Target:GameItem target;if(!session.World.Items.TryGetValue(request.target,out target)||!session.Leases.Available(owner,session.World.Id(target),now)||!item.MayTarget(target)||!item.CanTarget(target))return false;item.Target(target);return true;
                    }
                    return false;
                }
                if(request.kind==RequestKind.ShopAction){if(!CounterTrades.Allowed(owner)||!ShopAvailable(session,owner))return false;var store=PlayerStore.Instance;
                    switch((ShopOperation)request.operation){
                        case ShopOperation.NextCustomer:if(!store.CanNextClient())return false;store.TryCallNextClient();return true;
                        case ShopOperation.Buy:if(!store.CanBuyItemFromTable())return false;store.BuyItemFromTable();return true;
                        case ShopOperation.Sell:if(!store.CanSellItemFromTable(false))return false;store.SellItemFromTable();return true;
                        case ShopOperation.BuyAll:if(!store.CanBuyItemFromTable())return false;store.BuyAllItemFromTable();return true;
                        case ShopOperation.SellAll:if(!store.CanSellItemFromTable(false))return false;store.SellAllItemFromTable();return true;
                        case ShopOperation.AdvanceDialogue:if(!session.World.Presentation.CurrentDialogue(request.worldVersion)||DialogUIManager.Instance==null)return false;DialogUIManager.Instance.OnAdvancePressed();return true;
                        case ShopOperation.ChooseDialogue:if(!session.World.Presentation.CurrentDialogue(request.worldVersion))return false;var ui=DialogUIManager.Instance;if(ui==null||ui.currentDialog==null||request.index<0||ui.currentDialog.choices==null||request.index>=ui.currentDialog.choices.Count||!ui.currentDialog.IsChoiceEnabledAt(request.index))return false;ui.SelectChoice(request.index);return true;
                    }
                }
                return false;
            } finally {Executing=false;PanelRelay.Owner=0;PanelRelay.SourceItem=null;PanelRelay.SourceIsActivation=false;}
        }
        static bool Reject(string reason){
#if LAN_PROBE
            Debug.Log("[SMC REJECT] "+reason);
#endif
            return false;
        }
        static bool Move(Connection session,int owner,Request request,double now){
            if(request.moves==null||request.moves.Length<1||request.moves.Length>128||request.items.Length!=request.moves.Length)return Reject("request shape");
            var slots=new List<SlotMarker>();var items=new List<GameItem>();var homes=new List<GameInventory>();var shapes=new List<GridShape>();
            foreach(var move in request.moves){GameItem item;long version;
                if(!session.World.Items.TryGetValue(move.item,out item)||items.Contains(item)||Array.IndexOf(request.items,move.item)<0||!session.Leases.Owns(owner,move.item,move.lease,now)||!session.World.Versions.TryGetValue(move.item,out version)||version!=move.version||move.count<1||move.count>item.MaxNumRemove()||move.count>item.unitCount||move.rotation<0||move.rotation>3||Math.Abs((long)move.x)>2048||Math.Abs((long)move.y)>2048)return Reject("item state, lease or version");
                if(request.moves.Length>1&&move.count!=item.unitCount)return false;
                items.Add(item);homes.Add(item.parentInventory);shapes.Add(item.modifiedShape);
            }
            for(int i=0;i<items.Count;i++){var move=request.moves[i];var item=items[i];SlotMarker slot;
                if(move.target!=0){GameItem target;if(items.Count>1||!session.World.Items.TryGetValue(move.target,out target)||!session.Leases.Available(owner,session.World.Id(target),now)||!item.MayStack(target))return false;slot=item.GetStackSlot(target);if(slot==null||slot.numTransfer<move.count)return false;}
                else {GameInventory inventory;if(move.inventory==null||!session.World.Inventories.TryGetValue(move.inventory.Key,out inventory)||!GeneralHelper.MayPlayerInsertInto(inventory)||move.inventory.container!=0&&!session.Leases.Available(owner,move.inventory.container,now))return false;
                    GridShape shape=new GridShapeBuilder(item.modifiedShape).SetTransform(move.x,move.y,move.flip,move.rotation).Build();
                    slot=new SlotMarker{item=item,inventory=inventory,itemGridShape=shape,numTransfer=move.count};
                    var grid=inventory as GameGridInventory;
                    if(grid!=null){if(!grid.CheckShapePlacement(item,shape,items)||!inventory.CheckAcceptParentShapeStateModifier(item,slot,shape,item.modifiedState))return Reject("grid shape or inventory state");}
                    else if(!inventory.CheckInventorySlotStillValid(item,shape,item.modifiedState))return Reject("slot invalid");
                    foreach(var previous in slots)if(previous.inventory==inventory&&previous.itemGridShape!=null&&shape.Intersects(previous.itemGridShape,(a,b)=>a!=0&&b!=0))return false;
                }
                slots.Add(slot);
            }
            if(items.Count==1)return slots[0].TryAcceptOnce(request.moves[0].count)==request.moves[0].count;
            try{
                for(int i=0;i<items.Count;i++)if(homes[i]!=null&&!homes[i].Expel(items[i]))throw new InvalidOperationException("~SMC143~");
                for(int i=0;i<items.Count;i++){items[i].modifiedShape=slots[i].itemGridShape;if(!slots[i].inventory.UncheckedAccept(items[i]))throw new InvalidOperationException("~SMC090~");}
                return true;
            }catch{
                for(int i=0;i<items.Count;i++){if(items[i].parentInventory!=null)items[i].parentInventory.Expel(items[i]);items[i].modifiedShape=shapes[i];if(homes[i]!=null)homes[i].UncheckedAccept(items[i]);}return false;
            }
        }
    }
    [HarmonyPatch(typeof(PlayerStore),"InitialSave")]
    internal static class ClientInitialSave {static bool Prefix(PlayerStore __instance){if(!ReplicaSession.ClientStore)return true;__instance.saveSlotId=-777;return false;}}
    [HarmonyPatch(typeof(PlayerStore),"SaveGame")]
    internal static class ClientSave {static bool Prefix(){return !ReplicaSession.ClientStore;}}
    [HarmonyPatch]
    internal static class LocalItemPress {
        // Only picking an item up claims it. Opening, right-clicking or double-clicking an item never locks it for
        // others (those handlers keep their last item until the next press, which left machines locked).
        static IEnumerable<MethodBase> TargetMethods(){yield return typeof(ItemMouseDragHandler).GetMethod("OnEventPress",BindingFlags.Instance|BindingFlags.NonPublic);}
        [HarmonyPriority(Priority.First)] static bool Prefix(InputActionHandler __instance,ISet<KeyCode> __0){
            if(!NativeActions.Active||!(__0.Contains(KeyCode.Mouse0)||__0.Contains(KeyCode.Mouse1)))return true;
            GameItem item=RenderHandler.RaycastElement<GameItem>(Input.mousePosition,null);if(item==null)return true;
            var session=NativeActions.Session;int id=session.World.Id(item);
            // Only the host may assign an ID. Guests must use an ID supplied in the host's snapshot.
            if(id<=0&&session.IsHost){try{id=session.World.EnsureHostItem(item);}catch(Exception error){Diagnostics.Report("index new item",error);}}
            return NativeActions.Hold(__instance,new[]{id});
        }
    }
    [HarmonyPatch]
    internal static class LocalItemReset {
        static IEnumerable<MethodBase> TargetMethods(){yield return typeof(ItemMouseDragHandler).GetMethod("OnEventReset",BindingFlags.Instance|BindingFlags.NonPublic);}
        static void Postfix(InputActionHandler __instance){NativeActions.Unhold(__instance);}
    }
    [HarmonyPatch(typeof(ItemMouseDoubleClickHandler),"OpenContentAction")]
    // Looking inside a container is not exclusive: everyone may open the same container at once. Only taking
    // things (dragging, placing) claims items.
    internal static class OpenLocalContainer {static void Postfix(GameItem __0){}}
    [HarmonyPatch(typeof(PixelWindow),"Hide")]
    internal static class CloseLocalContainer {static void Postfix(PixelWindow __instance){NativeActions.Unhold(__instance);}}
    [HarmonyPatch(typeof(SlotMarker),"TryAcceptOnce")]
    internal static class ClientPlace {
        [HarmonyPriority(Priority.First)] static bool Prefix(SlotMarker __instance,int __0,ref int __result){if(PanelRelay.BuildingCratePreview&&PanelRelay.IsCratePreview(__instance.inventory,__instance.item))return true;if(LocalControl.LocalChoice(__instance.inventory,__instance.item))return true;if(!NativeActions.Active)return true;var s=NativeActions.Session;if(s.IsHost)return s.Leases.Available(0,s.World.Id(__instance.item),Time.realtimeSinceStartup);if(!LocalControl.UserAction||__instance.item==null){__result=0;return false;}var move=NativeActions.Describe(__instance,__0);if(move.count<=0){__result=0;return false;}s.Submit(new Request{kind=RequestKind.Move,items=new[]{move.item},moves=new[]{move}});__result=move.count;return false;}
    }
    [HarmonyPatch(typeof(SlotMarker),"AcceptUnchecked")]
    internal static class ClientPlaceUnchecked {
        [HarmonyPriority(Priority.First)] static bool Prefix(SlotMarker __instance,ref int __result){if(PanelRelay.BuildingCratePreview&&PanelRelay.IsCratePreview(__instance.inventory,__instance.item))return true;if(LocalControl.LocalChoice(__instance.inventory,__instance.item))return true;if(!NativeActions.Active)return true;var s=NativeActions.Session;if(s.IsHost)return s.Leases.Available(0,s.World.Id(__instance.item),Time.realtimeSinceStartup);if(!LocalControl.UserAction||__instance.item==null){__result=0;return false;}var move=NativeActions.Describe(__instance,__instance.numTransfer);if(move.count<=0){__result=0;return false;}s.Submit(new Request{kind=RequestKind.Move,items=new[]{move.item},moves=new[]{move}});__result=move.count;return false;}
    }
    [HarmonyPatch]
    internal static class ClientItemAction {
        static IEnumerable<MethodBase> TargetMethods(){yield return typeof(GameItem).GetMethod("ActivateSlotItem");yield return typeof(GameItem).GetMethod("ToggleSlotItem");yield return typeof(GameItem).GetMethod("ConfigureSlotItem");yield return typeof(GameItem).GetMethod("Target",new[]{typeof(GameItem)});}
        static bool Prefix(GameItem __instance,MethodBase __originalMethod,object[] __args){GameItem viewed=__args.Length>0?__args[0] as GameItem:null;bool view=__originalMethod.Name=="Target"&&viewed!=null&&NativeActions.ViewTool(__instance);
            if(!ReplicaSession.ClientStore){if(NativeActions.Active&&LocalControl.UserAction&&!view){var s=NativeActions.Session;if(!s.Leases.Available(0,s.World.Id(__instance),Time.realtimeSinceStartup))return false;if(__args.Length>0&&__args[0] is GameItem&&!s.Leases.Available(0,s.World.Id((GameItem)__args[0]),Time.realtimeSinceStartup))return false;}return true;}if(WorldReplica.Applying)return false;if(!NativeActions.Active||!LocalControl.UserAction)return false;
            if(__originalMethod.Name=="ActivateSlotItem"&&LocalControl.LocalChoice(__instance.parentInventory,__instance)&&EmporiumEntry.Instance.FindChoiceAction(__instance)!=null)return true;
            // Looking tools (magnifier, rename labeler) only open a panel on this player's own screen.
            if(view){if(__instance.onThisTargetItemFunc!=null)__instance.onThisTargetItemFunc(__instance,viewed);return false;}
            var op=__originalMethod.Name=="ActivateSlotItem"?ItemOperation.Activate:__originalMethod.Name=="ToggleSlotItem"?ItemOperation.Toggle:__originalMethod.Name=="ConfigureSlotItem"?ItemOperation.Configure:ItemOperation.Target;GameItem target=__args.Length>0?__args[0] as GameItem:null;NativeActions.Session.Submit(new Request{kind=RequestKind.ItemAction,operation=(int)op,items=target==null?new[]{NativeActions.Session.World.Id(__instance)}:new[]{NativeActions.Session.World.Id(__instance),NativeActions.Session.World.Id(target)},target=target==null?0:NativeActions.Session.World.Id(target)});return false;}
    }
    [HarmonyPatch]
    internal static class ClientShopAction {
        static readonly Dictionary<string,ShopOperation> operations=new Dictionary<string,ShopOperation>{{"TryCallNextClient",ShopOperation.NextCustomer},{"BuyItemFromTable",ShopOperation.Buy},{"SellItemFromTable",ShopOperation.Sell},{"BuyAllItemFromTable",ShopOperation.BuyAll},{"SellAllItemFromTable",ShopOperation.SellAll}};
        static IEnumerable<MethodBase> TargetMethods(){foreach(string name in operations.Keys)yield return typeof(PlayerStore).GetMethod(name);}
        [HarmonyPriority(Priority.First)] static bool Prefix(MethodBase __originalMethod){if(!ReplicaSession.ClientStore)return !NativeActions.Active||NativeActions.ShopAvailable(NativeActions.Session,0);if(WorldReplica.Applying)return false;if(NativeActions.Active&&LocalControl.UserAction)NativeActions.Session.Send(new Request{kind=RequestKind.ShopAction,items=new int[0],operation=(int)operations[__originalMethod.Name]});return false;}
    }
    [HarmonyPatch(typeof(GameItem),"TryUnloadAll",new[]{typeof(bool)})]
    internal static class SharedUnload {
        [HarmonyPriority(Priority.First)] static bool Prefix(GameItem __instance,ref GameItem __result){
            if(!NativeActions.Active)return !ReplicaSession.IsReplica;
            var session=NativeActions.Session;int id=session.World.Id(__instance);
            if(session.IsHost){if(session.Leases.Available(0,id,Time.realtimeSinceStartup))return true;__result=__instance;return false;}
            if(LocalControl.UserAction)session.Submit(new Request{kind=RequestKind.ItemAction,operation=(int)ItemOperation.Unload,items=new[]{id}});
            __result=__instance;return false;
        }
    }
    [HarmonyPatch(typeof(ItemPinHelper),"TogglePinned")]
    internal static class SharedPin {
        [HarmonyPriority(Priority.First)] static bool Prefix(GameItem __0,ref bool __result){
            if(!NativeActions.Active)return !ReplicaSession.IsReplica;
            var session=NativeActions.Session;int id=session.World.Id(__0);
            if(session.IsHost)return session.Leases.Available(0,id,Time.realtimeSinceStartup);
            if(LocalControl.UserAction)session.Submit(new Request{kind=RequestKind.ItemAction,operation=(int)ItemOperation.Pin,items=new[]{id}});
            __result=ItemPinHelper.IsPinned(__0);return false;
        }
    }
}
