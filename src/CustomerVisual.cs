using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    [Serializable] internal sealed class CustomerPose {
        public bool visible;
        public float x,y,z,r,g,b,a,solid;
    }
    // Follow the host's rendered customer, including the walk after the host has
    // cleared currentClientInstance. No arrival/leave gameplay runs on a guest.
    internal sealed class CustomerVisual {
        static readonly FieldInfo path=AccessTools.Field(typeof(StoreClientMono),"dOTweenPath");
        static readonly FieldInfo right=AccessTools.Field(typeof(StoreClientMono),"pathRight");
        static readonly int solidId=Shader.PropertyToID("_SolidBlend");
        readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
        StoreClientMono bound;
        CustomerPose target;
        CustomerPose pending;
        bool pendingSnap;
        Vector3 from;
        Color fromColor;
        float fromSolid,at;
        internal CustomerPose Capture(){
            var mono=StoreClientMono.Instance;if(mono==null||mono.image==null)return null;
            var p=mono.transform.localPosition;var c=mono.image.color;
            mono.image.GetPropertyBlock(block);
            return new CustomerPose{visible=mono.gameObject.activeSelf,x=p.x,y=p.y,z=p.z,r=c.r,g=c.g,b=c.b,a=c.a,solid=block.GetFloat(solidId)};
        }
        internal void Apply(CustomerPose pose,bool snap){
            var mono=StoreClientMono.Instance;if(pose==null||mono==null||mono.image==null)return;
            // The local shot replay owns visibility and tint until its last frame.
            // Retain the latest host pose, then resume from it when replay ends.
            if(ShotgunView.Animating){pending=pose;pendingSnap|=snap;return;}
            snap|=pendingSnap;pending=null;pendingSnap=false;
            bool show=pose.visible;
            bool jump=snap||bound!=mono||target==null||!mono.gameObject.activeSelf;
            bound=mono;
            if(mono.gameObject.activeSelf!=show)mono.gameObject.SetActive(show);
            // OnEnable may start the native idle animator. Freeze it after showing.
            mono.FreezeIdle();
            var main=path.GetValue(mono) as DG.Tweening.DOTweenPath;if(main!=null)main.DOPause();
            var other=right.GetValue(mono) as DG.Tweening.DOTweenPath;if(other!=null)other.DOPause();
            from=mono.transform.localPosition;fromColor=mono.image.color;
            mono.image.GetPropertyBlock(block);fromSolid=block.GetFloat(solidId);
            target=pose;at=Time.realtimeSinceStartup;
            if(jump)at-=.2f;
            Tick();
        }
        internal void Tick(){
            if(ShotgunView.Animating)return;
            if(pending!=null){Apply(pending,pendingSnap);return;}
            if(target==null||bound==null||bound!=StoreClientMono.Instance||!target.visible||!bound.gameObject.activeSelf)return;
            float t=Mathf.Clamp01((Time.realtimeSinceStartup-at)/.2f);
            bound.transform.localPosition=Vector3.Lerp(from,new Vector3(target.x,target.y,target.z),t);
            bound.image.color=Color.Lerp(fromColor,new Color(target.r,target.g,target.b,target.a),t);
            bound.image.GetPropertyBlock(block);block.SetFloat(solidId,Mathf.Lerp(fromSolid,target.solid,t));bound.image.SetPropertyBlock(block);
        }
    }
    [HarmonyPatch(typeof(StoreClientMono),"Update")]
    internal static class ReplicaCustomerFade {
        static bool Prefix(){return !ReplicaSession.ClientStore;}
    }
}
