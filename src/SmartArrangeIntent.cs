using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ShopTogether {
    // Smart Arrange owns its asynchronous solver; only the host runs that solver
    // and commits its result. The friend requests the same installed mod action.
    internal static class SmartArrangeIntent {
        static Type arrange, work;
        static Harmony patches;
        static Context current;
        sealed class Context { internal Connection session; internal GameGridInventory grid; internal int owner, page; internal GameGridInventory[] undoGrids; }
        internal static bool Bound { get { return patches != null; } }
        internal static void Bind() {
            if (patches != null) return;
            arrange = ModReflect.FindType("SmartInventoryArrange.SmartArrangeMod");
            if (arrange == null) return;
            work = ModReflect.FindType("SmartInventoryArrange.GameWork");
            var transaction = ModReflect.FindType("SmartInventoryArrange.InventoryTransaction");
            var undo = ModReflect.FindType("SmartInventoryArrange.ArrangeUndo");
            if (work == null || transaction == null || undo == null) return;
            patches = new Harmony("shop_together.smart-arrange");
            patches.Patch(ModReflect.Target(arrange,"Run"),prefix:new HarmonyMethod(typeof(SmartArrangeIntent),"Propose"),finalizer:new HarmonyMethod(typeof(SmartArrangeIntent),"Restore"));
            patches.Patch(ModReflect.Target(undo,"Start",new[]{typeof(GameGridInventory)}),prefix:new HarmonyMethod(typeof(SmartArrangeIntent),"Undo"),finalizer:new HarmonyMethod(typeof(SmartArrangeIntent),"Restore"));
            patches.Patch(ModReflect.Target(undo,"StartGroup"),prefix:new HarmonyMethod(typeof(SmartArrangeIntent),"UndoGroup"),finalizer:new HarmonyMethod(typeof(SmartArrangeIntent),"Restore"));
            patches.Patch(ModReflect.Target(work,"StartCoroutine"),prefix:new HarmonyMethod(typeof(SmartArrangeIntent),"Schedule"));
            patches.Patch(ModReflect.Target(work,"get_AutomaticAllowed"),postfix:new HarmonyMethod(typeof(SmartArrangeIntent),"HostAutomatic"));
            patches.Patch(ModReflect.Target(transaction,"ApplyCore"),prefix:new HarmonyMethod(typeof(SmartArrangeIntent),"Commit"),finalizer:new HarmonyMethod(typeof(SmartArrangeIntent),"Committed"));
        }
        static bool Propose(GameGridInventory __0,bool __1,out Context __state) {
            __state=current;
            var s=Connection.Current;
            if(s==null||!s.Ready)return !ReplicaSession.ClientStore;
            if(s.IsClient){
                if(!WorldReplica.Applying){ShopActions.RememberSmartSort(__0);var at=s.World.Address(__0);if(at!=null)s.Send(new Request{kind=RequestKind.Sort,items=new int[0],inventory=at,index=StoragePagesIntent.Page(__0),operation=__1?5:4});}
                return false;
            }
            if(!s.IsHost)return true;
            int owner=Math.Max(0,RemoteActions.CurrentOwner);
            if(!InventoryActions.Available(s,owner,__0)){Tell(s,owner,"~SMC328~");return false;}
            current=new Context{session=s,grid=__0,owner=owner,page=StoragePagesIntent.Page(__0)};return true;
        }
        static bool Undo(GameGridInventory __0,ref bool __result,out Context __state) {
            __state=current;var s=Connection.Current;
            if(s==null||!s.Ready)return !ReplicaSession.ClientStore;
            if(s.IsClient){RemoteActions.Forward(RemoteActions.Adapter("UndoPage",2),null,new object[]{__0,StoragePagesIntent.Page(__0)});__result=false;return false;}
            int owner=Math.Max(0,RemoteActions.CurrentOwner);
            if(!InventoryActions.Available(s,owner,__0)){Tell(s,owner,"~SMC328~");__result=false;return false;}
            current=new Context{session=s,grid=__0,owner=owner,page=StoragePagesIntent.Page(__0)};return true;
        }
        static bool UndoGroup(object __0,ref bool __result,out Context __state) {
            __state=current;var s=Connection.Current;
            if(s==null||!s.Ready)return !ReplicaSession.ClientStore;
            if(!s.IsHost){__result=false;return false;}
            if(__0==null)return true;
            var grids=new List<GameGridInventory>();
            var insertion=ModReflect.Get(__0,"Insertion");
            var entries=ModReflect.Get(insertion??__0,"Inventories") as IEnumerable;
            if(entries!=null)foreach(var entry in entries){
                var grid=insertion!=null?entry as GameGridInventory:ModReflect.Get<GameGridInventory>(entry,"Inventory");
                if(grid!=null&&!grids.Contains(grid))grids.Add(grid);
            }
            if(grids.Count==0)return true;
            var context=new Context{session=s,grid=current==null?grids[0]:current.grid,owner=current==null?Math.Max(0,RemoteActions.CurrentOwner):current.owner,page=current==null?StoragePagesIntent.Page(grids[0]):current.page,undoGrids=grids.ToArray()};
            if(!Available(context)){Tell(s,context.owner,"~SMC328~");__result=false;return false;}
            current=context;return true;
        }
        static bool Available(Context context){
            if(context.undoGrids==null)return InventoryActions.Available(context.session,context.owner,context.grid);
            foreach(var grid in context.undoGrids){
                if(!InventoryActions.Available(context.session,context.owner,grid))return false;
                // A grouped undo can restore other pages and containers too.
                foreach(var item in GraphUtils.FindChildrenType<GameItem>(grid,null,null))
                    if(!context.session.Leases.Available(context.owner,context.session.World.Id(item),UnityEngine.Time.realtimeSinceStartup))return false;
            }
            return true;
        }
        static void Restore(Context __state){current=__state;}
        internal static bool Run(GameGridInventory grid,bool preserve){
            if(arrange==null||patches==null)return AuthorityIntent.Refuse("~SMC328~");
            if(ModReflect.GetStatic<bool>(arrange,"Busy"))return AuthorityIntent.Refuse("~SMC125~");
            ModReflect.CallStatic(arrange,"Run",grid,preserve,null,0,0,null);
            return ModReflect.GetStatic<bool>(arrange,"Busy")||AuthorityIntent.Refuse("~SMC328~");
        }
        static void HostAutomatic(ref bool __result){if(ReplicaSession.ClientStore)__result=false;}
        static void Schedule(ref IEnumerator __0){if(current!=null&&__0!=null)__0=new Routine(__0,current);}
        sealed class Routine:IEnumerator,IDisposable {
            readonly IEnumerator inner;readonly Context context;bool disposed;object value;
            internal Routine(IEnumerator inner,Context context){this.inner=inner;this.context=context;}
            public object Current{get{return value;}}
            public bool MoveNext(){
                if(disposed)return false;
                if(Connection.Current!=context.session||!context.session.IsHost||!context.session.Ready){Dispose();return false;}
                // The original solver validates its snapshot; the shared-store lease
                // check prevents it from moving anything another player now holds.
                if(!Available(context)){Tell(context.session,context.owner,"~SMC328~");Dispose();return false;}
                var previous=current;int owner=RemoteActions.CurrentOwner, panel=PanelRelay.Owner;bool executing=NativeActions.Executing;
                current=context;RemoteActions.CurrentOwner=context.owner;PanelRelay.Owner=context.owner;NativeActions.Executing=true;
                try{
                    bool more=StoragePagesIntent.OnPage(context.grid,context.page,()=>inner.MoveNext());
                    value=more?inner.Current:null;if(value is IEnumerator)value=new Routine((IEnumerator)value,context);
                    if(!more){context.session.SyncStateSoon();Dispose();}return more;
                }finally{current=previous;RemoteActions.CurrentOwner=owner;PanelRelay.Owner=panel;NativeActions.Executing=executing;}
            }
            public void Reset(){throw new NotSupportedException();}
            public void Dispose(){if(disposed)return;disposed=true;var d=inner as IDisposable;if(d!=null)d.Dispose();}
        }
        static void Commit(object __instance,out HostNativeScope __state){
            __state=null;var s=Connection.Current;if(s==null||!s.Ready)return;
            if(ReplicaSession.ClientStore)throw new OperationCanceledException("Only the host commits shared inventory arrangements");
            var grid=ModReflect.Get<GameGridInventory>(__instance,"_inventory");int owner=current==null?0:current.owner;
            if(!InventoryActions.Available(s,owner,grid)){Tell(s,owner,"~SMC328~");throw new OperationCanceledException("Inventory is in use");}
            __state=HostNativeScope.Begin();
        }
        static void Committed(HostNativeScope __state){if(__state!=null)__state.Dispose();}
        static void Tell(Connection session,int owner,string note){if(owner==0)RemoteActions.Notice(note);else session.SendTo(owner,37,System.Text.Encoding.UTF8.GetBytes(note));}
        internal static void Shutdown(){if(patches!=null)patches.UnpatchAll("shop_together.smart-arrange");patches=null;arrange=null;work=null;current=null;}
    }
}
