using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ShopTogether;
class SecurityProbe {
    static void Require(bool ok,string label) { if(!ok) throw new Exception(label); Console.WriteLine("PASS "+label); }
    static void Until(Func<bool> test) { var timer=Stopwatch.StartNew(); while(!test()){if(timer.ElapsedMilliseconds>10000)throw new Exception("timeout");Thread.Sleep(2);} }
    // Steam carries packets, not a byte stream: game packets larger than one Steam packet are split and joined back.
    static void SteamStream() {
        MessageStream a=null,b=null;int pieces=0;
        a=new MessageStream((data,length)=>{pieces++;var copy=new byte[length];Buffer.BlockCopy(data,0,copy,0,length);b.Arrive(copy);return true;});
        b=new MessageStream((data,length)=>true);
        var packet=new byte[600*1024+17];for(int i=0;i<packet.Length;i++)packet[i]=(byte)(i*31+7);
        a.Send(packet);a.Send(new byte[]{9,8,7});
        var got=new System.Collections.Generic.List<byte>();var buffer=new byte[32768];int n;while((n=b.Receive(buffer))>0)for(int i=0;i<n;i++)got.Add(buffer[i]);
        bool same=got.Count==packet.Length+3&&got[packet.Length]==9&&got[packet.Length+2]==7;if(same)for(int i=0;i<packet.Length;i+=997)if(got[i]!=packet[i]){same=false;break;}
        Require(same&&pieces>=3&&n==-1,"Steam relay: large game packets split into Steam packets and rejoined in order");
        b.Failed=true;Require(b.Receive(buffer)==0,"Steam relay: a failed Steam session ends the connection");
        bool refused=false;try{a.Close();a.Send(new byte[1]);}catch(InvalidOperationException){refused=true;}Require(refused,"Steam relay: nothing is sent after closing");
        var c=new MessageStream((data,length)=>true);for(int i=0;i<300&&!c.Failed;i++)c.Arrive(new byte[256*1024]);Require(c.Failed,"Steam relay: unread data is bounded");
        // Over the internet Steam refuses data while its send buffer is full: wait and continue, in order.
        MessageStream d=null,e=null;bool open=false;double clock=0;
        d=new MessageStream((data,length)=>{if(!open)return false;var copy=new byte[length];Buffer.BlockCopy(data,0,copy,0,length);e.Arrive(copy);return true;});d.Now=()=>clock;
        e=new MessageStream((data,length)=>true);
        d.Send(packet);d.Send(new byte[]{5});Require(d.Pending&&!d.Failed,"Steam relay: a full send buffer is waited out, not treated as a lost link");
        clock=30;d.Flush();Require(!d.Failed,"Steam relay: a slow link is not dropped early");
        open=true;d.Flush();got.Clear();while((n=e.Receive(buffer))>0)for(int i=0;i<n;i++)got.Add(buffer[i]);
        Require(!d.Pending&&got.Count==packet.Length+1&&got[packet.Length]==5&&got[1234]==packet[1234],"Steam relay: held data is delivered in order once Steam accepts it");
        var f=new MessageStream((data,length)=>false);double t=0;f.Now=()=>t;f.Send(new byte[10]);t=89.9;f.Flush();Require(!f.Failed,"Steam relay: refusing for less than 90 seconds does not disconnect");t=90;f.Flush();Require(f.Failed,"Steam relay: 90 seconds without accepting data ends the link");
    }
    static void Pair(RoomIdentity identity,bool wrong) {
        using(var listener=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp))
        using(var clientSocket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp)) {
            listener.Bind(new IPEndPoint(IPAddress.Loopback,0)); listener.Listen(1); clientSocket.Connect(listener.LocalEndPoint);
            using(var server=new SecureChannel(listener.Accept(),identity.Certificate,null))
            using(var client=new SecureChannel(clientSocket,null,wrong?new string('0',64):identity.Fingerprint)) {
                if(wrong) {
                    bool rejected=false;try{Until(delegate{return client.Ready;});}catch(System.Security.Authentication.AuthenticationException){rejected=true;}
                    Require(rejected,"wrong host fingerprint rejected before application data");return;
                }
                Until(delegate{bool s=server.Ready;bool c=client.Ready;return s&&c;});
                byte[] message=new byte[]{1,2,3,4,5},buffer=new byte[32768];client.Send(message);
                int count=-1;Until(delegate{count=server.Receive(buffer);return count>=0;});
                Require(count==message.Length && buffer[0]==1 && buffer[4]==5,"pinned TLS 1.2 carries operation requests");
                server.Send(new byte[]{42});Until(delegate{count=client.Receive(buffer);return count>=0;});
                Require(count==1 && buffer[0]==42,"encrypted bidirectional transfer");
            }
        }
    }
    static int Main(){
        try {
            IPEndPoint ep;string error;
            Require(DirectConnection.Parse("203.0.113.20:7777","9000",out ep,out error)&&ep.Port==7777,"public IPv4 and inline port");
            Require(DirectConnection.Parse("192.168.1.101：7778","7777",out ep,out error)&&ep.Port==7778,"fullwidth colon");
            Require(!DirectConnection.Parse("0.0.0.0:7777","7777",out ep,out error)&&!DirectConnection.Parse("1.2.3.4:65536","7777",out ep,out error),"invalid destinations rejected");
            Require(!ProtocolGuard.Valid(true,false,false,5,8)&&!ProtocolGuard.Valid(true,true,true,3,100),"unauthenticated input and wrong packet direction rejected");
            Require(ProtocolGuard.MaxLength(true,true)==65537&&!ProtocolGuard.Valid(true,true,true,21,65537)&&!ProtocolGuard.Valid(true,true,true,7,8)&&!ProtocolGuard.Valid(true,true,true,250,0),"host payload bounds, old keyboard packets and unknown commands rejected");
            Require(!ProtocolGuard.Finite(float.NaN)&&!ProtocolGuard.Finite(float.PositiveInfinity),"nonfinite pointer values rejected");
            var rate=new ProtocolGuard();for(int i=0;i<800;i++)if(!rate.Admit(1))throw new Exception("rate burst");Require(!rate.Admit(1)&&rate.Admit(2),"input flood limit and recovery");
            var admission=new AdmissionGuard();for(int i=0;i<8;i++)if(!admission.Admit("peer",0))throw new Exception("admission burst");Require(!admission.Admit("peer",0)&&admission.Admit("peer",10),"connection attempt limit and recovery");
            Require(!ProtocolGuard.Valid(true,true,true,20,4096)&&ProtocolGuard.Valid(true,true,true,21,1024),"host accepts requests, never client world snapshots");
            Require(ProtocolGuard.Valid(true,true,true,36,200)&&!ProtocolGuard.Valid(true,true,true,36,65537)&&!ProtocolGuard.Valid(true,true,true,34,10)&&!ProtocolGuard.Valid(true,true,true,37,10),"host accepts bounded friend actions, never phase or refusal messages");
            Require(ProtocolGuard.Valid(false,true,true,34,40)&&ProtocolGuard.Valid(false,true,true,37,40)&&!ProtocolGuard.Valid(false,true,true,34,65537)&&!ProtocolGuard.Valid(false,true,true,36,40),"friend accepts bounded phase and refusal messages, never action requests");
            {
                var big=new byte[6*1024*1024];var rng=new Random(7);for(int i=0;i<big.Length;i++)big[i]=(byte)(i%97==0?rng.Next(256):'a'+(i%26));
                var packed=LargeTransfer.Pack(20,big);Require(packed.Type==26&&packed.Parts.Length>=1,"large store update is compressed into pieces");
                var asm=new LargeTransfer.Assembler();byte inner=0;byte[] whole=null;foreach(var part in packed.Parts){byte t;var r=asm.Add(part,out t);if(r!=null){whole=r;inner=t;}}
                bool same=whole!=null&&whole.Length==big.Length&&inner==20;if(same)for(int i=0;i<big.Length;i+=4099)if(whole[i]!=big[i]){same=false;break;}
                Require(same,"pieces reassemble into the identical store update");
                var small=LargeTransfer.Pack(20,new byte[100]);Require(small.Type==20&&small.Parts.Length==1,"small updates are sent unchanged");
                bool refused=false;try{var bad=(byte[])packed.Parts[0].Clone();bad[8]=21;new LargeTransfer.Assembler().Add(bad,out inner);}catch(InvalidOperationException){refused=true;}
                Require(refused,"pieces may only carry world updates and drift checks");
                Require(ProtocolGuard.Valid(false,true,true,26,LargeTransfer.Header+LargeTransfer.Chunk)&&!ProtocolGuard.Valid(false,true,true,26,LargeTransfer.Header+LargeTransfer.Chunk+1)&&!ProtocolGuard.Valid(true,true,true,26,100),"piece size bounded; host never accepts pieces");
            }
            Require(ProtocolGuard.Valid(true,true,true,38,8)&&!ProtocolGuard.Valid(true,true,true,38,9)&&!ProtocolGuard.Valid(true,true,true,39,16)&&ProtocolGuard.Valid(false,true,true,39,4+12*3)&&!ProtocolGuard.Valid(false,true,true,39,4+12*9)&&!ProtocolGuard.Valid(false,true,true,38,8),"pointer messages: fixed small sizes, each only in its direction");
            Require(ProtocolGuard.Valid(false,true,true,40,200)&&!ProtocolGuard.Valid(false,true,true,40,4097)&&!ProtocolGuard.Valid(true,true,true,40,200),"player name list only from host, bounded");
            Require(ProtocolGuard.Valid(true,true,true,41,1)&&!ProtocolGuard.Valid(true,true,true,41,2)&&!ProtocolGuard.Valid(false,true,true,41,1),"activity: one byte, friend to host only");
            Require(ProtocolGuard.Valid(false,true,true,43,300)&&!ProtocolGuard.Valid(false,true,true,43,16385)&&!ProtocolGuard.Valid(true,true,true,43,300),"reading-panel relay only from host, bounded");
            Require(ProtocolGuard.Valid(true,true,true,Diagnostics.Message,Diagnostics.Limit)&&!ProtocolGuard.Valid(true,false,false,Diagnostics.Message,1)&&!ProtocolGuard.Valid(true,true,true,Diagnostics.Message,Diagnostics.Limit+1)&&!ProtocolGuard.Valid(false,true,true,Diagnostics.Message,1),"diagnostics require an authenticated client, correct direction and bounded length");
            SteamStream();
            RoomSetupTests.Run();
            // Certificate and TLS checks need the game's own Mono runtime (Mono.Security); they run inside the game in RunDual.ps1.
            Console.WriteLine("NOTE certificate/TLS checks run in-game (RunDual.ps1)");
            Console.WriteLine("SECURITY SUITE PASSED");return 0;
        }catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
