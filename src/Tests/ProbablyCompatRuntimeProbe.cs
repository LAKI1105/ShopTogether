#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
 // An in-process stand-in with the inspected 2.6.1 method shapes. It never contacts the real service.
 internal static class TestVisitCore { internal static int Calls; [MethodImpl(MethodImplOptions.NoInlining)] internal static void Update() { Calls++; } }
 internal static class TestVisitState {
  internal static bool Active;internal static GameItem Preview,Selected;
  internal static bool IsPreview(GameItem item) { return item==Preview; }
  internal static bool IsSelectedOriginal(GameItem item) { return item==Selected; }
 }
 internal static class TestVisitTransfer { internal static bool Locked {get;set;} internal static int Prepared;[MethodImpl(MethodImplOptions.NoInlining)] internal static void Prepare(object trade) { Prepared++; } }
 internal static class TestVisitSprite { internal static string Register(string ignored) { return "lemocuber_probablymultiplayer_player_sprite"; } }
 internal static partial class RuntimeProbe {
  static void BindMockVisit() {
   ProbablyMultiplayerCompat.ProbeEnabled=true;ProbablyMultiplayerCompat.ProbeBind();
   Require(ProbablyMultiplayerCompat.ProbeBound,"compatibility bridge resolves and patches the simulated visitor methods");
  }
  // Synthetic visitor/transaction state: exercises the host boundary without connecting to the other mod's public service.
  static IEnumerator ProbablyCompatHost(Connection root) {
   var a=Find(root,"SMC_HOST_ITEM");var b=Find(root,"SMC_CLIENT_ITEM");
   Require(a!=null&&b!=null,"compatibility fixtures exist");
   BindMockVisit();TestVisitState.Active=true;TestVisitState.Preview=b;
   TestVisitCore.Update();Require(TestVisitCore.Calls==1,"host's visitor update remains active");
   int aId=root.World.Id(a),bId=root.World.Id(b);
   var selection=ModReflect.Get<List<GameItemElement>>(ItemMultiSelectHandler.current,"selectedItems");
   selection.Clear();selection.Add((GameItemElement)a);selection.Add((GameItemElement)b);
   AccessTools.Method(typeof(ItemMultiSelectHandler),"CommitPrecisePlacement").Invoke(ItemMultiSelectHandler.current,new object[]{EmporiumEntry.Instance.backInvinvElement,0,0});
   Require(a.parentInventory==EmporiumEntry.Instance.invElement&&b.parentInventory==EmporiumEntry.Instance.invElement,"host group move cannot bypass visitor preview protection");selection.Clear();
   var invalid=new SlotMarker{item=b,inventory=EmporiumEntry.Instance.invElement,itemGridShape=b.modifiedShape,numTransfer=b.unitCount};int placed=-1;
   NativeClick(0,()=>placed=invalid.TryAcceptOnce(-1));Require(placed==0&&b.parentInventory==EmporiumEntry.Instance.invElement,"host cannot move a visitor preview outside the counters");
   var counter=new SlotMarker{item=b,inventory=EmporiumEntry.Instance.backInvinvElement,itemGridShape=b.modifiedShape,numTransfer=b.unitCount};
   Require(ProbablyMultiplayerCompat.AllowsHostPlacement(counter,-1),"host can still select the full visitor preview on a counter");
   Require(!ProbablyMultiplayerCompat.AllowsHostPlacement(counter,0),"host cannot split a visitor preview stack");
   var group=new Request{kind=RequestKind.Move,items=new[]{aId,bId},moves=new[]{new Placement{item=aId},new Placement{item=bId}}};
   Require(ProbablyMultiplayerCompat.BlockRequest(root,1,group),"protected preview cannot bypass the boundary through a group move");
   Signal("PM_READY");
   var wait=Wait(()=>Received("PM_PREVIEW_DENIED"),"friend's preview pickup was refused");while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("PM_ITEM_HELD")&&!root.Leases.Available(0,aId,Time.realtimeSinceStartup),"friend holds an ordinary shop item");while(wait.MoveNext())yield return wait.Current;
   bool refused=false;try{TestVisitTransfer.Prepare(null);}catch(InvalidOperationException){refused=true;}
   Require(refused,"a visitor trade cannot reserve the inventory over an earlier guest hold");Signal("PM_PREPARE_REFUSED");
   wait=Wait(()=>root.Leases.Available(0,aId,Time.realtimeSinceStartup),"friend released the item");while(wait.MoveNext())yield return wait.Current;
   TestVisitTransfer.Prepare(null);Require(TestVisitTransfer.Prepared==1,"visitor trade may prepare after the hold is released");
   TestVisitTransfer.Locked=true;Signal("PM_LOCKED");
   wait=Wait(()=>Received("PM_LOCK_DENIED"),"friend cannot modify inventory during visitor trade commit");while(wait.MoveNext())yield return wait.Current;
   TestVisitTransfer.Locked=false;Signal("PM_FREE");
   wait=Wait(()=>a.parentInventory==EmporiumEntry.Instance.backInvinvElement,"ordinary cooperative drag still works after visitor trade");while(wait.MoveNext())yield return wait.Current;
   Require(b.parentInventory==EmporiumEntry.Instance.invElement,"the visitor preview fixture remained untouched");
   Signal("PM_HOST_MOVED");wait=Wait(()=>Received("PM_CLIENT_SYNCED"),"friend received the confirmed ordinary item");while(wait.MoveNext())yield return wait.Current;
   ProbablyMultiplayerCompat.ProbeEnabled=false;TestVisitState.Active=false;TestVisitState.Preview=null;
   Signal("PM_DONE");
  }
  static IEnumerator ProbablyCompatGuest(Connection root) {
   BindMockVisit();Require(ReplicaSession.ClientStore,"friend is running the host replica");TestVisitCore.Update();Require(TestVisitCore.Calls==0,"replica does not run a second visitor simulation");
   var wait=Wait(()=>Received("PM_READY"),"visitor transaction fixture ready");while(wait.MoveNext())yield return wait.Current;
   var a=Find(root,"SMC_HOST_ITEM");var b=Find(root,"SMC_CLIENT_ITEM");int aId=root.World.Id(a),bId=root.World.Id(b);
   root.Claim(new[]{bId});wait=Wait(()=>!root.ProbePending(bId),"protected preview claim answered");while(wait.MoveNext())yield return wait.Current;
   Require(!root.LocalLeases.ContainsKey(bId),"guest cannot acquire visitor's preview item");Signal("PM_PREVIEW_DENIED");
   root.Claim(new[]{aId});wait=Wait(()=>!root.ProbePending(aId)&&root.LocalLeases.ContainsKey(aId),"guest acquires an unrelated item during the visit");while(wait.MoveNext())yield return wait.Current;
   Signal("PM_ITEM_HELD");wait=Wait(()=>Received("PM_PREPARE_REFUSED"),"host checked the earlier hold");while(wait.MoveNext())yield return wait.Current;
   root.Release(new[]{aId});wait=Wait(()=>Received("PM_LOCKED"),"visitor transaction is reserved");while(wait.MoveNext())yield return wait.Current;
   root.Claim(new[]{aId});wait=Wait(()=>!root.ProbePending(aId),"reserved transaction answers claim");while(wait.MoveNext())yield return wait.Current;
   Require(!root.LocalLeases.ContainsKey(aId),"guest cannot acquire an item while visitor transaction is reserved");Signal("PM_LOCK_DENIED");
   wait=Wait(()=>Received("PM_FREE"),"visitor transaction released");while(wait.MoveNext())yield return wait.Current;
   ShowStorage();BeginDrag(a);Require(ItemMouseDragHandler.current.IsDraggingItem,"guest can drag an ordinary item after the transaction");
   wait=Wait(()=>root.LocalLeases.ContainsKey(aId),"ordinary item lease granted after transaction");while(wait.MoveNext())yield return wait.Current;
   Drop(EmporiumEntry.Instance.backInvinvElement,a);
   wait=Wait(()=>Received("PM_HOST_MOVED"),"host accepted the ordinary item after transaction");while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>a.parentInventory==EmporiumEntry.Instance.backInvinvElement,"host and friend agree on ordinary item after transaction");while(wait.MoveNext())yield return wait.Current;
   Signal("PM_CLIENT_SYNCED");wait=Wait(()=>Received("PM_DONE"),"host completed the compatibility scenario");while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
