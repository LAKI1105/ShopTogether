#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static int scratchRolls,crateMarks;
  static bool ObserveScratchRoll(){scratchRolls++;return true;}
  static bool ObserveCrateMark(){crateMarks++;return true;}
  static void ObservePanelRandomness(){
   var h=new Harmony("shop-together.panel1311-observe");
   var roll=new HarmonyMethod(AccessTools.Method(typeof(RuntimeProbe),"ObserveScratchRoll"));roll.priority=Priority.Last;
   var mark=new HarmonyMethod(AccessTools.Method(typeof(RuntimeProbe),"ObserveCrateMark"));mark.priority=Priority.Last;
   h.Patch(AccessTools.Method(typeof(ScratcherHelper),"SetRandomSymbols"),prefix:roll);
   h.Patch(AccessTools.Method(typeof(PreBuiltItemHelper),"MarkCrateScanned"),prefix:mark);
  }
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
   var extra=Panels1311Host(root,machine);while(extra.MoveNext())yield return extra.Current;
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
   var extra=Panels1311Client(root);while(extra.MoveNext())yield return extra.Current;
  }

  static IEnumerator Panels1311Host(Connection root,GameItem hostItem){
   var e=EmporiumEntry.Instance;
   Require(!NetworkUpgrade.IsUnlocked("RETIRED_CHEMIST"),"chemical unpack requires native confirmation");
   var chem=PanelFixture("common_chemical","PANEL_CHEMBOX");
   var scratch=PanelFixture("scratcher","PANEL_SCRATCH");LotteryHelper.SetSpawnLuck(scratch,HealthData.GetLuck()-10);
   var directive=PanelFixture("exchange_directive","PANEL_DIRECTIVE");
   var newspaper=PanelFixture("newspaper","PANEL_NEWSPAPER");
   var xray=PanelFixture("xray_scanner","PANEL_XRAY");
   var crate=PreBuiltItemHelper.LootCrateMedical();crate.name="PANEL_XCRATE";GeneralHelper.SetItemOwned(crate,true);
   if(e.vendingMachineElement.childItem==null)VendingMachineHelper.UnlockVendingMachine(0);
   if(e.vendingFountainElement.childItem==null)VendingFountainHelper.UnlockVendingFountain();
   var inside=crate.contentWindow.child as GameGridInventory;foreach(var item in new List<GameItem>(inside.childItems))item.Destroy();
   var hiddenTicket=ModHelper.SpawnItem("scratcher");hiddenTicket.modifiedShape=new GridShapeBuilder(hiddenTicket.modifiedShape).SetPosition(0,0).Build();Require(inside.UncheckedAccept(hiddenTicket),"unopened ticket placed inside crate");
   var hiddenTool=ModHelper.SpawnItem("screwdriver");hiddenTool.modifiedShape=new GridShapeBuilder(hiddenTool.modifiedShape).SetPosition(3,0).Build();Require(inside.UncheckedAccept(hiddenTool),"tool placed inside crate");
   Require(GraphUtils.TryAcceptAll(e.invElement,crate)==0,"locked supply crate placed");
   Require(crate.IsGameItemType("SUPPLY_CRATE")&&crate.GetTagReadonly("LOCK_STATUS").GetBool(),"supply crate is locked for native X-ray");
   GenericConfirmationUIManager.Instance.ClosePanel();ScratcherUIManager.Instance.CloseUI();ExchangeUIManager.Instance.ClosePanel();NewsUIManager.Instance.CloseUI();e.HideResponseWindow();
   int inventoryBefore=e.invElement.childItems.Count, startedBefore=PlayerStore.Instance.scratchersStartedTotal;
   ObservePanelRandomness();scratchRolls=0;crateMarks=0;
   root.World.Index();e.Validate(true);Signal("PANELS1311_READY");

   var wait=Wait(()=>Received("CHEM_CANCEL_OPEN"),"guest sees chemical cancel confirmation",90);while(wait.MoveNext())yield return wait.Current;
   Require(!GenericConfirmationUIManager.Instance.panel.activeSelf&&chem.parentInventory!=null,"chemical cancel prompt is only on guest");Signal("CHEM_CANCEL_ALLOWED");
   wait=Wait(()=>!PanelRelay.HasPending(1)&&Received("CHEM_CANCEL_SENT"),"guest cancel clears host confirmation",90);while(wait.MoveNext())yield return wait.Current;
   Require(chem.parentInventory!=null,"cancel does not unpack chemical box");Signal("CHEM_CANCEL_DONE");

   wait=Wait(()=>Received("CHEM_BUSY_OPEN"),"guest opens confirmation before competing control",90);while(wait.MoveNext())yield return wait.Current;
   int chemId=root.World.Id(chem);long busyToken=0;wait=Wait(()=>root.Leases.Claim(0,new[]{chemId},Time.realtimeSinceStartup,out busyToken),"host can claim chemical box while confirmation waits",30);while(wait.MoveNext())yield return wait.Current;Signal("CHEM_BUSY_ALLOWED");
   wait=Wait(()=>!PanelRelay.HasPending(1)&&Received("CHEM_BUSY_SENT"),"busy confirmation processed",90);while(wait.MoveNext())yield return wait.Current;
   Require(chem.parentInventory!=null&&root.Leases.Owns(0,chemId,busyToken,Time.realtimeSinceStartup),"confirmation cannot unpack another player's held box");root.Leases.Release(0,new[]{chemId});Signal("CHEM_BUSY_DONE");

   wait=Wait(()=>Received("CHEM_OPEN"),"guest sees chemical confirmation",90);while(wait.MoveNext())yield return wait.Current;
   Require(!GenericConfirmationUIManager.Instance.panel.activeSelf&&chem.parentInventory!=null,"host has no confirmation and chemical box remains intact");Signal("CHEM_ALLOWED");
   wait=Wait(()=>chem.parentInventory==null,"confirmed chemical box unpacked on host",90);while(wait.MoveNext())yield return wait.Current;
   Require(e.invElement.childItems.Count>inventoryBefore-1,"unpacking produced ingredients in host inventory");Signal("CHEM_DONE");

   wait=Wait(()=>Received("SCRATCH_OPEN"),"guest opened scratch ticket",90);while(wait.MoveNext())yield return wait.Current;
   Require(!ScratcherUIManager.Instance.ui.activeSelf&&scratch.IsTag("SCRATCHER_NEW_TAG"),"host stays out of guest scratch UI and ticket is initially new");Require(scratchRolls==1,"scratch ticket rerolled only once on host");Signal("SCRATCH_ALLOWED");
   wait=Wait(()=>scratch.GetTagReadonly("SCRATCHER_STATE1").GetInt()==1,"guest scratch reaches host",90);while(wait.MoveNext())yield return wait.Current;
   Require(!scratch.IsTag("SCRATCHER_NEW_TAG")&&PlayerStore.Instance.scratchersStartedTotal==startedBefore+1,"host applies first scratch once");Signal("SCRATCH_DONE");

   wait=Wait(()=>Received("DIRECTIVE_OPEN"),"guest opened exchange directive",90);while(wait.MoveNext())yield return wait.Current;
   Require(!ExchangeUIManager.Instance.panel.activeSelf,"guest directive does not open host panel");Signal("DIRECTIVE_DONE");
   wait=Wait(()=>Received("NEWS_OPEN"),"guest opened newspaper",90);while(wait.MoveNext())yield return wait.Current;
   Require(!NewsUIManager.Instance.uiGameObject.activeSelf,"guest newspaper does not open host panel");Signal("NEWS_DONE");

   wait=Wait(()=>Received("XRAY_OPEN"),"guest sees X-ray crate result",90);while(wait.MoveNext())yield return wait.Current;
   Require(crate.IsTag("LOOT_CRATE_SCANNED_TAG")&&crateMarks==1,"supply crate marked scanned once on host");
   Require(!e.responseWindow.IsVisible()&&!e.responseWindowClosable.IsVisible()&&!Interactable.isPaused,"guest X-ray does not display or pause host response window");
   ShowStorage();yield return null;Context(hostItem);Require(MenuOpen,"host can still open an item menu after guest X-ray");ItemContextHandler.current.UpdateReset();Signal("XRAY_DONE");
   wait=Wait(()=>Received("PANELS1311_DONE"),"guest verified new panels",90);while(wait.MoveNext())yield return wait.Current;
  }

  static IEnumerator Panels1311Client(Connection root){
   var wait=Wait(()=>Find(root,"PANEL_CHEMBOX")!=null&&Find(root,"PANEL_SCRATCH")!=null&&Find(root,"PANEL_DIRECTIVE")!=null&&Find(root,"PANEL_NEWSPAPER")!=null&&Find(root,"PANEL_XRAY")!=null&&Find(root,"PANEL_XCRATE")!=null&&Received("PANELS1311_READY"),"new panel fixtures replicated",90);while(wait.MoveNext())yield return wait.Current;
   var chem=Find(root,"PANEL_CHEMBOX");NativeClick(0,()=>chem.ActivateSlotItem());
   wait=Wait(()=>GenericConfirmationUIManager.Instance.panel.activeSelf,"chemical cancel confirmation opens on guest",90);while(wait.MoveNext())yield return wait.Current;Signal("CHEM_CANCEL_OPEN");
   wait=Wait(()=>Received("CHEM_CANCEL_ALLOWED"),"host chemical cancel panel stayed closed",90);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>GenericConfirmationUIManager.Instance.CancelButtonPressed());Signal("CHEM_CANCEL_SENT");
   wait=Wait(()=>Received("CHEM_CANCEL_DONE"),"host kept box after guest cancelled",90);while(wait.MoveNext())yield return wait.Current;root.ReleaseAll();

   NativeClick(0,()=>chem.ActivateSlotItem());wait=Wait(()=>GenericConfirmationUIManager.Instance.panel.activeSelf,"chemical confirmation reopened before contention",90);while(wait.MoveNext())yield return wait.Current;Signal("CHEM_BUSY_OPEN");
   wait=Wait(()=>Received("CHEM_BUSY_ALLOWED"),"host holds box while guest confirmation waits",90);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>GenericConfirmationUIManager.Instance.ConfirmButtonPressed());Signal("CHEM_BUSY_SENT");
   wait=Wait(()=>Received("CHEM_BUSY_DONE"),"host kept control of the held box",90);while(wait.MoveNext())yield return wait.Current;root.ReleaseAll();

   NativeClick(0,()=>chem.ActivateSlotItem());
   wait=Wait(()=>GenericConfirmationUIManager.Instance.panel.activeSelf,"chemical confirmation opens on guest",90);while(wait.MoveNext())yield return wait.Current;Signal("CHEM_OPEN");
   wait=Wait(()=>Received("CHEM_ALLOWED"),"host did not unpack before guest confirmed",90);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>GenericConfirmationUIManager.Instance.ConfirmButtonPressed());
   wait=Wait(()=>Find(root,"PANEL_CHEMBOX")==null&&Received("CHEM_DONE"),"confirmed chemical unpack replicated to guest",90);while(wait.MoveNext())yield return wait.Current;root.ReleaseAll();

   ObservePanelRandomness();scratchRolls=0;crateMarks=0;
   var scratch=Find(root,"PANEL_SCRATCH");NativeClick(0,()=>scratch.ActivateSlotItem());
   wait=Wait(()=>ScratcherUIManager.Instance.ui.activeSelf&&ScratcherUIManager.Instance.currentActiveScratcherItem==scratch,"scratch ticket opens on guest",90);while(wait.MoveNext())yield return wait.Current;Signal("SCRATCH_OPEN");
   wait=Wait(()=>Received("SCRATCH_ALLOWED"),"host scratch panel stayed closed",90);while(wait.MoveNext())yield return wait.Current;Require(scratchRolls==0,"guest did not reroll scratch ticket");
   var input=ScratcherUIManager.Instance.scratchInputs.Find(x=>x.slot==1);Require(input!=null,"native scratch first slot exists");
   var rect=(RectTransform)input.transform;var canvas=input.GetComponentInParent<Canvas>();var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;var bounds=rect.rect;
   input.OnPointerEnter(null);pressed.Add(KeyCode.Mouse0);
   for(int y=2;y<=8;y++)for(int x=2;x<=8;x++){float px=y%2==0?x/10f:(10-x)/10f;pointer=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(new Vector3(bounds.xMin+bounds.width*px,bounds.yMin+bounds.height*y/10f,0)));yield return null;}
   pressed.Remove(KeyCode.Mouse0);input.OnPointerExit(null);
   wait=Wait(()=>ModReflect.Get<bool>(input,"revealed"),"native mouse strokes reveal scratch slot",30);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>scratch.GetTagReadonly("SCRATCHER_STATE1").GetInt()==1&&Received("SCRATCH_DONE"),"scratched cell synced back to guest",90);while(wait.MoveNext())yield return wait.Current;ScratcherUIManager.Instance.CloseUI();root.ReleaseAll();

   var directive=Find(root,"PANEL_DIRECTIVE");NativeClick(0,()=>directive.ActivateSlotItem());wait=Wait(()=>ExchangeUIManager.Instance.panel.activeSelf,"exchange directive opens on guest",90);while(wait.MoveNext())yield return wait.Current;Signal("DIRECTIVE_OPEN");wait=Wait(()=>Received("DIRECTIVE_DONE"),"host directive panel stayed closed",90);while(wait.MoveNext())yield return wait.Current;ExchangeUIManager.Instance.ClosePanel();root.ReleaseAll();
   var newspaper=Find(root,"PANEL_NEWSPAPER");NativeClick(0,()=>newspaper.ActivateSlotItem());wait=Wait(()=>NewsUIManager.Instance.uiGameObject.activeSelf,"newspaper opens on guest",90);while(wait.MoveNext())yield return wait.Current;Signal("NEWS_OPEN");wait=Wait(()=>Received("NEWS_DONE"),"host newspaper stayed closed",90);while(wait.MoveNext())yield return wait.Current;NewsUIManager.Instance.CloseUI();root.ReleaseAll();

   var scanner=Find(root,"PANEL_XRAY");var crate=Find(root,"PANEL_XCRATE");NativeClick(0,()=>scanner.Target(crate));
   wait=Wait(()=>EmporiumEntry.Instance.responseWindowClosable.IsVisible()&&crate.IsTag("LOOT_CRATE_SCANNED_TAG"),"X-ray results and host scan state arrive on guest",90);while(wait.MoveNext())yield return wait.Current;
   Require(crateMarks==0&&scratchRolls==0,"guest neither marked crate nor rerolled its hidden ticket");
   var contents=GraphUtils.FindChildrenType<GameItem>(crate);var results=EmporiumEntry.Instance.responseInventoryClosable.childItems;
   var expected=new Dictionary<string,int>();foreach(var item in contents){int n;expected.TryGetValue(item.identifier,out n);expected[item.identifier]=n+1;}
   foreach(var item in results){int n;if(expected.TryGetValue(item.identifier,out n))expected[item.identifier]=n-1;}
   bool same=contents.Count>0&&results.Count==contents.Count;foreach(var pair in expected)if(pair.Value!=0)same=false;
   Require(same,"X-ray result shows every crate item");Signal("XRAY_OPEN");
   wait=Wait(()=>Received("XRAY_DONE"),"host remained interactive after guest X-ray",90);while(wait.MoveNext())yield return wait.Current;
   Interactable.isPaused=true;TooltipContainer.isPaused=true;FriendGuards.Tick();Require(Interactable.isPaused&&TooltipContainer.isPaused,"visible crate choice keeps world clicks paused");
   EmporiumEntry.Instance.HideResponseWindow();Require(EmporiumEntry.Instance.responseInventoryClosable.childItems.Count==0,"temporary X-ray display is cleared when closed");FriendGuards.Tick();Require(!Interactable.isPaused&&!TooltipContainer.isPaused,"closing crate preview restores world clicks");
   var e=EmporiumEntry.Instance;wait=Wait(()=>e.vendingMachineElement.childItem!=null&&e.vendingFountainElement.childItem!=null,"vending fixtures available after crate preview",90);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>StoreVendingMachine.Instance.OnClick());Require(e.vendingMachineElement.childItem.contentWindow.IsVisible(),"guest opens vending after crate preview");e.vendingMachineElement.childItem.contentWindow.Hide();
   NativeClick(0,()=>StoreVendingFountain.Instance.OnClick());Require(e.vendingFountainElement.childItem.contentWindow.IsVisible(),"guest opens water fountain after crate preview");e.vendingFountainElement.childItem.contentWindow.Hide();Signal("PANELS1311_DONE");
  }
 }
}
#endif
