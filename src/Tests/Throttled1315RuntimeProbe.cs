#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static IEnumerator ThrottledOutingHost(Connection root){
   var e=EmporiumEntry.Instance;var store=PlayerStore.Instance;
   store.storeState=PlayerStore.StoreState.CLOSED;store.isAfterhoursIntroduced=true;store.scavengingAttempts=0;store.healthData.woundState=0;store.healthData.isWoundedFresh=false;
   var patch=new Harmony("shop-together.throttled-outing-probe");
   patch.Patch(AccessTools.Method(typeof(ScavHelper),"RollMinorWound"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"NoLuck"));
   patch.Patch(AccessTools.Method(typeof(ScavHelper),"RollMajorWound"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"NoLuck"));
   var pad=ModHelper.SpawnItem("magnifier");pad.name="THROTTLE_PADDING";byte[] random=new byte[128*1024];new System.Random(1315).NextBytes(random);pad.shortDescription=Convert.ToBase64String(random);e.TryAddToPlayerInv(pad);root.World.Index();Signal("THROTTLE_PAD");
   var wait=Wait(()=>Received("THROTTLE_PAD_SEEN"),"large state transferred under the upload cap",120);while(wait.MoveNext())yield return wait.Current;
   var original=ModHelper.SpawnItem("backpack_large");original.name="THROTTLE_ORIGINAL_BAG";e.TryAddToPlayerInv(original);e.selectedBackpack=original;
   var map=MapUIManager.Instance;var location=AfterhourLocation.Get("dumping_ground");Require(location!=null,"dumping ground exists");map.Travel(location);
   wait=Wait(()=>map.Current==location&&!map.IsInTransit&&e.afterhourPocketSlotInvBackpack.childItem!=null,"host reached the dumping ground with the temporary backpack",90);while(wait.MoveNext())yield return wait.Current;
   var bag=e.afterhourPocketSlotInvBackpack.childItem;Require(OutingKits.IsVoidBag(bag),"host uses the temporary backpack");bag.name="THROTTLE_HOST_BAG";
   GameItem loot=null;
   for(int attempt=0;attempt<5&&loot==null&&ScavHelper.CanScavenge();attempt++){
    ScavHelper.ScavengeDumpingGrounds();
    foreach(var item in new List<GameItem>(e.afterhourInventory.childItems))if(GraphUtils.TryAcceptAll(bag,item)==0){loot=item;break;}
   }
   Require(loot!=null,"host scavenged an item and put it into the temporary backpack");loot.name="THROTTLE_HOST_LOOT";root.World.Index();Signal("THROTTLE_OUT_READY");
   wait=Wait(()=>Received("THROTTLE_OUT_SEEN"),"client received the scavenged item inside the temporary backpack",120);while(wait.MoveNext())yield return wait.Current;
   map.ReturnNow();wait=Wait(()=>map.Current==null&&!map.IsInTransit&&loot.parentInventory!=null,"host returned to the shop with the loot",90);while(wait.MoveNext())yield return wait.Current;root.World.Index();Signal("THROTTLE_RETURN");
   wait=Wait(()=>Received("THROTTLE_RETURN_SEEN"),"client kept the loot after the temporary backpack was removed",120);while(wait.MoveNext())yield return wait.Current;
   Require(root.ProbeRateBytes>=128*1024&&root.ProbeRateBytes<=125000*(root.ProbeRateElapsed+.1),"upload cap transferred substantial data without exceeding approximately 1 Mbps");
   Debug.Log("[SMC PASS] limited direct upload bytes="+root.ProbeRateBytes+" elapsed="+root.ProbeRateElapsed.ToString("F1")+" seconds");
  }
  static IEnumerator ThrottledOutingClient(Connection root){
   var wait=Wait(()=>Received("THROTTLE_PAD"),"large state fixture",40);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Find(root,"THROTTLE_PADDING")!=null&&Find(root,"THROTTLE_PADDING").shortDescription.Length>=170000,"large host state arrives under throttling",120);while(wait.MoveNext())yield return wait.Current;Signal("THROTTLE_PAD_SEEN");
   wait=Wait(()=>Received("THROTTLE_OUT_READY"),"host scavenged outside",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>{var bag=Find(root,"THROTTLE_HOST_BAG");var item=Find(root,"THROTTLE_HOST_LOOT");return bag!=null&&item!=null&&OutingKits.Contents(bag).Contains(item);},"host backpack and its scavenged content are replicated",120);while(wait.MoveNext())yield return wait.Current;Signal("THROTTLE_OUT_SEEN");
   wait=Wait(()=>Received("THROTTLE_RETURN"),"host returned",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>{var item=Find(root,"THROTTLE_HOST_LOOT");return root.IsClient&&root.Ready&&Find(root,"THROTTLE_HOST_BAG")==null&&item!=null&&item.parentInventory!=null&&root.World.Address(item.parentInventory)!=null&&root.World.Address(item.parentInventory).root<40;},"loot survives the removed backpack on the client",120);while(wait.MoveNext())yield return wait.Current;Signal("THROTTLE_RETURN_SEEN");
  }
 }
}
#endif
