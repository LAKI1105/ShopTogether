using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
    // Native hopper transfers already branch on CanAccept and preserve the item when false.
    // Include the selected item and destination in that host-side decision before moving it.
    [HarmonyPatch]
    internal static class HopperLeases {
        static IEnumerable<MethodBase> TargetMethods(){yield return AccessTools.Method(typeof(HopperHelper),"RunUnguarded");yield return AccessTools.Method(typeof(HopperHelper),"TryRouteOverflow");}
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions){
            var original=AccessTools.Method(typeof(GraphUtils),"CanAccept",new[]{typeof(GraphNodeStorage),typeof(GameItem)});
            foreach(var i in instructions){if(Equals(i.operand,original)){i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(HopperLeases),"CanAccept");}yield return i;}
        }
        internal static bool CanAccept(GraphNodeStorage destination,GameItem item){
            var s=Connection.Current;
            if(s!=null&&s.IsHost&&s.Ready){
                int owner=RemoteActions.CurrentOwner>=0?RemoteActions.CurrentOwner:LocalControl.UserAction?0:-1;
                var inventory=destination as GameInventory;var parent=destination as GameItem??(inventory==null?null:inventory.GetParentItem());
                foreach(var used in new[]{item,parent})if(used!=null&&!s.Leases.Available(owner,s.World.Id(used),Time.realtimeSinceStartup)){
                    if(owner>=0)RemoteActions.PendingNote="~SMC339~";return false;
                }
            }
            return GraphUtils.CanAccept(destination,item);
        }
    }
}
