#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 public sealed partial class Connection {
  internal void ProbeCutOutingLink1344(){
   var peer=peers.Find(p=>p.authenticated&&p.modsOk&&p.sceneReady);
   if(peer==null||peer.socket==null)throw new InvalidOperationException("Expected a live direct test peer");
   // Break the transport only. Normal Pump/error handling must release its
   // leases and return the friend to the menu; do not invoke Stop here.
   peer.socket.Shutdown(System.Net.Sockets.SocketShutdown.Both);
  }
 }
 internal static partial class RuntimeProbe {
  static string GroundSignature1344(Connection root){
   var items=new List<string>();foreach(var item in EmporiumEntry.Instance.afterhourInventory.childItems)
    items.Add(root.World.Id(item)+":"+item.identifier+":"+item.unitCount);
   items.Sort(StringComparer.Ordinal);return string.Join("|",items);
  }
  // Exercise the host's input scope too: directly invoking scavenging outside
  // input misses the interception of freshly generated, not-yet-indexed loot.
  static IEnumerator OutingHost1344(Connection root){
   var e=EmporiumEntry.Instance;var store=PlayerStore.Instance;var map=MapUIManager.Instance;
   EmporiumOption.Instance.autoCallNext=false;store.devMode=true;store.isAfterhoursIntroduced=true;
   store.storeState=PlayerStore.StoreState.CLOSED;store.visitLeftTonight=10;
   var pack=PanelFixture("backpack_large","OUT44_PACK");
   var hostBandage=PanelFixture("bandage_item","OUT44_HOST_BANDAGE");
   var friendBandage=PanelFixture("hemostatic_bandage_item","OUT44_FRIEND_BANDAGE");
   Require(GraphUtils.TryAcceptAll(pack,hostBandage)==0&&GraphUtils.TryAcceptAll(pack,friendBandage)==0,"owned bandages packed for the outing");
   root.World.Index();e.selectedBackpack=pack;
   NativeClick(0,()=>map.Travel(AfterhourLocation.Get("dumping_ground")));
   var wait=Wait(()=>OutingKits.HostAt("dumping_ground")&&!map.IsInTransit,"host reaches dumping ground",60);while(wait.MoveNext())yield return wait.Current;
   // Native injury immunities and nonnegative luck guarantee this search takes
   // the success branch without replacing native loot generation or placement.
   store.devMode=false;store.healthData.luck=100;store.healthData.isWoundedFresh=false;
   store.dumpingGroundMinorWoundImmunitySearchesLeft=10;store.dumpingGroundMajorWoundImmunitySearchesLeft=10;
   int before=e.afterhourInventory.childItems.Count;int attempts=store.scavengingAttempts;
   NativeClick(0,()=>map.ScavengeButtonClick());
   Require(store.scavengingAttempts==attempts+1&&e.afterhourInventory.childItems.Count>before,"host input consumes one search and places native generated loot");
   Require(!NativeActions.Executing,"host scavenging restores action interception");
   int loot=e.afterhourInventory.childItems.Count;root.SyncStateSoon();Signal("OUT44_LOOT="+loot);
   wait=Wait(()=>Received("OUT44_SEEN"),"guest receives host loot",60);while(wait.MoveNext())yield return wait.Current;
   HealthData.ReceiveMinorWound();attempts=store.scavengingAttempts;loot=e.afterhourInventory.childItems.Count;Signal("OUT44_REFUSE_SEARCH");
   wait=Wait(()=>Received("OUT44_REFUSED"),"guest receives host injury refusal despite stale local health",60);while(wait.MoveNext())yield return wait.Current;
   Require(store.scavengingAttempts==attempts&&e.afterhourInventory.childItems.Count==loot,"refused search consumes no attempt and produces no loot");
   Require(hostBandage.MayActivateSlotItem()&&hostBandage.CanActivateSlotItem(),"native bandage permits treating the host wound");
   NativeClick(0,()=>hostBandage.ActivateSlotItem());
   Require(store.healthData.isWoundStable&&!store.healthData.isWoundedFresh&&hostBandage.parentInventory==null,"host treatment consumes its bandage and clears fresh injury");
   root.SyncStateSoon();Signal("OUT44_HOST_HEALED");
   wait=Wait(()=>Received("OUT44_HEAL_SEEN"),"guest receives host treatment",60);while(wait.MoveNext())yield return wait.Current;
   HealthData.ReceiveMinorWound();root.SyncStateSoon();Signal("OUT44_GUEST_TREAT");
   wait=Wait(()=>store.healthData.isWoundStable&&!store.healthData.isWoundedFresh&&friendBandage.parentInventory==null,"guest treatment executes on host",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("OUT44_GUEST_HEALED"),"guest receives its treatment result",60);while(wait.MoveNext())yield return wait.Current;
   attempts=store.scavengingAttempts;loot=e.afterhourInventory.childItems.Count;Signal("OUT44_GUEST_SEARCH");
   wait=Wait(()=>store.scavengingAttempts==attempts+1&&e.afterhourInventory.childItems.Count>loot,"guest search creates loot on host",60);while(wait.MoveNext())yield return wait.Current;
   var pickup=PanelFixture("nuts_metal","OUT44_PICKUP");Require(GraphUtils.TryAcceptAll(e.afterhourInventory,pickup)==0,"small pickup placed on shared ground");
   root.World.Index();root.SyncStateSoon();Signal("OUT44_GUEST_LOOT="+e.afterhourInventory.childItems.Count);
   wait=Wait(()=>pickup.parentInventory!=null&&GraphUtils.FindParentType<GameItem>(pickup.parentInventory)==pack,"guest moves ground item into the shared backpack",60);while(wait.MoveNext())yield return wait.Current;
   root.SyncStateSoon();Signal("OUT44_GROUND="+GroundSignature1344(root));
   wait=Wait(()=>Received("OUT44_REJOINED"),"guest returns through native menu and rejoins the outing",120);while(wait.MoveNext())yield return wait.Current;
   Require(root.IsHost&&root.Ready&&OutingKits.HostAt("dumping_ground"),"guest leaving and rejoining preserves host outing");
   Signal("OUT44_HOLD_PICKUP");wait=Wait(()=>Received("OUT44_HOLDING"),"guest holds a pickup lease before transport loss",60);while(wait.MoveNext())yield return wait.Current;
   Require(!root.Leases.Available(0,root.World.Id(pickup),Time.realtimeSinceStartup),"host sees guest pickup lease before cut");
   Debug.Log("[SMC NOTE] intentionally cutting the direct connection while outside");root.ProbeCutOutingLink1344();
   wait=Wait(()=>root.Leases.Available(0,root.World.Id(pickup),Time.realtimeSinceStartup),"connection loss releases guest item lease",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("OUT44_RECOVERED"),"guest rejoins after transport loss with exact ground contents",120);while(wait.MoveNext())yield return wait.Current;
   Signal("OUT44_RETURN");wait=Wait(()=>map.Current==null&&!map.IsInTransit,"guest request returns everyone home",60);while(wait.MoveNext())yield return wait.Current;
   Require(GraphUtils.FindParentType<GameItem>(pickup.parentInventory)==pack&&pack.parentInventory!=e.afterhourPocketSlotInvBackpack,"picked-up item survives the return in the real backpack");
   wait=Wait(()=>Received("OUT44_HOME"),"guest receives return and carried item",60);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator OutingClient1344(Connection root){
   string count=null;var wait=Wait(()=>(count=ReceivePrefix("OUT44_LOOT="))!=null,"host search result",60);while(wait.MoveNext())yield return wait.Current;
   int loot=int.Parse(count);wait=Wait(()=>OutingKits.Joined=="dumping_ground"&&EmporiumEntry.Instance.afterhourInventory.childItems.Count==loot,"guest shows host garbage in shared ground",60);while(wait.MoveNext())yield return wait.Current;Signal("OUT44_SEEN");
   wait=Wait(()=>Received("OUT44_REFUSE_SEARCH"),"host injury refusal setup",60);while(wait.MoveNext())yield return wait.Current;
   // Deliberately stale client health must not authorize a search on the host.
   PlayerStore.Instance.healthData.isWoundedFresh=false;Require(ScavHelper.CanScavenge(),"stale guest health would allow a local search");
   RemoteActions.LastNote=null;int sent=Connection.Outgoing;NativeClick(0,()=>MapUIManager.Instance.ScavengeButtonClick());
   Require(Connection.Outgoing==sent+1,"native guest search button sends exactly one host request");
   wait=Wait(()=>RemoteActions.LastNote==LocHelper.GetLocalizedMechanic("afterhour_attempts_wounded"),"host injury refusal is displayed to guest",60);while(wait.MoveNext())yield return wait.Current;Signal("OUT44_REFUSED");
   wait=Wait(()=>Received("OUT44_HOST_HEALED"),"host treatment finished",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>PlayerStore.Instance.healthData.isWoundStable&&!PlayerStore.Instance.healthData.isWoundedFresh&&Find(root,"OUT44_HOST_BANDAGE")==null,"guest shows host healing and consumed bandage",60);while(wait.MoveNext())yield return wait.Current;Signal("OUT44_HEAL_SEEN");
   wait=Wait(()=>Received("OUT44_GUEST_TREAT"),"host applied fresh wound",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>!PlayerStore.Instance.healthData.isWoundStable&&PlayerStore.Instance.healthData.isWoundedFresh,"guest sees fresh wound",60);while(wait.MoveNext())yield return wait.Current;
   var bandage=Find(root,"OUT44_FRIEND_BANDAGE");Require(bandage!=null,"guest has shared packed bandage");NativeClick(0,()=>bandage.ActivateSlotItem());
   wait=Wait(()=>PlayerStore.Instance.healthData.isWoundStable&&!PlayerStore.Instance.healthData.isWoundedFresh&&Find(root,"OUT44_FRIEND_BANDAGE")==null,"guest receives authoritative healing result",60);while(wait.MoveNext())yield return wait.Current;Signal("OUT44_GUEST_HEALED");
   wait=Wait(()=>Received("OUT44_GUEST_SEARCH"),"guest search turn",60);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>MapUIManager.Instance.ScavengeButtonClick());
   count=null;wait=Wait(()=>(count=ReceivePrefix("OUT44_GUEST_LOOT="))!=null,"host accepts guest search",60);while(wait.MoveNext())yield return wait.Current;
   loot=int.Parse(count);wait=Wait(()=>EmporiumEntry.Instance.afterhourInventory.childItems.Count==loot,"guest sees loot from its own request",60);while(wait.MoveNext())yield return wait.Current;
   var pickup=Find(root,"OUT44_PICKUP");var pack=Find(root,"OUT44_PACK");Require(pickup!=null&&pack!=null,"guest has shared pickup and backpack");int pickupId=root.World.Id(pickup);
   NativeClick(0,()=>GraphUtils.TryAcceptAll(pack,pickup));
   string ground=null;wait=Wait(()=>(ground=ground??ReceivePrefix("OUT44_GROUND="))!=null&&GroundSignature1344(root)==ground&&GraphUtils.FindParentType<GameItem>(pickup.parentInventory)==pack,"pickup move and remaining ground match host",60);while(wait.MoveNext())yield return wait.Current;
   long version=root.World.Version;root.ProbeResync1339();wait=Wait(()=>root.World.Version>version&&!root.ProbeAwaitingFull,"full resync while outside completes",60);while(wait.MoveNext())yield return wait.Current;
   Require(GroundSignature1344(root)==ground&&root.World.Id(Find(root,"OUT44_PICKUP"))==pickupId,"full resync preserves ground identities and pickup identity");
   loot=EmporiumEntry.Instance.afterhourInventory.childItems.Count;
   int reports=IssueReport.Count;NativeClick(0,()=>StoreUIManager.Instance.escapeUIManager.OnMainMenu());
   Require(!root.IsClient&&!root.Ready&&!ReplicaSession.SceneReady,"native menu stops replication before scene teardown");
   wait=Wait(()=>MainMenuUIController.Instance!=null&&EmporiumEntry.Instance==null,"native menu exit completed",60);while(wait.MoveNext())yield return wait.Current;
   yield return new WaitForSecondsRealtime(.5f);Require(IssueReport.Count==reports,"native menu exit adds no session/apply/panel errors");
   root.ProbeJoin(Argument("--smc-invite"));wait=Wait(()=>root.Ready&&OutingKits.Joined=="dumping_ground"&&EmporiumEntry.Instance.afterhourInventory.childItems.Count==loot,"rejoin restores shared outing and ground",90);while(wait.MoveNext())yield return wait.Current;
   Require(GroundSignature1344(root)==ground&&root.World.Id(Find(root,"OUT44_PICKUP"))==pickupId,"late rejoin preserves exact ground and picked-up item");
   Signal("OUT44_REJOINED");
   wait=Wait(()=>Received("OUT44_HOLD_PICKUP"),"transport loss setup",60);while(wait.MoveNext())yield return wait.Current;root.Claim(new[]{pickupId});
   wait=Wait(()=>root.LocalLeases.ContainsKey(pickupId),"guest receives pickup lease",60);while(wait.MoveNext())yield return wait.Current;Signal("OUT44_HOLDING");
   wait=Wait(()=>!root.IsClient&&MainMenuUIController.Instance!=null&&EmporiumEntry.Instance==null,"broken transport returns guest to menu",90);while(wait.MoveNext())yield return wait.Current;
   root.ProbeJoin(Argument("--smc-invite"));wait=Wait(()=>root.Ready&&OutingKits.Joined=="dumping_ground"&&GroundSignature1344(root)==ground,"reconnect after broken transport restores exact ground",90);while(wait.MoveNext())yield return wait.Current;
   pickup=Find(root,"OUT44_PICKUP");pack=Find(root,"OUT44_PACK");Require(root.World.Id(pickup)==pickupId&&GraphUtils.FindParentType<GameItem>(pickup.parentInventory)==pack,"transport recovery preserves the picked-up item in backpack");
   string report=(string)AccessTools.Method(typeof(IssueReport),"Text").Invoke(null,null);Require(!report.Contains("NullReferenceException")&&!report.Contains("apply:")&&!report.Contains("machine panels:"),"transport recovery has no null-object, apply or machine-panel errors");Signal("OUT44_RECOVERED");
   wait=Wait(()=>Received("OUT44_RETURN"),"guest return turn",60);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>MapUIManager.Instance.Return());
   wait=Wait(()=>GenericConfirmationUIManager.Instance.panel.activeSelf,"guest sees warning about items left on ground",60);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>GenericConfirmationUIManager.Instance.ConfirmButtonPressed());
   wait=Wait(()=>OutingKits.Joined==null&&pack.parentInventory!=EmporiumEntry.Instance.afterhourPocketSlotInvBackpack,"guest follows everyone home",60);while(wait.MoveNext())yield return wait.Current;
   Require(GraphUtils.FindParentType<GameItem>(pickup.parentInventory)==pack,"guest still has picked-up item in shared backpack at home");Signal("OUT44_HOME");
  }
 }
}
#endif
