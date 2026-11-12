#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static bool sawDiagnostic;
  static void ObserveDiagnostic(byte[] __1){if(__1!=null&&Encoding.UTF8.GetString(__1).Contains("SMC_DIAGNOSTIC_TEST"))sawDiagnostic=true;}
  static void CheckDiagnosticProtocol(){
   Require(ProtocolGuard.Valid(true,true,true,Diagnostics.Message,1)&&ProtocolGuard.Valid(true,true,true,Diagnostics.Message,Diagnostics.Limit),"host accepts bounded authenticated diagnostics");
   Require(!ProtocolGuard.Valid(true,false,false,Diagnostics.Message,1)&&!ProtocolGuard.Valid(true,true,true,Diagnostics.Message,Diagnostics.Limit+1)&&!ProtocolGuard.Valid(false,true,true,Diagnostics.Message,1),"diagnostics reject unauthenticated, oversized and wrong-direction messages");
  }
  static IEnumerator Deals1320Host(Connection root){
   CheckDiagnosticProtocol();var h=new Harmony("shop-together.diagnostics1320-probe");h.Patch(AccessTools.Method(typeof(Diagnostics),"FromFriend"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"ObserveDiagnostic"));sawDiagnostic=false;
   var store=PlayerStore.Instance;EmporiumOption.Instance.autoCallNext=false;StoreShutterButton.Instance.OpenShutter();store.lockNextBellFlag=0;
   if(store.storeClientManager.clientStack.Count<1)store.storeClientManager.GenerateClient();store.TryCallNextClient();
   var wait=Wait(()=>store.currentClientInstance!=null,"customer arrived for trade tests",60);while(wait.MoveNext())yield return wait.Current;
   var customer=CurrentCustomer();PrepareCustomer(customer,1000);customer.clientBuyingTagList.Clear();customer.clientBuyingIdList.Clear();customer.willAcceptItem=item=>item!=null&&item.name=="DEAL_ACCEPT";
   var back=EmporiumEntry.Instance.backInvinvElement;foreach(var item in new List<GameItem>(back.childItems))item.Destroy();
   var accepted=ModHelper.SpawnItem("magnifier");accepted.name="DEAL_ACCEPT";GeneralHelper.SetItemOwned(accepted,true);Require(GraphUtils.TryAcceptAll(back,accepted)==0,"host placed accepted item on counter");
   var rejected=ModHelper.SpawnItem("screwdriver");rejected.name="DEAL_REJECT";GeneralHelper.SetItemOwned(rejected,true);Require(GraphUtils.TryAcceptAll(back,rejected)==0,"host placed rejected item on counter");
   root.World.Index();Require(customer.IsClientBuyingThisItem(accepted,false)&&!customer.IsClientBuyingThisItem(rejected,false),"host-only customer preference accepts and rejects different items");Signal("DEALS_READY");
   wait=Wait(()=>Received("DEALS_JUDGE_OK"),"client received host customer decisions",90);while(wait.MoveNext())yield return wait.Current;
   var offer=new BarterOffer{isFreeReward=true};offer.offerSets.Add(()=>offer.barterTextBody="SMC_OFFER_ONE");offer.offerSets.Add(()=>offer.barterTextBody="SMC_OFFER_TWO");offer.ShowCurrentSet();customer.barterOffer=offer;customer.clientIntent=StoreClient.ClientIntent.BARTER;Signal("BARTER_READY");
   wait=Wait(()=>offer.currentSetIndex==1&&offer.barterTextBody=="SMC_OFFER_TWO","guest arrow changed the host barter offer",90);while(wait.MoveNext())yield return wait.Current;Signal("BARTER_HOST_OK");
   wait=Wait(()=>Received("BARTER_GUEST_OK"),"guest received the new barter offer",90);while(wait.MoveNext())yield return wait.Current;
   var gun=PanelFixture("handmade_pistol","DEAL_GUN");root.World.Index();
   var order=new GunOrderData{tier=2,baseReward=321,dialogText="SMC_GUN_ORDER",isServiceOrder=true,mandatoryRequirements=new List<GunOrderRequirement>{new GunOrderRequirement{id="GUN_CURRENT_FIREPOWER_TAG",value=9,reward=15,isMustBeLargerThan=true}}};
   ModReflect.Set(order,"serviceGun",gun);ModReflect.Set(order,"serviceGunId",gun.identifier);ModReflect.Set(order,"serviceGunCondition",(ItemFeatureList.EquipmentCondition)0);
   customer.barterOffer=null;customer.gunOrderData=order;customer.clientIntent=StoreClient.ClientIntent.GUNSMITH;Signal("GUN_ORDER_READY");
   wait=Wait(()=>Received("GUN_ORDER_OK"),"guest received gun order and host gun reference",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>sawDiagnostic,"host received the bounded diagnostic from guest",90);while(wait.MoveNext())yield return wait.Current;
   Require(root.HasPeer(1),"guest stays connected after diagnostic and large selection refusal");Signal("DIAGNOSTIC_HOST_OK");
   wait=Wait(()=>Received("DIAGNOSTIC_CLIENT_OK"),"guest acknowledged diagnostic while still connected",45);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator Deals1320Client(Connection root){
   CheckDiagnosticProtocol();var config=AccessTools.Field(typeof(Cursors),"scaleSetting").GetValue(null);Require(config!=null,"other-player cursor size setting exists");
   var field=AccessTools.Field(typeof(ConfigEntry<float>),"value");float old=(float)field.GetValue(config);float baseScale=Mathf.Max(1f,Screen.height/1080f);
   try{field.SetValue(config,.5f);Require(Math.Abs(Cursors.Scale-.5f*baseScale)<.001f,"cursor setting permits minimum size");field.SetValue(config,4f);Require(Math.Abs(Cursors.Scale-4f*baseScale)<.001f,"cursor setting permits maximum size");field.SetValue(config,float.NaN);Require(Math.Abs(Cursors.Scale-Cursors.DefaultScale*baseScale)<.001f,"invalid cursor size falls back safely");}finally{field.SetValue(config,old);}
   var wait=Wait(()=>Find(root,"DEAL_ACCEPT")!=null&&Find(root,"DEAL_REJECT")!=null&&CurrentCustomer()!=null&&Received("DEALS_READY"),"customer and counter items synchronized",90);while(wait.MoveNext())yield return wait.Current;
   var accept=Find(root,"DEAL_ACCEPT");var reject=Find(root,"DEAL_REJECT");bool a=false,b=true;
   wait=Wait(()=>CustomerDeals.Answer(CurrentCustomer(),accept,false,out a)&&CustomerDeals.Answer(CurrentCustomer(),reject,false,out b),"host buying decisions reached client",90);while(wait.MoveNext())yield return wait.Current;
   Require(a&&!b&&CurrentCustomer().IsClientBuyingThisItem(accept,false)&&!CurrentCustomer().IsClientBuyingThisItem(reject,false),"client applies host-only acceptance and rejection");Signal("DEALS_JUDGE_OK");
   wait=Wait(()=>CurrentCustomer()!=null&&CurrentCustomer().barterOffer!=null&&CurrentCustomer().barterOffer.OfferSetCount()==2&&CurrentCustomer().barterOffer.barterTextBody=="SMC_OFFER_ONE"&&Received("BARTER_READY"),"host barter offer arrives on client",90);while(wait.MoveNext())yield return wait.Current;
   var offer=CurrentCustomer().barterOffer;Require(offer.IsBarterAcceptable(),"guest sees host's barter acceptability");NegociationUIManager.Instance.OpenUI();NativeClick(0,()=>NegociationUIManager.Instance.OnRightArrowButtonClick());
   wait=Wait(()=>offer.currentSetIndex==1&&offer.barterTextBody=="SMC_OFFER_TWO"&&Received("BARTER_HOST_OK"),"changed barter offer synchronized back to guest",90);while(wait.MoveNext())yield return wait.Current;Signal("BARTER_GUEST_OK");
   wait=Wait(()=>Find(root,"DEAL_GUN")!=null&&CurrentCustomer()!=null&&CurrentCustomer().gunOrderData!=null&&Received("GUN_ORDER_READY"),"gun order arrived from host",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>{var g=CurrentCustomer().gunOrderData;return g!=null&&g.tier==2&&g.baseReward==321&&g.dialogText=="SMC_GUN_ORDER"&&g.mandatoryRequirements!=null&&g.mandatoryRequirements.Count==1&&ModReflect.Get<GameItem>(g,"serviceGun")==Find(root,"DEAL_GUN");},"gun order requirements and service gun match host",90);while(wait.MoveNext())yield return wait.Current;Signal("GUN_ORDER_OK");
   int id=root.World.Id(accept);var many=new int[129];for(int i=0;i<many.Length;i++)many[i]=id;root.Send(new Request{kind=RequestKind.Claim,items=many});
   wait=Wait(()=>ModReflect.Get<string>(root,"status")=="~SMC246~","oversized selection is refused without disconnecting",60);while(wait.MoveNext())yield return wait.Current;
   Diagnostics.Soft("test diagnostic",()=>{throw new InvalidOperationException("SMC_DIAGNOSTIC_TEST");});
   wait=Wait(()=>root.IsClient&&root.Ready&&Received("DIAGNOSTIC_HOST_OK"),"client remains connected after diagnostic report",90);while(wait.MoveNext())yield return wait.Current;Signal("DIAGNOSTIC_CLIENT_OK");
  }
 }
}
#endif
