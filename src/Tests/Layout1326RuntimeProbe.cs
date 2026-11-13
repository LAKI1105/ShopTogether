#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static MachineryTabs LayoutTabs(GameItem bay) { return AccessTools.Method(typeof(StorageExpansionHelper),"GetTabs").Invoke(null,new object[]{bay}) as MachineryTabs; }
  static GameItem LayoutMaterial(){return ModHelper.SpawnItem("blank_module");}
  static bool LayoutText(PixelWindow window,string text){if(window==null||window.handler==null)return false;foreach(var t in window.handler.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))if(t.text==text)return true;return false;}
  static IEnumerator LayoutHost1326(Connection root) {
   var e=EmporiumEntry.Instance;e.invElement.SetShape(50,50);e.Validate(true);
   var printer=MachineFixture("bottle_printer","LAYOUT1326_PRINTER");var battery=PreBuiltItemHelper.BatteryDisposable(1000);GeneralHelper.SetItemOwned(battery,true);Require(GraphUtils.TryAcceptAll(printer,battery)==0,"host printer powered");
   var box=MachineFixture("toolbox","LAYOUT1326_TOOLBOX");var fridge=MachineFixture("fridge","LAYOUT1326_FRIDGE");var cage=MachineFixture("animal_cage","LAYOUT1326_CAGE");var bay=MachineFixture("storage_bay","LAYOUT1326_BAY");var crate=MachineFixture("sec_box","LAYOUT1326_CRATE");
   VendingMachineHelper.UnlockVendingMachine(0);VendingFountainHelper.UnlockVendingFountain();
   var vending=e.vendingMachineElement.childItem;Require(vending!=null&&e.vendingFountainElement.childItem!=null,"host vending and fountain installed");
   root.World.Index();Signal("LAYOUT1326_VERSION="+Convert.ToBase64String(root.ProbeRejectDifferentGame()));Signal("LAYOUT1326_READY");var wait=Wait(()=>Received("LAYOUT1326_INITIAL"),"guest has initial layouts",90);while(wait.MoveNext())yield return wait.Current;
   for(int i=0;i<3;i++)MachineBottlePrinter.UpgradePrinter(printer,LayoutMaterial());
   var scrap=ModHelper.SpawnItem("scrap_metal");GeneralHelper.SetItemOwned(scrap,true);scrap.Target(box);
   MachineFridge.UpgradeStorage(fridge,LayoutMaterial());AnimalCage.UpgradeSize(cage,LayoutMaterial());
   GameItem core=null;foreach(var item in GraphUtils.FindAllChildrenType<GameItem>(vending))if(item.identifier=="vending_machine_core")core=item;
   Require(core!=null,"vending core installed");VendingMachineHelper.UpgradeCapacity(core,LayoutMaterial());
   StorageExpansionHelper.Expand(bay,ModHelper.SpawnItem("storage_bay_expansion_kit"));var tabs=LayoutTabs(bay);Require(tabs!=null&&tabs.count==3,"host expanded storage bay to three tabs");
   var marker=ModHelper.SpawnItem("magnifier");marker.name="LAYOUT1326_TAB_ITEM";GeneralHelper.SetItemOwned(marker,true);Require(GraphUtils.TryAcceptAll(tabs.GetTabInventory(2),marker)==0,"host put an item on the new storage tab");
   LockHelper.UnlockContainer(crate);Require(!crate.contentWindow.isLocked,"host unlocked sealed crate");
   root.World.Index();Signal("LAYOUT1326_UPGRADED");
   wait=Wait(()=>ChildCount(printer,"bottled_water")==1,"guest prints medium bottle on host",90);while(wait.MoveNext())yield return wait.Current;
   for(int i=3;i<9;i++)MachineBottlePrinter.UpgradePrinter(printer,LayoutMaterial());Signal("LAYOUT1326_LARGE");
   wait=Wait(()=>Received("LAYOUT1326_DRAG"),"guest holds an item before full refresh",90);while(wait.MoveNext())yield return wait.Current;
   root.World.Index();var delta=root.World.Capture(true,0,root.Leases,Time.realtimeSinceStartup);root.World.LastSentVersion=delta.version;
   LargeTransfer.Deliver((type,data)=>root.SendTo(1,type,data),LargeTransfer.Pack(20,System.Text.Encoding.UTF8.GetBytes(WireJson.ToJson(delta))));
   Signal("LAYOUT1326_REFRESH");wait=Wait(()=>Received("LAYOUT1326_FULL_OK"),"guest survived full refresh and retained item",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("LAYOUT1326_CHANGE_DRAG"),"guest holds an item before a value update",90);while(wait.MoveNext())yield return wait.Current;
   var changed=Find(root,"SMC_CLIENT_ITEM");int changedId=root.World.Id(changed);long oldVersion=root.World.Versions[changedId];changed.unitValue+=123;root.World.MarkDirty(changed);
   wait=Wait(()=>root.World.Versions[changedId]>oldVersion,"host item value update committed",90);while(wait.MoveNext())yield return wait.Current;Signal("LAYOUT1326_CHANGED");
   wait=Wait(()=>Received("LAYOUT1326_CHANGE_OK"),"guest canceled stale drag after value update",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("LAYOUT1326_SLEEP_DRAG"),"guest holds an item before the host sleeps",90);while(wait.MoveNext())yield return wait.Current;
   int day=StoreStation.Instance.dayCounter;PlayerStore.Instance.storeState=PlayerStore.StoreState.CLOSED;NativeClick(0,()=>EndOfDayUIManager.Instance.OnEndDayButtonClick());
   wait=Wait(()=>StoreStation.Instance.dayCounter>day&&!StoreUIManager.Instance.savingTMP.gameObject.activeSelf,"host slept and entered the next day",120);while(wait.MoveNext())yield return wait.Current;Signal("LAYOUT1326_SLEPT");
   wait=Wait(()=>Received("LAYOUT1326_DONE"),"guest retained held item after real sleep",120);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator LayoutClient1326(Connection root) {
   GameItem printer=null,box=null,fridge=null,cage=null,bay=null,crate=null;var e=EmporiumEntry.Instance;
   var wait=Wait(()=>(printer=Find(root,"LAYOUT1326_PRINTER"))!=null&&(box=Find(root,"LAYOUT1326_TOOLBOX"))!=null&&(fridge=Find(root,"LAYOUT1326_FRIDGE"))!=null&&(cage=Find(root,"LAYOUT1326_CAGE"))!=null&&(bay=Find(root,"LAYOUT1326_BAY"))!=null&&(crate=Find(root,"LAYOUT1326_CRATE"))!=null&&e.vendingMachineElement.childItem!=null&&e.vendingFountainElement.childItem!=null&&Received("LAYOUT1326_READY"),"all initial host layouts replicated",120);while(wait.MoveNext())yield return wait.Current;
   string mismatch=null;wait=Wait(()=>(mismatch=ReceivePrefix("LAYOUT1326_VERSION="))!=null,"host rejects a simulated different game version",30);while(wait.MoveNext())yield return wait.Current;root.ProbeShowDifferentGame(Convert.FromBase64String(mismatch));Require(root.IsClient&&root.Ready,"simulated mismatch displays the game version and does not disturb the real test link");
   var buttons=MachineButtonPress.Buttons(printer.contentWindow);Require(buttons.Count>=3&&buttons[1].isDisabled&&buttons[2].isDisabled,"medium and large printer buttons start locked");
   int chill=MachineFridge.GetChillZone(fridge).inventoryShape.height,cageHeight=AnimalCage.GetCageInventory(cage).inventoryShape.height,vendingHeight=VendingMachineHelper.GetVendingInventory(e.vendingMachineElement.childItem).inventoryShape.height;
   Require(crate.contentWindow.isLocked,"sealed crate starts locked on guest");Signal("LAYOUT1326_INITIAL");
   wait=Wait(()=>Received("LAYOUT1326_UPGRADED"),"host upgraded items",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>MachineBottlePrinter.GetUpgradeCount(printer)==3&&!buttons[1].isDisabled&&buttons[2].isDisabled&&ToolboxHelper.GetUnlockedSlots(box)==4&&MachineFridge.GetChillZone(fridge).inventoryShape.height==chill+1&&AnimalCage.GetCageInventory(cage).inventoryShape.height==cageHeight+1&&VendingMachineHelper.GetVendingInventory(e.vendingMachineElement.childItem).inventoryShape.height==vendingHeight+1&&LayoutTabs(bay)!=null&&LayoutTabs(bay).count==3&&!crate.contentWindow.isLocked,"guest rebuilt upgraded printer, toolbox, fridge, cage, vending and unlocked crate",120);while(wait.MoveNext())yield return wait.Current;
   string fourth=(string)AccessTools.Method(typeof(ToolboxHelper),"SlotLabel").Invoke(null,new object[]{3});Require(LayoutText(box.contentWindow,fourth),"fourth toolbox slot displays its unlocked label on guest");
   var marker=(GameItem)null;wait=Wait(()=>(marker=Find(root,"LAYOUT1326_TAB_ITEM"))!=null&&marker.parentInventory==LayoutTabs(bay).GetTabInventory(2),"new storage tab and its item applied on guest",120);while(wait.MoveNext())yield return wait.Current;
   Require(root.IsClient&&root.Ready,"guest stayed connected after storage expansion");OpenNativeContents(crate);crate.contentWindow.Hide();MachineClick(printer,1);
   wait=Wait(()=>MachineBottlePrinter.GetUpgradeCount(printer)==9&&!buttons[2].isDisabled&&Received("LAYOUT1326_LARGE"),"large printer button enabled on guest",90);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>StoreVendingMachine.Instance.OnClick());Require(e.vendingMachineElement.childItem.contentWindow.IsVisible(),"guest opens outside vending locally");e.vendingMachineElement.childItem.contentWindow.Hide();
   NativeClick(0,()=>StoreVendingFountain.Instance.OnClick());Require(e.vendingFountainElement.childItem.contentWindow.IsVisible(),"guest opens outside fountain locally");e.vendingFountainElement.childItem.contentWindow.Hide();
   Interactable.isPaused=true;TooltipContainer.isPaused=true;FriendGuards.Tick();Require(!Interactable.isPaused&&!TooltipContainer.isPaused,"stale click pause is released after choice closes");
   ShowStorage();yield return null;var held=Find(root,"SMC_CLIENT_ITEM");int id=root.World.Id(held);BeginDrag(held);Require(ItemMouseDragHandler.current.IsDraggingItem,"guest is dragging before full host update");Signal("LAYOUT1326_DRAG");
   wait=Wait(()=>!ItemMouseDragHandler.current.IsDraggingItem&&root.World.Items.ContainsKey(id)&&root.World.Items[id].parentInventory!=null&&Received("LAYOUT1326_REFRESH"),"full refresh cancels guest drag without losing item",90);while(wait.MoveNext())yield return wait.Current;
   Require(root.Ready,"guest stays connected after full refresh");Signal("LAYOUT1326_FULL_OK");
   // The full-refresh notice has the same text and is deliberately deduplicated for 1.5 seconds.
   yield return new WaitForSecondsRealtime(1.6f);pressed.Clear();ShowStorage();yield return null;held=root.World.Items[id];BeginDrag(held);Require(ItemMouseDragHandler.current.IsDraggingItem,"guest starts drag before a host value change");RemoteActions.LastNote="";Signal("LAYOUT1326_CHANGE_DRAG");
   wait=Wait(()=>!ItemMouseDragHandler.current.IsDraggingItem&&RemoteActions.LastNote==Loc.Text("~SMC269~")&&root.World.Items.ContainsKey(id)&&root.World.Items[id].parentInventory!=null&&Received("LAYOUT1326_CHANGED"),"guest cancels drag after held item value changes",90);while(wait.MoveNext())yield return wait.Current;Signal("LAYOUT1326_CHANGE_OK");
   pressed.Clear();ShowStorage();yield return null;held=root.World.Items[id];BeginDrag(held);Require(ItemMouseDragHandler.current.IsDraggingItem,"guest starts another drag before real host sleep");Signal("LAYOUT1326_SLEEP_DRAG");
   wait=Wait(()=>!ItemMouseDragHandler.current.IsDraggingItem&&root.World.Items.ContainsKey(id)&&root.World.Items[id].parentInventory!=null&&Received("LAYOUT1326_SLEPT"),"real host sleep preserves the guest's dragged item",120);while(wait.MoveNext())yield return wait.Current;
   Require(root.IsClient&&root.Ready,"guest remains connected after host sleep");Signal("LAYOUT1326_DONE");
  }
 }
}
#endif
