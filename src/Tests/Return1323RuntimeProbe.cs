#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static GameItem ReturnFixture(GameInventory inventory,string name,string identifier="magnifier"){
   var item=ModHelper.SpawnItem(identifier);item.name=name;item.mayStackItemFunc=(a,b)=>false;GeneralHelper.SetItemOwned(item,true);
   Require(GraphUtils.TryAcceptAll(inventory,item)==0&&item.parentInventory==inventory,name+" placed in the requested inventory");return item;
  }
  static GameItem ReturnBackpack(string name){
   var item=ModHelper.SpawnItem("backpack_small");item.name=name;GeneralHelper.SetItemOwned(item,true);
   var storage=EmporiumEntry.Instance.invElement;Require(GraphUtils.TryAcceptAll(storage,item)==0&&item.parentInventory==storage,name+" available in shop storage");return item;
  }
  static bool InShop(Connection root,GameItem item){
   if(item==null||item.parentInventory==null)return false;
   var address=root.World.Address(item.parentInventory);return address!=null&&address.root<OutingKits.VanillaRoot;
  }
  static void ReturnDropOn(GameItem container){
   var before=pointer;pointer=Point(container);ItemMouseDragHandler.current.UpdateMouseMove(pointer,pointer-before);ItemMouseDragHandler.current.UpdateUpdate();
   pressed.Remove(KeyCode.Mouse0);ItemMouseDragHandler.current.UpdateRelease(new HashSet<KeyCode>{KeyCode.Mouse0});
  }
  static IEnumerator ReturnHost1323(Connection root){
   var store=PlayerStore.Instance;var e=EmporiumEntry.Instance;var map=MapUIManager.Instance;var dump=AfterhourLocation.Get("dumping_ground");
   store.storeState=PlayerStore.StoreState.CLOSED;store.isAfterhoursIntroduced=true;store.healthData.woundState=0;store.healthData.isWoundedFresh=false;
   store.dumpingGroundMinorWoundImmunitySearchesLeft=10;store.scavengingAttempts=0;e.invElement.SetShape(36,36);e.Validate(true);
   var pack=ReturnBackpack("RETURN1323_HOST_PACK");e.selectedBackpack=pack;map.Travel(dump);
   var wait=Wait(()=>map.Current==dump&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host arrived at dumping ground with a backpack",90);while(wait.MoveNext())yield return wait.Current;
   ClearHint();var bag=e.afterhourPocketSlotInvBackpack.childItem;Require(OutingKits.IsVoidBag(bag),"host has the borrowed outing backpack");
   int searches=store.scavengingAttempts;ScavHelper.ScavengeDumpingGrounds();Require(store.scavengingAttempts>searches,"host uses the native scavenging action");
   var carried=ReturnFixture(e.afterhourInventory,"RETURN1323_HOST_CARRIED");var ground=ReturnFixture(e.afterhourInventory,"RETURN1323_HOST_GROUND");
   wait=Wait(()=>root.World.Id(carried)>0&&root.World.Id(bag)>0,"fresh outing loot is indexed before host pickup",30);while(wait.MoveNext())yield return wait.Current;
   e.afterhourWindow.ToFront(true);e.Validate(true);yield return null;BeginDrag(carried);Require(ItemMouseDragHandler.current.IsDraggingItem,"host begins a native drag from the ground: enabled="+ItemMouseDragHandler.current.enabled+" init="+ModReflect.Get<bool>(ItemMouseDragHandler.current,"initialized")+" key="+ModReflect.Get<KeyCode>(ItemMouseDragHandler.current,"key")+" may="+carried.MayRemove()+" max="+carried.MaxNumRemove()+" selected="+ItemMouseDragHandler.current.GetCurrentItem()+" hover="+StoreUIManager.Instance.hoveringStoreUI+" uiDrag="+UIDragHandler.isDraggingAnyUI+" locked="+PlayerStore.Instance.lockAllInventoryFlag+" pin="+ItemPinHelper.BlocksClicks);ReturnDropOn(bag);
   wait=Wait(()=>OutingKits.Contents(bag).Contains(carried),"host drops a ground item into the outing backpack",60);while(wait.MoveNext())yield return wait.Current;
   map.ReturnNow();wait=Wait(()=>map.Current==null&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host returned from dumping ground",90);while(wait.MoveNext())yield return wait.Current;
   root.World.Index();Require(InShop(root,carried),"host backpack item returned to the shop");Require(ground.parentInventory==null&&Find(root,"RETURN1323_HOST_GROUND")==null,"host ground item was not brought to the shop");

   // A full shop must not silently lose carried contents or allow the day to end.
   e.selectedBackpack=pack;map.Travel(dump);wait=Wait(()=>map.Current==dump&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host arrived for the full-shop return",90);while(wait.MoveNext())yield return wait.Current;ClearHint();
   bag=e.afterhourPocketSlotInvBackpack.childItem;Require(OutingKits.IsVoidBag(bag),"host has an outing backpack for the full-shop test");
   var overflow=ReturnFixture(e.afterhourInventory,"RETURN1323_HOST_OVERFLOW");Require(GraphUtils.TryAcceptAll(bag,overflow)==0,"host carries the full-shop fixture");
   var blocked=new Dictionary<GameInventory,Func<GameItem,GameInventory,bool>>();foreach(var r in WorldReplica.Roots())if(r.Key<40&&!blocked.ContainsKey(r.Value)){blocked[r.Value]=r.Value.mayInventoryAddItemFunc;r.Value.mayInventoryAddItemFunc=(a,b)=>false;}
   try{
    map.ReturnNow();wait=Wait(()=>map.Current==null&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host returned to a full shop",90);while(wait.MoveNext())yield return wait.Current;
    Require(overflow.parentInventory!=null&&!InShop(root,overflow)&&HostVoidBackpackBack.HasLeftovers,"host carried item remains accessible when the shop is full");
    int day=StoreStation.Instance.dayCounter;EndOfDayUIManager.Instance.OnEndDayButtonClick();Require(StoreStation.Instance.dayCounter==day&&HostVoidBackpackBack.HasLeftovers,"host cannot sleep with unplaced outing items");
   }finally{foreach(var r in blocked)r.Key.mayInventoryAddItemFunc=r.Value;}
   Require(GraphUtils.TryAcceptAll(e.invElement,overflow)==0,"host moves the preserved item into freed shop storage");e.TransferAfterhourToInv(false);Require(!HostVoidBackpackBack.HasLeftovers&&InShop(root,overflow),"host can clear the recovered outing slot");

   var guestPack=ReturnBackpack("RETURN1323_GUEST_PACK");root.World.Index();e.Validate(true);Signal("RETURN1323_GUEST_GO");
   wait=Wait(()=>OutingKits.Of(1)!=null&&OutingKits.Of(1).location=="dumping_ground","guest arrived at own dumping ground",120);while(wait.MoveNext())yield return wait.Current;
   var kit=OutingKits.Of(1);Signal("RETURN1323_SEARCH");wait=Wait(()=>kit.attempts>0&&kit.ground.childItems.Count>0,"guest native scavenging executes on host",90);while(wait.MoveNext())yield return wait.Current;
   var guestCarried=ReturnFixture(kit.ground,"RETURN1323_GUEST_CARRIED");var guestGround=ReturnFixture(kit.ground,"RETURN1323_GUEST_GROUND");
   root.World.Index();e.Validate(true);Signal("RETURN1323_GUEST_DRAG");
   wait=Wait(()=>OutingKits.Contents(kit.backpack.childItem).Contains(guestCarried),"guest native ground-to-backpack drag committed by host",90);while(wait.MoveNext())yield return wait.Current;Signal("RETURN1323_GUEST_BACK");
   wait=Wait(()=>OutingKits.Of(1)==null,"guest returned from own dumping ground",90);while(wait.MoveNext())yield return wait.Current;root.World.Index();
   Require(InShop(root,guestCarried),"guest backpack item returned to the shop");Require(guestGround.parentInventory==null&&Find(root,"RETURN1323_GUEST_GROUND")==null,"guest ground item was not brought to the shop");Signal("RETURN1323_GUEST_DONE");

   StoreStation.Instance.dayCounter++;var doctor=AfterhourLocation.Get("dr_jackson");map.Travel(doctor);
   wait=Wait(()=>map.Current==doctor&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host arrived at Dr Jackson",90);while(wait.MoveNext())yield return wait.Current;
   Signal("RETURN1323_DOCTOR="+StoreStation.Instance.dayCounter);
   wait=Wait(()=>OutingKits.Of(1)!=null&&OutingKits.Of(1).location=="dumping_ground","guest independently scavenges while host visits doctor",120);while(wait.MoveNext())yield return wait.Current;
   kit=OutingKits.Of(1);var doctorLoot=ReturnFixture(kit.ground,"RETURN1323_DOCTOR_LOOT");Require(GraphUtils.TryAcceptAll(kit.backpack.childItem,doctorLoot)==0,"guest carries loot while host is at doctor");
   var doctorOverflow=ReturnFixture(kit.left,"RETURN1323_DOCTOR_OVERFLOW");var accept=e.invElement.mayInventoryAddItemFunc;
   try{e.invElement.mayInventoryAddItemFunc=(a,b)=>false;Require(!OutingKits.Home(doctorOverflow)&&doctorOverflow.parentInventory==kit.left,"full shop storage leaves guest loot in the outing slot instead of another display");}finally{e.invElement.mayInventoryAddItemFunc=accept;}
   Signal("RETURN1323_DOCTOR_BACK");
   wait=Wait(()=>OutingKits.Of(1)==null,"guest returned while host remains at doctor",90);while(wait.MoveNext())yield return wait.Current;
   Require(map.Current==doctor&&doctorLoot.parentInventory==e.invElement&&doctorOverflow.parentInventory==e.invElement,"guest loot reaches shop storage, not doctor's counters");root.World.Index();int oldX=doctorLoot.modifiedShape.minX,oldY=doctorLoot.modifiedShape.minY;Signal("RETURN1323_DOCTOR_DRAG");
   wait=Wait(()=>Received("RETURN1323_DOCTOR_DROPPED"),"guest released the item in shop storage",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>doctorLoot.parentInventory==e.invElement&&(doctorLoot.modifiedShape.minX!=oldX||doctorLoot.modifiedShape.minY!=oldY),"guest storage drag is committed by host while doctor remains open",90);while(wait.MoveNext())yield return wait.Current;
   map.ReturnNow();wait=Wait(()=>map.Current==null&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host returned from doctor",90);while(wait.MoveNext())yield return wait.Current;

   StoreStation.Instance.dayCounter++;Signal("RETURN1323_DISCONNECT="+StoreStation.Instance.dayCounter);
   wait=Wait(()=>OutingKits.Of(1)!=null&&OutingKits.Of(1).location=="dumping_ground","guest left before disconnect",120);while(wait.MoveNext())yield return wait.Current;
   kit=OutingKits.Of(1);var dcGround=ReturnFixture(kit.ground,"RETURN1323_DC_GROUND");var dcCarried=ReturnFixture(kit.ground,"RETURN1323_DC_CARRIED");
   var dcWaste=ReturnFixture(kit.ground,"RETURN1323_DC_WASTE","nuclear_waste");var dcFood=ReturnFixture(kit.ground,"RETURN1323_DC_FOOD","nutrifruit");
   SetFoodNight(1000);int wasteBefore=PlayerStore.GetStoreClientData().nuclearWasteDumped,foodBefore=PlayerStore.GetStoreClientData().serpicoFoodKcal;
   Require(GraphUtils.TryAcceptAll(kit.backpack.childItem,dcCarried)==0,"disconnect fixture is in the guest backpack");Signal("RETURN1323_DISCONNECT_NOW");
   wait=Wait(()=>!root.HasPeer(1)&&!OutingKits.AnyOut,"guest disconnected while outside",90);while(wait.MoveNext())yield return wait.Current;
   Require(InShop(root,dcGround)&&InShop(root,dcCarried)&&InShop(root,dcWaste)&&InShop(root,dcFood),"disconnection recovers ground, backpack, waste and food items to the shop");
   Require(PlayerStore.GetStoreClientData().nuclearWasteDumped==wasteBefore&&PlayerStore.GetStoreClientData().serpicoFoodKcal==foodBefore,"disconnect recovery does not count waste or food as deliberately delivered");Signal("RETURN1323_DONE");
  }
  static IEnumerator ReturnGuest1323(Connection root){
   var wait=Wait(()=>Received("RETURN1323_GUEST_GO"),"host completed host-return checks",240);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Find(root,"RETURN1323_GUEST_PACK")!=null&&!HostPhase.Away&&HostPhase.Last!=null&&HostPhase.Last.location==null,"guest backpack and shop phase replicated",90);while(wait.MoveNext())yield return wait.Current;
   OutRequest("Go","dumping_ground",Find(root,"RETURN1323_GUEST_PACK"));wait=Wait(()=>OutingKits.Here=="dumping_ground"&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy&&Received("RETURN1323_SEARCH"),"guest reached own dumping ground",120);while(wait.MoveNext())yield return wait.Current;
   ClearHint();NativeClick(0,()=>ScavHelper.ScavengeDumpingGrounds());
   wait=Wait(()=>OutingKits.Mine!=null&&OutingKits.Mine.backpack.childItem!=null&&Find(root,"RETURN1323_GUEST_CARRIED")!=null&&Find(root,"RETURN1323_GUEST_GROUND")!=null&&Received("RETURN1323_GUEST_DRAG"),"guest received ground loot and outing backpack",120);while(wait.MoveNext())yield return wait.Current;
   var item=Find(root,"RETURN1323_GUEST_CARRIED");OutingKits.Mine.window.ToFront(true);yield return null;BeginDrag(item);Require(ItemMouseDragHandler.current.IsDraggingItem,"guest begins a native drag from own ground");
   wait=Wait(()=>root.LocalLeases.ContainsKey(root.World.Id(item)),"guest obtains control of ground loot",60);while(wait.MoveNext())yield return wait.Current;ReturnDropOn(OutingKits.Mine.backpack.childItem);
   wait=Wait(()=>Received("RETURN1323_GUEST_BACK"),"host confirmed guest dragged loot into backpack",90);while(wait.MoveNext())yield return wait.Current;
   OutRequest("Back");wait=Wait(()=>OutingKits.Here==null&&Received("RETURN1323_GUEST_DONE"),"guest returned after scavenging",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Find(root,"RETURN1323_GUEST_CARRIED")!=null&&Find(root,"RETURN1323_GUEST_GROUND")==null,"guest sees carried loot but not discarded ground loot in the shop",90);while(wait.MoveNext())yield return wait.Current;

   string text=null;wait=Wait(()=>(text=ReceivePrefix("RETURN1323_DOCTOR="))!=null,"host reached doctor on the next night",120);while(wait.MoveNext())yield return wait.Current;int day=int.Parse(text);
   wait=Wait(()=>HostPhase.Last!=null&&HostPhase.Last.location=="dr_jackson"&&!HostPhase.Away&&StoreStation.Instance.dayCounter==day,"doctor phase and new night replicated",90);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.LastNote="";OutRequest("Go","dumping_ground",Find(root,"RETURN1323_GUEST_PACK"));
   wait=Wait(()=>OutingKits.Here=="dumping_ground"||!string.IsNullOrEmpty(RemoteActions.LastNote),"guest outing request was processed while host is at doctor",120);while(wait.MoveNext())yield return wait.Current;
   Require(OutingKits.Here=="dumping_ground","guest may leave while host is at doctor; notice="+RemoteActions.LastNote);
   wait=Wait(()=>Received("RETURN1323_DOCTOR_BACK"),"guest reached dumping ground while host is at doctor",120);while(wait.MoveNext())yield return wait.Current;
   OutRequest("Back");wait=Wait(()=>OutingKits.Here==null&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy&&Received("RETURN1323_DOCTOR_DRAG"),"guest returned to shop while host remains at doctor",120);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Find(root,"RETURN1323_DOCTOR_LOOT")!=null&&Find(root,"RETURN1323_DOCTOR_LOOT").parentInventory==EmporiumEntry.Instance.invElement,"doctor-trip loot appears in guest's shop storage",90);while(wait.MoveNext())yield return wait.Current;
   ShowStorage();yield return null;var loot=Find(root,"RETURN1323_DOCTOR_LOOT");BeginDrag(loot);Require(ItemMouseDragHandler.current.IsDraggingItem,"guest begins a native drag from shop storage");
   wait=Wait(()=>root.LocalLeases.ContainsKey(root.World.Id(loot)),"guest obtains control of returned storage item",60);while(wait.MoveNext())yield return wait.Current;Drop(EmporiumEntry.Instance.invElement,loot,true);Signal("RETURN1323_DOCTOR_DROPPED");

   text=null;wait=Wait(()=>(text=ReceivePrefix("RETURN1323_DISCONNECT="))!=null,"next night started for disconnect test",120);while(wait.MoveNext())yield return wait.Current;day=int.Parse(text);
   wait=Wait(()=>HostPhase.Last!=null&&HostPhase.Last.location==null&&!HostPhase.Away&&StoreStation.Instance.dayCounter==day,"host returned and disconnect night replicated",90);while(wait.MoveNext())yield return wait.Current;
   OutRequest("Go","dumping_ground",Find(root,"RETURN1323_GUEST_PACK"));wait=Wait(()=>OutingKits.Here=="dumping_ground"&&Received("RETURN1323_DISCONNECT_NOW"),"guest is outside with ground and backpack loot",120);while(wait.MoveNext())yield return wait.Current;
   root.Stop();wait=Wait(()=>Received("RETURN1323_DONE"),"host recovered both possessions after disconnect",90);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator SessionHost1323(Connection root){
   var store=PlayerStore.Instance;store.storeState=PlayerStore.StoreState.CLOSED;store.isAfterhoursIntroduced=true;store.healthData.woundState=0;store.healthData.isWoundedFresh=false;
   var pack=ReturnBackpack("RETURN1323_SESSION_PACK");root.World.Index();Signal("RETURN1323_SESSION_GO");
   var wait=Wait(()=>OutingKits.Of(1)!=null&&OutingKits.Of(1).location=="dumping_ground","guest is outside when session ends",120);while(wait.MoveNext())yield return wait.Current;
   var kit=OutingKits.Of(1);var ground=ReturnFixture(kit.ground,"RETURN1323_SESSION_GROUND");var carried=ReturnFixture(kit.ground,"RETURN1323_SESSION_CARRIED");var waste=ReturnFixture(kit.ground,"RETURN1323_SESSION_WASTE","nuclear_waste");
   Require(GraphUtils.TryAcceptAll(kit.backpack.childItem,carried)==0,"session-end fixture is in the guest backpack");Signal("RETURN1323_SESSION_STOP");
   wait=Wait(()=>Received("RETURN1323_SESSION_ACK"),"guest is ready for host shutdown",60);while(wait.MoveNext())yield return wait.Current;
   root.Stop();root.World.Index();Require(InShop(root,ground)&&InShop(root,carried)&&InShop(root,waste),"ending the host session recovers ground, backpack and waste items");Signal("RETURN1323_SESSION_DONE");
  }
  static IEnumerator SessionGuest1323(Connection root){
   var wait=Wait(()=>Find(root,"RETURN1323_SESSION_PACK")!=null&&Received("RETURN1323_SESSION_GO"),"session-end outing fixture replicated",90);while(wait.MoveNext())yield return wait.Current;
   OutRequest("Go","dumping_ground",Find(root,"RETURN1323_SESSION_PACK"));wait=Wait(()=>OutingKits.Here=="dumping_ground"&&Received("RETURN1323_SESSION_STOP"),"guest is outside before host closes the session",120);while(wait.MoveNext())yield return wait.Current;
   Signal("RETURN1323_SESSION_ACK");wait=Wait(()=>Received("RETURN1323_SESSION_DONE"),"host recovered items before the session ended",90);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
