#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static readonly Dictionary<int,IPEndPoint> groupPeers=new Dictionary<int,IPEndPoint>();
  static readonly HashSet<string> groupMessages=new HashSet<string>();
  static void GroupPoll(){while(control.Available>0){var ep=new IPEndPoint(IPAddress.Any,0);var bytes=control.Receive(ref ep);if(!IPAddress.IsLoopback(ep.Address)||bytes.Length>1024)continue;var s=Encoding.ASCII.GetString(bytes);groupMessages.Add(s);if(s.StartsWith("READY "))groupPeers[int.Parse(s.Substring(6))]=ep;}}
  static bool GroupAll(string key,int count){GroupPoll();for(int i=1;i<count;i++)if(!groupMessages.Contains(key+" "+i))return false;return true;}
  static void GroupSignal(string s){var b=Encoding.ASCII.GetBytes(s);foreach(var ep in groupPeers.Values)control.Send(b,b.Length,ep);}
  static double Percentile(List<double> samples,double q){samples.Sort();return samples.Count==0?0:samples[Math.Min(samples.Count-1,(int)(samples.Count*q))];}
  static IEnumerator GroupPerformance(Connection root,int count,int index){
   bool host=index==0;var item=Find(root,"SMC_GROUP_"+index);if(host)GroupSignal("MENUS");
   for(int phase=0;phase<2;phase++){
    string label=phase==0?"idle":"actions";IEnumerator wait;if(host)GroupSignal("PERF_"+phase);else{wait=Wait(()=>Received("PERF_"+phase),"performance phase "+label,120);while(wait.MoveNext())yield return wait.Current;}
    CaptureMilliseconds.Clear();var frames=new List<double>();var latency=new List<double>();float end=Time.realtimeSinceStartup+30,next=0,sentAt=0;int sequence=0;string pending=null;
    while(Time.realtimeSinceStartup<end){
     frames.Add(Time.unscaledDeltaTime*1000);
     if(pending!=null&&GeneralHelper.GetCustomName(item)==pending){latency.Add((Time.realtimeSinceStartup-sentAt)*1000);pending=null;}
     if(phase==1&&!host&&pending==null&&Time.realtimeSinceStartup>=next){pending="Perf "+index+" "+(++sequence);sentAt=Time.realtimeSinceStartup;next=sentAt+.5f;string name=pending;NativeClick(0,()=>GeneralHelper.SetCustomName(item,name));}
     yield return null;
    }
    if(pending!=null){wait=Wait(()=>GeneralHelper.GetCustomName(item)==pending,"final operation confirmed",60);while(wait.MoveNext())yield return wait.Current;latency.Add((Time.realtimeSinceStartup-sentAt)*1000);}
    Require(root.Ready&&(host||phase==0||latency.Count>0),"performance phase stayed connected and operations confirmed");
    Debug.Log("[SMC PERF] phase="+label+" players="+count+" index="+index+" frameMedianMs="+Percentile(frames,.5).ToString("F2")+" frameP95Ms="+Percentile(frames,.95).ToString("F2")+" captureMedianMs="+Percentile(CaptureMilliseconds,.5).ToString("F2")+" captureP95Ms="+Percentile(CaptureMilliseconds,.95).ToString("F2")+" confirmed="+latency.Count+" responseMedianMs="+Percentile(latency,.5).ToString("F2")+" responseP95Ms="+Percentile(latency,.95).ToString("F2")+" items="+root.World.Items.Count);
    if(host){wait=Wait(()=>GroupAll("PERFDONE"+phase,count),"all phase samples collected",120);while(wait.MoveNext())yield return wait.Current;}else Signal("PERFDONE"+phase+" "+index);
   }
   if(host)GroupSignal("COMPLETE");else{var wait=Wait(()=>Received("COMPLETE"),"performance host acknowledgement",120);while(wait.MoveNext())yield return wait.Current;}
   Debug.Log("[SMC COMPLETE] performance players="+count+" index="+index);
  }
  static IEnumerator GroupCheck(Connection root){
   int count=int.Parse(Argument("--smc-group")),index=int.Parse(Argument("--smc-index"));bool host=index==0;
   var wait=Wait(()=>MainMenuUIController.Instance!=null,"group main menu",120);while(wait.MoveNext())yield return wait.Current;
   AccessTools.Field(typeof(Connection),"playerName").SetValue(null,"SMC Player "+index);
   if(host){NewGameData.Instance.saveSlot=-777;NewGameData.Instance.saveFile=Argument("--smc-save");NewGameData.Instance.isLoadFromMainMenu=true;NewGameData.Instance.skipIntro=true;NewGameData.Instance.disableTutorialHint=true;MainMenuUIController.Instance.StartNewGameClick();
    wait=Wait(()=>EmporiumEntry.Instance!=null&&ItemMouseDragHandler.current!=null&&StoreStation.Instance!=null,"day70 store loaded",150);while(wait.MoveNext())yield return wait.Current;
    wait=Wait(()=>!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"loaded store transition finished",60);while(wait.MoveNext())yield return wait.Current;
    Require(StoreStation.Instance.dayCounter==70,"loaded the user's day70 snapshot");PlayerStore.Instance.saveSlotId=-777;InstallInput();var e=EmporiumEntry.Instance;
    var baseline=new WorldReplica();baseline.Index();Debug.Log("[SMC PERF] day70 originalItems="+baseline.Items.Count+" players="+count);
    // Reserve only two counters in this disposable copy for repeatable native drag fixtures.
    foreach(var inv in new[]{e.frontInvinvElement,e.backInvinvElement})foreach(var item in new List<GameItem>(inv.childItems))item.Destroy();
    for(int i=0;i<count;i++){var item=ModHelper.SpawnItem("magnifier");item.name="SMC_GROUP_"+i;item.mayStackItemFunc=(a,b)=>false;GeneralHelper.SetItemOwned(item,true);Require(GraphUtils.TryAcceptAll(e.backInvinvElement,item)==0,"group fixture "+i);}
    ShowStorage();control=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));string code=root.ProbeOpen();root.World.Index();Debug.Log("[SMC-INTERNAL-INVITE] "+code);Debug.Log("[SMC CONTROL] "+((IPEndPoint)control.Client.LocalEndPoint).Port);
    wait=Wait(()=>GroupAll("READY",count),"all "+count+" players loaded day70 host state",600);while(wait.MoveNext())yield return wait.Current;
   }else{InstallInput();control=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));other=new IPEndPoint(IPAddress.Loopback,int.Parse(Argument("--smc-control")));root.ProbeJoin(Argument("--smc-invite"));wait=Wait(()=>root.Ready&&Find(root,"SMC_GROUP_"+index)!=null,"group client received day70 world",180);while(wait.MoveNext())yield return wait.Current;ShowStorage();Signal("READY "+index);wait=Wait(()=>Received("MENUS"),"all players ready",600);while(wait.MoveNext())yield return wait.Current;}
   if(Argument("--smc-performance")=="1"){var measure=GroupPerformance(root,count,index);while(measure.MoveNext())yield return measure.Current;yield break;}
   var own=Find(root,"SMC_GROUP_"+index);StartOfDayUIManager.Instance.HideUI();AdvCalendarUIManager.Instance.CloseUI();ShowStorage();yield return null;Context(own);var menu=ModReflect.Get<PixelWindow>(ItemContextHandler.current,"contextWindow");menu.Center();RenderUI();var overlay=ModReflect.Get<Canvas>(ItemContextHandler.current,"overlayCanvas");bool foundMenu=false;foreach(var graphic in menu.handler.GetComponentsInChildren<UnityEngine.UI.Graphic>()){if(!graphic.raycastTarget||!graphic.gameObject.activeInHierarchy)continue;var gc=graphic.canvas;var pos=RectTransformUtility.WorldToScreenPoint(gc.renderMode==RenderMode.ScreenSpaceOverlay?null:gc.worldCamera,graphic.rectTransform.TransformPoint(graphic.rectTransform.rect.center));if(ItemContextHandler.current.FindContextMenuNode(pos,overlay)!=null){pointer=pos;foundMenu=true;break;}}Require(foundMenu,"test cursor is inside context menu");Require(MenuOpen,"player "+index+" native context menu open");
   if(host){GroupSignal("MENUS");wait=Wait(()=>GroupAll("MENU",count),"all menus open concurrently",90);while(wait.MoveNext())yield return wait.Current;Require(MenuOpen,"host menu remains open");GroupSignal("DRAG");}
   else{Signal("MENU "+index);wait=Wait(()=>Received("DRAG"),"independent menus barrier",90);while(wait.MoveNext())yield return wait.Current;Require(MenuOpen,"guest menu remains open");}
   ItemContextHandler.current.UpdateReset();BeginDrag(own);Require(ItemMouseDragHandler.current.IsDraggingItem,"player "+index+" native drag active");
   if(host){wait=Wait(()=>GroupAll("DRAGGING",count),"all players drag different items concurrently",90);while(wait.MoveNext())yield return wait.Current;Require(ItemMouseDragHandler.current.IsDraggingItem,"host retains drag with all guests dragging");GroupSignal("CONTEND");wait=Wait(()=>GroupAll("DENIED",count),"all later same-item claims denied",90);while(wait.MoveNext())yield return wait.Current;GroupSignal("DROP");}
   else{wait=Wait(()=>root.LocalLeases.ContainsKey(root.World.Id(own)),"guest owns its dragged item",90);while(wait.MoveNext())yield return wait.Current;Signal("DRAGGING "+index);wait=Wait(()=>Received("CONTEND"),"contention barrier",90);while(wait.MoveNext())yield return wait.Current;int busy=root.World.Id(Find(root,"SMC_GROUP_0"));root.Claim(new[]{busy});wait=Wait(()=>!root.ProbePending(busy),"contested claim reply",90);while(wait.MoveNext())yield return wait.Current;Require(!root.LocalLeases.ContainsKey(busy),"first holder retains shared item");Signal("DENIED "+index);wait=Wait(()=>Received("DROP"),"drop barrier",90);while(wait.MoveNext())yield return wait.Current;}
   // Each local UI chooses a separate cell; stagger only the drop to avoid choosing stale occupied cells.
   int oldX=own.modifiedShape.minX,oldY=own.modifiedShape.minY;yield return new WaitForSecondsRealtime(index*.5f);Drop(EmporiumEntry.Instance.backInvinvElement,own);
   wait=Wait(()=>own.parentInventory==EmporiumEntry.Instance.backInvinvElement&&(own.modifiedShape.minX!=oldX||own.modifiedShape.minY!=oldY),"player drop committed by host",90);while(wait.MoveNext())yield return wait.Current;
   if(!host)Signal("DROPPED "+index);
   else{wait=Wait(()=>GroupAll("DROPPED",count),"all player drops confirmed",90);while(wait.MoveNext())yield return wait.Current;GroupSignal("MEASURE");}
   if(!host){wait=Wait(()=>Received("MEASURE"),"performance measurement barrier",90);while(wait.MoveNext())yield return wait.Current;}
   CaptureMilliseconds.Clear();var frames=new List<double>();float until=Time.realtimeSinceStartup+30;while(Time.realtimeSinceStartup<until){frames.Add(Time.unscaledDeltaTime*1000);yield return null;}
   Require(root.Ready,"group session remains connected");Debug.Log("[SMC PERF] players="+count+" index="+index+" frameMedianMs="+Percentile(frames,.5).ToString("F2")+" frameP95Ms="+Percentile(frames,.95).ToString("F2")+" captureMedianMs="+Percentile(CaptureMilliseconds,.5).ToString("F2")+" captureP95Ms="+Percentile(CaptureMilliseconds,.95).ToString("F2")+" items="+root.World.Items.Count);
   if(host){wait=Wait(()=>GroupAll("MEASURED",count),"all performance samples complete",90);while(wait.MoveNext())yield return wait.Current;GroupSignal("COMPLETE");}
   else{Signal("MEASURED "+index);wait=Wait(()=>Received("COMPLETE"),"group host acknowledgement",90);while(wait.MoveNext())yield return wait.Current;}
   Debug.Log("[SMC COMPLETE] group players="+count+" index="+index);
  }
 }
}
#endif
