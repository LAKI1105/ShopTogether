using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace ShopTogether {
    [Serializable] internal sealed class ChoiceView {public string title,text;public bool enabled;}
    [Serializable] internal sealed class StoreView {
        public long customer,dialog;
        public Dictionary<string,string> customerFields;
        public string sprite,title,text;
        public bool hasDialog;
        public ChoiceView[] choices;
        public int negotiatedItem;
        public int clientsLeft;
        public int[] judged,buying,buyingShallow;
        public BarterView barter;
        public GunOrderView gunOrder;
    }
    // Only resolved customer values and text cross the wire. No delegates or dialogue actions are serialized.
    internal sealed class StorePresentation {
        static readonly List<FieldInfo> fields=Fields();
        StoreClientInstance lastCustomer;Dialogue lastDialog;string lastLine;long customerSerial,dialogSerial;
        long appliedCustomer=-1,appliedDialog=-1;
        string appliedSprite;
        static List<FieldInfo> Fields(){var result=new List<FieldInfo>();foreach(var field in typeof(StoreClient).GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)){if(field.Name.StartsWith("<",StringComparison.Ordinal))continue;var t=field.FieldType;if(t.IsPrimitive||t.IsEnum||t==typeof(string)||t==typeof(List<string>)||t==typeof(double?))result.Add(field);}return result;}
        internal static GameInventory CustomerInventory(){var customer=PlayerStore.Instance.currentClientInstance;return customer==null?null:ModReflect.Get<GameGridInventory>(customer,"gridInv");}
        internal StoreView Capture(WorldReplica world){
            var instance=PlayerStore.Instance.currentClientInstance;if(instance!=lastCustomer){lastCustomer=instance;customerSerial++;}
            var ui=DialogUIManager.Instance;var dialog=ui==null?null:ui.currentDialog;
            var manager=PlayerStore.Instance.storeClientManager;
            var result=new StoreView{customer=instance==null?0:customerSerial,hasDialog=dialog!=null,negotiatedItem=world.Id(PlayerStore.Instance.currentNegociatedItem),clientsLeft=manager!=null&&manager.clientStack!=null?manager.clientStack.Count:0};
            if(instance!=null){var blueprint=instance.GetClientBlueprint();Diagnostics.Soft("customer deals",()=>CustomerDeals.Capture(blueprint,world,result));result.customerFields=new Dictionary<string,string>();foreach(var field in fields)result.customerFields[field.Name]=JsonConvert.SerializeObject(ModReflect.Get(blueprint,field.Name));result.sprite=blueprint.spriteName;}
            if(dialog!=null){result.title=dialog.title;result.text=dialog.plainText;var choices=new List<ChoiceView>();if(dialog.choices!=null)for(int i=0;i<dialog.choices.Count;i++){var line=dialog.choices[i].Item2;choices.Add(new ChoiceView{title=line.title,text=line.plainText,enabled=dialog.IsChoiceEnabledAt(i)});}result.choices=choices.ToArray();}
            string lineSignature=JsonConvert.SerializeObject(new object[]{result.title,result.text,result.choices});if(dialog!=lastDialog||lineSignature!=lastLine){lastDialog=dialog;lastLine=lineSignature;dialogSerial++;}result.dialog=dialogSerial;return result;
        }
        internal bool CurrentDialogue(long revision){return revision==dialogSerial&&DialogUIManager.Instance!=null&&DialogUIManager.Instance.currentDialog==lastDialog;}
        internal long DialogRevision {get{return appliedDialog;}}
        /// <summary>Friend: customers still waiting today (the queue itself only exists on the host).</summary>
        internal static int ClientsLeft;
        internal void ApplyCustomer(StoreView view,WorldReplica world){
            if(view==null)return;ProbablyMultiplayerCompat.EnsurePortrait(view.sprite);ClientsLeft=Math.Max(0,Math.Min(1000,view.clientsLeft));
            bool changed=view.customer!=appliedCustomer;
            if(changed){
                var old=CustomerInventory();if(old!=null)foreach(var item in GraphUtils.FindChildrenType<GameItem>(old,null,null))item.Destroy();
                PlayerStore.Instance.currentClientInstance=view.customer==0?null:new StoreClientInstance();
                if(view.customer!=0)ModReflect.Set(PlayerStore.Instance.currentClientInstance,"storeClient",new StoreClient());
                appliedCustomer=view.customer;
            }
            if(view.customer==0){CustomerDeals.Reset();if(StoreUIManager.Instance!=null)StoreUIManager.Instance.HideClient();return;}
            var blueprint=PlayerStore.Instance.currentClientInstance.GetClientBlueprint();
            foreach(var field in fields){string value;if(view.customerFields!=null&&view.customerFields.TryGetValue(field.Name,out value))ModReflect.Set(blueprint,field.Name,JsonConvert.DeserializeObject(value,field.FieldType,new JsonSerializerSettings{TypeNameHandling=TypeNameHandling.None,MetadataPropertyHandling=MetadataPropertyHandling.Ignore,MaxDepth=8}));}
            Diagnostics.Soft("customer deals",()=>CustomerDeals.Apply(blueprint,view,world,changed));
            if(StoreClientMono.Instance!=null){if(appliedSprite!=view.sprite&&!string.IsNullOrEmpty(view.sprite)){StoreClientMono.Instance.image.sprite=SpriteDict.Instance.ShowClientSprite(view.sprite);appliedSprite=view.sprite;}if(changed){StoreUIManager.Instance.DisplayClient();StoreClientMono.Instance.SetImmediatlyArrive();}}
        }
        internal void ApplyDialogue(StoreView view,WorldReplica world){
            if(view==null)return;GameItem item;PlayerStore.Instance.currentNegociatedItem=world.Items.TryGetValue(view.negotiatedItem,out item)?item:null;
            if(view.dialog==appliedDialog)return;appliedDialog=view.dialog;var ui=DialogUIManager.Instance;if(ui==null)return;
            ui.HideAllDialogUI();if(!view.hasDialog){ui.currentDialog=null;return;}
            var dialog=new Dialogue().SetText(view.title??"",view.text??"");dialog.choices=new List<ValueTuple<GameItem,Dialogue,Action>>();
            if(view.choices!=null)foreach(var choice in view.choices){var line=new Dialogue().SetText(choice.title??"",choice.text??"");line.forceDisabled=!choice.enabled;dialog.choices.Add(new ValueTuple<GameItem,Dialogue,Action>(null,line,null));}
            ui.currentDialog=dialog;ModReflect.Call(ui,"DisplayLine",new object[]{dialog});if(dialog.choices.Count>0)ui.DisplayChoice();
        }
    }
    [HarmonyPatch]
    internal static class ReplicaDialogueInput {
        static IEnumerable<MethodBase> TargetMethods(){yield return AccessTools.Method(typeof(DialogUIManager),"OnAdvancePressed");yield return AccessTools.Method(typeof(DialogUIManager),"SelectChoice");}
        [HarmonyPriority(Priority.First)] static bool Prefix(MethodBase __originalMethod,object[] __args){if(!ReplicaSession.ClientStore)return true;var session=Connection.Current;if(session!=null&&session.Ready&&LocalControl.Focused&&!WorldReplica.Applying)session.Send(new Request{kind=RequestKind.ShopAction,items=new int[0],operation=(int)(__originalMethod.Name=="SelectChoice"?ShopOperation.ChooseDialogue:ShopOperation.AdvanceDialogue),index=__args.Length==0?0:(int)__args[0],worldVersion=session.World.Presentation.DialogRevision});return false;}
    }
    [HarmonyPatch]
    internal static class ReplicaCustomerSimulation {
        static IEnumerable<MethodBase> TargetMethods(){yield return AccessTools.Method(typeof(StoreClientMono),"OnArrived");yield return AccessTools.Method(typeof(StoreClientMono),"OnLeft");yield return AccessTools.Method(typeof(StoreClientMono),"PlayIdleForCurrentClient");}
        static bool Prefix(){return !ReplicaSession.ClientStore;}
    }
    /// <summary>Friend: the counter of customers left reads the host's queue size.</summary>
    [HarmonyPatch(typeof(StoreClientCounter),"Update")]
    internal static class ReplicaClientCounter {
        static bool Prefix(StoreClientCounter __instance){
            if(!ReplicaSession.ClientStore||PlayerStore.Instance==null)return true;
            var tmp=__instance.clientCounterTMP;if(tmp==null)return false;
            var state=PlayerStore.Instance.storeState;
            if(state==PlayerStore.StoreState.OPEN)tmp.text=StorePresentation.ClientsLeft.ToString();
            else if(state==PlayerStore.StoreState.CLOSED||state==PlayerStore.StoreState.MORNING)tmp.text="";
            return false;
        }
    }
    [HarmonyPatch(typeof(StoreClientCounter),"GetTooltip")]
    internal static class ReplicaClientCounterTooltip {
        // The tooltip only counts the queue: show the host's count by lending it a list of that length.
        static void Prefix(out object[] __state){
            __state=null;var manager=PlayerStore.Instance!=null?PlayerStore.Instance.storeClientManager:null;
            if(!ReplicaSession.ClientStore||manager==null)return;
            __state=new object[]{manager.clientStack};manager.clientStack=new List<StoreClient>(new StoreClient[StorePresentation.ClientsLeft]);
        }
        static void Finalizer(object[] __state){var manager=PlayerStore.Instance!=null?PlayerStore.Instance.storeClientManager:null;if(__state!=null&&manager!=null)manager.clientStack=(List<StoreClient>)__state[0];}
    }
}
