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
            if(patches!=null)return;session=AccessTools.TypeByName("BartenderMod.BarSession");if(session==null)return;
            story=AccessTools.TypeByName("BartenderMod.BarStory");goods=AccessTools.TypeByName("BartenderMod.BarStoryGoods");patches=new Harmony("shop_together.bar-intent");
            patches.Patch(AccessTools.Method(session,"Notice"),prefix:new HarmonyMethod(typeof(BarIntent),"Notice"));
            patches.Patch(AccessTools.Method(session,"Pay"),prefix:new HarmonyMethod(typeof(BarIntent),"BeforePay"));
            patches.Patch(AccessTools.Method(story,"Choose"),prefix:new HarmonyMethod(typeof(BarIntent),"Choose"),finalizer:new HarmonyMethod(typeof(BarIntent),"AfterEffect"));
            patches.Patch(AccessTools.Method(AccessTools.TypeByName("BartenderMod.BarStoryProps"),"Redeem"),prefix:new HarmonyMethod(typeof(BarIntent),"Redeem"),finalizer:new HarmonyMethod(typeof(BarIntent),"AfterEffect"));
            patches.Patch(AccessTools.Method(AccessTools.TypeByName("BartenderMod.BarStoryProps"),"OpenEnglishPaper"),prefix:new HarmonyMethod(typeof(BarIntent),"EnglishPaper"));
            patches.Patch(AccessTools.Method(AccessTools.TypeByName("BartenderMod.BarSupply"),"Checkout"),prefix:new HarmonyMethod(typeof(BarIntent),"BeginEffect"),finalizer:new HarmonyMethod(typeof(BarIntent),"AfterEffect"));
            foreach(var nested in AccessTools.TypeByName("BartenderMod.BarCustomers").GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic))
                foreach(var method in nested.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                    if(method.ReturnType==typeof(void)&&method.Name.Contains("<Offer>b__"))patches.Patch(method,prefix:new HarmonyMethod(typeof(BarIntent),"OfferCallback"),finalizer:new HarmonyMethod(typeof(BarIntent),"AfterEffect"));
        }
        static void Notice(string text){if(RemoteActions.CurrentOwner>0)RemoteActions.PendingNote=text;}
        static bool EnglishPaper(GameItem __0,object __1){
            if(!PanelRelay.Capturing)return true;
            // This native paper is created dynamically and cannot be reopened by registry
            // ID on a guest. Use the existing panel relay; all callbacks stay on the host.
            var props=AccessTools.TypeByName("BartenderMod.BarStoryProps");
            var translate=AccessTools.Method(AccessTools.TypeByName("BartenderMod.BarL"),"T",new[]{typeof(string)});
            string id=(string)AccessTools.Field(__1.GetType(),"Id").GetValue(__1);
            string name=(string)AccessTools.Field(__1.GetType(),"Name").GetValue(__1);
            var panel=CustomUIManager.Window("bartender_action.story_prop",(string)translate.Invoke(null,new object[]{name}),"paper").SetSize(460f,560f).SetDraggable(true);
            AccessTools.Field(props,"inspection").SetValue(null,panel.Handle);
            panel.BeginScroll(440f).AddLabel((string)AccessTools.Method(props,"EnglishDocument").Invoke(null,new object[]{id}));
            if(id=="contracts"){
                int branch=(int)AccessTools.Method(session,"Int").Invoke(null,new object[]{"story.viola.branch",0});
                string status=branch==1?"签下了品牌署名的那份。":branch==2?"定下了如实署名的那份。":"尚未签署。";
                panel.AddLabel((string)translate.Invoke(null,new object[]{status}));
            }
            panel.End();
            if(id=="ticket")panel.AddButton("Claim prize · 400 credits",()=>AccessTools.Method(props,"Redeem").Invoke(null,new object[]{__0}));
            panel.AddButton("Close",()=>AccessTools.Method(props,"Close").Invoke(null,null)).Show();
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
            var person=AccessTools.Field(order.GetType(),"Person").GetValue(order);string id=(string)AccessTools.Field(person.GetType(),"Id").GetValue(person);
            var items=AccessTools.Method(session,"Items").Invoke(null,null);
            if(id=="geqiu"){foreach(GameItem item in (IEnumerable)items)if(item.identifier=="bartender_action.prop.toolbox")return Free(item);}
            if(id=="marco"&&branch==1){var args=new object[]{items,null};if((bool)AccessTools.Method(goods,"TrySelectMarco").Invoke(null,args))foreach(GameItem item in (GameItem[])args[1])if(!Free(item))return false;}
            return true;
        }
        static bool Redeem(GameItem __0,out HostNativeScope __state){__state=null;if(!Free(__0))return false;__state=HostNativeScope.Begin();return true;}
        static bool BeforePay(int __0,ref bool __result){if(__0<=0||DialoguePayment.AllowSpending())return true;__result=false;return false;}
        static bool BeginEffect(out HostNativeScope __state){__state=HostNativeScope.Begin();return true;}
        static Exception AfterEffect(Exception __exception,HostNativeScope __state){if(__state!=null)__state.Dispose();return __exception;}
        internal static IEnumerable<GameItem> CapturedItems(Delegate callback){
            var seen=new HashSet<object>();var found=new HashSet<GameItem>();Collect(callback.Target,0,seen,found);return found;
        }
        static void Collect(object value,int depth,HashSet<object> seen,HashSet<GameItem> found){
            if(value==null||depth>4||!seen.Add(value))return;
            var item=value as GameItem;if(item!=null){found.Add(item);return;}
            if(value is IEnumerable && !(value is string)){foreach(var child in (IEnumerable)value)Collect(child,depth+1,seen,found);return;}
            Type t=value.GetType();if(t.Namespace!="BartenderMod"||value is UnityEngine.Object)return;
            foreach(var field in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))Collect(field.GetValue(value),depth+1,seen,found);
        }
        internal static void Shutdown(){if(patches!=null)patches.UnpatchAll("shop_together.bar-intent");patches=null;session=null;}
    }
}
