#if LAN_PROBE
using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static void Confirmation138Observed(){Debug.Log("[SMC NOTE] confirmation result="+(RemoteActions.PendingNote??"none"));}
  static void Notice138Observed(string text){if(text!=null&&text.StartsWith("~SMC"))Debug.Log("[SMC NOTE] action response="+text);}
  static StoreLamp Lamp138() {
   var go=new GameObject("SMC138_LAMP");go.transform.SetSiblingIndex(0);var lamp=go.AddComponent<StoreLamp>();
   lamp.light=new GameObject("Light");lamp.low=new GameObject("Low");lamp.high=new GameObject("High");
   foreach(var child in new[]{lamp.light,lamp.low,lamp.high}){child.transform.SetParent(go.transform);child.SetActive(false);}
   ModReflect.Set(lamp,"clickCooldownTimer",1000f);return lamp;
  }
  static string Call138(Connection root,string name,params object[] args){
   var method=RemoteActions.Adapter(name,args.Length);var call=RemoteActions.Encode(method,null,args);
   string reason=RemoteActions.Execute(root,1,call);return reason??RemoteActions.TakeNote();
  }
  static IEnumerator CompletionHost138(Connection root){
   var e=EmporiumEntry.Instance;var store=PlayerStore.Instance;e.invElement.SetShape(80,80);
   Time.timeScale=0;var lamp=Lamp138();var feed=PanelFixture("livestock_feed_dispenser","C138_FEED");var wrong=PanelFixture("screwdriver","C138_WRONG");
   feed.ModifyTag("STORED_KCAL_TAG",t=>t.SetInt(100));root.World.Index();
   Require(RemoteActions.Find(AccessTools.Method(typeof(VendingMachineHelper),"UnlockVendingMachine"))==null&&RemoteActions.Find(AccessTools.Method(typeof(VendingFountainHelper),"UnlockVendingFountain"))==null,"vending unlock effects cannot be requested independently");
   var bad=RemoteActions.Encode(RemoteActions.Adapter("ToggleService",2),null,new object[]{"none",true});bad.values[1]="garbage";
   Require(RemoteActions.Execute(root,1,bad)=="~SMC171~","invalid wire bool rejected before service mutation");
   Require(Call138(root,"Confront",wrong,0)=="~SMC247~"||RemoteActions.PendingNote=="~SMC346~","inspection without customer or valid issues refuses");
   Require(Call138(root,"ApplyPerk",wrong,"none")=="~SMC317~","wrong item cannot reprogram perks");
   Require(Call138(root,"Discover",wrong,9999,"none")=="~SMC095~","invalid feature discovery returns reason");
   var empty=AccessTools.Method(typeof(MachineFeedDispenser),"EmptyStorage");
   Require(NativeIntentGuards.Check(empty,null,new object[]{wrong})=="~SMC347~","feed operation rejects unrelated item");
   var blend=AccessTools.Method(typeof(MachineFeedDispenser),"Blend");
   Require(NativeIntentGuards.Check(blend,null,new object[]{feed,e.invElement})=="~SMC347~","feed operation rejects unrelated inventory");
   Require(Call138(root,"DebugBellClick","none")=="~SMC351~","debug bell refusal comes from host rules");
   Require(Call138(root,"ReturnHome")=="~SMC124~","return while inside gives explicit reason");
   // Exercise the real machine confirmation, including a mutation while it is open.
   OpenNativeContents(feed);MachineClick(feed,1);Require(GenericConfirmationUIManager.Instance.panel.activeSelf,"host feed clear opens native confirmation");
   feed.ModifyTag("STORED_KCAL_TAG",t=>t.SetInt(150));NativeClick(0,()=>GenericConfirmationUIManager.Instance.ConfirmButtonPressed());
   Require(feed.GetTagReadonly("STORED_KCAL_TAG").GetInt()==150&&RemoteActions.LastNote==Loc.Text("~SMC326~"),"host stale confirmation cannot clear changed feed");GenericConfirmationUIManager.Instance.ClosePanel();
   Signal("C138_READY");var wait=Wait(()=>Received("C138_CONFIRM_OPEN"),"guest opened native feed clear",60);while(wait.MoveNext())yield return wait.Current;
   feed.ModifyTag("STORED_KCAL_TAG",t=>t.SetInt(200));Signal("C138_CHANGED");
   wait=Wait(()=>Received("C138_CONFIRM_REFUSED"),"guest stale clear refused",60);while(wait.MoveNext())yield return wait.Current;
   Require(feed.GetTagReadonly("STORED_KCAL_TAG").GetInt()==200,"stale guest confirmation preserves current feed");Signal("C138_RETRY");
   wait=Wait(()=>feed.GetTagReadonly("STORED_KCAL_TAG").GetInt()==0,"fresh guest confirmation runs native clear",60);while(wait.MoveNext())yield return wait.Current;
   Signal("C138_LAMP_READY");wait=Wait(()=>ModReflect.Get<bool>(lamp,"isBroken"),"four guest lamp clicks run native break on host",60);while(wait.MoveNext())yield return wait.Current;
   Signal("C138_LAMP_BROKEN");wait=Wait(()=>Received("C138_LAMP_DONE"),"lamp result and denial replicated",60);while(wait.MoveNext())yield return wait.Current;
   UnityEngine.Object.Destroy(lamp.gameObject);Time.timeScale=1;
   EmporiumOption.Instance.autoCallNext=false;StoreShutterButton.Instance.OpenShutter();store.lockNextBellFlag=0;
   if(store.storeClientManager.clientStack.Count==0)store.storeClientManager.GenerateClient();store.TryCallNextClient();
   wait=Wait(()=>store.currentClientInstance!=null,"customer arrived for wholesale audit",60);while(wait.MoveNext())yield return wait.Current;
   var customer=CurrentCustomer();TradeCustomer1327(customer,StoreClient.ClientIntent.SELL);TradeClear1327();
   foreach(var old in e.frontInvinvElement.childItems.ToArray())old.Destroy();
   var wholesale=TradeFixture1327("magnifier","C138_WHOLESALE",false);customer.isOfferWholesale=true;customer.wholesaleDiscount=10;store.playerCash=0;root.World.Index();
   RemoteActions.LastNote=null;NativeClick(0,()=>NegociationUIManager.Instance.OnWholesaleClick());
   Require(store.playerCash==0&&!GeneralHelper.IsItemOwned(wholesale)&&RemoteActions.LastNote==Loc.Text("~SMC343~"),"host wholesale refuses insufficient cash before transfer");
   store.playerCash=100000;int total=store.GetWholesalePrice();long held;Require(root.Leases.Claim(2,new[]{root.World.Id(wholesale)},Time.realtimeSinceStartup,out held),"wholesale selection held by another player");
   RemoteActions.LastNote=null;NativeClick(0,()=>NegociationUIManager.Instance.OnWholesaleClick());
   Require(store.playerCash==100000&&!GeneralHelper.IsItemOwned(wholesale)&&RemoteActions.LastNote==Loc.Text("~SMC096~"),"host wholesale refuses held merchandise atomically");root.Leases.Release(2,new[]{root.World.Id(wholesale)});Signal("C138_WHOLESALE_READY");
   wait=Wait(()=>GeneralHelper.IsItemOwned(wholesale),"guest wholesale purchased native merchandise",60);while(wait.MoveNext())yield return wait.Current;
   Require(store.playerCash==100000-total&&!customer.isOfferWholesale,"wholesale native price charged exactly once");
   TradeCustomer1327(customer,StoreClient.ClientIntent.BUY);var bargain=AccessTools.Method(typeof(BargainUIManager),"OfferDiscount");
   Require(NativeIntentGuards.Check(bargain,BargainUIManager.Instance,new object[]{int.MaxValue})=="~SMC349~","bargain percentage must be one of native button values");
   Require(Call138(root,"Confront",wrong,0)=="~SMC247~","owned item cannot be confronted as customer merchandise");
   Require(Call138(root,"Inspect",wrong,1,32)=="~SMC305~","invalid inspection issue bit is rejected before native mutation");
   var estimate=AccessTools.Method(typeof(AppraisalServiceHelper),"OnEstimate");
   Require(NativeIntentGuards.Check(estimate,null,new object[]{wrong,123})=="~SMC247~","appraisal refuses unrelated item and customer intent");
   var delivery=TradeFixture1327("screwdriver","C138_DELIVERY",true);root.World.Index();int delivered=0;
   var finish=new Dialogue().SetEndAction(()=>{delivered++;delivery.Destroy();});
   var choice=new Dialogue().AddCustomChoice(DirectoryMaster.Item("$"),finish,()=>{});var talk=DialogUIManager.Instance;talk.currentDialog=choice;
   Require(root.Leases.Claim(2,new[]{root.World.Id(delivery)},Time.realtimeSinceStartup,out held),"dialogue delivery item held by another player");
   NativeClick(0,()=>talk.SelectChoice(0));Require(talk.currentDialog==choice&&delivered==0,"host dialogue choice waits for occupied counter content");root.Leases.Release(2,new[]{root.World.Id(delivery)});
   NativeClick(0,()=>talk.SelectChoice(0));Require(talk.currentDialog==finish&&DialogueIntent.Committing,"native dialogue accepted its pending delivery");
   Require(!DialogueIntent.RequestAllowed(root,new Request{kind=RequestKind.Claim,items=new[]{root.World.Id(delivery)}})&&!DialogueIntent.CanTouch(root,delivery),"pending native delivery prevents a new drag or edit before consumption");
   finish.ExecuteEndAction();finish.ExecuteEndAction();Require(delivered==1,"native end action consumes once without mod simulation");
   Signal("C138_TRADE_DONE");DialogUIManager.Instance.InterruptDialog();store.DismissCurrentClient(false);
   wait=Wait(()=>store.currentClientInstance==null,"customer leaves before shared outing",60);while(wait.MoveNext())yield return wait.Current;
   var outing=CompletionOutingsHost138(root);while(outing.MoveNext())yield return outing.Current;
  }
  static IEnumerator CompletionOutingsHost138(Connection root){
   var e=EmporiumEntry.Instance;var store=PlayerStore.Instance;Time.timeScale=1;e.invElement.SetShape(80,80);IEnumerator wait;
   var observe=new Harmony("shop-together.outing138-observe");observe.Patch(AccessTools.Method(typeof(PanelRelay),"ConfirmPanel"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"Confirmation138Observed"));

   store.devMode=true;store.isAfterhoursIntroduced=true;store.isStampIntroduced=true;store.skipIntro=true;store.storeState=PlayerStore.StoreState.CLOSED;store.visitLeftTonight=10;
   foreach(string place in new[]{"dumping_ground","commissary","dr_jackson"}){
    Signal("C138_DEPART="+place);
    wait=Wait(()=>OutingKits.HostAt(place)&&!MapUIManager.Instance.IsInTransit,"native travel reached "+place,60);while(wait.MoveNext())yield return wait.Current;
    Signal("C138_ARRIVED="+place);
    if(place=="commissary"){
     var product=store.commissaryData.GetItemData("processed_meat");product.StockCount=2;store.commissaryData.redeemedCredit=100;
     var ticket=PanelFixture("lottery","C138_TICKET");ticket.EnableTag("LOTTERY_IS_WINNING_TAG");ticket.unitValue=437;root.World.Index();
     long lease;Require(root.Leases.Claim(2,new[]{root.World.Id(ticket)},Time.realtimeSinceStartup,out lease),"another player holds lottery redemption item");
     int cash=store.playerCash;RemoteActions.LastNote=null;NativeClick(0,()=>CommissaryUIManager.Instance.OnRedeemAllLottery());
     Require(ticket.parentInventory!=null&&store.playerCash==cash&&RemoteActions.LastNote==Loc.Text("~SMC096~"),"host bulk redemption refuses occupied ticket before payout");root.Leases.Release(2,new[]{root.World.Id(ticket)});
     Signal("C138_SHOP_READY");
     wait=Wait(()=>product.StockCount==1&&store.commissaryData.redeemedCredit==75,"guest commissary buy charged native tokens once",60);while(wait.MoveNext())yield return wait.Current;
     wait=Wait(()=>store.playerCash==cash+437,"guest redeemed native lottery once",60);while(wait.MoveNext())yield return wait.Current;Signal("C138_SHOP_DONE");
    }
    // The ground changed after a warning: refuse the old confirmation, then
    // exercise cancellation, a held item, and a fresh successful return.
    foreach(var inv in OutingKits.Roots())foreach(var old in inv.Value.childItems.ToArray())if(GeneralHelper.IsItemOwned(old)){inv.Value.Expel(old);e.TryAddToPlayerInv(old);}
    var ground=ModHelper.SpawnItem("screwdriver");ground.name="C138_GROUND_"+place;GeneralHelper.SetItemOwned(ground,true);e.afterhourInventory.UncheckedAccept(ground);root.World.Index();Signal("C138_RETURN_READY="+place);
    wait=Wait(()=>Received("C138_RETURN_OPEN="+place),"host ground warning reached guest "+place,60);while(wait.MoveNext())yield return wait.Current;
    string prior=OutingKits.GroundStamp();ground.name+="_CHANGED";Require(prior!=OutingKits.GroundStamp(),"ground state changed after warning "+place);Signal("C138_GROUND_CHANGED="+place);
    wait=Wait(()=>Received("C138_RETURN_STALE="+place),"old ground confirmation refused "+place,60);while(wait.MoveNext())yield return wait.Current;Require(OutingKits.HostAt(place),"stale return preserves location "+place);
    long token;Require(root.Leases.Claim(2,new[]{root.World.Id(ground)},Time.realtimeSinceStartup,out token),"ground lease acquired before return "+place);
    Require(!OutingKits.ReturnAvailable()&&RemoteActions.TakeNote()=="~SMC096~","return protects held ground "+place);root.Leases.Release(2,new[]{root.World.Id(ground)});Signal("C138_RETURN_RETRY="+place);
    wait=Wait(()=>MapUIManager.Instance.Current==null&&!MapUIManager.Instance.IsInTransit,"native return completed "+place,60);while(wait.MoveNext())yield return wait.Current;Signal("C138_HOME="+place);
    wait=Wait(()=>Received("C138_HOME_SEEN="+place),"guest returned to shop "+place,60);while(wait.MoveNext())yield return wait.Current;store.visitLeftTonight=10;store.storeState=PlayerStore.StoreState.CLOSED;
   }
   Signal("C138_DONE");wait=Wait(()=>Received("C138_CLIENT_DONE"),"completion audit acknowledged",60);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator CompletionClient138(Connection root){
   var lamp=Lamp138();bool ready=false;var wait=Wait(()=>(ready=ready||Received("C138_READY"))&&Find(root,"C138_FEED")!=null,"completion fixtures synchronized",60);while(wait.MoveNext())yield return wait.Current;
   var feed=Find(root,"C138_FEED");MachineClick(feed,1);wait=Wait(()=>GenericConfirmationUIManager.Instance.panel.activeSelf,"guest feed confirmation opens",60);while(wait.MoveNext())yield return wait.Current;Signal("C138_CONFIRM_OPEN");
   wait=Wait(()=>Received("C138_CHANGED"),"host feed changed",60);while(wait.MoveNext())yield return wait.Current;RemoteActions.LastNote=null;NativeClick(0,()=>GenericConfirmationUIManager.Instance.ConfirmButtonPressed());
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC326~"),"guest changed-content confirmation receives reason",60);while(wait.MoveNext())yield return wait.Current;Signal("C138_CONFIRM_REFUSED");
   wait=Wait(()=>Received("C138_RETRY"),"fresh clear ready",60);while(wait.MoveNext())yield return wait.Current;MachineClick(feed,1);wait=Wait(()=>GenericConfirmationUIManager.Instance.panel.activeSelf,"fresh native clear confirmation",60);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>GenericConfirmationUIManager.Instance.ConfirmButtonPressed());
   wait=Wait(()=>Received("C138_LAMP_READY"),"lamp ready",60);while(wait.MoveNext())yield return wait.Current;
   for(int i=0;i<4;i++){NativeClick(0,()=>lamp.OnClick());yield return null;}
   bool broken=false;wait=Wait(()=>(broken=broken||Received("C138_LAMP_BROKEN"))&&ModReflect.Get<bool>(lamp,"isBroken")&&!lamp.light.activeSelf,"native lamp broken and lights replicated",60);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.LastNote=null;NativeClick(0,()=>lamp.OnClick());wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC350~"),"broken lamp gives host refusal",60);while(wait.MoveNext())yield return wait.Current;Signal("C138_LAMP_DONE");UnityEngine.Object.Destroy(lamp.gameObject);
   bool trade=false;wait=Wait(()=>(trade=trade||Received("C138_WHOLESALE_READY"))&&Find(root,"C138_WHOLESALE")!=null&&CurrentCustomer()!=null,"wholesale fixture synchronized",60);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>NegociationUIManager.Instance.OnWholesaleClick());wait=Wait(()=>Received("C138_TRADE_DONE"),"host wholesale and appraisal audit complete",60);while(wait.MoveNext())yield return wait.Current;
   var outing=CompletionOutingsClient138(root);while(outing.MoveNext())yield return outing.Current;
  }
  static IEnumerator CompletionOutingsClient138(Connection root){
   IEnumerator wait;
   var observe=new Harmony("shop-together.outing138-note");observe.Patch(AccessTools.Method(typeof(RemoteActions),"Notice"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"Notice138Observed"));
   foreach(string place in new[]{"dumping_ground","commissary","dr_jackson"}){
    wait=Wait(()=>Received("C138_DEPART="+place),"departure ready "+place,60);while(wait.MoveNext())yield return wait.Current;
    NativeClick(0,()=>FriendOuting.Depart(AfterhourLocation.Get(place)));
    bool arrived=false;wait=Wait(()=>(arrived=arrived||Received("C138_ARRIVED="+place))&&OutingKits.Joined==place,"guest native scene followed "+place,60);while(wait.MoveNext())yield return wait.Current;
    if(place=="commissary"){
     bool shop=false;wait=Wait(()=>(shop=shop||Received("C138_SHOP_READY"))&&Find(root,"C138_TICKET")!=null&&PlayerStore.Instance.commissaryData.redeemedCredit==100,"commissary data arrived",60);while(wait.MoveNext())yield return wait.Current;
     NativeClick(0,()=>PlayerStore.Instance.commissaryData.BuyItem("processed_meat"));NativeClick(0,()=>CommissaryUIManager.Instance.OnRedeemAllLottery());
     wait=Wait(()=>Received("C138_SHOP_DONE"),"host commissary trade completed",60);while(wait.MoveNext())yield return wait.Current;
    }
    wait=Wait(()=>Received("C138_RETURN_READY="+place),"return ready "+place,60);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>MapUIManager.Instance.Return());
    wait=Wait(()=>GenericConfirmationUIManager.Instance.panel.activeSelf,"host ground warning shown "+place,60);while(wait.MoveNext())yield return wait.Current;Signal("C138_RETURN_OPEN="+place);
    wait=Wait(()=>Received("C138_GROUND_CHANGED="+place),"ground mutation ready "+place,60);while(wait.MoveNext())yield return wait.Current;RemoteActions.LastNote=null;NativeClick(0,()=>GenericConfirmationUIManager.Instance.ConfirmButtonPressed());
    wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC326~"),"changed ground receives host refusal "+place,60);while(wait.MoveNext())yield return wait.Current;Signal("C138_RETURN_STALE="+place);
    wait=Wait(()=>Received("C138_RETURN_RETRY="+place),"fresh return ready "+place,60);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>MapUIManager.Instance.Return());wait=Wait(()=>GenericConfirmationUIManager.Instance.panel.activeSelf,"fresh ground confirmation "+place,60);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>GenericConfirmationUIManager.Instance.ConfirmButtonPressed());
    bool home=false;wait=Wait(()=>(home=home||Received("C138_HOME="+place))&&OutingKits.Joined==null,"guest home view synchronized "+place,60);while(wait.MoveNext())yield return wait.Current;Signal("C138_HOME_SEEN="+place);
   }
   wait=Wait(()=>Received("C138_DONE"),"host final completion",60);while(wait.MoveNext())yield return wait.Current;Signal("C138_CLIENT_DONE");
  }
 }
}
#endif
