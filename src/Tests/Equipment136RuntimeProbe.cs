#if LAN_PROBE
using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static IEnumerator EquipmentHost136(Connection root){
   var e=EmporiumEntry.Instance;var machine=e.cassettePlayer;var grid=MachineCassettePlayer.GetCassetteInventory();
   foreach(var old in new System.Collections.Generic.List<GameItem>(grid.childItems))old.Destroy();
   foreach(string id in new[]{"cassette1","cassette2"}){var tape=ModHelper.SpawnItem(id);GeneralHelper.SetItemOwned(tape,true);Require(GraphUtils.TryAcceptAll(grid,tape)==0,"cassette installed "+id);}
   machine.name="EQUIPMENT136_CASSETTE";root.World.Index();Signal("EQUIPMENT136_READY");
   int[] states={1,2,1,1,1,0};string[] tracks={"track1","track1","track1","track2","track1","track1"};
   for(int i=0;i<states.Length;i++){
    int step=i;var wait=Wait(()=>machine.GetTagReadonly("CASETTE_PLAYER_STATE_INT").GetInt()==states[step]&&(states[step]==0||machine.GetTagReadonly("CURRENT_PLAYING_TRACK_ID_TAG").GetString()==tracks[step]),"host cassette action "+i,60);while(wait.MoveNext())yield return wait.Current;
    Signal("EQUIPMENT136_HOST_"+i);wait=Wait(()=>Received("EQUIPMENT136_CLIENT_"+step),"guest cassette result "+i,60);while(wait.MoveNext())yield return wait.Current;
   }
   var heater=MachineFixture("heating_plate","EQUIPMENT136_HEATER");heater.contentWindow.Show();root.World.Index();long lease;Require(root.Leases.Claim(2,new[]{root.World.Id(heater)},Time.realtimeSinceStartup,out lease),"guest holds heater");
   var radio=ControlIntent.Controls(heater).Find(x=>x is MachineryRadioButton) as MachineryRadioButton;Require(radio!=null,"native heater control exists");int original=heater.GetTagReadonly("HEATING_PLATE_STATE").GetInt();RemoteActions.LastNote=null;
   NativeClick(0,()=>radio.SetOn(true,true));Require(heater.GetTagReadonly("HEATING_PLATE_STATE").GetInt()==original&&RemoteActions.LastNote==Loc.Text("~SMC096~"),"host control respects guest lease before callback");root.Leases.Release(2,new[]{root.World.Id(heater)});
   Signal("EQUIPMENT136_DONE");
  }
  static IEnumerator EquipmentClient136(Connection root){
   bool ready=false;var wait=Wait(()=>(ready=ready||Received("EQUIPMENT136_READY"))&&Find(root,"EQUIPMENT136_CASSETTE")!=null,"cassette ready",60);while(wait.MoveNext())yield return wait.Current;
   var machine=Find(root,"EQUIPMENT136_CASSETTE");int[] buttons={1,1,1,3,0,2};int[] states={1,2,1,1,1,0};string[] tracks={"track1","track1","track1","track2","track1","track1"};
   for(int i=0;i<buttons.Length;i++){
    int step=i;MachineClick(machine,buttons[i]);bool host=false;wait=Wait(()=>(host=host||Received("EQUIPMENT136_HOST_"+step))&&machine.GetTagReadonly("CASETTE_PLAYER_STATE_INT").GetInt()==states[step]&&(states[step]==0||AudioManager.Instance.audioSourceMusic.clip==AudioManager.Instance.audioDirectory.GetAudioClip(tracks[step]))&&AudioManager.Instance.audioSourceMusic.isPlaying==(states[step]==1),"guest native audio follows host "+i,60);while(wait.MoveNext())yield return wait.Current;
    Require(AudioManager.Instance.audioCoroutine==null,"guest never schedules next track "+i);Signal("EQUIPMENT136_CLIENT_"+i);
   }
   float before=EmporiumOption.Instance.musicVolume;MachineClick(machine,4);Require(EmporiumOption.Instance.musicVolume<=before,"volume remains personal");
   wait=Wait(()=>Received("EQUIPMENT136_DONE"),"host control lease checked",60);while(wait.MoveNext())yield return wait.Current;
  }
 }
}
#endif
