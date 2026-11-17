#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static IEnumerator ReportedHost1344(Connection root){
   var e=EmporiumEntry.Instance;var store=PlayerStore.Instance;var map=MapUIManager.Instance;
   EmporiumOption.Instance.autoCallNext=false;EmporiumOption.Instance.autoNextDialog=false;EmporiumOption.Instance.enableTypewriterEffect=false;
   store.devMode=true;store.isAfterhoursIntroduced=true;store.isStampIntroduced=true;store.storeState=PlayerStore.StoreState.CLOSED;store.visitLeftTonight=10;
   var pack=PanelFixture("backpack_large","P44_PACK");root.World.Index();e.HideResponseWindow();e.selectedBackpack=null;
   NativeClick(0,()=>StoreResponse.ShowAfterhourResponse());
   Require(e.responseInventoryClosable.childItems.Count>=2,"host input builds garbage-bag and backpack choice icons");
   GameItem none=null;foreach(var icon in e.responseInventoryClosable.childItems)if(icon.identifier=="plastic_bag")none=icon;
   Require(none!=null,"host garbage-bag choice exists");NativeClick(0,()=>none.ActivateSlotItem());
   var wait=Wait(()=>TransitUI.IsOpen&&e.selectedBackpack==null,"host activates no-backpack choice through native item action",30);while(wait.MoveNext())yield return wait.Current;map.CloseUI();
   NativeClick(0,()=>StoreResponse.ShowAfterhourResponse());GameItem choice=null;
   foreach(var icon in e.responseInventoryClosable.childItems)if(icon.name.Contains("P44_PACK"))choice=icon;
   Require(choice!=null,"host real-backpack choice exists");
   Require(!LocalControl.LocalChoice(e.responseInventoryClosable,pack),"choice exception does not admit a tracked real backpack");
   NativeClick(0,()=>choice.ActivateSlotItem());wait=Wait(()=>e.selectedBackpack==pack&&TransitUI.IsOpen,"host can select its real backpack",30);while(wait.MoveNext())yield return wait.Current;TransitUI.Close();
   Signal("P44_HOST_DEPART");NativeClick(0,()=>map.Travel(AfterhourLocation.Get("dumping_ground")));
   wait=Wait(()=>OutingKits.HostAt("dumping_ground")&&!map.IsInTransit,"host native travel completed",60);while(wait.MoveNext())yield return wait.Current;
   Require(e.afterhourPocketSlotInvBackpack.childItem==pack,"host selected backpack moved to the shared outing slot");root.SyncStateSoon();
   wait=Wait(()=>GeneralHelper.GetCustomName(pack)=="P44_SHARED_PACK","guest action updated the same shared host backpack",60);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>map.Return());if(GenericConfirmationUIManager.Instance.panel.activeSelf)NativeClick(0,()=>GenericConfirmationUIManager.Instance.ConfirmButtonPressed());
   wait=Wait(()=>map.Current==null&&!map.IsInTransit,"host native return completed",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("P44_HOME"),"guest followed the host home",60);while(wait.MoveNext())yield return wait.Current;
   Require(pack.parentInventory!=null&&pack.parentInventory!=e.afterhourPocketSlotInvBackpack,"shared real backpack returned to shop");
   // Both native leave paths must remain visible even though the host clears
   // currentClientInstance at the start of the walk.
   for(int pass=0;pass<2;pass++){
    Signal("P44_WALK="+pass);wait=Wait(()=>Received("P44_WALK_READY="+pass),"guest observing arrival "+pass,30);while(wait.MoveNext())yield return wait.Current;
    StoreShutterButton.Instance.OpenShutter();store.lockNextBellFlag=0;
    if(store.storeClientManager.clientStack.Count==0)store.storeClientManager.GenerateClient();store.TryCallNextClient();
    wait=Wait(()=>store.currentClientInstance!=null&&store.isClientArrived&&!StoreClientMono.Instance.IsMoving(),"native arrival completed "+pass,60);while(wait.MoveNext())yield return wait.Current;
    Signal("P44_ARRIVED="+pass);wait=Wait(()=>Received("P44_WALK_SEEN="+pass),"guest saw arrival movement "+pass,30);while(wait.MoveNext())yield return wait.Current;
    DialogUIManager.Instance.FlushScheduledEndActions();TradeClear1327();
    if(pass==0){
     Signal("P44_BURST_READY");wait=Wait(()=>Received("P44_BURST_OBSERVE"),"guest ready to observe rapid dialogue",30);while(wait.MoveNext())yield return wait.Current;
     var one=new Dialogue().SetText("CLIENT","P44_LINE_ONE");var two=one.NextDialogue().SetText("CLIENT","P44_LINE_TWO");two.NextDialogue().SetText("CLIENT","P44_LINE_THREE");
     var talk=DialogUIManager.Instance;Require(talk.StartDialogue(one,true),"host starts burst dialogue");talk.ContinueConversation();talk.ContinueConversation();root.SyncStateSoon();
     wait=Wait(()=>Received("P44_BURST_SEEN"),"guest displayed every same-frame host line in order",30);while(wait.MoveNext())yield return wait.Current;
     wait=Wait(()=>Received("P44_RESYNC_SEEN"),"resync did not replay prior dialogue frames",30);while(wait.MoveNext())yield return wait.Current;
    }
    Signal("P44_LEAVE="+pass);wait=Wait(()=>Received("P44_LEAVE_READY="+pass),"guest observing departure "+pass,30);while(wait.MoveNext())yield return wait.Current;
    store.DismissCurrentClient(false,pass==1,true);
    Require(store.currentClientInstance==null&&StoreClientMono.Instance.gameObject.activeSelf,"host clears customer before native leave finishes "+pass);
    wait=Wait(()=>!StoreClientMono.Instance.gameObject.activeSelf,"native leave completed "+pass,60);while(wait.MoveNext())yield return wait.Current;
    wait=Wait(()=>Received("P44_LEAVE_SEEN="+pass),"guest saw departure movement then hide "+pass,30);while(wait.MoveNext())yield return wait.Current;
   }
   Signal("P44_PERF");var perf=PerfHost1340(root);while(perf.MoveNext())yield return perf.Current;
  }
  static readonly List<string> shown1344=new List<string>();
  static void Shown1344(Dialogue __0){if(__0!=null&&__0.plainText!=null&&__0.plainText.StartsWith("P44_LINE_"))shown1344.Add(__0.plainText);}
  static IEnumerator ReportedClient1344(Connection root){
   EmporiumOption.Instance.enableTypewriterEffect=false;
   var observe=new Harmony("shop_together.reported1344");observe.Patch(AccessTools.Method(typeof(DialogUIManager),"DisplayLine"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"Shown1344"));
   try{
    var e=EmporiumEntry.Instance;var wait=Wait(()=>Received("P44_HOST_DEPART"),"host started outing via its own choice",60);while(wait.MoveNext())yield return wait.Current;
    wait=Wait(()=>OutingKits.Joined=="dumping_ground"&&e.afterhourPocketSlotInvBackpack.childItem!=null,"guest follows host with shared backpack",60);while(wait.MoveNext())yield return wait.Current;
    var pack=e.afterhourPocketSlotInvBackpack.childItem;Require(pack==Find(root,"P44_PACK"),"guest sees the selected real backpack, not a local copy");
    NativeClick(0,()=>GeneralHelper.SetCustomName(pack,"P44_SHARED_PACK"));
    wait=Wait(()=>OutingKits.Joined==null,"guest follows host return",60);while(wait.MoveNext())yield return wait.Current;Signal("P44_HOME");
    for(int pass=0;pass<2;pass++){
     wait=Wait(()=>Received("P44_WALK="+pass),"arrival observation requested "+pass,60);while(wait.MoveNext())yield return wait.Current;Signal("P44_WALK_READY="+pass);
     var positions=new HashSet<string>();bool arrived=false;float deadline=Time.realtimeSinceStartup+60;
     while(!arrived&&Time.realtimeSinceStartup<deadline){var mono=StoreClientMono.Instance;if(mono.gameObject.activeSelf)positions.Add(mono.transform.localPosition.ToString("F3"));arrived=Received("P44_ARRIVED="+pass);yield return null;}
     Require(arrived&&positions.Count>3,"guest renders multiple arrival positions "+pass+" samples="+positions.Count);Signal("P44_WALK_SEEN="+pass);
     if(pass==0){
      wait=Wait(()=>Received("P44_BURST_READY"),"rapid-dialogue setup ready",30);while(wait.MoveNext())yield return wait.Current;
      shown1344.Clear();Signal("P44_BURST_OBSERVE");wait=Wait(()=>shown1344.Count>=3,"all rapid dialogue lines reached guest",30);while(wait.MoveNext())yield return wait.Current;
      Require(string.Join(",",shown1344)=="P44_LINE_ONE,P44_LINE_TWO,P44_LINE_THREE","rapid lines displayed once and in host order");Signal("P44_BURST_SEEN");
      int count=shown1344.Count;long version=root.World.Version;root.ProbeResync1339();
      wait=Wait(()=>root.World.Version>version&&!root.ProbeAwaitingFull,"guest full resync completes during dialogue",60);while(wait.MoveNext())yield return wait.Current;yield return new WaitForSecondsRealtime(.5f);
      Require(shown1344.Count==count&&DialogUIManager.Instance.currentDialog.plainText=="P44_LINE_THREE","full resync keeps current dialogue without replay");Signal("P44_RESYNC_SEEN");
     }
     wait=Wait(()=>Received("P44_LEAVE="+pass),"departure observation requested "+pass,60);while(wait.MoveNext())yield return wait.Current;Signal("P44_LEAVE_READY="+pass);
     positions.Clear();deadline=Time.realtimeSinceStartup+60;bool retained=false;
     while(StoreClientMono.Instance.gameObject.activeSelf&&Time.realtimeSinceStartup<deadline){positions.Add(StoreClientMono.Instance.transform.localPosition.ToString("F3"));if(PlayerStore.Instance.currentClientInstance==null)retained=true;yield return null;}
     Require(!StoreClientMono.Instance.gameObject.activeSelf&&positions.Count>3&&retained,"guest shows departing customer after instance clears, then hides "+pass+" samples="+positions.Count);Signal("P44_LEAVE_SEEN="+pass);
    }
    wait=Wait(()=>Received("P44_PERF"),"baseline trade and performance regression starts",30);while(wait.MoveNext())yield return wait.Current;
    var perf=PerfClient1340(root);while(perf.MoveNext())yield return perf.Current;
   }finally{observe.UnpatchAll("shop_together.reported1344");shown1344.Clear();}
  }
 }
}
#endif
