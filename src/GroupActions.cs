using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    [HarmonyPatch]
    internal static class AutoPlaceRequest {
        static MethodBase TargetMethod(){return AccessTools.Method(typeof(GraphUtils),"TryAcceptAll",new[]{typeof(GraphNodeStorage),typeof(GameItem),typeof(int)});}
        [HarmonyPriority(Priority.First)] static bool Prefix(GraphNodeStorage __0,GameItem __1,int __2,ref int __result){
            if(PanelRelay.BuildingCratePreview&&PanelRelay.IsCratePreview(__0 as GameInventory,__1))return true;
            if(LocalControl.LocalChoice(__0 as GameInventory,__1))return true;
            if(!NativeActions.Active)return true;var session=NativeActions.Session;int id=session.World.Id(__1);
            if(session.IsHost)return session.Leases.Available(0,id,Time.realtimeSinceStartup);
            __result=0;if(!LocalControl.UserAction||id<=0)return false;
            var inventory=__0 as GameInventory;var target=__0 as GameItem;var address=session.World.Address(inventory);int targetId=session.World.Id(target);
            if(address==null&&targetId==0)return false;long version;session.World.Versions.TryGetValue(id,out version);// The game passes -1 for "the whole stack" (group drags, quick transfers).
            int count=__2<0?__1.unitCount:Math.Min(__2,__1.unitCount);if(count<1)return false;
            session.Submit(new Request{kind=RequestKind.AutoPlace,items=new[]{id},inventory=address,target=targetId,moves=new[]{new Placement{item=id,count=count,version=version}}});__result=count;return false;
        }
    }
    [HarmonyPatch(typeof(ItemMultiSelectHandler),"StartGroupDrag")]
    internal static class GroupClaim {
        static bool Prefix(ItemMultiSelectHandler __instance){if(!NativeActions.Active)return true;var session=NativeActions.Session;var selected=ModReflect.Get<List<GameItemElement>>(__instance,"selectedItems");if(session.IsClient&&selected.Count>128){RemoteActions.Notice("~SMC246~");return false;}var ids=new List<int>();foreach(var item in selected){if(session.IsHost&&(ProbablyMultiplayerCompat.TradeLocked||ProbablyMultiplayerCompat.Protected(item))){RemoteActions.Notice("~SMC272~");return false;}int id=session.World.Id(item);if(id>0)ids.Add(id);}return ids.Count==0||NativeActions.Hold(__instance,ids.ToArray());}
    }
    [HarmonyPatch(typeof(ItemMultiSelectHandler),"ClearSelection")]
    internal static class GroupRelease {static void Postfix(ItemMultiSelectHandler __instance){NativeActions.Unhold(__instance);}}
    [HarmonyPatch(typeof(ItemMultiSelectHandler),"CommitPrecisePlacement")]
    internal static class GroupPlaceRequest {
        [HarmonyPriority(Priority.First)] static bool Prefix(ItemMultiSelectHandler __instance,GameInventory __0,int __1,int __2){
            if(!NativeActions.Active)return true;var session=NativeActions.Session;
            var selected=ModReflect.Get<List<GameItemElement>>(__instance,"selectedItems");var ids=new List<int>();var moves=new List<Placement>();
            foreach(var item in selected){int id=session.World.Id(item);if(id<=0)return false;if(session.IsHost){if(ProbablyMultiplayerCompat.TradeLocked||ProbablyMultiplayerCompat.Protected(item)||!session.Leases.Available(0,id,Time.realtimeSinceStartup))return false;continue;}
                GridShape shape=(GridShape)ModReflect.CallStatic(typeof(ItemMultiSelectHandler),"TranslatedShape",new object[]{item,__1,__2});long version;session.World.Versions.TryGetValue(id,out version);ids.Add(id);moves.Add(new Placement{item=id,count=item.unitCount,x=shape.minX,y=shape.minY,rotation=shape.orientation,flip=shape.flipped,version=version,inventory=session.World.Address(__0)});
            }
            if(session.IsHost)return true;if(ids.Count>128){RemoteActions.Notice("~SMC246~");return false;}if(LocalControl.UserAction)session.Submit(new Request{kind=RequestKind.Move,items=ids.ToArray(),moves=moves.ToArray()});return false;
        }
    }
}
