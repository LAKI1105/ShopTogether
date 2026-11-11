#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static int powerDraws,fermentations; static GameItem watchedBattery;
  static void PowerDrawObserved(GameItem __0){if(__0==watchedBattery)powerDraws++;}
  static void FermentationObserved(){fermentations++;}
  static GameItem MachineFixture(string id,string name){var item=ModHelper.SpawnItem(id);item.name=name;EmporiumEntry.Instance.TryAddToPlayerInv(item);Require(item.parentInventory!=null,"fixture placed: "+id);return item;}
  static int ChildCount(GameItem item,string id){int n=0;foreach(var c in GraphUtils.FindAllChildrenType<GameItem>(item))if(c!=item&&c.identifier==id)n+=c.unitCount;return n;}
  static void MachineClick(GameItem item,int index){item.contentWindow.Show();item.contentWindow.Validate();var buttons=MachineButtonPress.Buttons(item.contentWindow);Require(buttons.Count>index,"native machine button exists: "+item.identifier);NativeClick(0,()=>AccessTools.Method(typeof(MachineryButton),"OnSelect").Invoke(buttons[index],null));}
  static IEnumerator MachinesHost(Connection root){
   var e=EmporiumEntry.Instance;e.invElement.SetShape(36,36);e.Validate(true);
   var h=new Harmony("shop-together.machine136-probe");h.Patch(AccessTools.Method(typeof(PowerHelper),"DrawPowerSource"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"PowerDrawObserved"));h.Patch(AccessTools.Method(typeof(WineHelper),"StartFermentation"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"FermentationObserved"));
   var printer=MachineFixture("bottle_printer","SMC_PRINTER");var battery=PreBuiltItemHelper.BatteryDisposable(1000);GeneralHelper.SetItemOwned(battery,true);Require(GraphUtils.TryAcceptAll(printer,battery)==0,"printer power source installed");foreach(var child in GraphUtils.FindAllChildrenType<GameItem>(printer))if(child.IsTag("power_source_item")){battery=child;break;} watchedBattery=battery; int charge=battery.GetTagReadonly("power_source_item_energy").GetInt();
   var wine=MachineFixture("wine_bottle","SMC_WINE");WaterHelper.FillWithPureWater(wine);var berry=ModHelper.SpawnItem("wine_berry");var yeast=ModHelper.SpawnItem("wine_yeast");Require(GraphUtils.TryAcceptAll(wine,berry)==0&&GraphUtils.TryAcceptAll(wine,yeast)==0,"native fermentation ingredients installed");
   Signal("MACHINES_READY");var wait=Wait(()=>ChildCount(printer,"small_bottled_water")==1,"client printer button produced one bottle on host",40);while(wait.MoveNext())yield return wait.Current;
   Require(powerDraws==1&&battery.GetTagReadonly("power_source_item_energy").GetInt()==charge-4,"printer charges exactly once on host: calls="+powerDraws+" charge="+battery.GetTagReadonly("power_source_item_energy").GetInt()+" initial="+charge);Signal("PRINTER_OK");wait=Wait(()=>ChildCount(printer,"small_bottled_water")==2,"localized guest button label accepted",40);while(wait.MoveNext())yield return wait.Current;Require(powerDraws==2&&battery.GetTagReadonly("power_source_item_energy").GetInt()==charge-8,"second localized press charged once");Signal("LOCAL_LABEL_OK");
   wait=Wait(()=>wine.GetTagReadonly("WINE_CONTAINER_STATE").GetInt()!=0,"client started native fermentation on host",40);while(wait.MoveNext())yield return wait.Current;
   Require(fermentations==1&&ChildCount(wine,"wine_berry")==0,"fermentation ingredients consumed once on host");Signal("WINE_OK");
   var upgrade=PlayerStore.Instance.networkUpgrade[NetworkUpgrade.BREWER];upgrade.state=1;upgrade.cooldownCurrent=0;upgrade.prerequisite.Clear();PlayerStore.Instance.playerCash=100000;PlayerStore.Instance.wildFavor=0;int price=upgrade.GetCost();Require(upgrade.IsReady(),"native Wild upgrade ready");Signal("WILD_READY");wait=Wait(()=>upgrade.IsUnlocked(),"client Wild upgrade executed on host",40);while(wait.MoveNext())yield return wait.Current;Require(PlayerStore.Instance.playerCash==100000-price,"Wild upgrade charged host exactly once");Signal("WILD_OK");
   wait=Wait(()=>Received("MACHINES_DONE"),"machine results replicated",40);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator MachinesClient(Connection root){
   var h=new Harmony("shop-together.machine136-probe");h.Patch(AccessTools.Method(typeof(PowerHelper),"DrawPowerSource"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"PowerDrawObserved"));h.Patch(AccessTools.Method(typeof(WineHelper),"StartFermentation"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"FermentationObserved"));
   var wait=Wait(()=>Find(root,"SMC_PRINTER")!=null&&Find(root,"SMC_WINE")!=null&&Received("MACHINES_READY"),"machines replicated",40);while(wait.MoveNext())yield return wait.Current;
   var printer=Find(root,"SMC_PRINTER");var wine=Find(root,"SMC_WINE");MachineClick(printer,0);wait=Wait(()=>Received("PRINTER_OK"),"host printer accounting verified",40);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>ChildCount(printer,"small_bottled_water")==1,"one printed bottle replicated",40);while(wait.MoveNext())yield return wait.Current;
   OutRequest("PressButton",printer,0,"Imprimir botella / 印製瓶子");wait=Wait(()=>Received("LOCAL_LABEL_OK"),"host accepts different localized button label",40);while(wait.MoveNext())yield return wait.Current;MachineClick(wine,0);wait=Wait(()=>Received("WINE_OK"),"host fermentation verified",40);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("WILD_READY"),"Wild upgrade ready",40);while(wait.MoveNext())yield return wait.Current;OutRequest("BuyWild",NetworkUpgrade.BREWER);wait=Wait(()=>Received("WILD_OK"),"host upgrade accounting verified",40);while(wait.MoveNext())yield return wait.Current;Signal("MACHINES_DONE");
  }
 }
}
#endif
