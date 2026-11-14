#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  const int StressDeviceCount=320;
  static readonly List<GameItem> stressDevices=new List<GameItem>();static int stressOriginalX,stressOriginalY;
  static IEnumerator StressPrepare1327(){
   var e=EmporiumEntry.Instance;e.invElement.SetShape(480,10);foreach(var existing in new List<GameItem>(e.backInvinvElement.childItems))existing.Destroy();
   foreach(var existing in new List<GameItem>(e.invElement.childItems))if(existing.name!="SMC_HOST_ITEM"&&existing.name!="SMC_CLIENT_ITEM")existing.Destroy();e.Validate(true);stressDevices.Clear();
   GameItem movable=null;foreach(var existing in e.invElement.childItems)if(existing.name=="SMC_CLIENT_ITEM")movable=existing;
   if(movable==null||GraphUtils.TryAcceptAll(e.backInvinvElement,movable)!=0||movable.parentInventory!=e.backInvinvElement)throw new InvalidOperationException("Could not prepare stress drag on the counter");stressOriginalX=movable.modifiedShape.minX;stressOriginalY=movable.modifiedShape.minY;
   Debug.Log("[SMC NOTE] stress fixture grid after setup="+e.invElement.inventoryShape.width+"x"+e.invElement.inventoryShape.height+" type="+e.invElement.GetType().Name);
   int cursor=0;for(int i=0;i<StressDeviceCount;i++){
    var item=ModHelper.SpawnItem("bottle_printer");item.name="STRESS1327_"+i;GeneralHelper.SetItemOwned(item,true);
    if(item.modifiedShape.width>3||item.modifiedShape.height>3)throw new InvalidOperationException("Unexpected stress-device shape");int x=0,y=0;GridShape shape=null;
    while(cursor<150*3){x=(cursor%150)*3;y=(cursor/150)*3;cursor++;var candidate=new GridShapeBuilder(item.modifiedShape).SetPosition(x,y).Build();if(e.invElement.CheckShapePlacement(item,candidate,new List<GameItem>())){shape=candidate;break;}}
    if(shape==null)throw new InvalidOperationException("No empty stress-device fixture position: "+i+" grid="+e.invElement.inventoryShape.width+"x"+e.invElement.inventoryShape.height+" first="+e.invElement.inventoryShape.Get(0,5)+" next="+e.invElement.inventoryShape.Get(0,9));
    item.modifiedShape=shape;if(!e.invElement.UncheckedAccept(item)||item.parentInventory!=e.invElement||item.modifiedShape.minX!=x||item.modifiedShape.minY!=y)throw new InvalidOperationException("Could not place stress device "+i);
    stressDevices.Add(item);if(i%5==4)yield return null;
   }
   Require(stressDevices.Count==StressDeviceCount,"host has 320 real machines before friend joins");e.Validate(true);
  }
  static int StressCount(Connection root){int n=0;foreach(var item in root.World.Items.Values)if(item!=null&&item.name!=null&&item.name.StartsWith("STRESS1327_",StringComparison.Ordinal))n++;return n;}
  static IEnumerator StressHost1327(Connection root){
   Require(StressCount(root)==StressDeviceCount,"all 320 host machines are in the synchronized shop");Signal("STRESS1327_START");
   var frames=new List<double>();float until=Time.realtimeSinceStartup+15f,next=0;int batch=0;
   while(Time.realtimeSinceStartup<until){
    frames.Add(Time.unscaledDeltaTime*1000);
    if(Time.realtimeSinceStartup>=next){next=Time.realtimeSinceStartup+.5f;for(int j=0;j<12;j++){var item=stressDevices[(batch++%stressDevices.Count)];item.unitValue++;root.World.MarkDirty(item);}}
    yield return null;
   }
   var wait=Wait(()=>Received("STRESS1327_DROPPED")&&Find(root,"SMC_CLIENT_ITEM").parentInventory==EmporiumEntry.Instance.backInvinvElement&&(Find(root,"SMC_CLIENT_ITEM").modifiedShape.minX!=stressOriginalX||Find(root,"SMC_CLIENT_ITEM").modifiedShape.minY!=stressOriginalY),"friend's drag reaches the 320-machine host",120);while(wait.MoveNext())yield return wait.Current;
   Debug.Log("[SMC PERF] machines="+StressDeviceCount+" hostFrameMedianMs="+Percentile(frames,.5).ToString("F2")+" hostFrameP95Ms="+Percentile(frames,.95).ToString("F2")+" captureP95Ms="+Percentile(CaptureMilliseconds,.95).ToString("F2"));Signal("STRESS1327_HOST_OK");
  }
  static IEnumerator StressClient1327(Connection root){
   var wait=Wait(()=>StressCount(root)==StressDeviceCount&&Received("STRESS1327_START"),"guest loaded 320 machines",240);while(wait.MoveNext())yield return wait.Current;
   ShowStorage();EmporiumEntry.Instance.backInvWindow.Show();EmporiumEntry.Instance.backInvWindow.ToFront(true);yield return null;var item=Find(root,"SMC_CLIENT_ITEM");Require(item!=null,"stress-test item exists");int oldX=item.modifiedShape.minX,oldY=item.modifiedShape.minY;var watch=System.Diagnostics.Stopwatch.StartNew();var gesture=NativeDragStart1327(item);while(gesture.MoveNext())yield return gesture.Current;Require(ItemMouseDragHandler.current.IsDraggingItem,"guest starts dragging with 320 machines");double pickupMs=watch.Elapsed.TotalMilliseconds;
   var frames=new List<double>();float until=Time.realtimeSinceStartup+10f;while(Time.realtimeSinceStartup<until){frames.Add(Time.unscaledDeltaTime*1000);if(!root.Ready||!ItemMouseDragHandler.current.IsDraggingItem)throw new InvalidOperationException("connection or drag interrupted with 320 machines");yield return null;}Require(root.Ready&&ItemMouseDragHandler.current.IsDraggingItem,"connection and drag stay active with 320 machines");
   watch.Restart();gesture=NativeDragDrop1327(EmporiumEntry.Instance.backInvinvElement,item,true);while(gesture.MoveNext())yield return gesture.Current;wait=Wait(()=>item.parentInventory==EmporiumEntry.Instance.backInvinvElement&&(item.modifiedShape.minX!=oldX||item.modifiedShape.minY!=oldY),"guest completed the drop with 320 machines",120);while(wait.MoveNext())yield return wait.Current;
   double p95=Percentile(frames,.95);Debug.Log("[SMC PERF] machines="+StressDeviceCount+" guestFrameMedianMs="+Percentile(frames,.5).ToString("F2")+" guestFrameP95Ms="+p95.ToString("F2")+" harnessPickupMs="+pickupMs.ToString("F2")+" harnessDropMs="+watch.Elapsed.TotalMilliseconds.ToString("F2"));Signal("STRESS1327_DROPPED");
   wait=Wait(()=>Received("STRESS1327_HOST_OK"),"host confirmed drop in large shop",120);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
