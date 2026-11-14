#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static IEnumerator NativeMouseAction1327(string action) {
   Require(pointer.x>=0&&pointer.x<Screen.width&&pointer.y>=0&&pointer.y<Screen.height,"native drag pointer is on the game screen");
   int x=Mathf.RoundToInt(pointer.x),y=Screen.height-Mathf.RoundToInt(pointer.y);
   Debug.Log("[SMC-MOUSE-"+action+"] "+(++mouseRequestSequence)+" "+x+" "+y);
   if(action=="DOWN") {var wait=Wait(()=>Input.GetMouseButtonDown(0)||Input.GetMouseButton(0),"Unity received native mouse press for dragging",20);while(wait.MoveNext())yield return wait.Current;}
   else if(action=="UP") {var wait=Wait(()=>Input.GetMouseButtonUp(0)||!Input.GetMouseButton(0),"Unity received native mouse release for dropping",20);while(wait.MoveNext())yield return wait.Current;}
   else yield return new WaitForSecondsRealtime(1.5f); // The external driver polls only this isolated desktop once per second.
  }
  static IEnumerator NativeDragStart1327(GameItem item) {
   pointer=Point(item);yield return null;var input=NativeMouseAction1327("DOWN");while(input.MoveNext())yield return input.Current;
   pointer+=new Vector3(30,20,0);input=NativeMouseAction1327("MOVE");while(input.MoveNext())yield return input.Current;
   var wait=Wait(()=>ItemMouseDragHandler.current.IsDraggingItem,"native mouse starts dragging "+item.name,30);while(wait.MoveNext())yield return wait.Current;
  }
  static IEnumerator NativeDragRelease1327() {var input=NativeMouseAction1327("UP");while(input.MoveNext())yield return input.Current;}
  static IEnumerator NativeDragDrop1327(GameInventory destination,GameItem item,bool requireDifferentCell=false) {
   var grid=destination as GameGridInventory;if(grid==null)throw new InvalidOperationException("Native drag destination is not a grid");
   var available=destination.TryFindOneValidInventorySlot(item);if(available==null){string occupants="";foreach(var child in grid.childItems){var s=child.modifiedShape;occupants+=" "+child.identifier+"@"+(s==null?"none":s.minX+","+s.minY+" "+s.width+"x"+s.height);}int cells=0;for(int y=0;y<grid.inventoryShape.height;y++)for(int x=0;x<grid.inventoryShape.width;x++)if(grid.inventoryShape.Get(x,y)!=0)cells++;var shape=item.modifiedShape;
    throw new InvalidOperationException("No valid native drag destination: grid="+grid.inventoryShape.width+"x"+grid.inventoryShape.height+" cells="+cells+" entries="+grid.childItems.Count+" item="+(shape==null?"none":shape.width+"x"+shape.height)+" owned="+GeneralHelper.IsItemOwned(item)+" itemCount="+item.unitCount+" maxRemove="+item.MaxNumRemove()+" mayRemove="+item.MayRemove()+" mayHaveSlot="+grid.MayHaveValidInventorySlot(item)+" parent="+(item.parentInventory==null?"none":item.parentInventory.GetType().Name)+occupants);}
   EmporiumEntry.Instance.ShowFrontCounters();EmporiumEntry.Instance.invWindow.Show();EmporiumEntry.Instance.Validate(true);if(destination==EmporiumEntry.Instance.backInvinvElement){EmporiumEntry.Instance.backInvWindow.Show();EmporiumEntry.Instance.backInvWindow.ToFront(true);}Canvas.ForceUpdateCanvases();
   var preview=((GameItemElement)item).tempShape.Build();float size=UISettings.current.gridSize;SlotMarker actual=null;int oldX=item.modifiedShape.minX,oldY=item.modifiedShape.minY;
   Debug.Log("[SMC NOTE] native drag grid="+grid.inventoryShape.width+"x"+grid.inventoryShape.height+" item="+preview.width+"x"+preview.height+" slot="+(available.itemGridShape==null?"none":available.itemGridShape.minX+","+available.itemGridShape.minY));
   for(int y=0;y<=grid.inventoryShape.height-preview.height&&actual==null;y++)for(int col=0;col<=grid.inventoryShape.width-preview.width;col++){
    int maxX=grid.inventoryShape.width-preview.width;int x=item.name=="SMC_CLIENT_ITEM"?maxX-col:col;
    var shape=new GridShapeBuilder(preview).SetPosition(x,y).Build();
    Vector3 target=grid.rectTransform.TransformPoint(new Vector3((shape.minX+(shape.maxX-shape.minX+1)*.5f)*size,-(shape.minY+(shape.maxY-shape.minY+1)*.5f)*size,0));
    pointer+=target-(Vector3)((GameItemElement)item).GetFakeRotatorCenteredPosition();var move=NativeMouseAction1327("MOVE");while(move.MoveNext())yield return move.Current;
    var hit=ModReflect.Get<SlotMarker>(ItemMouseDragHandler.current,"lastSlot");if(hit!=null&&hit.inventory==destination&&hit.itemGridShape!=null&&(!requireDifferentCell||hit.itemGridShape.minX!=oldX||hit.itemGridShape.minY!=oldY)&&grid.CheckShapePlacement(item,hit.itemGridShape,new List<GameItem>{item})){actual=hit;break;}
   }
   Require(actual!=null,"native mouse resolved the drag destination");var release=NativeDragRelease1327();while(release.MoveNext())yield return release.Current;
  }
 }
}
#endif
