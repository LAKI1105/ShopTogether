using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    // These installed mod tasks bypass ModHook.Fire*. Keep presentation tasks
    // running locally, but leave repair, empty-crate destruction and scheduled
    // gameplay to the host whose state is replicated.
    internal static class ModRuntimeAuthority {
        static readonly HashSet<MethodBase> installed=new HashSet<MethodBase>();
        static Harmony patches;
        static float next;
        internal static void Tick() {
            if(Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+1;
            CompanyIntent.Bind();
            CargoIntent.Bind(); StoragePagesIntent.Bind(); BarIntent.Bind(); OverflowReplica.Bind();
            StoragePagesIntent.Tick();
            foreach(var method in TargetMethods()) {
                if(installed.Contains(method))continue;
                if(patches==null)patches=new Harmony("shop_together.mod-runtime-authority");
                patches.Patch(method,prefix:new HarmonyMethod(typeof(ModRuntimeAuthority),"Prefix"));
                installed.Add(method);
            }
        }
        internal static void Shutdown() {
            if(patches!=null)patches.UnpatchAll("shop_together.mod-runtime-authority");
            patches=null;installed.Clear();next=0;
            CompanyIntent.Shutdown();
            CargoIntent.Shutdown(); StoragePagesIntent.Shutdown(); BarIntent.Shutdown(); OverflowReplica.Shutdown();
        }
        static IEnumerable<MethodBase> TargetMethods() {
            foreach (string name in new[]{"CorpPresence.Runtime.LoadedItemRepair", "CorpPresence.Crates", "CorpPresence.Runtime.RuntimeUpdates"}) {
                var type=AccessTools.TypeByName(name);
                if(type==null)continue;
                var method=AccessTools.Method(type,name.EndsWith("RuntimeUpdates")?"Run":"Tick");
                if(method!=null)yield return method;
            }
        }
        [HarmonyPriority(Priority.First)]
        static bool Prefix() { return ClientAuthority.Simulate; }
    }
}
