#if LAN_PROBE
using System;
using System.Collections;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static void NightPower(GameItem machine){var battery=PreBuiltItemHelper.BatteryDisposable(1000);GeneralHelper.SetItemOwned(battery,true);Require(GraphUtils.TryAcceptAll(machine,battery)==0,"powered "+machine.identifier);}
  static IEnumerator NightPlace(Connection root,string machineName,int slot,string itemName){
   var machine=Find(root,machineName);var item=Find(root,itemName);Require(machine!=null&&item!=null,"guest has "+itemName);
   var inventory=MachineHelper.GetBodyStorage(machine,slot);Require(inventory!=null,"guest machine slot "+machineName+" "+slot);
   NativeClick(0,()=>GraphUtils.TryAcceptAll(inventory,item));var wait=Wait(()=>item.parentInventory==inventory,"guest placed "+itemName+" through host",90);while(wait.MoveNext())yield return wait.Current;root.ReleaseAll();
  }
  static IEnumerator Overnight1313Host(Connection root){
   var e=EmporiumEntry.Instance;e.invElement.SetShape(50,50);e.Validate(true);
   var furnace=PanelFixture("furnace","NIGHT_FURNACE");var printer=PanelFixture("printer","NIGHT_PRINTER");var hydro=PanelFixture("hydroponic","NIGHT_HYDRO");var wine=PanelFixture("wine_bottle","NIGHT_WINE");
   NightPower(furnace);NightPower(printer);NightPower(hydro);WaterHelper.FillWithPureWater(wine);
   var scrap1=PanelFixture("scrap_metal","NIGHT_SCRAP_1");var scrap2=PanelFixture("scrap_metal","NIGHT_SCRAP_2");scrap1.mayStackItemFunc=(a,b)=>false;scrap2.mayStackItemFunc=(a,b)=>false;
   PanelFixture("printer_chip_small_bottle","NIGHT_CHIP");PanelFixture("printer_plastic","NIGHT_PLASTIC");PanelFixture("nutrifruit_seed","NIGHT_SEED");var water=PanelFixture("large_bottled_water","NIGHT_WATER");WaterHelper.FillWithPureWater(water);
   PanelFixture("wine_berry","NIGHT_BERRY");PanelFixture("wine_yeast","NIGHT_YEAST");root.World.Index();e.Validate(true);Signal("NIGHT_READY");
   var wait=Wait(()=>Received("NIGHT_PLACED"),"guest loaded all overnight machines",180);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>wine.GetTagReadonly("WINE_CONTAINER_STATE").GetInt()==1,"guest started fermentation on host",90);while(wait.MoveNext())yield return wait.Current;Signal("NIGHT_WINE_STARTED");
   wait=Wait(()=>Received("NIGHT_WINE_SEEN"),"guest received host fermentation state",90);while(wait.MoveNext())yield return wait.Current;
   Require(ChildCount(furnace,"scrap_metal")>=2&&ChildCount(printer,"printer_plastic")>=1&&MachineHydroponic.GetInputSlot(hydro).childItem!=null&&MachineHydroponic.GetWaterSlot(hydro).childItem!=null,"host received all guest overnight ingredients");
   int beforeWine=wine.GetTagReadonly("FERMENTATION_TIME_REMAINING_INT").GetInt();PlayerStore.Instance.EndNight();root.World.Index();
   int ingots=ChildCount(furnace,"metal_ingot"),printed=ChildCount(printer,"small_bottled_water"),progress=MachineProgressHelper.GetProgress(hydro).Item1,afterWine=wine.GetTagReadonly("FERMENTATION_TIME_REMAINING_INT").GetInt();
   Require(ingots>=1&&ChildCount(furnace,"scrap_metal")==0,"native overnight furnace consumed two scraps and produced ingot");
   Require(printed>=1&&ChildCount(printer,"printer_plastic")==0,"native overnight printer consumed plastic and produced bottles");
   Require(progress>0&&MachineHydroponic.GetInputSlot(hydro).childItem!=null,"native overnight hydroponic seed grew");
   Require(afterWine<beforeWine||wine.GetTagReadonly("WINE_CONTAINER_STATE").GetInt()==2,"native fermentation advanced overnight");
   Signal("NIGHT_RESULT="+ingots+","+printed+","+progress+","+afterWine+","+wine.GetTagReadonly("WINE_CONTAINER_STATE").GetInt());
   wait=Wait(()=>Received("NIGHT_MATCH"),"guest sees the same overnight results",120);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator Overnight1313Client(Connection root){
   var wait=Wait(()=>Find(root,"NIGHT_YEAST")!=null&&Find(root,"NIGHT_FURNACE")!=null&&Received("NIGHT_READY"),"overnight fixtures replicated",120);while(wait.MoveNext())yield return wait.Current;
   string[] machines={"NIGHT_FURNACE","NIGHT_FURNACE","NIGHT_PRINTER","NIGHT_PRINTER","NIGHT_HYDRO","NIGHT_HYDRO","NIGHT_WINE","NIGHT_WINE"};
   int[] slots={2,2,3,2,3,2,0,0};string[] items={"NIGHT_SCRAP_1","NIGHT_SCRAP_2","NIGHT_CHIP","NIGHT_PLASTIC","NIGHT_SEED","NIGHT_WATER","NIGHT_BERRY","NIGHT_YEAST"};
   for(int i=0;i<items.Length;i++){var place=NightPlace(root,machines[i],slots[i],items[i]);while(place.MoveNext())yield return place.Current;}
   var wine=Find(root,"NIGHT_WINE");MachineClick(wine,0);Require(wine.GetTagReadonly("WINE_CONTAINER_STATE").GetInt()==0,"guest does not start fermentation locally");Signal("NIGHT_PLACED");
   wait=Wait(()=>Received("NIGHT_WINE_STARTED"),"host started fermentation",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>wine.GetTagReadonly("WINE_CONTAINER_STATE").GetInt()==1,"host fermentation state replicated",90);while(wait.MoveNext())yield return wait.Current;Signal("NIGHT_WINE_SEEN");
   string result=null;wait=Wait(()=>(result=ReceivePrefix("NIGHT_RESULT="))!=null,"host completed native overnight cycle",120);while(wait.MoveNext())yield return wait.Current;var fields=result.Split(',');int ingots=int.Parse(fields[0]),printed=int.Parse(fields[1]),progress=int.Parse(fields[2]),remaining=int.Parse(fields[3]),state=int.Parse(fields[4]);
   var furnace=Find(root,"NIGHT_FURNACE");var printer=Find(root,"NIGHT_PRINTER");var hydro=Find(root,"NIGHT_HYDRO");
   wait=Wait(()=>ChildCount(furnace,"metal_ingot")==ingots&&ChildCount(furnace,"scrap_metal")==0&&ChildCount(printer,"small_bottled_water")==printed&&ChildCount(printer,"printer_plastic")==0&&MachineProgressHelper.GetProgress(hydro).Item1==progress&&wine.GetTagReadonly("FERMENTATION_TIME_REMAINING_INT").GetInt()==remaining&&wine.GetTagReadonly("WINE_CONTAINER_STATE").GetInt()==state,"guest furnace, printer, growth and fermentation match host overnight",120);while(wait.MoveNext())yield return wait.Current;
   Signal("NIGHT_MATCH");
  }
 }
}
#endif
