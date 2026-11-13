#if LAN_PROBE && PM_REAL_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static readonly List<JObject> visitorPackets=new List<JObject>();
  static bool RecordVisitorPacket(object __0){visitorPackets.Add(JObject.FromObject(__0));return false;}
  static int VisitorPacketCount(string type){int n=0;foreach(var p in visitorPackets)if((string)p["type"]==type)n++;return n;}
  static bool VisitorPacket(string type,string value,string expected){foreach(var p in visitorPackets)if((string)p["type"]==type&&(string)p[value]==expected)return true;return false;}
  static bool VisitorPacketContains(string type,string value,string expected){foreach(var p in visitorPackets)if((string)p["type"]==type&&((string)p[value]??"").Contains(expected))return true;return false;}
  static bool VisitorPacketContainsUnsafeMarkup(){foreach(var p in visitorPackets)if((string)p["type"]=="chat"){string text=(string)p["text"]??"";if(text.Contains("<")||text.Contains(">"))return true;}return false;}
  static bool VisitorMessage(string text){var phase=HostPhase.Last;if(phase==null||phase.visitor==null||phase.visitor.messages==null)return false;foreach(var message in phase.visitor.messages)if(message.Contains(text))return true;return false;}
  static IEnumerator ProbablyRealHost(Connection root) {
   var wait=Wait(()=>IsolatedVisitorTest.Loaded()&&ProbablyMultiplayerCompat.ProbeBound,"actual visitor mod and compatibility bridge loaded",40);while(wait.MoveNext())yield return wait.Current;
   var runtime=AccessTools.TypeByName("ProbablyMultiplayer.MultiplayerRuntime");var h=new Harmony("shop-together.visitor-send-probe");h.Patch(AccessTools.Method(runtime,"Send"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"RecordVisitorPacket"));
   var preview=IsolatedVisitorTest.Begin();IsolatedVisitorTest.Arrive();VisitorShare.ProbeConnected=true;EmporiumEntry.Instance.ShowFrontArea();
   Require(ProbablyMultiplayerCompat.VisitActive&&ProbablyMultiplayerCompat.Protected(preview),"actual visitor mod created a protected counter preview");
   root.World.Index();int previewId=root.World.Id(preview);Require(previewId>0,"host indexed real visitor preview");
   var ordinary=Find(root,"SMC_HOST_ITEM");IsolatedVisitorTest.HostOffer(ordinary);PlayerStore.Instance.playerCash=3000;
   bool hostNegotiation=NegociationUIManager.Instance.IsOpen;Signal("PM_REAL_READY="+previewId);
   wait=Wait(()=>Received("PM_PREVIEW_CLAIMED"),"guest holds the visitor preview",60);while(wait.MoveNext())yield return wait.Current;
   string refused=null;var invalid=new Placement{item=previewId,count=preview.unitCount,inventory=root.World.Address(EmporiumEntry.Instance.invElement)};
   root.Process(1,new Request{kind=RequestKind.Move,items=new[]{previewId},moves=new[]{invalid}},reply=>refused=reply.reason);
   Require(refused=="~SMC271~"&&preview.parentInventory==EmporiumEntry.Instance.frontInvinvElement,"host rejects moving a visitor preview into shop storage");
   refused=null;root.Process(1,new Request{kind=RequestKind.Claim,items=new[]{previewId,root.World.Id(ordinary)}},reply=>refused=reply.reason);
   Require(refused=="~SMC271~","host rejects group claims containing a visitor preview");Signal("PM_PREVIEW_SAFE");
   wait=Wait(()=>preview.parentInventory==EmporiumEntry.Instance.backInvinvElement&&Received("PM_PREVIEW_MOVED"),"guest selected visitor preview on host counter",90);while(wait.MoveNext())yield return wait.Current;
   Require(ProbablyMultiplayerCompat.Protected(preview),"visitor preview remains protected after guest selected it");
   wait=Wait(()=>root.Leases.Available(0,previewId,Time.realtimeSinceStartup),"preview lease released after guest drop",60);while(wait.MoveNext())yield return wait.Current;
   IsolatedVisitorTest.IncomingChat("VISITOR_HELLO");IsolatedVisitorTest.TradeScenario(false,11);Signal("PM_SALE_READY");
   wait=Wait(()=>Received("PM_UI_OPEN"),"guest opened visitor interface",60);while(wait.MoveNext())yield return wait.Current;
   Require(NegociationUIManager.Instance.IsOpen==hostNegotiation,"guest visitor interface did not open the host negotiation window");
   wait=Wait(()=>VisitorPacketContains("chat","text","FRIEND_HELLO")&&VisitorPacket("quote","price","175"),"guest chat and price sent through the actual visitor mod",90);while(wait.MoveNext())yield return wait.Current;
   Require(VisitorPacketCount("quote")==1,"stale offer was refused; one valid price was sent");Require(!VisitorPacketContainsUnsafeMarkup(),"guest chat cannot inject rich-text controls");Signal("PM_QUOTE_OK");
   yield return new WaitForSecondsRealtime(1.2f);IsolatedVisitorTest.TradeScenario(true,12);Signal("PM_BUY_READY");
   wait=Wait(()=>VisitorPacket("buy","revision","12"),"guest purchase was sent to actual visitor mod",90);while(wait.MoveNext())yield return wait.Current;
   IsolatedVisitorTest.Prepare();Require(ProbablyMultiplayerCompat.TradeLocked,"real visitor mod reserved the purchase");
   IsolatedVisitorTest.Commit();Require(PlayerStore.Instance.playerCash==2875,"real visitor purchase charged host once");
   root.World.Index();GameItem bought=null;int count=0;foreach(var item in root.World.Items.Values)if(item.name=="PM_REAL_VISITOR_ITEM"&&!ProbablyMultiplayerCompat.Protected(item)){bought=item;count++;}
   Require(count==1&&bought!=null&&bought.parentInventory==EmporiumEntry.Instance.backInvinvElement,"real visitor transaction produced one purchased item");Signal("PM_REAL_BOUGHT="+root.World.Id(bought));
   wait=Wait(()=>Received("PM_REAL_SYNCED"),"guest received purchase and host balance",90);while(wait.MoveNext())yield return wait.Current;
   IsolatedVisitorTest.End();VisitorShare.ProbeConnected=false;Signal("PM_REAL_ENDED");
  }
  static IEnumerator ProbablyRealGuest(Connection root) {
   var wait=Wait(()=>IsolatedVisitorTest.Loaded()&&ProbablyMultiplayerCompat.ProbeBound,"guest loaded actual visitor mod and bridge",40);while(wait.MoveNext())yield return wait.Current;
   Require(!IsolatedVisitorTest.LoginVisible(),"guest does not start an independent visitor login");
   IsolatedVisitorTest.SeedStaleSession();wait=Wait(()=>!IsolatedVisitorTest.HasSession(),"guest closes its own stale visitor session before using the host's visit",15);while(wait.MoveNext())yield return wait.Current;
   string message=null;wait=Wait(()=>(message=ReceivePrefix("PM_REAL_READY="))!=null,"host started visitor session",90);while(wait.MoveNext())yield return wait.Current;
   int previewId=int.Parse(message);GameItem preview=null;wait=Wait(()=>root.World.Items.TryGetValue(previewId,out preview)&&preview!=null&&ProbablyMultiplayerCompat.ReplicaVisitor,"guest sees visitor and offered item",90);while(wait.MoveNext())yield return wait.Current;
   Require(SpriteDict.Instance.spriteDictionary.ContainsKey("lemocuber_probablymultiplayer_player_sprite"),"guest has visitor portrait without second login");
   ShowStorage();EmporiumEntry.Instance.invWindow.Hide();EmporiumEntry.Instance.frontInvWindow.Show();EmporiumEntry.Instance.backInvWindow.Show();EmporiumEntry.Instance.frontInvWindow.ToFront(true);EmporiumEntry.Instance.Validate(true);yield return null;BeginDrag(preview);Require(ItemMouseDragHandler.current.IsDraggingItem,"guest begins native visitor-item drag");
   wait=Wait(()=>root.LocalLeases.ContainsKey(previewId),"guest obtained visitor preview lease",60);while(wait.MoveNext())yield return wait.Current;Signal("PM_PREVIEW_CLAIMED");wait=Wait(()=>Received("PM_PREVIEW_SAFE"),"host checked visitor item protections",60);while(wait.MoveNext())yield return wait.Current;Drop(EmporiumEntry.Instance.backInvinvElement,preview);Signal("PM_PREVIEW_MOVED");
   wait=Wait(()=>VisitorMessage("VISITOR_HELLO")&&Received("PM_SALE_READY"),"visitor chat reached guest",90);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>UnityEngine.Object.FindObjectOfType<StoreTablet>().OnClick());
   wait=Wait(()=>CustomUIManager.Instance.IsOpen("shop_together_visitor"),"guest opens local visitor chat and trade",60);while(wait.MoveNext())yield return wait.Current;Signal("PM_UI_OPEN");
   wait=Wait(()=>HostPhase.Last!=null&&HostPhase.Last.visitor!=null&&HostPhase.Last.visitor.canOffer,"guest can offer a price",60);while(wait.MoveNext())yield return wait.Current;
   var v=HostPhase.Last.visitor;RemoteActions.LastNote="";RemoteActions.Forward(RemoteActions.Adapter("VisitorOffer",3),null,new object[]{v.visit,v.revision-1,123});
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC281~"),"stale visitor quote is refused",60);while(wait.MoveNext())yield return wait.Current;
   var input=CustomUIManager.Instance.GetElement("shop_together_visitor","draft");input.input.text="<size=90>FRIEND_HELLO</size>";CustomUIManager.Instance.GetElement("shop_together_visitor","send").button.onClick.Invoke();
   var amount=CustomUIManager.Instance.GetElement("shop_together_visitor","price");amount.input.text="175";var offer=CustomUIManager.Instance.GetElement("shop_together_visitor","offer");Require(offer.button.interactable,"guest price button is enabled");offer.button.onClick.Invoke();
   wait=Wait(()=>Received("PM_QUOTE_OK"),"host accepted one valid guest price",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>HostPhase.Last!=null&&HostPhase.Last.visitor!=null&&HostPhase.Last.visitor.canBuy&&Received("PM_BUY_READY"),"guest sees visitor's confirmed price",90);while(wait.MoveNext())yield return wait.Current;
   var buy=CustomUIManager.Instance.GetElement("shop_together_visitor","buy");Require(buy.button.interactable,"guest purchase button is enabled");buy.button.onClick.Invoke();
   message=null;wait=Wait(()=>(message=ReceivePrefix("PM_REAL_BOUGHT="))!=null,"host completed visitor purchase",90);while(wait.MoveNext())yield return wait.Current;
   int purchasedId=int.Parse(message);GameItem purchased=null;wait=Wait(()=>root.World.Items.TryGetValue(purchasedId,out purchased)&&purchased!=null&&purchased.parentInventory==EmporiumEntry.Instance.backInvinvElement&&PlayerStore.Instance.playerCash==2875,"guest receives purchased item and updated balance",90);while(wait.MoveNext())yield return wait.Current;
   Require(!root.World.Items.ContainsKey(previewId),"visitor preview disappears after purchase");Signal("PM_REAL_SYNCED");wait=Wait(()=>Received("PM_REAL_ENDED"),"host ended visitor session",40);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
