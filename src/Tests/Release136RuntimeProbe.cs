#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static IEnumerator RunExtended(Connection root,bool host){
   string suite=Argument("--smc-suite");
   if(suite=="panels")return host?PanelsHost(root):PanelsClient(root);
   if(suite=="gun"||suite=="death")return host?GunHost(root):GunClient(root);
   if(suite=="join")return host?JoinHostTest(root):JoinClientTest(root);
   if(suite=="outing")return host?OutHost(root):OutClient(root);
   if(suite=="extra")return host?ExtraHost(root,Find(root,"SMC_CLIENT_ITEM")):ExtraClient(root);
   if(suite=="phone")return host?PhoneHost(root):PhoneClient(root);
   if(suite=="machines")return host?MachinesHost(root):MachinesClient(root);
   if(suite=="features")return host?FeaturesHost(root):FeaturesClient(root);
   if(suite=="overflow")return host?OverflowHost(root):OverflowClient(root);
   throw new InvalidOperationException("Unknown suite: "+suite);
  }
  static int phoneChoices;
  
  static void CheckAchievementSetting(){
   var f=AccessTools.Field(typeof(Achievements),"setting");var setting=f.GetValue(null);Require(setting!=null,"achievement option registered in mod configuration");
   var value=AccessTools.Field(typeof(ConfigEntry<bool>),"value");bool old=(bool)value.GetValue(setting);
   try{value.SetValue(setting,false);Require(!Achievements.Enabled,"achievement option disabled for this machine");
    Require(!(bool)AccessTools.Method(typeof(Achievements.UnlockGate),"Prefix").Invoke(null,new object[]{"SMC_TEST_NO_ACHIEVEMENT"}),"disabled multiplayer unlock refused");
    Require(!(bool)AccessTools.Method(typeof(Achievements.CountGate),"Prefix").Invoke(null,new object[]{"SMC_TEST_NO_STAT",99,"SMC_TEST_NO_ACHIEVEMENT"}),"disabled multiplayer progress refused");
   value.SetValue(setting,true); bool host=Connection.Current.IsHost;Require((bool)AccessTools.Method(typeof(Achievements.UnlockGate),"Prefix").Invoke(null,new object[]{"SMC_TEST_NO_ACHIEVEMENT"})==host,"enabled achievement policy allows host and rejects guest local simulation");if(!host){var relay=AccessTools.Field(typeof(Achievements),"relaying");relay.SetValue(null,true);try{Require((bool)AccessTools.Method(typeof(Achievements.UnlockGate),"Prefix").Invoke(null,new object[]{"SMC_TEST_NO_ACHIEVEMENT"}),"enabled guest accepts host-relayed achievement");}finally{relay.SetValue(null,false);}}
   }finally{value.SetValue(setting,old);}
  }
  static IEnumerator PhoneHost(Connection root){
   CheckAchievementSetting();var phone=PhoneUIManager.Instance;phone.CloseUI();
   var contact=StorePhoneClient.GetWildPhoneClient();contact.phoneState=(StorePhoneClient.PhoneState)2;PlayerStore.Instance.PhoneClientDict[StorePhoneClient.WILD_CLIENT_NUMBER]=contact;
   Signal("PHONE_READY");var wait=Wait(()=>ModReflect.Get<bool>(phone,"isTalking"),"client dial executed by native host phone",40);while(wait.MoveNext())yield return wait.Current;
   var group=phone.phonePanel.GetComponent<CanvasGroup>();Require(group!=null&&group.alpha==0&&!group.blocksRaycasts,"guest-only call leaves host phone invisible and noninteractive");
   var talk=DialoguePhoneUIManager.Instance;talk.StopAllCoroutines();
   var dialog=new Dialogue().SetText("","SMC shared phone question");dialog.choices=new List<ValueTuple<GameItem,Dialogue,Action>>{new ValueTuple<GameItem,Dialogue,Action>(null,new Dialogue().SetText("PLAYER","SMC shared answer"),()=>phoneChoices++)};
   talk.currentDialog=dialog;talk.DisplayClientText(dialog);talk.DisplayChoice();Signal("PHONE_CHOICE");
   wait=Wait(()=>phoneChoices==1,"client phone choice executed once on host",40);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>phone.OpenUI());Require(group.alpha==1&&group.blocksRaycasts,"host can reveal ongoing shared call");
   var view=new PhaseState();PhoneShare.Capture(view);Signal("PHONE_SHARED");
   wait=Wait(()=>Received("PHONE_SAME"),"both phone users see same dialogue",40);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>phone.CloseUI());Require(group.alpha==0&&phone.phonePanel.activeSelf,"guest phone remains hidden after host puts it down");Signal("PHONE_DISCONNECT");wait=Wait(()=>!root.HasPeer(1)&&!phone.phonePanel.activeSelf,"last guest disconnect hangs up hidden host phone",40);while(wait.MoveNext())yield return wait.Current;Signal("PHONE_DONE");
  }
  static IEnumerator PhoneClient(Connection root){
   CheckAchievementSetting();var wait=Wait(()=>Received("PHONE_READY"),"phone fixture ready",40);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>PhoneUIManager.Instance.OpenUI());yield return new WaitForSecondsRealtime(1);
   NativeClick(0,()=>PhoneUIManager.Instance.AutoCall(StorePhoneClient.WILD_CLIENT_NUMBER));
   wait=Wait(()=>Received("PHONE_CHOICE"),"host received native dial",40);while(wait.MoveNext())yield return wait.Current;
   var talk=DialoguePhoneUIManager.Instance;wait=Wait(()=>talk.currentDialog!=null&&talk.currentDialog.plainText=="SMC shared phone question"&&talk.currentDialog.choices!=null&&talk.currentDialog.choices.Count==1,"host call choices replicated",40);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>talk.SelectChoice(0));wait=Wait(()=>Received("PHONE_SHARED"),"host also picked up",40);while(wait.MoveNext())yield return wait.Current;
   Require(phoneChoices==0,"phone dialogue result never runs locally on client");Require(PhoneUIManager.Instance.phonePanel.activeSelf,"client stays on shared phone");Signal("PHONE_SAME");
   wait=Wait(()=>Received("PHONE_DISCONNECT"),"host put down shared phone",40);while(wait.MoveNext())yield return wait.Current;root.Stop();wait=Wait(()=>Received("PHONE_DONE"),"phone verification finished",40);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator OverflowHost(Connection root){
   var store=PlayerStore.Instance;store.storeState=PlayerStore.StoreState.CLOSED;store.isAfterhoursIntroduced=true;store.healthData.woundState=0;store.healthData.isWoundedFresh=false;
   Signal("OVERFLOW_GO");var wait=Wait(()=>OutingKits.AnyOut,"overflow client outside",40);while(wait.MoveNext())yield return wait.Current;Kit kit=null;foreach(var k in OutingKits.All)if(k.location!=null){kit=k;break;}
   var loot=ModHelper.SpawnItem("magnifier");loot.name="SMC_OVERFLOW_LOOT";loot.mayStackItemFunc=(x,y)=>false;Require(GraphUtils.TryAcceptAll(kit.ground,loot)==0&&loot.parentInventory==kit.ground,"overflow ground loot placed");
   var blocked=new Dictionary<GameInventory,Func<GameItem,GameInventory,bool>>();foreach(var r in WorldReplica.Roots())if(r.Key<40&&!blocked.ContainsKey(r.Value)){blocked[r.Value]=r.Value.mayInventoryAddItemFunc;r.Value.mayInventoryAddItemFunc=(a,b)=>false;}
   try{Signal("OVERFLOW_RETURN");wait=Wait(()=>kit.location==null,"return with no shop capacity",40);while(wait.MoveNext())yield return wait.Current;Require(kit.HasLeftovers&&kit.owner>0&&loot.parentInventory==kit.ground,"no-capacity return preserves loot and ownership");Require(OutingKits.AnyOut&&DayGuards.Sleep()!=null,"sleep blocked while connected friend has unstored outing items");Signal("OVERFLOW_HELD");wait=Wait(()=>Received("OVERFLOW_BLOCKED"),"another outing blocked until leftovers empty",40);while(wait.MoveNext())yield return wait.Current;}
   finally{foreach(var r in blocked)r.Key.mayInventoryAddItemFunc=r.Value;}
   Signal("OVERFLOW_MOVE");wait=Wait(()=>Received("OVERFLOW_DROPPED"),"client submitted manual drop",40);while(wait.MoveNext())yield return wait.Current;wait=Wait(()=>true&&loot.parentInventory==EmporiumEntry.Instance.backInvinvElement,"leftover dragged into shop and committed by host",40);while(wait.MoveNext())yield return wait.Current;Signal("OVERFLOW_DONE");
  }
  static IEnumerator OverflowClient(Connection root){
   var wait=Wait(()=>Received("OVERFLOW_GO"),"overflow fixture ready",40);while(wait.MoveNext())yield return wait.Current;OutRequest("Go","commissary",null);wait=Wait(()=>OutingKits.Here=="commissary"&&Received("OVERFLOW_RETURN"),"own outing ready",40);while(wait.MoveNext())yield return wait.Current;OutRequest("Back");wait=Wait(()=>OutingKits.Here==null&&Received("OVERFLOW_HELD"),"leftovers retained after return",40);while(wait.MoveNext())yield return wait.Current;
   Require(OutingKits.Mine!=null&&OutingKits.Mine.window.IsVisible(),"leftover window visible in shop");OutRequest("Go","commissary",null);yield return new WaitForSecondsRealtime(1);Require(OutingKits.Here==null,"cannot depart again with leftovers");Signal("OVERFLOW_BLOCKED");wait=Wait(()=>!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy&&Received("OVERFLOW_MOVE"),"shop capacity restored",40);while(wait.MoveNext())yield return wait.Current;
   var loot=Find(root,"SMC_OVERFLOW_LOOT");BeginDrag(loot);Require(ItemMouseDragHandler.current.IsDraggingItem,"leftover native drag started");wait=Wait(()=>root.LocalLeases.ContainsKey(root.World.Id(loot)),"leftover lease granted",30);while(wait.MoveNext())yield return wait.Current;Drop(EmporiumEntry.Instance.backInvinvElement,loot);Signal("OVERFLOW_DROPPED");wait=Wait(()=>Received("OVERFLOW_DONE"),"manual recovery completed",40);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
