#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static GameItem PanelFixture(string id,string name){var item=ModHelper.SpawnItem(id);item.name=name;GeneralHelper.SetItemOwned(item,true);Require(GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement,item)==0,"panel fixture placed: "+id);return item;}
  static void OpenNativeContents(GameItem item){NativeClick(0,()=>ItemMouseDoubleClickHandler.current.OpenContentAction(item,Vector2.zero));Require(item.contentWindow!=null&&item.contentWindow.IsVisible(),"native contents opened: "+item.name);}
  static IEnumerator PanelsHost(Connection root){
   var e=EmporiumEntry.Instance;e.invElement.SetShape(36,36);e.Validate(true);
   var machine=PanelFixture("bottle_printer","PANEL_MACHINE");var box=PanelFixture("backpack_large","PANEL_BOX");var scan=PanelFixture("chem_scanner","PANEL_SCAN");var strip=PanelFixture("water_test_strip","PANEL_STRIP");var water=PanelFixture("small_bottled_water","PANEL_WATER");WaterHelper.FillWithPureWater(water);
   ChemScannerUIManager.Instance.panel.SetActive(false);WaterStripUIManager.Instance.Close();
   foreach(var item in new List<GameItem>(e.backInvinvElement.childItems))item.Destroy();e.backInvinvElement.SetShape(16,8);
   for(int i=0;i<2;i++){var item=ModHelper.SpawnItem("magnifier");item.name="PANEL_GROUP_"+i;item.mayStackItemFunc=(a,b)=>false;GeneralHelper.SetItemOwned(item,true);item.modifiedShape=new GridShapeBuilder(item.modifiedShape).SetPosition(2+i*3,2).Build();e.backInvinvElement.UncheckedAccept(item);}
   root.World.Index();e.Validate(true);yield return null;var initialized=Wait(()=>machine.contentWindow!=null&&box.contentWindow!=null,"native machine and container windows initialized",30);while(initialized.MoveNext())yield return initialized.Current;OpenNativeContents(machine);Require(root.Leases.Available(1,root.World.Id(machine),Time.realtimeSinceStartup),"host opening machine does not lock it");Signal("PANELS_READY");
   var wait=Wait(()=>Received("CLIENT_OPEN"),"guest opens same machine",60);while(wait.MoveNext())yield return wait.Current;Require(machine.contentWindow.IsVisible()&&root.Leases.Available(0,root.World.Id(machine),Time.realtimeSinceStartup),"guest opening leaves host machine available");machine.contentWindow.Hide();OpenNativeContents(machine);machine.contentWindow.Hide();OpenNativeContents(box);box.contentWindow.Hide();Require(root.Leases.Available(1,root.World.Id(box),Time.realtimeSinceStartup),"host opening container leaves it available");Signal("OPEN_OK");
   wait=Wait(()=>Received("SCANNER_OPEN"),"guest scanner displayed",60);while(wait.MoveNext())yield return wait.Current;Require(!ChemScannerUIManager.Instance.panel.activeSelf,"guest scanner never opens host panel");Signal("SCANNER_OK");
   wait=Wait(()=>Received("STRIP_OPEN"),"guest strip result displayed",60);while(wait.MoveNext())yield return wait.Current;Require(strip.IsTag("IS_SPENT")&&!WaterStripUIManager.Instance.panel.activeSelf,"strip consumed on host without opening host panel");Signal("STRIP_OK");
   wait=Wait(()=>Received("GROUP_SENT"),"guest boxed selected items",60);while(wait.MoveNext())yield return wait.Current;wait=Wait(()=>ChildCount(box,"magnifier")==2,"both selected whole items moved to target container on host",60);while(wait.MoveNext())yield return wait.Current;Signal("GROUP_OK");
   wait=Wait(()=>Received("PANELS_DONE"),"guest verified panels and group results",60);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator PanelsClient(Connection root){
   var wait=Wait(()=>Find(root,"PANEL_GROUP_1")!=null&&Find(root,"PANEL_WATER")!=null&&Received("PANELS_READY"),"panel fixtures replicated",60);while(wait.MoveNext())yield return wait.Current;
   var machine=Find(root,"PANEL_MACHINE");var box=Find(root,"PANEL_BOX");OpenNativeContents(machine);Require(!root.LocalLeases.ContainsKey(root.World.Id(machine)),"guest opening machine takes no item lease");Signal("CLIENT_OPEN");wait=Wait(()=>Received("OPEN_OK"),"host reopens shared machine",60);while(wait.MoveNext())yield return wait.Current;machine.contentWindow.Hide();OpenNativeContents(box);Require(!root.LocalLeases.ContainsKey(root.World.Id(box)),"guest container takes no item lease");box.contentWindow.Hide();
   var scanner=Find(root,"PANEL_SCAN");var water=Find(root,"PANEL_WATER");NativeClick(0,()=>scanner.Target(water));wait=Wait(()=>ChemScannerUIManager.Instance.panel.activeSelf&&ChemScannerUIManager.Instance.currentScannedItem==water,"native scanner result opens on guest",60);while(wait.MoveNext())yield return wait.Current;Signal("SCANNER_OPEN");wait=Wait(()=>Received("SCANNER_OK"),"host scanner stayed closed",60);while(wait.MoveNext())yield return wait.Current;ChemScannerUIManager.Instance.panel.SetActive(false);root.ReleaseAll();
   var strip=Find(root,"PANEL_STRIP");NativeClick(0,()=>strip.Target(water));wait=Wait(()=>WaterStripUIManager.Instance.panel.activeSelf&&WaterStripUIManager.Instance.currentWaterContainer==water&&strip.IsTag("IS_SPENT"),"native strip result and consumed state arrive on guest",60);while(wait.MoveNext())yield return wait.Current;Signal("STRIP_OPEN");wait=Wait(()=>Received("STRIP_OK"),"host strip stayed closed",60);while(wait.MoveNext())yield return wait.Current;WaterStripUIManager.Instance.Close();root.ReleaseAll();
   ShowStorage();yield return null;RenderUI();var grid=EmporiumEntry.Instance.backInvinvElement;var handler=ItemMultiSelectHandler.current;float size=UISettings.current.gridSize;
   pointer=RectTransformUtility.WorldToScreenPoint(uiCamera,grid.rectTransform.TransformPoint(new Vector3(size,-size,0)));AccessTools.Method(typeof(StoreUIManager),"CheckRaycast").Invoke(StoreUIManager.Instance,null);pressed.Add(KeyCode.Mouse0);handler.UpdatePress(new HashSet<KeyCode>{KeyCode.Mouse0});var before=pointer;pointer=RectTransformUtility.WorldToScreenPoint(uiCamera,grid.rectTransform.TransformPoint(new Vector3(size*8,-size*6,0)));handler.UpdateMouseMove(pointer,pointer-before);pressed.Clear();handler.UpdateRelease(new HashSet<KeyCode>{KeyCode.Mouse0});
   var selected=ModReflect.Get<List<GameItemElement>>(handler,"selectedItems");Require(selected.Count==2,"native rectangle selects both items");pointer=Point(selected[0]);pressed.Add(KeyCode.Mouse0);handler.UpdatePress(new HashSet<KeyCode>{KeyCode.Mouse0});before=pointer;pointer+=new Vector3(30,20,0);handler.UpdateMouseMove(pointer,pointer-before);Require(handler.IsGroupDragging,"native multi-selection drag started");
   wait=Wait(()=>root.LocalLeases.ContainsKey(root.World.Id(selected[0]))&&root.LocalLeases.ContainsKey(root.World.Id(selected[1])),"group item leases granted",60);while(wait.MoveNext())yield return wait.Current;
   before=pointer;pointer=Point(box);handler.UpdateMouseMove(pointer,pointer-before);handler.UpdateUpdate();pressed.Clear();handler.UpdateRelease(new HashSet<KeyCode>{KeyCode.Mouse0});Signal("GROUP_SENT");wait=Wait(()=>ChildCount(box,"magnifier")==2&&Received("GROUP_OK"),"group drag results replicated",60);while(wait.MoveNext())yield return wait.Current;Signal("PANELS_DONE");
  }
 }
}
#endif
