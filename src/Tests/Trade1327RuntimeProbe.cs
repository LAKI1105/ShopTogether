#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static GameItem TradeFixture1327(string id,string name,bool owned){
   var item=ModHelper.SpawnItem(id);item.name=name;item.mayStackItemFunc=(a,b)=>false;GeneralHelper.SetItemOwned(item,true);
   if(GraphUtils.TryAcceptAll(EmporiumEntry.Instance.backInvinvElement,item)!=0)throw new InvalidOperationException("Cannot place trade fixture "+name);
   GeneralHelper.SetItemOwned(item,owned);return item;
  }
  static void TradeClear1327(){foreach(var item in new List<GameItem>(EmporiumEntry.Instance.backInvinvElement.childItems))item.Destroy();}
  static void TradeCustomer1327(StoreClient customer,StoreClient.ClientIntent intent){
   DialogUIManager.Instance.InterruptDialog();customer.clientIntent=intent;customer.isIntroduced=true;customer.isBusinessComplete=false;customer.isRegularSellDisable=false;customer.acceptDealDialogue=null;
  }
  static IEnumerator TradeTap1327(bool? interactable,string label){
   var ui=NegociationUIManager.Instance;ui.OpenUI();yield return null;Canvas.ForceUpdateCanvases();
   TabletButton tablet=null;var wait=Wait(()=>{tablet=ModReflect.Get<TabletButton>(ui,"primaryButton");return tablet!=null&&tablet.button!=null&&tablet.button.gameObject.activeInHierarchy;},label+" tablet button appears",45);while(wait.MoveNext())yield return wait.Current;
   if(interactable.HasValue)Require(tablet.button.interactable==interactable.Value,label+" tablet button has expected state");
   var click=WindowMouseAt(ButtonPoint(tablet.button),label);while(click.MoveNext())yield return click.Current;
  }
  static IEnumerator TradeHost1327(Connection root){
   var store=PlayerStore.Instance;var e=EmporiumEntry.Instance;EmporiumOption.Instance.autoCallNext=false;StoreShutterButton.Instance.OpenShutter();store.lockNextBellFlag=0;
   if(store.storeClientManager.clientStack.Count==0)store.storeClientManager.GenerateClient();store.TryCallNextClient();
   var wait=Wait(()=>store.currentClientInstance!=null,"customer arrived for real-input trade checks",90);while(wait.MoveNext())yield return wait.Current;
   var customer=store.currentClientInstance.GetClientBlueprint();TradeCustomer1327(customer,StoreClient.ClientIntent.SELL);customer.isIntroduced=false;TradeClear1327();
   var first=TradeFixture1327("magnifier","TRADE1327_BUY_A",false);var second=TradeFixture1327("screwdriver","TRADE1327_BUY_B",false);store.playerCash=0;root.World.Index();Signal("TRADE1327_TALK");
   wait=Wait(()=>Received("TRADE1327_TALK_OK"),"guest saw customer-still-talking notice",90);while(wait.MoveNext())yield return wait.Current;Require(!GeneralHelper.IsItemOwned(first)&&!GeneralHelper.IsItemOwned(second),"talking customer did not sell items");
   customer.isIntroduced=true;Signal("TRADE1327_POOR");wait=Wait(()=>Received("TRADE1327_POOR_OK"),"guest saw insufficient-funds notice",90);while(wait.MoveNext())yield return wait.Current;Require(store.playerCash==0&&!GeneralHelper.IsItemOwned(first)&&!GeneralHelper.IsItemOwned(second),"insufficient funds did not buy items");
   store.playerCash=100000;Signal("TRADE1327_BUY");wait=Wait(()=>GeneralHelper.IsItemOwned(first)&&GeneralHelper.IsItemOwned(second)&&store.playerCash<100000,"guest purchased all customer items on host",120);while(wait.MoveNext())yield return wait.Current;Signal("TRADE1327_BOUGHT");
   wait=Wait(()=>Received("TRADE1327_BOUGHT_OK"),"guest saw the completed purchase",90);while(wait.MoveNext())yield return wait.Current;
   TradeClear1327();TradeCustomer1327(customer,StoreClient.ClientIntent.BUY);customer.useClientBudget=true;customer.OverrideBudget(100000);customer.clientBuyingTagList.Clear();customer.clientBuyingTagList.Add("ALL_TYPE");
   var sellA=TradeFixture1327("magnifier","TRADE1327_SELL_A",true);var sellB=TradeFixture1327("screwdriver","TRADE1327_SELL_B",true);int cash=store.playerCash;root.World.Index();Signal("TRADE1327_SELL");
   wait=Wait(()=>store.playerCash>cash&&sellA.parentInventory!=e.backInvinvElement&&sellB.parentInventory!=e.backInvinvElement,"guest sold all owned counter items on host",120);while(wait.MoveNext())yield return wait.Current;Signal("TRADE1327_SOLD");
   wait=Wait(()=>Received("TRADE1327_SOLD_OK"),"guest saw completed sale",90);while(wait.MoveNext())yield return wait.Current;
   TradeClear1327();TradeCustomer1327(customer,StoreClient.ClientIntent.BARTER);customer.barterOffer=new BarterOffer{isFreeReward=true};customer.barterOffer.offerSets.Add(()=>customer.barterOffer.barterTextBody="TRADE1327_BARTER");customer.barterOffer.ShowCurrentSet();customer.acceptDealDialogue=new Dialogue().SetText("","TRADE1327_ACCEPTED");
   var held=TradeFixture1327("magnifier","TRADE1327_HELD",true);store.currentNegociatedItem=held;root.World.Index();ShowStorage();e.backInvWindow.Show();e.backInvWindow.ToFront(true);e.Validate(true);yield return null;BeginDrag(held);
   int id=root.World.Id(held);Require(ItemMouseDragHandler.current.IsDraggingItem&&root.LocalLeases.ContainsKey(id),"host holds the actual counter item before guest barter");Signal("TRADE1327_HELD");
   wait=Wait(()=>Received("TRADE1327_HELD_OK"),"guest saw held-item refusal",120);while(wait.MoveNext())yield return wait.Current;Require(!customer.isBusinessComplete&&ItemMouseDragHandler.current.IsDraggingItem,"refused barter preserves host's held item");
   ItemMouseDragHandler.current.Cancel();pressed.Clear();root.ReleaseAll();Signal("TRADE1327_RELEASED");
   wait=Wait(()=>customer.isBusinessComplete,"guest accepted barter on host",120);while(wait.MoveNext())yield return wait.Current;Signal("TRADE1327_BARTER_OK");
  }
  static IEnumerator TradeClient1327(Connection root){
   var wait=Wait(()=>Find(root,"TRADE1327_BUY_A")!=null&&Find(root,"TRADE1327_BUY_B")!=null&&CurrentCustomer()!=null&&CurrentCustomer().clientIntent==StoreClient.ClientIntent.SELL&&!CurrentCustomer().isIntroduced&&Received("TRADE1327_TALK"),"speaking customer and items replicated",120);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.LastNote="";var tap=TradeTap1327(null,"customer still speaking");while(tap.MoveNext())yield return tap.Current;
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC295~"),"guest sees customer-still-talking reason",60);while(wait.MoveNext())yield return wait.Current;Signal("TRADE1327_TALK_OK");
   wait=Wait(()=>CurrentCustomer()!=null&&CurrentCustomer().isIntroduced&&Received("TRADE1327_POOR"),"customer finished speaking",90);while(wait.MoveNext())yield return wait.Current;RemoteActions.LastNote="";tap=TradeTap1327(null,"insufficient funds");while(tap.MoveNext())yield return tap.Current;
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC296~"),"guest sees insufficient-funds reason",60);while(wait.MoveNext())yield return wait.Current;Signal("TRADE1327_POOR_OK");
   wait=Wait(()=>PlayerStore.Instance.playerCash==100000&&Received("TRADE1327_BUY"),"host funds replicated",90);while(wait.MoveNext())yield return wait.Current;tap=TradeTap1327(true,"buy all");while(tap.MoveNext())yield return tap.Current;
   wait=Wait(()=>Received("TRADE1327_BOUGHT")&&GeneralHelper.IsItemOwned(Find(root,"TRADE1327_BUY_A"))&&GeneralHelper.IsItemOwned(Find(root,"TRADE1327_BUY_B")),"guest sees both purchases",120);while(wait.MoveNext())yield return wait.Current;Signal("TRADE1327_BOUGHT_OK");
   wait=Wait(()=>Find(root,"TRADE1327_SELL_A")!=null&&Find(root,"TRADE1327_SELL_B")!=null&&CurrentCustomer()!=null&&CurrentCustomer().clientIntent==StoreClient.ClientIntent.BUY&&Received("TRADE1327_SELL"),"buyer and items replicated",120);while(wait.MoveNext())yield return wait.Current;tap=TradeTap1327(true,"sell all");while(tap.MoveNext())yield return tap.Current;
   wait=Wait(()=>Received("TRADE1327_SOLD"),"host completed sale",120);while(wait.MoveNext())yield return wait.Current;Signal("TRADE1327_SOLD_OK");
   wait=Wait(()=>CurrentCustomer()!=null&&CurrentCustomer().clientIntent==StoreClient.ClientIntent.BARTER&&CurrentCustomer().barterOffer!=null&&Find(root,"TRADE1327_HELD")!=null&&Received("TRADE1327_HELD"),"barter and held counter item replicated",120);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.LastNote="";tap=TradeTap1327(true,"barter while host holds an item");while(tap.MoveNext())yield return tap.Current;wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC290~"),"guest sees held-item refusal",90);while(wait.MoveNext())yield return wait.Current;Signal("TRADE1327_HELD_OK");
   wait=Wait(()=>Received("TRADE1327_RELEASED"),"host released counter item",90);while(wait.MoveNext())yield return wait.Current;tap=TradeTap1327(true,"accept barter offer");while(tap.MoveNext())yield return tap.Current;
   wait=Wait(()=>Received("TRADE1327_BARTER_OK"),"host accepted the guest barter",120);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
