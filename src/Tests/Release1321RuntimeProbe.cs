#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static string observedMindPerk;
  static void ObserveMindPerk(StartingPerk definition){observedMindPerk=definition==null?null:definition.id;}
  static Button PerkButton(CustomUIWindow window,StartingPerk perk){
   string name=perk.GetLocalizedDisplayName();foreach(var button in window.root.GetComponentsInChildren<Button>(true)){if(!button.interactable||button.transform.parent==null)continue;foreach(var text in button.transform.parent.GetComponentsInChildren<TMPro.TMP_Text>(true))if(text.text!=null&&text.text.Contains(name))return button;}return null;
  }
  static IEnumerator Release1321Host(Connection root){
   var e=EmporiumEntry.Instance;e.invElement.SetShape(80,80);e.Validate(true);
   var hopper=PanelFixture("intake_hopper","R1321_HOPPER");var outlet=PanelFixture("outlet_hopper","R1321_OUTLET");var coordinator=PanelFixture("storage_coordinator","R1321_COORDINATOR");var mind=PanelFixture("mind_reprogrammer","R1321_MIND");
   mind.ModifyTag("MIND_REPROGRAMMER_CHARGE_INT",t=>t.Enable().SetInt(100000));mind.ModifyTag("MIND_REPROGRAMMER_COOLDOWN_DAYS_INT",t=>t.Enable().SetInt(0));
   MachineStorageCoordinator.SavePresets(coordinator,new List<MachineStorageCoordinator.Preset>{new MachineStorageCoordinator.Preset{name="HOST_KEEP",keyCode=0,itemIds=new List<int>(),itemTabs=new List<MachineStorageCoordinator.ItemTabs>()}});
   root.World.Index();Signal("R1321_READY");var ids=new[]{"intake_hopper","outlet_hopper","storage_coordinator","mind_reprogrammer"};
   for(int i=0;i<ids.Length;i++){
    int index=i;var wait=Wait(()=>Received("R1321_OPEN_"+index),"guest opened "+ids[i],120);while(wait.MoveNext())yield return wait.Current;Require(!Visible(ids[i]),"guest "+ids[i]+" window did not open on host");Signal("R1321_HOST_CLEAR_"+i);
    if(i==0){wait=Wait(()=>HopperHelper.GetOrder(hopper)==9,"guest hopper slider changed host order",120);while(wait.MoveNext())yield return wait.Current;}
    if(i==1){wait=Wait(()=>HopperHelper.GetColor(outlet)==1,"guest outlet color setting changed on host",120);while(wait.MoveNext())yield return wait.Current;}
    if(i==2){wait=Wait(()=>CoordinatorPresets.Load(coordinator,root.PeerIdentity(1)).Count>0,"guest coordinator preset stored on host",120);while(wait.MoveNext())yield return wait.Current;Require(MachineStorageCoordinator.LoadPresets(coordinator)[0].name=="HOST_KEEP","guest coordinator preset leaves host preset unchanged");}
    if(i==3){string perk=null;wait=Wait(()=>(perk=ReceivePrefix("R1321_PERK="))!=null,"guest chose an editable perk",120);while(wait.MoveNext())yield return wait.Current;wait=Wait(()=>StartingPerk.IsPerkActive(perk),"guest mind-reprogrammer choice applied on host",40);while(wait.MoveNext())yield return wait.Current;Require(MindReprogrammerHelper.GetCharge(mind)<100000,"host paid the mind-reprogrammer charge");}
    Signal("R1321_APPLIED_"+i);
   }
   var store=PlayerStore.Instance;EmporiumOption.Instance.autoCallNext=false;StoreShutterButton.Instance.OpenShutter();store.lockNextBellFlag=0;if(store.storeClientManager.clientStack.Count==0)store.storeClientManager.GenerateClient();store.TryCallNextClient();
   var customerWait=Wait(()=>store.currentClientInstance!=null,"reminder customer fixture arrived",90);while(customerWait.MoveNext())yield return customerWait.Current;var customer=store.currentClientInstance.GetClientBlueprint();customer.identifier="wild_upgrade_reminder";customer.tryInterceptDismiss=null;customer.OnDismiss=null;Signal("R1321_WILD_READY");
   var wildWait=Wait(()=>Received("R1321_WILD_OPEN"),"guest opened Wild with reminder present",120);while(wildWait.MoveNext())yield return wildWait.Current;Require(!WildNetworkUI.IsOpen,"guest Wild window stayed off host screen");Signal("R1321_WILD_CLEAR");
   wildWait=Wait(()=>store.currentClientInstance==null,"closing guest Wild dismisses reminder customer on host",120);while(wildWait.MoveNext())yield return wildWait.Current;Signal("R1321_WILD_LEFT");
   var item=Find(root,"SMC_CLIENT_ITEM");int id=root.World.Id(item);Signal("R1321_STALE_READY");var staleWait=Wait(()=>Received("R1321_STALE_HELD"),"guest began dragging item before host change",120);while(staleWait.MoveNext())yield return staleWait.Current;
   long old=root.World.Versions[id];item.unitValue+=123;root.World.MarkDirty(item);staleWait=Wait(()=>root.World.Versions[id]>old,"host item change advanced its version",120);while(staleWait.MoveNext())yield return staleWait.Current;Signal("R1321_STALE_CHANGED");
   staleWait=Wait(()=>Received("R1321_STALE_NOTICE"),"guest received a stale-operation notice",120);while(staleWait.MoveNext())yield return staleWait.Current;Require(root.HasPeer(1),"guest stayed connected after stale operation");Signal("R1321_HOST_DONE");staleWait=Wait(()=>Received("R1321_CLIENT_DONE"),"guest acknowledged 1.3.21 tests",60);while(staleWait.MoveNext())yield return staleWait.Current;
  }
  static IEnumerator Release1321Client(Connection root){
   var wait=Wait(()=>Find(root,"R1321_HOPPER")!=null&&Find(root,"R1321_OUTLET")!=null&&Find(root,"R1321_COORDINATOR")!=null&&Find(root,"R1321_MIND")!=null&&Received("R1321_READY"),"1.3.21 machine fixtures replicated",120);while(wait.MoveNext())yield return wait.Current;
   string[] names={"R1321_HOPPER","R1321_OUTLET","R1321_COORDINATOR","R1321_MIND"};string[] windows={"intake_hopper","outlet_hopper","storage_coordinator","mind_reprogrammer"};
   for(int i=0;i<names.Length;i++){
    int index=i;var item=Find(root,names[i]);NativeClick(0,()=>{if(index==1)item.ConfigureSlotItem();else item.ActivateSlotItem();});wait=Wait(()=>Visible(windows[index]),"native operation opens "+windows[index]+" on guest",120);while(wait.MoveNext())yield return wait.Current;Signal("R1321_OPEN_"+i);wait=Wait(()=>Received("R1321_HOST_CLEAR_"+index),"host did not open "+windows[index],120);while(wait.MoveNext())yield return wait.Current;
    var window=CustomUIManager.Instance.GetWindow(windows[i]);
    if(i==0){Slider order=null;foreach(var s in window.root.GetComponentsInChildren<Slider>(true))if(s.minValue==1&&s.maxValue==15){order=s;break;}Require(order!=null&&order.interactable,"intake hopper order slider is available");SettingsClick(()=>order.value=9);}
    if(i==1){var color=window.Get("color_1");var button=color==null||color.root==null?null:color.root.GetComponent<Button>();Require(button!=null&&button.interactable,"outlet color button is available");SettingsClick(()=>button.onClick.Invoke());}
    if(i==2){var element=window.Get("new_preset");Require(element!=null&&element.button!=null&&element.button.interactable,"coordinator new-preset button is available");NativeClick(0,()=>element.button.onClick.Invoke());}
    if(i==3){StartingPerk choice=null;foreach(var p in MindReprogrammerHelper.GetEditablePerks())if(!StartingPerk.IsPerkActive(p.id)&&MindReprogrammerHelper.CanApply(item,p)){choice=p;break;}Require(choice!=null,"an editable mind perk is available");var button=PerkButton(window,choice);Require(button!=null,"mind-reprogrammer perk has an enabled UI button");var trace=new Harmony("shop-together.trace-mind-probe");trace.Patch(AccessTools.Method(typeof(MindReprogrammerHelper),"Apply"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"ObserveMindPerk"));observedMindPerk=null;Signal("R1321_PERK="+choice.id);NativeClick(0,()=>button.onClick.Invoke());Require(observedMindPerk==choice.id,"selected native perk button invoked matching action (expected "+choice.id+", observed "+observedMindPerk+")");}
    wait=Wait(()=>Received("R1321_APPLIED_"+index),"host confirmed "+windows[index]+" action",120);while(wait.MoveNext())yield return wait.Current;CustomUIManager.Instance.CloseWindow(windows[i]);root.ReleaseAll();
   }
   wait=Wait(()=>CurrentCustomer()!=null&&CurrentCustomer().identifier=="wild_upgrade_reminder"&&Received("R1321_WILD_READY"),"Wild reminder customer reached guest",120);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>WildNetworkUI.Open());wait=Wait(()=>WildNetworkUI.IsOpen,"guest opened Wild for reminder customer",60);while(wait.MoveNext())yield return wait.Current;Signal("R1321_WILD_OPEN");wait=Wait(()=>Received("R1321_WILD_CLEAR"),"host Wild screen stayed closed",60);while(wait.MoveNext())yield return wait.Current;var wild=CustomUIManager.Instance.GetWindow("wilds_network");NativeClick(0,()=>wild.Close());wait=Wait(()=>Received("R1321_WILD_LEFT"),"host dismissed reminder after guest closed Wild",120);while(wait.MoveNext())yield return wait.Current;
   wait=Wait(()=>Received("R1321_STALE_READY"),"stale item fixture ready",90);while(wait.MoveNext())yield return wait.Current;var stale=Find(root,"SMC_CLIENT_ITEM");ShowStorage();yield return null;int id=root.World.Id(stale);long old=root.World.Versions[id];BeginDrag(stale);Require(ItemMouseDragHandler.current.IsDraggingItem,"guest began native drag before host item update");wait=Wait(()=>root.LocalLeases.ContainsKey(id),"guest acquired item before host update",90);while(wait.MoveNext())yield return wait.Current;Signal("R1321_STALE_HELD");wait=Wait(()=>Received("R1321_STALE_CHANGED"),"host changed item after drag began",90);while(wait.MoveNext())yield return wait.Current;
   root.World.Versions[id]=old;RemoteActions.LastNote="";Drop(EmporiumEntry.Instance.backInvinvElement,stale);wait=Wait(()=>RemoteActions.LastNote==Loc.Text("~SMC167~"),"stale drag displays the host refusal reason",120);while(wait.MoveNext())yield return wait.Current;Require(root.IsClient&&root.Ready,"stale drag refusal preserves connection");Signal("R1321_STALE_NOTICE");wait=Wait(()=>Received("R1321_HOST_DONE"),"host stayed connected after stale drag",90);while(wait.MoveNext())yield return wait.Current;Signal("R1321_CLIENT_DONE");
  }
 }
}
#endif
