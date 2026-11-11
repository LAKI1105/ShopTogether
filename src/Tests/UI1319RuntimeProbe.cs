#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  const string RoomWindow="shop_together.connection", LauncherWindow="shop_together.launcher";
  static Button ButtonWithText(CustomUIWindow window,string text){
   if(window==null||!window.IsAlive)return null;
   foreach(var b in window.root.GetComponentsInChildren<Button>(true))foreach(var t in b.GetComponentsInChildren<TMPro.TMP_Text>(true))if(t.text==text)return b;
   return null;
  }
  static Button RoomButton(string token){return ButtonWithText(CustomUIManager.Instance.GetWindow(RoomWindow),Loc.Text(token));}
  static Button LauncherButton(string token){return ButtonWithText(CustomUIManager.Instance.GetWindow(LauncherWindow),Loc.Text(token));}
  static bool Visible(string name){var w=CustomUIManager.Instance.GetWindow(name);return w!=null&&w.IsVisible;}
  static void Click(Button button,string label){Require(button!=null&&button.interactable,label);NativeClick(0,()=>button.onClick.Invoke());}
  static IEnumerator MainMenu1319Host(Connection root){
   var wait=Wait(()=>Visible(LauncherWindow)&&LauncherButton("~SMC060~")!=null,"multiplayer block appears in the main menu",30);while(wait.MoveNext())yield return wait.Current;
   Click(LauncherButton("~SMC060~"),"main-menu multiplayer button exists");
   wait=Wait(()=>RoomButton("~SMC023~")!=null,"main-menu create room button visible",20);while(wait.MoveNext())yield return wait.Current;
   Click(RoomButton("~SMC023~"),"create room from the main menu");
   wait=Wait(()=>root.IsHost&&LauncherButton("~SMC089~")!=null&&LauncherButton("~SMC183~")!=null,"main-menu block reflects the open room",45);while(wait.MoveNext())yield return wait.Current;
   Click(LauncherButton("~SMC089~"),"open room details from the main-menu block");
   wait=Wait(()=>Visible(RoomWindow)&&RoomButton("~SMC184~")!=null,"room details open without creating another host",30);while(wait.MoveNext())yield return wait.Current;
   Require(root.IsHost,"host remains active while room details are shown");Click(RoomButton("~SMC184~"),"close room details");
   Click(LauncherButton("~SMC183~"),"close the room from the main-menu block");
   wait=Wait(()=>!root.IsHost&&LauncherButton("~SMC060~")!=null,"main-menu block returns to its initial state",30);while(wait.MoveNext())yield return wait.Current;
  }
  static int phoneAction;
  static void ArmPhoneChoice(Action action,string text){
   var talk=DialoguePhoneUIManager.Instance;talk.StopAllCoroutines();
   var dialog=new Dialogue().SetText("",text);
   dialog.choices=new List<ValueTuple<GameItem,Dialogue,Action>>{new ValueTuple<GameItem,Dialogue,Action>(null,new Dialogue().SetText("PLAYER","OK"),()=>{phoneAction++;action();})};
   talk.currentDialog=dialog;talk.DisplayClientText(dialog);talk.DisplayChoice();
  }
  static IEnumerator UI1319Host(Connection root){
   var e=EmporiumEntry.Instance;var store=PlayerStore.Instance;store.storeState=PlayerStore.StoreState.CLOSED;store.isAfterhoursIntroduced=true;
   store.appraiserLevel=3;store.isAppraisalIntroduced=true;store.appraisalBlacklist=new List<string>();e.invElement.SetShape(24,24);
   var certificate=AppraisalRankHelper.CreateCertificate(3);certificate.name="UI1319_CERTIFICATE";Require(GraphUtils.TryAcceptAll(e.invElement,certificate)==0,"host places an actual appraisal certificate");
   var backpack=PanelFixture("backpack_large","UI1319_BACKPACK");root.World.Index();e.Validate(true);e.HideResponseWindow();
   var contact=StorePhoneClient.GetWildPhoneClient();contact.phoneState=(StorePhoneClient.PhoneState)2;store.PhoneClientDict[StorePhoneClient.WILD_CLIENT_NUMBER]=contact;
   Signal("UI1319_READY");var wait=Wait(()=>Received("UI_BAG_OPEN"),"client opened its backpack selection",90);while(wait.MoveNext())yield return wait.Current;
   Require(!e.responseWindowClosable.IsVisible()&&backpack.parentInventory!=null,"client backpack choices do not open or move the host bag");Signal("UI_BAG_HOST_OK");
   wait=Wait(()=>Received("UI_BAG_PICKED"),"client selected a backpack and opened the travel map",90);while(wait.MoveNext())yield return wait.Current;
   Require(e.selectedBackpack==null&&backpack.parentInventory!=null,"backpack selection stays local until travel is requested");Signal("UI_CERT_GO");
   wait=Wait(()=>Received("UI_CERT_OPEN"),"client opened appraisal certificate",90);while(wait.MoveNext())yield return wait.Current;
   Require(!Visible("appraisal_items"),"appraisal window remains closed on host");Signal("UI_CERT_HOST_OK");
   wait=Wait(()=>store.appraisalBlacklist!=null&&store.appraisalBlacklist.Contains("hand"),"client checkbox changed the host appraisal settings",90);while(wait.MoveNext())yield return wait.Current;
   Require(!Visible("appraisal_items"),"host appraisal window stays closed when checkbox is changed");Signal("UI_CERT_APPLIED");
   wait=Wait(()=>Received("UI_PHONE_GO"),"client prepared phone",90);while(wait.MoveNext())yield return wait.Current;
   var phone=PhoneUIManager.Instance;wait=Wait(()=>ModReflect.Get<bool>(phone,"isTalking"),"client dial reaches host phone",90);while(wait.MoveNext())yield return wait.Current;
   DialogSettingUIManager.Instance.CloseUI();phoneAction=0;ArmPhoneChoice(()=>DialogSettingUIManager.Instance.OpenUI(),"UI1319_DIALOG_SETTING");Signal("UI_SETTING_CHOICE");
   wait=Wait(()=>phoneAction==1,"client triggered the host dialogue-setting action",90);while(wait.MoveNext())yield return wait.Current;
   Require(!DialogSettingUIManager.Instance.optionPanel.activeSelf,"dialogue settings do not open on host");Signal("UI_SETTING_HOST_OK");
   wait=Wait(()=>Received("UI_SETTING_DONE"),"client received dialogue settings",90);while(wait.MoveNext())yield return wait.Current;
   WildNetworkUI.Close();store.daySinceLastBrowseUpgrade=7;phoneAction=0;ArmPhoneChoice(()=>WildNetworkUI.Open(),"UI1319_WILD");Signal("UI_WILD_CHOICE");
   wait=Wait(()=>phoneAction==1,"client triggered the host Wild action",90);while(wait.MoveNext())yield return wait.Current;
   Require(!WildNetworkUI.IsOpen&&store.daySinceLastBrowseUpgrade==0,"Wild opens only on client while host records browsing");Signal("UI_WILD_HOST_OK");
   wait=Wait(()=>Received("UI_WILD_DONE"),"client received the Wild screen",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("UI_STATUS_OPEN"),"client opened connection status",90);while(wait.MoveNext())yield return wait.Current;
   Require(root.HasPeer(1),"opening status does not disconnect the client");Signal("UI_STATUS_HOST_OK");
   wait=Wait(()=>!root.HasPeer(1)&&Received("UI_LEFT"),"client explicitly left and disconnected",90);while(wait.MoveNext())yield return wait.Current;
  }
  static bool requestCornerClick,observedCornerClick;
  static bool SimulateCornerButton(string __1,ref bool __result){if(requestCornerClick&&__1==Loc.Text("~SMC238~")){requestCornerClick=false;observedCornerClick=true;__result=true;return false;}return true;}
  static void InstallCornerClick(){new Harmony("shop-together.corner1319-probe").Patch(AccessTools.Method(typeof(GUI),"Button",new[]{typeof(Rect),typeof(string)}),prefix:new HarmonyMethod(typeof(RuntimeProbe),"SimulateCornerButton"));}
  static IEnumerator Guard1319Host(Connection root){
   var tracked=Find(root,"SMC_HOST_ITEM");var home=tracked.parentInventory;Signal("GUARD_READY");
   var wait=Wait(()=>Received("GUARD_CHECKED"),"guest tested local preview restrictions",60);while(wait.MoveNext())yield return wait.Current;
   Require(tracked.parentInventory==home&&Find(root,"GUARD_LOCAL_PREVIEW")==null,"client preview cannot change or inject host inventory items");
  }
  static IEnumerator Guard1319Client(Connection root){
   var wait=Wait(()=>Received("GUARD_READY"),"host ready for local preview restrictions",60);while(wait.MoveNext())yield return wait.Current;
   var e=EmporiumEntry.Instance;var tracked=Find(root,"SMC_HOST_ITEM");var home=tracked.parentInventory;
   var preview=ModHelper.SpawnItem("magnifier");Require(preview!=null&&root.World.Id(preview)==0,"untracked local preview can be created");preview.name="GUARD_LOCAL_PREVIEW";
   NativeClick(0,()=>GraphUtils.TryAcceptAll(e.invElement,preview,1));Require(preview.parentInventory==null,"local preview cannot be inserted into a shared inventory");
   NativeClick(0,()=>GraphUtils.TryAcceptAll(e.responseInventoryClosable,tracked,1));Require(tracked.parentInventory==home,"shared item cannot be moved into a local choice inventory");
   preview.Destroy();Signal("GUARD_CHECKED");
  }
  static IEnumerator UI1319Client(Connection root){
   var wait=Wait(()=>Received("UI1319_READY"),"host prepared menu fixtures",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Find(root,"UI1319_BACKPACK")!=null,"backpack synchronized to client",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Find(root,"UI1319_CERTIFICATE")!=null,"appraisal certificate synchronized to client",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>PlayerStore.Instance.appraiserLevel==3,"appraisal rank synchronized to client",90);while(wait.MoveNext())yield return wait.Current;
   var e=EmporiumEntry.Instance;NativeClick(0,()=>StoreResponse.ShowAfterhourResponse());
   wait=Wait(()=>e.responseWindowClosable.IsVisible()&&e.responseInventoryClosable.childItems.Count>=2,"backpack choice window renders local preview items",30);while(wait.MoveNext())yield return wait.Current;
   GameItem choice=null;foreach(var row in e.choices)if(row.Item1!=null&&row.Item1.name.Contains("UI1319_BACKPACK")){choice=row.Item1;break;}
   Require(choice!=null,"backpack appears as a selectable local preview");Signal("UI_BAG_OPEN");wait=Wait(()=>Received("UI_BAG_HOST_OK"),"host unaffected by backpack menu",30);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>choice.ActivateSlotItem());wait=Wait(()=>e.selectedBackpack==Find(root,"UI1319_BACKPACK")&&TransitUI.IsOpen,"choosing a backpack opens the local travel map",30);while(wait.MoveNext())yield return wait.Current;
   Signal("UI_BAG_PICKED");TransitUI.Close();wait=Wait(()=>Received("UI_CERT_GO"),"host kept its backpack",30);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>Find(root,"UI1319_CERTIFICATE").ActivateSlotItem());wait=Wait(()=>Visible("appraisal_items"),"certificate opens the appraisal window on client",90);while(wait.MoveNext())yield return wait.Current;Signal("UI_CERT_OPEN");
   wait=Wait(()=>Received("UI_CERT_HOST_OK"),"host appraisal window stayed closed",30);while(wait.MoveNext())yield return wait.Current;
   var appraisal=CustomUIManager.Instance.GetWindow("appraisal_items");var toggles=appraisal.root.GetComponentsInChildren<Toggle>(true);
   Require(toggles.Length>0&&toggles[0].interactable&&toggles[0].isOn&&AppraisalItemRegistry.GetUnlocked(3)[0].id=="hand","appraisal has an enabled hand checkbox");
   NativeClick(0,()=>toggles[0].isOn=false);wait=Wait(()=>PlayerStore.Instance.appraisalBlacklist!=null&&PlayerStore.Instance.appraisalBlacklist.Contains("hand")&&Received("UI_CERT_APPLIED"),"appraisal checkbox change returns from host",90);while(wait.MoveNext())yield return wait.Current;
   CustomUIManager.Instance.CloseWindow("appraisal_items");Signal("UI_PHONE_GO");NativeClick(0,()=>PhoneUIManager.Instance.OpenUI());yield return new WaitForSecondsRealtime(1);NativeClick(0,()=>PhoneUIManager.Instance.AutoCall(StorePhoneClient.WILD_CLIENT_NUMBER));
   wait=Wait(()=>DialoguePhoneUIManager.Instance.currentDialog!=null&&DialoguePhoneUIManager.Instance.currentDialog.plainText=="UI1319_DIALOG_SETTING"&&Received("UI_SETTING_CHOICE"),"dialogue setting choice arrives",90);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>DialoguePhoneUIManager.Instance.SelectChoice(0));wait=Wait(()=>DialogSettingUIManager.Instance.optionPanel.activeSelf&&Received("UI_SETTING_HOST_OK"),"dialogue settings open on client only",90);while(wait.MoveNext())yield return wait.Current;DialogSettingUIManager.Instance.CloseUI();Signal("UI_SETTING_DONE");
   wait=Wait(()=>DialoguePhoneUIManager.Instance.currentDialog!=null&&DialoguePhoneUIManager.Instance.currentDialog.plainText=="UI1319_WILD"&&Received("UI_WILD_CHOICE"),"Wild choice arrives",90);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>DialoguePhoneUIManager.Instance.SelectChoice(0));wait=Wait(()=>WildNetworkUI.IsOpen&&Received("UI_WILD_HOST_OK"),"Wild screen opens on client only",90);while(wait.MoveNext())yield return wait.Current;
   var wild=CustomUIManager.Instance.GetWindow("wilds_network");var frame=wild==null?null:wild.root.GetComponent<WildNetworkFrame>();Require(frame!=null&&frame.tabBar!=null&&frame.tabBar.childCount>0,"Wild screen contains upgrade tabs");WildNetworkUI.Close();Signal("UI_WILD_DONE");
   InstallCornerClick();requestCornerClick=true;observedCornerClick=false;wait=Wait(()=>observedCornerClick&&Visible(RoomWindow),"top-right connection-status button opens its window",30);while(wait.MoveNext())yield return wait.Current;
   Require(root.IsClient&&root.Ready,"top-right button does not disconnect");Signal("UI_STATUS_OPEN");wait=Wait(()=>Received("UI_STATUS_HOST_OK"),"host still sees connected client",30);while(wait.MoveNext())yield return wait.Current;
   Click(RoomButton("~SMC182~"),"close connection-status window");Require(root.IsClient&&root.Ready,"closing status leaves connection intact");
   requestCornerClick=true;observedCornerClick=false;wait=Wait(()=>observedCornerClick&&Visible(RoomWindow),"connection-status window reopens",30);while(wait.MoveNext())yield return wait.Current;
   Click(RoomButton("~SMC181~"),"explicit leave button exists inside status window");
   wait=Wait(()=>!root.IsClient&&MainMenuUIController.Instance!=null&&EmporiumEntry.Instance==null,"explicit leave returns client to main menu",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Visible(LauncherWindow)&&LauncherButton("~SMC060~")!=null,"client main-menu multiplayer block resets after leaving",30);while(wait.MoveNext())yield return wait.Current;Signal("UI_LEFT");
  }
 }
}
#endif
