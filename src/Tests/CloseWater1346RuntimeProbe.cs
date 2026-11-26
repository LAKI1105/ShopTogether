#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
namespace ShopTogether {
 public sealed partial class Connection {
  // A deterministic channel: deliver complete or truncated bytes, then EOF in
  // the same Pump call. No Steam state or live test connection is modified.
  sealed class EndingChannel1346 : IChannel {
   readonly byte[] bytes;int offset;
   internal EndingChannel1346(byte[] bytes){this.bytes=bytes;}
   public bool Ready{get{return true;}} public bool Writing{get{return false;}}
   public int Receive(byte[] destination){int n=Math.Min(destination.Length,bytes.Length-offset);Buffer.BlockCopy(bytes,offset,destination,0,n);offset+=n;return n;}
   public void Send(byte[] data){} public void Dispose(){}
  }
  internal void ProbeCloseReason1346(){
   var before=modReport;
   try{
    var report=new ModReport{missing=new[]{new ModEntry{id="probe.required",name="Required test mod",version="1"}},update=new ModEntry[0],extra=new ModEntry[0]};
    var packets=new Peer();packets.Send(31,Encode(report));byte[] refusal=packets.outgoing.Dequeue();
    for(int mode=0;mode<5;mode++){
     var bytes=new List<byte>();
     if(mode==1)for(int i=0;i<80;i++){packets.Send(39,new byte[4]);bytes.AddRange(packets.outgoing.Dequeue());}
     if(mode<=1)bytes.AddRange(refusal);
     else if(mode==2){bytes.AddRange(refusal);bytes.RemoveAt(bytes.Count-1);}
     else if(mode==3)bytes.AddRange(new byte[]{0,0,0,1,250});
     var peer=new Peer{channel=new EndingChannel1346(bytes.ToArray()),authenticated=true,matched=true,versionSent=true,modsOk=true};
     modReport=null;Exception ended=null;
     for(int frame=0;frame<100&&ended==null;frame++)try{Pump(peer,false);}catch(Exception e){ended=e;}
     string expected=mode<=1?"~SMC067~":mode==3?"~SMC029~":"~SMC156~";
     if(ended==null||!ended.Message.Contains(expected))throw new InvalidOperationException("EOF case "+mode+" expected "+expected+" got "+(ended==null?"no close":ended.Message));
     if(mode<=1&&(modReport==null||modReport.missing.Length!=1||modReport.missing[0].id!="probe.required"))throw new InvalidOperationException("Refusal details lost at EOF");
     if(mode>=2&&modReport!=null)throw new InvalidOperationException("Incomplete or invalid frame was decoded");
     Debug.Log("[SMC PASS] EOF case "+mode+" preserves complete refusal, rejects truncation/invalid data, or reports empty close");
    }
    // Host must not execute a departing peer's request after detecting EOF.
    packets.Send(21,Encode(new Request{kind=RequestKind.Renew,sequence=123}));
    var hostPeer=new Peer{channel=new EndingChannel1346(packets.outgoing.Dequeue()),authenticated=true,matched=true,versionSent=true,modsOk=true};
    Exception hostEnd=null;try{Pump(hostPeer,true);}catch(Exception e){hostEnd=e;}
    if(hostEnd==null||!hostEnd.Message.Contains("~SMC156~")||hostPeer.lastSequence!=0)throw new InvalidOperationException("Host processed an action after EOF");
    Debug.Log("[SMC PASS] host still refuses to execute requests after detecting closed transport");
    var steam=new SteamRelay.SteamChannel(0);steam.Fail("Steam P2P error 4");
    Exception failure=null;try{bool writing=steam.Writing;}catch(Exception e){failure=e;}
    if(failure==null||!failure.Message.Contains("Steam P2P error 4"))throw new InvalidOperationException("Steam failure reason was discarded");
    Debug.Log("[SMC PASS] Steam per-channel failure reason survives the write-failure path");
   }finally{modReport=before;}
  }
 }
 internal static partial class RuntimeProbe {
  static int naturalWaterCalls1346;
  static void CountNaturalWater1346(){naturalWaterCalls1346++;}
  static void ReceiveCapacity1346(){
   var old=new MessageStream((data,length)=>true);var shared=new byte[MessageStream.Piece];
   for(int i=0;i<=MessageStream.MaxQueued/MessageStream.Piece;i++)old.Arrive(shared);
   Require(old.Failed,"old ungated receive path marks a live stream failed when unread data exceeds 64 MB");old.Close();
   var stream=new MessageStream((data,length)=>true);var buffer=new byte[32768];int sent=0,received=0,offset=0,waits=0;
   Action consume=()=>{int n=stream.Receive(buffer);if(n<=0)throw new InvalidOperationException("Buffered transfer stopped prematurely");
    if(offset==0&&BitConverter.ToInt32(buffer,0)!=received)throw new InvalidOperationException("Receive order changed");offset+=n;
    if(offset==MessageStream.Piece){if(buffer[n-1]!=(byte)(received%251))throw new InvalidOperationException("Receive contents changed");offset=0;received++;}};
   while(sent<320){
    if(!stream.CanArrive(MessageStream.Piece)){waits++;if(stream.Failed||waits>3000)throw new InvalidOperationException("Capacity wait failed");consume();continue;}
    var data=new byte[MessageStream.Piece];Buffer.BlockCopy(BitConverter.GetBytes(sent),0,data,0,4);data[data.Length-1]=(byte)(sent%251);stream.Arrive(data);sent++;
   }
   while(received<sent)consume();
   Require(waits>0&&!stream.Failed&&offset==0&&stream.Receive(buffer)==-1,"80 MB transfer pauses at the existing receive cap and drains in order without disconnecting");
   stream.Close();Require(!stream.CanArrive(1),"closed stream does not resume receiving");
  }
  static IEnumerator NaturalWater1346(Connection root,bool host){
   ReceiveCapacity1346();naturalWaterCalls1346=0;int waterErrors=0;
   var observer=new HarmonyLib.Harmony("shop-together.natural-water1346-probe");
   observer.Patch(HarmonyLib.AccessTools.Method(typeof(WaterFeatureHelper),"GetWaterIndex"),prefix:new HarmonyLib.HarmonyMethod(typeof(RuntimeProbe),"CountNaturalWater1346"));
   Application.LogCallback log=(message,stack,type)=>{if(message.Contains("Water purity does not match"))waterErrors++;};Application.logMessageReceived+=log;
   try{
    if(host){
     var source=ClockBottle("N46_SOURCE",300000);var target=ClockBottle("N46_TARGET",0);
     PanelFixture("aquascan","N46_SCAN");PanelFixture("water_test_strip","N46_STRIP");root.World.Index();root.SyncStateSoon();
     var wait=Wait(()=>Received("N46_EMPTY_SCANNED"),"guest used native scanner on empty container",90);while(wait.MoveNext())yield return wait.Current;
     NativeClick(0,()=>source.Target(target));
     wait=Wait(()=>WaterHelper.GetTotalVolume(source)==0&&WaterHelper.GetTotalVolume(target)==300000,"native pour empties source and fills target on host",60);while(wait.MoveNext())yield return wait.Current;root.SyncStateSoon();
     wait=Wait(()=>Received("N46_DONE"),"guest completed native strip, scanner and resync flows",90);while(wait.MoveNext())yield return wait.Current;
    }else{
     var wait=Wait(()=>Find(root,"N46_SOURCE")!=null&&Find(root,"N46_TARGET")!=null&&Find(root,"N46_SCAN")!=null&&Find(root,"N46_STRIP")!=null,"native water tools and containers replicated",90);while(wait.MoveNext())yield return wait.Current;
     var source=Find(root,"N46_SOURCE");var target=Find(root,"N46_TARGET");var scanner=Find(root,"N46_SCAN");var strip=Find(root,"N46_STRIP");
     NativeClick(0,()=>scanner.Target(target));
     wait=Wait(()=>WaterScannerUIManager.Instance.panel.activeSelf&&WaterScannerUIManager.Instance.currentWaterContainer==target,"native empty-container scan opened on guest",60);while(wait.MoveNext())yield return wait.Current;
     Require(WaterHelper.GetTotalVolume(target)==0,"empty scan preserves zero liquid volume");WaterScannerUIManager.Instance.Close();root.ReleaseAll();Signal("N46_EMPTY_SCANNED");
     wait=Wait(()=>WaterHelper.GetTotalVolume(target)==300000&&WaterHelper.GetTotalVolume(source)==0,"host pour replicated with source empty and target full",60);while(wait.MoveNext())yield return wait.Current;
     NativeClick(0,()=>strip.Target(target));
     wait=Wait(()=>strip.IsTag("TEST_WATER_QUALITY")&&WaterStripUIManager.Instance.panel.activeSelf,"native test-strip action and result panel completed",60);while(wait.MoveNext())yield return wait.Current;
     Require(strip.GetTagReadonly("TEST_WATER_QUALITY").GetInt()==10000,"strip reports native pure-water result");root.ReleaseAll();
     NativeClick(0,()=>scanner.Target(target));
     wait=Wait(()=>WaterScannerUIManager.Instance.panel.activeSelf&&WaterScannerUIManager.Instance.currentWaterContainer==target,"native full-container scan opened on guest",60);while(wait.MoveNext())yield return wait.Current;root.ReleaseAll();
     root.ProbeResync1339();wait=Wait(()=>!root.ProbeAwaitingFull,"full resync completed with water scanner open",60);while(wait.MoveNext())yield return wait.Current;
     Require(WaterHelper.GetTotalVolume(target)==300000&&WaterHelper.GetWaterPurity(target)==10000&&WaterHelper.GetTotalVolume(source)==0,"open scanner and resync preserve authoritative water quantities");Signal("N46_DONE");
    }
    Require(waterErrors==0,"normal creation, pour, scan, strip and resync paths produced no zero-purity classification error");
    Debug.Log("[SMC NOTE] natural GetWaterIndex calls="+naturalWaterCalls1346+"; the classifier was never invoked directly by this probe");
   }finally{Application.logMessageReceived-=log;observer.UnpatchAll("shop-together.natural-water1346-probe");}
  }
  [MethodImpl(MethodImplOptions.NoInlining)]
  static int TraceWater1346(GameItem item){return WaterFeatureHelper.GetWaterIndex(item);}
  static IEnumerator CloseWater1346(Connection root,bool host){
   root.ProbeCloseReason1346();
   if(host){
    var bottle=ClockBottle("CLOSE46_WATER",0);root.World.Index();root.SyncStateSoon();
    var wait=Wait(()=>Received("CLOSE46_ZERO"),"guest checked deliberately invoked zero-water classifier",90);while(wait.MoveNext())yield return wait.Current;
    WaterHelper.AddLiquid(bottle,"water",300000);root.SyncStateSoon();
    wait=Wait(()=>Received("CLOSE46_PURE"),"guest retains host water quantities after diagnostics",60);while(wait.MoveNext())yield return wait.Current;
   }else{
    var wait=Wait(()=>Find(root,"CLOSE46_WATER")!=null,"empty host bottle replicated",90);while(wait.MoveNext())yield return wait.Current;
    var bottle=Find(root,"CLOSE46_WATER");int amount=WaterHelper.GetTotalVolume(bottle);
    Debug.Log("[SMC NOTE] Deliberately invoking native zero-purity classifier to verify evidence capture; this is not the player's unknown trigger.");
    Require(amount==0&&TraceWater1346(bottle)==-1,"native empty-container classification result is preserved");
    string report=IssueText();Require(report.Contains("TraceWater1346")&&report.Contains("water=0")&&report.Contains("total=0")&&report.Contains("small_bottled_water"),"classification report retains actual caller, item and quantities");
    Require(WaterHelper.GetTotalVolume(bottle)==amount,"diagnostics did not modify empty bottle contents");Signal("CLOSE46_ZERO");
    wait=Wait(()=>WaterHelper.GetTotalVolume(bottle)==300000&&WaterHelper.GetWaterPurity(bottle)==10000,"host fill amount and purity replicate normally",60);while(wait.MoveNext())yield return wait.Current;
    int issues=IssueReport.Count;Require(TraceWater1346(bottle)==0&&IssueReport.Count==issues&&WaterHelper.GetTotalVolume(bottle)==300000,"valid water classification is unchanged and adds no failure report");Signal("CLOSE46_PURE");
   }
  }
 }
}
#endif
