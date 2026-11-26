using System;
using System.Reflection;
using HarmonyLib;

namespace ShopTogether {
    internal static class StoragePagesIntent {
        static Type type;static Harmony patches;static int scope;static string lastMetadata;
        static object Current {get{return type==null?null:ModReflect.GetStatic(type,"Current");}}
        static int Number(object value,string field){return (int)ModReflect.Get(value,field);}
        static object Call(object value,string method,params object[] args){return value==null?ModReflect.CallStatic(type,method,args):ModReflect.Call(value,method,args);}
        internal static void Tick(){if(type==null||!ReplicaSession.ClientStore||Connection.Current==null||!Connection.Current.Ready||EmporiumEntry.Instance==null)return;
            bool previous=WorldReplica.Applying;WorldReplica.Applying=true;try{if(Current==null){Call(null,"Attach");lastMetadata=null;}if(Current!=null)RestoreReplica(Current);}finally{WorldReplica.Applying=previous;}}
        internal static void Bind(){
            if(patches!=null)return;type=ModReflect.FindType("ShopStoragePages.Pages");if(type==null)return;
            patches=new Harmony("shop_together.storage-pages-intent");
            patches.Patch(ModReflect.Target(type,"SetName",new[]{typeof(int),typeof(string)}),prefix:new HarmonyMethod(typeof(StoragePagesIntent),"Propose"));
            patches.Patch(ModReflect.Target(type,"ConfirmUnlock",new[]{typeof(int)}),prefix:new HarmonyMethod(typeof(StoragePagesIntent),"Propose"));
            foreach(string method in new[]{"CanUseControls","CanOpenMenu"})patches.Patch(((MethodInfo)ModReflect.Target(type,method)),prefix:new HarmonyMethod(typeof(StoragePagesIntent),"Controls"));
            patches.Patch(((MethodInfo)ModReflect.Target(type,"Unlock")),prefix:new HarmonyMethod(typeof(StoragePagesIntent),"BeforeUnlock"),finalizer:new HarmonyMethod(typeof(StoragePagesIntent),"AfterUnlock"));
            patches.Patch(((MethodInfo)ModReflect.Target(type,"RestoreRecordedProgress")),prefix:new HarmonyMethod(typeof(StoragePagesIntent),"RestoreReplica"));
        }
        static bool RestoreReplica(object __instance){
            if(!ReplicaSession.ClientStore)return true;
            var s=Connection.Current;if(s==null||!s.Ready)return false;
            string json=ModHelper.GetModData("player.shop_storage_pages","page_state_v1");if(json==lastMetadata)return false;
            var saved=((MethodInfo)ModReflect.Target(ModReflect.FindType("ShopStoragePages.LoadPages"),"ReadMetadata")).Invoke(null,new object[]{json});
            bool previous=WorldReplica.Applying;WorldReplica.Applying=true;try{Call(__instance,"Restore",saved,null);lastMetadata=json;}finally{WorldReplica.Applying=previous;}return false;
        }
        static bool Propose(MethodBase __originalMethod,object[] __args){
            if(!ReplicaSession.ClientStore)return true;
            RemoteActions.Forward(RemoteActions.Adapter("StoragePageAction",3),null,new object[]{__originalMethod.Name,(int)__args[0],__args.Length>1?(string)__args[1]:""});return false;
        }
        static bool Controls(ref bool __result){if(scope<=0)return true;__result=true;return false;}
        sealed class UnlockState {internal int page,unlocked;internal bool remote;}
        static bool BeforeUnlock(object __instance,int index,int price,PlayerStore store,out UnlockState __state){
            __state=null;var s=Connection.Current;if(s==null||!s.IsHost||!s.Ready)return true;
            if(RemoteActions.CurrentOwner<0)RemoteActions.PendingNote=null;
            if(!DialoguePayment.AllowSpending())return false;
            Call(__instance,"RestoreRecordedProgress");
            if(SaveManager.IsDecoding||PlayerStore.Instance!=store||index!=Number(__instance,"unlocked")||index>=10||index<0||price!=(int)((MethodInfo)ModReflect.Target(ModReflect.FindType("ShopStoragePages.PagePrices"),"ForPage")).Invoke(null,new object[]{index}))RemoteActions.PendingNote="~SMC326~";
            else if(!PlayerStore.Instance.CanPay(price))RemoteActions.PendingNote="~SMC343~";
            if(RemoteActions.PendingNote!=null){if(RemoteActions.CurrentOwner<0)RemoteActions.Notice(RemoteActions.PendingNote);return false;}
            __state=new UnlockState{page=Number(__instance,"page"),unlocked=Number(__instance,"unlocked"),remote=RemoteActions.CurrentOwner>0};if(__state.remote)scope++;
            return true;
        }
        static void AfterUnlock(object __instance,UnlockState __state){if(__state==null)return;try{
            if(Number(__instance,"unlocked")==__state.unlocked&&RemoteActions.PendingNote==null){RemoteActions.PendingNote="~SMC369~";if(RemoteActions.CurrentOwner<0)RemoteActions.Notice(RemoteActions.PendingNote);}
            if(__state.remote){ModReflect.Set(__instance,"page",__state.page);Call(__instance,"RefreshCaption");}
            var s=Connection.Current;if(s!=null)s.SyncStateSoon();
        }finally{if(__state.remote)scope--;}}
        internal static void StoragePageAction(string action,int page,string name){
            var current=Current;if(current==null||PlayerStore.Instance==null||SaveManager.IsDecoding){RemoteActions.PendingNote="~SMC326~";return;}
            Call(current,"RestoreRecordedProgress");int count=Number(current,"unlocked");
            if(page<0||page>=10||(action=="SetName"?page>=count:action!="ConfirmUnlock"||page!=count)){RemoteActions.PendingNote="~SMC326~";return;}
            if(action=="ConfirmUnlock"&&GenericConfirmationUIManager.Instance.panel.activeInHierarchy){RemoteActions.PendingNote="~SMC125~";return;}
            scope++;try{if(action=="SetName"){Call(current,action,page,name??"");RemoteActions.PendingNote="~SMC341~";}else Call(current,action,page);}finally{scope--;}
        }
        internal static int Page(GameGridInventory inventory){var current=Current;return current!=null&&ModReflect.Get(current,"grid")==inventory?Number(current,"page"):0;}
        internal static bool Includes(GameGridInventory inventory,GameItem item){var current=Current;if(current==null||ModReflect.Get(current,"grid")!=inventory)return true;
            var view=((MethodInfo)ModReflect.Target(ModReflect.FindType("ShopStoragePages.SortView"),"Create")).Invoke(null,new object[]{inventory});return view==null||(bool)((MethodInfo)ModReflect.Target(view.GetType(),"Includes")).Invoke(view,new object[]{item.modifiedShape});}
        internal static bool OnPage(GameGridInventory inventory,int page,Func<bool> action){
            var current=Current;if(current==null||ModReflect.Get(current,"grid")!=inventory)return page==0&&action();
            if(page<0||page>=Number(current,"unlocked"))return AuthorityIntent.Refuse("~SMC326~");
            int old=Number(current,"page");ModReflect.Set(current,"page",page);
            try{return action();}finally{ModReflect.Set(current,"page",old);}
        }
        internal static void UndoPage(GameInventory inventory,int page){var grid=inventory as GameGridInventory;if(grid==null){RemoteActions.PendingNote="~SMC328~";return;}OnPage(grid,page,()=>{ShopActions.UndoSort(inventory);return true;});}
        internal static void Shutdown(){if(patches!=null)patches.UnpatchAll("shop_together.storage-pages-intent");patches=null;type=null;scope=0;lastMetadata=null;}
    }
}
