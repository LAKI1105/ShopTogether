using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    [Serializable] internal sealed class CargoPanelData { public int token,page,bay;public string[] filters; }
    internal static class CargoIntent {
        const string Controller="corp_presence_cargo_sort_controller";
        static Type warehouse,window,rename;static Harmony patches;static GameItem configuringSource,configuringBay;
        static int serial;static readonly Dictionary<int,Pending> pending=new Dictionary<int,Pending>();
        sealed class Pending {internal int owner,page;internal GameItem source,bay;internal Action<string[]> save;internal HashSet<string> allowed;}
        internal static void Bind(){
            if(patches!=null)return;warehouse=ModReflect.FindType("CorpPresence.CargoWarehouse");if(warehouse==null)return;
            window=ModReflect.FindType("CorpPresence.UI.CargoSortWindow");rename=ModReflect.FindType("CorpPresence.UI.CargoTabRename");
            patches=new Harmony("shop_together.cargo-intent");patches.Patch(((MethodInfo)ModReflect.Target(window,"Open")),prefix:new HarmonyMethod(typeof(CargoIntent),"Capture"));
            patches.Patch(((MethodInfo)ModReflect.Target(rename,"Open")),prefix:new HarmonyMethod(typeof(CargoIntent),"CaptureRename"));
        }
        static MachineryTabs Tabs(GameItem bay){
            if(warehouse==null||bay==null)return null;var table=(System.Runtime.CompilerServices.ConditionalWeakTable<GameItem,MachineryTabs>)ModReflect.GetStatic(warehouse,"Inventories");
            MachineryTabs tabs;return table.TryGetValue(bay,out tabs)?tabs:null;
        }
        internal static int Page(GameItem source,GameItem bay){if(source==null||source.identifier!=Controller)return 0;var tabs=Tabs(bay);return tabs==null?-1:tabs.selected;}
        internal static bool IsController(GameItem source){return warehouse!=null&&source!=null&&source.identifier==Controller;}
        internal static bool Target(GameItem source,GameItem bay,int page){
            var tabs=Tabs(bay);if(tabs==null||page<0||page>=tabs.count)return AuthorityIntent.Refuse("~SMC305~");
            int old=tabs.selected;var previousSource=configuringSource;var previousBay=configuringBay;configuringSource=source;configuringBay=bay;
            try{tabs.Select(page);source.Target(bay);return true;}finally{tabs.Select(old);configuringSource=previousSource;configuringBay=previousBay;}
        }
        static bool Capture(int page,string[] filters,Action<string[]> save){
            if(!PanelRelay.Capturing)return true;
            if(configuringSource==null||configuringBay==null){RemoteActions.PendingNote="~SMC326~";return false;}
            int token=++serial;var allowed=new HashSet<string>(TypeHelper.ALL_TYPES_LIST){"NARCOTIC","corp_presence_tobacco_and_alcohol","LUXURY_ITEM"};foreach(string f in filters)allowed.Add(f);
            foreach(int key in new List<int>(pending.Keys))if(pending[key].owner==PanelRelay.Owner)pending.Remove(key);
            pending[token]=new Pending{owner=PanelRelay.Owner,page=page,source=configuringSource,bay=configuringBay,save=save,allowed=allowed};
            Publish(55,new CargoPanelData{token=token,page=page,bay=Connection.Current.World.Id(configuringBay),filters=filters});return false;
        }
        static void Publish(int method,CargoPanelData data){Connection.Current.SendTo(PanelRelay.Owner,PanelRelay.Message,Encoding.UTF8.GetBytes(WireJson.ToJson(new PanelCall{m=method,texts=new[]{WireJson.ToJson(data)}})));PanelRelay.NotifiedDuringAction=true;}
        static bool CaptureRename(GameItem bay,int page){if(!PanelRelay.Capturing)return true;Publish(56,new CargoPanelData{page=page,bay=Connection.Current.World.Id(bay)});return false;}
        internal static void Show(int method,string json){
            var data=WireJson.FromJson<CargoPanelData>(json);GameItem bay;var s=Connection.Current;if(data==null||s==null||!s.World.Items.TryGetValue(data.bay,out bay))return;
            if(method==56){((MethodInfo)ModReflect.Target(rename,"Open")).Invoke(null,new object[]{bay,data.page});return;}
            Action<string[]> save=tags=>RemoteActions.Forward(RemoteActions.Adapter("CargoSave",2),null,new object[]{data.token,WireJson.ToJson(tags)});
            Action renameAction=()=>((MethodInfo)ModReflect.Target(rename,"Open")).Invoke(null,new object[]{bay,data.page});
            ((MethodInfo)ModReflect.Target(window,"Open")).Invoke(null,new object[]{data.page,data.filters,save,renameAction});
        }
        internal static void CargoSave(int token,string json){
            Pending p;var s=Connection.Current;int owner=RemoteActions.CurrentOwner;
            if(s==null||!pending.TryGetValue(token,out p)||p.owner!=owner){RemoteActions.PendingNote="~SMC326~";return;}
            pending.Remove(token);var tabs=Tabs(p.bay);
            if(tabs==null||p.page<0||p.page>=tabs.count||p.source==null||p.source.parentInventory==null||p.bay.parentInventory==null||!p.source.MayTarget(p.bay)||!p.source.CanTarget(p.bay)){RemoteActions.PendingNote="~SMC316~";return;}
            if(!AuthorityIntent.Free(s,owner,s.World.Id(p.source))||!AuthorityIntent.Free(s,owner,s.World.Id(p.bay)))return;
            var tags=WireJson.FromJson<string[]>(json);if(tags==null||tags.Length>p.allowed.Count){RemoteActions.PendingNote="~SMC305~";return;}
            var unique=new HashSet<string>();foreach(string tag in tags)if(tag==null||!p.allowed.Contains(tag)||!unique.Add(tag)){RemoteActions.PendingNote="~SMC305~";return;}
            p.save(tags);RemoteActions.PendingNote="~SMC341~";
        }
        internal static void Reset(){pending.Clear();configuringSource=null;configuringBay=null;}
        internal static void Shutdown(){Reset();if(patches!=null)patches.UnpatchAll("shop_together.cargo-intent");patches=null;warehouse=null;}
    }
}
