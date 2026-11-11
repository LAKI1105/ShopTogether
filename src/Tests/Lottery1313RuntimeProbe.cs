#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static string winningFixture; static int guestLotteryRolls;
  static void CountGuestLotteryRoll(){if(ReplicaSession.ClientStore)guestLotteryRolls++;}
  static bool FixedLotteryDraw(ref string __result){if(winningFixture==null)return true;__result=winningFixture;return false;}
  static IEnumerator Lottery1313Host(Connection root){
   var store=PlayerStore.Instance;var e=EmporiumEntry.Instance;EmporiumOption.Instance.autoCallNext=false;StoreShutterButton.Instance.OpenShutter();store.lockNextBellFlag=0;store.TryCallNextClient();
   var wait=Wait(()=>store.currentClientInstance!=null,"lottery seller arrives",90);while(wait.MoveNext())yield return wait.Current;
   var customer=store.currentClientInstance.GetClientBlueprint();customer.clientIntent=StoreClient.ClientIntent.SELL;customer.isIntroduced=true;customer.isBusinessComplete=false;customer.isRegularSellDisable=false;
   wait=Wait(()=>Received("LOTTERY_GUEST_READY"),"guest lottery randomness observer ready",90);while(wait.MoveNext())yield return wait.Current;
   foreach(var old in new List<GameItem>(e.backInvinvElement.childItems))old.Destroy();
   var ticket=ModHelper.SpawnItem("lottery_ticket");ticket.name="LOTTERY_TEST_TICKET";GeneralHelper.SetItemOwned(ticket,true);Require(GraphUtils.TryAcceptAll(e.backInvinvElement,ticket)==0,"lottery seller's ticket placed on counter");GeneralHelper.SetItemOwned(ticket,false);
   var other=ModHelper.SpawnItem("magnifier");other.name="LOTTERY_TEST_OTHER";GeneralHelper.SetItemOwned(other,true);Require(GraphUtils.TryAcceptAll(e.backInvinvElement,other)==0,"second item placed on counter");GeneralHelper.SetItemOwned(other,false);
   store.playerCash=5000;store.currentNegociatedItem=other;string number=ticket.GetTagReadonly("LOTTERY_NUMBER_TAG").GetString();int cost=(int)ticket.GetNegociatedValue();root.World.Index();Signal("LOTTERY_READY="+Convert.ToBase64String(Encoding.UTF8.GetBytes(number)));
   wait=Wait(()=>GeneralHelper.IsItemOwned(ticket)||GeneralHelper.IsItemOwned(other),"guest purchase reached host",90);while(wait.MoveNext())yield return wait.Current;
   Require(GeneralHelper.IsItemOwned(ticket)&&!GeneralHelper.IsItemOwned(other)&&store.playerCash==5000-cost,"only selected ticket was bought at its host price");
   Require(store.currentNegociatedItem==other,"guest purchase preserves host's own counter selection");Signal("LOTTERY_PURCHASED");
   wait=Wait(()=>Received("LOTTERY_NUMBER_OK"),"guest received purchased ticket and original number",90);while(wait.MoveNext())yield return wait.Current;
   var h=new Harmony("shop-together.lottery1313-probe");h.Patch(AccessTools.Method(typeof(LotteryHelper),"DrawWinningNumberWithLuck"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"FixedLotteryDraw"));winningFixture=number;LotteryHelper.OnDrawWiningNumber();winningFixture=null;
   Require(ticket.GetTagReadonly("LOTTERY_MATCH_COUNT_TAG").GetInt()==6&&LotteryHelper.CanRedeemPrize(ticket),"native host draw marked the test ticket as winning");int cashBefore=store.playerCash;long prize=ticket.unitValue;Signal("LOTTERY_DRAW="+prize);
   wait=Wait(()=>ticket.parentInventory==null&&store.playerCash==cashBefore+prize,"guest redeemed winning ticket exactly once on host",90);while(wait.MoveNext())yield return wait.Current;Signal("LOTTERY_CASH="+store.playerCash);
   wait=Wait(()=>Received("LOTTERY_DONE"),"guest cash matches redeemed prize",90);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator Lottery1313Client(Connection root){
   var observer=new Harmony("shop-together.lottery1313-observer");observer.Patch(AccessTools.Method(typeof(LotteryHelper),"GenerateLotteryTicket"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"CountGuestLotteryRoll"));guestLotteryRolls=0;Signal("LOTTERY_GUEST_READY");
   string encoded=null;var wait=Wait(()=>Find(root,"LOTTERY_TEST_TICKET")!=null&&Find(root,"LOTTERY_TEST_OTHER")!=null&&(encoded=ReceivePrefix("LOTTERY_READY="))!=null&&CurrentCustomer()!=null&&CurrentCustomer().clientIntent==StoreClient.ClientIntent.SELL,"lottery seller and ticket replicated",120);while(wait.MoveNext())yield return wait.Current;
   var number=Encoding.UTF8.GetString(Convert.FromBase64String(encoded));var ticket=Find(root,"LOTTERY_TEST_TICKET");var ui=NegociationUIManager.Instance;ui.OpenUI();yield return null;
   NativeClick(0,()=>AccessTools.Method(typeof(NegociationUIManager),"OnRowClick").Invoke(ui,new object[]{ticket}));Require(ui.currentNegociatedItem==ticket,"guest selected the lottery ticket on the native tablet");NativeClick(0,()=>ui.OnOfferAcceptClick());
   wait=Wait(()=>Received("LOTTERY_PURCHASED"),"host purchased guest's selected ticket",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>GeneralHelper.IsItemOwned(ticket),"guest purchase replicated",90);while(wait.MoveNext())yield return wait.Current;
   Require(ticket.GetTagReadonly("LOTTERY_NUMBER_TAG").GetString()==number&&guestLotteryRolls==0,"guest ticket retains host number without drawing another lottery number");Signal("LOTTERY_NUMBER_OK");
   string value=null;wait=Wait(()=>(value=ReceivePrefix("LOTTERY_DRAW="))!=null,"host completed lottery draw",90);while(wait.MoveNext())yield return wait.Current;long prize=long.Parse(value);
   wait=Wait(()=>ticket.GetTagReadonly("LOTTERY_WINNING_NUMBER_TAG").GetString()==number&&ticket.GetTagReadonly("LOTTERY_MATCH_COUNT_TAG").GetInt()==6&&ticket.unitValue==prize,"guest ticket number, draw and prize match host",90);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>ticket.ActivateSlotItem());value=null;wait=Wait(()=>(value=ReceivePrefix("LOTTERY_CASH="))!=null,"host redeemed ticket",90);while(wait.MoveNext())yield return wait.Current;int cash=int.Parse(value);
   wait=Wait(()=>Find(root,"LOTTERY_TEST_TICKET")==null&&PlayerStore.Instance.playerCash==cash,"guest ticket removal and cash match host",90);while(wait.MoveNext())yield return wait.Current;Signal("LOTTERY_DONE");
  }
 }
}
#endif
