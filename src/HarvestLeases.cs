using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    // A butcher may select animals outside its own subtree. Check the actual
    // selected creature before cuts are spawned or the original is killed.
    [HarmonyPatch]
    internal static class HarvestLeases {
        internal static int AutomaticDepth;
        static IEnumerable<MethodBase> TargetMethods() {
            foreach(string name in new[]{"TryStoreAnimal","TryButcherAnimal","TryStoreFish","TryButcherFish"})
                yield return AccessTools.Method(typeof(MachineAutoButcher),name);
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod) {
            var code=new List<CodeInstruction>(instructions);var run=generator.DefineLabel();code[0].labels.Add(run);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Ldarg_1);
            yield return new CodeInstruction(__originalMethod.GetParameters().Length==3?OpCodes.Ldarg_2:OpCodes.Ldnull);
            yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(HarvestLeases),"Allow"));
            yield return new CodeInstruction(OpCodes.Brtrue,run);
            yield return new CodeInstruction(OpCodes.Ldc_I4_2); // Native Outcome.Skipped
            yield return new CodeInstruction(OpCodes.Ret);
            foreach(var instruction in code)yield return instruction;
        }
        internal static bool Allow(GameItem creature, GameInventory tray, GameItem dispenser) {
            if(ReplicaSession.ClientStore && !WorldReplica.Applying)return false;
            var s=Connection.Current;
            if(s==null || !s.IsHost || !s.Ready)return true;
            // Background harvesting owns no player's lease. Manual host actions
            // may use the host's own held item; scheduled harvesting may not.
            int owner=AutomaticDepth>0?-1:RemoteActions.CurrentOwner>=0?RemoteActions.CurrentOwner:LocalControl.UserAction?0:-1;
            foreach(var item in new[]{creature,tray==null?null:tray.GetParentItem(),dispenser}) {
                if(item==null)continue;
                if(DialogueIntent.MayChange(s,item)&&AuthorityIntent.FreeTree(s,owner,item))continue;
                string reason=RemoteActions.PendingNote=="~SMC353~"?"~SMC353~":"~SMC339~";
                if(owner>=0 || NativeActions.Executing)RemoteActions.PendingNote=reason;
                if(RemoteActions.CurrentOwner<0 && LocalControl.UserAction)RemoteActions.Notice(reason);
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(MachineAutoButcher), "Run")]
    internal static class HarvestRunScope {
        static void Prefix(bool manual, out bool __state) {
            __state = !manual;
            if (__state) HarvestLeases.AutomaticDepth++;
        }
        static void Finalizer(bool __state) {
            if (__state) HarvestLeases.AutomaticDepth--;
        }
    }
    [HarmonyPatch]
    internal static class HarvestContentLease {
        static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(HusbandryHelper),"OnButcher");
            yield return AccessTools.Method(typeof(AquacultureHelper),"OnFillet");
        }
        static bool Prefix(GameItem __0) { return HarvestLeases.Allow(__0,null,null); }
    }
    [HarmonyPatch]
    internal static class HarvestSelectionLease {
        static IEnumerable<MethodBase> TargetMethods(){yield return AccessTools.Method(typeof(MachineAutoButcher),"RunCage");yield return AccessTools.Method(typeof(MachineAutoButcher),"RunTank");}
        static void Prefix(ref IEnumerable<GameItem> items,GameGridInventory tray,object[] __args){
            GameItem dispenser=__args.Length==6?__args[2] as GameItem:null;
            if(items!=null)items=items.Where(item=>HarvestLeases.Allow(item,tray,dispenser)).ToList();
        }
    }
}
