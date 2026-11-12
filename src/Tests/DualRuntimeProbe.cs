#if LAN_PROBE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    public sealed partial class Connection {
        internal RouterMapping ProbeRouter {get{return routerMapping;}}
        internal string ProbeOpen(){QuickHost();if(string.IsNullOrEmpty(localInvite))throw new InvalidOperationException(status);if(listener==null||!IPAddress.IsLoopback(((IPEndPoint)listener.LocalEndPoint).Address))throw new InvalidOperationException("Test host must listen only on loopback");Debug.Log("[SMC PASS] test host listens only on loopback");CustomUIManager.Instance.CloseWindow(WindowId);hostReadyVisible=false;return localInvite;}
        internal void ProbeJoin(string code){inviteText=code;JoinInvite();CustomUIManager.Instance.CloseWindow(WindowId);}
        internal string ProbeMapping {get{return mappingStatus==null?"pending":mappingStatus.Message;}}
        internal bool ProbePending(int id){return requested.Contains(id);}
        sealed class ProbeRateChannel : IChannel {
            readonly IChannel inner; readonly int rate; byte[] pending; int offset; double credit,last;
            internal long SentBytes; internal readonly double Started;
            internal ProbeRateChannel(IChannel channel,int bytesPerSecond){inner=channel;rate=bytesPerSecond;last=Started=Time.realtimeSinceStartup;}
            public bool Ready {get{return inner.Ready;}}
            public bool Writing {get{
                if(inner.Writing)return true;
                if(pending==null)return false;
                double now=Time.realtimeSinceStartup;credit=Math.Min(rate*.05,credit+Math.Max(0,now-last)*rate);last=now;
                int length=Math.Min((int)credit,Math.Min(8192,pending.Length-offset));
                if(length>0){var chunk=new byte[length];Buffer.BlockCopy(pending,offset,chunk,0,length);inner.Send(chunk);offset+=length;credit-=length;SentBytes+=length;if(offset==pending.Length){pending=null;offset=0;}}
                return pending!=null||inner.Writing;
            }}
            public void Send(byte[] packet){if(pending!=null)throw new InvalidOperationException("Throttle already has a packet");pending=packet;}
            public int Receive(byte[] destination){return inner.Receive(destination);}
            public void Dispose(){inner.Dispose();}
        }
        ProbeRateChannel probeRate;
        internal void ProbeLimitUpload(int bytesPerSecond){var peer=peers.Find(p=>p.authenticated&&p.modsOk);if(peer==null||!(peer.channel is SecureChannel))throw new InvalidOperationException("Expected an authenticated direct test connection");probeRate=new ProbeRateChannel(peer.channel,bytesPerSecond);peer.channel=probeRate;}
        internal long ProbeRateBytes {get{return probeRate==null?0:probeRate.SentBytes;}}
        internal double ProbeRateElapsed {get{return probeRate==null?0:Time.realtimeSinceStartup-probeRate.Started;}}
        internal bool ProbeAwaitingFull {get{return awaitingFull;}}
        internal int ProbeApplyFailures {get{return applyFailures;}}
        sealed class ProbeBackpressureChannel : IChannel {
            internal bool Accept; internal readonly List<byte> Received=new List<byte>(); readonly MessageStream stream;
            internal ProbeBackpressureChannel(){stream=new MessageStream((data,length)=>{if(!Accept)return false;for(int i=0;i<length;i++)Received.Add(data[i]);return true;});}
            internal bool Pending {get{return stream.Pending;}}
            public bool Ready {get{return true;}}
            public bool Writing {get{stream.Flush();return stream.Pending;}}
            public void Send(byte[] packet){stream.Send(packet);}
            public int Receive(byte[] destination){return -1;}
            public void Dispose(){stream.Close();}
        }
        internal static void ProbeBackpressure(){
            var channel=new ProbeBackpressureChannel();var peer=new Peer{channel=channel};peer.Send(20,new byte[]{7});peer.Write();
            if(peer.outgoing.Count!=0||!channel.Pending)throw new InvalidOperationException("Steam backpressure fixture was not retained");
            channel.Accept=true;peer.Write();if(channel.Pending||channel.Received.Count!=6||channel.Received[5]!=7)throw new InvalidOperationException("Steam pending packet was not retried with an empty peer queue");peer.Dispose();
            channel=new ProbeBackpressureChannel{Accept=true};peer=new Peer{channel=channel};for(int i=0;i<100;i++)peer.Send(20,new byte[]{(byte)i});peer.Write();
            if(peer.outgoing.Count!=36||channel.Received.Count!=64*6)throw new InvalidOperationException("Peer write pacing is not limited to 64 packets");peer.Dispose();
            channel=new ProbeBackpressureChannel();peer=new Peer{channel=channel};for(int i=0;i<32;i++)peer.Send(20,new byte[0]);peer.Send(39,new byte[0]);peer.Send(34,new byte[0]);peer.Send(40,new byte[0]);
            if(peer.outgoing.Count!=32)throw new InvalidOperationException("Replaceable packets accumulated during congestion");peer.queuedBytes=256L*1024*1024-4;bool blocked=false;try{peer.Send(20,new byte[0]);}catch(Exception){blocked=true;}
            if(!blocked)throw new InvalidOperationException("Peer byte limit omitted framing");peer.Dispose();Debug.Log("[SMC PASS] Steam backpressure drains even after the peer queue empties; pacing and bounds hold");
        }
    }
    internal static partial class RuntimeProbe {
        static UdpClient control;static IPEndPoint other;
        internal static readonly List<double> CaptureMilliseconds=new List<double>();
        static Camera uiCamera;static RenderTexture uiTarget;
        static void CaptureFailure(){if(uiTarget==null)return;var old=RenderTexture.active;Texture2D image=null;try{RenderUI();RenderTexture.active=uiTarget;image=new Texture2D(uiTarget.width,uiTarget.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,uiTarget.width,uiTarget.height),0,0);image.Apply();Debug.Log("[SMC-TEST-IMAGE] "+Convert.ToBase64String(ImageConversion.EncodeToPNG(image)));}catch(Exception error){Debug.Log("[SMC IMAGE ERROR] "+error.GetType().Name);}finally{RenderTexture.active=old;if(image!=null)UnityEngine.Object.Destroy(image);}}
        static void RenderUI(){
            if(uiCamera==null){uiCamera=new GameObject("SMC isolated UI camera").AddComponent<Camera>();uiCamera.enabled=false;uiCamera.orthographic=true;uiCamera.orthographicSize=Screen.height*.5f;uiCamera.transform.position=new Vector3(Screen.width*.5f,Screen.height*.5f,-1000);uiCamera.nearClipPlane=.1f;uiCamera.farClipPlane=2000;uiTarget=new RenderTexture(Screen.width,Screen.height,24);uiCamera.targetTexture=uiTarget;
                foreach(var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())if(canvas.rootCanvas==canvas){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=uiCamera;canvas.planeDistance=1000;}}
            Canvas.ForceUpdateCanvases();uiCamera.Render();
        }
        static readonly Queue<string> controlMessages=new Queue<string>();
        static Vector3 pointer;static bool fakeInput;static readonly HashSet<KeyCode> pressed=new HashSet<KeyCode>();
        static string Argument(string key){string[] args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]==key)return args[i+1];return null;}
        static void CloseControl(){if(control!=null){control.Close();control=null;}fakeInput=false;LocalControl.ProbeInput=false;pressed.Clear();if(uiCamera!=null)UnityEngine.Object.Destroy(uiCamera.gameObject);if(uiTarget!=null){uiTarget.Release();UnityEngine.Object.Destroy(uiTarget);}}
        static void Poll(){while(control!=null&&control.Available>0){IPEndPoint source=new IPEndPoint(IPAddress.Any,0);byte[] data=control.Receive(ref source);if(!IPAddress.IsLoopback(source.Address)||data.Length>1024)continue;other=source;controlMessages.Enqueue(Encoding.ASCII.GetString(data));}}
        static bool Received(string message){Poll();int count=controlMessages.Count;for(int i=0;i<count;i++){string next=controlMessages.Dequeue();if(next==message)return true;controlMessages.Enqueue(next);}return false;}
        static void Signal(string message){byte[] bytes=Encoding.ASCII.GetBytes(message);control.Send(bytes,bytes.Length,other);}
        static IEnumerator Wait(Func<bool> predicate,string label,float duration=40){float deadline=Time.realtimeSinceStartup+duration;bool done=false;while(!(done=predicate())&&Time.realtimeSinceStartup<deadline)yield return null;Require(done,label);}
        static void InstallInput(){
            var h=new Harmony("local.lan_shared_shop.test-input");
            h.Patch(AccessTools.PropertyGetter(typeof(Input),"mousePosition"),new HarmonyMethod(typeof(RuntimeProbe).GetMethod("MousePosition",BindingFlags.Static|BindingFlags.NonPublic)));
            var key=new HarmonyMethod(typeof(RuntimeProbe).GetMethod("Key",BindingFlags.Static|BindingFlags.NonPublic));
            h.Patch(AccessTools.Method(typeof(Input),"GetKey",new[]{typeof(KeyCode)}),key);
            h.Patch(AccessTools.Method(typeof(Input),"GetKeyDown",new[]{typeof(KeyCode)}),key);
            h.Patch(AccessTools.Method(typeof(Input),"GetKeyUp",new[]{typeof(KeyCode)}),key);
            h.Patch(AccessTools.Method(typeof(Input),"GetMouseButton",new[]{typeof(int)}),transpiler:new HarmonyMethod(typeof(RuntimeProbe).GetMethod("MouseBody",BindingFlags.Static|BindingFlags.NonPublic)));
            var noEdge=new HarmonyMethod(typeof(RuntimeProbe).GetMethod("NoMouseEdge",BindingFlags.Static|BindingFlags.NonPublic));
            h.Patch(AccessTools.Method(typeof(Input),"GetMouseButtonDown",new[]{typeof(int)}),transpiler:noEdge);
            h.Patch(AccessTools.Method(typeof(Input),"GetMouseButtonUp",new[]{typeof(int)}),transpiler:noEdge);
            fakeInput=true;LocalControl.ProbeInput=true;
        }
        static bool MousePosition(ref Vector3 __result){if(!fakeInput)return true;__result=pointer;return false;}
        static bool Key(KeyCode __0,MethodBase __originalMethod,ref bool __result){if(!fakeInput)return true;__result=__originalMethod.Name=="GetKey"&&pressed.Contains(__0);return false;}
        static bool ReadMouseButton(int button){return fakeInput&&pressed.Contains((KeyCode)((int)KeyCode.Mouse0+button));}
        static IEnumerable<CodeInstruction> MouseBody(IEnumerable<CodeInstruction> instructions){yield return new CodeInstruction(OpCodes.Ldarg_0);yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(RuntimeProbe),"ReadMouseButton"));yield return new CodeInstruction(OpCodes.Ret);}
        static IEnumerable<CodeInstruction> NoMouseEdge(IEnumerable<CodeInstruction> instructions){yield return new CodeInstruction(OpCodes.Ldarg_0);yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(RuntimeProbe),"ReadMouseEdge"));yield return new CodeInstruction(OpCodes.Ret);}
        static Vector2 Point(GameItem item){
            var element=item as GameItemElement;if(element==null)throw new Exception("Test item has no native element");RenderUI();
            string detail="";foreach(var graphic in element.handler.GetComponentsInChildren<UnityEngine.UI.Image>()){if(!graphic.raycastTarget||!graphic.gameObject.activeInHierarchy)continue;var canvas=graphic.canvas;var rect=graphic.rectTransform.rect;for(int x=1;x<=5;x++)for(int y=1;y<=5;y++){Vector2 position=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,graphic.rectTransform.TransformPoint(new Vector3(rect.xMin+rect.width*x/6,rect.yMin+rect.height*y/6,0)));if(RenderHandler.RaycastElement<GameItem>(position,null)==item){pointer=position;AccessTools.Method(typeof(StoreUIManager),"CheckRaycast").Invoke(StoreUIManager.Instance,null);if(!StoreUIManager.Instance.hoveringStoreUI)return position;}if(x==3&&y==3){var hits=new List<UnityEngine.EventSystems.RaycastResult>();UnityEngine.EventSystems.EventSystem.current.RaycastAll(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){position=position},hits);detail+=" @"+position+" enabled="+graphic.enabled+" count="+item.unitCount+" sprite="+(graphic.sprite==null?"null":graphic.sprite.name)+" depth="+graphic.depth+" cull="+graphic.canvasRenderer.cull+" hit="+graphic.Raycast(position,canvas.worldCamera)+" hits="+hits.Count;foreach(var hit in hits){var node=hit.gameObject.GetComponent<TreeNodeRender>();detail+=" "+hit.gameObject.name+":"+(node==null?"none":Convert.ToString(node.FindParentElement())+"/"+(node.FindParentElement() is GameItem?((GameItem)node.FindParentElement()).name:""));}}}}
            for(var tr=element.handler.transform;tr!=null;tr=tr.parent)detail+=" parent="+tr.name+":"+tr.gameObject.activeSelf; foreach(var im in element.handler.GetComponentsInChildren<UnityEngine.UI.Image>(true))detail+=" image="+im.name+":"+im.raycastTarget+":"+im.gameObject.activeInHierarchy; throw new Exception("Test item has no raycastable native location: "+item.name+detail);
        }
        static void Context(GameItem item){pointer=Point(item);var key=KeyBindHelper.Get("secondary_click");pressed.Add(key);ItemContextHandler.current.UpdatePress(new HashSet<KeyCode>{key});if(!MenuOpen)throw new Exception("Context press failed: enabled="+ItemContextHandler.current.enabled+" initialized="+ModReflect.Get<bool>(ItemContextHandler.current,"initialized")+" key="+key+" focus="+LocalControl.Focused+" equipped="+ItemSelectHandler.current.IsEquipped+" blocked="+ItemPinHelper.BlocksClicks+" dragging="+ItemMouseDragHandler.current.IsDraggingItem);pressed.Remove(key);ItemContextHandler.current.UpdateRelease(new HashSet<KeyCode>{key});if(MenuOpen){var menu=ModReflect.Get<PixelWindow>(ItemContextHandler.current,"contextWindow");menu.Center();RenderUI();var overlay=ModReflect.Get<Canvas>(ItemContextHandler.current,"overlayCanvas");bool found=false;foreach(var graphic in menu.handler.GetComponentsInChildren<UnityEngine.UI.Graphic>()){if(!graphic.raycastTarget||!graphic.gameObject.activeInHierarchy)continue;var canvas=graphic.canvas;var pos=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,graphic.rectTransform.TransformPoint(graphic.rectTransform.rect.center));if(ItemContextHandler.current.FindContextMenuNode(pos,overlay)!=null){pointer=pos;found=true;break;}}Require(found,"test cursor rests inside the context menu");}}
        static bool MenuOpen {get{var window=ModReflect.Get<PixelWindow>(ItemContextHandler.current,"contextWindow");return window!=null&&window.handler.gameObject.activeInHierarchy;}}
        static void BeginDrag(GameItem item){pointer=Point(item);AccessTools.Method(typeof(StoreUIManager),"CheckRaycast").Invoke(StoreUIManager.Instance,null);pressed.Add(KeyCode.Mouse0);ItemMouseDragHandler.current.UpdatePress(new HashSet<KeyCode>{KeyCode.Mouse0});Vector3 old=pointer;pointer+=new Vector3(30,20,0);ItemMouseDragHandler.current.UpdateMouseMove(pointer,pointer-old);ItemMouseDragHandler.current.UpdateUpdate();}
        static void Drop(GameInventory destination,GameItem item){
            SlotMarker slot=destination.TryFindOneValidInventorySlot(item);if(slot==null)throw new Exception("No test destination slot");
            EmporiumEntry.Instance.ShowFrontCounters();EmporiumEntry.Instance.invWindow.Show();EmporiumEntry.Instance.Validate(true);Canvas.ForceUpdateCanvases();
            var grid=(GameGridInventory)destination;var preview=((GameItemElement)item).tempShape.Build();float size=UISettings.current.gridSize;SlotMarker actual=null;
            bool mustMove=Argument("--smc-group")!=null;int oldX=item.modifiedShape.minX,oldY=item.modifiedShape.minY;
            for(int y=0;y<=grid.inventoryShape.height-preview.height && actual==null;y++)for(int col=0;col<=grid.inventoryShape.width-preview.width;col++){
                int maxX=grid.inventoryShape.width-preview.width;
                // Wrap each group player's starting column so later players still search the whole grid.
                int x=Argument("--smc-group")!=null?maxX-(int.Parse(Argument("--smc-index"))*(preview.width+1)+col)%(maxX+1):item.name=="SMC_CLIENT_ITEM"?maxX-col:col;
                var shape=new GridShapeBuilder(preview).SetPosition(x,y).Build();
                Vector3 target=grid.rectTransform.TransformPoint(new Vector3((shape.minX+(shape.maxX-shape.minX+1)*.5f)*size,-(shape.minY+(shape.maxY-shape.minY+1)*.5f)*size,0));
                Vector3 old=pointer;pointer+=target-(Vector3)((GameItemElement)item).GetFakeRotatorCenteredPosition();ItemMouseDragHandler.current.UpdateMouseMove(pointer,pointer-old);ItemMouseDragHandler.current.UpdateUpdate();
                var hit=ModReflect.Get<SlotMarker>(ItemMouseDragHandler.current,"lastSlot");if(hit!=null&&hit.inventory==destination&&(!mustMove||hit.itemGridShape!=null&&(hit.itemGridShape.minX!=oldX||hit.itemGridShape.minY!=oldY)&&grid.CheckShapePlacement(item,hit.itemGridShape,new List<GameItem>{item}))){actual=hit;break;}
            }
            Require(actual!=null,"native cursor resolved the drop destination");
            if(mustMove)Require(actual.itemGridShape.minX!=oldX||actual.itemGridShape.minY!=oldY,"group drop selects a different cell ("+oldX+","+oldY+" -> "+actual.itemGridShape.minX+","+actual.itemGridShape.minY+")");
            pressed.Remove(KeyCode.Mouse0);ItemMouseDragHandler.current.UpdateRelease(new HashSet<KeyCode>{KeyCode.Mouse0});
        }
        static GameItem Find(Connection session,string name){foreach(var item in session.World.Items.Values)if(item.name==name)return item;return null;}
        static void ShowStorage(){var hints=ModReflect.Get<GameObject>(StoreUIManager.Instance,"controlHintOverlay");if(hints!=null&&hints.activeSelf)StoreUIManager.Instance.ToggleControlHintOverlay();StoreUIManager.Instance.CloseAllUI();StoreUIManager.Instance.DisableBackingBlock();EmporiumEntry.Instance.ShowFrontCounters();EmporiumEntry.Instance.invWindow.Show();EmporiumEntry.Instance.Validate(true);}
        // TLS with the game's own runtime: pinned certificate carries data both ways; a wrong fingerprint is refused.
        static int ChannelState(SecureChannel channel){try{return channel.Ready?1:0;}catch(System.Security.Authentication.AuthenticationException){return -1;}catch(IOException){return -1;}}
        static IEnumerator TlsCheck(){
            var identity=new RoomIdentity();
            try{
                Require(identity.Certificate.HasPrivateKey&&identity.Fingerprint.Length==64,"ephemeral room identity");
                foreach(bool wrong in new[]{false,true}){
                    var listener=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);var clientSocket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);SecureChannel server=null,client=null;
                    try{
                        listener.Bind(new IPEndPoint(IPAddress.Loopback,0));listener.Listen(1);clientSocket.Connect(listener.LocalEndPoint);
                        server=new SecureChannel(listener.Accept(),identity.Certificate,null);client=new SecureChannel(clientSocket,null,wrong?new string('0',64):identity.Fingerprint);
                        float deadline=Time.realtimeSinceStartup+10;int c=0,s=0;
                        while(Time.realtimeSinceStartup<deadline){s=ChannelState(server);c=ChannelState(client);if(c<0||(c>0&&s>0))break;yield return null;}
                        if(wrong){Require(c<0,"wrong host fingerprint rejected before application data");continue;}
                        Require(c>0&&s>0,"pinned TLS handshake inside the game runtime");
                        byte[] buffer=new byte[32768];client.Send(new byte[]{1,2,3,4,5});int count=-1;deadline=Time.realtimeSinceStartup+10;
                        while((count=server.Receive(buffer))<0&&Time.realtimeSinceStartup<deadline)yield return null;
                        Require(count==5&&buffer[0]==1&&buffer[4]==5,"pinned TLS carries operation requests");
                        server.Send(new byte[]{42});deadline=Time.realtimeSinceStartup+10;
                        while((count=client.Receive(buffer))<0&&Time.realtimeSinceStartup<deadline)yield return null;
                        Require(count==1&&buffer[0]==42,"encrypted bidirectional transfer");
                    }finally{if(client!=null)client.Dispose();if(server!=null)server.Dispose();clientSocket.Close();listener.Close();}
                }
            }finally{identity.Dispose();}
        }
        static IEnumerator RouterCheck(){
            var route=new GatewayRoute{Local=IPAddress.Loopback,Gateway=IPAddress.Loopback,Mask=IPAddress.Parse("255.0.0.0")};
            const string wan="urn:schemas-upnp-org:service:WANIPConnection:1";
            using(var router=new FakeRouter()){
                router.Existing(7777,"unrelated-application");
                var lease=new UpnpLease(route,7777);var run=lease.ProbeTry(router.Control,wan,()=>false);while(run.MoveNext())yield return run.Current;
                Require(lease.Granted&&lease.External.Port!=7777&&lease.Lifetime==3600&&router.Description(7777)=="unrelated-application","UPnP selects another external port and preserves pre-existing mapping");
                var renew=lease.Renew(()=>false);while(renew.MoveNext())yield return renew.Current;Require(lease.Granted&&router.AddCount==2,"owned UPnP temporary lease renews");
                lease.Remove();var wait=Wait(()=>router.Count==1,"room cleanup deletes its own mapping",10);while(wait.MoveNext())yield return wait.Current;
                Require(router.Description(7777)=="unrelated-application","room cleanup leaves other mappings untouched");
            }
            using(var router=new FakeRouter()){
                router.Permanent=true;var lease=new UpnpLease(route,7777);var run=lease.ProbeTry(router.Control,wan,()=>false);while(run.MoveNext())yield return run.Current;
                Require(!lease.Granted,"router silently making a permanent rule is rejected");
                var wait=Wait(()=>router.Count==0,"permanent rule cleaned up",10);while(wait.MoveNext())yield return wait.Current;
            }
            using(var router=new FakeRouter()){
                var lease=new UpnpLease(route,7777);var run=lease.ProbeTry(router.Control,wan,()=>false);while(run.MoveNext())yield return run.Current;
                Require(lease.Granted,"third lease granted");int port=lease.ProbeExternalPort;router.Existing(port,"new-owner");lease.Remove();
                float until=Time.realtimeSinceStartup+2;while(Time.realtimeSinceStartup<until)yield return null;
                Require(router.Description(port)=="new-owner","cleanup does not delete a subsequently replaced mapping");
            }
            Debug.Log("[SMC PASS] router: UPnP leases are temporary, renewed, and only our own mapping is ever removed (no background threads)");
        }
        static IEnumerator HostCheck(Connection root){
            var wait=Wait(()=>MainMenuUIController.Instance!=null,"host main menu",90);while(wait.MoveNext())yield return wait.Current;
            Require(!Screen.fullScreen,"host runs in windowed mode");
            if(Argument("--smc-suite")==null)Connection.ProbeBackpressure();
            if(Argument("--smc-suite")=="ui1319"){var menu=MainMenu1319Host(root);while(menu.MoveNext())yield return menu.Current;}
            if(Argument("--smc-suite")==null){var routerCheck=RouterCheck();while(routerCheck.MoveNext())yield return routerCheck.Current;}
            if(Argument("--smc-suite")==null){var tlsCheck=TlsCheck();while(tlsCheck.MoveNext())yield return tlsCheck.Current;}
            NewGameData.Instance.skipIntro=true;NewGameData.Instance.disableTutorialHint=true;MainMenuUIController.Instance.StartNewGameClick();
            wait=Wait(()=>EmporiumEntry.Instance!=null&&ItemMouseDragHandler.current!=null,"host isolated shop",90);while(wait.MoveNext())yield return wait.Current;yield return new WaitForSecondsRealtime(2);
            InstallInput();var a=ModHelper.SpawnItem("magnifier");var b=ModHelper.SpawnItem("magnifier");a.mayStackItemFunc=(x,y)=>false;b.mayStackItemFunc=(x,y)=>false;a.name="SMC_HOST_ITEM";b.name="SMC_CLIENT_ITEM";b.ModifyTag("FEED_DISPENSER_TARGET_PERCENT_INT",t=>t.Enable().SetInt(85));GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement,a,1);GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement,b,1);foreach(var live in EmporiumEntry.Instance.invElement.childItems){if(live.name=="SMC_HOST_ITEM")a=live;if(live.name=="SMC_CLIENT_ITEM")b=live;}Require(a.unitCount>0&&b.unitCount>0,"test fixtures retain live native items");ShowStorage();
            wait=Wait(()=>!StoreUIManager.Instance.blackscreen.gameObject.activeInHierarchy,"initial shop transition finished",30);while(wait.MoveNext())yield return wait.Current;ShowStorage();yield return null;pointer=Point(a);yield return null;yield return null;Context(a);Require(MenuOpen,"native context preflight");ItemContextHandler.current.UpdateReset();int ticks=0;a.onOneSecondItemFunc=(x,y,z)=>{ticks++;x.unitValue=UnityEngine.Random.Range(100000,200000);};
            control=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));string code=root.ProbeOpen();root.World.Index();
            {Invite invite;string inviteError;Require(InviteCode.TryDecode(code,out invite,out inviteError),"invite decodes");
             if(SteamRelay.Available&&SteamRelay.LocalId!=0){Require(invite.SteamId==SteamRelay.LocalId,"invite carries this host's Steam account for friends outside the network");ulong parsed;Require(SteamInvite.TryParse("+shoptogether "+SteamRelay.LocalId,out parsed)&&parsed==SteamRelay.LocalId&&!SteamInvite.TryParse("+connect 1.2.3.4:7777",out parsed)&&!SteamInvite.TryParse("+shoptogether 1",out parsed),"Steam friends-list join string: only a person's Steam account is accepted");bool self=false;try{SteamRelay.Connect(SteamRelay.LocalId);}catch(InvalidOperationException){self=true;}Require(self,"Steam refuses joining your own account");}
             else Debug.Log("[SMC NOTE] Steam not running in this test; Steam relay checks skipped");}
            if(SteamRelay.Available&&SteamRelay.LocalId!=0){PlayerNames.Changed=true;wait=Wait(()=>SteamInvite.Advertising,"a Steam room is joinable by Steam friends without a separate invite step",10);while(wait.MoveNext())yield return wait.Current;}
            Debug.Log("[SMC-INTERNAL-INVITE] "+code);Debug.Log("[SMC CONTROL] "+((IPEndPoint)control.Client.LocalEndPoint).Port);
            wait=Wait(()=>Received("JOINED"),"real client authenticated and applied host state",100);while(wait.MoveNext())yield return wait.Current;
            string limit=Argument("--smc-throttle");if(!string.IsNullOrEmpty(limit)){int kbps;if(!int.TryParse(limit,out kbps)||kbps<1)throw new InvalidOperationException("Invalid test rate");root.ProbeLimitUpload(kbps*125);Debug.Log("[SMC PASS] direct host upload is limited to "+kbps+" kbit/s for the outing test");}
            if(Argument("--smc-suite")!=null){var suite=RunExtended(root,true);while(suite.MoveNext())yield return suite.Current;Signal("COMPLETE");Debug.Log("[SMC COMPLETE] host extended "+Argument("--smc-suite"));yield break;}ShowStorage();pointer=Point(a);yield return null;yield return null;Context(a);Require(MenuOpen,"host native context menu open");Signal("OPEN_MENU");
            wait=Wait(()=>Received("CLIENT_MENU"),"client native context menu open",30);while(wait.MoveNext())yield return wait.Current;Require(MenuOpen,"host menu stays open while client opens another");Signal("CLOSE_MENU");ItemContextHandler.current.UpdateReset();
            wait=Wait(()=>Received("MENU_CLOSED"),"client closed its menu");while(wait.MoveNext())yield return wait.Current;
            BeginDrag(a);Require(ItemMouseDragHandler.current.IsDraggingItem,"host native drag started");Signal("DRAG_OTHER");
            wait=Wait(()=>Received("CLIENT_DRAG"),"client drag overlaps host drag");while(wait.MoveNext())yield return wait.Current;Require(ItemMouseDragHandler.current.IsDraggingItem,"host drag survives client drag");
            Signal("DROP");Drop(EmporiumEntry.Instance.backInvinvElement,a);
            wait=Wait(()=>a.parentInventory==EmporiumEntry.Instance.backInvinvElement&&b.parentInventory==EmporiumEntry.Instance.backInvinvElement,"both drops committed by host",30);while(wait.MoveNext())yield return wait.Current;
            Signal("HOST_DROPPED");wait=Wait(()=>Received("REPLICA_MATCH"),"client sees both committed item positions");while(wait.MoveNext())yield return wait.Current;
            wait=Wait(()=>Received("DRIFT_FIXED"),"client drift was detected and corrected from host state",60);while(wait.MoveNext())yield return wait.Current;
            wait=Wait(()=>Received("REMOTE_SENT"),"client forwarded a rename",60);while(wait.MoveNext())yield return wait.Current;
            wait=Wait(()=>GeneralHelper.GetCustomName(b)=="SMC_RENAMED","host executed the friend's rename with its own game code",30);while(wait.MoveNext())yield return wait.Current;
            Require(RemoteActions.Forwarded>=1,"host counted the forwarded action");Debug.Log("[SMC PASS] remote action: friend's rename ran on host");
            wait=Wait(()=>Cursors.Seen(1),"host sees the friend's mouse pointer",20);while(wait.MoveNext())yield return wait.Current;Debug.Log("[SMC PASS] cursors: friend pointer shown on host");
            wait=Wait(()=>Activity.Of(1)==Activity.Labels[6],"host sees what the friend is doing",20);while(wait.MoveNext())yield return wait.Current;Debug.Log("[SMC PASS] activity: friend shown as "+Activity.Of(1));
            wait=Wait(()=>Received("READY_AWAY"),"client completed settings before away phase",60);while(wait.MoveNext())yield return wait.Current;HostPhase.ProbeAway=true;Signal("GO_AWAY");
            wait=Wait(()=>Received("PHASE_AWAY_OK"),"friend followed host away",30);while(wait.MoveNext())yield return wait.Current;
            Require(GeneralHelper.GetCustomName(b)=="SMC_RENAMED","action while host away was not executed");
            HostPhase.ProbeAway=false;
            wait=Wait(()=>Received("PHASE_BACK"),"friend resumed after host returned",30);while(wait.MoveNext())yield return wait.Current;Debug.Log("[SMC PASS] phase: friend waits while host is away and resumes on return");
            Require(ticks>0,"host simulation ran during peer session");if(CaptureMilliseconds.Count>0){CaptureMilliseconds.Sort();Debug.Log("[SMC PERF] incremental snapshot samples="+CaptureMilliseconds.Count+" medianMs="+CaptureMilliseconds[CaptureMilliseconds.Count/2].ToString("F2")+" p95Ms="+CaptureMilliseconds[Math.Min(CaptureMilliseconds.Count-1,(int)(CaptureMilliseconds.Count*.95))].ToString("F2"));}Debug.Log("[SMC ROUTER] "+root.ProbeMapping);Signal("COMPLETE");Debug.Log("[SMC COMPLETE] host: independent menus, overlapping drags, authoritative drops and invitation");
        }
        static IEnumerator ClientCheck(Connection root){
            var wait=Wait(()=>MainMenuUIController.Instance!=null,"client main menu",90);while(wait.MoveNext())yield return wait.Current;InstallInput();
            Require(!Screen.fullScreen,"client runs in windowed mode");
            if(Argument("--smc-suite")=="recovery")InstallRecoveryProbe(1);
            control=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));other=new IPEndPoint(IPAddress.Loopback,int.Parse(Argument("--smc-control")));root.ProbeJoin(Argument("--smc-invite"));
            wait=Wait(()=>root.Ready,"client loaded host snapshot through pinned TLS",90);while(wait.MoveNext())yield return wait.Current;
            if(Argument("--smc-suite")=="recovery")Require(recoveryInjected==1&&root.ProbeApplyFailures==0,"initial failed snapshot recovered before the client became ready");
            var a=Find(root,"SMC_HOST_ITEM");var b=Find(root,"SMC_CLIENT_ITEM");Require(a!=null&&b!=null,"client has the host test items");int ticks=0;Action<GameItem,GameInventory,SlotMarker> randomCallback=(x,y,z)=>{ticks++;x.unitValue=UnityEngine.Random.Range(1,100);};a.onOneSecondItemFunc=randomCallback;ShowStorage();Signal("JOINED");if(Argument("--smc-suite")!=null){var suite=RunExtended(root,false);while(suite.MoveNext())yield return suite.Current;wait=Wait(()=>Received("COMPLETE"),"extended host acknowledgement");while(wait.MoveNext())yield return wait.Current;Debug.Log("[SMC COMPLETE] client extended "+Argument("--smc-suite"));yield break;}
            wait=Wait(()=>Received("OPEN_MENU"),"host menu barrier");while(wait.MoveNext())yield return wait.Current;pointer=Point(b);yield return null;yield return null;Context(b);Require(MenuOpen,"client native context menu open");Signal("CLIENT_MENU");
            wait=Wait(()=>Received("CLOSE_MENU"),"both native menus remained independent");while(wait.MoveNext())yield return wait.Current;ItemContextHandler.current.UpdateReset();Signal("MENU_CLOSED");
            wait=Wait(()=>Received("DRAG_OTHER"),"host drag barrier");while(wait.MoveNext())yield return wait.Current;
            int occupied=root.World.Id(a);root.Claim(new[]{occupied});wait=Wait(()=>!root.ProbePending(occupied),"same-item claim received host reply");while(wait.MoveNext())yield return wait.Current;Require(!root.LocalLeases.ContainsKey(occupied),"host rejected a later claim on the held item");Require(ItemBusyHint.Visible&&ItemBusyHint.Text.StartsWith("物品使用中"),"friend is told the item is in use ("+ItemBusyHint.Text+")");
            BeginDrag(b);Require(ItemMouseDragHandler.current.IsDraggingItem,"client native drag started");
            int orientation=((GameItemElement)b).tempShape.orientation;var rotate=KeyBindHelper.Get("rotate_cw");pressed.Add(rotate);ItemMouseDragHandler.current.UpdatePress(new HashSet<KeyCode>{rotate});pressed.Remove(rotate);ItemMouseDragHandler.current.UpdateRelease(new HashSet<KeyCode>{rotate});Require(((GameItemElement)b).tempShape.orientation!=orientation,"client configured rotation shortcut changes its local drag preview");
            wait=Wait(()=>root.LocalLeases.ContainsKey(root.World.Id(b)),"client drag lease granted");while(wait.MoveNext())yield return wait.Current;Signal("CLIENT_DRAG");
            wait=Wait(()=>Received("DROP"),"simultaneous drop barrier");while(wait.MoveNext())yield return wait.Current;Drop(EmporiumEntry.Instance.backInvinvElement,b);
            wait=Wait(()=>Received("HOST_DROPPED"),"host committed both operations");while(wait.MoveNext())yield return wait.Current;
            wait=Wait(()=>a.parentInventory==EmporiumEntry.Instance.backInvinvElement&&b.parentInventory==EmporiumEntry.Instance.backInvinvElement,"replica matches both host item locations");while(wait.MoveNext())yield return wait.Current;
            Require(a.onOneSecondItemFunc==randomCallback&&ticks==0,"client item random callback stayed installed but never executed");ReplicaVisuals.Refresh(true);Require(StoreVendingMachine.Instance==null||StoreVendingMachine.Instance.gameObject.activeSelf==PlayerStore.Instance.isVendingMachineUnlocked,"friend shop furniture follows the host (vending machine)");Require(a.unitValue>=100000,"client displays host-generated random result");Signal("REPLICA_MATCH");
            // Drift 1: a side effect slips past the barriers and changes a synced item on this machine only.
            int hostCount=b.unitCount;WorldReplica.Applying=true;try{b.unitCount=hostCount+7;}finally{WorldReplica.Applying=false;}
            Require(b.unitCount==hostCount+7,"drift fixture applied locally");
            wait=Wait(()=>b.unitCount==hostCount,"drifted item restored from host within the drift-check window",30);while(wait.MoveNext())yield return wait.Current;
            Require(root.Drift.Corrections>=1,"drift check reported a correction");Debug.Log("[SMC PASS] drift: locally changed item restored ("+root.Drift.LastNote+")");
            // Drift 2: an item appears on this machine that the host never created (e.g. another mod's local spawn).
            GameItem stray;WorldReplica.Applying=true;try{stray=ModHelper.SpawnItem("magnifier");stray.mayStackItemFunc=(x,y)=>false;stray.name="SMC_STRAY_ITEM";GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement,stray,1);}finally{WorldReplica.Applying=false;}
            Func<bool> strayPresent=()=>{foreach(var live in GraphUtils.FindChildrenType<GameItem>(EmporiumEntry.Instance.invElement,null,null))if(live!=null&&live.name=="SMC_STRAY_ITEM")return true;return false;};
            Require(strayPresent(),"stray fixture placed locally");
            wait=Wait(()=>!strayPresent(),"locally created item removed by drift check",40);while(wait.MoveNext())yield return wait.Current;
            Require(root.Drift.Removed>=1,"drift check reported a removal");Debug.Log("[SMC PASS] drift: item not created by host was removed");
            // Gameplay mod hooks never fire on a friend's machine; presentation hooks still do.
            int gameplayCalls=0;Action onWake=()=>gameplayCalls++;ModHook.OnWakingUpEarly+=onWake;
            try{AccessTools.Method(typeof(ModHook),"FireOnWakingUpEarly").Invoke(null,null);}finally{ModHook.OnWakingUpEarly-=onWake;}
            Require(gameplayCalls==0,"gameplay mod hook blocked on replica");Debug.Log("[SMC PASS] mod hooks: gameplay callbacks are host-only");
            Signal("DRIFT_FIXED");
            // Remote action: a rename typed on this machine is executed by the host and returns through sync.
            Require(RemoteActions.Adapter("Confront",2)!=null&&RemoteActions.Adapter("Go",2)!=null&&RemoteActions.Adapter("Back",0)!=null&&RemoteActions.Adapter("Scavenge",0)!=null&&RemoteActions.Adapter("Join",0)!=null&&RemoteActions.Adapter("Unjoin",0)!=null,"confront, own-outing and come-along actions are forwarded");Require(RemoteActions.Find(typeof(MapUIManager).GetMethod("ReturnNow"))==null&&RemoteActions.Adapter("TravelTo",1)==null,"a friend can no longer move the host");bool kitRoot=false,hostGround=false;foreach(var r in WorldReplica.Roots()){if(r.Key==OutingKits.KitRoot)kitRoot=true;if(r.Key==OutingKits.VanillaRoot)hostGround=true;}Require(kitRoot&&hostGround&&OutingKits.All.Length==OutingKits.Slots,"outing grounds and friend kits are part of the synced world");Require(OutingKits.FriendMayGo("dumping_ground")&&OutingKits.FriendMayGo("commissary")&&!OutingKits.FriendMayGo("dr_jackson"),"friends may go to the dumping ground and commissary only");
            int registered=0;foreach(var target in RemoteActions.Targets())registered++;Require(registered>=40,"forwarded action list loaded ("+registered+")");
            LocalControl.ProbeInput=true;LocalControl.depth++;try{GeneralHelper.SetCustomName(b,"SMC_RENAMED");}finally{LocalControl.depth--;}
            Require(GeneralHelper.GetCustomName(b)!="SMC_RENAMED","rename was not applied locally ahead of the host");Signal("REMOTE_SENT");Activity.Commissary=true;
            wait=Wait(()=>GeneralHelper.GetCustomName(b)=="SMC_RENAMED","host result arrived through world sync",30);while(wait.MoveNext())yield return wait.Current;Debug.Log("[SMC PASS] remote action: rename forwarded, executed on host, synced back");
            // Machine-panel setting: changed on this machine, sent to the host, checked there, and synced back.
            Func<int> percent=()=>b.GetTagReadonly("FEED_DISPENSER_TARGET_PERCENT_INT").GetInt();
            Action<int> panel=v=>{MachineSettings.Input++;try{b.ModifyTag("FEED_DISPENSER_TARGET_PERCENT_INT",t=>t.Enable().SetInt(v));}finally{MachineSettings.Input--;}WorldReplica.Applying=true;try{b.ModifyTag("FEED_DISPENSER_TARGET_PERCENT_INT",t=>t.Enable().SetInt(85));}finally{WorldReplica.Applying=false;}};
            Require(percent()==85,"machine setting fixture synced to friend");panel(40);Require(percent()==85,"setting fixture restored locally before the host answers");
            wait=Wait(()=>percent()==40,"machine setting executed on host and synced back",30);while(wait.MoveNext())yield return wait.Current;
            panel(999);wait=Wait(()=>percent()==100,"out-of-range machine setting clamped by host",30);while(wait.MoveNext())yield return wait.Current;
            Require(!MachineSettings.Allowed("CASH_INT")&&!MachineSettings.Allowed("AUTO_BUTCHER_LAST_COUNT_INT"),"only panel settings may be changed by friends");Debug.Log("[SMC PASS] machine settings: friend panel change forwarded, range-checked, synced back");
            Require(Array.TrueForAll(PanelRelay.Methods,m=>m!=null)&&Array.IndexOf(PanelRelay.Methods,AccessTools.Method(typeof(NegociationUIManager),"OpenConfirmPanel"))>=0&&!PanelRelay.Capturing,"reading panels and budget confirmations are relayed to the player who used them");Require((ItemContextHandler.current==null||!NativeActions.holders.ContainsKey(ItemContextHandler.current))&&(ItemMouseDoubleClickHandler.current==null||!NativeActions.holders.ContainsKey(ItemMouseDoubleClickHandler.current)),"opening an item never locks it");Debug.Log("[SMC PASS] panels: friend reading panels open on the friend's screen");
            string myId=root.MyIdentity;Require(!string.IsNullOrEmpty(myId)&&myId.Length<=66,"friend has a stable coordinator identity ("+myId+")");string cleaned=CoordinatorPresets.Clean("{\"presets\":[{\"name\":\"A\",\"keyCode\":-5,\"itemIds\":[1,2],\"itemTabs\":[{\"itemId\":9,\"tabs\":\"0\"}]}]}");Require(cleaned!=null&&cleaned.Contains("\"keyCode\":0")&&!cleaned.Contains("\"itemId\":9"),"coordinator presets from friends are cleaned ("+cleaned+")");Require(CoordinatorPresets.Clean("not json")==null,"broken coordinator presets refused");Require(RemoteActions.Adapter("Save",2)!=null&&RemoteActions.Adapter("Fire",0)!=null&&RemoteActions.Adapter("Raise",0)!=null&&RemoteActions.Adapter("Lower",0)!=null,"coordinator and shotgun actions are forwarded");Debug.Log("[SMC PASS] coordinator presets and shotgun wiring");Require(RemoteActions.Adapter("SetTag",3)!=null&&RemoteActions.Adapter("PressButton",3)!=null&&RemoteActions.Adapter("Inspect",3)!=null&&RemoteActions.Adapter("MatchLabel",1)!=null&&RemoteActions.Adapter("BuyWild",1)!=null&&RemoteActions.Adapter("AcceptDeal",0)!=null&&RemoteActions.Adapter("UndoSort",1)!=null&&RemoteActions.Adapter("Discover",3)!=null,"audited actions are forwarded");Require(!MachineSettings.Allowed("IS_OWNED_TAG")&&MachineSettings.Allowed("HOPPER_ORDER_INT")&&MachineSettings.Allowed("PINNED_TAG"),"item settings: ownership never taken from a friend, panel settings are");Debug.Log("[SMC PASS] audit wiring");Require(RemoteActions.Adapter("Pick",0)!=null&&RemoteActions.Adapter("Key",1)!=null&&RemoteActions.Adapter("Dial",1)!=null&&RemoteActions.Adapter("Choose",2)!=null,"the shop phone is shared");Require(RemoteActions.Adapter("UseBandage",0)!=null&&OutingKits.IsVoidBag(null)==false&&Achievements.Message==42,"outing supplies and achievements wired");
            wait=Wait(()=>Cursors.Seen(0),"friend sees the host's mouse pointer",20);while(wait.MoveNext())yield return wait.Current;Debug.Log("[SMC PASS] cursors: host pointer shown on friend");
            wait=Wait(()=>PlayerNames.Of(0)!="房主","friend received the players' chosen names",20);while(wait.MoveNext())yield return wait.Current;Debug.Log("[SMC PASS] names: host pointer labelled "+PlayerNames.Of(0));
            Activity.Commissary=false;Signal("READY_AWAY");wait=Wait(()=>Received("GO_AWAY"),"host went away",30);while(wait.MoveNext())yield return wait.Current;wait=Wait(()=>HostPhase.Away,"friend sees host away",30);while(wait.MoveNext())yield return wait.Current;
            LocalControl.depth++;try{GeneralHelper.SetCustomName(b,"SMC_AWAY");}finally{LocalControl.depth--;}
            Require(RemoteActions.LastNote==HostPhase.Text,"friend told to wait while host is away");yield return new WaitForSecondsRealtime(2);
            Require(GeneralHelper.GetCustomName(b)=="SMC_RENAMED","no action executed while host away");Signal("PHASE_AWAY_OK");
            wait=Wait(()=>!HostPhase.Away,"friend sees host back",30);while(wait.MoveNext())yield return wait.Current;Signal("PHASE_BACK");
            wait=Wait(()=>Received("COMPLETE"),"host final acknowledgement");while(wait.MoveNext())yield return wait.Current;Debug.Log("[SMC COMPLETE] client: host-only simulation, native local interaction, drift correction and host-only mod hooks");
        }
    }
    internal sealed class FakeRouter : IDisposable {
        sealed class Mapping {internal string Description,Address,Inside;internal int Seconds;}
        readonly Dictionary<int,Mapping> entries=new Dictionary<int,Mapping>();readonly object gate=new object();
        readonly TcpListener listener;readonly GameObject pump;bool stopped;
        internal readonly Uri Control;internal bool Permanent;internal int AddCount;
        internal FakeRouter(){listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Control=new Uri("http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/control");pump=new GameObject("SMC_FakeRouter");pump.AddComponent<FakeRouterPump>().Router=this;}
        internal int Count {get{lock(gate)return entries.Count;}}
        internal void Existing(int port,string name){lock(gate)entries[port]=new Mapping{Description=name,Address="127.0.0.1",Inside="7777",Seconds=3600};}
        internal string Description(int port){lock(gate){Mapping entry;return entries.TryGetValue(port,out entry)?entry.Description:null;}}
        // Polled from the main thread (no background threads, same as the mod itself).
        internal void Poll(){while(!stopped&&listener.Pending()){try{using(var client=listener.AcceptTcpClient()){client.ReceiveTimeout=3000;client.SendTimeout=3000;Handle(client.GetStream());}}catch(SocketException){if(!stopped)throw;}catch(ObjectDisposedException){if(!stopped)throw;}}}
        void Handle(NetworkStream stream){
            var headers=new List<byte>();int next;while((next=stream.ReadByte())>=0){headers.Add((byte)next);int count=headers.Count;if(count>8192)throw new Exception("header too large");if(count>=4&&headers[count-4]==13&&headers[count-3]==10&&headers[count-2]==13&&headers[count-1]==10)break;}
            string text=Encoding.ASCII.GetString(headers.ToArray());int length=0;string action=null;
            foreach(string line in text.Split(new[]{"\r\n"},StringSplitOptions.None)){if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=int.Parse(line.Substring(15).Trim());if(line.StartsWith("SOAPAction:",StringComparison.OrdinalIgnoreCase))action=line.Substring(line.LastIndexOf('#')+1).Trim(' ','"');}
            if(text.IndexOf("100-continue",StringComparison.OrdinalIgnoreCase)>=0){byte[] interim=Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");stream.Write(interim,0,interim.Length);}
            if(length<1||length>8192)throw new Exception("invalid request body");byte[] body=new byte[length];int offset=0;while(offset<length){int n=stream.Read(body,offset,length-offset);if(n<=0)throw new EndOfStreamException();offset+=n;}
            var request=RouterRules.ParseXml(body);string fields="";int fault=0;
            Func<string,string> get=name=>{var node=request.SelectSingleNode("//*[local-name()='"+name+"']");return node==null?null:node.InnerText;};
            lock(gate){
                if(action=="GetExternalIPAddress")fields="<NewExternalIPAddress>8.8.8.8</NewExternalIPAddress>";
                else {
                    int port=int.Parse(get("NewExternalPort"));Mapping entry;
                    if(action=="GetSpecificPortMappingEntry"){
                        if(!entries.TryGetValue(port,out entry))fault=714;
                        else fields="<NewInternalClient>"+entry.Address+"</NewInternalClient><NewInternalPort>"+entry.Inside+"</NewInternalPort><NewPortMappingDescription>"+entry.Description+"</NewPortMappingDescription><NewLeaseDuration>"+entry.Seconds+"</NewLeaseDuration>";
                    }else if(action=="AddPortMapping"){
                        entries[port]=new Mapping{Address=get("NewInternalClient"),Inside=get("NewInternalPort"),Description=get("NewPortMappingDescription"),Seconds=Permanent?0:int.Parse(get("NewLeaseDuration"))};AddCount++;
                    }else if(action=="DeletePortMapping")entries.Remove(port);else throw new Exception("unexpected SOAP operation");
                }
            }
            string payload=fault==0?"<"+action+"Response>"+fields+"</"+action+"Response>":"<Fault><errorCode>"+fault+"</errorCode></Fault>";
            byte[] response=Encoding.UTF8.GetBytes("<Envelope>"+payload+"</Envelope>");byte[] responseHeaders=Encoding.ASCII.GetBytes("HTTP/1.1 "+(fault==0?"200 OK":"500 Internal Server Error")+"\r\nContent-Length: "+response.Length+"\r\nContent-Type: text/xml\r\nConnection: close\r\n\r\n");stream.Write(responseHeaders,0,responseHeaders.Length);stream.Write(response,0,response.Length);
        }
        public void Dispose(){stopped=true;listener.Stop();if(pump!=null)UnityEngine.Object.Destroy(pump);}
    }
    internal sealed class FakeRouterPump : MonoBehaviour { internal FakeRouter Router; void Update(){if(Router!=null)Router.Poll();} }
}
#endif
