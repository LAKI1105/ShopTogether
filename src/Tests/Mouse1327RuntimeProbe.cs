#if LAN_PROBE
using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static int mouseRequestSequence;
  static Vector3 MouseSprite(GameObject machine){
   Require(machine!=null&&machine.activeInHierarchy,"vending machine exists on guest scene");var camera=Camera.main;Require(camera!=null,"game camera available for native pointer");
   var events=UnityEngine.EventSystems.EventSystem.current;string detail="";
   foreach(var sprite in machine.GetComponentsInChildren<SpriteRenderer>())if(sprite!=null&&sprite.enabled&&sprite.sprite!=null){var b=sprite.bounds;
    for(int y=1;y<=9;y++)for(int x=1;x<=9;x++){var world=new Vector3(Mathf.Lerp(b.min.x,b.max.x,x/10f),Mathf.Lerp(b.min.y,b.max.y,y/10f),b.center.z);Vector3 p=camera.WorldToScreenPoint(world);if(p.z<=0||p.x<0||p.x>=Screen.width||p.y<0||p.y>=Screen.height)continue;
     var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();if(events!=null)events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events){position=p},hits);
     if(hits.Count==0||hits[0].gameObject.transform.IsChildOf(machine.transform))return p;
     if(detail.Length==0){var hit=hits[0].gameObject;var node=hit.GetComponentInParent<TreeNodeRender>();var w=node==null?null:node.FindParentWindow();var image=hit.GetComponent<UnityEngine.UI.Image>();detail=hit.name+" window="+(w==null?"none":w.titleString+" visible="+w.IsVisible())+" sprite="+(image==null||image.sprite==null?"none":image.sprite.name)+" color="+(image==null?"none":image.color.ToString());
      if(node!=null){detail+=" node="+node.name+" directWindow="+(node.window==null?"none":node.window.titleString)+" parent="+(node.parent==null?"none":node.parent.name);var handler=AccessTools.Field(typeof(TreeNodeRender),"eventHandler").GetValue(node);if(handler!=null)foreach(string field in new[]{"mouseClickFunc","mouseDownFunc","mouseUpFunc"}){var list=AccessTools.Field(handler.GetType(),field).GetValue(handler) as System.Collections.IEnumerable;if(list!=null)foreach(var entry in list){var d=entry as Delegate;if(d!=null)detail+=" "+field+"="+d.Method.DeclaringType.Name+"."+d.Method.Name;}}}
     }
    }
   }
   throw new InvalidOperationException("vending picture has no unobstructed screen position: "+detail);
  }
  static IEnumerator WindowMouseClick(GameObject machine,PixelWindow panel,string label){
   panel.Hide();yield return null;pointer=MouseSprite(machine);
   var events=UnityEngine.EventSystems.EventSystem.current;var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
   if(events!=null)events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events){position=pointer},hits);
   foreach(var hit in hits){var top=hit.gameObject;if(top==null)continue;var graphic=top.GetComponent<UnityEngine.UI.Graphic>();var input=top.GetComponentInParent<Interactable>();var downHandler=UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerDownHandler>(top);var click=UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(top);
    string path=top.name;for(var t=top.transform.parent;t!=null;t=t.parent){var group=t.GetComponent<CanvasGroup>();var node=t.GetComponent<TreeNodeRender>();var element=node!=null?node.FindParentElement():null;string components="";if(t.name=="Node")foreach(var component in t.GetComponents<MonoBehaviour>())if(component!=null)components+=","+component.GetType().Name;path=t.name+(group!=null?"[alpha="+group.alpha+",blocks="+group.blocksRaycasts+"]":"")+(element!=null?"[element="+element.GetType().Name+"]":"")+components+"/"+path;}
    UnityEngine.Debug.Log("[SMC NOTE] raycast "+label+" "+path+" graphic="+(graphic==null?"none":graphic.GetType().Name+"/"+graphic.color.a+"/"+graphic.canvasRenderer.GetInheritedAlpha())+" interactable="+(input==null?"none":input.name)+" down="+(downHandler==null?"none":downHandler.name)+" click="+(click==null?"none":click.name));
    if(hits.IndexOf(hit)>=5)break;
   }
   var clickInput=WindowMouseAt(pointer,label);while(clickInput.MoveNext())yield return clickInput.Current;
   var wait=Wait(()=>panel.IsVisible(),"native mouse opens "+label,20);while(wait.MoveNext())yield return wait.Current;
   panel.Hide();yield return null;
  }
  static IEnumerator WindowMouseAt(Vector3 position,string label){
   var events=UnityEngine.EventSystems.EventSystem.current;
   if(events!=null){AccessTools.Field(typeof(UnityEngine.EventSystems.EventSystem),"m_HasFocus").SetValue(events,true);var module=events.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();if(module!=null)module.forceModuleActive=true;}
   pointer=position;Require(pointer.x>=0&&pointer.x<Screen.width&&pointer.y>=0&&pointer.y<Screen.height,label+" is inside the visible game screen ("+pointer+" / "+Screen.width+"x"+Screen.height+")");yield return null;
   if(events!=null){var testHits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events){position=pointer},testHits);var active=events.currentInputModule;UnityEngine.Debug.Log("[SMC NOTE] " + label + " focus="+events.isFocused+" app="+Application.isFocused+" cursor="+Cursor.lockState+" uiPosition="+(active==null?"none":active.input.mousePosition.ToString())+" mousePresent="+(active!=null&&active.input.mousePresent)+" module="+(active==null?"none":active.GetType().Name)+" hits="+testHits.Count);for(int i=0;i<Math.Min(4,testHits.Count);i++){var o=testHits[i].gameObject;var click=UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(o);var g=o.GetComponent<UnityEngine.UI.Graphic>();var c=g==null?null:g.canvas;var rect=o.transform as RectTransform;var corners=new Vector3[4];if(rect!=null)rect.GetWorldCorners(corners);string path=o.name;for(var t=o.transform.parent;t!=null;t=t.parent)path=t.name+"/"+path;UnityEngine.Debug.Log("[SMC NOTE] " + label + " hit="+path+" click="+(click==null?"none":click.name)+" canvas="+(c==null?"none":c.name+"/"+c.sortingOrder+"/"+c.overrideSorting)+" corners="+corners[0]+" to "+corners[2]);}}
   int x=Mathf.RoundToInt(pointer.x),y=Screen.height-Mathf.RoundToInt(pointer.y);
   UnityEngine.Debug.Log("[SMC-MOUSE-REQUEST] "+(++mouseRequestSequence)+" "+x+" "+y);bool down=false,edge=false;float until=Time.realtimeSinceStartup+15f;
   while(!down&&Time.realtimeSinceStartup<until){yield return null;edge|=Input.GetMouseButtonDown(0);down|=edge||Input.GetMouseButton(0);}
   Require(down,"Unity received actual window mouse input for "+label);yield return null;
   if(events!=null&&events.currentInputModule!=null){var field=AccessTools.Field(typeof(UnityEngine.EventSystems.PointerInputModule),"m_PointerData");var data=field==null?null:field.GetValue(events.currentInputModule) as System.Collections.IDictionary;var p=data!=null&&data.Contains(-1)?data[-1] as UnityEngine.EventSystems.PointerEventData:null;UnityEngine.Debug.Log("[SMC NOTE] "+label+" downEdge="+edge+" pointer="+(p==null?"none":p.position+" press="+(p.pointerPress==null?"none":p.pointerPress.name)+" ray="+(p.pointerCurrentRaycast.gameObject==null?"none":p.pointerCurrentRaycast.gameObject.name)+" eligible="+p.eligibleForClick));}
   bool up=false;until=Time.realtimeSinceStartup+2f;while(!up&&Time.realtimeSinceStartup<until){yield return null;up=Input.GetMouseButtonUp(0);}UnityEngine.Debug.Log("[SMC NOTE] "+label+" mouse release="+up);yield return new WaitForSecondsRealtime(.2f);
  }
  static IEnumerator MouseHost1327(Connection root){
   VendingMachineHelper.UnlockVendingMachine(0);VendingFountainHelper.UnlockVendingFountain();StoreShutterButton.Instance.OpenShutter();root.World.Index();Signal("MOUSE1327_READY");
   var wait=Wait(()=>Received("MOUSE1327_DONE"),"guest used native mouse on both vending machines",120);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator MouseClient1327(Connection root){
   var e=EmporiumEntry.Instance;var wait=Wait(()=>e.vendingMachineElement.childItem!=null&&e.vendingFountainElement.childItem!=null&&Received("MOUSE1327_READY"),"vending machines reached the guest",120);while(wait.MoveNext())yield return wait.Current;
   StoreUIManager.Instance.CloseAllUI();StoreUIManager.Instance.DisableBackingBlock();EmporiumEntry.Instance.ShowFrontArea();yield return null;
   wait=Wait(()=>StoreVendingFountain.Instance!=null&&StoreVendingMachine.Instance!=null&&StoreVendingFountain.Instance.gameObject.activeInHierarchy&&StoreVendingMachine.Instance.gameObject.activeInHierarchy,"guest vending sprites are visible in the shop",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>!HostPhase.ShutterClosed(),"host's open shutter reached the guest",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>StoreShutterButton.Instance.blockerMaskObject!=null&&!StoreShutterButton.Instance.blockerMaskObject.gameObject.activeSelf,"guest releases the shutter mask when its open animation finishes",90);while(wait.MoveNext())yield return wait.Current;
   // Preserve the private desktop: send Win32 mouse messages only to this game's window, never move the user's cursor.
   // Unity's screen-position getter is supplied with that window-relative position; button edges remain native.
   fakeInput=true;LocalControl.ProbeInput=true;Application.runInBackground=true;
   var click=WindowMouseClick(StoreVendingFountain.Instance.gameObject,e.vendingFountainElement.childItem.contentWindow,"water fountain");while(click.MoveNext())yield return click.Current;
   click=WindowMouseClick(StoreVendingMachine.Instance.gameObject,e.vendingMachineElement.childItem.contentWindow,"vending machine");while(click.MoveNext())yield return click.Current;
   Require(e.responseWindow!=null,"guest customer-choice window exists");e.responseWindow.Show();Interactable.isPaused=true;TooltipContainer.isPaused=true;yield return null;e.HideResponseWindow();yield return null;
   click=WindowMouseClick(StoreVendingFountain.Instance.gameObject,e.vendingFountainElement.childItem.contentWindow,"water fountain after customer choices");while(click.MoveNext())yield return click.Current;
   click=WindowMouseClick(StoreVendingMachine.Instance.gameObject,e.vendingMachineElement.childItem.contentWindow,"vending machine after customer choices");while(click.MoveNext())yield return click.Current;
   Signal("MOUSE1327_DONE");
  }
 }
}
#endif
