#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static int perfValidations1340;
  static void CountValidation1340(){perfValidations1340++;}
  static IEnumerator PerfHost1340(Connection root){
   var store=PlayerStore.Instance;var e=EmporiumEntry.Instance;
   EmporiumOption.Instance.autoCallNext=false;EmporiumOption.Instance.autoNextDialog=false;EmporiumOption.Instance.enableTypewriterEffect=false;
   StoreShutterButton.Instance.OpenShutter();store.lockNextBellFlag=0;
   if(store.storeClientManager.clientStack.Count==0)store.storeClientManager.GenerateClient();store.TryCallNextClient();
   var wait=Wait(()=>store.currentClientInstance!=null&&store.isClientArrived&&!StoreClientMono.Instance.IsMoving(),"native customer arrived",60);while(wait.MoveNext())yield return wait.Current;
   DialogUIManager.Instance.FlushScheduledEndActions();TradeClear1327();var customer=CurrentCustomer();TradeCustomer1327(customer,StoreClient.ClientIntent.BUY);
   customer.isIntroduced=false;customer.useClientBudget=true;customer.OverrideBudget(100000);customer.clientBuyingTagList.Clear();customer.clientBuyingIdList.Clear();customer.willAcceptItem=x=>x.name=="PERF1340_SELL";
   var goods=TradeFixture1327("screwdriver","PERF1340_SELL",true);store.playerCash=10000;int delivered=0;
   var line=new Dialogue().SetText("CLIENT","PERF1340_WAIT");var last=line.NextDialogue().SetText("CLIENT","PERF1340_DELIVERY").SetEndAction(()=>{delivered++;customer.OnIntroduced();});last.endActionDelay=8;
   Require(DialogUIManager.Instance.StartDialogue(line,true),"host started a real manual-advance dialogue chain");root.World.Index();root.SyncStateSoon();
   wait=Wait(()=>Received("P140_VISIBLE"),"guest kept the unfinished line visible",45);while(wait.MoveNext())yield return wait.Current;
   Require(DialogUIManager.Instance.currentDialog==line&&delivered==0,"host did not advance or execute delivery while guest waited");Signal("P140_ADVANCE");
   wait=Wait(()=>Received("P140_BLOCKED"),"guest received pending-delivery sale refusal",45);while(wait.MoveNext())yield return wait.Current;
   Require(store.playerCash==10000&&goods.parentInventory==e.backInvinvElement,"refused sale did not change cash or ownership");
   wait=Wait(()=>delivered==1,"native delayed dialogue delivery executed once",30);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>store.playerCash>10000&&goods.parentInventory!=e.backInvinvElement,"guest native tablet sale executed on host",60);while(wait.MoveNext())yield return wait.Current;
   Signal("P140_CASH="+store.playerCash);wait=Wait(()=>Received("P140_SOLD"),"guest received sale proceeds",30);while(wait.MoveNext())yield return wait.Current;
   e.invElement.SetShape(80,80);var box=MachineFixture("water_test_container","P140_BOX");var strip=ModHelper.SpawnItem("water_test_strip");strip.name="P140_STRIP";GeneralHelper.SetItemOwned(strip,true);Require(GraphUtils.TryAcceptAll(box,strip)==0,"native container contains strip");
   for(int i=0;i<32;i++){var machine=ModHelper.SpawnItem("livestock_feed_dispenser");machine.name="P140_MACHINE_"+i;GeneralHelper.SetItemOwned(machine,true);e.hiddenElement.UncheckedAccept(machine);}
   for(int i=0;i<160;i++){var item=ModHelper.SpawnItem("magnifier");item.name="P140_ITEM_"+i;GeneralHelper.SetItemOwned(item,true);e.hiddenElement.UncheckedAccept(item);}
   root.World.Index();root.SyncStateSoon();wait=Wait(()=>Received("P140_FIXTURES"),"guest received loaded-store fixtures",90);while(wait.MoveNext())yield return wait.Current;
   var grid=strip.parentInventory as GameGridInventory;Require(grid!=null,"strip has native grid");int width=grid.inventoryShape.width+1;grid.SetShape(width,grid.inventoryShape.height);root.SyncStateSoon();Signal("P140_WIDTH="+width);
   wait=Wait(()=>Received("P140_RESIZED"),"grid-only dimension change reached guest",45);while(wait.MoveNext())yield return wait.Current;
   Require(box.parentInventory.Expel(box)&&GraphUtils.TryAcceptAll(e.backInvinvElement,box)==0,"filled container moved across roots");root.SyncStateSoon();
   wait=Wait(()=>Received("P140_RESYNCED"),"full resync preserves container and dimensions",60);while(wait.MoveNext())yield return wait.Current;
   var check=new WorldReplica();var leases=new LeaseBook(check.Related);var full=check.Capture(true,0,leases,Time.realtimeSinceStartup);var delta=check.Capture(false,0,leases,Time.realtimeSinceStartup);
   Require(full.grids.Length>0&&delta.grids.Length==0,"unchanged grid dimensions omitted from incremental capture");
   Debug.Log("[SMC PERF] grid metadata fullBytes="+System.Text.Encoding.UTF8.GetByteCount(WireJson.ToJson(full.grids))+" idleIncrementalBytes="+System.Text.Encoding.UTF8.GetByteCount(WireJson.ToJson(delta.grids))+" grids="+full.grids.Length);
   ReadoutBenchmark1340(root);Signal("P140_BENCH");wait=Wait(()=>Received("P140_BENCH_DONE"),"guest completed open-panel benchmark",60);while(wait.MoveNext())yield return wait.Current;
   var perf=Performance1339(root,true);while(perf.MoveNext())yield return perf.Current;
   Require(root.IsHost&&root.Ready&&root.HasFriends&&delivered==1,"host connection and exactly-once delivery survived full scenario");
  }
  static IEnumerator PerfClient1340(Connection root){
   EmporiumOption.Instance.enableTypewriterEffect=false;
   var wait=Wait(()=>DialogUIManager.Instance.currentDialog!=null&&DialogUIManager.Instance.currentDialog.plainText=="PERF1340_WAIT"&&Find(root,"PERF1340_SELL")!=null,"guest sees host dialogue and sale item",60);while(wait.MoveNext())yield return wait.Current;
   yield return new WaitForSecondsRealtime(5);
   Require(DialogUIManager.Instance.currentDialog!=null&&DialogUIManager.Instance.currentDialog.plainText=="PERF1340_WAIT"&&DialogUIManager.Instance.clientBubble.gameObject.activeInHierarchy,"guest does not auto-end the host's unfinished line");Signal("P140_VISIBLE");
   wait=Wait(()=>Received("P140_ADVANCE"),"host confirms still waiting",30);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>DialogUIManager.Instance.OnAdvancePressed());
   wait=Wait(()=>DialogUIManager.Instance.currentDialog!=null&&DialogUIManager.Instance.currentDialog.plainText=="PERF1340_DELIVERY","guest advance reached host and next line returned",30);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.LastNote="";var tap=TradeTap1327(null,"sale during real pending dialogue delivery");while(tap.MoveNext())yield return tap.Current;
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC353~"),"pending-delivery rejection returned to initiating guest",20);while(wait.MoveNext())yield return wait.Current;Signal("P140_BLOCKED");
   wait=Wait(()=>DialogUIManager.Instance.currentDialog==null&&CurrentCustomer().isIntroduced,"host completed delivery and cleared guest dialogue",35);while(wait.MoveNext())yield return wait.Current;
   tap=TradeTap1327(true,"sale after host dialogue delivery");while(tap.MoveNext())yield return tap.Current;
   string cash=null;wait=Wait(()=>(cash=cash??ReceivePrefix("P140_CASH="))!=null&&PlayerStore.Instance.playerCash==int.Parse(cash),"guest sees authoritative sale proceeds",45);while(wait.MoveNext())yield return wait.Current;Signal("P140_SOLD");
   wait=Wait(()=>Find(root,"P140_MACHINE_31")!=null&&Find(root,"P140_ITEM_159")!=null&&Find(root,"P140_STRIP")!=null,"loaded-store fixtures arrived",90);while(wait.MoveNext())yield return wait.Current;Signal("P140_FIXTURES");
   string width=null;wait=Wait(()=>(width=width??ReceivePrefix("P140_WIDTH="))!=null&&((GameGridInventory)Find(root,"P140_STRIP").parentInventory).inventoryShape.width==int.Parse(width),"guest receives grid resize without item change",45);while(wait.MoveNext())yield return wait.Current;Signal("P140_RESIZED");
   wait=Wait(()=>Find(root,"P140_BOX").parentInventory==EmporiumEntry.Instance.backInvinvElement,"container move reaches guest",45);while(wait.MoveNext())yield return wait.Current;
   long version=root.World.Version;root.ProbeResync1339();yield return new WaitForSecondsRealtime(1);
   wait=Wait(()=>root.World.Version>version&&!root.ProbeAwaitingFull,"guest applies requested full resync",60);while(wait.MoveNext())yield return wait.Current;
   Require(Find(root,"P140_STRIP").parentInventory.GetParentItem()==Find(root,"P140_BOX")&&((GameGridInventory)Find(root,"P140_STRIP").parentInventory).inventoryShape.width==int.Parse(width),"full resync preserves child identity and resized grid");Signal("P140_RESYNCED");
   wait=Wait(()=>Received("P140_BENCH"),"host readout benchmark complete",60);while(wait.MoveNext())yield return wait.Current;
   var machine=Find(root,"P140_MACHINE_0");machine.contentWindow.Show();machine.contentWindow.Validate();yield return null;yield return null;
   PanelBenchmark1340(root,machine);machine.contentWindow.Hide();Signal("P140_BENCH_DONE");
   var perf=Performance1339(root,false);while(perf.MoveNext())yield return perf.Current;
   Require(root.IsClient&&root.Ready&&root.ProbeApplyFailures==0,"guest stayed connected with no world-apply failures");
  }
  static void ReadoutBenchmark1340(Connection root){
   var reader=new HostReadouts();var samples=reader.Capture(root.World,true);var copies=WireJson.FromJson<MachineReadout[]>(WireJson.ToJson(samples));
   var same=AccessTools.Method(typeof(HostReadouts),"Same");var args=new object[2];var baseline=new string[samples.Length];
   for(int i=0;i<samples.Length;i++){baseline[i]=WireJson.ToJson(samples[i]);args[0]=samples[i];args[1]=copies[i];Require((bool)same.Invoke(null,args),"typed readout equality preserves serialized snapshot item "+samples[i].item);}
   // Same workload, alternating order. This compares the replaced comparison
   // stage, not whole-game FPS; reflection overhead remains on the new path.
   var oldTimes=new List<double>();var newTimes=new List<double>();var timer=new System.Diagnostics.Stopwatch();
   for(int round=0;round<10;round++)for(int side=0;side<2;side++){
    bool old=(round+side)%2==0;timer.Restart();
    for(int repeat=0;repeat<10;repeat++)for(int i=0;i<samples.Length;i++)if(old){if(WireJson.ToJson(copies[i])!=baseline[i])throw new Exception("Readout baseline differs");}else{args[0]=samples[i];args[1]=copies[i];if(!(bool)same.Invoke(null,args))throw new Exception("Readout equality differs");}
    timer.Stop();if(round>0)(old?oldTimes:newTimes).Add(timer.Elapsed.TotalMilliseconds);
   }
   Debug.Log("[SMC PERF] readout-comparison machines="+samples.Length+" sweepsPerSample=10 oldMedianMs="+Percentile(oldTimes,.5).ToString("F3")+" newMedianMs="+Percentile(newTimes,.5).ToString("F3"));
  }
  static void PanelBenchmark1340(Connection root,GameItem machine){
   root.World.Readouts.ApplyOpen(root.World);var widgets=(List<PixelElement>)AccessTools.Method(typeof(HostReadouts),"Widgets").Invoke(root.World.Readouts,new object[]{machine});
   var sliders=widgets.FindAll(x=>x is MachinerySlider);Require(sliders.Count>0,"open machine has a native slider");
   var h=new Harmony("shop_together.perf1340-count");h.Patch(AccessTools.Method(typeof(MachinerySlider),"Validate"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"CountValidation1340"));
   try{
    perfValidations1340=0;for(int i=0;i<100;i++)foreach(MachinerySlider slider in sliders){slider.SetRange(slider.min,slider.max,slider.step);slider.SetValue(slider.value,false);}int before=perfValidations1340;
    perfValidations1340=0;for(int i=0;i<100;i++)root.World.Readouts.ApplyOpen(root.World);int after=perfValidations1340;
    Require(before>0&&after==0,"unchanged open sliders avoid redundant native layout validation");
    Debug.Log("[SMC PERF] unchanged-panel refreshes=100 oldSliderValidations="+before+" newSliderValidations="+after);
   }finally{h.UnpatchAll("shop_together.perf1340-count");}
  }
 }
}
#endif
