using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ShopTogether {
    // A replica owns presentation only. It never starts a second store simulation.
    internal static class ClientAuthority {
        internal static bool Simulate {get{return !ReplicaSession.ClientStore;}}
    }

    [HarmonyPatch]
    internal static class ClientWorldStart {
        static IEnumerable<MethodBase> TargetMethods(){
            foreach(string name in new[]{"StartNewGame","LoadGame","HandleSkipIntro"})yield return AccessTools.Method(typeof(PlayerStore),name);
            yield return AccessTools.Method(typeof(EmporiumEntry),"AddStartingItem");
            yield return AccessTools.Method(typeof(StartingPerkContent),"HandleNewGamePerkBeforeItems");
            yield return AccessTools.Method(typeof(StartingPerkContent),"HandleNewGamePerk");
            yield return AccessTools.Method(typeof(StoreStation),"StartDay");
            yield return AccessTools.Method(typeof(StoreClientManager),"GenerateClient");
            yield return AccessTools.Method(typeof(StoreClientManager),"ClientGenCheck");
        }
        [HarmonyPriority(Priority.First)] static bool Prefix(){return ClientAuthority.Simulate;}
    }

    [HarmonyPatch(typeof(PlayerStore),"PreLoadStore")]
    internal static class ClientPreload {
        [HarmonyPriority(Priority.First)] static bool Prefix(ref PlayerStore __result){if(!ReplicaSession.ClientStore)return true;if(ReplicaSession.Seed!=null){Connection.Current.World.Prime(ReplicaSession.Seed);ReplicaSession.Seed=null;}__result=PlayerStore.Instance;return false;}
    }

    [HarmonyPatch(typeof(EmporiumEntry),"Update")]
    internal static class ClientItemClock {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,ILGenerator generator){
            var code=new List<CodeInstruction>(instructions);
            int start=-1;
            for(int i=2;i<code.Count;i++)if(code[i].opcode==OpCodes.Ldfld&&code[i].operand is FieldInfo&&((FieldInfo)code[i].operand).Name=="oneSecondTimer"){start=i-2;break;}
            if(start<0||code[start].opcode!=OpCodes.Ldarg_0||code[start+1].opcode!=OpCodes.Ldarg_0)throw new InvalidOperationException("Unsupported EmporiumEntry item clock; refusing replica mode");
            var resume=generator.DefineLabel();
            var check=new CodeInstruction(OpCodes.Call,AccessTools.PropertyGetter(typeof(ClientAuthority),"Simulate"));
            check.labels.AddRange(code[start].labels);code[start].labels.Clear();code[start].labels.Add(resume);
            code.InsertRange(start,new[]{check,new CodeInstruction(OpCodes.Brtrue,resume),new CodeInstruction(OpCodes.Ret)});
            return code;
        }
    }

    // Mod extension callbacks: default-deny on a replica. Only callbacks that build presentation or register
    // definitions run on every machine; every gameplay callback (including ones added by future game versions)
    // runs once, on the host, and its results arrive through world sync. This is what keeps other mods'
    // random rolls, spawns and money changes from happening twice or differently on a friend's machine.
    [HarmonyPatch]
    internal static class ClientWorldHooks {
        internal static readonly HashSet<string> Presentation=new HashSet<string>{
            "FireOnModItemDirectoryInit","FireOnModAssetsLoaded","FireOnModConfigApplied","FireOnLootTablesLoaded",
            "FireOnCreateTooltipEarly","FireOnCreateTooltipLate","FireOnCreateDevTooltipEarly","FireOnCreateDevTooltipLate",
            "FireOnPaperOpened"};
        internal static int Blocked;
        static IEnumerable<MethodBase> TargetMethods(){
            foreach(var method in typeof(ModHook).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.DeclaredOnly))
                if(method.Name.StartsWith("Fire",StringComparison.Ordinal)&&!Presentation.Contains(method.Name)&&!method.ContainsGenericParameters)yield return method;
        }
        [HarmonyPriority(Priority.First)] static bool Prefix(){if(ClientAuthority.Simulate)return true;Blocked++;return false;}
    }
}
