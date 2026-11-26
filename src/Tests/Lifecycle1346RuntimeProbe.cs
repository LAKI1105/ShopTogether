#if LAN_PROBE
using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static void ObserveLoader1346(Connection root){
   var loader=ModLoader.Instance;Require(loader!=null,"native mod loader exists during play");
   int reportsAtQuit=-1;
   Application.quitting+=()=>{
    reportsAtQuit=IssueReport.Count;
    if(loader==null)Debug.LogError("[SMC FAIL] loader disappeared before application quit");
    else Debug.Log("[SMC LIFETIME] same native loader still alive at application quit");
   };
   // Observe the original game's real OnDestroy log, without patching or destroying
   // the loader. This callback survives the mod's normal shutdown/unpatch phase.
   Application.logMessageReceived+=(message,stack,type)=>{
    if(!message.StartsWith("[ModLoader] The mod loader object was destroyed",StringComparison.Ordinal))return;
    bool session=root!=null&&(root.IsHost||root.IsClient);
    Debug.Log("[SMC LIFETIME] native loader destruction; quitting="+Diagnostics.Quitting+" sessionStillActive="+session);
    if(reportsAtQuit>=0&&Diagnostics.Quitting&&IssueReport.Count==reportsAtQuit)
     Debug.Log("[SMC LIFETIME PASS] normal native loader destruction did not add a gameplay report");
    else Debug.LogError("[SMC FAIL] loader destruction was premature or added a shutdown report");
   };
  }
  static IEnumerator PaperLifecycle1346(string name){
   var canvas=StoreUIManager.Instance.paperUICanvasObject;
   var panels=canvas.GetComponentsInChildren<StoreUI>(true);
   var selected=Array.Find(panels,p=>p.isPaperUI&&p.gameObject.name==name);
   Require(selected!=null,"real native paper panel available: "+name);
   int errors=0;Application.LogCallback observe=(message,stack,type)=>{if(message.Contains("Cannot change the sibling position"))errors++;};
   Application.logMessageReceived+=observe;
   bool visible=selected.gameObject.activeSelf;bool canvasVisible=canvas.activeSelf;
   try{
    canvas.SetActive(true);selected.gameObject.SetActive(true);yield return null;yield return null;
    canvas.SetActive(false);yield return null;yield return null;
    canvas.SetActive(true);selected.gameObject.SetActive(true);yield return null;yield return null;
    Require(selected.transform.GetSiblingIndex()==selected.transform.parent.childCount-1,"opened paper panel retains front ordering after lifecycle callback");
    selected.gameObject.SetActive(false);yield return null;yield return null;
    Require(selected.transform.GetSiblingIndex()==0,"closed paper panel retains back ordering after lifecycle callback");
    Require(errors==0,"native parent canvas enable and disable produced no sibling errors");
   }finally{
    selected.gameObject.SetActive(visible);canvas.SetActive(canvasVisible);Application.logMessageReceived-=observe;
   }
  }
  static bool entered1346,cleared1346;
  static void CaptureDeparture1346(){
   var e=EmporiumEntry.Instance;entered1346=true;
   cleared1346=!Interactable.isPaused&&!TooltipContainer.isPaused&&!e.responseWindowClosable.IsVisible()&&e.responseInventoryClosable.childItems.Count==0;
  }
  static IEnumerator Lifecycle1346(Connection root,bool host){
   var loader=ModLoader.Instance;ObserveLoader1346(root);
   foreach(string name in new[]{"_RecorderPanel","NegotiationTablet"}){var run=PaperLifecycle1346(name);while(run.MoveNext())yield return run.Current;}
   if(host){
    var wait=Wait(()=>Received("LIFE46_CHOICE"),"friend has open backpack choices",90);while(wait.MoveNext())yield return wait.Current;
    var store=PlayerStore.Instance;store.devMode=true;store.isAfterhoursIntroduced=true;store.storeState=PlayerStore.StoreState.CLOSED;store.visitLeftTonight=10;
    NativeClick(0,()=>MapUIManager.Instance.Travel(AfterhourLocation.Get("dumping_ground")));
    wait=Wait(()=>Received("LIFE46_MENU"),"friend returned to menu with the same loader",90);while(wait.MoveNext())yield return wait.Current;
    Require(ModLoader.Instance==loader,"host loader survives peer departure");
   }else{
    new Harmony("shop-together.lifecycle1346-probe").Patch(AccessTools.Method(typeof(OutingKits),"Enter"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"CaptureDeparture1346"));
    FriendOuting.ChooseBackpack();Require(EmporiumEntry.Instance.responseWindowClosable.IsVisible()&&Interactable.isPaused,"native closable backpack choice pauses world input");Signal("LIFE46_CHOICE");
    var arrived=Wait(()=>entered1346,"friend applied host departure",90);while(arrived.MoveNext())yield return arrived.Current;
    Require(cleared1346,"departure clears closable choices and input pause before watchdog tick");
    root.LeaveClientToMenu();var wait=Wait(()=>MainMenuUIController.Instance!=null,"friend leaves to native main menu",60);while(wait.MoveNext())yield return wait.Current;
    Require(ModLoader.Instance==loader,"scene change preserves the native persistent loader");Signal("LIFE46_MENU");
   }
  }
 }
}
#endif
