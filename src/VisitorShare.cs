using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    [Serializable] internal sealed class VisitorView {
        public long visit; public int revision, quoteRevision, price; public bool canOffer, canBuy, canWithdraw;
        public string[] messages, items;
    }

    /// <summary>The host alone talks to the visitor service. Friends see a small, local view and submit
    /// only chat, quote, purchase or withdrawal requests, checked against the host's current visit.</summary>
    internal static class VisitorShare {
        const string WindowId="shop_together_visitor";
        const BindingFlags Static=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        static Type runtime,visit,chat; static FieldInfo sessionField,playerField,arrivedField,departingField;
        static PropertyInfo connectedProperty,historyProperty; static MethodInfo sendMethod,receiveMethod;
        static VisitorView view; static string draft="",price=""; static float tradeSent;
        static readonly Dictionary<int,float> chatSent=new Dictionary<int,float>();
#if LAN_PROBE
        internal static bool ProbeConnected;
#endif
        static bool Bind() {
            if(runtime!=null)return true;
            if(!ProbablyMultiplayerCompat.Available)return false;
            var core=AccessTools.TypeByName("ProbablyMultiplayer.Core");if(core==null)return false;var assembly=core.Assembly;
            if(assembly.GetName().Name!="ProbablyMultiplayer"||assembly.GetName().Version.ToString(3)!="2.6.1")return false;
            runtime=assembly.GetType("ProbablyMultiplayer.MultiplayerRuntime");
            visit=assembly.GetType("ProbablyMultiplayer.NativeVisit");chat=assembly.GetType("ProbablyMultiplayer.NativeChat");
            if(runtime==null||visit==null||chat==null){runtime=null;return false;}
            sessionField=runtime.GetField("Session",Static);playerField=runtime.GetField("PlayerId",Static);
            arrivedField=visit.GetField("Arrived",Static);departingField=visit.GetField("Departing",Static);
            connectedProperty=runtime.GetProperty("Connected",Static);historyProperty=chat.GetProperty("History",Static);
            sendMethod=AccessTools.Method(runtime,"Send",new[]{typeof(object)});
            receiveMethod=AccessTools.Method(chat,"Receive",new[]{typeof(string),typeof(string),typeof(string),typeof(string),typeof(bool)});
            if(sessionField==null||playerField==null||arrivedField==null||departingField==null||connectedProperty==null||historyProperty==null||sendMethod==null||receiveMethod==null){runtime=null;return false;}
            return true;
        }
        static object Value(object obj,string name){if(obj==null)return null;var p=AccessTools.Property(obj.GetType(),name);return p!=null?p.GetValue(obj,null):null;}
        static string StringValue(object obj,string name){return Value(obj,name) as string ?? "";}
        static int IntValue(object obj,string name){object v=Value(obj,name);return v is int?(int)v:0;}
        static string Clean(string text,int max) {
            var b=new StringBuilder();foreach(char c in text??""){if(b.Length>=max)break;if(c=='<'||c=='>'||char.IsControl(c)){if(c=='\n'||c=='\r'||c=='\t')b.Append(' ');continue;}b.Append(c);}return b.ToString().Trim();
        }
        static bool Connected(){
#if LAN_PROBE
            if(ProbeConnected)return true;
#endif
            return connectedProperty!=null&&(bool)connectedProperty.GetValue(null,null);
        }
        static object Current() {
            if(!Bind()||!ProbablyMultiplayerCompat.VisitActive||!(bool)arrivedField.GetValue(null)||(bool)departingField.GetValue(null)||!Connected())return null;
            return sessionField.GetValue(null);
        }
        static long Stamp(object session){string id=StringValue(session,"Id");return id.Length==0?0:Fingerprint.Hash(id);}
        static int Count(object x){var c=x as ICollection;if(c!=null)return c.Count;return 0;}
        static bool Idle(object s){return s!=null&&string.IsNullOrEmpty(StringValue(s,"TradeId"))&&!ProbablyMultiplayerCompat.TradeLocked;}
        static VisitorView Snapshot() {
            var s=Current();if(s==null)return null;long stamp=Stamp(s);if(stamp==0)return null;
            string self=playerField.GetValue(null) as string ?? "",buyer=StringValue(s,"BuyerId");object quote=Value(s,"Quote"),selected=Value(s,"Selected");int rev=IntValue(s,"Revision");
            bool ready=Idle(s),hasItems=Count(selected)>0;
            var v=new VisitorView{visit=stamp,revision=rev,quoteRevision=quote==null?0:IntValue(quote,"Revision"),price=quote==null?0:IntValue(quote,"Price"),canOffer=ready&&hasItems&&buyer.Length>0&&buyer!=self&&quote==null,canBuy=ready&&hasItems&&buyer==self&&quote!=null&&PlayerStore.Instance!=null&&PlayerStore.Instance.playerCash>=IntValue(quote,"Price"),canWithdraw=ready&&quote!=null&&StringValue(quote,"SellerId")==self};
            var lines=new List<string>();var history=historyProperty.GetValue(null,null) as IEnumerable;
            if(history!=null)foreach(var line in history){string text=Clean(StringValue(line,"Text"),160);if(text.Length==0)continue;bool own=Value(line,"Own") is bool&&(bool)Value(line,"Own");lines.Add((own?"~SMC287~":"~SMC288~")+"："+text);if(lines.Count>6)lines.RemoveAt(0);}
            v.messages=lines.ToArray();var chosen=new HashSet<string>();var ids=selected as IEnumerable;if(ids!=null)foreach(var id in ids)if(id is string)chosen.Add((string)id);
            var names=new List<string>();var offers=Value(s,"Offers") as IDictionary;
            if(offers!=null)foreach(DictionaryEntry offer in offers){var items=offer.Value as IEnumerable;if(items==null)continue;foreach(var item in items)if(chosen.Contains(StringValue(item,"Id"))){names.Add(Clean(StringValue(item,"Name"),60));if(names.Count>=12)break;}if(names.Count>=12)break;}
            v.items=names.ToArray();return v;
        }
        internal static void Capture(PhaseState phase){if(phase==null||!ProbablyMultiplayerCompat.Available)return;try{phase.visitor=Snapshot();}catch(Exception e){Diagnostics.Report("visitor view",e);}}
        internal static void Follow(PhaseState phase){view=phase==null?null:phase.visitor;if(view==null){Close();return;}Refresh();}
        internal static void Reset(){view=null;draft=price="";chatSent.Clear();tradeSent=0;Close();}
        static void Close(){if(CustomUIManager.Instance!=null&&CustomUIManager.Instance.IsOpen(WindowId))CustomUIManager.Instance.CloseWindow(WindowId);}
        static void Request(string name,params object[] values){RemoteActions.Forward(RemoteActions.Adapter(name,values.Length),null,values);}
        internal static void Open() {
            if(!ReplicaSession.ClientStore||CustomUIManager.Instance==null)return;
            if(view==null){RemoteActions.Notice("~SMC281~");return;}
            if(CustomUIManager.Instance.IsOpen(WindowId)){Refresh();return;}
            CustomUIManager.Window(WindowId,Loc.Text("~SMC273~"),CustomUIManager.LAYER_OVERLAY).SetSize(680,650).Center().SetDraggable(true).SetCloseOnEscape()
                .AddLabel(Loc.Text("~SMC274~")).AddLabel("", "status").AddLabel("", "items").AddLabel("", "messages")
                .AddInput(Loc.Text("~SMC275~"),draft,v=>draft=v,"draft",v=>draft=v)
                .AddButton(Loc.Text("~SMC276~"),()=>{var v=view;string message=Clean(draft,160);if(v==null||message.Length==0)return;Request("VisitorSay",v.visit,message);draft="";var input=CustomUIManager.Instance.GetElement(WindowId,"draft");if(input!=null&&input.input!=null)input.input.SetTextWithoutNotify("");},"send")
                .AddInput(Loc.Text("~SMC277~"),price,v=>price=v,"price",v=>price=v)
                .AddButton(Loc.Text("~SMC278~"),()=>{int n;var v=view;if(v==null)return;if(!int.TryParse(price,NumberStyles.None,CultureInfo.InvariantCulture,out n)||n<0||n>2000000000){RemoteActions.Notice("~SMC282~");return;}Request("VisitorOffer",v.visit,v.revision,n);},"offer")
                .AddButton(Loc.Text("~SMC279~"),()=>{var v=view;if(v!=null)Request("VisitorBuy",v.visit,v.quoteRevision);},"buy")
                .AddButton(Loc.Text("~SMC280~"),()=>{var v=view;if(v!=null)Request("VisitorWithdraw",v.visit,v.quoteRevision);},"withdraw")
                .AddButton(Loc.Text("~SMC182~"),Close).Show();
            Refresh();
        }
        static void Text(string id,string text){var x=CustomUIManager.Instance.GetElement(WindowId,id);if(x!=null)x.SetText(Loc.Text(text));}
        static void Enabled(string id,bool enabled){var x=CustomUIManager.Instance.GetElement(WindowId,id);if(x!=null)x.SetInteractable(enabled);}
        static void Refresh(){
            if(view==null||CustomUIManager.Instance==null||!CustomUIManager.Instance.IsOpen(WindowId))return;
            string[] messages=view.messages??new string[0],items=view.items??new string[0];
            Text("messages",string.Join("\n",messages));Text("items",items.Length==0?"~SMC283~":"~SMC286~"+string.Join("、",items));
            Text("status",view.canBuy||view.canWithdraw?"~SMC285~"+view.price.ToString(CultureInfo.InvariantCulture):"~SMC284~");
            Enabled("offer",view.canOffer);Enabled("buy",view.canBuy);Enabled("withdraw",view.canWithdraw);
        }
        static bool Authorized(long stamp,out object s){
            s=null;var c=Connection.Current;int owner=RemoteActions.CurrentOwner;var kit=OutingKits.Of(owner);
            if(c==null||!c.IsHost||!c.Ready||owner<=0||!c.HasPeer(owner)||(kit!=null&&kit.location!=null)||OutingKits.IsJoined(owner))return false;
            try{s=Current();return s!=null&&stamp!=0&&stamp==Stamp(s);}catch{return false;}
        }
        static void Refused(){RemoteActions.PendingNote="~SMC281~";}
        static bool TradeAllowed(object s,int revision,bool quoteRevision){
            if(!Idle(s)||Time.realtimeSinceStartup-tradeSent<1f)return false;
            object q=Value(s,"Quote");return quoteRevision?q!=null&&IntValue(q,"Revision")==revision:IntValue(s,"Revision")==revision;
        }
        static void Send(object packet){sendMethod.Invoke(null,new[]{packet});}
        internal static void VisitorSay(long stamp,string text){
            object s;if(!Authorized(stamp,out s)){Refused();return;}int owner=RemoteActions.CurrentOwner;float last;
            if(chatSent.TryGetValue(owner,out last)&&Time.realtimeSinceStartup-last<.75f)return;
            text=Clean(text,160);if(text.Length==0)return;string name=Clean(Loc.Text(PlayerNames.Of(owner)),30);string message="["+name+"] "+text;
            string request=Guid.NewGuid().ToString("N"),self=playerField.GetValue(null) as string ?? "";
            Send(new{type="chat",requestId=request,text=message});receiveMethod.Invoke(null,new object[]{self,name,message,request,true});chatSent[owner]=Time.realtimeSinceStartup;
        }
        internal static void VisitorOffer(long stamp,int revision,int amount){
            object s;if(!Authorized(stamp,out s)||!TradeAllowed(s,revision,false)||amount<0||amount>2000000000||Count(Value(s,"Selected"))==0||Value(s,"Quote")!=null){Refused();return;}
            string buyer=StringValue(s,"BuyerId"),self=playerField.GetValue(null) as string ?? "";if(buyer.Length==0||buyer==self){Refused();return;}
            Send(new{type="quote",price=amount,revision=revision});tradeSent=Time.realtimeSinceStartup;
        }
        internal static void VisitorBuy(long stamp,int revision){
            object s;if(!Authorized(stamp,out s)||!TradeAllowed(s,revision,true)||Count(Value(s,"Selected"))==0){Refused();return;}object q=Value(s,"Quote");
            string self=playerField.GetValue(null) as string ?? "";int amount=IntValue(q,"Price");if(StringValue(s,"BuyerId")!=self||amount<0||PlayerStore.Instance==null||PlayerStore.Instance.playerCash<amount){Refused();return;}
            Send(new{type="buy",revision=revision});tradeSent=Time.realtimeSinceStartup;
        }
        internal static void VisitorWithdraw(long stamp,int revision){
            object s;if(!Authorized(stamp,out s)||!TradeAllowed(s,revision,true)){Refused();return;}object q=Value(s,"Quote");string self=playerField.GetValue(null) as string ?? "";
            if(StringValue(q,"SellerId")!=self){Refused();return;}Send(new{type="withdraw_quote"});tradeSent=Time.realtimeSinceStartup;
        }
    }
}
