using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ShopTogether;

internal static class RoomSetupTests {
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
    internal static void Run(){
        var seed=new Invite{Local=new IPEndPoint(IPAddress.Parse("192.168.1.101"),7777),External=new IPEndPoint(IPAddress.Parse("8.8.8.8"),50123),Fingerprint=new string('A',64),Password="room-test-only-not-a-real-secret"};
        Invite decoded;string error;string code=InviteCode.Encode(seed);
        Check(InviteCode.TryDecode(code,out decoded,out error)&&decoded.Local.Equals(seed.Local)&&decoded.External.Equals(seed.External)&&decoded.Fingerprint==seed.Fingerprint&&decoded.Password==seed.Password,"invite carries distinct internal/external ports, host identity and credential");
        seed.SteamId=76561198120456732UL;Check(InviteCode.TryDecode(InviteCode.Encode(seed),out decoded,out error)&&decoded.SteamId==seed.SteamId,"invite carries the host Steam account for relay connections");
        var groupSeed=new Invite{Local=seed.Local,Fingerprint=seed.Fingerprint,Password=seed.Password,SteamId=103582791429521412UL};Check(!InviteCode.TryDecode(InviteCode.Encode(groupSeed),out decoded,out error),"non-person Steam ids (groups, servers) are refused");
        seed.SteamId=0;
        seed.External=null;Check(InviteCode.TryDecode(InviteCode.Encode(seed),out decoded,out error)&&decoded.External==null,"LAN invitation stays explicitly LAN-only");
        int index=20;string broken=code.Substring(0,index)+(code[index]=='A'?'B':'A')+code.Substring(index+1);
        Check(!InviteCode.TryDecode(broken,out decoded,out error)&&!InviteCode.TryDecode(new string('A',10000),out decoded,out error)&&!InviteCode.TryDecode("https://example.com/run",out decoded,out error),"corrupt, oversized and command-like invitations rejected");
        Check(!InviteCode.IsPublic(IPAddress.Parse("100.100.1.1"))&&!InviteCode.IsPublic(IPAddress.Parse("192.168.1.1"))&&!InviteCode.IsPublic(IPAddress.Parse("127.0.0.1"))&&!InviteCode.IsPublic(IPAddress.Parse("239.1.1.1"))&&InviteCode.IsPublic(IPAddress.Parse("8.8.8.8")),"CGNAT/private/loopback/multicast not advertised as public");
        using(var occupied=DirectConnection.Listen(0,false)){
            int port=((IPEndPoint)occupied.LocalEndPoint).Port;
            using(var auto=DirectConnection.Listen(port,true)){Check(((IPEndPoint)auto.LocalEndPoint).Port!=port,"one-click hosting selects another available port when occupied");}
            bool rejected=false;try{using(var duplicate=DirectConnection.Listen(port,false)){}}catch(SocketException){rejected=true;}Check(rejected,"manual port conflict is explicit");
        }
        var gateway=IPAddress.Parse("192.168.1.1");
        Check(RouterRules.ValidGatewayUrl(new Uri("http://192.168.1.1:5000/control"),gateway)&&!RouterRules.ValidGatewayUrl(new Uri("http://example.com/control"),gateway)&&!RouterRules.ValidGatewayUrl(new Uri("http://127.0.0.1/control"),gateway)&&!RouterRules.ValidGatewayUrl(new Uri("file:///C:/Windows/win.ini"),gateway)&&!RouterRules.ValidGatewayUrl(new Uri("http://user:password@192.168.1.1/control"),gateway),"router discovery cannot redirect requests to DNS, files or unrelated hosts");
        bool xmlRejected=false;try{RouterRules.ParseXml(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY p SYSTEM 'file:///C:/Windows/win.ini'>]><x>&p;</x>"));}catch(XmlException){xmlRejected=true;}Check(xmlRejected,"router XML external entities disabled");
        byte[] mapping={0,130,0,0,0,0,0,1,30,97,195,80,0,0,14,16};int outside,seconds;
        Check(RouterRules.NatPmpParseMapping(mapping,7777,out outside,out seconds)&&outside==50000&&seconds==3600,"NAT-PMP actual assigned port and lifetime honored");
        Check(!RouterRules.NatPmpParseMapping(mapping,7778,out outside,out seconds),"NAT-PMP reply cannot target another internal port");
        var remove=RouterRules.NatPmpRequest(7777,0,0);Check(remove[4]==30&&remove[5]==97&&remove[8]==0&&remove[11]==0,"NAT-PMP cleanup targets only this room internal port");
        // UPnP lease behaviour (alternate port, renew, owned-only cleanup, permanent-rule refusal) runs in-game: see DualRuntimeProbe RouterCheck.
        var book=new LeaseBook((a,b)=>a==100&&b==101||a==101&&b==100);long first,other;
        Check(book.Claim(1,new[]{100},0,out first)&&!book.Claim(2,new[]{101},0,out other)&&book.Claim(2,new[]{200},0,out other),"first owner wins for item/container while separate items remain concurrent");
        Check(book.Owns(1,100,first,1)&&!book.Owns(2,100,first,1)&&!book.Owns(1,100,first+1,1),"lease owner and token both enforced");
        Check(!book.Claim(3,new[]{300,100},1,out other)&&book.Available(4,300,1),"multi-item claim fails atomically");
        book.Disconnect(1);Check(book.Claim(3,new[]{101},2,out other)&&book.Available(4,200,21),"disconnect and expired lease release control");
    }
}
