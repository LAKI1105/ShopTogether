using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace ShopTogether {
    [Serializable] internal sealed class OverflowPageState {public int sequence,claimDay,night,width,height;}
    internal static class OverflowReplica {
        const int RootBase=1000;
        static Type type;static Harmony patches;static readonly List<object> retired=new List<object>();
        static object Get(object p,string name){return AccessTools.Field(p.GetType(),name).GetValue(p);}
        static IList Pages {get{return type==null?null:(IList)AccessTools.Field(type,"Pages").GetValue(null);}}
        static object Call(string name,params object[] args){return AccessTools.Method(type,name).Invoke(null,args);}
        internal static void Bind(){
            if(patches!=null)return;type=AccessTools.TypeByName("TrashOverflow");if(type==null)return;
            patches=new Harmony("shop_together.overflow-replica");
            foreach(string method in new[]{"QueueEmptyCheck","BeginNight","EndNight","ClearExpiredPages","RestorePages","SavePages"})patches.Patch(AccessTools.Method(type,method),prefix:new HarmonyMethod(typeof(OverflowReplica),"HostOnly"));
            foreach(string method in new[]{"TryProtect","TryFillOutput"})patches.Patch(AccessTools.Method(type,method),prefix:new HarmonyMethod(typeof(OverflowReplica),"HostResult"));
        }
        static bool HostOnly(){return !ReplicaSession.ClientStore;}
        static bool HostResult(ref bool __result){if(!ReplicaSession.ClientStore)return true;__result=false;return false;}
        internal static IEnumerable<KeyValuePair<int,GameInventory>> Roots(){
            if(Pages==null)yield break;foreach(var p in Pages){int seq=(int)Get(p,"Sequence");if(seq>0&&seq<int.MaxValue-RootBase)yield return new KeyValuePair<int,GameInventory>(RootBase+seq,(GameGridInventory)Get(p,"Grid"));}
        }
        internal static OverflowPageState[] Capture(){
            if(Pages==null)return null;var result=new List<OverflowPageState>();
            foreach(var p in Pages){var grid=(GameGridInventory)Get(p,"Grid");result.Add(new OverflowPageState{sequence=(int)Get(p,"Sequence"),claimDay=(int)Get(p,"ClaimDay"),night=(int)Get(p,"CreatedNight"),width=grid.inventoryShape.width,height=grid.inventoryShape.height});}return result.ToArray();
        }
        internal static void Prepare(OverflowPageState[] states){
            if(states==null)return;Bind();if(Pages==null){if(states.Length>0)throw new InvalidOperationException("Required overflow mod is not loaded");return;}
            AccessTools.Field(type,"_loading").SetValue(null,false);AccessTools.Field(AccessTools.TypeByName("MultiPageStoreFix"),"GameLoaded").SetValue(null,true);
            var seen=new HashSet<int>();foreach(var state in states){if(state.sequence<=0||state.sequence>=int.MaxValue-RootBase||state.width<1||state.width>512||state.height<1||state.height>512||!seen.Add(state.sequence))throw new InvalidOperationException("Invalid overflow page");}
            retired.Clear();foreach(var p in Pages)if(!seen.Contains((int)Get(p,"Sequence")))retired.Add(p);
            foreach(var state in states){object p=null;foreach(var existing in Pages)if((int)Get(existing,"Sequence")==state.sequence){p=existing;break;}
                if(p==null)p=Call("NewPage",state.claimDay,state.night,null,state.sequence);
                AccessTools.Field(p.GetType(),"ClaimDay").SetValue(p,state.claimDay);AccessTools.Field(p.GetType(),"CreatedNight").SetValue(p,state.night);
                var grid=(GameGridInventory)Get(p,"Grid");if(grid.inventoryShape.width!=state.width||grid.inventoryShape.height!=state.height)grid.SetShape(state.width,state.height);
            }
        }
        internal static void Finish(){if(Pages==null)return;foreach(var p in retired)Call("RemovePage",p,true);retired.Clear();Call("RefreshView");}
        internal static void Shutdown(){if(patches!=null)patches.UnpatchAll("shop_together.overflow-replica");patches=null;type=null;retired.Clear();}
    }
}
