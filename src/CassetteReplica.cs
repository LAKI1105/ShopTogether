using System;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    [Serializable] internal sealed class CassetteState { public int state; public string track; public float position; public bool playing; }
    // Playback follows the host; only the host installs the next-track callback.
    internal sealed class CassetteReplica {
        string track; int state=-1;
        internal static CassetteState Capture(){
            var e=EmporiumEntry.Instance;var item=e==null?null:e.cassettePlayer;
            if(item==null)return null;
            return new CassetteState{state=item.GetTagReadonly("CASETTE_PLAYER_STATE_INT").GetInt(),track=item.GetTagReadonly("CURRENT_PLAYING_TRACK_ID_TAG").GetString(),position=AudioManager.Instance.audioSourceMusic.time,playing=AudioManager.Instance.audioSourceMusic.isPlaying};
        }
        internal void Apply(CassetteState value){
            if(value==null||value.state<0||value.state>2||float.IsNaN(value.position)||float.IsInfinity(value.position))return;
            var audio=AudioManager.Instance;var source=audio.audioSourceMusic;
            AudioManager.StopAudioCoroutine();
            if(value.state==0){if(state!=0)audio.StopMusic();}
            else if(!string.IsNullOrEmpty(value.track)&&audio.CanPlay(value.track)){
                bool changed=track!=value.track||state<=0||source.clip==null;
                if(changed)audio.PlayMusic(value.track);
                if(value.state==1&&value.playing&&!source.isPlaying&&!AudioListener.pause){source.Play();changed=true;}
                if(source.clip!=null&&(changed||Math.Abs(source.time-value.position)>1.5f))source.time=Mathf.Clamp(value.position,0f,Math.Max(0f,source.clip.length-.01f));
                if(value.state==2)audio.PauseMusic();
                else if(state==2&&!changed)audio.UnpauseMusic();
            }
            track=value.track;state=value.state;
            var e=EmporiumEntry.Instance;
            if(e!=null&&e.cassettePlayer!=null)AccessTools.Method(typeof(MachineCassettePlayer),"RefreshPlayingFrames").Invoke(null,new object[]{e.cassettePlayer});
        }
    }
    [HarmonyPatch(typeof(MachineCassettePlayer),"OnMachineLoaded")]
    internal static class ReplicaCassetteLoad {static bool Prefix(){return !ReplicaSession.ClientStore;}}
    [HarmonyPatch(typeof(MachineCassettePlayer),"OnCassetteInventoryChanged")]
    internal static class ReplicaCassetteContents {static bool Prefix(){return !ReplicaSession.ClientStore;}}
    [HarmonyPatch(typeof(AudioManager),"OnTrackDone")]
    internal static class ReplicaCassetteAdvance {static bool Prefix(){return !ReplicaSession.ClientStore;}}
}
