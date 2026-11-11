#if LAN_PROBE
using System;
using System.Collections;
using System.Text;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static bool releaseBlend; static int guestInsertCallbacks, guestBlendCalls, nestedMoves, nestedMovesBlocked;
  static IEnumerator HoldBlend(IEnumerator original){while(!releaseBlend)yield return null;while(original.MoveNext())yield return original.Current;}
  static void DelayHostBlend(ref IEnumerator __result){if(Connection.Current!=null&&Connection.Current.IsHost)__result=HoldBlend(__result);}
  static void RecordFeedInsert(){if(ReplicaSession.ClientStore&&WorldReplica.Sandbox>0)guestInsertCallbacks++;}
  static void RecordGuestBlend(){if(ReplicaSession.ClientStore)guestBlendCalls++;}
  static void ObserveFeed(){var h=new Harmony("shop-together.replica1313-probe");
   h.Patch(AccessTools.Method(typeof(MachineFeedDispenser),"RunPendingAutoBlends"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"DelayHostBlend"));
   h.Patch(AccessTools.Method(typeof(MachineFeedDispenser),"OnItemInserted"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"RecordFeedInsert"));
   h.Patch(AccessTools.Method(typeof(MachineFeedDispenser),"TryBlendItem"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"RecordGuestBlend"));}
  static IEnumerator Replica1313Host(Connection root){
   var e=EmporiumEntry.Instance;e.invElement.SetShape(36,36);e.Validate(true);ObserveFeed();releaseBlend=false;
   var feed=PanelFixture("livestock_feed_dispenser","REPLICA_FEED");var food=PanelFixture("nutrifruit","REPLICA_FOOD");
   int calories=food.GetTagReadonly("CALORIE_VALUE_TAG").GetInt();Require(calories>0,"real food supplies calories to feed machine");
   feed.ModifyTag("FEED_DISPENSER_AUTO_BLEND_BOOL",t=>t.Enable().SetBool(true));root.World.Index();Signal("FEED_READY");
   var wait=Wait(()=>Received("FEED_GUEST_READY"),"guest ready before host inserts food",90);while(wait.MoveNext())yield return wait.Current;
   Require(GraphUtils.TryAcceptAll(MachineFeedDispenser.GetStorageGrid(feed),food)==0,"host inserted food into auto-blending machine");
   root.World.Index();var delta=root.World.Capture(true,0,root.Leases,Time.realtimeSinceStartup);root.World.LastSentVersion=delta.version;
   LargeTransfer.Deliver((type,data)=>root.SendTo(1,type,data),LargeTransfer.Pack(20,Encoding.UTF8.GetBytes(WireJson.ToJson(delta))));
   Signal("FEED_INSERTED");wait=Wait(()=>Received("FEED_GUEST_OBSERVED"),"guest applied insertion before host blend",90);while(wait.MoveNext())yield return wait.Current;
   Require(feed.GetTagReadonly("STORED_KCAL_TAG").GetInt()==0&&food.parentInventory!=null,"host blend stayed delayed for replica check");releaseBlend=true;
   wait=Wait(()=>feed.GetTagReadonly("STORED_KCAL_TAG").GetInt()==calories&&food.parentInventory==null,"host blended food exactly once",90);while(wait.MoveNext())yield return wait.Current;Signal("FEED_RESULT="+calories);
   wait=Wait(()=>Received("FEED_MATCH"),"guest feed matches host",90);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator Replica1313Client(Connection root){
   ObserveFeed();guestInsertCallbacks=guestBlendCalls=0;
   var wait=Wait(()=>Find(root,"REPLICA_FEED")!=null&&Find(root,"REPLICA_FOOD")!=null&&Received("FEED_READY"),"feed fixture replicated",90);while(wait.MoveNext())yield return wait.Current;Signal("FEED_GUEST_READY");
   var feed=Find(root,"REPLICA_FEED");var food=Find(root,"REPLICA_FOOD");
   MachineFeedDispenser.GetStorageGrid(feed).onSlotAddItemEndFunc+=(item,inventory,slot)=>{nestedMoves++;if(!inventory.Expel(item))nestedMovesBlocked++;item.ModifyTag("SMC_TEST_CALLBACK_TAG",t=>t.Enable().SetInt(1));};
   wait=Wait(()=>food.parentInventory==MachineFeedDispenser.GetStorageGrid(feed)&&guestInsertCallbacks>0&&Received("FEED_INSERTED"),"guest applied real feed insertion callback",90);while(wait.MoveNext())yield return wait.Current;
   yield return new WaitForSecondsRealtime(2);Require(feed.GetTagReadonly("STORED_KCAL_TAG").GetInt()==0&&food.parentInventory==MachineFeedDispenser.GetStorageGrid(feed),"guest callback did not change authoritative feed or remove food (kcal="+feed.GetTagReadonly("STORED_KCAL_TAG").GetInt()+", same inventory="+(food.parentInventory==MachineFeedDispenser.GetStorageGrid(feed))+", blends="+guestBlendCalls+")");
   Require(nestedMoves>0&&nestedMovesBlocked==nestedMoves&&!food.IsTag("SMC_TEST_CALLBACK_TAG"),"replica blocked a callback from moving or changing the same inserted item");
   Require(guestBlendCalls==0,"guest did not run deferred auto-blending logic");Signal("FEED_GUEST_OBSERVED");
   string result=null;wait=Wait(()=>(result=ReceivePrefix("FEED_RESULT="))!=null,"host finished single blend",90);while(wait.MoveNext())yield return wait.Current;int calories=int.Parse(result);
   wait=Wait(()=>feed.GetTagReadonly("STORED_KCAL_TAG").GetInt()==calories&&Find(root,"REPLICA_FOOD")==null,"guest feed amount and consumed food match host",90);while(wait.MoveNext())yield return wait.Current;Signal("FEED_MATCH");
  }
 }
}
#endif
