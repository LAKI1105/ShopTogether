#if LAN_PROBE
using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static IEnumerator HostDrag1345(Connection root){
   // Select native mouse input at startup. Unity's internal-call input methods
   // cannot safely be restored by unpatching a replacement transpiler in Mono.
   EmporiumOption.Instance.autoCallNext=false;EmporiumOption.Instance.autoNextDialog=false;
   DialogUIManager.Instance.StopAllTrackedDialogueCoroutines();DialogUIManager.Instance.currentDialog=null;
   var e=EmporiumEntry.Instance;var item=PanelFixture("magnifier","HOST45_DRAG");root.World.Index();root.SyncStateSoon();
   StoreUIManager.Instance.CloseAllUI();ShowStorage();e.ShowFrontCounters();e.Validate(true);yield return null;
   foreach(var destination in new GameInventory[]{e.backInvinvElement,e.invElement,e.frontInvinvElement,e.invElement}){
    var gesture=NativeDragStart1327(item);while(gesture.MoveNext())yield return gesture.Current;
    gesture=NativeDragDrop1327(destination,item);while(gesture.MoveNext())yield return gesture.Current;
    var moved=Wait(()=>item.parentInventory==destination&&!ItemMouseDragHandler.current.IsDraggingItem,"host native mouse places item into "+root.World.Address(destination).Key,30);while(moved.MoveNext())yield return moved.Current;
   }
   // A rejected stale placement must show its real reason, not a busy-player hint.
   yield return new WaitForSecondsRealtime(2.2f);Require(!ItemBusyHint.Visible,"no busy hint before stale request");RemoteActions.LastNote=null;
   int id=root.World.Id(item);root.Submit(new Request{kind=RequestKind.AutoPlace,items=new[]{id},inventory=root.World.Address(e.backInvinvElement),moves=new[]{new Placement{item=id,count=1,version=root.World.Versions[id]-1}}});
   Require(item.parentInventory==e.invElement&&RemoteActions.LastNote==Loc.Text("~SMC312~")&&!ItemBusyHint.Visible,"stale host move shows state-change reason without another-player hint");
   Signal("HOST45_CLAIM");var wait=Wait(()=>Received("HOST45_HELD"),"guest holds test item",60);while(wait.MoveNext())yield return wait.Current;
   Require(root.BusyOwner(new[]{id})>0,"host identifies actual guest item owner");
   pointer=Point(item);yield return null;var input=NativeMouseAction1327("DOWN");while(input.MoveNext())yield return input.Current;
   pointer+=new Vector3(30,20,0);input=NativeMouseAction1327("MOVE");while(input.MoveNext())yield return input.Current;
   Require(!ItemMouseDragHandler.current.IsDraggingItem&&ItemBusyHint.Visible,"host pickup is correctly blocked while guest holds the same item");
   input=NativeDragRelease1327();while(input.MoveNext())yield return input.Current;Signal("HOST45_RELEASE");
   wait=Wait(()=>Received("HOST45_RELEASED"),"guest released item",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>root.BusyOwner(new[]{id})<0,"host receives release of guest ownership",30);while(wait.MoveNext())yield return wait.Current;
   var retry=NativeDragStart1327(item);while(retry.MoveNext())yield return retry.Current;
   retry=NativeDragDrop1327(e.backInvinvElement,item);while(retry.MoveNext())yield return retry.Current;
   wait=Wait(()=>item.parentInventory==e.backInvinvElement,"host drags onto counter after guest releases item",30);while(wait.MoveNext())yield return wait.Current;
   Signal("HOST45_COUNTER");wait=Wait(()=>Received("HOST45_SEEN"),"guest receives host counter placement",60);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator HostDragClient1345(Connection root){
   bool ready=false;var wait=Wait(()=>(ready=ready||Received("HOST45_CLAIM"))&&Find(root,"HOST45_DRAG")!=null,"host drag sequence completed",90);while(wait.MoveNext())yield return wait.Current;
   int id=root.World.Id(Find(root,"HOST45_DRAG"));root.Claim(new[]{id});
   wait=Wait(()=>root.LocalLeases.ContainsKey(id),"guest receives item control",30);while(wait.MoveNext())yield return wait.Current;Signal("HOST45_HELD");
   wait=Wait(()=>Received("HOST45_RELEASE"),"host observed actual contention",60);while(wait.MoveNext())yield return wait.Current;root.Release(new[]{id});Signal("HOST45_RELEASED");
   wait=Wait(()=>Received("HOST45_COUNTER"),"host counter drop completed",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Find(root,"HOST45_DRAG").parentInventory==EmporiumEntry.Instance.backInvinvElement,"guest sees host item on counter",60);while(wait.MoveNext())yield return wait.Current;Signal("HOST45_SEEN");
  }
 }
}
#endif
