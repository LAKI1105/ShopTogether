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
            int owner=System.Math.Max(0,RemoteActions.CurrentOwner);
            foreach(var item in new[]{creature,tray==null?null:tray.GetParentItem(),dispenser}) {
                if(item==null)continue;
                if(AuthorityIntent.FreeTree(s,owner,item))continue;
                if(owner>0 || NativeActions.Executing)RemoteActions.PendingNote="~SMC339~";
                else if(LocalControl.UserAction)RemoteActions.Notice("~SMC339~");
                return false;
            }
            return true;
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
