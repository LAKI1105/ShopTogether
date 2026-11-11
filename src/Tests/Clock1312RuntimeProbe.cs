#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static bool HasMachineText(GameItem item,string text){
   if(item==null||item.contentWindow==null||item.contentWindow.handler==null)return false;
   foreach(var node in item.contentWindow.handler.GetComponentsInChildren<TreeNodeRender>(true)){var value=node.element as MachineryText;if(value!=null&&value.GetText()==text)return true;}return false;
  }
  static string ReceivePrefix(string prefix){Poll();int count=controlMessages.Count;for(int i=0;i<count;i++){string value=controlMessages.Dequeue();if(value.StartsWith(prefix,StringComparison.Ordinal))return value.Substring(prefix.Length);controlMessages.Enqueue(value);}return null;}
  static int Part(GameItem item,string name){return item.GetTagReadonly(Liquid.GetCurrentPartFromId(name)).GetInt();}
  static GameItem ClockBottle(string name,int water){var b=PanelFixture("small_bottled_water",name);ChemicalHelper.RemoveAllLiquid(b);if(water>0)WaterHelper.AddLiquid(b,"water",water);return b;}
  static IEnumerator Clock1312Host(Connection root){
   var e=EmporiumEntry.Instance;e.invElement.SetShape(36,36);e.Validate(true);
   var plate=PanelFixture("heating_plate","CLOCK_PLATE");var heated=ModHelper.SpawnItem("small_bottled_water");heated.name="CLOCK_HEATED";GeneralHelper.SetItemOwned(heated,true);WaterHelper.FillWithPureWater(heated);
   heated.ModifyTag("CONTAINER_TEMP_TAG",t=>t.Enable().SetDouble(26));Require(GraphUtils.TryAcceptAll(plate,heated)==0&&heated.parentInventory is GameSlotInventory,"heating plate contains test water");
   plate.ModifyTag("HEATING_PLATE_STATE",t=>t.Enable().SetInt(0));
   var finisher=PanelFixture("chem_finisher","CLOCK_FINISHER");
   var sourceHost=ClockBottle("CLOCK_SOURCE_HOST",300000);var sourceGuest=ClockBottle("CLOCK_SOURCE_GUEST",300000);var target=ClockBottle("CLOCK_TARGET",0);
   var purified=ClockBottle("CLOCK_PURIFIED",200000);WaterHelper.AddLiquid(purified,"microbe",50000);var tablet=PanelFixture("water_tablet","CLOCK_TABLET");
   var nativePurifyTick=purified.onOneSecondItemFunc;Require(nativePurifyTick!=null,"purification container has native reaction clock");purified.onOneSecondItemFunc=null;
   root.World.Index();e.Validate(true);Signal("CLOCK_READY");
   var wait=Wait(()=>Received("HEAT_READY"),"guest opened heating plate",90);while(wait.MoveNext())yield return wait.Current;
   plate.ModifyTag("HEATING_PLATE_STATE",t=>t.Enable().SetInt(2));
   wait=Wait(()=>Received("HEAT_VISIBLE"),"guest observed multiple temperature updates",90);while(wait.MoveNext())yield return wait.Current;
   plate.ModifyTag("HEATING_PLATE_STATE",t=>t.Enable().SetInt(0));heated.onOneSecondItemFunc=null;double finalTemp=heated.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble();Require(finalTemp>27,"host alone increased actual temperature");Signal("HEAT_TEMP="+finalTemp.ToString("R",CultureInfo.InvariantCulture));
   wait=Wait(()=>Received("HEAT_MATCH"),"guest temperature matches host",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>finisher.IsTag("SELECTED_CHEM_ID_TAG"),"guest selected finisher recipe on host",90);while(wait.MoveNext())yield return wait.Current;Signal("RECIPE_HOST");
   wait=Wait(()=>Received("RECIPE_VISIBLE"),"guest sees recipe target and status",90);while(wait.MoveNext())yield return wait.Current;
   Signal("POUR_GO");NativeClick(0,()=>sourceHost.Target(target));
   wait=Wait(()=>Received("POUR_SENT"),"guest sent concurrent pour",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>WaterHelper.GetTotalVolume(target)==450000,"both pours filled target without overflow",45);while(wait.MoveNext())yield return wait.Current;
   int hostLeft=WaterHelper.GetTotalVolume(sourceHost),guestLeft=WaterHelper.GetTotalVolume(sourceGuest),targetAmount=WaterHelper.GetTotalVolume(target);
   Require(hostLeft<300000&&guestLeft<300000&&hostLeft+guestLeft+targetAmount==600000&&targetAmount<=target.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt(),"both pours conserve water and respect bottle capacity");Signal("POUR_HOST");
   wait=Wait(()=>Received("POUR_MATCH"),"guest sees the same pour totals",90);while(wait.MoveNext())yield return wait.Current;
   Signal("PURIFY_GO");wait=Wait(()=>tablet.parentInventory==null&&Part(purified,"chlorine")>0,"guest tablet dissolved once on host",90);while(wait.MoveNext())yield return wait.Current;Signal("PURIFY_DISSOLVED");
   wait=Wait(()=>Received("PURIFY_SEEN"),"guest received dissolved tablet",90);while(wait.MoveNext())yield return wait.Current;
   purified.onOneSecondItemFunc=nativePurifyTick;wait=Wait(()=>Part(purified,"microbe")<50000,"native host purification reaction ran",30);while(wait.MoveNext())yield return wait.Current;
   purified.onOneSecondItemFunc=null;int microbes=Part(purified,"microbe"),chlorine=Part(purified,"chlorine");Signal("PURIFY_VALUE="+microbes+","+chlorine);
   wait=Wait(()=>Received("CLOCK_DONE"),"guest saw native purification result",90);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator Clock1312Client(Connection root){
   var wait=Wait(()=>Find(root,"CLOCK_PLATE")!=null&&Find(root,"CLOCK_HEATED")!=null&&Find(root,"CLOCK_FINISHER")!=null&&Find(root,"CLOCK_SOURCE_HOST")!=null&&Find(root,"CLOCK_SOURCE_GUEST")!=null&&Find(root,"CLOCK_TARGET")!=null&&Find(root,"CLOCK_PURIFIED")!=null&&Find(root,"CLOCK_TABLET")!=null&&Received("CLOCK_READY"),"clock fixtures replicated",90);while(wait.MoveNext())yield return wait.Current;
   var plate=Find(root,"CLOCK_PLATE");var heated=Find(root,"CLOCK_HEATED");OpenNativeContents(plate);
   int ticks=0;Action<GameItem,GameInventory,SlotMarker> tryWrite=(item,inv,slot)=>{ticks++;item.ModifyTag("CURRENT_TARGET_TEMP",t=>t.Enable().SetInt(3),true);item.DisableTag("CONTAINER_TAG",true);item.EnableTag("CLOCK_ILLEGAL_TAG",true);};plate.onOneSecondItemFunc+=tryWrite;
   var readings=new HashSet<int>();Signal("HEAT_READY");float deadline=Time.realtimeSinceStartup+30;
   while(Time.realtimeSinceStartup<deadline&&readings.Count<2){int t=(int)heated.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble();if(t>26&&HasMachineText(plate,MetaHelper.FormatTemperature(t)))readings.Add(t);yield return null;}
   Require(readings.Count>=2,"guest heating panel updates through multiple native seconds");Require(ticks>=2&&plate.GetTagReadonly("CURRENT_TARGET_TEMP").GetInt()==99&&plate.IsTag("CONTAINER_TAG")&&!plate.IsTag("CLOCK_ILLEGAL_TAG"),"guest panel refresh blocks ModifyTag, DisableTag and EnableTag writes");plate.onOneSecondItemFunc-=tryWrite;Signal("HEAT_VISIBLE");
   string value=null;wait=Wait(()=>(value=ReceivePrefix("HEAT_TEMP="))!=null,"host temperature sample",90);while(wait.MoveNext())yield return wait.Current;double hostTemp=double.Parse(value,CultureInfo.InvariantCulture);
   wait=Wait(()=>Math.Abs(heated.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble()-hostTemp)<0.001&&HasMachineText(plate,MetaHelper.FormatTemperature((int)hostTemp)),"guest temperature and panel match host",90);while(wait.MoveNext())yield return wait.Current;Signal("HEAT_MATCH");plate.contentWindow.Hide();
   var finisher=Find(root,"CLOCK_FINISHER");OpenNativeContents(finisher);var recipe=ChemFinisherRecipe.chemFinisherRecipes[0];
   SettingsClick(()=>{ChemFinisherUIManager.Instance.OpenUI(finisher);ChemFinisherUIManager.Instance.OnChemRecipeSelected(recipe.identifier);ChemFinisherUIManager.Instance.HideUI();});
   wait=Wait(()=>Received("RECIPE_HOST"),"finisher recipe accepted by host",90);while(wait.MoveNext())yield return wait.Current;
   string status=LocHelper.GetLocalizedMechanic("mech_turbo_booster_status",LocHelper.GetLocalizedMechanic("mech_chem_finisher_status_not_enough_reagent"));
   wait=Wait(()=>HasMachineText(finisher,recipe.displayName)&&HasMachineText(finisher,status),"guest finisher displays selected target and status",90);while(wait.MoveNext())yield return wait.Current;Signal("RECIPE_VISIBLE");finisher.contentWindow.Hide();
   wait=Wait(()=>Received("POUR_GO"),"concurrent pour barrier",90);while(wait.MoveNext())yield return wait.Current;var source=Find(root,"CLOCK_SOURCE_GUEST");var target=Find(root,"CLOCK_TARGET");NativeClick(0,()=>source.Target(target));Signal("POUR_SENT");
   wait=Wait(()=>WaterHelper.GetTotalVolume(target)==450000&&Received("POUR_HOST"),"concurrent pour results replicated",90);while(wait.MoveNext())yield return wait.Current;
   int total=WaterHelper.GetTotalVolume(Find(root,"CLOCK_SOURCE_HOST"))+WaterHelper.GetTotalVolume(source)+WaterHelper.GetTotalVolume(target);Require(total==600000&&WaterHelper.GetTotalVolume(target)<=target.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt(),"guest sees conserved volume with no overflow");Signal("POUR_MATCH");root.ReleaseAll();
   wait=Wait(()=>Received("PURIFY_GO"),"purification ready",90);while(wait.MoveNext())yield return wait.Current;var tablet=Find(root,"CLOCK_TABLET");var water=Find(root,"CLOCK_PURIFIED");NativeClick(0,()=>tablet.Target(water));
   wait=Wait(()=>Part(water,"chlorine")>0&&Received("PURIFY_DISSOLVED"),"dissolved tablet appears on guest",90);while(wait.MoveNext())yield return wait.Current;float started=Time.realtimeSinceStartup;Signal("PURIFY_SEEN");
   string result=null;wait=Wait(()=>(result=ReceivePrefix("PURIFY_VALUE="))!=null,"host purification result",90);while(wait.MoveNext())yield return wait.Current;var parts=result.Split(',');int microbes=int.Parse(parts[0]),chlorine=int.Parse(parts[1]);
   wait=Wait(()=>Part(water,"microbe")==microbes&&Part(water,"chlorine")==chlorine,"guest purification composition matches host",90);while(wait.MoveNext())yield return wait.Current;Require(microbes<50000&&Time.realtimeSinceStartup-started<8,"purification result updated promptly on guest");Signal("CLOCK_DONE");
  }
 }
}
#endif
