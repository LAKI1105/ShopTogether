#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static GameItem ImmediatePack(string name){
   var pack=ModHelper.SpawnItem("backpack_large");pack.name=name;GeneralHelper.SetItemOwned(pack,true);
   Require(GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement,pack)==0&&pack.parentInventory!=null,"ordinary large backpack is in shop storage");return pack;
  }
  static bool ImmediateFits(GameItem bag,GameItem item){
   if(bag==null||item==null||!item.MayRemove()||item.MaxNumRemove()<1)return false;
   foreach(var grid in GraphUtils.FindAllChildrenType<GameGridInventory>(bag))if(grid.TryFindOneValidInventorySlot(item)!=null)return true;
   return false;
  }
  static GameItem ImmediateFind(GameInventory ground,GameItem bag){
   if(ground!=null)foreach(var item in ground.childItems)if(ImmediateFits(bag,item))return item;
   return null;
  }
  static IEnumerator ImmediateHost1324(Connection root){
   var store=PlayerStore.Instance;var entry=EmporiumEntry.Instance;var map=MapUIManager.Instance;var dump=AfterhourLocation.Get("dumping_ground");
   store.storeState=PlayerStore.StoreState.CLOSED;store.isAfterhoursIntroduced=true;store.healthData.woundState=0;store.healthData.isWoundedFresh=false;
   store.dumpingGroundMinorWoundImmunitySearchesLeft=10;store.dumpingGroundMajorWoundImmunitySearchesLeft=10;store.scavengingAttempts=0;
   var harmony=new Harmony("shop-together.immediate1324");harmony.Patch(AccessTools.Method(typeof(HealthData),"RollBadLuck"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"NoLuck"));
   var pack=ImmediatePack("IMMEDIATE1324_PACK");root.World.Index();entry.selectedBackpack=pack;map.Travel(dump);
   var wait=Wait(()=>map.Current==dump&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host arrived for real scavenging",90);while(wait.MoveNext())yield return wait.Current;
   ClearHint();entry.afterhourWindow.ToFront(true);entry.Validate(true);
   var bag=entry.afterhourPocketSlotInvBackpack.childItem;Require(OutingKits.IsVoidBag(bag),"host has an empty outing backpack");
   foreach(var old in new List<GameItem>(entry.afterhourInventory.childItems)){entry.afterhourInventory.Expel(old);old.Destroy();}
   root.World.Index();GameItem loot=null;int attempts=0;
   while(loot==null&&attempts<5&&ScavHelper.CanScavenge()){
    int before=store.scavengingAttempts;NativeClick(0,()=>ScavHelper.ScavengeDumpingGrounds());Require(store.scavengingAttempts>before,"host runs the native scavenging action");attempts++;
    loot=ImmediateFind(entry.afterhourInventory,bag);
   }
   Require(loot!=null,"host found a real scavenged item that fits the backpack");
   Require(root.World.Id(loot)==0,"fresh host loot has no prior network ID before the click");
   entry.Validate(true);int frame=Time.frameCount;BeginDrag(loot);Require(ItemMouseDragHandler.current.IsDraggingItem&&Time.frameCount==frame,"host immediately starts dragging real fresh loot without waiting a frame");
   int id=root.World.Id(loot);Require(id>0&&root.LocalLeases.ContainsKey(id),"host pickup registers the new item and acquires its lease");
   ReturnDropOn(bag);Require(Time.frameCount==frame&&OutingKits.Contents(bag).Contains(loot),"host immediately drops the scavenged item into the backpack");
   map.ReturnNow();wait=Wait(()=>map.Current==null&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host returned after immediate pickup",90);while(wait.MoveNext())yield return wait.Current;
   root.World.Index();Require(InShop(root,loot),"host immediate pickup returned to the shop");Signal("IMMEDIATE1324_GUEST_GO");
   wait=Wait(()=>OutingKits.Of(1)!=null&&OutingKits.Of(1).location=="dumping_ground","guest arrived at own dumping ground",120);while(wait.MoveNext())yield return wait.Current;
   var kit=OutingKits.Of(1);Require(kit.backpack.childItem!=null,"guest outing backpack exists on host");Signal("IMMEDIATE1324_SEARCH");
   string selected=null;wait=Wait(()=>(selected=ReceivePrefix("IMMEDIATE1324_ITEM="))!=null,"guest immediately dragged real scavenged loot",120);while(wait.MoveNext())yield return wait.Current;
   int guestId=int.Parse(selected);GameItem guestLoot=null;
   wait=Wait(()=>root.World.Items.TryGetValue(guestId,out guestLoot)&&OutingKits.Contents(kit.backpack.childItem).Contains(guestLoot),"guest immediate drop reached the host once",120);while(wait.MoveNext())yield return wait.Current;
   Require(kit.attempts==1,"guest scavenging executed once on the host");Signal("IMMEDIATE1324_DROP_OK");
   wait=Wait(()=>OutingKits.Of(1)==null,"guest returned after immediate pickup",120);while(wait.MoveNext())yield return wait.Current;root.World.Index();Require(InShop(root,guestLoot),"guest immediate pickup returned to the shop");
   Signal("IMMEDIATE1324_DONE");
  }
  static IEnumerator ImmediateGuest1324(Connection root){
   var wait=Wait(()=>Find(root,"IMMEDIATE1324_PACK")!=null&&!HostPhase.Away&&HostPhase.Last!=null&&HostPhase.Last.location==null&&Received("IMMEDIATE1324_GUEST_GO"),"host finished real immediate pickup",180);while(wait.MoveNext())yield return wait.Current;
   OutRequest("Go","dumping_ground",Find(root,"IMMEDIATE1324_PACK"));
   wait=Wait(()=>OutingKits.Here=="dumping_ground"&&OutingKits.Mine!=null&&OutingKits.Mine.backpack.childItem!=null&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy&&Received("IMMEDIATE1324_SEARCH"),"guest reached independent dumping ground",120);while(wait.MoveNext())yield return wait.Current;
   ClearHint();var kit=OutingKits.Mine;kit.window.ToFront(true);kit.window.Validate();
   float requestedAt=Time.realtimeSinceStartup;NativeClick(0,()=>ScavHelper.ScavengeDumpingGrounds());
   GameItem loot=null;wait=Wait(()=>{loot=ImmediateFind(kit.ground,kit.backpack.childItem);return loot!=null;},"real guest scavenged loot appears on own ground",120);while(wait.MoveNext())yield return wait.Current;
   float visibleAt=Time.realtimeSinceStartup;int frame=Time.frameCount;int id=root.World.Id(loot);
   Require(id>0&&root.World.Items.ContainsKey(id),"guest receives a host-assigned ID with the visible item");
   BeginDrag(loot);Require(ItemMouseDragHandler.current.IsDraggingItem&&Time.frameCount==frame,"guest immediately starts dragging on the first visible frame");
   Require(root.ProbePending(id)||root.LocalLeases.ContainsKey(id),"guest has requested the host's control lease");
   Signal("IMMEDIATE1324_ITEM="+id);ReturnDropOn(kit.backpack.childItem);
   Require(Time.frameCount==frame,"guest releases the new item without waiting for a network round trip");
   Debug.Log("[SMC PERF] guest search-to-visible ms="+((visibleAt-requestedAt)*1000f).ToString("F1"));
   wait=Wait(()=>Received("IMMEDIATE1324_DROP_OK"),"host accepted the immediate guest drop",120);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>OutingKits.Contents(kit.backpack.childItem).Contains(loot),"guest sees the host-confirmed backpack item",90);while(wait.MoveNext())yield return wait.Current;
   OutRequest("Back");wait=Wait(()=>OutingKits.Here==null&&Received("IMMEDIATE1324_DONE"),"guest returned after immediate pickup",120);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
