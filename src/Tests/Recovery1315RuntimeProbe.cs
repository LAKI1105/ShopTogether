#if LAN_PROBE
using System;
using System.Collections;
using HarmonyLib;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static int recoveryFailures,recoveryInjected;
  static bool InjectReplicaFailure(){if(recoveryFailures>0){recoveryFailures--;recoveryInjected++;throw new InvalidOperationException("probe-induced snapshot failure");}return true;}
  static void InstallRecoveryProbe(int count){recoveryFailures=count;new Harmony("shop-together.recovery-probe").Patch(AccessTools.Method(typeof(WorldReplica),"Apply"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"InjectReplicaFailure"));}
  static IEnumerator Recovery1315Host(Connection root){
   Signal("RECOVERY_ARM");var wait=Wait(()=>Received("RECOVERY_ARMED"),"client armed snapshot failure",40);while(wait.MoveNext())yield return wait.Current;
   var item=Find(root,"SMC_HOST_ITEM");item.name="RECOVERY_HOST_ITEM";root.World.MarkDirty(item);
   wait=Wait(()=>Received("RECOVERY_RECOVERED"),"four failed updates recovered with a full snapshot",100);while(wait.MoveNext())yield return wait.Current;
   Signal("RECOVERY_FAIL_FIVE");wait=Wait(()=>Received("RECOVERY_DISCONNECTED")&&!root.HasPeer(1),"five consecutive failures disconnect the client",100);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator Recovery1315Client(Connection root){
   var wait=Wait(()=>Received("RECOVERY_ARM"),"recovery fixture",40);while(wait.MoveNext())yield return wait.Current;
   int before=recoveryInjected;recoveryFailures=4;Signal("RECOVERY_ARMED");
   wait=Wait(()=>recoveryInjected==before+4&&root.Ready&&!root.ProbeAwaitingFull&&root.ProbeApplyFailures==0&&Find(root,"RECOVERY_HOST_ITEM")!=null,"four failed applies recover and restore host data",100);while(wait.MoveNext())yield return wait.Current;
   Signal("RECOVERY_RECOVERED");wait=Wait(()=>Received("RECOVERY_FAIL_FIVE"),"five failure fixture",40);while(wait.MoveNext())yield return wait.Current;
   before=recoveryInjected;recoveryFailures=5;
   wait=Wait(()=>recoveryInjected==before+5&&!root.IsClient,"client disconnects only after fifth failed apply",100);while(wait.MoveNext())yield return wait.Current;
   Signal("RECOVERY_DISCONNECTED");
  }
 }
}
#endif
