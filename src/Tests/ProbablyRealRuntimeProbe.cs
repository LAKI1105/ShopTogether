#if LAN_PROBE && PM_REAL_PROBE
using System;
using System.Collections;
using UnityEngine;

namespace ShopTogether {
 internal static partial class RuntimeProbe {
  // Both real mods are loaded. The external visit is an in-memory fixture; no public trade is sent.
  static IEnumerator ProbablyRealHost(Connection root) {
   var wait=Wait(()=>IsolatedVisitorTest.Loaded()&&ProbablyMultiplayerCompat.ProbeBound,"actual visitor mod and compatibility bridge loaded",40);while(wait.MoveNext())yield return wait.Current;
   var preview=IsolatedVisitorTest.Begin();
   Require(ProbablyMultiplayerCompat.VisitActive,"actual visitor mod created an active human visit");
   root.World.Index();Require(preview!=null&&ProbablyMultiplayerCompat.Protected(preview),"actual visitor mod created a protected counter preview");
   int previewId=root.World.Id(preview);Require(previewId>0,"host indexed real visitor preview");
   var ordinary=Find(root,"SMC_HOST_ITEM");int ordinaryId=root.World.Id(ordinary);Signal("PM_REAL_READY="+previewId);
   wait=Wait(()=>Received("PM_REAL_PREVIEW_DENIED"),"friend cannot claim the real visitor preview",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("PM_REAL_HELD")&&!root.Leases.Available(0,ordinaryId,Time.realtimeSinceStartup),"friend holds an ordinary item during the real visit",60);while(wait.MoveNext())yield return wait.Current;
   PlayerStore.Instance.playerCash=3000;
   Require(IsolatedVisitorTest.PrepareRejectedForHold(),"real visitor trade preparation respects a friend's earlier hold");Signal("PM_REAL_PREPARE_DENIED");
   wait=Wait(()=>root.Leases.Available(0,ordinaryId,Time.realtimeSinceStartup),"friend releases ordinary item",60);while(wait.MoveNext())yield return wait.Current;
   IsolatedVisitorTest.Prepare();Require(ProbablyMultiplayerCompat.TradeLocked,"real visitor mod reserved the transaction");Signal("PM_REAL_LOCKED");
   wait=Wait(()=>Received("PM_REAL_LOCK_DENIED"),"friend was blocked during the real transaction reservation",60);while(wait.MoveNext())yield return wait.Current;
   IsolatedVisitorTest.Commit();Require(PlayerStore.Instance.playerCash==2875,"real visitor mod charged the correct price once");
   root.World.Index();GameItem bought=null;int count=0;
   foreach(var item in root.World.Items.Values)if(item.name=="PM_REAL_VISITOR_ITEM"&&!ProbablyMultiplayerCompat.Protected(item)){bought=item;count++;}
   Require(count==1&&bought!=null&&bought.parentInventory==EmporiumEntry.Instance.backInvinvElement,"real visitor transaction produced one purchased item on host counter");
   Signal("PM_REAL_BOUGHT="+root.World.Id(bought));
   wait=Wait(()=>Received("PM_REAL_SYNCED"),"friend received the real transaction result",90);while(wait.MoveNext())yield return wait.Current;
   IsolatedVisitorTest.End();Signal("PM_REAL_ENDED");
  }
  static IEnumerator ProbablyRealGuest(Connection root) {
   var wait=Wait(()=>IsolatedVisitorTest.Loaded()&&ProbablyMultiplayerCompat.ProbeBound,"friend loaded actual visitor mod and compatibility bridge",40);while(wait.MoveNext())yield return wait.Current;
   Require(!IsolatedVisitorTest.LoginVisible(),"friend does not start an independent visitor login");
   string message=null;wait=Wait(()=>(message=ReceivePrefix("PM_REAL_READY="))!=null,"host started real visitor mod visit",90);while(wait.MoveNext())yield return wait.Current;
   int previewId=int.Parse(message);GameItem preview=null;
   wait=Wait(()=>root.World.Items.TryGetValue(previewId,out preview)&&preview!=null,"friend received real visitor's preview",90);while(wait.MoveNext())yield return wait.Current;
   var customer=PlayerStore.Instance.currentClientInstance;
   Require(customer!=null&&customer.GetClientBlueprint().identifier=="lemocuber_probablymultiplayer_human","friend sees host's real human visitor state");
   Require(SpriteDict.Instance.spriteDictionary.ContainsKey("lemocuber_probablymultiplayer_player_sprite"),"friend has a visitor portrait without a second login");
   root.Claim(new[]{previewId});wait=Wait(()=>!root.ProbePending(previewId),"real visitor preview claim answered",30);while(wait.MoveNext())yield return wait.Current;
   Require(!root.LocalLeases.ContainsKey(previewId),"friend cannot take real visitor preview into the shop");Signal("PM_REAL_PREVIEW_DENIED");
   var ordinary=Find(root,"SMC_HOST_ITEM");int ordinaryId=root.World.Id(ordinary);root.Claim(new[]{ordinaryId});
   wait=Wait(()=>!root.ProbePending(ordinaryId)&&root.LocalLeases.ContainsKey(ordinaryId),"friend can hold an unrelated item during visit",30);while(wait.MoveNext())yield return wait.Current;
   Signal("PM_REAL_HELD");wait=Wait(()=>Received("PM_REAL_PREPARE_DENIED"),"host rejected trade while item was held",30);while(wait.MoveNext())yield return wait.Current;
   root.Release(new[]{ordinaryId});wait=Wait(()=>Received("PM_REAL_LOCKED"),"real visitor trade reserved",60);while(wait.MoveNext())yield return wait.Current;
   root.Claim(new[]{ordinaryId});wait=Wait(()=>!root.ProbePending(ordinaryId),"transaction lock answered friend's claim",30);while(wait.MoveNext())yield return wait.Current;
   Require(!root.LocalLeases.ContainsKey(ordinaryId),"friend cannot move an item during real transaction reservation");Signal("PM_REAL_LOCK_DENIED");
   message=null;wait=Wait(()=>(message=ReceivePrefix("PM_REAL_BOUGHT="))!=null,"host completed real visitor transaction",60);while(wait.MoveNext())yield return wait.Current;
   int purchasedId=int.Parse(message);GameItem purchased=null;
   wait=Wait(()=>root.World.Items.TryGetValue(purchasedId,out purchased)&&purchased!=null&&purchased.parentInventory==EmporiumEntry.Instance.backInvinvElement&&PlayerStore.Instance.playerCash==2875,"friend receives purchased item and updated money",90);while(wait.MoveNext())yield return wait.Current;
   Require(!root.World.Items.ContainsKey(previewId),"visitor preview disappears after the real transaction");Signal("PM_REAL_SYNCED");
   wait=Wait(()=>Received("PM_REAL_ENDED"),"host ended the real visitor session",40);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
