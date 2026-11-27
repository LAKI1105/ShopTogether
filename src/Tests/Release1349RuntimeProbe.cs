#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Steamworks;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static int arrangeCommits1349, accepted1349;
  static void CountArrange1349(){arrangeCommits1349++;}
  static bool AcceptKnown1349(CSteamID __0,ref bool __result){if(__0.m_SteamID!=peerSteam1347)return true;accepted1349++;__result=true;return false;}
  static void Transport1349(){
   var channel=new SteamRelay.SteamChannel(peerSteam1347);var field=AccessTools.Field(typeof(SteamRelay.SteamChannel),"lastArrival");
   var stream=(MessageStream)AccessTools.Field(typeof(SteamRelay.SteamChannel),"stream").GetValue(channel);
   var channels=(Dictionary<ulong,SteamRelay.SteamChannel>)AccessTools.Field(typeof(SteamRelay),"open").GetValue(null);
   var expected=AccessTools.Field(typeof(SteamRelay),"expectedHost");ulong old=(ulong)expected.GetValue(null);
   var patch=new Harmony("shop-together.transport1349-probe");
   try{
    channel.Arrive(new byte[]{17,29});field.SetValue(channel,Time.realtimeSinceStartup-61);
    var buffer=new byte[8];Require(channel.Receive(buffer)==2&&buffer[0]==17&&buffer[1]==29,"buffered reliable bytes are delivered after a long application pause");
    Require(channel.Receive(buffer)==-1,"drained backlog waits for next poll instead of falsely closing the session");
    field.SetValue(channel,Time.realtimeSinceStartup-61);Require(channel.Receive(buffer)==0,"actual 60-second silence still closes the channel");
    channels.Add(peerSteam1347,channel);expected.SetValue(null,0UL);accepted1349=0;
    patch.Patch(AccessTools.Method(typeof(SteamNetworking),"AcceptP2PSessionWithUser"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"AcceptKnown1349"));
    AccessTools.Method(typeof(SteamRelay),"OnRequest").Invoke(null,new object[]{new P2PSessionRequest_t{m_steamIDRemote=new CSteamID(peerSteam1347)}});
    Require(accepted1349==1&&channels[peerSteam1347]==channel,"known Steam peer route request is accepted without replacing protocol state");
    AccessTools.Method(typeof(SteamRelay),"OnFail").Invoke(null,new object[]{new P2PSessionConnectFail_t{m_steamIDRemote=new CSteamID(peerSteam1347),m_eP2PSessionError=4}});
    bool failed=false;try{bool value=channel.Writing;}catch(InvalidOperationException){failed=true;}
    Require(failed,"Steam error 4 remains a real failure; potentially dropped reliable bytes are never ignored");
   }finally{patch.UnpatchAll("shop-together.transport1349-probe");channels.Remove(peerSteam1347);expected.SetValue(null,old);stream.Close();}
   string source="game Error: [Smart Inventory Arrange]: SIA_SORT_ERROR BEGIN "+new string('x',300)+" PRIMARY EXCEPTION (complete): System.InvalidOperationException: native slot rejected commit";
   string compact=IssueReport.Compact(IssueReport.Sanitize(source));
   Require(compact.Length<=300&&compact.Contains("Smart Inventory Arrange")&&compact.Contains("native slot rejected commit"),"compact report retains the exception cause after a long third-party header");
  }
  static string Layout1349(GameInventory grid){var names=new List<string>();foreach(var item in grid.childItems)if(item.name.StartsWith("S49_"))names.Add(item.name+":"+item.modifiedShape.minX+","+item.modifiedShape.minY+","+item.modifiedShape.orientation);names.Sort(StringComparer.Ordinal);return string.Join(";",names.ToArray());}
  static IEnumerator Release1349(Connection root,bool host){
   Transport1349();PollCapacity1347();
   SmartArrangeIntent.Bind();Require(SmartArrangeIntent.Bound,"official Smart Inventory Arrange integration bound");
   var mod=ModReflect.FindType("SmartInventoryArrange.SmartArrangeMod");var tx=ModReflect.FindType("SmartInventoryArrange.InventoryTransaction");
   var observe=new Harmony("shop-together.smart1349-probe");arrangeCommits1349=0;
   observe.Patch(ModReflect.Target(tx,"ApplyCore"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"CountArrange1349"));
   try{
    var grid=EmporiumEntry.Instance.invElement;IEnumerator wait;
    if(host){
     foreach(var item in new List<GameItem>(grid.childItems))item.Destroy();
     for(int i=0;i<12;i++)PanelFixture(i%2==0?"magnifier":"screwdriver","S49_"+i);
     root.World.Index();root.SyncStateSoon();Signal("S49_READY");
     wait=Wait(()=>arrangeCommits1349>0&&!ModReflect.GetStatic<bool>(mod,"Busy"),"friend smart arrangement commits on host",120);while(wait.MoveNext())yield return wait.Current;
     Require(grid.childItems.Count==12,"smart arrange retains all twelve items");root.SyncStateSoon();Signal("S49_LAYOUT:"+Layout1349(grid));
     wait=Wait(()=>Received("S49_SYNCED"),"friend receives committed layout without running local solver",90);while(wait.MoveNext())yield return wait.Current;
     var held=Find(root,"S49_0");int id=root.World.Id(held);long token;bool claimed=root.Leases.Claim(0,new[]{id},Time.realtimeSinceStartup,out token);Require(claimed,"host holds an item before rejected friend arrangement");
     int commits=arrangeCommits1349;Signal("S49_HELD");wait=Wait(()=>Received("S49_REFUSED"),"friend receives refusal while host holds item",90);while(wait.MoveNext())yield return wait.Current;
     Require(arrangeCommits1349==commits,"occupied inventory never commits an arrangement");root.Leases.Release(0,new[]{id});Signal("S49_RELEASED");
    }else{
     wait=Wait(()=>Find(root,"S49_11")!=null&&Received("S49_READY"),"all smart arrange fixtures replicated",90);while(wait.MoveNext())yield return wait.Current;
     NativeClick(0,()=>ModReflect.CallStatic(mod,"Run",grid,false,null,0,0,null));
     string expectedLayout=null;wait=Wait(()=>{if(expectedLayout==null)expectedLayout=ReceivePrefix("S49_LAYOUT:");return expectedLayout!=null&&Layout1349(grid)==expectedLayout;},"friend layout matches host smart arrangement",120);while(wait.MoveNext())yield return wait.Current;
     Require(arrangeCommits1349==0,"friend never runs local arrangement commit");Signal("S49_SYNCED");
     wait=Wait(()=>Received("S49_HELD"),"host holding sort item",90);while(wait.MoveNext())yield return wait.Current;
     RemoteActions.LastNote="";NativeClick(0,()=>ModReflect.CallStatic(mod,"Run",grid,true,null,0,0,null));
     wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC328~"),"occupied inventory refusal reaches friend",60);while(wait.MoveNext())yield return wait.Current;Signal("S49_REFUSED");
     wait=Wait(()=>Received("S49_RELEASED"),"host releases item",60);while(wait.MoveNext())yield return wait.Current;
    }
    var undoCheck=GroupedUndo1350(root,host);while(undoCheck.MoveNext())yield return undoCheck.Current;
    var water=WaterSales1349(root,host);while(water.MoveNext())yield return water.Current;
   }finally{observe.UnpatchAll("shop-together.smart1349-probe");}
  }
  static IEnumerator GroupedUndo1350(Connection root,bool host){
   var arrange=ModReflect.FindType("SmartInventoryArrange.SmartArrangeMod");var undo=ModReflect.FindType("SmartInventoryArrange.ArrangeUndo");IEnumerator wait;
   if(host){
    var group=Activator.CreateInstance(ModReflect.FindType("SmartInventoryArrange.ArrangeUndo+Group"),true);
    var grids=new List<GameGridInventory>();var contents=new List<GameItem>();
    for(int k=0;k<2;k++){
     var bag=PanelFixture("backpack_small","U50_BAG_"+k);
     foreach(var old in new List<GameItem>(bag.FindAllChildItems()))old.Destroy();
     for(int j=0;j<4;j++){var item=ModHelper.SpawnItem(j%2==0?"magnifier":"screwdriver");item.name="U50_ITEM_"+k+"_"+j;GeneralHelper.SetItemOwned(item,true);Require(GraphUtils.TryAcceptAll(bag,item)==0,"group undo item placed");contents.Add(item);}
     var grid=contents[k*4].parentInventory as GameGridInventory;Require(grid!=null,"group undo container has native grid");grids.Add(grid);root.World.Index();
     int before=arrangeCommits1349;NativeClick(0,()=>ModReflect.CallStatic(arrange,"Run",grid,false,null,0,0,group));
     wait=Wait(()=>arrangeCommits1349>before&&!ModReflect.GetStatic<bool>(arrange,"Busy"),"grouped container arrangement completed",90);while(wait.MoveNext())yield return wait.Current;
    }
    var entries=ModReflect.Get(group,"Inventories") as IList;Require(entries!=null&&entries.Count==2,"one native undo group contains both containers");
    int id=root.World.Id(contents[4]);long token;Require(root.Leases.Claim(0,new[]{id},Time.realtimeSinceStartup,out token),"host holds item in second undo container");root.SyncStateSoon();Signal("U50_HELD");
    wait=Wait(()=>Received("U50_REFUSED"),"friend receives grouped undo refusal for other container",90);while(wait.MoveNext())yield return wait.Current;
    Require(ModReflect.GetStatic(undo,"_latest")==group&&!(bool)ModReflect.GetStatic(undo,"Running"),"refused grouped undo preserves original undo state");root.Leases.Release(0,new[]{id});Signal("U50_RELEASED");
    wait=Wait(()=>ModReflect.GetStatic(undo,"_latest")==null&&!(bool)ModReflect.GetStatic(undo,"Running"),"friend undo restores whole group after release",90);while(wait.MoveNext())yield return wait.Current;root.SyncStateSoon();Signal("U50_DONE");
   }else{
    wait=Wait(()=>Find(root,"U50_ITEM_0_0")!=null&&Find(root,"U50_ITEM_1_3")!=null&&Received("U50_HELD"),"both undo containers replicated",90);while(wait.MoveNext())yield return wait.Current;
    var grid=Find(root,"U50_ITEM_0_0").parentInventory;RemoteActions.LastNote="";
    NativeClick(0,()=>RemoteActions.Forward(RemoteActions.Adapter("UndoPage",2),null,new object[]{grid,0}));
    wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC328~"),"undo of first container refuses held item in second container",60);while(wait.MoveNext())yield return wait.Current;Signal("U50_REFUSED");
    wait=Wait(()=>Received("U50_RELEASED"),"other-container item released",60);while(wait.MoveNext())yield return wait.Current;
    NativeClick(0,()=>RemoteActions.Forward(RemoteActions.Adapter("UndoPage",2),null,new object[]{grid,0}));
    wait=Wait(()=>Received("U50_DONE"),"grouped undo completes on host",90);while(wait.MoveNext())yield return wait.Current;
   }
  }
  static IEnumerator WaterSales1349(Connection root,bool host){
   IEnumerator wait;
   if(host){
    var store=PlayerStore.Instance;EmporiumOption.Instance.autoCallNext=false;StoreShutterButton.Instance.OpenShutter();store.lockNextBellFlag=0;
    if(store.storeClientManager.clientStack.Count==0)store.storeClientManager.GenerateClient();store.TryCallNextClient();
    wait=Wait(()=>store.currentClientInstance!=null,"native customer arrives for water sale",90);while(wait.MoveNext())yield return wait.Current;
    var c=store.currentClientInstance.GetClientBlueprint();TradeCustomer1327(c,StoreClient.ClientIntent.BUY);TradeClear1327();
    c.clientBuyingTagList.Clear();c.clientBuyingTagList.Add("WATER");c.useClientBudget=true;c.OverrideBudget(100000);
    var bottle=ClockBottle("W49_PURE",300000);Require(GraphUtils.TryAcceptAll(EmporiumEntry.Instance.backInvinvElement,bottle)==0,"pure water placed on counter");
    Require(WaterHelper.GetWaterPurity(bottle)==10000&&c.IsClientBuyingThisItem(bottle),"base game customer accepts the pure-water fixture");
    var pitcher=PanelFixture("water_pitcher","W50_FILTERED");ChemicalHelper.RemoveAllLiquid(pitcher);WaterHelper.AddLiquid(pitcher,"water",300000);
    var filter=ModHelper.SpawnItem("water_filter");GeneralHelper.SetItemOwned(filter,true);Require(GraphUtils.TryAcceptAll(pitcher,filter)==0,"water container contains native filter");
    Require(!c.IsClientBuyingThisItem(pitcher)&&c.IsClientBuyingThisItem(pitcher,true)&&CounterTrades.RefusalForItem(c,pitcher)=="~SMC372~","filter refusal matches native customer rules and specific message");
    pitcher.Destroy();int cash=store.playerCash;root.World.Index();root.SyncStateSoon();Signal("W49_SELL");
    wait=Wait(()=>store.playerCash>cash&&bottle.parentInventory!=EmporiumEntry.Instance.backInvinvElement,"friend's pure-water sale completes on host",90);while(wait.MoveNext())yield return wait.Current;
    Signal("W49_CASH:"+store.playerCash);wait=Wait(()=>Received("W49_SYNCED"),"friend sees water sale and authoritative cash",90);while(wait.MoveNext())yield return wait.Current;
   }else{
    wait=Wait(()=>Find(root,"W49_PURE")!=null&&CurrentCustomer()!=null&&Received("W49_SELL"),"pure water and buyer replicated",90);while(wait.MoveNext())yield return wait.Current;
    var bottle=Find(root,"W49_PURE");Require(WaterHelper.GetWaterPurity(bottle)==10000,"friend displays same pure water");
    NativeClick(0,()=>RemoteActions.Forward(RemoteActions.Adapter("CounterAction",2),null,new object[]{0,bottle}));
    string cash=null;wait=Wait(()=>{if(cash==null)cash=ReceivePrefix("W49_CASH:");return cash!=null&&PlayerStore.Instance.playerCash==int.Parse(cash)&&bottle.parentInventory!=EmporiumEntry.Instance.backInvinvElement;},"friend sees completed pure-water sale and exact host cash",90);while(wait.MoveNext())yield return wait.Current;Signal("W49_SYNCED");
   }
  }
 }
}
#endif
