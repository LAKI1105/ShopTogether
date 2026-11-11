using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.Xml;

namespace ShopTogether {
    /// <summary>
    /// Opens a temporary TCP forward on the home router (UPnP IGD, then NAT-PMP), renews it, and removes it
    /// when the room closes. Runs as a coroutine on the game's main thread: every network step is
    /// non-blocking (polled UDP sockets, UnityWebRequest), so the mod starts no threads of its own.
    /// Safety rules: only the local gateway is ever contacted, mappings are owned (unique description),
    /// lifetimes are capped at one hour, and the mapping is deleted on close.
    /// </summary>
    internal sealed class RouterMapping : IDisposable {
        readonly int port;
        bool disposed;
        internal bool Done;
        internal MappingStatus Current=new MappingStatus{Message="~SMC049~"};
        UpnpLease upnp;NatPmpLease natpmp;
        internal RouterMapping(int port){this.port=port;}
        /// <summary>Started by the owning Connection with its own StartCoroutine.</summary>
        internal IEnumerator Run(){
            var routes=GatewayRoute.Find();
            foreach(var route in routes){
                if(disposed)break;
                var u=new UpnpLease(route,port);var attempt=u.Create(()=>disposed);while(attempt.MoveNext())yield return attempt.Current;
                if(u.Granted){upnp=u;break;}
                var n=new NatPmpLease(route,port);attempt=n.Create(()=>disposed);while(attempt.MoveNext())yield return attempt.Current;
                if(n.Granted){natpmp=n;break;}
            }
            IRouterLease lease=(IRouterLease)upnp??natpmp;
            if(disposed){Cleanup();Done=true;yield break;}
            if(lease==null){Current=new MappingStatus{Message="~SMC048~"};Done=true;yield break;}
            Publish(lease);Done=true;
            while(!disposed){
                float until=Time.realtimeSinceStartup+Math.Max(30,lease.Lifetime/2);
                while(!disposed&&Time.realtimeSinceStartup<until)yield return null;
                if(disposed)break;
                var renew=lease.Renew(()=>disposed);while(renew.MoveNext())yield return renew.Current;
                if(!lease.Granted){Current=new MappingStatus{Message="~SMC046~"};yield break;}
                Publish(lease);
            }
            Cleanup();
        }
        void Publish(IRouterLease lease){
            bool publicAddress=InviteCode.IsPublic(lease.External.Address);
            Current=new MappingStatus{Local=lease.Local,External=publicAddress?lease.External:null,Message=publicAddress?"~SMC155~":"~SMC024~"};
        }
        void Cleanup(){try{if(upnp!=null)upnp.Remove();if(natpmp!=null)natpmp.Remove();}catch{}upnp=null;natpmp=null;}
        public void Dispose(){if(disposed)return;disposed=true;Cleanup();}
    }
    internal interface IRouterLease {
        IPEndPoint External {get;}
        IPAddress Local {get;}
        int Lifetime {get;}
        bool Granted {get;}
        IEnumerator Renew(Func<bool> cancelled);
        void Remove();
    }

