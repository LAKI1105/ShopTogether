#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static bool fixedMarkup;
  static bool CustomerRandom(ref int __result){if(!fixedMarkup)return true;__result=0;return false;}
  static StoreClient CurrentCustomer(){var p=PlayerStore.Instance;return p!=null&&p.currentClientInstance!=null?p.currentClientInstance.GetClientBlueprint():null;}
  static NegotiationTabletFrame TradeFrame(){return ModReflect.Get<NegotiationTabletFrame>(NegociationUIManager.Instance,"frame");}
  static bool BudgetVisible(int amount){var f=TradeFrame();if(f==null||f.footer==null)return false;string value=TabletUI.Credits(amount);foreach(var t in f.footer.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))if(t.text!=null&&t.text.Contains(value))return true;return false;}
  static void SendCount(string name,StoreClientCounter c){int count=PlayerStore.Instance.storeClientManager.clientStack.Count;Signal(name+"="+count+"|"+Convert.ToBase64String(Encoding.UTF8.GetBytes(c.GetTooltip())));}
  static IEnumerator CheckCount(string name,StoreClientCounter c){
   string data=null;var wait=Wait(()=>(data=ReceivePrefix(name+"="))!=null,"host "+name+" received",90);while(wait.MoveNext())yield return wait.Current;
   var pair=data.Split('|');int count=int.Parse(pair[0]);string tooltip=Encoding.UTF8.GetString(Convert.FromBase64String(pair[1]));
   wait=Wait(()=>StorePresentation.ClientsLeft==count&&c.clientCounterTMP.text==count.ToString(),"guest "+name+" counter matches host",90);while(wait.MoveNext())yield return wait.Current;
   var manager=PlayerStore.Instance.storeClientManager;var original=manager.clientStack;Require(c.GetTooltip()==tooltip&&ReferenceEquals(original,manager.clientStack),"guest "+name+" tooltip matches host and preserves local queue");
  }
  static void PrepareCustomer(StoreClient c,int budget){c.clientIntent=StoreClient.ClientIntent.BUY;c.isIntroduced=true;c.isBusinessComplete=false;c.isAug=false;c.isSecurity=false;c.alwaysRejectBadBargain=false;c.useClientBudget=true;c.OverrideBudget(budget);c.clientBuyingTagList.Clear();c.clientBuyingTagList.Add("ALL_TYPE");}
  static IEnumerator Customer1314Host(Connection root){
   var store=PlayerStore.Instance;EmporiumOption.Instance.autoCallNext=false;StoreShutterButton.Instance.OpenShutter();store.lockNextBellFlag=0;
   var manager=store.storeClientManager;if(manager.clientStack.Count<2)manager.GenerateClient();Require(manager.clientStack.Count>=2,"shop has at least two queued customers");
   var counter=UnityEngine.Object.FindObjectOfType<StoreClientCounter>();Require(counter!=null&&counter.clientCounterTMP!=null,"host customer counter exists");
   int initial=manager.clientStack.Count;SendCount("COUNT_INITIAL",counter);var wait=Wait(()=>Received("COUNT_INITIAL_OK"),"guest checked initial queue",90);while(wait.MoveNext())yield return wait.Current;
   store.TryCallNextClient();wait=Wait(()=>store.currentClientInstance!=null,"first actual customer arrived",60);while(wait.MoveNext())yield return wait.Current;
   Require(manager.clientStack.Count==initial-1,"first customer removes one from host queue");var customer=CurrentCustomer();PrepareCustomer(customer,1000);SendCount("COUNT_FIRST",counter);Signal("BUDGET_FIRST");
   wait=Wait(()=>customer.GetBudget()>1000,"guest discount increased host customer budget",90);while(wait.MoveNext())yield return wait.Current;int increased=customer.GetBudget();Signal("BUDGET_UP="+increased);
   wait=Wait(()=>Received("BUDGET_UP_OK"),"guest saw increased budget",90);while(wait.MoveNext())yield return wait.Current;
   customer.OnDismiss=null;store.DismissCurrentClient(false);store.lockNextBellFlag=0;store.TryCallNextClient();wait=Wait(()=>store.currentClientInstance!=null,"second actual customer arrived",60);while(wait.MoveNext())yield return wait.Current;
   Require(manager.clientStack.Count==initial-2,"second customer removes one from host queue");customer=CurrentCustomer();PrepareCustomer(customer,1000);SendCount("COUNT_SECOND",counter);
   var h=new Harmony("shop-together.customer1314-probe");h.Patch(AccessTools.Method(typeof(RNG),"GetRandomInt",new[]{typeof(int),typeof(int)}),prefix:new HarmonyMethod(typeof(RuntimeProbe),"CustomerRandom"));fixedMarkup=true;Signal("BUDGET_SECOND");
   wait=Wait(()=>customer.GetBudget()<1000,"guest markup decreased host customer budget",90);while(wait.MoveNext())yield return wait.Current;fixedMarkup=false;int decreased=customer.GetBudget();Signal("BUDGET_DOWN="+decreased);
   wait=Wait(()=>Received("BUDGET_DOWN_OK"),"guest saw decreased budget",90);while(wait.MoveNext())yield return wait.Current;
   var back=EmporiumEntry.Instance.backInvinvElement;foreach(var old in new List<GameItem>(back.childItems))old.Destroy();var item=ModHelper.SpawnItem("magnifier");item.name="CUSTOMER_EXPENSIVE";GeneralHelper.SetItemOwned(item,true);item.unitValue=10000;Require(GraphUtils.TryAcceptAll(back,item)==0,"expensive owned item on counter");store.currentNegociatedItem=item;NegociationUIManager.Instance.currentNegociatedItem=item;root.World.Index();
   var ui=NegociationUIManager.Instance;ui.OpenUI();yield return null;Require(!ui.IsClientHaveEnoughToBuy(),"host identifies sale above customer budget");ui.OpenConfirmPanel();var frame=TradeFrame();Require(frame!=null&&frame.confirmRoot.activeSelf,"host displays over-budget warning");string expected=Convert.ToBase64String(Encoding.UTF8.GetBytes(frame.confirmText.text));ui.OnConfirmPanelNo();Signal("WARNING="+expected);
   var cashBefore=store.playerCash;wait=Wait(()=>Received("WARNING_CANCEL"),"guest cancelled over-budget sale",90);while(wait.MoveNext())yield return wait.Current;
   Require(item.parentInventory==back&&store.playerCash==cashBefore&&!frame.confirmRoot.activeSelf,"guest warning stays off host and cancel does not sell");Signal("WARNING_AGAIN");
   wait=Wait(()=>store.playerCash>cashBefore&&item.parentInventory!=back,"guest confirmation executes the sale on host",90);while(wait.MoveNext())yield return wait.Current;
   Require(!frame.confirmRoot.activeSelf,"guest confirmation never opens host warning");Signal("WARNING_SOLD");
  }
  static IEnumerator Customer1314Client(Connection root){
   var counter=UnityEngine.Object.FindObjectOfType<StoreClientCounter>();Require(counter!=null&&counter.clientCounterTMP!=null,"guest customer counter exists");
   var check=CheckCount("COUNT_INITIAL",counter);while(check.MoveNext())yield return check.Current;Signal("COUNT_INITIAL_OK");
   check=CheckCount("COUNT_FIRST",counter);while(check.MoveNext())yield return check.Current;
   var wait=Wait(()=>CurrentCustomer()!=null&&CurrentCustomer().GetBudget()==1000&&Received("BUDGET_FIRST"),"first host customer budget replicated",90);while(wait.MoveNext())yield return wait.Current;
   var ui=NegociationUIManager.Instance;ui.OpenUI();wait=Wait(()=>BudgetVisible(1000),"guest tablet shows host budget",90);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>BargainUIManager.Instance.OfferDiscount(10));string val=null;wait=Wait(()=>(val=ReceivePrefix("BUDGET_UP="))!=null,"host increased budget",90);while(wait.MoveNext())yield return wait.Current;int up=int.Parse(val);
   wait=Wait(()=>CurrentCustomer()!=null&&CurrentCustomer().GetBudget()==up&&BudgetVisible(up),"guest tablet updates to increased budget",90);while(wait.MoveNext())yield return wait.Current;Signal("BUDGET_UP_OK");
   check=CheckCount("COUNT_SECOND",counter);while(check.MoveNext())yield return check.Current;
   wait=Wait(()=>CurrentCustomer()!=null&&CurrentCustomer().GetBudget()==1000&&Received("BUDGET_SECOND"),"second customer budget replicated",90);while(wait.MoveNext())yield return wait.Current;ui.OpenUI();
   NativeClick(0,()=>BargainUIManager.Instance.OfferMarkup(5));val=null;wait=Wait(()=>(val=ReceivePrefix("BUDGET_DOWN="))!=null,"host decreased budget",90);while(wait.MoveNext())yield return wait.Current;int down=int.Parse(val);
   wait=Wait(()=>CurrentCustomer()!=null&&CurrentCustomer().GetBudget()==down&&BudgetVisible(down),"guest tablet updates to decreased budget",90);while(wait.MoveNext())yield return wait.Current;Signal("BUDGET_DOWN_OK");
   string encoded=null;wait=Wait(()=>Find(root,"CUSTOMER_EXPENSIVE")!=null&&(encoded=ReceivePrefix("WARNING="))!=null,"expensive sale and host warning ready",90);while(wait.MoveNext())yield return wait.Current;string expected=Encoding.UTF8.GetString(Convert.FromBase64String(encoded));ui.OpenUI();yield return null;
   Require(!ui.IsClientHaveEnoughToBuy(),"guest identifies sale above customer budget");NativeClick(0,()=>ui.OnSellAllClick());yield return new WaitForSecondsRealtime(2);var frame=TradeFrame();
   Require(frame!=null&&frame.confirmRoot.activeSelf&&frame.confirmText.text==expected,"guest sees the same over-budget warning as host");NativeClick(0,()=>ui.OnConfirmPanelNo());Require(!frame.confirmRoot.activeSelf,"guest can cancel its own warning");Signal("WARNING_CANCEL");
   wait=Wait(()=>Received("WARNING_AGAIN"),"host confirms cancellation preserved sale",90);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>ui.OnSellAllClick());
   wait=Wait(()=>frame.confirmRoot.activeSelf,"guest warning opens again before confirmed sale",90);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>ui.OnConfirmPanelYes());Require(!frame.confirmRoot.activeSelf,"guest confirmation closes locally");
   wait=Wait(()=>Received("WARNING_SOLD"),"host processed confirmed sale",90);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
