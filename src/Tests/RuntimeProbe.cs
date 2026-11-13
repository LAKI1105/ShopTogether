#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
    internal static partial class RuntimeProbe {
        static bool Skip(){return false;}
        // The disposable test copy must never accept a connection from the network.
        static bool ProbeListen(int preferredPort,bool automatic,ref Socket __result){
            for(int attempt=0;;attempt++){
                var socket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);socket.ExclusiveAddressUse=true;
                try{socket.Bind(new IPEndPoint(IPAddress.Loopback,attempt==0?preferredPort:0));socket.Listen(3);socket.Blocking=false;__result=socket;return false;}
                catch(SocketException error){socket.Close();if(!automatic||attempt>0||error.SocketErrorCode!=SocketError.AddressAlreadyInUse)throw;}
                catch{socket.Close();throw;}
            }
        }
        static bool ProbeLocalAddress(ref string __result){__result="127.0.0.1";return false;}
        static IEnumerator NoRouterMapping(){yield break;}
        static bool ProbeRouterMapping(ref IEnumerator __result){__result=NoRouterMapping();return false;}
        // The real InitialSave calls SaveGame, which initializes this buffer. The isolated test skips disk saves
        // but must keep that in-memory invariant for other mods that encode items immediately.
        static bool Initial(PlayerStore __instance){__instance.saveSlotId=-777;__instance.runID="smc-isolated-runtime-test";if(__instance.savedItemFeatureList==null)__instance.savedItemFeatureList=new List<ItemFeature>();return false;}
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);Debug.Log("[SMC PASS] "+message);}
        internal static IEnumerator Run(Connection root){
            SuppressPersistence();
            var runner=Argument("--smc-group")!=null?GroupCheck(root):Argument("--smc-role")=="host"?HostCheck(root):Argument("--smc-role")=="client"?ClientCheck(root):Check(root);bool more;
            while(true){try{more=runner.MoveNext();}catch(Exception error){CaptureFailure();Debug.LogError("[SMC FAIL] "+error);break;}if(!more)break;yield return runner.Current;}
            CloseControl();root.Stop();float until=Time.realtimeSinceStartup+2;while(Time.realtimeSinceStartup<until)yield return null;yield return null;Application.Quit();
        }
        static IEnumerator Check(Connection root){
            float timeout=Time.realtimeSinceStartup+90;
            while(MainMenuUIController.Instance==null&&Time.realtimeSinceStartup<timeout)yield return null;
            Require(MainMenuUIController.Instance!=null,"main menu initialized");
            NewGameData.Instance.skipIntro=true;NewGameData.Instance.disableTutorialHint=true;MainMenuUIController.Instance.StartNewGameClick();
            timeout=Time.realtimeSinceStartup+90;
            while((EmporiumEntry.Instance==null||ItemMouseDragHandler.current==null)&&Time.realtimeSinceStartup<timeout)yield return null;
            yield return new WaitForSecondsRealtime(2);
            Require(EmporiumEntry.Instance!=null&&ItemContextHandler.current!=null,"isolated store and native UI initialized");
            Require(PlayerStore.Instance.saveSlotId==-777,"isolated session has no player save slot");
            var world=new WorldReplica();var leases=new LeaseBook(world.Related);
            int savedFeatures=PlayerStore.Instance.savedItemFeatureList==null?0:PlayerStore.Instance.savedItemFeatureList.Count;
            var timer=System.Diagnostics.Stopwatch.StartNew();
            var delta=world.Capture(true,0,leases,Time.realtimeSinceStartup);
            Debug.Log("[SMC PERF] snapshot ms="+timer.Elapsed.TotalMilliseconds.ToString("F2"));
            Require((PlayerStore.Instance.savedItemFeatureList==null?0:PlayerStore.Instance.savedItemFeatureList.Count)==savedFeatures,"snapshot does not append to host save-feature buffer");
            Require(delta.items.Length>0,"native store snapshot contains item records");
            string json=WireJson.ToJson(delta);Debug.Log("[SMC SNAPSHOT] items="+delta.items.Length+" bytes="+System.Text.Encoding.UTF8.GetByteCount(json));
            var decoded=WireJson.FromJson<WorldDelta>(json);Debug.Log("[SMC DECODE] sourceVersion="+delta.version+" decodedVersion="+(decoded==null?-1:decoded.version)+" items="+(decoded==null||decoded.items==null?-1:decoded.items.Length)+" prefix="+json.Substring(0,Math.Min(70,json.Length)));var replica=new WorldReplica();replica.Apply(decoded);
            Require(replica.Items.Count==world.Items.Count,"native snapshot roundtrip preserves inventory objects");
            var item=ModHelper.SpawnItem("magnifier");GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement,item,1);int ticks=0;item.onOneSecondItemFunc=(a,b,c)=>{ticks++;};
            yield return new WaitForSecondsRealtime(2.2f);Require(ticks>0,"host executes item simulation clock");
            ticks=0;ReplicaSession.ClientStore=true;ReplicaSession.SceneReady=true;
            yield return new WaitForSecondsRealtime(2.2f);Require(ticks==0,"replica does not execute item simulation clock");
            ReplicaSession.ClientStore=false;ReplicaSession.SceneReady=false;
            Debug.Log("[SMC COMPLETE] native bootstrap and authority clock");
        }
        static void SuppressPersistence(){
            var harmony=new Harmony("local.lan_shared_shop.isolated-test");var skip=new HarmonyMethod(typeof(RuntimeProbe).GetMethod("Skip",BindingFlags.Static|BindingFlags.NonPublic));
            harmony.Patch(AccessTools.Method(typeof(PlayerStore),"InitialSave"),new HarmonyMethod(typeof(RuntimeProbe).GetMethod("Initial",BindingFlags.Static|BindingFlags.NonPublic)));
            harmony.Patch(AccessTools.Method(typeof(PlayerStore),"SaveGame"),skip);
            harmony.Patch(AccessTools.Method(typeof(AchievementTracker),"Unlock"),skip);harmony.Patch(AccessTools.Method(typeof(AchievementTracker),"IncrementCountAndUnlock"),skip);
            harmony.Patch(AccessTools.Method(typeof(DirectConnection),"Listen"),new HarmonyMethod(typeof(RuntimeProbe),"ProbeListen"));
            harmony.Patch(AccessTools.Method(typeof(Connection),"LocalAddress"),new HarmonyMethod(typeof(RuntimeProbe),"ProbeLocalAddress"));
            harmony.Patch(AccessTools.Method(typeof(RouterMapping),"Run"),new HarmonyMethod(typeof(RuntimeProbe),"ProbeRouterMapping"));
        }
    }
}
#endif
