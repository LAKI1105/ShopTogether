#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static int BandageCount(){int n=0;foreach(var i in EmporiumEntry.Instance.GetAllOwnedItems(false,false))if(i.identifier=="bandage_item")n+=i.unitCount;return n;} static bool forceInjury; static int injuryNotices;
  static bool WoundRoll(ref bool __result){__result=forceInjury;return false;}
  static bool NoLuck(ref bool __result){__result=false;return false;}
  static void InjuryNotice(string __0,string __1){if(!OutingKits.Capturing&&__1=="sec")injuryNotices++;}
  static void OutRequest(string action,params object[] args){NativeClick(0,()=>RemoteActions.Forward(RemoteActions.Adapter(action,args.Length),null,args));}
  static IEnumerator OutHost(Connection root){
   var store=PlayerStore.Instance;var e=EmporiumEntry.Instance;
   store.storeState=PlayerStore.StoreState.CLOSED;store.isAfterhoursIntroduced=true;store.healthData.woundState=0;store.healthData.isWoundedFresh=false;store.scavengingAttempts=2;
   var h=new Harmony("shop-together.outing-probe");
   h.Patch(AccessTools.Method(typeof(ScavHelper),"RollMinorWound"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"WoundRoll"));
   h.Patch(AccessTools.Method(typeof(ScavHelper),"RollMajorWound"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"NoLuck"));
   h.Patch(AccessTools.Method(typeof(HealthData),"RollLuck"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"NoLuck"));
   h.Patch(AccessTools.Method(typeof(HealthData),"RollBadLuck"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"NoLuck"));
   h.Patch(AccessTools.Method(typeof(StoreUIManager),"Notify"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"InjuryNotice"));
   {
    var original=ModHelper.SpawnItem("backpack_small");original.name="HOST_REAL_BAG";e.TryAddToPlayerInv(original);var originalHome=original.parentInventory;
    var kept=ModHelper.SpawnItem("magnifier");kept.mayStackItemFunc=(x,y)=>false;Require(GraphUtils.TryAcceptAll(original,kept)==0,"host real bag contents fixture");
    e.selectedBackpack=original;e.TransferBackpackToInv();var borrowed=e.afterhourPocketSlotInvBackpack.childItem;
    Require(borrowed!=null&&OutingKits.IsVoidBag(borrowed)&&!ReferenceEquals(borrowed,original),"host native departure creates void backpack");
    Require(original.parentInventory==originalHome&&OutingKits.Contents(original).Contains(kept),"host original bag and contents stay in shop");
    Require(OutingKits.Contents(borrowed).Count==0&&!borrowed.MayRemove()&&borrowed.MaxNumRemove()==0,"host void backpack is empty and cannot be moved");
    e.TryAddToPlayerInv(borrowed);Require(borrowed.parentInventory==e.afterhourPocketSlotInvBackpack,"void bag cannot become shop item");
    var loot=ModHelper.SpawnItem("magnifier");loot.mayStackItemFunc=(x,y)=>false;Require(GraphUtils.TryAcceptAll(borrowed,loot)==0,"host void bag receives loot");
    e.TransferAfterhourToInv();Require(e.afterhourPocketSlotInvBackpack.childItem==null&&loot.parentInventory!=null&&!OutingKits.Contents(original).Contains(loot),"host native return unpacks loot and removes void bag");
    Require(original.parentInventory==originalHome&&OutingKits.Contents(original).Contains(kept),"host real bag remains unchanged after return");
   }
   float woundWithout=ScavHelper.GetMinorWoundChance();var flashlight=ModHelper.SpawnItem("flashlight");e.TryAddToPlayerInv(flashlight);OutingSupplies.Reset();Require(Math.Abs(ScavHelper.GetMinorWoundChance()*2-woundWithout)<0.01f,"shop flashlight halves native injury probability");var bandage=ModHelper.SpawnItem("bandage_item");bandage.unitCount=1;bandage.mayStackItemFunc=(x,y)=>false;e.TryAddToPlayerInv(bandage);var backpack=ModHelper.SpawnItem("backpack_small");backpack.name="OUT_BACKPACK";e.TryAddToPlayerInv(backpack);Signal("OUT_READY");var wait=Wait(()=>OutingKits.AnyOut,"client independently entered dumping ground",40);while(wait.MoveNext())yield return wait.Current;
   Kit kit=null;foreach(var k in OutingKits.All)if(k.owner>0){kit=k;break;}int owner=kit.owner;var voidBag=kit.backpack.childItem;Require(OutingKits.IsVoidBag(voidBag)&&OutingKits.Contents(voidBag).Count==0,"borrowed backpack starts empty");root.World.Index();Require(!OutingKits.RequestAllowed(root,owner,new Request{kind=RequestKind.Move,items=new[]{root.World.Id(voidBag)}}),"void backpack cannot be moved through client request");Require(kit.location=="dumping_ground"&&kit.attempts==0,"friend starts own search count");Require(backpack.parentInventory!=kit.backpack&&backpack.parentInventory!=null&&kit.backpack.childItems.Count==1&&OutingKits.IsVoidBag(kit.backpack.childItems[0]),"original backpack stays in shop while friend receives void bag");
   int day=StoreStation.Instance.dayCounter;EndOfDayUIManager.Instance.OnEndDayButtonClick();Require(StoreStation.Instance.dayCounter==day&&DayGuards.Sleep()!=null,"host sleep denied while friend outside");Signal("OUT_SEARCH");
   wait=Wait(()=>kit.attempts==1,"native client scavenging executed by host",40);while(wait.MoveNext())yield return wait.Current;
   Require(store.scavengingAttempts==2&&kit.ground.childItems.Count>0,"own loot ground and independent attempt counter");
   Require(!OutingKits.MayUse(owner+1,100+kit.slot*4)&&!OutingKits.MayUse(owner,40),"private outing inventories reject other players");
   forceInjury=true;store.dumpingGroundMinorWoundImmunitySearchesLeft=0;Signal("OUT_INJURE");wait=Wait(()=>kit.attempts==2,"second scavenging request",40);while(wait.MoveNext())yield return wait.Current;Require(store.healthData.woundState>0&&injuryNotices>0,"native injury sends host notice");forceInjury=false;int beforeBandages=BandageCount();Signal("BANDAGE_READY");wait=Wait(()=>BandageCount()==beforeBandages-1,"client bandage button consumes exactly one shop bandage",30);while(wait.MoveNext())yield return wait.Current;Require(store.healthData.isWoundStable,"native bandage stabilizes injury");
   var pocket=ModHelper.SpawnItem("magnifier");pocket.name="OUT_POCKET";GraphUtils.TryAcceptAll(kit.left,pocket);Signal("OUT_RETURN");
   wait=Wait(()=>!OutingKits.AnyOut,"friend returned from independent outing",40);while(wait.MoveNext())yield return wait.Current;Require(pocket.parentInventory!=kit.left&&pocket.parentInventory!=null,"carried pocket item returned to shop");Require(backpack.parentInventory!=null&&backpack.parentInventory!=kit.backpack,"backpack returned to shop");
   StoreStation.Instance.dayCounter++;store.healthData.woundState=0;store.healthData.isWoundedFresh=false;
   store.commissaryData.redeemedCredit=1000;store.commissaryData.commissaryItemDatas.Add(new CommissaryItemData{identifier="magnifier",StockCount=1,price=17});Signal("COMM_READY");
   wait=Wait(()=>OutingKits.FriendAt(owner,"commissary"),"friend entered commissary",40);while(wait.MoveNext())yield return wait.Current;
   kit=OutingKits.Of(owner);Signal("COMM_BUY");wait=Wait(()=>kit.ground.childItems.Count>0,"native purchase reaches own ground",40);while(wait.MoveNext())yield return wait.Current;Require(store.commissaryData.redeemedCredit==983,"purchase charged exactly once");
   var bought=kit.ground.childItems[0];Signal("OUT_DISCONNECT");wait=Wait(()=>!root.HasPeer(owner)&&!OutingKits.AnyOut,"disconnect returns outing possessions",40);while(wait.MoveNext())yield return wait.Current;Require(bought.parentInventory!=null&&bought.parentInventory!=kit.ground,"disconnected friend's ground loot recovered in shop");Signal("OUT_DONE");
  }
  static IEnumerator OutClient(Connection root){
   var wait=Wait(()=>Received("OUT_READY"),"outing fixtures ready",40);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.LastNote="";OutRequest("Go","dr_jackson",null);wait=Wait(()=>!string.IsNullOrEmpty(RemoteActions.LastNote),"Dr Jackson refusal arrived from host",30);while(wait.MoveNext())yield return wait.Current;Require(OutingKits.Here==null&&RemoteActions.LastNote==Loc.Text("~SMC202~"),"Dr Jackson refused with the expected notice");
   wait=Wait(()=>Find(root,"OUT_BACKPACK")!=null,"backpack replicated",30);while(wait.MoveNext())yield return wait.Current;OutRequest("Go","dumping_ground",Find(root,"OUT_BACKPACK"));wait=Wait(()=>OutingKits.Here=="dumping_ground","own dumping ground view",40);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("OUT_SEARCH"),"host sleep blocked",40);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>OutingKits.Mine!=null&&OutingKits.Mine.backpack.childItem!=null&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"void bag visible after arrival",30);while(wait.MoveNext())yield return wait.Current;var vb=OutingKits.Mine.backpack.childItem;Require(!vb.MayRemove()&&vb.MaxNumRemove()==0,"void backpack native pickup and transfer disabled");BeginDrag(vb);Require(!ItemMouseDragHandler.current.IsDraggingItem,"native drag cannot pick up void backpack");ItemMouseDragHandler.current.UpdateReset();pressed.Clear();NativeClick(0,()=>EndOfDayUIManager.Instance.OnEndDayButtonClick());OutRequest("Scavenge");
   wait=Wait(()=>Received("OUT_INJURE"),"loot and counter checked",40);while(wait.MoveNext())yield return wait.Current;OutRequest("Scavenge");
   wait=Wait(()=>Received("BANDAGE_READY"),"injury ready for bandage",40);while(wait.MoveNext())yield return wait.Current;wait=Wait(()=>CustomUIManager.Instance.GetWindow("shop_together.bandage")!=null,"injury bandage window visible",30);while(wait.MoveNext())yield return wait.Current;var bandageWindow=CustomUIManager.Instance.GetWindow("shop_together.bandage");var buttons=bandageWindow.ContentRoot.GetComponentsInChildren<UnityEngine.UI.Button>();Require(buttons.Length>0,"bandage button available");NativeClick(0,()=>buttons[0].onClick.Invoke());wait=Wait(()=>Received("OUT_RETURN"),"injury notice checked",40);while(wait.MoveNext())yield return wait.Current;OutRequest("Back");
   wait=Wait(()=>OutingKits.Here==null&&Received("COMM_READY"),"return and next night",40);while(wait.MoveNext())yield return wait.Current;OutRequest("Go","commissary",null);
   wait=Wait(()=>OutingKits.Here=="commissary"&&Received("COMM_BUY"),"own commissary view",40);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>PlayerStore.Instance.commissaryData.BuyItem("magnifier"));
   wait=Wait(()=>Received("OUT_DISCONNECT"),"purchase verified",40);while(wait.MoveNext())yield return wait.Current;root.Stop();wait=Wait(()=>Received("OUT_DONE"),"host recovered belongings",40);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