    internal static class RouterIo {
        internal static bool ValidGatewayUrl(Uri uri,IPAddress gateway){return RouterRules.ValidGatewayUrl(uri,gateway);}
        internal static XmlDocument ParseXml(byte[] bytes){return RouterRules.ParseXml(bytes);}
        internal static string Value(XmlNode node,string name){return RouterRules.Value(node,name);}
        internal sealed class HttpResult{internal XmlDocument Doc;internal bool Failed;}
        /// <summary>GET (body==null) or SOAP POST to the gateway; polled, never blocks the frame.</summary>
        internal static IEnumerator Http(Uri url,IPAddress gateway,string action,byte[] body,HttpResult result,Func<bool> cancelled){
            result.Failed=true;result.Doc=null;
            if(!ValidGatewayUrl(url,gateway))yield break;
            var request=new UnityWebRequest(url.AbsoluteUri,body==null?"GET":"POST");
            request.downloadHandler=new DownloadHandlerBuffer();request.redirectLimit=0;request.timeout=3;
            if(body!=null){request.uploadHandler=new UploadHandlerRaw(body);request.SetRequestHeader("Content-Type","text/xml; charset=utf-8");request.SetRequestHeader("SOAPAction","\""+action+"\"");}
            try{
                request.SendWebRequest();
                while(!request.isDone){if(cancelled()){request.Abort();yield break;}yield return null;}
                long code=request.responseCode;
                if(code!=200&&code!=500)yield break;
                byte[] data=request.downloadHandler.data;
                try{result.Doc=ParseXml(data);result.Failed=false;}catch{}
            }finally{request.Dispose();}
        }
        internal static IEnumerator Discover(GatewayRoute route,List<Uri> urls,Func<bool> cancelled){
            Socket socket=null;
            try{
                socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);socket.Blocking=false;
                socket.Bind(new IPEndPoint(route.Local,0));socket.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.MulticastTimeToLive,2);socket.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.MulticastInterface,route.Local.GetAddressBytes());
                var target=new IPEndPoint(IPAddress.Parse("239.255.255.250"),1900);
                foreach(string search in new[]{"urn:schemas-upnp-org:device:InternetGatewayDevice:1","urn:schemas-upnp-org:service:WANIPConnection:1","urn:schemas-upnp-org:service:WANPPPConnection:1"}){
                    byte[] data=Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: "+search+"\r\n\r\n");socket.SendTo(data,target);
                }
            }catch{if(socket!=null)socket.Close();yield break;}
            float until=Time.realtimeSinceStartup+1.5f;byte[] bytes=new byte[4096];
            try{
                while(Time.realtimeSinceStartup<until&&urls.Count<4&&!cancelled()){
                    while(socket.Available>0){
                        EndPoint source=new IPEndPoint(IPAddress.Any,0);int length;
                        try{length=socket.ReceiveFrom(bytes,ref source);}catch(SocketException){break;}
                        if(!((IPEndPoint)source).Address.Equals(route.Gateway))continue;
                        string text=Encoding.ASCII.GetString(bytes,0,length);if(!text.StartsWith("HTTP/1.1 200",StringComparison.OrdinalIgnoreCase))continue;
                        foreach(string line in text.Split(new[]{"\r\n"},StringSplitOptions.None))if(line.StartsWith("location:",StringComparison.OrdinalIgnoreCase)){Uri uri;if(Uri.TryCreate(line.Substring(9).Trim(),UriKind.Absolute,out uri)&&ValidGatewayUrl(uri,route.Gateway)&&!urls.Contains(uri))urls.Add(uri);}
                    }
                    yield return null;
                }
            }finally{socket.Close();}
        }
    }

    internal sealed class UpnpLease : IRouterLease {
        static readonly string[] Services={"urn:schemas-upnp-org:service:WANIPConnection:1","urn:schemas-upnp-org:service:WANIPConnection:2","urn:schemas-upnp-org:service:WANPPPConnection:1"};
        readonly GatewayRoute route;readonly int internalPort;readonly string description;
        Uri control;string service;int externalPort;
        public IPEndPoint External {get;private set;}
        public IPAddress Local {get{return route.Local;}}
        public int Lifetime {get;private set;}
        public bool Granted {get;private set;}
        internal UpnpLease(GatewayRoute route,int port){this.route=route;internalPort=port;Lifetime=3600;description="SMC-"+Guid.NewGuid().ToString("N");}
        static string Get(XmlDocument doc,string name){if(doc==null)return null;var node=doc.SelectSingleNode("//*[local-name()='"+name+"']");return node==null?null:node.InnerText;}
        static int RandomPortOffset(){byte[] bytes=new byte[2];using(var random=System.Security.Cryptography.RandomNumberGenerator.Create())random.GetBytes(bytes);return ((bytes[0]<<8)|bytes[1])%16384;}
        string Key {get{return "<NewRemoteHost></NewRemoteHost><NewExternalPort>"+externalPort+"</NewExternalPort><NewProtocol>TCP</NewProtocol>";}}
        byte[] Envelope(string action,string fields){return Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:"+action+" xmlns:u=\""+service+"\">"+fields+"</u:"+action+"></s:Body></s:Envelope>");}
        sealed class SoapResult{internal XmlDocument Doc;internal int Fault;internal bool Ok;}
        IEnumerator Soap(string action,string fields,SoapResult result,Func<bool> cancelled){
            result.Ok=false;result.Fault=0;result.Doc=null;
            var http=new RouterIo.HttpResult();var step=RouterIo.Http(control,route.Gateway,service+"#"+action,Envelope(action,fields),http,cancelled);while(step.MoveNext())yield return step.Current;
            if(http.Failed)yield break;
            int code;if(int.TryParse(Get(http.Doc,"errorCode"),out code)){result.Fault=code;yield break;}
            if(http.Doc.SelectSingleNode("//*[local-name()='"+action+"Response']")==null)yield break;
            result.Doc=http.Doc;result.Ok=true;
        }
        bool Owns(XmlDocument doc){return Get(doc,"NewInternalClient")==route.Local.ToString()&&Get(doc,"NewInternalPort")==internalPort.ToString()&&Get(doc,"NewPortMappingDescription")==description;}
        internal IEnumerator Create(Func<bool> cancelled){
            var urls=new List<Uri>();var discover=RouterIo.Discover(route,urls,cancelled);while(discover.MoveNext())yield return discover.Current;
            foreach(var location in urls){
                if(cancelled())yield break;
                var page=new RouterIo.HttpResult();var get=RouterIo.Http(location,route.Gateway,null,null,page,cancelled);while(get.MoveNext())yield return get.Current;
                if(page.Failed)continue;
                foreach(XmlNode node in page.Doc.SelectNodes("//*[local-name()='service']")){
                    string type=RouterIo.Value(node,"serviceType"),path=RouterIo.Value(node,"controlURL");
                    if(Array.IndexOf(Services,type)<0)continue;
                    Uri candidate;if(!Uri.TryCreate(location,path,out candidate)||!RouterIo.ValidGatewayUrl(candidate,route.Gateway))continue;
                    control=candidate;service=type;
                    var attempt=TryService(cancelled);while(attempt.MoveNext())yield return attempt.Current;
                    if(Granted)yield break;
                }
            }
        }
        IEnumerator TryService(Func<bool> cancelled){
            var r=new SoapResult();var step=Soap("GetExternalIPAddress","",r,cancelled);while(step.MoveNext())yield return step.Current;
            IPAddress external;if(!r.Ok||!IPAddress.TryParse(Get(r.Doc,"NewExternalIPAddress"),out external)||external.AddressFamily!=AddressFamily.InterNetwork)yield break;
            for(int attempt=0;attempt<8&&!cancelled();attempt++){
                externalPort=attempt==0?internalPort:49152+RandomPortOffset();
                step=Soap("GetSpecificPortMappingEntry",Key,r,cancelled);while(step.MoveNext())yield return step.Current;
                if(r.Ok||r.Fault!=714)continue; // port taken (or router error): try another
                step=Soap("AddPortMapping",Key+"<NewInternalPort>"+internalPort+"</NewInternalPort><NewInternalClient>"+route.Local+"</NewInternalClient><NewEnabled>1</NewEnabled><NewPortMappingDescription>"+description+"</NewPortMappingDescription><NewLeaseDuration>"+Lifetime+"</NewLeaseDuration>",r,cancelled);while(step.MoveNext())yield return step.Current;
                if(!r.Ok){if(r.Fault==718)continue;yield break;}
                step=Soap("GetSpecificPortMappingEntry",Key,r,cancelled);while(step.MoveNext())yield return step.Current;
                int seconds;
                if(!r.Ok||!Owns(r.Doc)||!int.TryParse(Get(r.Doc,"NewLeaseDuration"),out seconds)||seconds<1||seconds>3600){Remove();externalPort=0;yield break;}
                Lifetime=seconds;External=new IPEndPoint(external,externalPort);Granted=true;yield break;
            }
        }
        public IEnumerator Renew(Func<bool> cancelled){
            Granted=false;var r=new SoapResult();
            var step=Soap("GetSpecificPortMappingEntry",Key,r,cancelled);while(step.MoveNext())yield return step.Current;if(!r.Ok||!Owns(r.Doc))yield break;
            step=Soap("AddPortMapping",Key+"<NewInternalPort>"+internalPort+"</NewInternalPort><NewInternalClient>"+route.Local+"</NewInternalClient><NewEnabled>1</NewEnabled><NewPortMappingDescription>"+description+"</NewPortMappingDescription><NewLeaseDuration>"+Lifetime+"</NewLeaseDuration>",r,cancelled);while(step.MoveNext())yield return step.Current;if(!r.Ok)yield break;
            step=Soap("GetSpecificPortMappingEntry",Key,r,cancelled);while(step.MoveNext())yield return step.Current;
            int seconds;if(!r.Ok||!Owns(r.Doc)||!int.TryParse(Get(r.Doc,"NewLeaseDuration"),out seconds)||seconds<1||seconds>3600)yield break;
            step=Soap("GetExternalIPAddress","",r,cancelled);while(step.MoveNext())yield return step.Current;
            IPAddress address;if(!r.Ok||!IPAddress.TryParse(Get(r.Doc,"NewExternalIPAddress"),out address)||address.AddressFamily!=AddressFamily.InterNetwork)yield break;
            Lifetime=seconds;External=new IPEndPoint(address,externalPort);Granted=true;
        }
        /// <summary>
        /// Deletes the mapping only if the router still reports it as ours (same client, port and unique
        /// description), so a rule someone else created on that port later is never removed. The two requests
        /// are chained on UnityWebRequest completion callbacks, which run on the main thread.
        /// </summary>
        public void Remove(){
            if(externalPort==0||control==null)return;
            int port=externalPort;externalPort=0;Granted=false;
            string key="<NewRemoteHost></NewRemoteHost><NewExternalPort>"+port+"</NewExternalPort><NewProtocol>TCP</NewProtocol>";
            try{
                var read=Post("GetSpecificPortMappingEntry",key);
                read.SendWebRequest().completed+=delegate{
                    bool owned=false;
                    try{if(read.responseCode==200){var doc=RouterIo.ParseXml(read.downloadHandler.data);owned=Get(doc,"errorCode")==null&&Owns(doc);}}catch{}
                    read.Dispose();
                    if(!owned)return;
                    try{var del=Post("DeletePortMapping",key);del.SendWebRequest().completed+=delegate{del.Dispose();};}catch{}
                };
            }catch{}
        }
        UnityWebRequest Post(string action,string fields){
            var request=new UnityWebRequest(control.AbsoluteUri,"POST");request.uploadHandler=new UploadHandlerRaw(Envelope(action,fields));request.downloadHandler=new DownloadHandlerBuffer();
            request.redirectLimit=0;request.timeout=3;request.SetRequestHeader("Content-Type","text/xml; charset=utf-8");request.SetRequestHeader("SOAPAction","\""+service+"#"+action+"\"");return request;
        }
