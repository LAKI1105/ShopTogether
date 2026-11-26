using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShopTogether {
    [Serializable] internal sealed class CustomPanelData {
        public string id,title,layer; public int token,style; public bool closed;
        public List<CustomPanelOp> ops=new List<CustomPanelOp>();
        public List<CustomPanelVisual> visuals=new List<CustomPanelVisual>();
    }
    [Serializable] internal sealed class CustomPanelOp { public int method,target,last; public string[] args; }
    [Serializable] internal sealed class CustomPanelVisual { public int id,alignment;public string text;public float size;public float[] color; }
    // Preserve the installed mod's builder layout and host callbacks. No gameplay callback
    // is reconstructed or executed on a friend. Only these explicitly inventoried panels use it.
    internal static class CustomPanelRelay {
        static readonly HashSet<string> ids=new HashSet<string>{"bartender_action.bar","bartender_action.offer","bartender_action.story","bartender_action.story_prop"};
        static readonly Dictionary<CustomUIBuilder,HostPanel> builders=new Dictionary<CustomUIBuilder,HostPanel>();
        static readonly Dictionary<CustomUIElement,HostPanel> elements=new Dictionary<CustomUIElement,HostPanel>();
        static readonly Dictionary<CustomUIWindow,HostPanel> windows=new Dictionary<CustomUIWindow,HostPanel>();
        static readonly Dictionary<int,HostPanel> panels=new Dictionary<int,HostPanel>();
        static readonly Dictionary<string,CustomUIWindow> guestWindows=new Dictionary<string,CustomUIWindow>();
        static readonly List<MethodInfo> methods=BuildMethods();
        static int serial,depth;static bool replaying,resetting;
        internal sealed class Callback { internal Delegate action;internal CustomUIElement element; }
        internal sealed class HostPanel {
            internal int owner;internal CustomPanelData data;internal CustomUIWindow window;internal GameItem source;internal GameInventory parent;
            internal readonly Dictionary<CustomUIElement,int> elementIds=new Dictionary<CustomUIElement,int>();
            internal readonly Dictionary<int,Callback> callbacks=new Dictionary<int,Callback>();
        }
        internal sealed class Recording { internal HostPanel panel;internal CustomPanelOp op;internal List<int> callbacks=new List<int>(); }
        static List<MethodInfo> BuildMethods() {
            var list=new List<MethodInfo>();
            foreach(var m in typeof(CustomUIBuilder).GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).Concat(typeof(CustomUIElement).GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)))
                if(!m.IsSpecialName && (m.ReturnType==typeof(CustomUIBuilder)||m.ReturnType==typeof(CustomUIElement)) && m.Name!="OnClose" && m.GetParameters().All(p=>Supported(p.ParameterType)))list.Add(m);
            list.Sort((a,b)=>StringComparer.Ordinal.Compare(Signature(a),Signature(b)));return list;
        }
        static string Signature(MethodInfo m){return m.DeclaringType.Name+"."+m.Name+"("+string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName).ToArray())+")";}
        static bool Supported(Type t){return t==typeof(string)||t==typeof(int)||t==typeof(float)||t==typeof(bool)||t==typeof(Color)||t==typeof(Vector2)||t==typeof(Sprite)||t==typeof(IList<string>)||t==typeof(TextAnchor)||t==typeof(Action)||t==typeof(Action<bool>)||t==typeof(Action<float>)||t==typeof(Action<int>)||t==typeof(Action<string>);}
        internal static IEnumerable<MethodBase> Methods(){return methods.Cast<MethodBase>();}
        internal static void Namespace(ref string id){if(!replaying && PanelRelay.Capturing && ids.Contains(id))id="smc."+PanelRelay.Owner+"."+id;}
        static string Original(string id){if(id==null)return null;foreach(var name in ids)if(id.EndsWith("."+name,StringComparison.Ordinal))return name;return null;}
        internal static void Created(string id,string title,string layerId,CustomUIManager.Style style,CustomUIBuilder builder){
            if(replaying||!PanelRelay.Capturing||builder==null)return;string original=Original(id);if(original==null)return;
            var p=new HostPanel{owner=PanelRelay.Owner,window=builder.Handle,source=PanelRelay.SourceItem,parent=PanelRelay.SourceItem==null?null:PanelRelay.SourceItem.parentInventory,
                data=new CustomPanelData{id=original,title=title,layer=layerId,style=(int)style,token=++serial}};
            builders[builder]=p;windows[p.window]=p;panels[p.data.token]=p;p.window.Hide();
        }
        static int ElementId(HostPanel p,CustomUIElement e){if(e==null)return 0;int id;if(!p.elementIds.TryGetValue(e,out id)){id=p.elementIds.Count+1;p.elementIds.Add(e,id);elements[e]=p;}return id;}
        internal static void Begin(object instance,MethodBase method,object[] args,out Recording state){
            state=null;if(replaying||!PanelRelay.Capturing||depth!=0)return;
            HostPanel p;bool known=instance is CustomUIBuilder?builders.TryGetValue((CustomUIBuilder)instance,out p):elements.TryGetValue((CustomUIElement)instance,out p);
            if(!known||p.owner!=PanelRelay.Owner)return;
            var s=new Recording{panel=p,op=new CustomPanelOp{method=methods.IndexOf((MethodInfo)method),target=instance is CustomUIElement?ElementId(p,(CustomUIElement)instance):0,args=new string[args.Length]}};
            for(int i=0;i<args.Length;i++)s.op.args[i]=Pack(args[i],s);
            state=s;depth++;
        }
        internal static void End(object instance,Recording state){if(state==null)return;depth--;var p=state.panel;var e=instance is CustomUIBuilder?((CustomUIBuilder)instance).Last:(CustomUIElement)instance;
            state.op.last=ElementId(p,e);foreach(int token in state.callbacks)p.callbacks[token].element=e;
            if(state.callbacks.Count==0&&instance is CustomUIElement&&methods[state.op.method].Name=="SetText"){
                for(int i=p.data.ops.Count-1;i>=0;i--)if(p.data.ops[i].method==state.op.method&&p.data.ops[i].target==state.op.target){p.data.ops[i]=state.op;return;}
            }
            p.data.ops.Add(state.op);
        }
        static string Pack(object arg,Recording state){
            if(arg==null)return null;var d=arg as Delegate;if(d!=null){int key=state.panel.callbacks.Count+1;state.panel.callbacks.Add(key,new Callback{action=d});state.callbacks.Add(key);return key.ToString(CultureInfo.InvariantCulture);}
            var sprite=arg as Sprite;if(sprite!=null)return WireJson.ToJson(new[]{sprite.name,sprite.texture.name,sprite.rect.x.ToString("R",CultureInfo.InvariantCulture),sprite.rect.y.ToString("R",CultureInfo.InvariantCulture),sprite.rect.width.ToString("R",CultureInfo.InvariantCulture),sprite.rect.height.ToString("R",CultureInfo.InvariantCulture)});
            if(arg is Color){var c=(Color)arg;return WireJson.ToJson(new[]{c.r,c.g,c.b,c.a});}if(arg is Vector2){var v=(Vector2)arg;return WireJson.ToJson(new[]{v.x,v.y});}
            return WireJson.ToJson(arg);
        }
        static object Unpack(string text,Type type,int panel){
            if(text==null)return null;
            if(typeof(Delegate).IsAssignableFrom(type)){
                int token=int.Parse(text,CultureInfo.InvariantCulture);
                if(type==typeof(Action))return (Action)(()=>Send(panel,token,""));
                if(type==typeof(Action<bool>))return (Action<bool>)(v=>Send(panel,token,v?"1":"0"));
                if(type==typeof(Action<float>))return (Action<float>)(v=>Send(panel,token,v.ToString("R",CultureInfo.InvariantCulture)));
                if(type==typeof(Action<int>))return (Action<int>)(v=>Send(panel,token,v.ToString(CultureInfo.InvariantCulture)));
                return (Action<string>)(v=>Send(panel,token,v));
            }
            if(type==typeof(Sprite)){var a=WireJson.FromJson<string[]>(text);foreach(var s in Resources.FindObjectsOfTypeAll<Sprite>())if(s.name==a[0]&&s.texture.name==a[1]&&s.rect.x.ToString("R",CultureInfo.InvariantCulture)==a[2]&&s.rect.y.ToString("R",CultureInfo.InvariantCulture)==a[3]&&s.rect.width.ToString("R",CultureInfo.InvariantCulture)==a[4]&&s.rect.height.ToString("R",CultureInfo.InvariantCulture)==a[5])return s;return null;}
            if(type==typeof(Color)){var a=WireJson.FromJson<float[]>(text);return new Color(a[0],a[1],a[2],a[3]);}
            if(type==typeof(Vector2)){var a=WireJson.FromJson<float[]>(text);return new Vector2(a[0],a[1]);}
            if(type==typeof(string))return WireJson.FromJson<string>(text);if(type==typeof(bool))return WireJson.FromJson<bool>(text);
            if(type==typeof(float))return WireJson.FromJson<float>(text);if(type==typeof(int))return WireJson.FromJson<int>(text);
            if(type==typeof(TextAnchor))return (TextAnchor)WireJson.FromJson<int>(text);return WireJson.FromJson<string[]>(text);
        }
        static void Send(int panel,int callback,string value){if(!ReplicaSession.IsReplica||replaying)return;RemoteActions.Forward(RemoteActions.Adapter("CustomPanelAction",3),null,new object[]{panel,callback,value??""});}
        internal static bool Showing(CustomUIWindow window){HostPanel p;if(replaying||!windows.TryGetValue(window,out p))return true;Publish(p);return false;}
        static void Publish(HostPanel p){
            if(resetting||Connection.Current==null||!Connection.Current.IsHost||!Connection.Current.HasPeer(p.owner))return;
            p.data.title=p.window.frame!=null&&p.window.frame.titleText!=null?p.window.frame.titleText.text:p.data.title;
            p.data.visuals.Clear();foreach(var pair in p.elementIds){var e=pair.Key;if(e.text==null)continue;var c=e.text.color;p.data.visuals.Add(new CustomPanelVisual{id=pair.Value,text=e.text.text,size=e.text.fontSize,alignment=(int)e.text.alignment,color=new[]{c.r,c.g,c.b,c.a}});}
            var bytes=Encoding.UTF8.GetBytes(WireJson.ToJson(new PanelCall{m=54,texts=new[]{WireJson.ToJson(p.data)}}));
            if(bytes.Length>1024*1024){RemoteActions.PendingNote="~SMC201~";return;}
            Connection.Current.SendTo(p.owner,PanelRelay.Message,bytes);PanelRelay.NotifiedDuringAction=true;
        }
        internal static void Closed(CustomUIWindow window){HostPanel p;if(!windows.TryGetValue(window,out p))return;p.data.closed=true;Publish(p);panels.Remove(p.data.token);windows.Remove(window);foreach(var e in p.elementIds.Keys)elements.Remove(e);foreach(var b in builders.Where(pair=>pair.Value==p).Select(pair=>pair.Key).ToArray())builders.Remove(b);}
        internal static void CustomPanelAction(int panel,int callback,string value){
            HostPanel p;Callback cb;var s=Connection.Current;int owner=RemoteActions.CurrentOwner;
            if(s==null||owner<=0||!panels.TryGetValue(panel,out p)||p.owner!=owner||!p.window.IsAlive||!p.callbacks.TryGetValue(callback,out cb)){RemoteActions.PendingNote="~SMC326~";return;}
            if(value==null||value.Length>1000){RemoteActions.PendingNote="~SMC305~";return;}
            if(p.source!=null&&(p.source.parentInventory!=p.parent||s.World.Id(p.source)<=0||!s.Leases.Available(owner,s.World.Id(p.source),Time.realtimeSinceStartup))){RemoteActions.PendingNote="~SMC096~";return;}
            var e=cb.element;var select=e==null?null:e.button!=null?(Selectable)e.button:e.toggle!=null?(Selectable)e.toggle:e.slider!=null?(Selectable)e.slider:e.dropdown!=null?(Selectable)e.dropdown:e.input;
            if(e!=null&&(!e.IsAlive||!e.root.activeSelf)||select!=null&&!select.IsInteractable()){RemoteActions.PendingNote="~SMC305~";Publish(p);return;}
            var pars=cb.action.Method.GetParameters();object[] args=new object[pars.Length];
            if(pars.Length>0){Type t=pars[0].ParameterType;
                if(t==typeof(bool)){if(value!="0"&&value!="1"){RemoteActions.PendingNote="~SMC305~";return;}args[0]=value=="1";}
                else if(t==typeof(float)){float f;if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out f)||float.IsNaN(f)||float.IsInfinity(f)||e==null||e.slider==null||f<e.slider.minValue||f>e.slider.maxValue){RemoteActions.PendingNote="~SMC305~";return;}args[0]=f;}
                else if(t==typeof(int)){int n;if(!int.TryParse(value,out n)||e==null||e.dropdown==null||n<0||n>=e.dropdown.options.Count){RemoteActions.PendingNote="~SMC305~";return;}args[0]=n;}
                else if(t==typeof(string))args[0]=value;else{RemoteActions.PendingNote="~SMC305~";return;}
            }
            var source=PanelRelay.SourceItem;PanelRelay.SourceItem=p.source;
            try{
                var captured=BarIntent.CapturedItems(cb.action).ToArray();foreach(var item in captured)if(!BarIntent.Free(item))return;
                string before=CallbackStamp(p,captured);int windowsBefore=serial;
                cb.action.DynamicInvoke(args);
                if(RemoteActions.PendingNote==null&&serial==windowsBefore&&before==CallbackStamp(p,captured))RemoteActions.PendingNote="~SMC326~";
                if(p.window.IsAlive)Publish(p);
            }finally{PanelRelay.SourceItem=source;}
        }
        static string CallbackStamp(HostPanel panel,GameItem[] items){
            var s=Connection.Current;var b=new StringBuilder(WireJson.ToJson(panel.data));b.Append(s.World.StoreHash(PlayerStore.Instance.saveSlotId));
            foreach(var item in items)if(item!=null)b.Append(Fingerprint.Item(s.World,s.World.Id(item),item));
            foreach(var e in panel.elementIds.Keys)if(e.text!=null)b.Append(e.text.text);return b.ToString();
        }
        internal static void Open(string json){
            var data=WireJson.FromJson<CustomPanelData>(json);if(data==null||!ids.Contains(data.id)||data.ops==null||data.ops.Count>10000)return;
            CustomUIWindow previous;guestWindows.TryGetValue(data.id,out previous);var scroll=new List<Vector2>();if(previous!=null&&previous.root!=null)foreach(var v in previous.root.GetComponentsInChildren<ScrollRect>(true))scroll.Add(v.normalizedPosition);
            replaying=true;try{
                if(data.closed){if(previous!=null)previous.Close();guestWindows.Remove(data.id);return;}
                var b=CustomUIManager.Instance.CreateWindow(data.id,data.title,data.layer,null,(CustomUIManager.Style)data.style);var map=new Dictionary<int,CustomUIElement>();
                foreach(var op in data.ops){if(op.method<0||op.method>=methods.Count)throw new InvalidOperationException("Unknown panel control");var m=methods[op.method];var pars=m.GetParameters();if(op.args==null||pars.Length!=op.args.Length)throw new InvalidOperationException("Invalid panel control");var args=new object[pars.Length];for(int i=0;i<args.Length;i++)args[i]=Unpack(op.args[i],pars[i].ParameterType,data.token);
                    object target=op.target==0?(object)b:map[op.target];m.Invoke(target,args);if(op.last>0)map[op.last]=target is CustomUIBuilder?b.Last:(CustomUIElement)target;
                }
                foreach(var v in data.visuals){CustomUIElement e;if(!map.TryGetValue(v.id,out e)||e.text==null)continue;e.SetText(v.text);e.text.fontSize=v.size;e.text.alignment=(TextAlignmentOptions)v.alignment;e.text.color=new Color(v.color[0],v.color[1],v.color[2],v.color[3]);}
                // Disabled status is a host hint. A click must still reach the host for its current verdict.
                foreach(var e in map.Values){if(e.button!=null)e.button.interactable=true;if(e.toggle!=null)e.toggle.interactable=true;if(e.slider!=null)e.slider.interactable=true;if(e.dropdown!=null)e.dropdown.interactable=true;}
                b.Handle.SetTitle(data.title);b.Show();guestWindows[data.id]=b.Handle;int n=0;foreach(var v in b.Handle.root.GetComponentsInChildren<ScrollRect>(true)){if(n<scroll.Count)v.normalizedPosition=scroll[n];n++;}
            }finally{replaying=false;}
        }
        internal static void Reset(){resetting=true;try{foreach(var p in panels.Values.ToArray())if(p.window.IsAlive)p.window.Close();foreach(var w in guestWindows.Values)if(w.IsAlive)w.Close();}finally{resetting=false;}panels.Clear();builders.Clear();elements.Clear();windows.Clear();guestWindows.Clear();depth=0;}
        internal static void Failed(Recording state){if(state!=null&&depth>0)depth--;}
    }
    [HarmonyPatch(typeof(CustomUIManager),"CreateWindow")]
    internal static class CustomPanelCreate {
        static void Prefix(ref string id){CustomPanelRelay.Namespace(ref id);}
        static void Postfix(string id,string title,string layerId,CustomUIManager.Style style,CustomUIBuilder __result){CustomPanelRelay.Created(id,title,layerId,style,__result);}
    }
    [HarmonyPatch(typeof(CustomUIManager),"GetWindow")]
    internal static class CustomPanelLookup {static void Prefix(ref string id){CustomPanelRelay.Namespace(ref id);}}
    [HarmonyPatch]
    internal static class CustomPanelRecord {
        static IEnumerable<MethodBase> TargetMethods(){return CustomPanelRelay.Methods();}
        static void Prefix(object __instance,MethodBase __originalMethod,object[] __args,out CustomPanelRelay.Recording __state){CustomPanelRelay.Begin(__instance,__originalMethod,__args,out __state);}
        static void Postfix(object __instance,CustomPanelRelay.Recording __state){CustomPanelRelay.End(__instance,__state);}
        static Exception Finalizer(Exception __exception,CustomPanelRelay.Recording __state){if(__exception!=null)CustomPanelRelay.Failed(__state);return __exception;}
    }
    [HarmonyPatch(typeof(CustomUIWindow),"Show")]
    internal static class CustomPanelShow {static bool Prefix(CustomUIWindow __instance){return CustomPanelRelay.Showing(__instance);}}
    [HarmonyPatch(typeof(CustomUIWindow),"Close")]
    internal static class CustomPanelClose {static void Prefix(CustomUIWindow __instance){CustomPanelRelay.Closed(__instance);}}
}
