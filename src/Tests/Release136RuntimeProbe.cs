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
   if(suite=="perf1340")return host?PerfHost1340(root):PerfClient1340(root);
   if(suite=="settlement1339")return SettlementPerformance1339(root,host);
   if(suite=="reported1339")return host?ReportedPathsHost1339(root):ReportedPathsClient1339(root);
   if(suite=="release1339")return host?ReleaseHost1339(root):ReleaseClient1339(root);
   if(suite=="outing138")return host?CompletionOutingsHost138(root):CompletionOutingsClient138(root);
   if(suite=="completion138")return host?CompletionHost138(root):CompletionClient138(root);
   if(suite=="authority137")return host?AuthorityHost137(root):AuthorityClient137(root);
   if(suite=="audit135"||suite=="interact135")return host?AuditHost135(root):AuditClient135(root);
   if(suite=="custom135")return host?CustomHost135(root):CustomClient135(root);
   if(suite=="book136")return host?BookHost136(root):BookClient136(root);
   if(suite=="equipment136")return host?EquipmentHost136(root):EquipmentClient136(root);
   if(suite=="overflow136")return host?OverflowHost136(root):OverflowClient136(root);
   if(suite=="cargo135")return host?CargoHost135(root):CargoClient135(root);
   if(suite=="authority134")return host?AuthorityHost134(root):AuthorityClient134(root);
   if(suite=="authority134recipe")return host?AuthorityRecipeHost134(root):AuthorityRecipeClient134(root);
   if(suite=="mouse1327")return host?MouseHost1327(root):MouseClient1327(root);
   if(suite=="trade1327")return host?TradeHost1327(root):TradeClient1327(root);
   if(suite=="stress1327")return host?StressHost1327(root):StressClient1327(root);
   if(suite=="issue1328")return host?IssueHost1328(root):IssueClient1328(root);
   if(suite=="numeric1326")return host?NumericHost1326(root):NumericClient1326(root);
   if(suite=="layout1326")return host?LayoutHost1326(root):LayoutClient1326(root);
   if(suite=="release1321")return host?Release1321Host(root):Release1321Client(root);
   if(suite=="deals1320")return host?Deals1320Host(root):Deals1320Client(root);
   if(suite=="guard1319")return host?Guard1319Host(root):Guard1319Client(root);
   if(suite=="ui1319")return host?UI1319Host(root):UI1319Client(root);
   if(suite=="recovery")return host?Recovery1315Host(root):Recovery1315Client(root);
   if(suite=="lottery")return host?Lottery1313Host(root):Lottery1313Client(root);
   if(suite=="overnight")return host?Overnight1313Host(root):Overnight1313Client(root);
   if(suite=="replica")return host?Replica1313Host(root):Replica1313Client(root);
   if(suite=="customers")return host?Customer1314Host(root):Customer1314Client(root);
   if(suite=="clock")return host?Clock1312Host(root):Clock1312Client(root);
   if(suite=="panels")return host?PanelsHost(root):PanelsClient(root);
   if(suite=="gun"||suite=="death")return host?GunHost(root):GunClient(root);
   if(suite=="extra")return host?ExtraHost(root,Find(root,"SMC_CLIENT_ITEM")):ExtraClient(root);
   if(suite=="phone")return host?PhoneHost(root):PhoneClient(root);
   if(suite=="machines")return host?MachinesHost(root):MachinesClient(root);
   if(suite=="features")return host?FeaturesHost(root):FeaturesClient(root);
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
  static void OutRequest(string action,params object[] args){NativeClick(0,()=>RemoteActions.Forward(RemoteActions.Adapter(action,args.Length),null,args));}
 }
}
#endif
