using System;
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
        static int scannedAssemblyCount = -1;
        internal static void Tick() {
            if(Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+1;
            // Missing optional types make Harmony search the loaded assemblies.
            // Retrying those same misses every second stalls the main thread even
            // with no optional mods installed. Mods load as assemblies; retry when
            // that set grows, including a mod loaded after this one initialized.
            int assemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
            if (assemblyCount != scannedAssemblyCount) {
                CompanyIntent.Bind();
                SmartArrangeIntent.Bind(); CargoIntent.Bind(); StoragePagesIntent.Bind(); BarIntent.Bind(); OverflowReplica.Bind();
                foreach(var method in TargetMethods()) {
                    if(installed.Contains(method))continue;
                    if(patches==null)patches=new Harmony("shop_together.mod-runtime-authority");
                    patches.Patch(method,prefix:new HarmonyMethod(typeof(ModRuntimeAuthority),"Prefix"));
                    installed.Add(method);
                }
                // Keep the pre-scan count: assemblies loaded by binding must be
                // considered on the next tick. A failed bind remains retryable.
                scannedAssemblyCount = assemblyCount;
            }
            StoragePagesIntent.Tick();
        }
        internal static void Shutdown() {
            if(patches!=null)patches.UnpatchAll("shop_together.mod-runtime-authority");
            patches=null;installed.Clear();next=0;scannedAssemblyCount=-1;
            CompanyIntent.Shutdown();
            SmartArrangeIntent.Shutdown(); CargoIntent.Shutdown(); StoragePagesIntent.Shutdown(); BarIntent.Shutdown(); OverflowReplica.Shutdown();
        }
        static IEnumerable<MethodBase> TargetMethods() {
            foreach (string name in new[]{"CorpPresence.Runtime.LoadedItemRepair", "CorpPresence.Crates", "CorpPresence.Runtime.RuntimeUpdates"}) {
                var type=ModReflect.FindType(name);
                if(type==null)continue;
                var method=((MethodInfo)ModReflect.Target(type,name.EndsWith("RuntimeUpdates")?"Run":"Tick"));
                if(method!=null)yield return method;
            }
        }
        [HarmonyPriority(Priority.First)]
        static bool Prefix() { return ClientAuthority.Simulate; }
    }
}
