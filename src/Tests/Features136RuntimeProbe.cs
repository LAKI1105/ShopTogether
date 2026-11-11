#if LAN_PROBE
using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static int blends, cigaretteConfronts, stampConfronts, policeReports;
  static void BlendObserved(){blends++;}
  static void CigaretteObserved(){cigaretteConfronts++;}
  static void StampObserved(){stampConfronts++;}
  static void PoliceObserved(){policeReports++;}
  static void SettingsClick(Action a){NativeClick(0,()=>{MachineSettings.Input++;try{a();}finally{MachineSettings.Input--;}});}
  static IEnumerator FeaturesHost(Connection root){CheckAchievementSetting();
   var e=EmporiumEntry.Instance;e.invElement.SetShape(36,36);e.Validate(true);
   var harmony=new Harmony("shop-together.features136-probe");
   harmony.Patch(AccessTools.Method(typeof(ChemicalHelper),"BlendInto"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"BlendObserved"));
   harmony.Patch(AccessTools.Method(typeof(InsCigaretteHelper),"CreateConfrontDialogue"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"CigaretteObserved"));
   harmony.Patch(AccessTools.Method(typeof(InsStampHelper),"CreateConfrontDialogue"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"StampObserved"));
   harmony.Patch(AccessTools.Method(typeof(PlayerStore),"OnCustomerReported"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"PoliceObserved"));
   var blender=MachineFixture("blender","SMC_BLENDER");var bottle=ModHelper.SpawnItem("large_bottled_water");ChemicalHelper.RemoveAllLiquid(bottle);GeneralHelper.SetItemOwned(bottle,true);
   Require(GraphUtils.TryAcceptAll(blender,bottle)==0,"blender empty container installed");
   var ingredient=ModHelper.SpawnItem("birth_control_pill");Require(ingredient.IsTag("BLENDABLE_TAG"),"native blendable ingredient");GeneralHelper.SetItemOwned(ingredient,true);Require(GraphUtils.TryAcceptAll(blender,ingredient)==0,"blender ingredient installed");
   var hopper=ModHelper.SpawnItem("intake_hopper");hopper.name="SMC_HOPPER";GeneralHelper.SetItemOwned(hopper,true);e.TryAddToPlayerInv(hopper);
   var chem=MachineFixture("chem_finisher","SMC_CHEM");var pin=Find(root,"SMC_HOST_ITEM");
   Signal("FEATURES_READY");var wait=Wait(()=>blends==1,"client blender executed once on host",40);while(wait.MoveNext())yield return wait.Current;
   Require(ChildCount(blender,"birth_control_pill")==0,"blender consumed ingredient on host");Signal("BLENDER_OK");
   wait=Wait(()=>hopper.GetTagReadonly("HOPPER_ORDER_INT").GetInt()==9&&hopper.GetTagReadonly("HOPPER_KEEP_INT").GetInt()==3&&ItemPinHelper.IsPinned(pin)&&chem.IsTag("SELECTED_CHEM_ID_TAG"),"hopper, pin and chemical recipe replicated to host",40);while(wait.MoveNext())yield return wait.Current;Signal("SETTINGS_OK");
   foreach(var old in new System.Collections.Generic.List<GameItem>(e.backInvinvElement.childItems))e.TryAddToPlayerInv(old); var weighed=ModHelper.SpawnItem("magnifier");weighed.name="SMC_WEIGHT";weighed.AddItemFeature(new ItemFeature("weight").SetPublicDisplay("unknown").SetActualDisplay("known"));GraphUtils.TryAcceptAll(e.backInvinvElement,weighed);Signal("SCALE_READY");wait=Wait(()=>weighed.FindItemFeatureByCategory("weight").IsFeatureDiscovered(),"client weight discovery committed by host",40);while(wait.MoveNext())yield return wait.Current;Signal("SCALE_OK");
   var bag=MachineFixture("backpack_small","SMC_SORT_BAG");var a=ModHelper.SpawnItem("magnifier");a.name="SMC_SORT_A";GeneralHelper.SetItemOwned(a,true);GraphUtils.TryAcceptAll(bag,a);var b=ModHelper.SpawnItem("screwdriver");b.name="SMC_SORT_B";GeneralHelper.SetItemOwned(b,true);GraphUtils.TryAcceptAll(bag,b);
   var grid=a.parentInventory as GameGridInventory;Require(grid!=null&&b.parentInventory==grid,"sort backpack fixture");var original=new Vector2Int(a.modifiedShape.minX,a.modifiedShape.minY);
   Signal("SORT_READY");wait=Wait(()=>InventorySortHelper.CanUndo(grid),"client backpack sort executed on host",40);while(wait.MoveNext())yield return wait.Current;Signal("SORT_OK");wait=Wait(()=>!InventorySortHelper.CanUndo(grid),"client sort undo executed on host",40);while(wait.MoveNext())yield return wait.Current;Require(new Vector2Int(a.modifiedShape.minX,a.modifiedShape.minY)==original,"undo restores original item placement");Signal("UNDO_OK");
   StoreShutterButton.Instance.OpenShutter();PlayerStore.Instance.lockNextBellFlag=0;UnityEngine.Object.FindObjectOfType<StoreBell>().RingBell();wait=Wait(()=>PlayerStore.Instance.currentClientInstance!=null,"inspection customer present",40);while(wait.MoveNext())yield return wait.Current;
   var cig=PreBuiltItemHelper.FakeCigarette();cig.name="SMC_CIGARETTE";GeneralHelper.SetItemOwned(cig,true);GraphUtils.TryAcceptAll(e.frontInvinvElement,cig);GeneralHelper.SetItemOwned(cig,false);var stamp=PreBuiltItemHelper.CreateFakeStamp();stamp.name="SMC_STAMP";GeneralHelper.SetItemOwned(stamp,true);GraphUtils.TryAcceptAll(e.frontInvinvElement,stamp);GeneralHelper.SetItemOwned(stamp,false);
   Signal("INSPECT_READY");wait=Wait(()=>cigaretteConfronts==1,"native cigarette confrontation ran once on host",40);while(wait.MoveNext())yield return wait.Current;DialogUIManager.Instance.InterruptDialog();Signal("CIG_OK");wait=Wait(()=>stampConfronts==1,"native stamp confrontation ran once on host",40);while(wait.MoveNext())yield return wait.Current;DialogUIManager.Instance.InterruptDialog();Signal("STAMP_OK");
   wait=Wait(()=>policeReports==1,"client police request reached native host operation",40);while(wait.MoveNext())yield return wait.Current;Signal("POLICE_OK");
   wait=Wait(()=>Received("FEATURES_DONE"),"guest feature completion",40);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator FeaturesClient(Connection root){CheckAchievementSetting();
   var wait=Wait(()=>Find(root,"SMC_BLENDER")!=null&&Find(root,"SMC_HOPPER")!=null&&Find(root,"SMC_CHEM")!=null&&Received("FEATURES_READY"),"feature fixtures replicated",40);while(wait.MoveNext())yield return wait.Current;
   MachineClick(Find(root,"SMC_BLENDER"),0);wait=Wait(()=>Received("BLENDER_OK"),"host blender accounting confirmed",40);while(wait.MoveNext())yield return wait.Current;
   SettingsClick(()=>{var hopper=Find(root,"SMC_HOPPER");hopper.ModifyTag("HOPPER_ORDER_INT",t=>t.Enable().SetInt(9));hopper.ModifyTag("HOPPER_KEEP_INT",t=>t.Enable().SetInt(3));});
   NativeClick(0,()=>ItemPinHelper.TogglePinned(Find(root,"SMC_HOST_ITEM")));
   SettingsClick(()=>{ChemFinisherUIManager.Instance.OpenUI(Find(root,"SMC_CHEM"));ChemFinisherUIManager.Instance.OnChemRecipeSelected(ChemFinisherRecipe.chemFinisherRecipes[0].identifier);ChemFinisherUIManager.Instance.HideUI();});
   wait=Wait(()=>Received("SETTINGS_OK"),"host settings confirmed",40);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Find(root,"SMC_WEIGHT")!=null&&Received("SCALE_READY"),"scale fixture replicated",40);while(wait.MoveNext())yield return wait.Current;PlayerStore.Instance.currentNegociatedItem=Find(root,"SMC_WEIGHT");NativeClick(0,()=>PlayerStore.Instance.currentNegociatedItem.FindItemFeatureByCategory("weight").DiscoverFeature());wait=Wait(()=>Received("SCALE_OK"),"host weight discovery confirmed",40);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Find(root,"SMC_SORT_A")!=null&&Find(root,"SMC_SORT_B")!=null&&Received("SORT_READY"),"sort fixtures replicated",40);while(wait.MoveNext())yield return wait.Current;var grid=Find(root,"SMC_SORT_A").parentInventory as GameGridInventory;
   NativeClick(0,()=>InventorySortHelper.Sort(grid,true,true));wait=Wait(()=>Received("SORT_OK"),"native sort reached host",40);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>InventorySortHelper.Undo(grid));wait=Wait(()=>Received("UNDO_OK"),"host undo confirmed",40);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Find(root,"SMC_CIGARETTE")!=null&&Find(root,"SMC_STAMP")!=null&&Received("INSPECT_READY"),"inspection fixtures replicated",40);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>{InspectionUIManager.Instance.OnInspectItem(Find(root,"SMC_CIGARETTE"));InspectionUIManager.Instance.toggle1.isOn=true;InspectionUIManager.Instance.OnCigaretteConfront();});wait=Wait(()=>Received("CIG_OK"),"host cigarette dialogue confirmed",40);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>{InspectionUIManager.Instance.OnInspectItem(Find(root,"SMC_STAMP"));InspectionUIManager.Instance.toggle1.isOn=true;InspectionUIManager.Instance.OnStampConfront();});wait=Wait(()=>Received("STAMP_OK"),"host stamp dialogue confirmed",40);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>PlayerStore.Instance.OnCustomerReported());wait=Wait(()=>Received("POLICE_OK"),"host police routing confirmed",40);while(wait.MoveNext())yield return wait.Current;Signal("FEATURES_DONE");
  }
 }
}
#endif