#if LAN_PROBE
        internal IEnumerator ProbeTry(Uri control,string service,Func<bool> cancelled){this.control=control;this.service=service;var a=TryService(cancelled);while(a.MoveNext())yield return a.Current;}
        internal int ProbeExternalPort{get{return externalPort;}}
#endif
    }

    internal sealed class NatPmpLease : IRouterLease {
        readonly GatewayRoute route;readonly int internalPort;
        public IPEndPoint External {get;private set;}
        public IPAddress Local {get{return route.Local;}}
        public int Lifetime {get;private set;}
        public bool Granted {get;private set;}
        internal NatPmpLease(GatewayRoute route,int port){this.route=route;internalPort=port;}
        internal static byte[] Request(int inside,int outside,int seconds){return RouterRules.NatPmpRequest(inside,outside,seconds);}
        internal static bool ParseMapping(byte[] data,int inside,out int external,out int seconds){return RouterRules.NatPmpParseMapping(data,inside,out external,out seconds);}
        sealed class Reply{internal byte[] Data;}
        IEnumerator Exchange(byte[] request,int responseSize,Reply reply,Func<bool> cancelled){
            reply.Data=null;Socket socket=null;
            try{socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);socket.Blocking=false;socket.Bind(new IPEndPoint(route.Local,0));socket.Connect(new IPEndPoint(route.Gateway,5351));}
            catch{if(socket!=null)socket.Close();yield break;}
            byte[] bytes=new byte[64];
            try{
                foreach(float delay in new[]{.25f,.5f,1f}){
                    if(cancelled())yield break;
                    try{socket.Send(request);}catch{yield break;}
                    float until=Time.realtimeSinceStartup+delay;
                    while(Time.realtimeSinceStartup<until&&!cancelled()){
                        while(socket.Available>0){
                            int count;try{count=socket.Receive(bytes);}catch(SocketException){break;}
                            if(count!=responseSize||bytes[0]!=0||bytes[1]!=(128+request[1]))continue;
                            if(bytes[2]!=0||bytes[3]!=0)yield break;
                            reply.Data=new byte[count];Buffer.BlockCopy(bytes,0,reply.Data,0,count);yield break;
                        }
                        yield return null;
                    }
                }
            }finally{socket.Close();}
        }
        internal IEnumerator Create(Func<bool> cancelled){
            var reply=new Reply();var step=Exchange(new byte[]{0,0},12,reply,cancelled);while(step.MoveNext())yield return step.Current;if(reply.Data==null)yield break;
            var ip=new IPAddress(new[]{reply.Data[8],reply.Data[9],reply.Data[10],reply.Data[11]});
            step=Exchange(Request(internalPort,0,3600),16,reply,cancelled);while(step.MoveNext())yield return step.Current;
            int external,lifetime;if(!ParseMapping(reply.Data,internalPort,out external,out lifetime)||lifetime==0)yield break;
            External=new IPEndPoint(ip,external);Lifetime=lifetime;Granted=true;
        }
        public IEnumerator Renew(Func<bool> cancelled){
            Granted=false;var reply=new Reply();
            var step=Exchange(Request(internalPort,External.Port,3600),16,reply,cancelled);while(step.MoveNext())yield return step.Current;
            int port,seconds;if(!ParseMapping(reply.Data,internalPort,out port,out seconds)||seconds<1)yield break;
            step=Exchange(new byte[]{0,0},12,reply,cancelled);while(step.MoveNext())yield return step.Current;if(reply.Data==null)yield break;
            External=new IPEndPoint(new IPAddress(new[]{reply.Data[8],reply.Data[9],reply.Data[10],reply.Data[11]}),port);Lifetime=seconds;Granted=true;
        }
        /// <summary>Single best-effort datagram asking the router to drop the mapping (lifetime 0).</summary>
        public void Remove(){
            if(External==null)return;
            try{using(var socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp)){socket.Bind(new IPEndPoint(route.Local,0));socket.SendTo(Request(internalPort,0,0),new IPEndPoint(route.Gateway,5351));}}catch{}
            External=null;Granted=false;
        }
    }
    /// <summary>Asks a public echo service for this PC's IPv4 address; polled from the main thread, no threads.</summary>
    internal sealed class PublicAddress : IDisposable {
        readonly UnityEngine.Networking.UnityWebRequest request;
        internal PublicAddress() {
            request = UnityEngine.Networking.UnityWebRequest.Get("https://api.ipify.org");
            request.timeout = 5; request.redirectLimit = 0;
            request.SendWebRequest();
        }
        internal bool IsCompleted { get { return request == null || request.isDone; } }
        internal string Value {
            get {
                if (request == null || !request.isDone || request.result != UnityEngine.Networking.UnityWebRequest.Result.Success) return null;
                string text = request.downloadHandler.text; if (text == null || text.Length > 64) return null;
                IPAddress ip; text = text.Trim();
                return IPAddress.TryParse(text, out ip) && ip.AddressFamily == AddressFamily.InterNetwork ? ip.ToString() : null;
            }
        }
        public void Dispose() { try { request.Abort(); } catch { } request.Dispose(); }
    }
}
