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
  const string DumpWindow="shop_together.dumping_ground";
  static string dumpNotice;
  static void DumpNoticeObserved(string __0,string __1){dumpNotice=__0;}
  static Button DumpButton(string token){
   var w=CustomUIManager.Instance.GetWindow(DumpWindow);if(w==null||!w.IsVisible)return null;
   foreach(var b in w.root.GetComponentsInChildren<Button>(true))foreach(var t in b.GetComponentsInChildren<TMP_Text>(true))if(t.text.StartsWith(Loc.Text(token),StringComparison.Ordinal))return b;
   return null;
  }
  static int GroundCalories(GameInventory ground){int total=0;foreach(var item in GraphUtils.FindAllChildrenType<GameItem>(ground))if(DumpingGround.IsFood(item))total+=item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt()*Math.Max(1,item.unitCount);return total;}
  static int GroundWaste(GameInventory ground){int total=0;foreach(var item in ground.childItems)if(item.identifier=="nuclear_waste")total++;return total;}
  static int ShopFruit(){int total=0;foreach(var item in EmporiumEntry.Instance.GetAllOwnedItems(false,false))if(item.identifier=="nutrifruit")total+=item.unitCount;return total;}
  static GameItem DumpFixture(string id,string name){var i=PanelFixture(id,name);i.mayStackItemFunc=(a,b)=>false;return i;}
  static GameItem FillGround(GameGridInventory ground){var item=ModHelper.SpawnItem("magnifier");item.mayStackItemFunc=(a,b)=>false;GeneralHelper.SetItemOwned(item,true);ground.SetShape(item.modifiedShape.width,item.modifiedShape.height);Require(GraphUtils.TryAcceptAll(ground,item)==0,"test ground is completely filled");return item;}
  static void EmptyGround(GameGridInventory ground,GameItem filler){ground.Expel(filler);filler.Destroy();ground.SetShape(12,9);}
  static void CheckFoodSelection(){
   var food=new List<GameItem>();foreach(int value in new[]{60,40,70}){var item=ModHelper.SpawnItem("nutrifruit");item.ModifyTag("CALORIE_VALUE_TAG",t=>t.Enable().SetInt(value));item.unitCount=1;food.Add(item);}
   try{var choose=AccessTools.Method(typeof(DumpingGround),"SelectFood");var exact=new object[]{food,100,0L};choose.Invoke(null,exact);Require((long)exact[2]==100,"food selection finds an exact combination instead of a greedy excess");var over=new object[]{food,90,0L};choose.Invoke(null,over);Require((long)over[2]==100,"food selection uses the smallest excess when exact calories are impossible");}
   finally{foreach(var item in food)item.Destroy();}
  }
  static void SaveDumpResults(){
   // Write only the game's slot file in the redirected test profile, without invoking other mods' save hooks.
   AccessTools.Method(typeof(PlayerStore),"WriteSlotFile").Invoke(PlayerStore.Instance,null);
   Debug.Log("[SMC-SAVE-EXPECTED] waste="+PlayerStore.GetStoreClientData().nuclearWasteDumped+" food="+PlayerStore.GetStoreClientData().serpicoFoodKcal);
  }
  static void CheckDumpWindow(string role){
   var w=CustomUIManager.Instance.GetWindow(DumpWindow);Require(w!=null&&w.IsVisible,role+" dumping-ground controls are visible");Canvas.ForceUpdateCanvases();
   var corners=new Vector3[4];w.Rect.GetWorldCorners(corners);var canvas=w.Rect.GetComponentInParent<Canvas>();var camera=canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
   foreach(var point in corners){var p=RectTransformUtility.WorldToScreenPoint(camera,point);Require(p.x>=-1&&p.x<=Screen.width+1&&p.y>=-1&&p.y<=Screen.height+1,role+" dumping-ground controls fit the screen ("+p+")");}
  }
  static void SetFoodNight(int calories){var data=PlayerStore.GetStoreClientData();data.serpicoReported=false;data.serpicoLetterDelivered=true;data.serpicoFoodKcal=2000-calories;StoreStation.Instance.dayCounter=SerpicoStoryline.LetterDay;Require(SerpicoStoryline.IsFoodWindowOpen(),"isolated save is during Serpico's food nights");}
  static void ClearHint(){if(HintVisible())CloseHint();}
  static IEnumerator DumpHost1322(Connection root){
   var store=PlayerStore.Instance;var e=EmporiumEntry.Instance;var map=MapUIManager.Instance;var place=AfterhourLocation.Get("dumping_ground");
   store.storeState=PlayerStore.StoreState.CLOSED;store.isAfterhoursIntroduced=true;store.healthData.woundState=0;store.healthData.isWoundedFresh=false;e.invElement.SetShape(36,36);e.Validate(true);
   var trace=new Harmony("shop-together.dump1322");trace.Patch(AccessTools.Method(typeof(StoreUIManager),"Notify"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"DumpNoticeObserved"));
   CheckFoodSelection();var hostWaste=new List<GameItem>();for(int i=0;i<3;i++)hostWaste.Add(DumpFixture("nuclear_waste","DUMP_HOST_WASTE_"+i));int before=PlayerStore.GetStoreClientData().nuclearWasteDumped;
   map.Travel(place);var wait=Wait(()=>map.Current==place&&!map.IsInTransit&&DumpButton("~SMC254~")!=null,"host arrives and sees waste button",90);while(wait.MoveNext())yield return wait.Current;ClearHint();yield return null;CheckDumpWindow("host");
   var hostFiller=FillGround(e.afterhourInventory);dumpNotice=null;Click(DumpButton("~SMC254~"),"host presses waste button with full ground");Require(dumpNotice==Loc.Text("~SMC262~"),"host sees the ground-full notice");foreach(var item in hostWaste)Require(item.parentInventory!=e.afterhourInventory,"full ground leaves host waste in shop");EmptyGround(e.afterhourInventory,hostFiller);
   Click(DumpButton("~SMC254~"),"host presses the waste button");Require(GroundWaste(e.afterhourInventory)==3,"host's three waste objects reach host ground");foreach(var item in hostWaste)Require(item.parentInventory==e.afterhourInventory,"host waste moved out of shop");
   map.ReturnNow();AccessTools.Field(typeof(DumpingGround),"nextCheck").SetValue(null,0f);DumpingGround.Tick();Require(!Visible(DumpWindow),"controls close even when leaving immediately after pressing");wait=Wait(()=>map.Current==null&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host returns after dumping waste",90);while(wait.MoveNext())yield return wait.Current;
   Require(PlayerStore.GetStoreClientData().nuclearWasteDumped==before+3,"host returned waste increments nuclearWasteDumped by three");Require(GroundWaste(e.afterhourInventory)==0,"host waste disappeared after return");
   var fruit=DumpFixture("nutrifruit","DUMP_HOST_FRUIT");fruit.unitCount=4;int per=fruit.GetTagReadonly("CALORIE_VALUE_TAG").GetInt();Require(per>0&&per*2<2000,"real food fixture has usable calories");SetFoodNight(per*2);
   map.Travel(place);wait=Wait(()=>map.Current==place&&!map.IsInTransit&&DumpButton("~SMC256~")!=null,"host food button appears on a Serpico night",90);while(wait.MoveNext())yield return wait.Current;ClearHint();
   var other=ModHelper.SpawnItem("magnifier");other.name="DUMP_HOST_OTHER";other.mayStackItemFunc=(a,b)=>false;GeneralHelper.SetItemOwned(other,true);Require(GraphUtils.TryAcceptAll(e.afterhourInventory,other)==0,"host ground has unrelated loot");
   Click(DumpButton("~SMC256~"),"host presses the food button");Require(GroundCalories(e.afterhourInventory)==per*2,"host delivers exactly the remaining calories from a stack");Require(fruit.parentInventory!=null&&fruit.parentInventory!=e.afterhourInventory&&fruit.unitCount==2,"host keeps the unused food stack in the shop");
   map.ReturnNow();wait=Wait(()=>map.Current==null&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host returns after giving Serpico food",90);while(wait.MoveNext())yield return wait.Current;
   Require(PlayerStore.GetStoreClientData().serpicoFoodKcal==2000,"host return records exactly the requested food calories");Require(GroundCalories(e.afterhourInventory)==0,"host donated food is gone");Require(other.parentInventory!=null&&other.parentInventory!=e.afterhourInventory,"host's unrelated ground loot returned to the shop");
   var held=DumpFixture("nuclear_waste","DUMP_HELD_BY_HOST");var guestWaste=new List<GameItem>();for(int i=0;i<3;i++)guestWaste.Add(DumpFixture("nuclear_waste","DUMP_GUEST_WASTE_"+i));root.World.Index();e.Validate(true);Signal("DUMP_GUEST_SETUP");
   wait=Wait(()=>OutingKits.Of(1)!=null&&OutingKits.Of(1).location=="dumping_ground","guest arrived at own dumping ground",120);while(wait.MoveNext())yield return wait.Current;
   ShowStorage();yield return null;BeginDrag(held);Require(ItemMouseDragHandler.current.IsDraggingItem,"host actually begins dragging the protected waste");wait=Wait(()=>root.LocalLeases.ContainsKey(root.World.Id(held)),"host holds waste lease during guest operation",60);while(wait.MoveNext())yield return wait.Current;
   var kit=OutingKits.Of(1);var guestFiller=FillGround(kit.ground);Signal("DUMP_GUEST_FULL");wait=Wait(()=>Received("DUMP_GUEST_FULL_SEEN"),"guest received full-ground warning",90);while(wait.MoveNext())yield return wait.Current;Require(GroundWaste(kit.ground)==0&&held.parentInventory!=null,"guest full ground moved no waste");EmptyGround(kit.ground,guestFiller);Signal("DUMP_GUEST_PRESS");
   before=PlayerStore.GetStoreClientData().nuclearWasteDumped;wait=Wait(()=>GroundWaste(kit.ground)==3,"guest's waste reached only guest ground",160);while(wait.MoveNext())yield return wait.Current;
   Require(held.parentInventory!=kit.ground&&held.parentInventory!=null&&ItemMouseDragHandler.current.IsDraggingItem,"guest button leaves waste held by host untouched");Require(GroundWaste(e.afterhourInventory)==0,"guest waste did not appear on host ground");
   ItemMouseDragHandler.current.UpdateReset();pressed.Clear();Signal("DUMP_GUEST_BACK");wait=Wait(()=>OutingKits.Of(1)==null,"guest returned from waste run",90);while(wait.MoveNext())yield return wait.Current;Require(PlayerStore.GetStoreClientData().nuclearWasteDumped==before+3,"guest return increments host waste counter by three");
   var guestFruit=DumpFixture("nutrifruit","DUMP_GUEST_FRUIT");guestFruit.unitCount=4;int guestPer=guestFruit.GetTagReadonly("CALORIE_VALUE_TAG").GetInt();SetFoodNight(guestPer*2);StoreStation.Instance.dayCounter=SerpicoStoryline.LetterDay+1;Require(SerpicoStoryline.IsFoodWindowOpen(),"next night is still within the Serpico food window");Signal("DUMP_GUEST_FOOD_SETUP="+StoreStation.Instance.dayCounter);
   wait=Wait(()=>OutingKits.Of(1)!=null&&OutingKits.Of(1).location=="dumping_ground","guest arrived for Serpico food",120);while(wait.MoveNext())yield return wait.Current;kit=OutingKits.Of(1);
   var otherGuest=ModHelper.SpawnItem("magnifier");otherGuest.name="DUMP_GUEST_OTHER";otherGuest.mayStackItemFunc=(a,b)=>false;GeneralHelper.SetItemOwned(otherGuest,true);Require(GraphUtils.TryAcceptAll(kit.ground,otherGuest)==0,"guest ground has unrelated loot");int fruitBefore=ShopFruit();Signal("DUMP_GUEST_FOOD_PRESS");
   wait=Wait(()=>GroundCalories(kit.ground)>0,"guest food button reached the host",150);while(wait.MoveNext())yield return wait.Current;Require(GroundCalories(kit.ground)==guestPer*2,"guest delivers exactly the remaining food calories");Require(ShopFruit()==fruitBefore-2,"guest leaves all unneeded food units in the shop");Signal("DUMP_GUEST_FOOD_BACK");
   wait=Wait(()=>OutingKits.Of(1)==null,"guest returned after giving food",90);while(wait.MoveNext())yield return wait.Current;Require(PlayerStore.GetStoreClientData().serpicoFoodKcal==2000,"guest food is recorded by host");Require(otherGuest.parentInventory!=null&&otherGuest.parentInventory!=kit.ground,"guest's unrelated ground loot returned to the shop");Require(GroundCalories(kit.ground)==0,"guest's donated food is gone");
   SetFoodNight(guestPer*2);StoreStation.Instance.dayCounter=SerpicoStoryline.LetterDay+2;Require(SerpicoStoryline.IsFoodWindowOpen(),"third Serpico night is available for concurrent players");
   map.Travel(place);wait=Wait(()=>map.Current==place&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy&&DumpButton("~SMC256~")!=null,"host arrived for concurrent food preparation",90);while(wait.MoveNext())yield return wait.Current;ClearHint();Signal("DUMP_TOGETHER_GO="+StoreStation.Instance.dayCounter);
   wait=Wait(()=>OutingKits.Of(1)!=null&&OutingKits.Of(1).location=="dumping_ground"&&Received("DUMP_TOGETHER_READY"),"guest joined a separate dumping ground",120);while(wait.MoveNext())yield return wait.Current;kit=OutingKits.Of(1);
   Click(DumpButton("~SMC256~"),"host first prepares the remaining food");Require(GroundCalories(e.afterhourInventory)==guestPer*2,"first player reserves exactly the food needed");int leftInShop=ShopFruit();Signal("DUMP_TOGETHER_PRESS");
   wait=Wait(()=>Received("DUMP_TOGETHER_DONE"),"guest received existing food reservation",90);while(wait.MoveNext())yield return wait.Current;Require(GroundCalories(kit.ground)==0&&ShopFruit()==leftInShop,"second player cannot take the same needed food twice");
   wait=Wait(()=>OutingKits.Of(1)==null,"guest leaves before the host",90);while(wait.MoveNext())yield return wait.Current;map.ReturnNow();wait=Wait(()=>map.Current==null&&!map.IsInTransit&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"host returns after concurrent preparation",90);while(wait.MoveNext())yield return wait.Current;
   Require(PlayerStore.GetStoreClientData().serpicoFoodKcal==2000,"concurrent preparation records only the required calories");SaveDumpResults();Signal("DUMP_COMPLETE");
  }
  static IEnumerator DumpGuest1322(Connection root){
   var wait=Wait(()=>Received("DUMP_GUEST_SETUP"),"host completed own waste and food checks",240);while(wait.MoveNext())yield return wait.Current;wait=Wait(()=>!HostPhase.Away&&HostPhase.Last!=null&&HostPhase.Last.location==null,"host return state reached guest before independent outing",90);while(wait.MoveNext())yield return wait.Current;OutRequest("Go","dumping_ground",null);
   wait=Wait(()=>OutingKits.Here=="dumping_ground","guest outing state reached its location",60);while(wait.MoveNext())yield return wait.Current;
   if(StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy){AccessTools.Field(typeof(DumpingGround),"nextCheck").SetValue(null,0f);DumpingGround.Tick();Require(!Visible(DumpWindow),"guest controls wait until the arrival transition finishes");}
   wait=Wait(()=>!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"guest outing transition finished",60);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Find(root,"DUMP_GUEST_WASTE_0")!=null,"guest received shop waste fixture",60);while(wait.MoveNext())yield return wait.Current;
   Debug.Log("[SMC DEBUG] guest waste owned="+GeneralHelper.IsItemOwned(Find(root,"DUMP_GUEST_WASTE_0"))+" parent="+(Find(root,"DUMP_GUEST_WASTE_0").parentInventory!=null)+" shopOwned="+EmporiumEntry.Instance.GetAllOwnedItems(false,false).Count);
   wait=Wait(()=>DumpButton("~SMC254~")!=null,"guest sees waste button at own location",60);while(wait.MoveNext())yield return wait.Current;ClearHint();yield return null;CheckDumpWindow("guest");
   wait=Wait(()=>Received("DUMP_GUEST_FULL"),"guest ground has been filled",90);while(wait.MoveNext())yield return wait.Current;RemoteActions.LastNote="";Click(DumpButton("~SMC254~"),"guest presses waste button with full ground");wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC262~"),"guest sees the ground-full notice",90);while(wait.MoveNext())yield return wait.Current;Signal("DUMP_GUEST_FULL_SEEN");
   wait=Wait(()=>Received("DUMP_GUEST_PRESS"),"host is holding protected waste",90);while(wait.MoveNext())yield return wait.Current;wait=Wait(()=>DumpButton("~SMC254~")!=null,"guest waste button ready after warning",30);while(wait.MoveNext())yield return wait.Current;Click(DumpButton("~SMC254~"),"guest presses the waste button");
   wait=Wait(()=>OutingKits.Mine!=null&&GroundWaste(OutingKits.Mine.ground)==3,"guest sees three waste objects on own ground",90);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("DUMP_GUEST_BACK"),"host confirmed held item protection",90);while(wait.MoveNext())yield return wait.Current;OutRequest("Back");wait=Wait(()=>OutingKits.Here==null,"guest returns to the shop",90);while(wait.MoveNext())yield return wait.Current;
   string dayText=null;wait=Wait(()=>(dayText=ReceivePrefix("DUMP_GUEST_FOOD_SETUP="))!=null,"host set up another Serpico night",90);while(wait.MoveNext())yield return wait.Current;int nextDay=int.Parse(dayText);wait=Wait(()=>!HostPhase.Away&&HostPhase.Last!=null&&HostPhase.Last.day==nextDay&&StoreStation.Instance.dayCounter==nextDay,"guest received the next night before leaving",90);while(wait.MoveNext())yield return wait.Current;OutRequest("Go","dumping_ground",null);
   wait=Wait(()=>OutingKits.Here=="dumping_ground","guest outing state reached Serpico's location",50);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"guest Serpico outing transition finished",50);while(wait.MoveNext())yield return wait.Current;
   var foodData=PlayerStore.GetStoreClientData();Debug.Log("[SMC FOOD STATE] day="+StoreStation.Instance.dayCounter+" letterday="+SerpicoStoryline.LetterDay+" delivered="+foodData.serpicoLetterDelivered+" kcal="+foodData.serpicoFoodKcal+" open="+SerpicoStoryline.IsFoodWindowOpen());
   wait=Wait(()=>DumpButton("~SMC256~")!=null,"guest sees the Serpico food button",50);while(wait.MoveNext())yield return wait.Current;ClearHint();
   wait=Wait(()=>Received("DUMP_GUEST_FOOD_PRESS"),"guest ground loot fixture is ready",90);while(wait.MoveNext())yield return wait.Current;Click(DumpButton("~SMC256~"),"guest presses the food button");
   wait=Wait(()=>Received("DUMP_GUEST_FOOD_BACK"),"host confirmed exact food calories",90);while(wait.MoveNext())yield return wait.Current;OutRequest("Back");
   wait=Wait(()=>OutingKits.Here==null,"guest returned after independent food outing",90);while(wait.MoveNext())yield return wait.Current;
   string togetherDay=null;wait=Wait(()=>(togetherDay=ReceivePrefix("DUMP_TOGETHER_GO="))!=null,"host is ready for simultaneous food preparation",120);while(wait.MoveNext())yield return wait.Current;int sharedDay=int.Parse(togetherDay);
   wait=Wait(()=>!HostPhase.Away&&HostPhase.Last!=null&&HostPhase.Last.day==sharedDay&&HostPhase.Last.location=="dumping_ground","guest sees the arrived host and new night",90);while(wait.MoveNext())yield return wait.Current;OutRequest("Go","dumping_ground",null);
   wait=Wait(()=>OutingKits.Here=="dumping_ground"&&!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy&&DumpButton("~SMC256~")!=null,"guest has a separate food button while host is outside",120);while(wait.MoveNext())yield return wait.Current;ClearHint();Signal("DUMP_TOGETHER_READY");
   wait=Wait(()=>Received("DUMP_TOGETHER_PRESS"),"host prepared food first",90);while(wait.MoveNext())yield return wait.Current;wait=Wait(()=>DumpButton("~SMC256~")!=null,"guest food button is available after host reservation",30);while(wait.MoveNext())yield return wait.Current;RemoteActions.LastNote="";Click(DumpButton("~SMC256~"),"guest tries to prepare the same food");
   wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC265~"),"guest is told enough food is already prepared",90);while(wait.MoveNext())yield return wait.Current;Signal("DUMP_TOGETHER_DONE");OutRequest("Back");
   wait=Wait(()=>OutingKits.Here==null&&Received("DUMP_COMPLETE"),"guest returned and host saved the shared food result",120);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
