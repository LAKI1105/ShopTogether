using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace ShopTogether {
    internal static class DirectConnection {
        internal static Socket Listen(int preferredPort,bool automatic){
            for(int attempt=0;;attempt++){
                var socket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);socket.ExclusiveAddressUse=true;
                try{socket.Bind(new IPEndPoint(IPAddress.Any,attempt==0?preferredPort:0));socket.Listen(3);socket.Blocking=false;return socket;}
                catch(SocketException error){socket.Close();if(!automatic||attempt>0||error.SocketErrorCode!=SocketError.AddressAlreadyInUse)throw;}
                catch{socket.Close();throw;}
            }
        }
        internal static bool Parse(string address, string portText, out IPEndPoint endpoint, out string error) {
            endpoint = null; error = null;
            string target = (address ?? "").Trim().Replace('：', ':');
            int separator = target.LastIndexOf(':');
            if (separator >= 0) { portText = target.Substring(separator + 1).Trim(); target = target.Substring(0, separator).Trim(); }
            int port;
            if (!int.TryParse(portText, out port) || port < 1 || port > 65535) { error = "~SMC130~"; return false; }
            IPAddress ip;
            if (!IPAddress.TryParse(target, out ip) || ip.AddressFamily != AddressFamily.InterNetwork || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast)) {
                error = "~SMC165~"; return false;
            }
            endpoint = new IPEndPoint(ip, port); return true;
        }
    }
    // Same public-address service used by the existing independent server launcher.
}
