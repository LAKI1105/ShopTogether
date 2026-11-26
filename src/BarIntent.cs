using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    internal static class BarIntent {
        static Harmony patches;static Type session,story,goods;
        internal static void Bind(){
            if(patches!=null)return;session=ModReflect.FindType("BartenderMod.BarSession");if(session==null)return;
            story=ModReflect.FindType("BartenderMod.BarStory");goods=ModReflect.FindType("BartenderMod.BarStoryGoods");patches=new Harmony("shop_together.bar-intent");
            patches.Patch(((MethodInfo)ModReflect.Target(session,"Notice")),prefix:new HarmonyMethod(typeof(BarIntent),"Notice"));
            patches.Patch(((MethodInfo)ModReflect.Target(session,"Pay")),prefix:new HarmonyMethod(typeof(BarIntent),"BeforePay"));
            patches.Patch(((MethodInfo)ModReflect.Target(story,"Choose")),prefix:new HarmonyMethod(typeof(BarIntent),"Choose"),finalizer:new HarmonyMethod(typeof(BarIntent),"AfterEffect"));
            patches.Patch(((MethodInfo)ModReflect.Target(ModReflect.FindType("BartenderMod.BarStoryProps"),"Redeem")),prefix:new HarmonyMethod(typeof(BarIntent),"Redeem"),finalizer:new HarmonyMethod(typeof(BarIntent),"AfterEffect"));
            patches.Patch(((MethodInfo)ModReflect.Target(ModReflect.FindType("BartenderMod.BarStoryProps"),"OpenEnglishPaper")),prefix:new HarmonyMethod(typeof(BarIntent),"EnglishPaper"));
            patches.Patch(((MethodInfo)ModReflect.Target(ModReflect.FindType("BartenderMod.BarSupply"),"Checkout")),prefix:new HarmonyMethod(typeof(BarIntent),"BeginEffect"),finalizer:new HarmonyMethod(typeof(BarIntent),"AfterEffect"));
            patches.Patch(ModReflect.Target("BartenderMod.BarCustomers+<>c__DisplayClass34_0","<Offer>b__0"),prefix:new HarmonyMethod(typeof(BarIntent),"OfferCallback"),finalizer:new HarmonyMethod(typeof(BarIntent),"AfterEffect"));
            patches.Patch(ModReflect.Target("BartenderMod.BarCustomers+<>c__DisplayClass34_0","<Offer>b__1"),prefix:new HarmonyMethod(typeof(BarIntent),"OfferCallback"),finalizer:new HarmonyMethod(typeof(BarIntent),"AfterEffect"));
        }
        static void Notice(string text){if(RemoteActions.CurrentOwner>0)RemoteActions.PendingNote=text;}
        static bool EnglishPaper(GameItem __0,object __1){
            if(!PanelRelay.Capturing)return true;
            // This native paper is created dynamically and cannot be reopened by registry
            // ID on a guest. Use the existing panel relay; all callbacks stay on the host.
            var props=ModReflect.FindType("BartenderMod.BarStoryProps");
            var translate=((MethodInfo)ModReflect.Target(ModReflect.FindType("BartenderMod.BarL"),"T",new[]{typeof(string)}));
            string id=(string)ModReflect.Get(__1,"Id");
            string name=(string)ModReflect.Get(__1,"Name");
            var panel=CustomUIManager.Window("bartender_action.story_prop",(string)translate.Invoke(null,new object[]{name}),"paper").SetSize(460f,560f).SetDraggable(true);
            ModReflect.SetStatic(props,"inspection",panel.Handle);
            panel.BeginScroll(440f).AddLabel((string)((MethodInfo)ModReflect.Target(props,"EnglishDocument")).Invoke(null,new object[]{id}));
            if(id=="contracts"){
                int branch=(int)((MethodInfo)ModReflect.Target(session,"Int")).Invoke(null,new object[]{"story.viola.branch",0});
                string status=branch==1?"签下了品牌署名的那份。":branch==2?"定下了如实署名的那份。":"尚未签署。";
                panel.AddLabel((string)translate.Invoke(null,new object[]{status}));
            }
            panel.End();
            if(id=="ticket")panel.AddButton("Claim prize · 400 credits",()=>((MethodInfo)ModReflect.Target(props,"Redeem")).Invoke(null,new object[]{__0}));
            panel.AddButton("Close",()=>((MethodInfo)ModReflect.Target(props,"Close")).Invoke(null,null)).Show();
            return false;
        }
        internal static bool Free(GameItem item){
            var s=Connection.Current;if(s==null||!s.IsHost||!s.Ready)return true;
            if(item==null||item.parentInventory==null||s.World.Id(item)<=0){RemoteActions.PendingNote="~SMC326~";return false;}
            bool free=DialogueIntent.MayChange(s,item)&&AuthorityIntent.FreeTree(s,Math.Max(0,RemoteActions.CurrentOwner),item);if(!free&&RemoteActions.CurrentOwner<0)RemoteActions.Notice(RemoteActions.PendingNote??"~SMC096~");return free;
        }
        static bool OfferCallback(object __instance,out HostNativeScope __state){__state=null;var found=new HashSet<GameItem>();Collect(__instance,0,new HashSet<object>(),found);foreach(var item in found)if(!Free(item))return false;__state=HostNativeScope.Begin();return true;}
        static bool Choose(object order,int branch,out HostNativeScope __state){
            __state=HostNativeScope.Begin();
            var connection=Connection.Current;if(connection==null||!connection.IsHost||!connection.Ready)return true;
            var person=ModReflect.Get(order,"Person");string id=(string)ModReflect.Get(person,"Id");
            var items=((MethodInfo)ModReflect.Target(session,"Items")).Invoke(null,null);
            if(id=="geqiu"){foreach(GameItem item in (IEnumerable)items)if(item.identifier=="bartender_action.prop.toolbox")return Free(item);}
            if(id=="marco"&&branch==1){var args=new object[]{items,null};if((bool)((MethodInfo)ModReflect.Target(goods,"TrySelectMarco")).Invoke(null,args))foreach(GameItem item in (GameItem[])args[1])if(!Free(item))return false;}
            return true;
        }
        static bool Redeem(GameItem __0,out HostNativeScope __state){__state=null;if(!Free(__0))return false;__state=HostNativeScope.Begin();return true;}
        static bool BeforePay(int __0,ref bool __result){if(__0<=0||DialoguePayment.AllowSpending())return true;__result=false;return false;}
        static bool BeginEffect(out HostNativeScope __state){__state=HostNativeScope.Begin();return true;}
        static Exception AfterEffect(Exception __exception,HostNativeScope __state){if(__state!=null)__state.Dispose();return __exception;}
        internal static IEnumerable<GameItem> CapturedItems(Delegate callback){
            var seen=new HashSet<object>();var found=new HashSet<GameItem>();Collect(callback.Target,0,seen,found);return found;
        }
        // Captured item/closure fields used by the supported bartender integration.
        static readonly string[] CapturedFieldNames={"<>2__current","<>4__this","<>9__3","<>9__5","action","after","available","b","bag","before","caption","Card","CS$<>8__locals1","CS$<>8__locals2","glass","i","item","items","line","motion","order","p","Person","product","r","recipe","refreshRows","regular","routine","s","score","state","stock","temporary","Trade","unitPrices"};
        static void Collect(object value,int depth,HashSet<object> seen,HashSet<GameItem> found){
            if(value==null||depth>4||!seen.Add(value))return;
            var item=value as GameItem;if(item!=null){found.Add(item);return;}
            if(value is IEnumerable && !(value is string)){foreach(var child in (IEnumerable)value)Collect(child,depth+1,seen,found);return;}
            Type t=value.GetType();if(t.Namespace!="BartenderMod"||value is UnityEngine.Object)return;
            foreach(string field in CapturedFieldNames)if(ModReflect.Has(value,field))Collect(ModReflect.Get(value,field),depth+1,seen,found);
        }
        internal static void Shutdown(){if(patches!=null)patches.UnpatchAll("shop_together.bar-intent");patches=null;session=null;}
    }
}
