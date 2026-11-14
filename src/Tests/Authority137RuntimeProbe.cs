#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static IEnumerator AuthorityHost137(Connection root){
   EmporiumEntry.Instance.invElement.SetShape(80,80);Time.timeScale=0;
   var box=PanelFixture("backpack_large","AUTH137_BOX");var child=ModHelper.SpawnItem("screwdriver");child.name="AUTH137_CHILD";GeneralHelper.SetItemOwned(child,true);
   Require(GraphUtils.TryAcceptAll(box,child)==0,"request container contains child");
   var request=new StoreRequest{id="auth137",title="auth137",requiredItem="backpack_large",requiredItemCount=1,creditReward=731,duration=3};PlayerStore.Instance.storeRequests.Add(request);PlayerStore.Instance.playerCash=100000;root.World.Index();
   Require(request.CanFillRequest(),"native request has its delivery item");
   long lease;Require(root.Leases.Claim(2,new[]{root.World.Id(child)},Time.realtimeSinceStartup,out lease),"another player holds request child");
   RemoteActions.LastNote=null;NativeClick(0,()=>request.FillRequest());
   Require(PlayerStore.Instance.storeRequests.Contains(request)&&PlayerStore.Instance.playerCash==100000&&box.parentInventory!=null&&RemoteActions.LastNote==Loc.Text("~SMC096~"),"host request refuses occupied descendant before consumption or reward");
   Require(!BarIntent.Free(box),"bar delivery checks occupied descendants");root.Leases.Release(2,new[]{root.World.Id(child)});Signal("AUTH137_REQUEST_READY");
   var wait=Wait(()=>!PlayerStore.Instance.storeRequests.Contains(request),"guest delivered request on host",60);while(wait.MoveNext())yield return wait.Current;
   Require(PlayerStore.Instance.playerCash==100731&&!EmporiumEntry.Instance.GetAllOwnedItems().Contains(box),"request consumed container and paid reward once");Signal("AUTH137_REQUEST_PAID");
   wait=Wait(()=>Received("AUTH137_REPLAY_REFUSED"),"replayed delivery refused",60);while(wait.MoveNext())yield return wait.Current;Require(PlayerStore.Instance.playerCash==100731,"replay never duplicates reward");
   var meridian=AccessTools.TypeByName("CorpPresence.MeridianCo");var services=AccessTools.TypeByName("CorpPresence.Services");var company=AuditCompany("meridian");var state=AuditCompanyState("meridian");AccessTools.Property(state.GetType(),"Coop").SetValue(state,1000,null);PlayerStore.Instance.playerCash=100000;
   var poor=ItemConditionList.conditionLookup["CATEGORY_EQUIPMENT_CONDITION"].First(x=>x.identifier.EndsWith("poor"));
   for(int i=0;i<4;i++){var gun=PanelFixture("handmade_pistol","AUTH137_GUN_"+i);var feature=gun.FindItemFeatureByCategory("CATEGORY_EQUIPMENT_CONDITION");Require(feature!=null,"native firearm condition exists");feature.realCondition=poor;gun.EnableTag("REPO_ELIGIBLE_OVERRIDE_TAG");gun.Validate();}
   var candidates=((IEnumerable)AccessTools.Method(services,"RefurbishCandidates").Invoke(null,null)).Cast<GameItem>().ToList();Require(candidates.Count>=4,"native refurbish selection has excess candidate");root.World.Index();
   var chosen=candidates.Take(3).ToArray();var excluded=candidates[3];string result=null;var method=AccessTools.Method(services,"RequestRefurbish");
   Require(root.Leases.Claim(2,new[]{root.World.Id(chosen[0])},Time.realtimeSinceStartup,out lease),"another player holds selected refurbish item");
   NativeClick(0,()=>result=(string)method.Invoke(null,new[]{company}));Require(result==Loc.Text("~SMC096~")&&PlayerStore.Instance.playerCash==100000&&chosen.All(x=>x.parentInventory!=null),"host company service refuses occupied selected item atomically");root.Leases.Release(2,new[]{root.World.Id(chosen[0])});
   Require(root.Leases.Claim(2,new[]{root.World.Id(excluded)},Time.realtimeSinceStartup,out lease),"another player holds unselected fourth item");int fee=chosen.Sum(x=>(int)AccessTools.Method(meridian,"RefurbishFee",new[]{typeof(GameItem)}).Invoke(null,new object[]{x}));int before=((IList)AccessTools.Property(state.GetType(),"Orders").GetValue(state,null)).Count;
   NativeClick(0,()=>result=(string)method.Invoke(null,new[]{company}));Require(result==null,"unselected fourth item never blocks native three-item service");
   Require(PlayerStore.Instance.playerCash==100000-fee&&((IList)AccessTools.Property(state.GetType(),"Orders").GetValue(state,null)).Count==before+1&&EmporiumEntry.Instance.GetAllOwnedItems().Contains(excluded),"native refurbish fee and single order preserve unselected item");
   root.Leases.Release(2,new[]{root.World.Id(excluded)});CompanyIntent.Flush(true);Signal("AUTH137_COMPANY="+(100000-fee));
   wait=Wait(()=>Received("AUTH137_DONE"),"company result synchronized to guest",60);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator AuthorityClient137(Connection root){
   bool ready=false;var wait=Wait(()=>(ready=ready||Received("AUTH137_REQUEST_READY"))&&PlayerStore.Instance.storeRequests.Any(x=>x.id=="auth137")&&Find(root,"AUTH137_CHILD")!=null,"request and contained item replicated",60);while(wait.MoveNext())yield return wait.Current;
   var request=PlayerStore.Instance.storeRequests.First(x=>x.id=="auth137");int index=PlayerStore.Instance.storeRequests.IndexOf(request);string key=RequestAdapters.Key(request);NativeClick(0,()=>request.FillRequest());
   bool paid=false;wait=Wait(()=>(paid=paid||Received("AUTH137_REQUEST_PAID"))&&PlayerStore.Instance.playerCash==100731&&!PlayerStore.Instance.storeRequests.Any(x=>x.id=="auth137"),"delivery reward and request removal replicated",60);while(wait.MoveNext())yield return wait.Current;
   RemoteActions.LastNote=null;RemoteActions.Forward(RemoteActions.Adapter("Fill",2),null,new object[]{index,key});wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC334~"),"stale request receives explicit refusal",60);while(wait.MoveNext())yield return wait.Current;Signal("AUTH137_REPLAY_REFUSED");
   string cash=null;wait=Wait(()=>(cash=cash??ReceivePrefix("AUTH137_COMPANY="))!=null&&PlayerStore.Instance.playerCash==int.Parse(cash),"host company service cash replicated",60);while(wait.MoveNext())yield return wait.Current;Signal("AUTH137_DONE");
  }
 }
}
#endif
