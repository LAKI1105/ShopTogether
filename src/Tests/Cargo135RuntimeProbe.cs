#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static MachineryTabs CargoTabs(GameItem bay){return (MachineryTabs)AccessTools.Method(typeof(CargoIntent),"Tabs").Invoke(null,new object[]{bay});}
  static SettingValue Setting(string key,int value){return new SettingValue{tag=key,packed="1|"+value+"|0|0|0|0|"};}
  static object MainPages(){return AccessTools.Field(AccessTools.TypeByName("ShopStoragePages.Pages"),"Current").GetValue(null);}
  static int MainPageCount(){var p=MainPages();return (int)AccessTools.Field(p.GetType(),"unlocked").GetValue(p);}
  static IEnumerator CargoHost135(Connection root){
   EmporiumEntry.Instance.invElement.SetShape(80,80);
   var bay=PanelFixture("corp_presence_cargo_warehouse","CARGO_BAY");var controller=PanelFixture("corp_presence_cargo_sort_controller","CARGO_CONTROLLER");
   var module=PanelFixture("corp_presence_cargo_warehouse_module","CARGO_MODULE");module.Target(bay);var tabs=CargoTabs(bay);Require(tabs!=null&&tabs.count==2,"native cargo expansion creates second tab");tabs.Select(0);
   var machine=PanelFixture("auto_butcher","CARGO_SETTINGS");root.World.Index();Signal("CARGO_IDS="+root.World.Id(bay)+","+root.World.Id(controller)+","+root.World.Id(machine));
   var wait=Wait(()=>bay.GetTagReadonly("corp_presence_cargo_filter_1").GetString()=="NARCOTIC","guest cargo filters applied to requested second tab",90);while(wait.MoveNext())yield return wait.Current;
   Require(tabs.selected==0&&string.IsNullOrEmpty(bay.GetTagReadonly("corp_presence_cargo_filter_0").GetString()),"host selected page and first-page filters unchanged");
   wait=Wait(()=>StorageExpansionHelper.GetTabName(bay,1)=="Shared Cargo","cargo tab rename executed on host",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>machine.GetTagReadonly("AUTO_BUTCHER_CAGE_WEIGHT_MIN_INT").GetInt()==700&&machine.GetTagReadonly("AUTO_BUTCHER_CAGE_WEIGHT_MAX_INT").GetInt()==800,"atomic valid setting pair executed on host",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("CARGO_DONE"),"cargo and batch rejection results synchronized",90);while(wait.MoveNext())yield return wait.Current;
   var held=PanelFixture("screwdriver","CARGO_HELD");root.World.Index();long token;int id=root.World.Id(held);Require(root.Leases.Claim(2,new[]{id},Time.realtimeSinceStartup,out token),"competing player holds hopper source");
   Require(!HopperLeases.CanAccept(EmporiumEntry.Instance.backInvinvElement,held),"hopper refuses moving another player's held item");
   Require(!HarvestLeases.Allow(held,null,null),"harvest refuses another player's selected item");root.Leases.Release(2,new[]{id});
   PlayerStore.Instance.playerCash=10000;Signal("CARGO_PAGES_READY");
   wait=Wait(()=>MainPageCount()==2,"main storage unlock executed by native host",90);while(wait.MoveNext())yield return wait.Current;
   Require(PlayerStore.Instance.playerCash==9500,"storage unlock paid native host price once");
   wait=Wait(()=>((string[])AccessTools.Field(MainPages().GetType(),"names").GetValue(MainPages()))[1]=="Shared","main storage rename executed by host",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("CARGO_PAGES_DONE"),"guest storage page metadata returned",60);while(wait.MoveNext())yield return wait.Current;
   Require(StoragePagesIntent.Page(EmporiumEntry.Instance.invElement)==0,"guest storage actions preserve host selected page");Signal("CARGO_HOST_DONE");
  }
  static IEnumerator CargoClient135(Connection root){
   string ids=null;var wait=Wait(()=>(ids=ids??ReceivePrefix("CARGO_IDS="))!=null,"cargo fixture addresses received",60);while(wait.MoveNext())yield return wait.Current;
   var p=Array.ConvertAll(ids.Split(','),int.Parse);wait=Wait(()=>root.World.Items.ContainsKey(p[0])&&root.World.Items.ContainsKey(p[1])&&root.World.Items.ContainsKey(p[2]),"cargo fixtures replicated",60);while(wait.MoveNext())yield return wait.Current;
   var bay=root.World.Items[p[0]];var source=root.World.Items[p[1]];var machine=root.World.Items[p[2]];var tabs=CargoTabs(bay);Require(tabs!=null&&tabs.count==2,"guest cargo tabs restored");tabs.Select(1);
   NativeClick(0,()=>source.Target(bay));var window=AccessTools.TypeByName("CorpPresence.UI.CargoSortWindow");
   wait=Wait(()=>(bool)AccessTools.Property(window,"Active").GetValue(null,null),"cargo filter panel opened on guest",60);while(wait.MoveNext())yield return wait.Current;
   Require((int)AccessTools.Field(window,"_page").GetValue(null)==1,"cargo panel uses guest-selected tab");
   AccessTools.Field(window,"_checked").SetValue(null,new HashSet<string>{"NARCOTIC"});NativeClick(0,()=>AccessTools.Method(window,"SaveAndClose").Invoke(null,new object[]{true}));
   wait=Wait(()=>LabelerBlueUIManager.Instance.panel.activeSelf,"save-and-rename opens guest name editor",60);while(wait.MoveNext())yield return wait.Current;
   LabelerBlueUIManager.Instance.inputField.text="Shared Cargo";NativeClick(0,()=>LabelerBlueUIManager.Instance.OnEditEnd("Shared Cargo"));LabelerBlueUIManager.Instance.CloseUI();
   wait=Wait(()=>bay.GetTagReadonly("corp_presence_cargo_filter_1").GetString()=="NARCOTIC"&&StorageExpansionHelper.GetTabName(bay,1)=="Shared Cargo","cargo filters and name returned from host",60);while(wait.MoveNext())yield return wait.Current;
   int original=machine.GetTagReadonly("AUTO_BUTCHER_CAGE_WEIGHT_MIN_INT").GetInt();RemoteActions.LastNote=null;
   RemoteActions.Forward(RemoteActions.Adapter("SetSettings",2),null,new object[]{machine,WireJson.ToJson(new[]{Setting("AUTO_BUTCHER_CAGE_WEIGHT_MIN_INT",900),Setting("AUTO_BUTCHER_CAGE_WEIGHT_MAX_INT",200)})});
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC344~"),"invalid range returns host reason",45);while(wait.MoveNext())yield return wait.Current;
   Require(machine.GetTagReadonly("AUTO_BUTCHER_CAGE_WEIGHT_MIN_INT").GetInt()==original,"invalid range applies no partial setting");
   RemoteActions.Forward(RemoteActions.Adapter("SetSettings",2),null,new object[]{machine,WireJson.ToJson(new[]{Setting("AUTO_BUTCHER_CAGE_WEIGHT_MIN_INT",700),Setting("AUTO_BUTCHER_CAGE_WEIGHT_MAX_INT",800)})});
   wait=Wait(()=>machine.GetTagReadonly("AUTO_BUTCHER_CAGE_WEIGHT_MIN_INT").GetInt()==700&&machine.GetTagReadonly("AUTO_BUTCHER_CAGE_WEIGHT_MAX_INT").GetInt()==800,"valid range pair replicated together",45);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.LastNote=null;RemoteActions.Forward(RemoteActions.Adapter("SetSettings",2),null,new object[]{machine,WireJson.ToJson(new[]{Setting("AUTO_BUTCHER_CAGE_WEIGHT_MIN_INT",750),Setting("CURRENT_PART_ACID",999)})});
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC305~"),"batch containing a simulation result is refused",45);while(wait.MoveNext())yield return wait.Current;
   Require(machine.GetTagReadonly("AUTO_BUTCHER_CAGE_WEIGHT_MIN_INT").GetInt()==700,"forbidden second entry does not commit first entry");Signal("CARGO_DONE");
   wait=Wait(()=>Received("CARGO_PAGES_READY"),"host cross-item lease checks complete",60);while(wait.MoveNext())yield return wait.Current;
   var pages=MainPages();Require(pages!=null&&MainPageCount()==1,"guest main storage initially has one page");
   NativeClick(0,()=>AccessTools.Method(pages.GetType(),"ConfirmUnlock").Invoke(pages,new object[]{1}));
   wait=Wait(()=>GenericConfirmationUIManager.Instance.panel.activeSelf,"storage price confirmation relayed to guest",60);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>GenericConfirmationUIManager.Instance.ConfirmButtonPressed());
   wait=Wait(()=>MainPageCount()==2&&PlayerStore.Instance.playerCash==9500,"host storage unlock and cash replicated",60);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>AccessTools.Method(pages.GetType(),"SetName").Invoke(pages,new object[]{1,"Shared"}));
   wait=Wait(()=>((string[])AccessTools.Field(pages.GetType(),"names").GetValue(pages))[1]=="Shared","host storage page name replicated",60);while(wait.MoveNext())yield return wait.Current;Signal("CARGO_PAGES_DONE");
   wait=Wait(()=>Received("CARGO_HOST_DONE"),"host storage view preserved",60);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
