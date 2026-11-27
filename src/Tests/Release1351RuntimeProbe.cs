#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static bool rejectSnapshot1351;
  static int capturedMovable1351;
  static void CountArrange1351(object __instance){arrangeCommits1349++;capturedMovable1351=ModReflect.Get<int>(__instance,"MovableCount");}
  static void RejectSnapshot1351(string __0){
   if(rejectSnapshot1351&&RemoteActions.CurrentOwner>0&&__0!="capture-complete"){
    rejectSnapshot1351=false;throw new InvalidOperationException("Snapshot guard: controlled inventory change during friend sort");
   }
  }
  static IEnumerator Release1351(Connection root,bool host){
   SmartArrangeIntent.Bind();StoragePagesIntent.Bind();
   var ready=Wait(()=>SmartArrangeIntent.Bound&&MainPages()!=null,"real Smart Arrange and storage pages loaded",60);while(ready.MoveNext())yield return ready.Current;
   var mod=ModReflect.FindType("SmartInventoryArrange.SmartArrangeMod");var undo=ModReflect.FindType("SmartInventoryArrange.ArrangeUndo");
   var tx=ModReflect.FindType("SmartInventoryArrange.InventoryTransaction");
   var grid=EmporiumEntry.Instance.invElement;var patch=new Harmony("shop-together.release1351-probe");arrangeCommits1349=0;
   patch.Patch(ModReflect.Target(tx,"ApplyCore"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"CountArrange1351"));
   if(host)patch.Patch(ModReflect.Target(tx,"AssertSnapshotCurrent"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"RejectSnapshot1351"));
   IEnumerator wait;
   try{
    if(host){
     foreach(var old in new List<GameItem>(grid.childItems))old.Destroy();
     ModHelper.SetModData("player.shop_storage_pages","page_state_v1","{\"unlocked\":2,\"revision\":100,\"names\":[\"\",\"\"]}");
     var saved=ModReflect.CallStatic(ModReflect.FindType("ShopStoragePages.LoadPages"),"ReadMetadata",ModHelper.GetModData("player.shop_storage_pages","page_state_v1"));
     ModReflect.Call(MainPages(),"Restore",saved,null);
     for(int i=0;i<12;i++){
      var item=ModHelper.SpawnItem(i%2==0?"magnifier":"screwdriver");GeneralHelper.SetItemOwned(item,true);item.name="S49_PAGE_"+i;
      item.modifiedShape=new GridShapeBuilder(item.modifiedShape).SetPosition(i%4*5+1,11+i/4*3).Build();grid.UncheckedAccept(item);
     }
     foreach(var item in grid.childItems)Require(grid.CheckInventorySlotStillValid(item,item.modifiedShape,item.state),"original fixture has a legal non-overlapping slot: "+item.name);
     var held=PanelFixture("magnifier","PAGE1351_HELD");root.World.Index();long lease;
     int heldId=root.World.Id(held);Require(root.Leases.Claim(0,new[]{heldId},Time.realtimeSinceStartup,out lease),"host holds first-page item while friend sorts second page");
     string original=Layout1349(grid);root.SyncStateSoon();Signal("P51_READY");
     wait=Wait(()=>{root.Leases.Renew(0,Time.realtimeSinceStartup);return arrangeCommits1349>0&&!ModReflect.GetStatic<bool>(mod,"Busy");},"friend second-page async sort completes despite first-page lease",120);while(wait.MoveNext())yield return wait.Current;
     Require(capturedMovable1351==12&&root.Leases.Owns(0,heldId,lease,Time.realtimeSinceStartup),"solver only captured twelve requested-page items; other page remains held");
     Require(ModReflect.GetStatic(undo,"_latest")!=null,"page sort retained its native undo snapshot");
     Require(StoragePagesIntent.Page(grid)==0&&held.parentInventory==grid&&held.modifiedShape.minY<10,"host page and held item are unchanged");
     foreach(var item in grid.childItems)if(item.name.StartsWith("S49_PAGE_"))Require(item.modifiedShape.minY>=11,"sorted item stays on requested page");
     root.Leases.Release(0,new[]{heldId});root.SyncStateSoon();Signal("P51_SORTED:"+Layout1349(grid));
     wait=Wait(()=>ModReflect.GetStatic(undo,"_latest")==null&&!ModReflect.GetStatic<bool>(undo,"Running"),"friend native StartLatest shortcut executes host undo",90);while(wait.MoveNext())yield return wait.Current;
     Require(Layout1349(grid)==original&&StoragePagesIntent.Page(grid)==0,"shortcut restores exact requested-page positions without switching host page; actual="+Layout1349(grid)+" expected="+original+" page="+StoragePagesIntent.Page(grid));
     root.SyncStateSoon();Signal("P51_UNDONE:"+original);
     wait=Wait(()=>Received("P51_UNDO_SYNCED"),"friend sees authoritative undo result",90);while(wait.MoveNext())yield return wait.Current;
     int commits=arrangeCommits1349;
     NativeClick(0,()=>StoragePagesIntent.OnPage(grid,1,()=>{ModReflect.CallStatic(mod,"Run",grid,false,null,0,0,null);return true;}));
     wait=Wait(()=>arrangeCommits1349>commits&&!ModReflect.GetStatic<bool>(mod,"Busy"),"host creates newer own arrangement",90);while(wait.MoveNext())yield return wait.Current;
     var hostGroup=ModReflect.GetStatic(undo,"_latest");Signal("P51_HOST_SORTED");
     wait=Wait(()=>Received("P51_FOREIGN_REFUSED"),"friend shortcut refuses host's newer undo",90);while(wait.MoveNext())yield return wait.Current;
     Require(hostGroup==ModReflect.GetStatic(undo,"_latest"),"refused shortcut preserves host undo");
     rejectSnapshot1351=true;Signal("P51_FAIL_READY");
     wait=Wait(()=>Received("P51_FAILURE_RECEIVED"),"async native snapshot rejection reaches originating friend",90);while(wait.MoveNext())yield return wait.Current;
     Require(!rejectSnapshot1351&&!ModReflect.GetStatic<bool>(mod,"Busy"),"actual SIA coroutine caught rejection and finished");
    }else{
     wait=Wait(()=>Find(root,"S49_PAGE_11")!=null&&MainPages()!=null&&MainPageCount()==2&&Received("P51_READY"),"page fixtures and unlocked page replicated",90);while(wait.MoveNext())yield return wait.Current;
     NativeClick(0,()=>StoragePagesIntent.OnPage(grid,1,()=>{ModReflect.CallStatic(mod,"Run",grid,false,null,0,0,null);return true;}));
     string expected=null;wait=Wait(()=>{if(expected==null)expected=ReceivePrefix("P51_SORTED:");return expected!=null&&Layout1349(grid)==expected;},"friend receives requested-page layout",120);while(wait.MoveNext())yield return wait.Current;
     Require(arrangeCommits1349==0,"friend does not commit local sorting");
     NativeClick(0,()=>ModReflect.CallStatic(undo,"StartLatest"));
     expected=null;wait=Wait(()=>{if(expected==null)expected=ReceivePrefix("P51_UNDONE:");return expected!=null&&Layout1349(grid)==expected;},"native undo shortcut result returns to friend",90);while(wait.MoveNext())yield return wait.Current;Signal("P51_UNDO_SYNCED");
     wait=Wait(()=>Received("P51_HOST_SORTED"),"newer host arrangement ready",90);while(wait.MoveNext())yield return wait.Current;
     RemoteActions.LastNote="";NativeClick(0,()=>ModReflect.CallStatic(undo,"StartLatest"));
     wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC373~"),"friend is told own undo is no longer available",60);while(wait.MoveNext())yield return wait.Current;Signal("P51_FOREIGN_REFUSED");
     wait=Wait(()=>Received("P51_FAIL_READY"),"native async failure fixture ready",90);while(wait.MoveNext())yield return wait.Current;
     RemoteActions.LastNote="";NativeClick(0,()=>StoragePagesIntent.OnPage(grid,1,()=>{ModReflect.CallStatic(mod,"Run",grid,false,null,0,0,null);return true;}));
     wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC374~"),"native async failure is localized and relayed to friend",90);while(wait.MoveNext())yield return wait.Current;Signal("P51_FAILURE_RECEIVED");
     Require(arrangeCommits1349==0,"client remains a replica throughout sort, undo and failure");
    }
   }finally{patch.UnpatchAll("shop-together.release1351-probe");rejectSnapshot1351=false;}
  }
 }
}
#endif
