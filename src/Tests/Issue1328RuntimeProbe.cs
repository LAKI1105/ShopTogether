#if LAN_PROBE
using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static string issueClipboard,issueUrl;static bool guestRelaySanitized;
  static void ObserveIssueClick(Button __instance){Debug.Log("[SMC NOTE] Unity dispatched button click: "+(__instance==null?"none":__instance.name));}
  static void ObserveIssueLauncher(){Debug.Log("[SMC NOTE] issue launcher rebuilt at frame "+Time.frameCount);}
  static void ObserveIssueRelay(byte[] __1){var text=System.Text.Encoding.UTF8.GetString(__1??new byte[0]);if(!text.Contains("ISSUE1328_GUEST"))return;guestRelaySanitized=text.IndexOf("198.51.100.42",StringComparison.OrdinalIgnoreCase)<0&&text.IndexOf("2001:db8::43",StringComparison.OrdinalIgnoreCase)<0&&text.IndexOf(Environment.UserName,StringComparison.OrdinalIgnoreCase)<0;}
  static string IssueText(){return (string)AccessTools.Method(typeof(IssueReport),"Text").Invoke(null,null);}
  static void CheckIssuePrivate(string text){
   Require(text!=null&&text.Length<=990,"report fits the 990-character limit");
   foreach(string value in new[]{"Jane Doe","Two Words","192.0.2.123","2001:db8::42","fe80::abcd","76561198000000001","Alice Test","BOB TEST",Environment.UserName})
    if(!string.IsNullOrEmpty(value))Require(text.IndexOf(value,StringComparison.OrdinalIgnoreCase)<0,"report removes a private-data sample");
  }
  static void SeedIssuePrivacy(){
   PlayerNames.FromHost(new NameList{ids=new[]{0,1},names=new[]{"Alice Test","Bob Test"}});
   string sample="ISSUE1328_PRIVATE C:\\Users\\Jane Doe\\Desktop\\test.txt /home/Two Words/file 192.0.2.123:7777 2001:db8::42 [fe80::abcd%3]:1234 76561198000000001 alice test BOB TEST "+Environment.UserName;
   IssueReport.Add(sample);CheckIssuePrivate(IssueText());
   int count=IssueReport.Count;IssueReport.Add("ISSUE1328_DUPLICATE");IssueReport.Add("ISSUE1328_DUPLICATE");Require(IssueReport.Count==count+1,"duplicate reports are stored once");
   for(int i=0;i<35;i++)IssueReport.Add("ISSUE1328_CAP_"+i);Require(IssueReport.Count==30,"only the latest 30 reports are retained");
   IssueReport.Add(sample);CheckIssuePrivate(IssueText());
  }
  static Button IssueButton(string window){
   var ui=CustomUIManager.Instance;if(ui==null||!ui.IsOpen(window))return null;var w=ui.GetWindow(window);if(w==null||w.root==null)return null;
   foreach(var b in w.root.GetComponentsInChildren<Button>(true))foreach(var t in b.GetComponentsInChildren<TMPro.TMP_Text>(true))if(t.text!=null&&t.text==Loc.Text("~SMC298~"+"（"+IssueReport.Count+"）"))return b;
   return null;
  }
  static Vector3 ButtonPoint(Button b){var rect=b.transform as RectTransform;if(rect==null)throw new InvalidOperationException("report button has no UI rect");var canvas=b.GetComponentInParent<Canvas>();if(canvas!=null)canvas=canvas.rootCanvas;var point=RectTransformUtility.WorldToScreenPoint(canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null,rect.TransformPoint(rect.rect.center));Debug.Log("[SMC NOTE] button screen="+Screen.width+"x"+Screen.height+" point="+point+" rect="+rect.rect+" canvas="+(canvas==null?"none":canvas.renderMode+"/"+canvas.scaleFactor));return point;}
  static IEnumerator IssueHost1328(Connection root){
   SeedIssuePrivacy();Diagnostics.Report("issue-host",new InvalidOperationException("ISSUE1328_HOST"));guestRelaySanitized=false;new Harmony("shop-together.issue-relay-probe").Patch(AccessTools.Method(typeof(Diagnostics),"FromFriend"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"ObserveIssueRelay"));
   var wait=Wait(()=>IssueText().Contains("ISSUE1328_GUEST"),"host recorded the friend's diagnostic",90);while(wait.MoveNext())yield return wait.Current;
   Require(guestRelaySanitized,"friend's diagnostic was anonymized before transmission to host");
   CheckIssuePrivate(IssueText());AccessTools.Method(typeof(Connection),"ShowHostReady").Invoke(root,null);
   wait=Wait(()=>IssueButton("shop_together.connection")!=null,"host room window offers issue reporting",30);while(wait.MoveNext())yield return wait.Current;
   Require(PlayerPrefs.GetString(Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_REPORT_KEY"),"").Length>0,"host report persisted in its isolated preference key");Signal("ISSUE1328_HOST_OK");
   wait=Wait(()=>Received("ISSUE1328_CLIENT_OK"),"guest retained its report for restart",60);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator IssueClient1328(Connection root){
   SeedIssuePrivacy();Diagnostics.Report("issue-guest",new InvalidOperationException("ISSUE1328_GUEST 198.51.100.42 2001:db8::43 "+Environment.UserName));
   Debug.LogError("ISSUE1328_GAME_ERROR 192.0.2.123");
   var wait=Wait(()=>IssueText().Contains("ISSUE1328_GAME_ERROR"),"guest captured a session game error",30);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("ISSUE1328_HOST_OK"),"host received guest diagnostic",90);while(wait.MoveNext())yield return wait.Current;
   CheckIssuePrivate(IssueText());AccessTools.Method(typeof(Connection),"ShowStatus").Invoke(root,null);
   wait=Wait(()=>IssueButton("shop_together.connection")!=null,"guest status window offers issue reporting",30);while(wait.MoveNext())yield return wait.Current;
   Require(PlayerPrefs.GetString(Environment.GetEnvironmentVariable("SHOPTOGETHER_TEST_REPORT_KEY"),"").Length>0,"guest report persisted in its isolated preference key");Signal("ISSUE1328_CLIENT_OK");
  }
  static IEnumerator IssueRestore1328(Connection root){
   var wait=Wait(()=>MainMenuUIController.Instance!=null,"restarted game reaches its main menu",90);while(wait.MoveNext())yield return wait.Current;InstallInput();Application.runInBackground=true;
   wait=Wait(()=>IssueReport.Count>0&&IssueButton("shop_together.launcher")!=null,"report button and count survive a real process restart",90);while(wait.MoveNext())yield return wait.Current;
   issueClipboard=issueUrl=null;IssueReport.ProbeClipboard=text=>issueClipboard=text;IssueReport.ProbeOpenPage=id=>issueUrl="https://steamcommunity.com/sharedfiles/filedetails/?id="+id;
   var inputProbe=new Harmony("shop-together.issue-click-probe");inputProbe.Patch(AccessTools.Method(typeof(Button),"OnPointerClick"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"ObserveIssueClick"));inputProbe.Patch(AccessTools.Method(typeof(Connection),"DrawLauncher"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"ObserveIssueLauncher"));
   yield return new WaitForSecondsRealtime(4f); // Let the game's own main-menu initialization and localization settle.
   Canvas.ForceUpdateCanvases();var button=IssueButton("shop_together.launcher");Require(button!=null&&button.interactable&&button.gameObject.activeInHierarchy,"report button accepts input after restart");
   var click=WindowMouseAt(ButtonPoint(button),"report button");while(click.MoveNext())yield return click.Current;
   Debug.Log("[SMC NOTE] report after click copied="+(issueClipboard!=null)+" opened="+(issueUrl!=null)+" remaining="+IssueReport.Count+" originalButtonActive="+(button!=null&&button.gameObject.activeInHierarchy)+" sameButton="+ReferenceEquals(button,IssueButton("shop_together.launcher")));
   wait=Wait(()=>issueClipboard!=null&&issueUrl!=null&&IssueReport.Count==0,"report button copied the report, requested the Workshop page and cleared the list",30);while(wait.MoveNext())yield return wait.Current;
   CheckIssuePrivate(issueClipboard);Require(issueUrl.Contains("3837477869"),"report points to the correct Workshop item");
   wait=Wait(()=>IssueButton("shop_together.launcher")==null,"report button disappears after submitting",30);while(wait.MoveNext())yield return wait.Current;
   Debug.Log("[SMC COMPLETE] issue report restored and copied after restart");
  }
 }
}
#endif
