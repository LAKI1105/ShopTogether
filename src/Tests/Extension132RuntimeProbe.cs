#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
    internal static partial class RuntimeProbe {
        static int clickEdge=-1, bellCalls, vendingCalls; static int Aimers {get{return ((HashSet<int>)typeof(ShotgunView).GetField("aimers",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Count;}}
        static bool ReadMouseEdge(int button){return fakeInput&&button==clickEdge;}
        static void BellObserved(){if(Connection.Current!=null&&Connection.Current.IsHost)bellCalls++;}
        static void VendingObserved(){if(Connection.Current!=null&&Connection.Current.IsHost)vendingCalls++;}
        static void NativeClick(int button,Action action){clickEdge=button;LocalControl.depth++;try{action();}finally{LocalControl.depth--;clickEdge=-1;}}
        static IEnumerator ExtraHost(Connection root,GameItem item){
            var h=new Harmony("shop-together.regression132");
            h.Patch(AccessTools.Method(typeof(StoreBell),"RingBell"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"BellObserved"));
            h.Patch(AccessTools.Method(typeof(VendingMachineHelper),"TransferCashToPlayer"),postfix:new HarmonyMethod(typeof(RuntimeProbe),"VendingObserved"));
            item.ModifyTag("STORAGE_COORDINATOR_TAG",t=>t.Enable());
            MachineStorageCoordinator.SavePresets(item,new List<MachineStorageCoordinator.Preset>{new MachineStorageCoordinator.Preset{name="HOST_ONLY",keyCode=(int)KeyCode.F8,itemIds=new List<int>(),itemTabs=new List<MachineStorageCoordinator.ItemTabs>()}});
            var store=PlayerStore.Instance;store.isShootingUnlocked=true;StoreUIManager.Instance.shotgun.SetActive(true);VendingMachineHelper.UnlockVendingMachine(0);
            var vending=EmporiumEntry.Instance.vendingMachineElement.childItem;Require(vending!=null,"vending machine fixture exists");
            vending.ModifyTag("VENDING_CASH",t=>t.Enable().SetInt(321));
            int beforeCash=store.playerCash;StoreUIManager.Instance.CloseAllUI();StoreShutterButton.Instance.OpenShutter();store.lockNextBellFlag=0;
            Signal("EXTRA_READY");
            var wait=Wait(()=>Received("PRESETS_SAVED"),"friend coordinator presets saved",40);while(wait.MoveNext())yield return wait.Current;
            wait=Wait(()=>CoordinatorPresets.Load(item,root.PeerIdentity(1)).Count==1,"host stored friend-specific coordinator presets",30);while(wait.MoveNext())yield return wait.Current;
            Require(MachineStorageCoordinator.LoadPresets(item)[0].name=="HOST_ONLY","friend shortcut leaves host presets unchanged");Signal("PRESETS_CONFIRMED");
            wait=Wait(()=>Received("REJOINED"),"friend rejoined and retained own shortcut",90);while(wait.MoveNext())yield return wait.Current;
            Require(MachineStorageCoordinator.LoadPresets(item)[0].keyCode==(int)KeyCode.F8,"host shortcut still unchanged after friend reconnect");
            wait=Wait(()=>bellCalls>0,"friend bell click executed on host",30);while(wait.MoveNext())yield return wait.Current;
            wait=Wait(()=>vendingCalls>0,"friend vending cash action executed on host",30);while(wait.MoveNext())yield return wait.Current;
            Require(store.playerCash==beforeCash+321&&vending.GetTagReadonly("VENDING_CASH").GetInt()==0,"vending revenue transferred once into host cash");wait=Wait(()=>store.currentClientInstance!=null,"bell produced a customer for shooting fixture",40);while(wait.MoveNext())yield return wait.Current;
            var blueprint=store.currentClientInstance.GetClientBlueprint();blueprint.forceShootable=true;blueprint.isSecurity=false;blueprint.nonShootable=false;blueprint.survivesShot=true;blueprint.holsterResumesDialogue=true;
            Signal("GUN_READY");
            wait=Wait(()=>Aimers>0,"host knows friend is aiming",30);while(wait.MoveNext())yield return wait.Current;Require(!StoreUIManager.Instance.isShootingMode,"friend aiming does not switch host camera");
            wait=Wait(()=>Received("CLIENT_AIM"),"client aiming camera and interface follow host",30);while(wait.MoveNext())yield return wait.Current;
            Signal("HOLSTER_NOW");wait=Wait(()=>Aimers==0&&Received("LOWERED"),"friend right click lowers own gun and clears host aiming record",30);while(wait.MoveNext())yield return wait.Current;Signal("RAISE_AGAIN");wait=Wait(()=>Aimers>0&&Received("AIM_AGAIN"),"friend aims again for shot",30);while(wait.MoveNext())yield return wait.Current;int shots=ShotgunView.Shots;Signal("FIRE_NOW");wait=Wait(()=>ShotgunView.Shots>shots,"client trigger fired on host",30);while(wait.MoveNext())yield return wait.Current;
            wait=Wait(()=>Received("CLIENT_SHOT"),"client replayed shot animation",30);while(wait.MoveNext())yield return wait.Current;
            wait=Wait(()=>!StoreUIManager.Instance.isShootingMode,"host returns to shop after shot",30);while(wait.MoveNext())yield return wait.Current;
            wait=Wait(()=>Received("EXTRA_DONE"),"client restored normal view",30);while(wait.MoveNext())yield return wait.Current;
            Signal("EXTRA_HOST_DONE");Debug.Log("[SMC PASS] 1.3.2 gun, reconnect presets, bell and vending roundtrips");
        }
        static IEnumerator ExtraClient(Connection root){
            var wait=Wait(()=>Received("EXTRA_READY"),"additional gameplay fixtures ready",40);while(wait.MoveNext())yield return wait.Current;
            var item=Find(root,"SMC_CLIENT_ITEM");wait=Wait(()=>item.IsTag("STORAGE_COORDINATOR_TAG"),"coordinator fixture replicated",30);while(wait.MoveNext())yield return wait.Current;
            NativeClick(0,()=>MachineStorageCoordinator.SavePresets(item,new List<MachineStorageCoordinator.Preset>{new MachineStorageCoordinator.Preset{name="FRIEND_ONLY",keyCode=(int)KeyCode.F9,itemIds=new List<int>(),itemTabs=new List<MachineStorageCoordinator.ItemTabs>()}}));
            Signal("PRESETS_SAVED");wait=Wait(()=>Received("PRESETS_CONFIRMED"),"host confirmed private shortcut",30);while(wait.MoveNext())yield return wait.Current;
            string identity=root.MyIdentity;root.Stop();AccessTools.Method(typeof(Connection),"ReturnToMenu").Invoke(root,null);wait=Wait(()=>MainMenuUIController.Instance!=null&&EmporiumEntry.Instance==null,"friend returned to menu before reconnect",90);while(wait.MoveNext())yield return wait.Current;root.ProbeJoin(Argument("--smc-invite"));wait=Wait(()=>root.Ready,"rejoined host with same identity",90);while(wait.MoveNext())yield return wait.Current;
            Require(root.MyIdentity==identity,"coordinator identity survives reconnect");item=Find(root,"SMC_CLIENT_ITEM");
            var presets=MachineStorageCoordinator.LoadPresets(item);Require(presets.Count==1&&presets[0].name=="FRIEND_ONLY"&&presets[0].keyCode==(int)KeyCode.F9,"friend shortcut restored after real reconnect");Signal("REJOINED");
            NativeClick(0,()=>UnityEngine.Object.FindObjectOfType<StoreBell>().RingBell());
            var vending=EmporiumEntry.Instance.vendingMachineElement.childItem;NativeClick(0,()=>StoreVendingMachine.Instance.OnClick());Require(vending.contentWindow.handler.gameObject.activeInHierarchy,"friend vending panel opens locally");
            NativeClick(0,()=>VendingMachineHelper.TransferCashToPlayer(vending));
            wait=Wait(()=>Received("GUN_READY"),"host customer ready",45);while(wait.MoveNext())yield return wait.Current;
            wait=Wait(()=>PlayerStore.Instance.currentClientInstance!=null,"shooting customer replicated before local pickup",30);while(wait.MoveNext())yield return wait.Current;var fixture=PlayerStore.Instance.currentClientInstance.GetClientBlueprint();fixture.forceShootable=true;fixture.isSecurity=false;fixture.nonShootable=false;fixture.survivesShot=true;fixture.holsterResumesDialogue=true;NativeClick(0,()=>UnityEngine.Object.FindObjectOfType<Shotgun>().OnClick());
            var ui=StoreUIManager.Instance;wait=Wait(()=>ShotgunView.Aiming&&ui.isShootingMode&&ui.shootingCamera.activeSelf&&ui.shootingUI.activeSelf,"client gun view follows host",30);while(wait.MoveNext())yield return wait.Current;Signal("CLIENT_AIM");
            wait=Wait(()=>Received("HOLSTER_NOW"),"host ready for right click",30);while(wait.MoveNext())yield return wait.Current;NativeClick(1,()=>ui.ExitShootingMode());wait=Wait(()=>!ShotgunView.Aiming&&!ui.isShootingMode&&ui.mainCamera.activeSelf,"right click lowers local gun",30);while(wait.MoveNext())yield return wait.Current;Signal("LOWERED");wait=Wait(()=>Received("RAISE_AGAIN"),"host confirmed holster",30);while(wait.MoveNext())yield return wait.Current;NativeClick(0,()=>UnityEngine.Object.FindObjectOfType<Shotgun>().OnClick());wait=Wait(()=>ShotgunView.Aiming&&ui.shootingUI.activeSelf,"aiming restored",30);while(wait.MoveNext())yield return wait.Current;Signal("AIM_AGAIN");wait=Wait(()=>Received("FIRE_NOW"),"both aiming views ready",30);while(wait.MoveNext())yield return wait.Current;
            int seen=(int)typeof(ShotgunView).GetField("seen",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);NativeClick(0,()=>ui.Shoot());
            wait=Wait(()=>(int)typeof(ShotgunView).GetField("seen",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)>seen&&ui.shootingWoundRenderer.gameObject.activeSelf,"friend receives shot and displays animation",30);while(wait.MoveNext())yield return wait.Current;
            yield return new WaitForSecondsRealtime(2);Signal("CLIENT_SHOT");
            wait=Wait(()=>!ShotgunView.Aiming&&!ui.isShootingMode&&ui.mainCamera.activeSelf,"shot animation returns client to normal camera",30);while(wait.MoveNext())yield return wait.Current;Signal("EXTRA_DONE");
            wait=Wait(()=>Received("EXTRA_HOST_DONE"),"host confirmed all extra operations",30);while(wait.MoveNext())yield return wait.Current;
        }
    }
}
#endif
