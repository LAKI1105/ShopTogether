#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShopTogether {
 internal static partial class RuntimeProbe {
  const string HintId="shop_together.outing_hint";
  static bool HintVisible(){var w=CustomUIManager.Instance.GetWindow(HintId);return w!=null&&w.IsAlive&&w.IsVisible;}
  static void CaptureHint(string role){
   if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SMC_HINT_EVIDENCE_DIR")))return;
   RenderUI();var old=RenderTexture.active;Texture2D picture=null;
   try{RenderTexture.active=uiTarget;picture=new Texture2D(uiTarget.width,uiTarget.height,TextureFormat.RGB24,false);picture.ReadPixels(new Rect(0,0,uiTarget.width,uiTarget.height),0,0);picture.Apply();Debug.Log("[SMC-HINT-IMAGE] "+Convert.ToBase64String(ImageConversion.EncodeToPNG(picture)));}
   finally{RenderTexture.active=old;if(picture!=null)UnityEngine.Object.Destroy(picture);}
  }
  static void CheckHint(string role){
   var w=CustomUIManager.Instance.GetWindow(HintId);Require(w!=null&&w.IsVisible,role+" outing notice is visible");
   Canvas.ForceUpdateCanvases();CaptureHint(role);
   Require(w.frame!=null&&w.frame.titleText!=null&&w.frame.titleText.text==Loc.Text("~SMC249~"),role+" notice has the correct title");
   var labels=w.ContentRoot.GetComponentsInChildren<TMP_Text>(true);
   foreach(string token in new[]{"~SMC248~","~SMC251~","~SMC252~"}){
    string expected=Loc.Text(token);TMP_Text found=null;
    foreach(var label in labels)if(label.text==expected&&label.gameObject.activeInHierarchy){found=label;break;}
    Require(found!=null,role+" notice contains "+token);
    found.ForceMeshUpdate(true,true);var area=found.rectTransform.rect;var rendered=found.GetRenderedValues(false);
    Require(!found.isTextTruncated&&rendered.x<=area.width+1f&&rendered.y<=area.height+1f,role+" notice line fits "+token+" rect="+area.size+" rendered="+rendered+" truncated="+found.isTextTruncated);
   }
   var corners=new Vector3[4];w.Rect.GetWorldCorners(corners);var canvas=w.Rect.GetComponentInParent<Canvas>();var camera=canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
   foreach(var point in corners){var p=RectTransformUtility.WorldToScreenPoint(camera,point);Require(p.x>=-1&&p.x<=Screen.width+1&&p.y>=-1&&p.y<=Screen.height+1,role+" notice stays inside the screen ("+p+")");}
   var button=ButtonWithText(w,Loc.Text("~SMC250~"));Require(button!=null&&button.interactable,role+" notice has an enabled acknowledgement button");
  }
  static void CloseHint(){var w=CustomUIManager.Instance.GetWindow(HintId);var button=ButtonWithText(w,Loc.Text("~SMC250~"));Require(button!=null,"notice acknowledgement button exists");NativeClick(0,()=>button.onClick.Invoke());}
  static IEnumerator HintHost1322(Connection root){
   var store=PlayerStore.Instance;store.storeState=PlayerStore.StoreState.CLOSED;store.isAfterhoursIntroduced=true;store.healthData.woundState=0;store.healthData.isWoundedFresh=false;
   var map=MapUIManager.Instance;var place=AfterhourLocation.Get("dumping_ground");Require(map!=null&&place!=null&&place.IsAvailable(),"host dumping ground is available");
   map.Travel(place);var wait=Wait(()=>map.Current==place&&!map.IsInTransit&&HintVisible(),"host arrival displays outing notice",90);while(wait.MoveNext())yield return wait.Current;yield return null;
   CheckHint("host");CloseHint();wait=Wait(()=>!HintVisible(),"host acknowledgement closes notice",30);while(wait.MoveNext())yield return wait.Current;
   map.ReturnNow();wait=Wait(()=>map.Current==null&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host returned from first outing",90);while(wait.MoveNext())yield return wait.Current;
   map.Travel(place);wait=Wait(()=>map.Current==place&&!map.IsInTransit,"host simulated same-night re-entry",90);while(wait.MoveNext())yield return wait.Current;yield return new WaitForSecondsRealtime(1);Require(!HintVisible(),"host notice stays closed on same-night re-entry");
   map.ReturnNow();wait=Wait(()=>map.Current==null&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host returned before next night",90);while(wait.MoveNext())yield return wait.Current;
   StoreStation.Instance.dayCounter++;map.Travel(place);wait=Wait(()=>map.Current==place&&!map.IsInTransit&&HintVisible(),"host notice returns on next night",90);while(wait.MoveNext())yield return wait.Current;
   map.ReturnNow();wait=Wait(()=>map.Current==null&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy&&!HintVisible(),"host return automatically closes notice",90);while(wait.MoveNext())yield return wait.Current;
   Signal("HINT_GUEST_GO");wait=Wait(()=>Received("HINT_GUEST_RETURNED"),"guest completed first outing",120);while(wait.MoveNext())yield return wait.Current;
   var used=(HashSet<string>)AccessTools.Field(typeof(OutingKits),"outTonight").GetValue(null);used.Clear();Signal("HINT_GUEST_RETRY");
   wait=Wait(()=>Received("HINT_GUEST_SAME_DONE"),"guest completed simulated same-night re-entry",120);while(wait.MoveNext())yield return wait.Current;
   StoreStation.Instance.dayCounter++;Signal("HINT_GUEST_NEXT="+StoreStation.Instance.dayCounter);
   wait=Wait(()=>Received("HINT_GUEST_DONE"),"guest next-night notice and return verified",120);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator HintGuest1322(Connection root){
   var wait=Wait(()=>Received("HINT_GUEST_GO"),"host completed own outing checks",180);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>!HostPhase.Away&&HostPhase.Last!=null&&HostPhase.Last.location==null,"host return state reached guest before notice test",90);while(wait.MoveNext())yield return wait.Current;
   OutRequest("Go","dumping_ground",null);wait=Wait(()=>OutingKits.Here=="dumping_ground","guest reached the independent outing",90);while(wait.MoveNext())yield return wait.Current;
   if(StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy){AccessTools.Field(typeof(OutingSupplies),"nextCheck").SetValue(null,0f);OutingSupplies.Tick();Require(!HintVisible(),"guest notice waits until the arrival transition finishes");}
   wait=Wait(()=>!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"guest outing transition finished",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(HintVisible,"guest arrival displays outing notice",60);while(wait.MoveNext())yield return wait.Current;yield return null;
   CheckHint("guest");CloseHint();wait=Wait(()=>!HintVisible(),"guest acknowledgement closes notice",30);while(wait.MoveNext())yield return wait.Current;
   OutRequest("Back");wait=Wait(()=>OutingKits.Here==null&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"guest returned from first outing",90);while(wait.MoveNext())yield return wait.Current;Signal("HINT_GUEST_RETURNED");
   wait=Wait(()=>Received("HINT_GUEST_RETRY"),"host permits a simulated same-night re-entry",60);while(wait.MoveNext())yield return wait.Current;OutRequest("Go","dumping_ground",null);
   wait=Wait(()=>OutingKits.Here=="dumping_ground"&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"guest simulated same-night re-entry",90);while(wait.MoveNext())yield return wait.Current;yield return new WaitForSecondsRealtime(1);Require(!HintVisible(),"guest notice stays closed on same-night re-entry");
   OutRequest("Back");wait=Wait(()=>OutingKits.Here==null&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"guest returned before next night",90);while(wait.MoveNext())yield return wait.Current;Signal("HINT_GUEST_SAME_DONE");
   string next=null;wait=Wait(()=>(next=ReceivePrefix("HINT_GUEST_NEXT="))!=null,"next night began",60);while(wait.MoveNext())yield return wait.Current;int day=int.Parse(next);
   wait=Wait(()=>StoreStation.Instance.dayCounter==day,"guest received the next night",90);while(wait.MoveNext())yield return wait.Current;OutRequest("Go","dumping_ground",null);
   wait=Wait(()=>OutingKits.Here=="dumping_ground"&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy&&HintVisible(),"guest notice returns on next night",90);while(wait.MoveNext())yield return wait.Current;
   OutRequest("Back");wait=Wait(()=>OutingKits.Here==null&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy&&!HintVisible(),"guest return automatically closes notice",90);while(wait.MoveNext())yield return wait.Current;Signal("HINT_GUEST_DONE");
  }
 }
}
#endif
