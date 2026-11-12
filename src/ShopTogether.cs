using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    public sealed class Entry : IMod {
        GameObject instance;
        public void Init(ModManifest manifest) { Achievements.Define(); Cursors.DefineSettings(); Diagnostics.Watch(); }
        public void OnEnable() {
            instance=new GameObject("ShopTogetherAuthority");UnityEngine.Object.DontDestroyOnLoad(instance);instance.AddComponent<Connection>();
        }
        public void OnDisable(){if(instance!=null)UnityEngine.Object.Destroy(instance);}
    }
    public sealed partial class Connection : MonoBehaviour {
        internal const string BuildVersion="1.3.24";
        internal const string WireVersion=BuildVersion+"-"+BuildIdentity.Code;
        const string LauncherId="shop_together.launcher",WindowId="shop_together.connection";
        // Up to 8 players: the host and 7 friends.
        internal const int MaxFriends=7;
        internal static Connection Current;
        /// <summary>Friend side: how many requests this player has sent to the host (to tell whether a click already asked it for something).</summary>
        internal static int Outgoing;
        internal bool IsHost,IsClient,Ready;
        internal int PlayerId;
        internal readonly WorldReplica World=new WorldReplica();
        internal LeaseBook Leases;
        internal readonly Dictionary<int,long> LocalLeases=new Dictionary<int,long>();
        readonly HashSet<int> requested=new HashSet<int>();
        readonly List<Request> waiting=new List<Request>();
        readonly HashSet<int> deferredRelease=new HashSet<int>();
        readonly List<Peer> peers=new List<Peer>();
        readonly AdmissionGuard admissions=new AdmissionGuard();
        Socket listener;Peer server;RoomIdentity identity;PublicAddress publicAddress;
        RouterMapping routerMapping;MappingStatus mappingStatus;
        readonly Queue<Candidate> joinCandidates=new Queue<Candidate>();
        string inviteText="",localInvite="",externalInvite="";
        string address="",portText="7777",hostKey="",secret="",status="",publicEndpoint="";
        ModReport modReport;string hostNotice="";readonly LargeTransfer.Assembler incoming=new LargeTransfer.Assembler();
        bool launcherShown,hostReadyVisible,startingClient,needsFull,lanOnly,statusOnMenu,returningToMenu;
        bool awaitingFull;int applyFailures;
        int nextPlayer,generation;long sequence,remoteSequence;float nextSync,nextRenew,nextDigest,nextPhase,nextPhaseCheck;string lastPhase;
        internal readonly DriftCheck Drift=new DriftCheck();
        readonly Queue<WorldDelta> pendingWorld=new Queue<WorldDelta>();
        sealed class Peer : IDisposable {
            internal Socket socket;internal IChannel channel;internal int id;
            internal bool authenticated,matched,versionSent,sceneReady,initialSent,closing,modsOk;internal string name="";internal int activity;
            internal long lastSequence,lastRemote;internal float deadline,partialSince,closeAt;
            internal readonly Queue<byte[]> outgoing=new Queue<byte[]>();internal readonly List<byte> input=new List<byte>();
            internal readonly byte[] buffer=new byte[32768];internal readonly ProtocolGuard guard=new ProtocolGuard();
            // Cursors, phase and the player list are resent regularly; when the link is behind they are skipped
            // instead of piling up. Everything else waits its turn; only a link that stays this far behind is dropped.
            internal static bool Replaceable(byte type){return type==39||type==34||type==40;}
            internal void Send(byte type,byte[] bytes){if(Replaceable(type)&&outgoing.Count>=32)return;if(outgoing.Count>=8192||queuedBytes+bytes.Length+5>256L*1024*1024)throw new Exception("~SMC134~");queuedBytes+=bytes.Length+5;byte[] packet=new byte[bytes.Length+5];int n=bytes.Length+1;packet[0]=(byte)(n>>24);packet[1]=(byte)(n>>16);packet[2]=(byte)(n>>8);packet[3]=(byte)n;packet[4]=type;Buffer.BlockCopy(bytes,0,packet,5,bytes.Length);outgoing.Enqueue(packet);}
            internal long queuedBytes;internal int partialSize;
            internal void Write(){for(int i=0;i<64&&channel.Ready&&!channel.Writing&&outgoing.Count>0;i++){var packet=outgoing.Dequeue();queuedBytes-=packet.Length;channel.Send(packet);}}
            public void Dispose(){if(channel!=null)channel.Dispose();else if(socket!=null)socket.Close();outgoing.Clear();queuedBytes=0;input.Clear();}
        }
        void Start(){Application.runInBackground=true;Current=this;Leases=new LeaseBook(World.Related);
#if LAN_PROBE
            StartCoroutine(RuntimeProbe.Run(this));
#endif
        }
        void OnApplicationQuit(){SteamRelay.ShuttingDown=true;}
        void OnDestroy(){SteamRelay.ShuttingDown=true;Stop();if(Current==this)Current=null;}
        internal void Stop(){MachineSettings.Reset();PanelRelay.Reset();ShopActions.Reset();OutingSupplies.Reset();DumpingGround.Reset();if(IsHost){try{OutingKits.ReturnEveryone();}catch(Exception e){Debug.LogWarning("[ShopTogether] outing return: "+e.Message);}}OutingKits.Reset();
            hostReadyVisible=false;lanOnly=false;hostNotice="";
            if(routerMapping!=null){routerMapping.Dispose();routerMapping=null;}mappingStatus=null;joinCandidates.Clear();localInvite="";externalInvite="";publicEndpoint="";
            if(publicAddress!=null){publicAddress.Dispose();publicAddress=null;}
            foreach(var peer in peers)peer.Dispose();peers.Clear();SteamRelay.CloseAll();SteamInvite.ClearPresence();SteamInvite.Invited.Clear();
            if(server!=null){server.Dispose();server=null;}if(listener!=null){listener.Close();listener=null;}
            if(identity!=null){identity.Dispose();identity=null;}
            IsHost=false;IsClient=false;Ready=false;startingClient=false;pendingWorld.Clear();Diagnostics.Reset();incoming.Reset();generation++;
            LocalLeases.Clear();requested.Clear();waiting.Clear();deferredRelease.Clear();NativeActions.holders.Clear();
            World.Reset();HostPhase.Reset();awaitingFull=false;applyFailures=0;Cursors.Reset();ReplicaVisuals.Reset();PlayerNames.Reset();nextNames=0;remoteSequence=0;nextPhase=0;nextPhaseCheck=0;lastPhase=null;sequence=0;nextPlayer=0;nextSync=0;nextRenew=0;needsFull=false;
            if(Leases!=null)Leases=new LeaseBook(World.Related);
        }
        static string Password(){byte[] bytes=new byte[24];using(var r=RandomNumberGenerator.Create())r.GetBytes(bytes);return Convert.ToBase64String(bytes);}
        static string LocalAddress(){try{foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces()){if(adapter.OperationalStatus!=OperationalStatus.Up)continue;var p=adapter.GetIPProperties();if(p.GatewayAddresses.Count==0)continue;foreach(var a in p.UnicastAddresses)if(a.Address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a.Address))return a.Address.ToString();}}catch{}return "127.0.0.1";}
        void Host(){int port;if(!int.TryParse(portText,out port)||port<1||port>65535){status="~SMC130~";ShowHost();return;}if(secret.Length<8||secret.Length>64||secret==SteamInvite.FriendToken){status="~SMC122~";ShowHost();return;}
            OpenHost(port,false,false);}
        void QuickHost(){secret=Password();OpenHost(7777,true,false);}
        // Same room, but the router is never touched and no outside address is looked up: only machines on this network can join.
        void LanHost(){secret=Password();OpenHost(7777,true,true);}
        void OpenHost(int port,bool automatic,bool lan){
            Stop();try{
                listener=DirectConnection.Listen(port,automatic);
                port=((IPEndPoint)listener.LocalEndPoint).Port;portText=port.ToString();identity=new RoomIdentity();IsHost=true;PlayerId=0;status="~SMC034~";
                lanOnly=lan;SteamInvite.Invited.Clear();if(!lan&&SteamRelay.Available){try{SteamRelay.Host(id=>IsHost&&peers.Count<MaxFriends&&admissions.Admit("steam:"+id,Time.realtimeSinceStartup));}catch(Exception e){Debug.LogWarning("[ShopTogether] Steam relay unavailable: "+e.Message);}}RefreshInvites(IPAddress.Parse(LocalAddress()),null);if(lan)mappingStatus=new MappingStatus{Message="~SMC042~"};else{routerMapping=new RouterMapping(port);StartCoroutine(routerMapping.Run());}ShowHostReady();
            }catch(Exception error){Stop();status="~SMC145~"+error.Message;ShowHost();}
        }
        void RefreshInvites(IPAddress local,IPEndPoint external){
            var invite=new Invite{Local=new IPEndPoint(local,int.Parse(portText)),Fingerprint=identity.Fingerprint,Password=secret,SteamId=lanOnly?0:SteamRelay.LocalId};
            localInvite=InviteCode.Encode(invite);externalInvite="";
            if(external!=null){invite.External=external;externalInvite=InviteCode.Encode(invite);publicEndpoint=external.ToString();}
        }
        void JoinInvite(){
            Invite invite;string error;if(!InviteCode.TryDecode(inviteText,out invite,out error)){status=error;ShowJoin();return;}
            // Same network: direct first. Otherwise Steam (works through any router, relayed if needed), then direct.
            var candidates=new List<Candidate>();bool localFirst=GatewayRoute.IsLocalSubnet(invite.Local.Address);bool steam=invite.SteamId!=0&&SteamRelay.Available&&invite.SteamId!=SteamRelay.LocalId;
            if(localFirst)candidates.Add(new Candidate{Endpoint=invite.Local});if(steam)candidates.Add(new Candidate{Steam=invite.SteamId});
            if(invite.External!=null)candidates.Add(new Candidate{Endpoint=invite.External});if(!localFirst)candidates.Add(new Candidate{Endpoint=invite.Local});
            if(invite.SteamId!=0&&!SteamRelay.Available)Debug.LogWarning("[ShopTogether] Steam is not running; trying direct connection only");
            hostKey=invite.Fingerprint;secret=invite.Password;try{BeginJoin(candidates);}catch(Exception failure){Stop();status="~SMC149~"+failure.Message;ShowJoin();}
        }
        sealed class Candidate{internal IPEndPoint Endpoint;internal ulong Steam;}
        void BeginJoin(IEnumerable<IPEndPoint> endpoints){var list=new List<Candidate>();foreach(var e in endpoints)list.Add(new Candidate{Endpoint=e});BeginJoin(list);}
        void BeginJoin(List<Candidate> candidates){modReport=null;Stop();foreach(var c in candidates)joinCandidates.Enqueue(c);IsClient=true;NextJoinCandidate();ShowStatus();}
        void NextJoinCandidate(){
            if(server!=null){server.Dispose();server=null;}
            if(joinCandidates.Count==0)throw new IOException("~SMC148~");
            var candidate=joinCandidates.Dequeue();
            if(candidate.Steam!=0){try{server=new Peer{channel=SteamRelay.Connect(candidate.Steam),deadline=Time.realtimeSinceStartup+25};status="~SMC050~";Debug.Log("[ShopTogether] joining through Steam");return;}catch(Exception e){Debug.LogWarning("[ShopTogether] Steam join failed: "+e.Message);if(joinCandidates.Count>0){NextJoinCandidate();return;}throw;}}
            var endpoint=candidate.Endpoint;var socket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
            try{socket.Blocking=false;try{socket.Connect(endpoint);}catch(SocketException error){if(error.SocketErrorCode!=SocketError.WouldBlock&&error.SocketErrorCode!=SocketError.InProgress)throw;}server=new Peer{socket=socket,deadline=Time.realtimeSinceStartup+8};status="~SMC051~";}
            catch{socket.Close();if(joinCandidates.Count>0){NextJoinCandidate();return;}throw;}
        }
        void Join(){string target=address.Trim();int marker=target.IndexOf('#');if(marker>=0){hostKey=target.Substring(marker+1);target=target.Substring(0,marker);}hostKey=SecureChannel.NormalizeFingerprint(hostKey);IPEndPoint endpoint;string error;
            if(hostKey==null){status="~SMC164~";ShowManualJoin();return;}
            if(!DirectConnection.Parse(target,portText,out endpoint,out error)){status=error;ShowManualJoin();return;}
            if(secret.Length<8||secret.Length>64){status="~SMC122~";ShowManualJoin();return;}
            try{BeginJoin(new[]{endpoint});}catch(Exception e){Stop();status=e.Message;ShowStatus();}}
        void Update(){
            if(!IsClient&&MainMenuUIController.Instance!=null&&EmporiumEntry.Instance==null){ReplicaSession.ClientStore=false;ReplicaSession.SceneReady=false;}
            if(MainMenuUIController.Instance!=null&&CustomUIManager.Instance!=null&&(!launcherShown||launcherState!=LauncherState())){launcherShown=true;DrawLauncher();}
            SteamInvite.Hook();
            // Joined from the Steam friends list (or "Invite to Game"): connect through Steam once at the main menu.
            if(SteamInvite.Pending!=null&&!IsHost&&!IsClient&&MainMenuUIController.Instance!=null&&CustomUIManager.Instance!=null){string connect=SteamInvite.Pending;SteamInvite.Pending=null;ulong hostId;
                if(SteamInvite.TryParse(connect,out hostId)&&SteamRelay.Available&&hostId!=SteamRelay.LocalId){secret=SteamInvite.FriendToken;hostKey="";try{BeginJoin(new List<Candidate>{new Candidate{Steam=hostId}});}catch(Exception failure){Stop();status="~SMC149~"+failure.Message;ShowStatus();}}}
            if(MainMenuUIController.Instance==null)launcherShown=false;else{returningToMenu=false;if(statusOnMenu&&CustomUIManager.Instance!=null){statusOnMenu=false;ShowStatus();}}
            if(publicAddress!=null&&publicAddress.IsCompleted){string ip=publicAddress.Value;publicAddress.Dispose();publicAddress=null;IPAddress parsed;if(IsHost&&IPAddress.TryParse(ip,out parsed)&&InviteCode.IsPublic(parsed)){if(routerMapping!=null){routerMapping.Dispose();routerMapping=null;}RefreshInvites(IPAddress.Parse(LocalAddress()),new IPEndPoint(parsed,int.Parse(portText)));mappingStatus=new MappingStatus{Message="~SMC071~"};}else status="~SMC144~";if(hostReadyVisible)ShowHostReady();}
            if(IsHost&&routerMapping!=null&&mappingStatus!=routerMapping.Current){mappingStatus=routerMapping.Current;RefreshInvites(mappingStatus.Local??IPAddress.Parse(LocalAddress()),mappingStatus.External);if(hostReadyVisible)ShowHostReady();}
            try{
                if(IsHost||IsClient){SteamRelay.Poll();Diagnostics.Soft("supplies",OutingSupplies.Tick);Diagnostics.Soft("dumping ground",DumpingGround.Tick);NativeActions.Sweep();Diagnostics.Soft("panels",()=>PanelRelay.Tick(this));Diagnostics.Soft("machine panels",()=>MachineDisplay.Tick(this));if(IsClient)Diagnostics.Flush(this);}
                if(IsHost){
                    SteamRelay.SteamChannel arrival;while((arrival=SteamRelay.NextArrival())!=null){if(peers.Count>=MaxFriends){arrival.Dispose();continue;}peers.Add(new Peer{channel=arrival,id=++nextPlayer,deadline=Time.realtimeSinceStartup+20});Debug.Log("[ShopTogether] friend connecting through Steam");}
                    if(listener.Poll(0,SelectMode.SelectRead)){var incoming=listener.Accept();if(peers.Count>=MaxFriends||!admissions.Admit(((IPEndPoint)incoming.RemoteEndPoint).Address.ToString(),Time.realtimeSinceStartup))incoming.Close();else{var peer=new Peer{socket=incoming,id=++nextPlayer,deadline=Time.realtimeSinceStartup+15};peers.Add(peer);peer.channel=new SecureChannel(incoming,identity.Certificate,null);}}
                    foreach(var peer in peers.ToArray())try{Pump(peer,true);}catch(Exception e){Leases.Disconnect(peer.id);peer.Dispose();peers.Remove(peer);PlayerNames.Changed=true;Debug.Log("[ShopTogether] peer disconnected ("+PlayerNames.Of(peer.id)+"): "+e.GetType().Name+" "+e.Message);}
                    Ready=EmporiumEntry.Instance!=null&&MainMenuUIController.Instance==null;
                    if(Ready){Diagnostics.Soft("outing tick",()=>OutingKits.HostTick(this));Diagnostics.Soft("phone tick",()=>PhoneShare.HostTick(this));}Diagnostics.Soft("phase",BroadcastPhase);Diagnostics.Soft("cursors",()=>Cursors.Tick(this));Diagnostics.Soft("names",BroadcastNames);
                    if(Ready&&Time.realtimeSinceStartup>=nextRenew){nextRenew=Time.realtimeSinceStartup+5;Leases.Renew(0,Time.realtimeSinceStartup);}
                    if(Ready&&Time.realtimeSinceStartup>=nextSync){nextSync=Time.realtimeSinceStartup+.2f;Leases.Expire(Time.realtimeSinceStartup);bool initial=peers.Exists(p=>p.authenticated&&p.modsOk&&!p.initialSent);bool recipients=peers.Exists(p=>p.authenticated&&p.modsOk&&(p.sceneReady||!p.initialSent));if(recipients){var delta=World.Capture(needsFull||initial,0,Leases,Time.realtimeSinceStartup);World.LastSentVersion=delta.version;byte[] raw=Encode(delta);var packed=LargeTransfer.Pack(20,raw);if(packed.Type==26)Debug.Log("[ShopTogether] large update: "+(raw.Length/1024)+" KB in "+packed.Parts.Length+" compressed pieces (full="+delta.full+")");foreach(var p in peers.ToArray())if(p.authenticated&&p.modsOk&&(p.sceneReady||!p.initialSent)){try{LargeTransfer.Deliver(p.Send,packed);p.initialSent=true;}catch(Exception dropped){Leases.Disconnect(p.id);p.Dispose();peers.Remove(p);PlayerNames.Changed=true;Debug.Log("[ShopTogether] peer dropped while sending ("+PlayerNames.Of(p.id)+"): "+dropped.GetType().Name+" "+dropped.Message);}}needsFull=false;
                        if(Time.realtimeSinceStartup>=nextDigest&&peers.Exists(p=>p.authenticated&&p.modsOk&&p.sceneReady)){nextDigest=Time.realtimeSinceStartup+4;var digest=LargeTransfer.Pack(32,Encode(World.BuildDigest()));foreach(var p in peers.ToArray())if(p.authenticated&&p.modsOk&&p.sceneReady){try{LargeTransfer.Deliver(p.Send,digest);}catch(Exception dropped){Leases.Disconnect(p.id);p.Dispose();peers.Remove(p);PlayerNames.Changed=true;Debug.Log("[ShopTogether] peer dropped while sending ("+PlayerNames.Of(p.id)+"): "+dropped.GetType().Name+" "+dropped.Message);}}}}else World.Index();}
                }
                if(IsClient&&server!=null){
                    if(server.channel==null&&server.socket!=null&&server.socket.Poll(0,SelectMode.SelectWrite)){if((int)server.socket.GetSocketOption(SocketOptionLevel.Socket,SocketOptionName.Error)!=0)throw new Exception("~SMC147~");server.channel=new SecureChannel(server.socket,null,hostKey);}
                    Pump(server,false);Diagnostics.Soft("cursors",()=>Cursors.Tick(this));Diagnostics.Soft("machine settings",MachineSettings.Flush);
                    if(server.authenticated&&!startingClient&&pendingWorld.Count>0){startingClient=true;StartCoroutine(StartClientScene(generation));}
                    var applyTimer=System.Diagnostics.Stopwatch.StartNew();
                    for(int applied=0;applied<8&&pendingWorld.Count>0&&ReplicaSession.SceneReady&&(applied==0||applyTimer.ElapsedMilliseconds<6);applied++){var next=pendingWorld.Dequeue();
                        if(awaitingFull&&!next.full){}
                        else{
                            // An update that cannot be applied asks the host for the whole shop again instead of ending the session.
                            try{World.Apply(next);awaitingFull=false;if(next.full)applyFailures=0;
                                if(!Ready){Ready=true;status="~SMC025~";CustomUIManager.Instance.CloseWindow(WindowId);}}
                            catch(Exception failure){Diagnostics.Report("apply",failure);if(++applyFailures>=5)throw;Debug.LogWarning("[ShopTogether] update not applied, asking the host for the whole shop: "+failure.Message);awaitingFull=true;server.Send(24,new byte[0]);}
                        }}
                    if(Ready&&Time.realtimeSinceStartup>=nextRenew){nextRenew=Time.realtimeSinceStartup+5;Send(new Request{kind=RequestKind.Renew});}
                    if(Ready&&!LocalControl.Focused&&LocalLeases.Count>0)ReleaseAll();
                    if(Ready&&pendingWorld.Count==0){ResyncRequest resync=null;Diagnostics.Soft("drift",()=>resync=Drift.Step(World,2.0));if(resync!=null)server.Send(33,Encode(resync));}
                }
            }catch(Exception e){
                if(IsClient&&server!=null&&!server.authenticated&&joinCandidates.Count>0){try{NextJoinCandidate();return;}catch(Exception retry){e=retry;}}
                bool client=IsClient;if(client&&server!=null&&server.authenticated){try{Diagnostics.Report("session ended",e);Diagnostics.Flush(this,true);server.Write();}catch{}}Stop();status="~SMC132~"+e.Message;Debug.LogException(e);if(client&&MainMenuUIController.Instance==null){statusOnMenu=true;ReturnToMenu();}else ShowStatus();}
        }
        IEnumerator StartClientScene(int sessionGeneration){
            if(MainMenuUIController.Instance==null)throw new InvalidOperationException("~SMC163~");
            ReplicaSession.ClientStore=true;ReplicaSession.SceneReady=false;
            // Native inventory prefabs are initialized inside the store scene, not the main menu.
            ReplicaSession.Seed=pendingWorld.Peek();
            NewGameData.Instance.skipIntro=true;NewGameData.Instance.disableTutorialHint=true;
            MainMenuUIController.Instance.StartNewGameClick();
            float deadline=Time.realtimeSinceStartup+90;
            while((EmporiumEntry.Instance==null||ItemMouseDragHandler.current==null)&&Time.realtimeSinceStartup<deadline){if(sessionGeneration!=generation||!IsClient)yield break;yield return null;}
            if(sessionGeneration!=generation||!IsClient)yield break;
            if(EmporiumEntry.Instance==null){Stop();yield break;}
            yield return null;if(sessionGeneration!=generation||!IsClient)yield break;ReplicaSession.SceneReady=true;server.Send(24,new byte[0]);
        }
        internal void SendRemote(RemoteCall call){if(!IsClient||server==null||!server.authenticated)return;call.sequence=++remoteSequence;Outgoing++;byte[] bytes=Encode(call);if(bytes.Length>65536){RemoteActions.Notice("~SMC201~");return;}server.Send(36,bytes);}
        /// <summary>Host side: a stable name for a connected friend (Steam account, or the typed name for direct connections).</summary>
        /// <summary>Runs a short on-screen effect (the friend-side replay of the shotgun shot).</summary>
        internal void PlayEffect(IEnumerator effect){if(effect!=null)StartCoroutine(effect);}
        internal void BroadcastAchievement(string text){byte[] bytes=Encoding.UTF8.GetBytes(text);if(bytes.Length>512)return;foreach(var p in peers.ToArray())if(p.authenticated&&p.modsOk){try{p.Send(Achievements.Message,bytes);}catch{}}}
        internal void SendTo(int owner,byte type,byte[] data){foreach(var p in peers.ToArray())if(p.id==owner&&p.authenticated&&p.modsOk){try{p.Send(type,data);}catch{}}}
        internal bool HasPeer(int id){foreach(var p in peers)if(p.id==id&&p.authenticated)return true;return false;}
        internal string NameOf(int id){foreach(var p in peers)if(p.id==id)return string.IsNullOrEmpty(p.name)?"~SMC102~"+id:p.name;return "~SMC102~"+id;}
        internal string PeerIdentity(int owner){foreach(var p in peers)if(p.id==owner&&p.authenticated){var steam=p.channel as SteamRelay.SteamChannel;return steam!=null?"S"+steam.Remote.ToString(CultureInfo.InvariantCulture):NameIdentity(p.name);}return null;}
        /// <summary>Friend side: the same name the host uses for this player.</summary>
        internal string MyIdentity{get{if(!IsClient||server==null)return null;if(server.channel is SteamRelay.SteamChannel){ulong id=SteamRelay.LocalId;return id!=0?"S"+id.ToString(CultureInfo.InvariantCulture):null;}return NameIdentity(PlayerNames.Clean(MyName));}}
        static string NameIdentity(string name){if(string.IsNullOrEmpty(name))return null;var b=new StringBuilder("N");foreach(byte x in Encoding.UTF8.GetBytes(name)){if(b.Length>=65)break;b.Append(x.ToString("X2",CultureInfo.InvariantCulture));}return b.ToString();}
        void BroadcastPhase(){
            if(!peers.Exists(p=>p.authenticated&&p.modsOk)||Time.realtimeSinceStartup<nextPhaseCheck)return;nextPhaseCheck=Time.realtimeSinceStartup+.25f;
            string now=WireJson.ToJson(HostPhase.Capture(Ready));if(now==lastPhase&&Time.realtimeSinceStartup<nextPhase)return;
            nextPhase=Time.realtimeSinceStartup+1f;lastPhase=now;byte[] bytes=Encoding.UTF8.GetBytes(now);if(bytes.Length>65536){Debug.LogWarning("[ShopTogether] phase too large: "+bytes.Length);return;}
            foreach(var p in peers.ToArray())if(p.authenticated&&p.modsOk){try{p.Send(34,bytes);}catch(Exception dropped){Leases.Disconnect(p.id);p.Dispose();peers.Remove(p);PlayerNames.Changed=true;Debug.Log("[ShopTogether] peer dropped while sending ("+PlayerNames.Of(p.id)+"): "+dropped.GetType().Name+" "+dropped.Message);}}
        }
        static byte[] Encode<T>(T data){byte[] bytes=Encoding.UTF8.GetBytes(WireJson.ToJson(data));if(bytes.Length>LargeTransfer.MaxPayload)throw new InvalidOperationException("~SMC075~");return bytes;}
        void Pump(Peer peer,bool host){
            if(!peer.authenticated&&Time.realtimeSinceStartup>peer.deadline)throw new Exception("~SMC140~");if(peer.channel==null||!peer.channel.Ready)return;
            if(!host&&!peer.versionSent){peer.versionSent=true;peer.Send(16,Encoding.UTF8.GetBytes(WireVersion));}
            peer.Write();if(peer.closing){if(peer.outgoing.Count==0&&!peer.channel.Writing){
                // Steam closes a session at once and may drop what was just sent (e.g. the list of mods to install): wait a moment first.
                if(peer.channel is SteamRelay.SteamChannel){if(peer.closeAt==0){peer.closeAt=Time.realtimeSinceStartup+3;return;}if(Time.realtimeSinceStartup<peer.closeAt)return;}
                throw new Exception("~SMC186~");}return;}
            // Friend: while updates are still waiting to be applied, leave the rest in the link instead of queueing more.
            if(!host&&pendingWorld.Count>=24){peer.partialSince=Time.realtimeSinceStartup;return;}
            // Read only while what was read is being handled: a busy frame leaves the rest in the link instead of overflowing.
            // (Both limits stay above the largest single message each side accepts, so a message is never left half-read.)
            for(int i=0;i<4&&peer.input.Count<(host?96*1024:2*1024*1024);i++){int count=peer.channel.Receive(peer.buffer);if(count<0)break;if(count==0)throw new Exception("~SMC156~");for(int j=0;j<count;j++)peer.input.Add(peer.buffer[j]);if(peer.input.Count>(host?131072:4*1024*1024+4))throw new Exception("~SMC154~");}
            var timer=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<32&&peer.input.Count>=5;i++){int length=(peer.input[0]<<24)|(peer.input[1]<<16)|(peer.input[2]<<8)|peer.input[3];byte type=peer.input[4];
                if(length<1||!ProtocolGuard.Valid(host,peer.authenticated,peer.matched,type,length-1))throw new Exception("~SMC029~");if(peer.input.Count<length+4)break;
                if(host&&!peer.guard.Admit(Time.realtimeSinceStartup))break;   // over the message rate: handle the rest a moment later
                byte[] data=new byte[length-1];peer.input.CopyTo(5,data,0,data.Length);peer.input.RemoveRange(0,length+4);Receive(peer,host,type,data);peer.partialSince=0;if(peer.closing||timer.ElapsedMilliseconds>=2)break;}
            // A big update over a slow upload takes a while: only a message that stops arriving altogether ends the link.
            if(peer.input.Count>0){if(peer.partialSince==0||peer.input.Count!=peer.partialSize){peer.partialSince=Time.realtimeSinceStartup;peer.partialSize=peer.input.Count;}else if(Time.realtimeSinceStartup-peer.partialSince>60)throw new Exception("~SMC153~");}else peer.partialSize=0;
        }
        void Receive(Peer peer,bool host,byte type,byte[] data){
            if(!peer.authenticated){
                if(type==17)throw new Exception("~SMC064~");
                if(type==16){if(Encoding.UTF8.GetString(data)!=WireVersion){if(host){peer.Send(17,new byte[0]);peer.closing=true;return;}throw new Exception("~SMC064~");}peer.matched=true;if(host)peer.Send(16,Encoding.UTF8.GetBytes(WireVersion));else peer.Send(1,Encoding.UTF8.GetBytes(secret));return;}
                if(host&&type==1){string given=Encoding.UTF8.GetString(data);var steamPeer=peer.channel as SteamRelay.SteamChannel;
                    // Steam friends who joined from the friends list: no password, but only accounts on the host's own friends list.
                    bool friendJoin=given==SteamInvite.FriendToken&&steamPeer!=null&&SteamInvite.IsFriend(steamPeer.Remote);
                    if(given!=secret&&!friendJoin){peer.Send(15,new byte[0]);peer.closing=true;return;}if(friendJoin)Debug.Log("[ShopTogether] Steam friend joined from the friends list");peer.authenticated=true;peer.Send(2,BitConverter.GetBytes(peer.id));Debug.Log("[ShopTogether] authenticated data client build="+BuildVersion);return;}
                if(!host&&type==15)throw new Exception(secret==SteamInvite.FriendToken?"~SMC086~":"~SMC088~");
                if(!host&&type==2){peer.authenticated=true;joinCandidates.Clear();PlayerId=BitConverter.ToInt32(data,0);peer.Send(30,Encode(new ModList{mods=ModParity.Local(),entries=ModRequirements.Local(),name=MyName}));return;}
                throw new Exception("~SMC073~");
            }
            if(host&&type==30){if(peer.modsOk)throw new Exception("~SMC115~");var list=WireJson.FromJson<ModList>(Encoding.UTF8.GetString(data));var report=ModRequirements.Diff(ModRequirements.Local(),list==null?null:list.entries);peer.name=PlayerNames.Clean(list==null?null:list.name);if(peer.name.Length==0)peer.name="~SMC102~"+peer.id;if(report!=null){string summary=ModRequirements.Summary(report);Debug.Log("[ShopTogether] refused peer: mod mismatch: "+summary);hostNotice="~SMC091~"+(summary.Length>300?summary.Substring(0,300)+"…":summary);if(hostReadyVisible)ShowHostReady();byte[] bytes=Encode(report);if(bytes.Length>32768)bytes=Encode(new ModReport{missing=new ModEntry[0],update=new ModEntry[0],extra=new ModEntry[0]});peer.Send(31,bytes);peer.closing=true;return;}peer.modsOk=true;PlayerNames.Changed=true;Debug.Log("[ShopTogether] "+peer.name+" joined");return;}
            if(!host&&type==31){modReport=ModRequirements.Clean(WireJson.FromJson<ModReport>(Encoding.UTF8.GetString(data)));throw new Exception("~SMC067~");}
            if(!host&&type==26){byte inner;var whole=incoming.Add(data,out inner);if(whole!=null)Receive(peer,false,inner,whole);return;}
            if(!host&&type==32){var digest=WireJson.FromJson<Digest>(Encoding.UTF8.GetString(data));Diagnostics.Soft("digest",()=>Drift.Offer(digest));return;}
            if(host&&type==33){if(!peer.modsOk||!peer.sceneReady)return;var resync=WireJson.FromJson<ResyncRequest>(Encoding.UTF8.GetString(data));if(resync==null||(resync.ids!=null&&resync.ids.Length>512))throw new Exception("~SMC054~");World.ForceResend(resync);nextSync=0;return;}
            if(host&&type==Diagnostics.Message){Diagnostics.FromFriend(string.IsNullOrEmpty(peer.name)?PlayerNames.Of(peer.id):peer.name,data);return;}
            if(host&&!peer.modsOk)throw new Exception("~SMC072~");
            if(host&&type==24){peer.sceneReady=true;needsFull=true;return;}
            if(host&&type==38){Cursors.FromFriend(peer.id,data);return;}
            if(host&&type==41){int code=data[0];if(Activity.Valid(code)&&code!=peer.activity){peer.activity=code;PlayerNames.Changed=true;}return;}
            if(!host&&type==39){Diagnostics.Soft("cursors",()=>Cursors.FromHost(data,PlayerId));return;}
            if(!host&&type==40){Diagnostics.Soft("names",()=>{var names=WireJson.FromJson<NameList>(Encoding.UTF8.GetString(data));PlayerNames.FromHost(names);Activity.FromHost(names);if(names!=null&&names.ids!=null)SteamInvite.Presence(false,false,SteamInvite.CleanGroup(names.group),names.ids.Length,PlayerNames.Of(0));});return;}
            if(host&&type==36){if(!peer.modsOk||!peer.sceneReady)return;var call=WireJson.FromJson<RemoteCall>(Encoding.UTF8.GetString(data));if(call==null||call.sequence<=peer.lastRemote)throw new Exception("~SMC169~");peer.lastRemote=call.sequence;string refused=Ready?RemoteActions.Execute(this,peer.id,call):"~SMC082~";if(refused!=null)peer.Send(37,Encoding.UTF8.GetBytes(refused));else{nextSync=0;string note=RemoteActions.TakeNote();if(note!=null)peer.Send(37,Encoding.UTF8.GetBytes(note));}return;}
            if(!host&&type==34){var phase=WireJson.FromJson<PhaseState>(Encoding.UTF8.GetString(data));bool wasAway=HostPhase.Away;bool changed=false;Diagnostics.Soft("host phase",()=>changed=HostPhase.Apply(phase));if(changed&&ReplicaSession.SceneReady)peer.Send(24,new byte[0]);if(HostPhase.Away&&!wasAway&&Ready)Diagnostics.Soft("release",ReleaseAll);return;}
            if(!host&&type==42){Diagnostics.Soft("achievement",()=>Achievements.FromHost(data));return;}
            if(!host&&type==PanelRelay.Message){Diagnostics.Soft("panel",()=>PanelRelay.FromHost(data));return;}
            if(!host&&type==37){Diagnostics.Soft("notice",()=>RemoteActions.Notice(Encoding.UTF8.GetString(data)));return;}
            if(host&&type==21){var request=WireJson.FromJson<Request>(Encoding.UTF8.GetString(data));if(request==null||request.sequence<=peer.lastSequence)throw new Exception("~SMC169~");peer.lastSequence=request.sequence;Process(peer.id,request,reply=>peer.Send(22,Encode(reply)));return;}
            if(!host&&type==20){var delta=WireJson.FromJson<WorldDelta>(Encoding.UTF8.GetString(data));if(delta==null||pendingWorld.Count>=512)throw new Exception("~SMC104~");pendingWorld.Enqueue(delta);return;}
            if(!host&&type==22){var reply=WireJson.FromJson<LeaseReply>(Encoding.UTF8.GetString(data));if(reply==null)throw new Exception("~SMC168~");Diagnostics.Soft("reply",()=>{OnLocalReply(reply);if(!reply.granted)NativeActions.CancelItems(reply.items);});return;}
            throw new Exception("~SMC047~");
        }
        internal void SendToHost(byte type,byte[] data){if(IsClient&&server!=null&&server.authenticated)server.Send(type,data);}
        // A completed search can add items or merge stacks without invoking an item-change callback.
        // Prioritize this one ground inventory on the next update rather than waiting for a full scan.
        internal void SyncGroundSoon(GameInventory ground){
            if(!IsHost||!Ready||ground==null)return;
            foreach(var item in ground.childItems)World.MarkDirty(item);
            nextSync=0;
        }
        internal void Send(Request request){if(request.kind!=RequestKind.Renew&&request.kind!=RequestKind.Release)Outgoing++;if(IsClient&&HostPhase.Away&&request.kind!=RequestKind.Release&&request.kind!=RequestKind.Renew){status=HostPhase.Text;if(request.items!=null)NativeActions.CancelItems(request.items);return;}request.sequence=++sequence;if(IsHost)Process(0,request,OnLocalReply);else if(IsClient&&server!=null&&server.authenticated)server.Send(21,Encode(request));}
        /// <summary>The other player currently holding any of these items, or -1.</summary>
        internal int BusyOwner(int[] ids){
            if(ids==null)return -1;double now=Time.realtimeSinceStartup;
            foreach(int id in ids){if(id<=0)continue;
                if(IsHost){int o=Leases.OwnerOf(0,id,now);if(o>=0)return o;}
                else foreach(var l in World.RemoteLeases)if(l.Value!=PlayerId&&(l.Key==id||World.Related(l.Key,id)))return l.Value;}
            return -1;
        }
        void OnLocalReply(LeaseReply reply){if(!reply.granted){int owner=BusyOwner(reply.items);ItemBusyHint.Show(owner);}if(reply.items!=null)foreach(int id in reply.items){requested.Remove(id);if(reply.granted)LocalLeases[id]=reply.token;else LocalLeases.Remove(id);}if(!reply.granted){status=reply.reason;if(!string.IsNullOrEmpty(reply.reason)&&BusyOwner(reply.items)<0)RemoteActions.Notice(reply.reason);}FlushWaiting();}   // a refused action says why (busy items already show who has them)
        internal void Claim(int[] ids){var fresh=new List<int>();foreach(int id in ids){deferredRelease.Remove(id);if(!LocalLeases.ContainsKey(id)&&requested.Add(id))fresh.Add(id);}for(int at=0;at<fresh.Count;at+=128)Send(new Request{kind=RequestKind.Claim,items=fresh.GetRange(at,Math.Min(128,fresh.Count-at)).ToArray()});}   // the host takes at most 128 per request
        internal void Submit(Request request){if(request.items==null||request.items.Length==0)return;Claim(request.items);waiting.Add(request);FlushWaiting();}
        void FlushWaiting(){while(waiting.Count>0){var request=waiting[0];bool pending=false,denied=false;foreach(int id in request.items){if(requested.Contains(id))pending=true;else if(!LocalLeases.ContainsKey(id))denied=true;}if(pending)break;waiting.RemoveAt(0);if(denied)continue;if(request.moves!=null)foreach(var m in request.moves)m.lease=LocalLeases[m.item];else request.lease=LocalLeases[request.items[0]];Send(request);foreach(int id in request.items)if(!NativeActions.Retains(id))deferredRelease.Add(id);}FlushReleases();}
        void FlushReleases(){var release=new List<int>();foreach(int id in deferredRelease){if(requested.Contains(id))continue;bool busy=false;foreach(var r in waiting)if(Array.IndexOf(r.items,id)>=0){busy=true;break;}if(!busy)release.Add(id);}if(release.Count==0)return;foreach(int id in release){deferredRelease.Remove(id);LocalLeases.Remove(id);}for(int at=0;at<release.Count;at+=128)Send(new Request{kind=RequestKind.Release,items=release.GetRange(at,Math.Min(128,release.Count-at)).ToArray()});}
        internal void Release(int[] ids){foreach(int id in ids)deferredRelease.Add(id);FlushReleases();}
        internal void ReleaseAll(){waiting.Clear();var ids=new HashSet<int>(LocalLeases.Keys);ids.UnionWith(requested);Release(new List<int>(ids).ToArray());NativeActions.CancelLocal();}
        internal void Process(int owner,Request request,Action<LeaseReply> reply){
            if(!IsHost||!Ready)return;World.Refresh();double now=Time.realtimeSinceStartup;
#if LAN_PROBE
            if(request.kind==RequestKind.Move||request.kind==RequestKind.AutoPlace){var move=request.moves!=null&&request.moves.Length>0?request.moves[0]:null;long v=0;if(move!=null)World.Versions.TryGetValue(move.item,out v);Debug.Log("[SMC REQUEST] kind="+request.kind+" owner="+owner+" currentVersion="+v+" owns="+(move!=null&&Leases.Owns(owner,move.item,move.lease,now))+" move="+WireJson.ToJson(move));}
#endif
            if(request.kind==RequestKind.Renew){Leases.Renew(owner,now);return;}
            if(request.items==null)throw new Exception("~SMC170~");
            // A bounded refusal must not echo an oversized list or turn a release into a disconnect.
            if(request.items.Length>128){reply(new LeaseReply{sequence=request.sequence,items=new int[0],reason="~SMC246~"});return;}
            if(request.kind==RequestKind.Release){Leases.Release(owner,request.items);return;}
            foreach(int id in request.items)if(!World.Items.ContainsKey(id)){reply(new LeaseReply{sequence=request.sequence,items=request.items,reason="~SMC095~"});return;}
            if(request.kind==RequestKind.Claim){long token;bool ok=Leases.Claim(owner,request.items,now,out token);reply(new LeaseReply{sequence=request.sequence,items=request.items,owner=owner,token=token,granted=ok,reason=ok?null:"~SMC096~"});return;}
            // A game error while running a friend's action refuses that action; it must not cut the friend off.
            bool success=false;string failed="~SMC167~";try{success=NativeActions.Execute(this,owner,request);if(success)World.Committed(request);}catch(Exception failure){success=false;failed="~SMC085~";Debug.LogWarning("[ShopTogether] action by "+PlayerNames.Of(owner)+" failed on host ("+request.kind+"): "+failure);}finally{if(!success)reply(new LeaseReply{sequence=request.sequence,items=request.items,reason=failed});nextSync=0;}
        }
        // The main-menu box always shows where things stand and leads back to the room: a room that is open stays
        // reachable from here (and from the button at the top right in the shop) instead of being created again.
        string launcherState="";
        string LauncherState(){return (IsHost?"H":IsClient?(Ready?"C":"c"):"-")+"|"+ConnectedNames;}
        void DrawLauncher(){
            launcherState=LauncherState();
            var w=CustomUIManager.Window(LauncherId,Loc.Text("~SMC057~"),CustomUIManager.LAYER_MENU).SetSize(340,IsHost||IsClient?240:150).SetPosition(new Vector2(-600,-300));
            if(IsHost){w.AddLabel(Loc.Text("~SMC237~"+(ConnectedNames.Length>0?"~SMC006~"+ConnectedNames:"~SMC012~"))).AddButton(Loc.Text("~SMC089~"),ShowHostReady).AddButton(Loc.Text("~SMC183~"),()=>{Stop();DrawLauncher();});}
            else if(IsClient){w.AddLabel(Loc.Text(Ready?"~SMC239~":"~SMC240~")).AddButton(Loc.Text("~SMC238~"),ShowStatus).AddButton(Loc.Text("~SMC181~"),()=>{Stop();DrawLauncher();});}
            else w.AddButton(Loc.Text("~SMC060~"),ShowChoice);
            w.Show();
        }
        // The name shown next to this player's mouse pointer on everyone's screen (Steam name by default).
        static string playerName="";
        internal static string MyName{get{if(playerName.Length==0){try{if(SteamRelay.Available)playerName=PlayerNames.Clean(Steamworks.SteamFriends.GetPersonaName());}catch{}if(playerName.Length==0)playerName="~SMC101~";}return playerName;}}
        void ShowChoice(){if(IsHost){ShowHostReady();return;}if(IsClient){ShowStatus();return;}CustomUIManager.Window(WindowId,Loc.Text("~SMC057~"),CustomUIManager.LAYER_MENU).SetSize(460,460).Center().AddLabel(Loc.Text("~SMC066~")).AddInput(Loc.Text("~SMC058~"),MyName,v=>playerName=PlayerNames.Clean(v),null,v=>playerName=PlayerNames.Clean(v)).AddButton(Loc.Text("~SMC023~"),QuickHost).AddButton(Loc.Text("~SMC107~"),LanHost).AddButton(Loc.Text("~SMC175~"),ShowJoin).AddButton(Loc.Text("~SMC062~"),ShowHost).AddCloseButton(null,null).Show();}
        void ShowHost(){if(secret.Length<8)secret=Password();CustomUIManager.Window(WindowId,Loc.Text("~SMC105~"),CustomUIManager.LAYER_MENU).SetSize(480,380).Center().AddLabel(Loc.Text("~SMC087~")).AddInput(Loc.Text("~SMC121~"),secret,v=>secret=v,null,v=>secret=v).AddLabel(Loc.Text("~SMC190~")).AddInput(Loc.Text("~SMC129~"),portText,v=>portText=v,null,v=>portText=v).AddLabel(Loc.Text("~SMC141~")).AddLabel(Loc.Text(status)).AddButton(Loc.Text("~SMC105~"),Host).AddButton(Loc.Text("~SMC103~"),ShowChoice).Show();}
        void ShowJoin(){CustomUIManager.Window(WindowId,Loc.Text("~SMC039~"),CustomUIManager.LAYER_MENU).SetSize(680,340).Center().AddLabel(Loc.Text("~SMC151~")).AddInput(Loc.Text("~SMC135~"),inviteText,v=>inviteText=v,null,v=>inviteText=v).AddLabel(Loc.Text(status)).AddButton(Loc.Text("~SMC038~"),JoinInvite).AddButton(Loc.Text("~SMC033~"),ShowManualJoin).AddButton(Loc.Text("~SMC103~"),ShowChoice).Show();}
        void ShowManualJoin(){CustomUIManager.Window(WindowId,Loc.Text("~SMC032~"),CustomUIManager.LAYER_MENU).SetSize(650,560).Center().AddLabel(Loc.Text("~SMC036~")).AddInput(Loc.Text("~SMC035~"),address,v=>address=v,null,v=>address=v).AddLabel(Loc.Text("~SMC129~")).AddInput(Loc.Text("~SMC129~"),portText,v=>portText=v,null,v=>portText=v).AddLabel(Loc.Text("~SMC037~")).AddInput(Loc.Text("~SMC037~"),hostKey??"",v=>hostKey=v,null,v=>hostKey=v).AddLabel(Loc.Text("~SMC087~")).AddInput(Loc.Text("~SMC121~"),secret,v=>secret=v,null,v=>secret=v).AddLabel(Loc.Text(status)).AddButton(Loc.Text("~SMC040~"),Join).AddButton(Loc.Text("~SMC103~"),ShowJoin).Show();}
        void ShowHostReady(){
            hostReadyVisible=true;var window=CustomUIManager.Window(WindowId,Loc.Text("~SMC059~"),MainMenuUIController.Instance!=null?CustomUIManager.LAYER_MENU:CustomUIManager.LAYER_OVERLAY).SetSize(720,660).Center();bool steam=!lanOnly&&SteamRelay.Available&&SteamRelay.LocalId!=0;
            if(steam){window.AddLabel(Loc.Text("~SMC045~")).AddButton(Loc.Text("~SMC162~"),()=>GUIUtility.systemCopyBuffer=externalInvite.Length>0?externalInvite:localInvite);
                window.AddLabel(Loc.Text("~SMC188~"));}
            window.AddLabel(Loc.Text("~SMC120~"+LocalAddress()+":"+portText));if(!steam)window.AddLabel(Loc.Text(mappingStatus==null?"~SMC049~":mappingStatus.Message));if(hostNotice.Length>0)window.AddLabel(Loc.Text(hostNotice));window.AddLabel(Loc.Text("~SMC065~"+MyName+(ConnectedNames.Length>0?"~SMC006~"+ConnectedNames:"~SMC012~")));
            if(steam){}else if(externalInvite.Length>0)window.AddLabel(Loc.Text("~SMC044~"+publicEndpoint)).AddButton(Loc.Text("~SMC160~"),()=>GUIUtility.systemCopyBuffer=externalInvite);
            else if(!lanOnly)window.AddButton(Loc.Text("~SMC070~"),()=>{if(publicAddress==null)publicAddress=new PublicAddress();});
            if(!steam)window.AddButton(Loc.Text("~SMC161~"),()=>GUIUtility.systemCopyBuffer=localInvite);window.AddLabel(Loc.Text("~SMC136~")).AddLabel(Loc.Text("~SMC113~"));
            if(MainMenuUIController.Instance!=null)window.AddButton(Loc.Text("~SMC106~"),()=>{hostReadyVisible=false;CustomUIManager.Instance.CloseWindow(WindowId);MainMenuUIController.Instance.OnNewGameClick();}).AddButton(Loc.Text("~SMC185~"),()=>{hostReadyVisible=false;CustomUIManager.Instance.CloseWindow(WindowId);MainMenuUIController.Instance.OnLoadGameClick();});
            window.AddButton(Loc.Text("~SMC184~"),()=>{hostReadyVisible=false;CustomUIManager.Instance.CloseWindow(WindowId);}).AddButton(Loc.Text("~SMC183~"),()=>{Stop();CustomUIManager.Instance.CloseWindow(WindowId);if(MainMenuUIController.Instance!=null)DrawLauncher();}).Show();
        }
        // Same steps as the game's own pause menu "Main Menu" button, without its save/analytics. A friend's copy of the
        // shop is never saved: the save barriers stay active until the menu scene is up.
        void ReturnToMenu(){
            if(returningToMenu||MainMenuUIController.Instance!=null)return;returningToMenu=true;
            bool was=WorldReplica.Applying;WorldReplica.Applying=true;
            try{if(NewGameData.Instance!=null)NewGameData.Instance.isLoadFromMainMenu=true;if(PlayerStore.Instance!=null)PlayerStore.Instance.Reset();}catch(Exception e){Debug.LogWarning("[ShopTogether] leave shop: "+e.Message);}finally{WorldReplica.Applying=was;}
            EmporiumEntry.ResetData();PlayerStore.ResetData();StoreStation.ResetData();
            UnityEngine.SceneManagement.SceneManager.LoadScene("EmporiumMenu",UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        // Like Accomplice: tell the friend exactly which mods to get, and open each one's Workshop page so they can
        // subscribe with one click. (The game does not let mods subscribe on their own; Steam downloads after the click.)
        void ShowModReport(){
            var r=modReport;var w=CustomUIManager.Window(WindowId,Loc.Text("~SMC159~"),MainMenuUIController.Instance!=null?CustomUIManager.LAYER_MENU:CustomUIManager.LAYER_OVERLAY).SetSize(820,760).Center();
            w.AddLabel(Loc.Text("~SMC110~"));
            int download=0;
            foreach(var e in r.missing){
                LoadedMod installed=null;foreach(var mod in ModLoader.Mods)if(mod!=null&&mod.Manifest!=null&&string.Equals(mod.Manifest.ID,e.id,StringComparison.OrdinalIgnoreCase)){installed=mod;break;}
                ulong id=ModRequirements.Workshop(e);
                if(installed!=null&&!installed.IsEnabled)w.AddLabel(Loc.Text("~SMC026~"+e.name+"~SMC002~"));
                else if(id!=0){download++;string name=e.name;w.AddButton(Loc.Text("~SMC114~"+name+"~SMC018~"+id+"）"),()=>ModWorkshop.OpenItemPage(id));}
                else w.AddLabel(Loc.Text("~SMC083~"+e.name+"~SMC019~"));
            }
            foreach(var e in r.update){
                string[] v=(e.version??"").Split('|');ulong id=ModRequirements.Workshop(e);
                string text="~SMC093~"+e.name+"~SMC020~"+(v.Length>0?v[0]:"?")+"~SMC014~"+(v.Length>1?v[1]:"?")+"）";
                if(id!=0){string name=e.name;w.AddButton(Loc.Text(text+"~SMC003~"),()=>ModWorkshop.OpenItemPage(id));}else w.AddLabel(Loc.Text(text));
            }
            if(r.update.Length>0)w.AddLabel(Loc.Text("~SMC094~"));
            if(r.extra.Length>0)w.AddLabel(Loc.Text("~SMC063~"));foreach(var e in r.extra)w.AddLabel(Loc.Text("・"+e.name));
            if(download>0)w.AddLabel(Loc.Text("~SMC109~"));
            w.AddLabel(Loc.Text("~SMC043~"));
            w.AddButton(Loc.Text("~SMC182~"),()=>{modReport=null;CustomUIManager.Instance.CloseWindow(WindowId);}).Show();
        }
        void ShowStatus(){
            if(modReport!=null&&CustomUIManager.Instance!=null){ShowModReport();return;}if(CustomUIManager.Instance==null)return;
            var w=CustomUIManager.Window(WindowId,Loc.Text("~SMC057~"),MainMenuUIController.Instance!=null?CustomUIManager.LAYER_MENU:CustomUIManager.LAYER_OVERLAY).SetSize(500,IsClient?280:220).Center();
            if(IsClient)w.AddLabel(Loc.Text(Ready?"~SMC239~":"~SMC240~"));
            if(status.Length>0&&!(IsClient&&Ready))w.AddLabel(Loc.Text(status));
            // Leaving is a deliberate click in this window, never a single misclick on the corner button.
            if(IsClient)w.AddButton(Loc.Text("~SMC181~"),()=>{CustomUIManager.Instance.CloseWindow(WindowId);bool inShop=MainMenuUIController.Instance==null;Stop();if(inShop)ReturnToMenu();else DrawLauncher();});
            w.AddButton(Loc.Text("~SMC182~"),()=>CustomUIManager.Instance.CloseWindow(WindowId)).Show();
        }
        float nextNames;
        void BroadcastNames(){
            if(!PlayerNames.Changed&&Time.realtimeSinceStartup<nextNames)return;PlayerNames.Changed=false;nextNames=Time.realtimeSinceStartup+5;
            var ids=new List<int>{0};var names=new List<string>{MyName};var doing=new List<int>{Activity.Current()};foreach(var p in peers)if(p.authenticated&&p.modsOk){ids.Add(p.id);names.Add(p.name);doing.Add(p.activity);}
            var list=new NameList{ids=ids.ToArray(),names=names.ToArray(),group=PresenceGroup,activities=doing.ToArray()};PlayerNames.FromHost(list);Activity.FromHost(list);SteamInvite.Presence(true,!lanOnly&&peers.Count<MaxFriends,PresenceGroup,ids.Count,MyName);byte[] bytes=Encode(list);
            foreach(var p in peers.ToArray())if(p.authenticated&&p.modsOk){try{p.Send(40,bytes);}catch(Exception dropped){Leases.Disconnect(p.id);p.Dispose();peers.Remove(p);PlayerNames.Changed=true;Debug.Log("[ShopTogether] peer dropped while sending ("+PlayerNames.Of(p.id)+"): "+dropped.GetType().Name+" "+dropped.Message);}}
        }
        // Players of one shop show up together in Steam friends lists.
        string PresenceGroup{get{ulong id=SteamRelay.LocalId;return "shop"+(id!=0?id.ToString():(identity!=null?identity.Fingerprint.Substring(0,16):"lan"));}}
        internal string ConnectedNames{get{var n=new List<string>();foreach(var p in peers)if(p.authenticated&&p.modsOk)n.Add(p.name);return string.Join("、",n.ToArray());}}
        internal void SendActivity(int code){if(IsClient&&server!=null&&server.authenticated&&Ready)server.Send(41,new[]{(byte)code});}
        internal void SendCursor(byte[] data){if(IsClient&&server!=null&&server.authenticated&&Ready)server.Send(38,data);}
        internal void BroadcastCursors(byte[] data){foreach(var p in peers.ToArray())if(p.authenticated&&p.modsOk&&p.sceneReady){try{p.Send(39,data);}catch(Exception dropped){Leases.Disconnect(p.id);p.Dispose();peers.Remove(p);PlayerNames.Changed=true;Debug.Log("[ShopTogether] peer dropped while sending ("+PlayerNames.Of(p.id)+"): "+dropped.GetType().Name+" "+dropped.Message);}}}
        internal bool ShowCursors{get{return Ready&&(IsHost||IsClient)&&EmporiumEntry.Instance!=null;}}
        void OnGUI(){Cursors.Draw(this);if(ShowCursors)Activity.Draw(IsHost?0:PlayerId);if(IsHost&&GUI.Button(new Rect(Screen.width-170,10,160,32),Loc.Text("~SMC089~")))ShowHostReady();if(IsClient&&GUI.Button(new Rect(Screen.width-170,10,160,32),Loc.Text("~SMC238~")))ShowStatus();
            if(IsClient&&Ready&&HostPhase.Away){GUI.Box(new Rect(Screen.width/2-220,Screen.height/2-40,440,80),Loc.Text(""));GUI.Label(new Rect(Screen.width/2-200,Screen.height/2-25,400,50),Loc.Text(HostPhase.Text));}
        }
    }
}
