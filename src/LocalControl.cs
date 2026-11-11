using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    internal static class LocalControl {
        internal static int depth,BlockedWrites;
#if LAN_PROBE
        internal static bool ProbeInput;
#endif
        internal static bool Focused {get{
#if LAN_PROBE
            if(ProbeInput)return true;
#endif
            return Application.isFocused;
        }}
        internal static bool UserAction {get{return depth>0&&Focused;}}
        internal static bool Replica {get{return ReplicaSession.ClientStore&&ReplicaSession.SceneReady&&!WorldReplica.Applying;}}
        internal static bool AllowWorldWrite(){if(!Replica)return true;BlockedWrites++;return false;}
    }
    [HarmonyPatch]
    internal static class InputScope {
        static IEnumerable<MethodBase> TargetMethods(){
            foreach(var method in typeof(InputActionHandler).GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))if(method.Name=="UpdatePress"||method.Name=="UpdateRelease"||method.Name=="UpdateMouseMove"||method.Name=="UpdateMouseScroll"||method.Name=="UpdateUpdate")yield return method;
            yield return AccessTools.Method(typeof(UnityEngine.UI.Button),"Press");
            // Shop furniture clicked with the mouse (bell, shotgun, vending machines…) is a player action too.
            foreach(var name in new[]{"HandleClick","OnPointerUp","OnDoubleClick"}){var m=AccessTools.Method(typeof(Interactable),name);if(m!=null)yield return m;}
        }
        // Presses, releases and clicks are the player's own input (settings they change are sent to the host);
        // per-frame updates and mouse moves only count as player actions for forwarded functions.
        static bool IsClick(string name){return name=="UpdatePress"||name=="UpdateRelease"||name=="Press"||name=="HandleClick"||name=="OnPointerUp"||name=="OnDoubleClick";}
        [HarmonyPriority(Priority.First)] static bool Prefix(MethodBase __originalMethod,ref int __state){
            if(ReplicaSession.ClientStore&&!LocalControl.Focused&&__originalMethod.Name!="UpdateUpdate")return false;
            __state=0;if(LocalControl.Focused){LocalControl.depth++;__state=1;if(IsClick(__originalMethod.Name)){MachineSettings.Input++;__state=2;}}return true;
        }
        static Exception Finalizer(Exception __exception,int __state){if(__state>0)LocalControl.depth--;if(__state==2&&MachineSettings.Input>0)MachineSettings.Input--;return __exception;}
    }
    // All store decisions run on the host. Explicit request adapters may run before this final barrier.
    [HarmonyPatch]
    internal static class ReplicaStoreBarrier {
        static IEnumerable<MethodBase> TargetMethods(){
            foreach(var method in typeof(PlayerStore).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))if(!method.IsSpecialName&&method.ReturnType==typeof(void)&&method.Name!="LoadPower"&&method.Name!="RefreshCounterItem")yield return method;
            foreach(var method in typeof(StoreStation).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))if(!method.IsSpecialName&&method.ReturnType==typeof(void))yield return method;
            foreach(var method in typeof(StoreClientManager).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))if(!method.IsSpecialName&&method.ReturnType==typeof(void))yield return method;
        }
        [HarmonyPriority(Priority.Last)] static bool Prefix(){if(!ReplicaSession.ClientStore||WorldReplica.Applying)return true;LocalControl.BlockedWrites++;return false;}
    }
    [HarmonyPatch]
    internal static class ReplicaInventoryBarrier {
        static IEnumerable<MethodBase> TargetMethods(){
            yield return AccessTools.Method(typeof(GameGridInventory),"UncheckedAccept");yield return AccessTools.Method(typeof(GameGridInventory),"Expel");
            yield return AccessTools.Method(typeof(GameGridScrollableInventory),"UncheckedAccept");yield return AccessTools.Method(typeof(GameGridScrollableInventory),"Expel");
            yield return AccessTools.Method(typeof(GameSlotInventory),"UncheckedAccept");yield return AccessTools.Method(typeof(GameSlotInventory),"Expel");
            yield return AccessTools.Method(typeof(GameCharacterRaidInventory),"UncheckedAccept");yield return AccessTools.Method(typeof(GameCharacterRaidInventory),"Expel");
        }
        [HarmonyPriority(Priority.Last)] static bool Prefix(ref bool __result){if(LocalControl.AllowWorldWrite())return true;__result=false;return false;}
    }
    [HarmonyPatch(typeof(GameItem),"Create")]
    internal static class ReplicaSpawnBarrier {static bool Prefix(ref GameItem __result){if(LocalControl.AllowWorldWrite())return true;__result=null;return false;}}
    [HarmonyPatch(typeof(GameItemElement),"Destroy")]
    internal static class ReplicaDestroyBarrier {static bool Prefix(){return LocalControl.AllowWorldWrite();}}
    [HarmonyPatch]
    internal static class ReplicaItemDataBarrier {
        static readonly HashSet<string> data=new HashSet<string>{"set_state","set_modifiedState","set_shape","set_modifiedShape","set_unitCount","set_uniqueId","set_unitBaseValue","set_unitValue","set_lateUnitValue","set_backupUnitValue","set_name","set_shortDescription","set_flavorText"};
        static IEnumerable<MethodBase> TargetMethods(){foreach(var method in typeof(GameItem).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))if(data.Contains(method.Name))yield return method;}
        static bool Prefix(GameItem __instance){var session=Connection.Current;return !LocalControl.Replica||session==null||session.World.Id(__instance)==0||LocalControl.AllowWorldWrite();}
    }
}
