#if LAN_PROBE
using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static object AuditCompany(string id){return AccessTools.Method(AccessTools.TypeByName("CorpPresence.Companies"),"Find").Invoke(null,new object[]{id});}
  static IEnumerator OverflowHost136(Connection root){
   var type=AccessTools.TypeByName("TrashOverflow");var page=AccessTools.Method(type,"NewPage").Invoke(null,new object[]{StoreStation.GetDayCounter(),0,null,301});
   var grid=(GameGridInventory)AccessTools.Field(page.GetType(),"Grid").GetValue(page);var item=ModHelper.SpawnItem("screwdriver");GeneralHelper.SetItemOwned(item,true);item.name="OVERFLOW136_ITEM";
   Require((bool)AccessTools.Method(type,"Store").Invoke(null,new object[]{grid,item,true}),"native overflow page accepts protected item");
   root.World.Index();Require(root.World.Address(grid).root==1301&&root.World.Id(item)>0,"overflow page indexed with stable address");Signal("OVERFLOW136_READY");
   var wait=Wait(()=>item.parentInventory==EmporiumEntry.Instance.backInvinvElement,"guest took item from host overflow page",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>OverflowReplica.Capture().Length==0,"native host removed empty overflow page",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("OVERFLOW136_DONE"),"guest retained moved item after page removal",60);while(wait.MoveNext())yield return wait.Current;
   var c=AuditCompany("pastel");var state=AuditCompanyState("pastel");AccessTools.Property(state.GetType(),"Coop").SetValue(state,0,null);AccessTools.Property(state.GetType(),"DealerJoined").SetValue(state,false,null);PlayerStore.Instance.playerCash=100000;CompanyIntent.Flush(true);Signal("ORDER136_LOCKED");
   wait=Wait(()=>Received("ORDER136_DENIED"),"host rejected ineligible company service",60);while(wait.MoveNext())yield return wait.Current;
   Require(PlayerStore.Instance.playerCash==100000,"ineligible service does not charge cash");AccessTools.Property(state.GetType(),"Coop").SetValue(state,1000,null);
   var catalog=(Array)AccessTools.Property(c.GetType(),"Catalog").GetValue(c,null);var entry=catalog.GetValue(0);int quote=(int)AccessTools.Method(c.GetType(),"OrderUnitPrice").Invoke(c,new[]{entry,state});Require(quote>0,"native company quote exists");
   CompanyIntent.Flush(true);Signal("ORDER136_READY");
   wait=Wait(()=>((IList)AccessTools.Property(state.GetType(),"Orders").GetValue(state,null)).Count>0,"company order created on host",90);while(wait.MoveNext())yield return wait.Current;
   Require(PlayerStore.Instance.playerCash==100000-quote,"host ignores guest price and uses native quote");Signal("ORDER136_HOST_DONE");
   wait=Wait(()=>Received("ORDER136_DONE"),"company order cash synchronized",60);while(wait.MoveNext())yield return wait.Current;
   var specimen=AuditCompanyState("carmicheal");AccessTools.Property(specimen.GetType(),"Coop").SetValue(specimen,0,null);AccessTools.Property(specimen.GetType(),"Licensed").SetValue(specimen,true,null);
   int before=((IList)AccessTools.Property(specimen.GetType(),"Orders").GetValue(specimen,null)).Count;PlayerStore.Instance.playerCash=100000;CompanyIntent.Flush(true);Signal("SPECIMEN136_READY");
   wait=Wait(()=>((IList)AccessTools.Property(specimen.GetType(),"Orders").GetValue(specimen,null)).Count==before+1,"licensed tier zero specimen order accepted by host",60);while(wait.MoveNext())yield return wait.Current;
   Require(PlayerStore.Instance.playerCash==99500,"specimen host price overrides guest price");Signal("SPECIMEN136_HOST_DONE");
   wait=Wait(()=>Received("SPECIMEN136_DONE"),"specimen result returned",60);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator OverflowClient136(Connection root){
   bool ready=false;var wait=Wait(()=>(ready=ready||Received("OVERFLOW136_READY"))&&Find(root,"OVERFLOW136_ITEM")!=null,"overflow item replicated",90);while(wait.MoveNext())yield return wait.Current;
   var item=Find(root,"OVERFLOW136_ITEM");Require(root.World.Address(item.parentInventory).root==1301,"guest overflow page uses host address");Require(OverflowReplica.Capture().Length==1,"guest native overflow page created");
   int id=root.World.Id(item);long version;root.World.Versions.TryGetValue(id,out version);
   root.Submit(new Request{kind=RequestKind.Move,items=new[]{id},moves=new[]{new Placement{item=id,count=1,version=version,inventory=root.World.Address(EmporiumEntry.Instance.backInvinvElement),x=0,y=0}}});
   wait=Wait(()=>item.parentInventory==EmporiumEntry.Instance.backInvinvElement&&OverflowReplica.Capture().Length==0,"moved item survived host overflow page removal",90);while(wait.MoveNext())yield return wait.Current;Signal("OVERFLOW136_DONE");
   wait=Wait(()=>Received("ORDER136_LOCKED"),"host company eligibility fixture ready",60);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.LastNote=null;RemoteActions.Forward(RemoteActions.Adapter("CompanyAction",1),null,new object[]{WireJson.ToJson(new CompanyCommand{action="CorpPresence.Services.JoinDealer",args=new[]{"c:pastel"}})});
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC345~"),"host eligibility refusal reaches guest",60);while(wait.MoveNext())yield return wait.Current;Signal("ORDER136_DENIED");
   wait=Wait(()=>Received("ORDER136_READY"),"native company order ready",60);while(wait.MoveNext())yield return wait.Current;
   var c=AuditCompany("pastel");var catalog=(Array)AccessTools.Property(c.GetType(),"Catalog").GetValue(c,null);var entry=catalog.GetValue(0);var method=AccessTools.Method(AccessTools.TypeByName("CorpPresence.Services"),"PlaceOrder");var cart=(IDictionary)Activator.CreateInstance(method.GetParameters()[1].ParameterType);cart.Add(entry,1);
   NativeClick(0,()=>method.Invoke(null,new object[]{c,cart,new Func<object,int>(x=>1)}));
   wait=Wait(()=>Received("ORDER136_HOST_DONE"),"host order price verified",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>PlayerStore.Instance.playerCash<100000,"company cash returned from host",60);while(wait.MoveNext())yield return wait.Current;Signal("ORDER136_DONE");
   wait=Wait(()=>Received("SPECIMEN136_READY"),"licensed tier zero specimen ready",60);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.Forward(RemoteActions.Adapter("CompanyAction",1),null,new object[]{WireJson.ToJson(new CompanyCommand{action="CorpPresence.CarmichaelCo.OrderSpecimen",args=new[]{"c:carmicheal","s:@silverblood","i:1"}})});
   wait=Wait(()=>Received("SPECIMEN136_HOST_DONE"),"native specimen eligibility and price verified",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>PlayerStore.Instance.playerCash==99500,"specimen cash synchronized",60);while(wait.MoveNext())yield return wait.Current;Signal("SPECIMEN136_DONE");
  }
 }
}
#endif
