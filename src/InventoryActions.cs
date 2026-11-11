using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    internal static class InventoryActions {
        internal static bool Available(Connection session,int owner,GameGridInventory inventory){
            if(inventory==null||!InventorySortHelper.MaySort(inventory)||!InventorySortHelper.CanSort(inventory))return false;
            foreach(var item in GraphUtils.FindChildrenType<GameItem>(inventory,null,null))if(!session.Leases.Available(owner,session.World.Id(item),Time.realtimeSinceStartup))return false;
            var address=session.World.Address(inventory);return address!=null&&OutingKits.MayUse(owner,address.root)&&(address.container==0||session.Leases.Available(owner,address.container,Time.realtimeSinceStartup));
        }
        internal static bool Execute(Connection session,int owner,Request request){
            GameInventory inventory;if(request.operation<0||request.operation>3||request.inventory==null||!session.World.Inventories.TryGetValue(request.inventory.Key,out inventory))return false;
            var grid=inventory as GameGridInventory;if(!Available(session,owner,grid))return false;
            return InventorySortHelper.Sort(grid,(request.operation&1)!=0,(request.operation&2)!=0);
        }
    }
    [HarmonyPatch(typeof(InventorySortHelper),"Sort")]
    internal static class SharedInventorySort {
        [HarmonyPriority(Priority.First)] static bool Prefix(GameGridInventory __0,bool __1,bool __2,ref bool __result){
            if(!NativeActions.Active)return !ReplicaSession.IsReplica;
            var session=NativeActions.Session;
            if(session.IsHost)return InventoryActions.Available(session,0,__0);
            __result=false;if(!LocalControl.UserAction)return false;
            var address=session.World.Address(__0);if(address!=null)session.Send(new Request{kind=RequestKind.Sort,items=new int[0],inventory=address,operation=(__1?1:0)|(__2?2:0)});
            return false;
        }
    }
}
