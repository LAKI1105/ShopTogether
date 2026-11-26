#if LAN_PROBE
using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static IEnumerator CustomHost135(Connection root){
   EmporiumEntry.Instance.invElement.SetShape(80,80);PlayerStore.Instance.playerCash=100000;
   ModHelper.SetModDataInt("bartender_action","bar.level",0);ModHelper.SetModDataInt("bartender_action","mina.ticket_redeemed",0);
   var lamp=PanelFixture("bartender_action.prop.worklamp","CUSTOM_LAMP");var ticket=PanelFixture("bartender_action.prop.ticket","CUSTOM_TICKET");var menu=PanelFixture("bartender_action.menu","CUSTOM_MENU");root.World.Index();Signal("CUSTOM_READY");
   var wait=Wait(()=>Received("CUSTOM_LAMP_OPEN"),"guest worklamp description displayed",90);while(wait.MoveNext())yield return wait.Current;
   Require(CustomUIManager.Instance.GetWindow("bartender_action.story_prop")==null,"guest worklamp panel does not open on host");Signal("CUSTOM_LAMP_CHECKED");
   wait=Wait(()=>PlayerStore.Instance.playerCash==100400,"ticket native redemption credited host once",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>ModHelper.GetModDataInt("bartender_action","bar.level",0)==1,"guest business purchase executed on host",90);while(wait.MoveNext())yield return wait.Current;
   Require(PlayerStore.Instance.playerCash<100400,"business purchase charged host cash");
   wait=Wait(()=>Received("CUSTOM_DONE"),"guest mod panels and actions verified",120);while(wait.MoveNext())yield return wait.Current;
  }
  static CustomUIWindow CustomVisible(string id){var w=CustomUIManager.Instance.GetWindow(id);return w!=null&&w.IsVisible?w:null;}
  static IEnumerator CustomClient135(Connection root){
   bool ready=false;var wait=Wait(()=>(ready=ready||Received("CUSTOM_READY"))&&Find(root,"CUSTOM_MENU")!=null&&Find(root,"CUSTOM_LAMP")!=null&&Find(root,"CUSTOM_TICKET")!=null,"custom panel items replicated",90);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>Find(root,"CUSTOM_LAMP").ActivateSlotItem());
   wait=Wait(()=>CustomVisible("bartender_action.story_prop")!=null,"native worklamp panel arrived on guest",60);while(wait.MoveNext())yield return wait.Current;Signal("CUSTOM_LAMP_OPEN");
   wait=Wait(()=>Received("CUSTOM_LAMP_CHECKED"),"host panel remains separate",60);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>Find(root,"CUSTOM_TICKET").ActivateSlotItem());
   wait=Wait(()=>{var w=CustomVisible("bartender_action.story_prop");return w!=null&&w.ContentRoot.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t=>t.text.Contains("400"));},"ticket reward panel arrived",60);while(wait.MoveNext())yield return wait.Current;
   var ticketWindow=CustomVisible("bartender_action.story_prop");var redeem=ticketWindow.ContentRoot.GetComponentsInChildren<Button>(true).First(b=>b.GetComponentInChildren<TMPro.TMP_Text>(true).text.Contains("400"));
   long cash=PlayerStore.Instance.playerCash;NativeClick(0,()=>redeem.onClick.Invoke());Require(PlayerStore.Instance.playerCash==cash,"ticket callback does not credit guest locally");
   wait=Wait(()=>PlayerStore.Instance.playerCash==100400&&Find(root,"CUSTOM_TICKET")==null,"ticket credit and removal replicated",60);while(wait.MoveNext())yield return wait.Current;
   NativeClick(0,()=>Find(root,"CUSTOM_MENU").ActivateSlotItem());
   wait=Wait(()=>CustomVisible("bartender_action.bar")!=null,"bar business panel arrived",60);while(wait.MoveNext())yield return wait.Current;
   var menu=CustomVisible("bartender_action.bar");var buy=menu.ContentRoot.GetComponentsInChildren<Button>(true).First(b=>b.GetComponentInChildren<TMPro.TMP_Text>(true).text.Contains("300"));
   NativeClick(0,()=>buy.onClick.Invoke());Require(ModHelper.GetModDataInt("bartender_action","bar.level",0)==0,"business callback waits for host");
   wait=Wait(()=>ModHelper.GetModDataInt("bartender_action","bar.level",0)==1&&CustomVisible("bartender_action.bar")!=null&&CustomVisible("bartender_action.bar").ContentRoot.GetComponentsInChildren<Toggle>(true).Length>0,"host business state and rebuilt operating panel arrived",90);while(wait.MoveNext())yield return wait.Current;
   menu=CustomVisible("bartender_action.bar");NativeClick(0,()=>menu.Get("nav_shop").button.onClick.Invoke());
   wait=Wait(()=>{var w=CustomVisible("bartender_action.bar");return w!=null&&w.ContentRoot.GetComponentsInChildren<Transform>(true).Any(t=>t.name.Contains("cart_summary"));},"host procurement catalog rendered with native layout",60);while(wait.MoveNext())yield return wait.Current;
   var shop=CustomVisible("bartender_action.bar");var plus=shop.ContentRoot.GetComponentsInChildren<Button>(true).First(b=>b.gameObject.name.Contains("plus_"));string before=shop.Get("cart_summary").GetText();
   NativeClick(0,()=>plus.onClick.Invoke());
   wait=Wait(()=>CustomVisible("bartender_action.bar").Get("cart_summary").GetText()!=before,"host cart callback refreshed guest quantities and total",60);while(wait.MoveNext())yield return wait.Current;
   Signal("CUSTOM_DONE");
  }
 }
}
#endif
