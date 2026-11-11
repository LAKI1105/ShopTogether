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
        /// <summary>
        /// Friend: the game also builds throw-away items purely for display (choice icons, previews, shop listings).
        /// Those are not part of the shared shop, so they may be created, moved between and destroyed in containers
        /// that are not part of it either. Anything the host tracks stays read-only here.
        /// </summary>
        internal static bool LocalOnly(GameInventory inventory,GameItem item){var s=Connection.Current;if(s==null||!ReplicaSession.ClientStore)return false;var w=s.World;return (inventory==null||w.Address(inventory)==null)&&(item==null||w.Id(item)<=0);}
        // Choice icons belong only to this player's temporary response windows, never to a synced inventory.
        internal static bool LocalChoice(GameInventory inventory,GameItem item){var e=EmporiumEntry.Instance;return e!=null&&inventory!=null&&(inventory==e.responseInventory||inventory==e.responseInventoryClosable)&&LocalOnly(inventory,item);}
    }
    [HarmonyPatch]
    internal static class InputScope {
        static IEnumerable<MethodBase> TargetMethods(){
            foreach(var method in typeof(InputActionHandler).GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))if(method.Name=="UpdatePress"||method.Name=="UpdateRelease"||method.Name=="UpdateMouseMove"||method.Name=="UpdateMouseScroll"||method.Name=="UpdateUpdate")yield return method;
            yield return AccessTools.Method(typeof(UnityEngine.UI.Button),"Press");
            // Scratching is processed during the UI component's Update while the mouse is held.
            yield return AccessTools.Method(typeof(ScratchInput),"Update");
            // Shop furniture clicked with the mouse (bell, shotgun, vending machines…) is a player action too.
            foreach(var name in new[]{"HandleClick","OnPointerUp","OnDoubleClick"}){var m=AccessTools.Method(typeof(Interactable),name);if(m!=null)yield return m;}
        }
        // Presses, releases and clicks are the player's own input (settings they change are sent to the host);
        // per-frame updates and mouse moves only count as player actions for forwarded functions.
        static bool IsClick(string name){return name=="UpdatePress"||name=="UpdateRelease"||name=="Press"||name=="HandleClick"||name=="OnPointerUp"||name=="OnDoubleClick";}
        [HarmonyPriority(Priority.First)] static bool Prefix(MethodBase __originalMethod,ref int __state){
            if(ReplicaSession.ClientStore&&!LocalControl.Focused&&__originalMethod.Name!="UpdateUpdate")return false;
            if(__originalMethod.DeclaringType==typeof(ScratchInput)&&!UnityEngine.Input.GetMouseButton(0))return true;
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
        [HarmonyPriority(Priority.Last)] static bool Prefix(object __instance,GameItem __0,MethodBase __originalMethod,ref bool __result){
            if(WorldReplica.Sandbox>0){if(__0!=null&&__0==WorldReplica.SandboxItem&&WorldReplica.SandboxMovePermit){WorldReplica.SandboxMovePermit=false;return true;}__result=false;return false;}
            if(PanelRelay.IsCratePreview(__instance as GameInventory,__0)&&(__originalMethod.Name=="Expel"||PanelRelay.BuildingCratePreview))return true;
            if(LocalControl.LocalOnly(__instance as GameInventory,__0))return true;
            if(LocalControl.AllowWorldWrite())return true;__result=false;return false;
        }
    }
    // The feed dispenser schedules its blend for a later frame, after the apply sandbox has ended.
    // Only the host may schedule that game-logic work; its result is replicated back to guests.
    [HarmonyPatch(typeof(MachineFeedDispenser), "OnItemInserted")]
    internal static class ReplicaFeedBlendBarrier {
        static bool Prefix(){return !ReplicaSession.ClientStore||WorldReplica.Sandbox==0;}
    }
    // Decoding calls the lottery factory before restoring the host's saved tags. Do not draw a
    // new number or alter the guest's lottery history while reconstructing that host item.
    [HarmonyPatch(typeof(LotteryHelper), "InitLotteryTicket")]
    internal static class ReplicaLotteryFactoryBarrier {
        static bool Prefix(){return !(ReplicaSession.ClientStore&&WorldReplica.Applying&&SaveManager.IsDecoding);}
    }
    [HarmonyPatch(typeof(GameItem),"Create")]
    internal static class ReplicaSpawnBarrier {static bool Prefix(ref GameItem __result){if(WorldReplica.Sandbox==0)return true;__result=null;return false;}}   // a new item is outside the shared shop until the host places it
    [HarmonyPatch(typeof(GameItemElement),"Destroy")]
    internal static class ReplicaDestroyBarrier {static bool Prefix(GameItemElement __instance){return WorldReplica.Sandbox==0&&(PanelRelay.IsCratePreview(__instance.parentInventory,__instance)||LocalControl.LocalOnly(__instance.parentInventory,__instance)||LocalControl.AllowWorldWrite());}}
    [HarmonyPatch]
    internal static class ReplicaItemDataBarrier {
        static readonly HashSet<string> data=new HashSet<string>{"set_state","set_modifiedState","set_shape","set_modifiedShape","set_unitCount","set_uniqueId","set_unitBaseValue","set_unitValue","set_lateUnitValue","set_backupUnitValue","set_name","set_shortDescription","set_flavorText"};
        static IEnumerable<MethodBase> TargetMethods(){foreach(var method in typeof(GameItem).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))if(data.Contains(method.Name))yield return method;}
        static bool Prefix(GameItem __instance){var session=Connection.Current;return !LocalControl.Replica||session==null||session.World.Id(__instance)==0||LocalControl.AllowWorldWrite();}
    }
}
