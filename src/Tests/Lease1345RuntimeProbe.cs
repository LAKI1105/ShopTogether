#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static void LeaseGroup1345(params GameItem[] items){
   ShowStorage();var group=ItemMultiSelectHandler.current;
   group.UpdateReset();var selected=ModReflect.Get<List<GameItemElement>>(group,"selectedItems");
   var homes=ModReflect.Get<Dictionary<GameItemElement,GameInventory>>(group,"selectedHomes");
   foreach(var item in items){selected.Add((GameItemElement)item);homes[(GameItemElement)item]=item.parentInventory;}
   pointer=Point(items[0]);pressed.Add(group.key);
   NativeClick(0,()=>AccessTools.Method(typeof(ItemMultiSelectHandler),"StartGroupDrag").Invoke(group,null));
   Require(group.IsGroupDragging,"native group drag started");
  }
  static void LeaseGroupRelease1345(){
   var group=ItemMultiSelectHandler.current;pointer=Vector3.zero;
   pressed.Remove(group.key);group.UpdateRelease(new HashSet<KeyCode>{group.key});
   Require(!group.IsGroupDragging,"native group mouse release ends drag");
  }
  static IEnumerator ExpiredHostControl1345(Connection root,GameItem item){
   // Controlled time at the lease-book boundary: one expired lease and one
   // still valid lease belonging to the same peer when its renewal arrives.
   var book=new LeaseBook((a,b)=>false);long old=0,live=0;
   Require(book.Claim(7,new[]{1},0,out old)&&book.Claim(7,new[]{2},5,out live),"expiry clock setup");
   book.Renew(7,20);
   Require(book.OwnerOf(0,1,20)<0&&!book.Owns(7,1,old,20),"renewal cannot revive expired guest control");
   Require(book.Owns(7,2,live,30),"renewal keeps unexpired guest control valid");
   int id=root.World.Id(item);object holder=new object();double now=Time.realtimeSinceStartup;
   root.Leases.Claim(999,new[]{id},now-21,out old);
   root.Process(999,new Request{kind=RequestKind.Renew},r=>{throw new InvalidOperationException("Unexpected renew reply");});
   Require(root.BusyOwner(new[]{id})<0&&NativeActions.Hold(holder,new[]{id}),"host takes item after late guest renewal without false occupancy");NativeActions.Unhold(holder);
   root.Leases.Claim(999,new[]{id},Time.realtimeSinceStartup,out live);
   root.Process(999,new Request{kind=RequestKind.Renew},r=>{});
   Require(!NativeActions.Hold(holder,new[]{id})&&root.Leases.Owns(999,id,live,Time.realtimeSinceStartup),"host still cannot steal an unexpired guest hold");root.Leases.Disconnect(999);
   // The authoritative entry expired while the host still cached its token.
   root.Leases.Claim(0,new[]{id},Time.realtimeSinceStartup-21,out old);root.LocalLeases[id]=old;
   Require(NativeActions.Hold(holder,new[]{id})&&root.LocalLeases[id]!=old&&root.Leases.Owns(0,id,root.LocalLeases[id],Time.realtimeSinceStartup),"host replaces its stale cached token before acting");
   var e=EmporiumEntry.Instance;var home=item.parentInventory;
   foreach(var destination in new[]{e.backInvinvElement,home}){
    root.Submit(new Request{kind=RequestKind.AutoPlace,items=new[]{id},inventory=root.World.Address(destination),moves=new[]{new Placement{item=id,count=item.unitCount,version=root.World.Versions[id]}}});
    Require(item.parentInventory==destination,"host move succeeds with reacquired control");
   }
   NativeActions.Unhold(holder);yield return new WaitForSecondsRealtime(2.2f);
   Require(!ItemBusyHint.Visible,"previous genuine occupancy hint has expired");
   foreach(var kind in new[]{RequestKind.Move,RequestKind.AutoPlace,RequestKind.ItemAction}){
    RemoteActions.LastNote=null;
    root.Send(new Request{kind=kind,items=new[]{id},lease=old,operation=(int)ItemOperation.Toggle,inventory=root.World.Address(e.backInvinvElement),moves=kind==RequestKind.ItemAction?null:new[]{new Placement{item=id,count=item.unitCount,lease=old,version=root.World.Versions[id],inventory=root.World.Address(e.backInvinvElement)}}});
    Require(RemoteActions.LastNote==Loc.Text("~SMC371~")&&!ItemBusyHint.Visible&&item.parentInventory==home,"expired "+kind+" reports lost control without another-player hint or movement");
   }
   Require(NativeActions.Hold(holder,new[]{id}),"host can pick up again after stale requests");NativeActions.Unhold(holder);
  }
  static IEnumerator LeaseHost1345(Connection root){
   EmporiumOption.Instance.autoCallNext=false;EmporiumOption.Instance.autoNextDialog=false;
   DialogUIManager.Instance.StopAllTrackedDialogueCoroutines();DialogUIManager.Instance.currentDialog=null;
   var bag=PanelFixture("backpack_large","LEASE45_BAG");
   var child=PanelFixture("nuts_metal","LEASE45_CHILD");
   var loose=PanelFixture("magnifier","LEASE45_LOOSE");
   Require(GraphUtils.TryAcceptAll(bag,child)==0,"fixture item placed inside backpack");root.World.Index();
   int bagId=root.World.Id(bag),childId=root.World.Id(child),looseId=root.World.Id(loose);
   root.SyncStateSoon();var indexed=Wait(()=>root.World.Versions.ContainsKey(looseId),"fixture version captured for stale-control checks",30);while(indexed.MoveNext())yield return indexed.Current;
   var expired=ExpiredHostControl1345(root,loose);while(expired.MoveNext())yield return expired.Current;
   // Keep one successful local hold, then try to extend it with an occupied
   // item. This deterministically reaches the partial-acquisition failure.
   object holder=new object();long token;
   Require(NativeActions.Hold(holder,new[]{bagId}),"host receives first item control");
   Require(root.Leases.Claim(999,new[]{looseId},Time.realtimeSinceStartup,out token),"conflicting owner setup");
   Require(!NativeActions.Hold(holder,new[]{bagId,looseId}),"mixed host hold is refused");
   Require(!NativeActions.Retains(bagId)&&!root.LocalLeases.ContainsKey(bagId)&&root.Leases.Available(999,bagId,Time.realtimeSinceStartup),"failed host hold releases previously acquired backpack and contents");
   Require(root.Leases.Owns(999,looseId,token,Time.realtimeSinceStartup),"rollback preserves other player's control");root.Leases.Disconnect(999);
   object retained=new object();Require(NativeActions.Hold(retained,new[]{bagId}),"independent holder setup");
   Require(root.Leases.Claim(999,new[]{looseId},Time.realtimeSinceStartup,out token),"second conflict setup");
   Require(!NativeActions.Hold(holder,new[]{bagId,looseId})&&root.LocalLeases.ContainsKey(bagId),"rollback preserves another local holder");
   NativeActions.Unhold(retained);root.Leases.Disconnect(999);root.SyncStateSoon();Signal("LEASE45_READY");
   foreach(string phase in new[]{"RELEASE","RESYNC"}){
    var wait=Wait(()=>Received("LEASE45_"+phase+"_HELD"),"guest group held for "+phase,60);while(wait.MoveNext())yield return wait.Current;
    Require(root.BusyOwner(new[]{bagId})>0&&root.BusyOwner(new[]{childId})>0&&root.BusyOwner(new[]{looseId})>0,"group-held backpack protects contents and loose item");
    Signal("LEASE45_"+phase+"_GO");
    wait=Wait(()=>Received("LEASE45_"+phase+"_DONE"),"guest completed "+phase,60);while(wait.MoveNext())yield return wait.Current;
    wait=Wait(()=>root.BusyOwner(new[]{bagId,childId,looseId})<0,"host sees all group leases released after "+phase,30);while(wait.MoveNext())yield return wait.Current;
    Require(NativeActions.Hold(holder,new[]{bagId,childId,looseId}),"host takes over backpack, contents and loose item after "+phase);NativeActions.Unhold(holder);
    Signal("LEASE45_"+phase+"_ACK");
   }
   var held=Wait(()=>Received("LEASE45_CHILD_HELD"),"guest holds backpack content before disconnection",60);while(held.MoveNext())yield return held.Current;
   Require(root.BusyOwner(new[]{bagId})>0&&root.BusyOwner(new[]{childId})>0&&root.BusyOwner(new[]{looseId})<0,"held content blocks its backpack but not unrelated items");
   root.ProbeCutOutingLink1344();
   held=Wait(()=>root.BusyOwner(new[]{bagId,childId,looseId})<0,"disconnect releases content and backpack control",60);while(held.MoveNext())yield return held.Current;
   Require(NativeActions.Hold(holder,new[]{bagId,childId}),"host takes over after transport disconnect");NativeActions.Unhold(holder);
   held=Wait(()=>Received("LEASE45_REJOINED"),"guest rejoined with no stale holds",120);while(held.MoveNext())yield return held.Current;
   Require(root.BusyOwner(new[]{bagId,childId,looseId})<0,"rejoin does not restore disconnected control");Signal("LEASE45_FINISH");
  }
  static IEnumerator LeaseClient1345(Connection root){
   bool ready=false;var wait=Wait(()=>(ready=ready||Received("LEASE45_READY"))&&Find(root,"LEASE45_CHILD")!=null&&Find(root,"LEASE45_LOOSE")!=null,"lease fixtures synced",90);while(wait.MoveNext())yield return wait.Current;
   var bag=Find(root,"LEASE45_BAG");var child=Find(root,"LEASE45_CHILD");var loose=Find(root,"LEASE45_LOOSE");
   int bagId=root.World.Id(bag),childId=root.World.Id(child),looseId=root.World.Id(loose);
   foreach(string phase in new[]{"RELEASE","RESYNC"}){
    LeaseGroup1345(bag,loose);
    wait=Wait(()=>root.LocalLeases.ContainsKey(bagId)&&root.LocalLeases.ContainsKey(looseId),"guest receives complete group control for "+phase,30);while(wait.MoveNext())yield return wait.Current;
    Signal("LEASE45_"+phase+"_HELD");wait=Wait(()=>Received("LEASE45_"+phase+"_GO"),"host observes group control",60);while(wait.MoveNext())yield return wait.Current;
    if(phase=="RELEASE")LeaseGroupRelease1345();
    else{root.ProbeResync1339();wait=Wait(()=>!root.ProbeAwaitingFull&&!ItemMultiSelectHandler.current.IsGroupDragging,"full resync cancels group drag",60);while(wait.MoveNext())yield return wait.Current;pressed.Remove(ItemMultiSelectHandler.current.key);}
    wait=Wait(()=>!root.LocalLeases.ContainsKey(bagId)&&!root.LocalLeases.ContainsKey(looseId),"local group control cleared after "+phase,30);while(wait.MoveNext())yield return wait.Current;
    Require(!NativeActions.Retains(bagId)&&!NativeActions.Retains(looseId),"no orphan group holders after "+phase);
    Signal("LEASE45_"+phase+"_DONE");wait=Wait(()=>Received("LEASE45_"+phase+"_ACK"),"host successfully takes group over",60);while(wait.MoveNext())yield return wait.Current;
   }
   OpenNativeContents(bag);bag.contentWindow.Center();RenderUI();BeginDrag(child);
   Require(ItemMouseDragHandler.current.IsDraggingItem,"guest drags item inside backpack");
   wait=Wait(()=>root.LocalLeases.ContainsKey(childId),"guest receives backpack-content control",30);while(wait.MoveNext())yield return wait.Current;Signal("LEASE45_CHILD_HELD");
   wait=Wait(()=>!root.IsClient&&MainMenuUIController.Instance!=null&&EmporiumEntry.Instance==null,"transport loss returns guest to menu",90);while(wait.MoveNext())yield return wait.Current;
   pressed.Clear();Require(root.LocalLeases.Count==0&&NativeActions.holders.Count==0,"disconnect clears all local leases and holders");
   root.ProbeJoin(Argument("--smc-invite"));wait=Wait(()=>root.Ready&&Find(root,"LEASE45_CHILD")!=null,"guest rejoins shared shop",90);while(wait.MoveNext())yield return wait.Current;
   child=Find(root,"LEASE45_CHILD");bag=Find(root,"LEASE45_BAG");
   Require(root.World.Id(child)==childId&&root.World.Id(bag)==bagId&&child.parentInventory.GetParentItem()==bag,"rejoin preserves backpack and content identities");
   Require(root.LocalLeases.Count==0&&NativeActions.holders.Count==0,"rejoin has no leftover local control");Signal("LEASE45_REJOINED");
   wait=Wait(()=>Received("LEASE45_FINISH"),"host confirms rejoin control state",60);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
