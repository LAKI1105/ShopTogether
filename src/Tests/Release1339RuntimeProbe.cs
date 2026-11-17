#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 public sealed partial class Connection {
  internal void ProbeResync1339(){awaitingFull=true;SendToHost(24,new byte[0]);}
  internal void ProbeBufferedMessages1339(){
   var peer=new Peer{channel=new ProbeBackpressureChannel{Accept=true},authenticated=true,matched=true,modsOk=true,id=991};
   try{
    for(int i=0;i<20;i++)peer.input.AddRange(new byte[]{0,0,0,2,41,(byte)(i%2)});
    peer.input.AddRange(new byte[]{0,0,0});
    for(int i=0;i<64&&peer.input.Count>3;i++)Pump(peer,true);
    if(peer.input.Count!=3||peer.activity!=1)throw new InvalidOperationException("Buffered activity messages lost order or partial header");
    peer.input.AddRange(new byte[]{2,41,0});Pump(peer,true);
    if(peer.input.Count!=0||peer.activity!=0)throw new InvalidOperationException("Split activity message did not resume");
    Debug.Log("[SMC PASS] buffered messages preserve order and incomplete header after batch compaction");
   }finally{peer.Dispose();}
  }
  internal System.Net.Sockets.Socket ProbeBrokenArrival1339(){
   var socket=new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,System.Net.Sockets.SocketType.Stream,System.Net.Sockets.ProtocolType.Tcp);
   try{socket.Connect(listener.LocalEndPoint);socket.Send(new byte[]{0,0,0,0,0});return socket;}catch{socket.Dispose();throw;}
  }
  internal void ProbeOldRefusal1339(int id){
   long token;if(!LocalLeases.TryGetValue(id,out token))throw new InvalidOperationException("Expected real drag lease before old-reply check");
   long newer=sequence;itemRequests[id]=newer;
   try{OnLocalReply(new LeaseReply{sequence=newer-1,items=new[]{id},granted=false,reason="~SMC312~"});
    if(!LocalLeases.ContainsKey(id)||LocalLeases[id]!=token||!ItemMouseDragHandler.current.IsDraggingItem)throw new InvalidOperationException("Old refusal revoked current drag");
   }finally{itemRequests.Remove(id);}
   Debug.Log("[SMC PASS] delayed old refusal preserves a newer real drag lease");
  }
 }
 internal static partial class RuntimeProbe {
  static IEnumerator ReleaseHost1339(Connection root){
   var store=PlayerStore.Instance;var e=EmporiumEntry.Instance;
   root.ProbeBufferedMessages1339();
   using(var broken=root.ProbeBrokenArrival1339()){yield return new WaitForSecondsRealtime(1);}
   yield return new WaitForSecondsRealtime(1);Require(root.IsHost&&root.Ready,"failed extra TLS arrival leaves running host intact");
   EmporiumOption.Instance.autoCallNext=false;StoreShutterButton.Instance.OpenShutter();store.lockNextBellFlag=0;
   if(store.storeClientManager.clientStack.Count==0)store.storeClientManager.GenerateClient();store.TryCallNextClient();
   var wait=Wait(()=>store.currentClientInstance!=null&&store.isClientArrived&&!StoreClientMono.Instance.IsMoving(),"native customer finished arriving",60);while(wait.MoveNext())yield return wait.Current;
   DialogUIManager.Instance.FlushScheduledEndActions();DialogUIManager.Instance.InterruptDialog();
   var customer=store.currentClientInstance.GetClientBlueprint();TradeCustomer1327(customer,StoreClient.ClientIntent.SELL);customer.isIntroduced=false;TradeClear1327();
   var item=TradeFixture1327("magnifier","RELEASE1339_BUY",false);store.playerCash=100000;root.World.Index();root.SyncStateSoon();
   wait=Wait(()=>GeneralHelper.IsItemOwned(item)&&store.playerCash<100000,"guest buys from ordinary SELL customer with introduced=false",60);while(wait.MoveNext())yield return wait.Current;
   Signal("RELEASE1339_BOUGHT");wait=Wait(()=>Received("RELEASE1339_BOUGHT_SEEN"),"purchase cash and ownership reached guest",60);while(wait.MoveNext())yield return wait.Current;
   DialogUIManager.Instance.FlushScheduledEndActions();TradeClear1327();TradeCustomer1327(customer,StoreClient.ClientIntent.BUY);DialogUIManager.Instance.FlushScheduledEndActions();store.currentClientInstance=StoreClientInstance.CreateClientInstance(customer);customer.isIntroduced=false;customer.useClientBudget=true;customer.OverrideBudget(100000);customer.clientBuyingTagList.Clear();customer.clientBuyingIdList.Clear();customer.willAcceptItem=goods=>goods.name=="RELEASE1339_SELL";Require(!DialogueIntent.Committing,"previous trade dialogue finished before buyer fixture");
   item=TradeFixture1327("screwdriver","RELEASE1339_SELL",true);int cash=store.playerCash;root.World.Index();root.SyncStateSoon();
   wait=Wait(()=>store.playerCash>cash&&item.parentInventory!=e.backInvinvElement,"guest sells to ordinary BUY customer with introduced=false",60);while(wait.MoveNext())yield return wait.Current;
   Signal("RELEASE1339_SOLD="+store.playerCash);wait=Wait(()=>Received("RELEASE1339_SOLD_SEEN"),"sale cash reached guest",60);while(wait.MoveNext())yield return wait.Current;
   TradeClear1327();e.invElement.SetShape(80,80);
   var box=TradeFixture1327("water_test_container","RELEASE1339_BOX",true);var strip=ModHelper.SpawnItem("water_test_strip");strip.name="RELEASE1339_STRIP";GeneralHelper.SetItemOwned(strip,true);
   Require(GraphUtils.TryAcceptAll(box,strip)==0,"water test strip is inside its native box");root.World.Index();root.SyncStateSoon();
   wait=Wait(()=>Received("RELEASE1339_BOX_SEEN"),"guest sees box and contained strip before move",60);while(wait.MoveNext())yield return wait.Current;
   Require(box.parentInventory.Expel(box)&&GraphUtils.TryAcceptAll(e.invElement,box)==0,"host moved filled box from counter to storage");root.World.Index();root.SyncStateSoon();
   wait=Wait(()=>Received("RELEASE1339_MOVED"),"guest applies cross-root container move without missing inventory",60);while(wait.MoveNext())yield return wait.Current;
   Require(box.parentInventory.Expel(box)&&GraphUtils.TryAcceptAll(e.backInvinvElement,box)==0,"host moved filled box back to counter");root.World.Index();root.SyncStateSoon();
   wait=Wait(()=>Received("RELEASE1339_RETURNED"),"guest preserves child identity after return move",60);while(wait.MoveNext())yield return wait.Current;
   CheckFullCapture1339(box);
   var reports=ReportedPathsHost1339(root);while(reports.MoveNext())yield return reports.Current;
   var perf=Performance1339(root,true);while(perf.MoveNext())yield return perf.Current;
  }
  static IEnumerator ReleaseClient1339(Connection root){
   var wait=Wait(()=>Find(root,"RELEASE1339_BUY")!=null&&CurrentCustomer()!=null&&CurrentCustomer().clientIntent==StoreClient.ClientIntent.SELL&&PlayerStore.Instance.playerCash==100000,"ordinary seller and cash synchronized",60);while(wait.MoveNext())yield return wait.Current;
   long beforeResync=root.World.Version;root.ProbeResync1339();yield return new WaitForSecondsRealtime(1);
   wait=Wait(()=>root.World.Version>beforeResync&&!root.ProbeAwaitingFull,"customer remains available after requested full resync",60);while(wait.MoveNext())yield return wait.Current;
   Require(!CurrentCustomer().isIntroduced,"seller retains native introduced=false");EmporiumEntry.Instance.backInvWindow.Show();EmporiumEntry.Instance.backInvWindow.ToFront(true);yield return null;var merchandise=Find(root,"RELEASE1339_BUY") as GameItemElement;Require(merchandise!=null&&merchandise.handler.gameObject.activeInHierarchy&&merchandise.background.image.enabled,"customer merchandise is rendered on guest counter after resync");RemoteActions.Forward(RemoteActions.Adapter("CounterAction",2),null,new object[]{1,null});
   bool bought=false;wait=Wait(()=>(bought=bought||Received("RELEASE1339_BOUGHT"))&&GeneralHelper.IsItemOwned(Find(root,"RELEASE1339_BUY"))&&PlayerStore.Instance.playerCash<100000,"guest sees paid purchase",60);while(wait.MoveNext())yield return wait.Current;Signal("RELEASE1339_BOUGHT_SEEN");
   wait=Wait(()=>Find(root,"RELEASE1339_SELL")!=null&&CurrentCustomer()!=null&&CurrentCustomer().clientIntent==StoreClient.ClientIntent.BUY,"ordinary buyer synchronized",60);while(wait.MoveNext())yield return wait.Current;
   Require(!CurrentCustomer().isIntroduced,"buyer retains native introduced=false");Require(CurrentCustomer().IsClientBuyingThisItem(Find(root,"RELEASE1339_SELL")),"guest uses host-only buying preference");Require((string)AccessTools.Method(typeof(NegociationUIManager),"GetRefusalLabel").Invoke(null,new object[]{CurrentCustomer(),Find(root,"RELEASE1339_SELL")})=="","accepted item does not display not-interested refusal");RemoteActions.Forward(RemoteActions.Adapter("CounterAction",2),null,new object[]{2,null});
   string sold=null;wait=Wait(()=>(sold=sold??ReceivePrefix("RELEASE1339_SOLD="))!=null&&PlayerStore.Instance.playerCash==int.Parse(sold),"guest sees sale proceeds",60);while(wait.MoveNext())yield return wait.Current;Signal("RELEASE1339_SOLD_SEEN");
   wait=Wait(()=>Find(root,"RELEASE1339_BOX")!=null&&Find(root,"RELEASE1339_STRIP")!=null,"native strip box synchronized",60);while(wait.MoveNext())yield return wait.Current;
   var box=Find(root,"RELEASE1339_BOX");var strip=Find(root,"RELEASE1339_STRIP");Require(strip.parentInventory!=null&&strip.parentInventory.GetParentItem()==box,"strip attached to its own box");Signal("RELEASE1339_BOX_SEEN");
   wait=Wait(()=>box.parentInventory==EmporiumEntry.Instance.invElement&&strip.parentInventory!=null&&strip.parentInventory.GetParentItem()==box,"filled box moved to storage on guest",60);while(wait.MoveNext())yield return wait.Current;Signal("RELEASE1339_MOVED");
   wait=Wait(()=>box.parentInventory==EmporiumEntry.Instance.backInvinvElement&&strip.parentInventory!=null&&strip.parentInventory.GetParentItem()==box,"filled box returned to counter on guest",60);while(wait.MoveNext())yield return wait.Current;
   Require(root.Ready&&root.IsClient,"guest stays connected after purchases and container moves");Signal("RELEASE1339_RETURNED");
   var reports=ReportedPathsClient1339(root);while(reports.MoveNext())yield return reports.Current;
   var perf=Performance1339(root,false);while(perf.MoveNext())yield return perf.Current;
  }
  static IEnumerator ReportedPathsHost1339(Connection root){
   var e=EmporiumEntry.Instance;var store=PlayerStore.Instance;DialogUIManager.Instance.FlushScheduledEndActions();DialogUIManager.Instance.InterruptDialog();store.DismissCurrentClient(false);
   var printer=MachineFixture("bottle_printer","R139_PRINTER");GeneralHelper.SetItemOwned(printer,true);
   var battery=PreBuiltItemHelper.BatteryDisposable(1000);GeneralHelper.SetItemOwned(battery,true);Require(GraphUtils.TryAcceptAll(printer,battery)==0,"reported printer has native power source");
   var tank=MachineFixture("water_tank","R139_TANK");var bottle=MachineFixture("small_bottled_water","R139_BOTTLE");GeneralHelper.SetItemOwned(tank,true);GeneralHelper.SetItemOwned(bottle,true);WaterHelper.FillWithPureWater(tank);
   var pack=MachineFixture("backpack_large","R139_PACK");GeneralHelper.SetItemOwned(pack,true);var contents=ModHelper.SpawnItem("screwdriver");contents.name="R139_PACK_ITEM";GeneralHelper.SetItemOwned(contents,true);Require(GraphUtils.TryAcceptAll(pack,contents)==0,"selected backpack has native contents");
   int waterBefore=WaterHelper.GetTotalVolume(tank)+WaterHelper.GetTotalVolume(bottle);var drag=TradeFixture1327("magnifier","R139_DRAG",true);TradeFixture1327("screwdriver","R139_DRAG2",true);root.World.Index();root.SyncStateSoon();
   var wait=Wait(()=>ChildCount(printer,"small_bottled_water")==2,"two consecutive guest printer selections print on host with host window closed",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>WaterHelper.GetTotalVolume(bottle)>0,"guest pours tank into bottle on host",60);while(wait.MoveNext())yield return wait.Current;
   Require(WaterHelper.GetTotalVolume(tank)+WaterHelper.GetTotalVolume(bottle)==waterBefore,"native water transfer conserves liquid volume");
   wait=Wait(()=>Received("R139_DRAG_HELD"),"guest started native drag",60);while(wait.MoveNext())yield return wait.Current;drag.unitValue++;drag.Validate();root.SyncStateSoon();Signal("R139_DRAG_CHANGE");
   wait=Wait(()=>Received("R139_GROUP_HELD"),"guest started native group drag",60);while(wait.MoveNext())yield return wait.Current;drag.unitValue++;drag.Validate();root.SyncStateSoon();Signal("R139_GROUP_CHANGE");
   wait=Wait(()=>Received("R139_ITEMS_DONE"),"guest received printing, water and drag results",60);while(wait.MoveNext())yield return wait.Current;
   store.devMode=false;store.storeState=PlayerStore.StoreState.OPEN;root.SyncStateSoon();Signal("R139_REFUSE_DEPART");
   wait=Wait(()=>Received("R139_PACK_RETAINED"),"refused departure keeps guest backpack choice",60);while(wait.MoveNext())yield return wait.Current;
   store.devMode=true;store.isAfterhoursIntroduced=true;store.storeState=PlayerStore.StoreState.CLOSED;store.visitLeftTonight=10;root.SyncStateSoon();Signal("R139_RETRY_DEPART");
   wait=Wait(()=>OutingKits.HostAt("dumping_ground")&&!MapUIManager.Instance.IsInTransit,"retried departure arrives at dumping ground",60);while(wait.MoveNext())yield return wait.Current;
   Require(e.afterhourPocketSlotInvBackpack.childItem==pack&&contents.parentInventory.GetParentItem()==pack,"host took selected real backpack with its contents");
   wait=Wait(()=>Received("R139_PACK_ARRIVED"),"guest sees selected backpack and contents outside",60);while(wait.MoveNext())yield return wait.Current;
   MapUIManager.Instance.ReturnNow();wait=Wait(()=>MapUIManager.Instance.Current==null&&!MapUIManager.Instance.IsInTransit,"host returned from backpack check",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("R139_BACK_HOME"),"guest returned from backpack check",60);while(wait.MoveNext())yield return wait.Current;
   var sleep=SettlementHost1339(root);while(sleep.MoveNext())yield return sleep.Current;
  }
  static IEnumerator SettlementHost1339(Connection root){
   var store=PlayerStore.Instance;IEnumerator wait;
   store.playerCash=100000;store.storeState=PlayerStore.StoreState.CLOSED;root.SyncStateSoon();EndOfDayUIManager.Instance.OpenUI();
   wait=Wait(()=>Received("R139_EVENING"),"guest automatically opens end-of-day settlement",60);while(wait.MoveNext())yield return wait.Current;
   int day=StoreStation.Instance.dayCounter;NativeClick(0,()=>EndOfDayUIManager.Instance.OnEndDayButtonClick());
   wait=Wait(()=>StoreStation.Instance.dayCounter>day&&AdvCalendarUIManager.Instance.nightlyReportSection.activeInHierarchy,"host completed native sleep and morning report",90);while(wait.MoveNext())yield return wait.Current;
   var phase=HostPhase.Capture(true);Require(phase.reportLogs!=null&&phase.morning>0&&store.nightLogs.Count==0,"host report retained after native log consumption");
   // The independent test-control channel accepts at most 1024 bytes. Compare the host text
   // by a bounded digest; the actual report still has to arrive through the production link.
   Debug.Log("[SMC NOTE] full report control payload bytes="+(12+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(WireJson.ToJson(phase))).Length));
   Signal("R139_REPORT="+phase.morning+"|"+phase.reportDay+"|"+ReportHash1339(phase.reportLogs));
   wait=Wait(()=>Received("R139_MORNING"),"guest displayed same morning report after sleep",60);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator ReportedPathsClient1339(Connection root){
   var wait=Wait(()=>Find(root,"R139_DRAG")!=null&&Find(root,"R139_PRINTER")!=null&&Find(root,"R139_TANK")!=null&&Find(root,"R139_BOTTLE")!=null&&Find(root,"R139_PACK_ITEM")!=null,"reported-path fixtures synchronized",60);while(wait.MoveNext())yield return wait.Current;
   var e=EmporiumEntry.Instance;var printer=Find(root,"R139_PRINTER");var tank=Find(root,"R139_TANK");var bottle=Find(root,"R139_BOTTLE");var pack=Find(root,"R139_PACK");var contents=Find(root,"R139_PACK_ITEM");var drag=Find(root,"R139_DRAG");
   for(int printed=1;printed<=2;printed++){
    printer.contentWindow.Show();printer.contentWindow.Validate();var button=MachineButtonPress.Buttons(printer.contentWindow)[0];NativeClick(0,()=>button.Select());
    Require(!button.isSelected,"printer selection resets after forwarded click");
    wait=Wait(()=>ChildCount(printer,"small_bottled_water")==printed,"consecutive printed bottle appears on guest",60);while(wait.MoveNext())yield return wait.Current;
   }printer.contentWindow.Hide();
   int tankId=root.World.Id(tank);long staleVersion=root.World.Versions[tankId]-1;
   root.Submit(new Request{kind=RequestKind.AutoPlace,operation=1,items=new[]{tankId},target=root.World.Id(bottle),moves=new[]{new Placement{item=tankId,count=1,version=staleVersion}}});
   wait=Wait(()=>WaterHelper.GetTotalVolume(bottle)>0,"drag-use request pours water despite a stale data version",60);while(wait.MoveNext())yield return wait.Current;
   StoreUIManager.Instance.CloseAllUI();ShowStorage();e.backInvWindow.Show();e.backInvWindow.ToFront(true);yield return null;long value=drag.unitValue;var gesture=NativeDragStart1327(drag);while(gesture.MoveNext())yield return gesture.Current;
   var picture=(GameItemElement)drag;picture.RotCW();var heldAngles=picture.fakeRotator.rectTransform.localEulerAngles;Signal("R139_DRAG_HELD");
   bool changed=false;wait=Wait(()=>(changed=changed||Received("R139_DRAG_CHANGE"))&&drag.unitValue!=value,"dragged item receives host data change",60);while(wait.MoveNext())yield return wait.Current;
   Require(ItemMouseDragHandler.current.IsDraggingItem&&picture.fakeBackground.image.enabled&&picture.fakeMiddle.parent==RenderHandler.current.overlayRoot,"ordinary update preserves visible drag picture on overlay");
   Require(Vector3.Distance(heldAngles,picture.fakeRotator.rectTransform.localEulerAngles)<.01f,"host data update preserves held rotation");
   wait=Wait(()=>root.LocalLeases.ContainsKey(root.World.Id(drag)),"host granted drag lease",30);while(wait.MoveNext())yield return wait.Current;root.ProbeOldRefusal1339(root.World.Id(drag));
   ItemMouseDragHandler.current.Cancel();
   var group=ItemMultiSelectHandler.current;var selected=ModReflect.Get<List<GameItemElement>>(group,"selectedItems");selected.Clear();selected.Add(picture);var second=(GameItemElement)Find(root,"R139_DRAG2");Require(second!=null,"second group item is synchronized");selected.Add(second);
   ModReflect.Get<Dictionary<GameItemElement,GameInventory>>(group,"selectedHomes")[picture]=picture.parentInventory;
   ModReflect.Get<Dictionary<GameItemElement,GameInventory>>(group,"selectedHomes")[second]=second.parentInventory;
   NativeClick(0,()=>AccessTools.Method(typeof(ItemMultiSelectHandler),"StartGroupDrag").Invoke(group,null));
   Require(group.IsGroupDragging,"native group handler started");value=drag.unitValue;Signal("R139_GROUP_HELD");
   changed=false;wait=Wait(()=>(changed=changed||Received("R139_GROUP_CHANGE"))&&drag.unitValue!=value,"group-held item receives host change",60);while(wait.MoveNext())yield return wait.Current;
   Require(group.IsGroupDragging&&picture.fakeBackground.image.enabled&&second.fakeBackground.image.enabled&&picture.fakeMiddle.parent==RenderHandler.current.overlayRoot,"group drag pictures survive host data update");
   root.ProbeResync1339();wait=Wait(()=>!root.ProbeAwaitingFull&&!group.IsGroupDragging,"full resync cancels group drag",60);while(wait.MoveNext())yield return wait.Current;
   gesture=NativeDragRelease1327();while(gesture.MoveNext())yield return gesture.Current;Signal("R139_ITEMS_DONE");
   wait=Wait(()=>Received("R139_REFUSE_DEPART"),"host will refuse departure while shop open",60);while(wait.MoveNext())yield return wait.Current;
   e.selectedBackpack=pack;RemoteActions.LastNote=null;NativeClick(0,()=>FriendOuting.Depart(AfterhourLocation.Get("dumping_ground")));
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC203~"),"departure refusal received",60);while(wait.MoveNext())yield return wait.Current;Require(e.selectedBackpack==pack,"backpack selection survives refused departure");Signal("R139_PACK_RETAINED");
   wait=Wait(()=>Received("R139_RETRY_DEPART"),"host permits departure retry",60);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>FriendOuting.Depart(AfterhourLocation.Get("dumping_ground")));
   wait=Wait(()=>OutingKits.Joined=="dumping_ground"&&e.afterhourPocketSlotInvBackpack.childItem==pack&&contents.parentInventory!=null&&contents.parentInventory.GetParentItem()==pack,"guest arrived with same backpack and contents",60);while(wait.MoveNext())yield return wait.Current;Signal("R139_PACK_ARRIVED");
   wait=Wait(()=>OutingKits.Joined==null,"guest follows host return",60);while(wait.MoveNext())yield return wait.Current;Signal("R139_BACK_HOME");
   var sleep=SettlementClient1339(root);while(sleep.MoveNext())yield return sleep.Current;
  }
  static IEnumerator SettlementClient1339(Connection root){
   IEnumerator wait;
   wait=Wait(()=>EndOfDayUIManager.Instance.IsOpen,"guest sees host evening settlement without opening it manually",60);while(wait.MoveNext())yield return wait.Current;Signal("R139_EVENING");
   string json=null;wait=Wait(()=>(json=json??ReceivePrefix("R139_REPORT="))!=null,"host completed morning report capture",90);while(wait.MoveNext())yield return wait.Current;
   var expected=json.Split('|');int morning=int.Parse(expected[0]),day=int.Parse(expected[1]);
   wait=Wait(()=>(int)AccessTools.Field(typeof(SharedScreens),"seenMorning").GetValue(null)==morning&&StoreStation.Instance.dayCounter==day&&AdvCalendarUIManager.Instance.nightlyReportSection.activeInHierarchy,"guest applied this day's morning report",60);while(wait.MoveNext())yield return wait.Current;
   var report=HostPhase.Last;Require(report!=null&&report.reportLogs!=null&&ReportHash1339(report.reportLogs)==expected[2],"received report text exactly matches independent host digest");
   yield return null;var text="";foreach(var line in StartOfDayUIManager.Instance.contentGroupObject.GetComponentsInChildren<TMPro.TextMeshProUGUI>())text+=line.text+"\n";
   foreach(var log in report.reportLogs)Require(text.Contains(log),"host nightly log appears on guest: expected="+log+" actual="+text);Require(!EndOfDayUIManager.Instance.IsOpen,"previous evening settlement closes after sleep");Signal("R139_MORNING");
  }
  static string ReportHash1339(string[] logs){using(var hash=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(WireJson.ToJson(logs))));}
  static IEnumerator SettlementPerformance1339(Connection root,bool host){
   var settlement=host?SettlementHost1339(root):SettlementClient1339(root);while(settlement.MoveNext())yield return settlement.Current;
   var perf=Performance1339(root,host);while(perf.MoveNext())yield return perf.Current;
  }
  static void CheckFullCapture1339(GameItem item){
   // Separate capture baseline: do not consume the live room's pending update.
   var world=new WorldReplica();var leases=new LeaseBook(world.Related);world.Capture(true,0,leases,Time.realtimeSinceStartup);
   long before=item.unitValue;int cash=PlayerStore.Instance.playerCash;
   try{
    item.unitValue++;PlayerStore.Instance.playerCash++;WorldDelta incremental;
    var full=world.Capture(true,0,leases,Time.realtimeSinceStartup,out incremental);int id=world.Id(item);
    Require(Array.Exists(full.items,r=>r.id==id&&r.node.unitValue==before+1)&&Array.Exists(incremental.items,r=>r.id==id&&r.node.unitValue==before+1),"full capture retains changed item in existing-peer delta");
    Require(full.full&&!incremental.full&&incremental.items.Length<full.items.Length&&incremental.store!=null,"full and incremental views share store changes without resending unchanged items");
   }finally{item.unitValue=before;PlayerStore.Instance.playerCash=cash;}
  }
  static IEnumerator Performance1339(Connection root,bool host){
   bool trace=Argument("--smc-suite")=="perf1340";if(trace)LatencyTrace.Begin();
   GameItem item=null;
   if(host){item=MachineFixture("magnifier","R139_PERF_TARGET");GeneralHelper.SetItemOwned(item,true);root.World.Index();root.SyncStateSoon();Signal("R139_PERF_START");}
   else{bool started=false;var wait=Wait(()=>(started=started||Received("R139_PERF_START"))&&(item=Find(root,"R139_PERF_TARGET"))!=null,"performance target and start signal synchronized",60);while(wait.MoveNext())yield return wait.Current;}
   CaptureMilliseconds.Clear();var frames=new List<double>();var latency=new List<double>();float until=Time.realtimeSinceStartup+12,next=0,sent=0;int count=0;string pending=null;
   while(Time.realtimeSinceStartup<until){
    frames.Add(Time.unscaledDeltaTime*1000);
    if(pending!=null&&GeneralHelper.GetCustomName(item)==pending){latency.Add((Time.realtimeSinceStartup-sent)*1000);pending=null;}
    if(!host&&pending==null&&Time.realtimeSinceStartup>=next){pending="R139_PERF_"+(++count);sent=Time.realtimeSinceStartup;next=sent+.5f;string name=pending;NativeClick(0,()=>GeneralHelper.SetCustomName(item,name));}
    yield return null;
   }
   if(pending!=null){var wait=Wait(()=>GeneralHelper.GetCustomName(item)==pending,"final performance action synchronized",60);while(wait.MoveNext())yield return wait.Current;latency.Add((Time.realtimeSinceStartup-sent)*1000);}
   Require(root.Ready&&(host||latency.Count>0),"performance sample kept connection and confirmed host actions");
   Debug.Log("[SMC PERF] release1339 role="+(host?"host":"client")+" items="+root.World.Items.Count+" frames="+frames.Count+" frameMedianMs="+Percentile(frames,.5).ToString("F2")+" frameP95Ms="+Percentile(frames,.95).ToString("F2")+" captureSamples="+CaptureMilliseconds.Count+" captureMedianMs="+Percentile(CaptureMilliseconds,.5).ToString("F2")+" captureP95Ms="+Percentile(CaptureMilliseconds,.95).ToString("F2")+" confirmed="+latency.Count+" responseMedianMs="+Percentile(latency,.5).ToString("F2")+" responseP95Ms="+Percentile(latency,.95).ToString("F2"));
   if(host){var wait=Wait(()=>Received("R139_PERF_DONE"),"client completed performance sample",60);while(wait.MoveNext())yield return wait.Current;}else Signal("R139_PERF_DONE");
   if(trace)LatencyTrace.End();
  }
 }
}
#endif
