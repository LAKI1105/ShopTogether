#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Steamworks;
namespace ShopTogether {
 internal static partial class RuntimeProbe {
  static int queuedSteam1347,readSteam1347;
  static int channelSteam1347;
  const ulong peerSteam1347=76561197960265827UL;
  // Intercept only this synchronous probe's calls. No fabricated packet is
  // sent to Steam and the real TLS peers continue after the patches are removed.
  static bool PeekSteam1347(ref uint __0,int __1,ref bool __result){
   if(__1!=channelSteam1347)return true;
   __0=queuedSteam1347>0?(uint)MessageStream.Piece:0;__result=queuedSteam1347>0;return false;
  }
  static bool ReadSteam1347(byte[] __0,uint __1,ref uint __2,ref CSteamID __3,int __4,ref bool __result){
   if(__4!=channelSteam1347)return true;
   __2=0;__3=new CSteamID(peerSteam1347);__result=false;
   if(queuedSteam1347>0){if(__1<MessageStream.Piece)throw new InvalidOperationException("Probe destination too small");Array.Clear(__0,0,MessageStream.Piece);__0[0]=173;__2=(uint)MessageStream.Piece;queuedSteam1347--;readSteam1347++;__result=true;}return false;
  }
  static void PollCapacity1347(){
   Require(SteamRelay.Available,"Steam API available for polling integration check");
   AccessTools.Method(typeof(SteamRelay),"Hook").Invoke(null,null);
   var hostField=AccessTools.Field(typeof(SteamRelay),"expectedHost");ulong previousHost=(ulong)hostField.GetValue(null);
   var channels=(Dictionary<ulong,SteamRelay.SteamChannel>)AccessTools.Field(typeof(SteamRelay),"open").GetValue(null);
   Require(!channels.ContainsKey(peerSteam1347),"polling fixture does not replace an existing peer");
   channelSteam1347=(int)AccessTools.Field(typeof(SteamRelay),"Channel").GetRawConstantValue();
   var patch=new Harmony("shop-together.poll1347-probe");var channel=new SteamRelay.SteamChannel(peerSteam1347);
   var stream=(MessageStream)AccessTools.Field(typeof(SteamRelay.SteamChannel),"stream").GetValue(channel);
   try{
    patch.Patch(AccessTools.Method(typeof(SteamNetworking),"IsP2PPacketAvailable"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"PeekSteam1347"));
    patch.Patch(AccessTools.Method(typeof(SteamNetworking),"ReadP2PPacket"),prefix:new HarmonyMethod(typeof(RuntimeProbe),"ReadSteam1347"));
    channels.Add(peerSteam1347,channel);hostField.SetValue(null,peerSteam1347);
    var block=new byte[MessageStream.Piece];for(int i=0;i<MessageStream.MaxQueued/MessageStream.Piece;i++)channel.Arrive(block);
    queuedSteam1347=1;readSteam1347=0;SteamRelay.Poll();
    Require(queuedSteam1347==1&&readSteam1347==0&&!channel.Failed,"full guest queue leaves the next packet unread without failure");
    var buffer=new byte[32768];Require(channel.Receive(buffer)==buffer.Length,"full queue continues draining");SteamRelay.Poll();
    Require(readSteam1347==0&&!channel.Failed,"partial room does not consume a packet that would overflow");
    for(int i=1;i<MessageStream.Piece/buffer.Length;i++)Require(channel.Receive(buffer)==buffer.Length,"queue consumer makes progress");
    SteamRelay.Poll();Require(queuedSteam1347==0&&readSteam1347==1&&!channel.Failed,"poll resumes when one complete packet fits");
    int total=0,last=0,n;while((n=channel.Receive(buffer))>0){total+=n;if(total==MessageStream.MaxQueued-MessageStream.Piece+buffer.Length)last=buffer[0];}
    Require(total==MessageStream.MaxQueued&&last==173,"resumed packet arrives after all earlier bytes without loss or reordering");
    channel.Fail("probe failure");queuedSteam1347=1;SteamRelay.Poll();bool threw=false;try{bool writing=channel.Writing;}catch(InvalidOperationException){threw=true;}
    Require(threw&&queuedSteam1347==1,"capacity gate does not conceal a real channel failure");
    stream.Close();channel=new SteamRelay.SteamChannel(peerSteam1347);stream=(MessageStream)AccessTools.Field(typeof(SteamRelay.SteamChannel),"stream").GetValue(channel);channels[peerSteam1347]=channel;hostField.SetValue(null,0UL);
    for(int i=0;i<MessageStream.MaxQueued/MessageStream.Piece;i++)channel.Arrive(block);
    SteamRelay.Poll();Require(channel.Failed&&queuedSteam1347==0,"host receive-overflow guard remains active");
   }finally{patch.UnpatchAll("shop-together.poll1347-probe");channels.Remove(peerSteam1347);hostField.SetValue(null,previousHost);stream.Close();queuedSteam1347=0;}
  }
  static IEnumerator Release1347(Connection root,bool host){
   PollCapacity1347();
   var flow=host?LeaseHost1345(root):LeaseClient1345(root);
   while(flow.MoveNext())yield return flow.Current;
  }
 }
}
#endif
