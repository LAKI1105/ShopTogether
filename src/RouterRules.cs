using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Xml;

namespace ShopTogether {
    // Pure router rules shared by the in-game port mapper and the out-of-game test suite (no Unity types).
    internal sealed class GatewayRoute {
        internal IPAddress Local,Gateway,Mask;
        internal static List<GatewayRoute> Find(){
            var result=new List<GatewayRoute>();
            foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces()){
                if(adapter.OperationalStatus!=OperationalStatus.Up||adapter.NetworkInterfaceType==NetworkInterfaceType.Loopback)continue;
                var properties=adapter.GetIPProperties();
                foreach(var gateway in properties.GatewayAddresses)foreach(var local in properties.UnicastAddresses){
                    if(gateway.Address.AddressFamily!=AddressFamily.InterNetwork||local.Address.AddressFamily!=AddressFamily.InterNetwork||IPAddress.IsLoopback(gateway.Address)||gateway.Address.Equals(IPAddress.Any))continue;
                    bool duplicate=false;foreach(var existing in result)if(existing.Gateway.Equals(gateway.Address)&&existing.Local.Equals(local.Address)){duplicate=true;break;}
                    if(!duplicate)result.Add(new GatewayRoute{Local=local.Address,Gateway=gateway.Address,Mask=local.IPv4Mask});
                }
            }
            return result;
        }
        internal static bool IsLocalSubnet(IPAddress address){foreach(var route in Find()){byte[] a=address.GetAddressBytes(),b=route.Local.GetAddressBytes(),mask=route.Mask.GetAddressBytes();if(a.Length!=4)continue;bool same=true;for(int i=0;i<4;i++)if((a[i]&mask[i])!=(b[i]&mask[i]))same=false;if(same)return true;}return false;}
    }
    internal sealed class MappingStatus {
        internal IPEndPoint External;
        internal IPAddress Local;
        internal string Message;
    }
    internal static class RouterRules {
        // Discovery documents may only point back to the selected gateway. No redirects, DTD, DNS or proxy.
        internal static bool ValidGatewayUrl(Uri uri,IPAddress gateway){IPAddress host;return uri!=null&&uri.IsAbsoluteUri&&uri.Scheme=="http"&&uri.UserInfo.Length==0&&uri.Fragment.Length==0&&uri.AbsoluteUri.Length<=2048&&IPAddress.TryParse(uri.Host,out host)&&host.Equals(gateway);}
        internal static XmlDocument ParseXml(byte[] bytes){
            if(bytes==null||bytes.Length>131072)throw new InvalidDataException("Router document too large");
            var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=131072};
            using(var stream=new MemoryStream(bytes))using(var reader=XmlReader.Create(stream,settings)){var doc=new XmlDocument{XmlResolver=null};doc.Load(reader);return doc;}
        }
        internal static string Value(XmlNode node,string name){var child=node.SelectSingleNode("./*[local-name()='"+name+"']");return child==null?null:child.InnerText;}
        internal static byte[] NatPmpRequest(int inside,int outside,int seconds){if(inside<1||inside>65535||outside<0||outside>65535||seconds<0||seconds>3600)throw new ArgumentException();return new byte[]{0,2,0,0,(byte)(inside>>8),(byte)inside,(byte)(outside>>8),(byte)outside,(byte)(seconds>>24),(byte)(seconds>>16),(byte)(seconds>>8),(byte)seconds};}
        internal static bool NatPmpParseMapping(byte[] data,int inside,out int external,out int seconds){external=0;seconds=0;if(data==null||data.Length!=16||data[0]!=0||data[1]!=130||data[2]!=0||data[3]!=0||((data[8]<<8)|data[9])!=inside)return false;external=(data[10]<<8)|data[11];uint lifetime=((uint)data[12]<<24)|((uint)data[13]<<16)|((uint)data[14]<<8)|data[15];if(external==0||lifetime>3600)return false;seconds=(int)lifetime;return true;}
    }
}
