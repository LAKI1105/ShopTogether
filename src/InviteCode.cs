using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ShopTogether {
    internal sealed class Invite {
        internal IPEndPoint Local, External;
        internal string Fingerprint, Password;
        internal ulong SteamId;   // host Steam account for relay connections; 0 = none (LAN-only room or Steam offline)
    }
    internal static class InviteCode {
        internal const string Prefix="SMC1-";
        // The code is a room credential, not a URL or executable command. Never log it.
        internal static string Encode(Invite invite){
            if(invite==null||invite.Local==null||!ValidEndpoint(invite.Local))throw new ArgumentException("Invalid local endpoint");
            if(invite.External!=null&&(!ValidEndpoint(invite.External)||!IsPublic(invite.External.Address)))throw new ArgumentException("Invalid public endpoint");
            byte[] password=new UTF8Encoding(false,true).GetBytes(invite.Password??"");
            if(password.Length<8||password.Length>256||invite.Password.Length>64)throw new ArgumentException("Invalid password");
            byte[] key=Hex(invite.Fingerprint);
            using(var output=new MemoryStream())using(var writer=new BinaryWriter(output)){
                writer.Write((byte)2);WriteEndpoint(writer,invite.Local);writer.Write(invite.External!=null);
                if(invite.External!=null)WriteEndpoint(writer,invite.External);
                writer.Write(key);writer.Write(invite.SteamId);writer.Write((ushort)password.Length);writer.Write(password);writer.Flush();
                byte[] payload=output.ToArray();using(var sha=SHA256.Create()){byte[] hash=sha.ComputeHash(payload);writer.Write(hash,0,4);writer.Flush();}
                return Prefix+Convert.ToBase64String(output.ToArray()).TrimEnd('=').Replace('+','-').Replace('/','_');
            }
        }
        internal static bool TryDecode(string text,out Invite invite,out string error){
            invite=null;error="~SMC138~";
            if(text==null)return false;text=text.Trim();
            if(text.Length>512||!text.StartsWith(Prefix,StringComparison.Ordinal))return false;
            string encoded=text.Substring(Prefix.Length);
            foreach(char c in encoded)if(!(c>='A'&&c<='Z'||c>='a'&&c<='z'||c>='0'&&c<='9'||c=='-'||c=='_'))return false;
            try{
                string padded=encoded.Replace('-','+').Replace('_','/');padded=padded.PadRight((padded.Length+3)/4*4,'=');byte[] bytes=Convert.FromBase64String(padded);
                if(bytes.Length<54||bytes.Length>318)return false;
                using(var sha=SHA256.Create()){byte[] hash=sha.ComputeHash(bytes,0,bytes.Length-4);int mismatch=0;for(int i=0;i<4;i++)mismatch|=hash[i]^bytes[bytes.Length-4+i];if(mismatch!=0)return false;}
                using(var input=new MemoryStream(bytes,0,bytes.Length-4,false))using(var reader=new BinaryReader(input)){
                    byte version=reader.ReadByte();if(version!=1&&version!=2){error="~SMC137~";return false;}
                    var result=new Invite{Local=ReadEndpoint(reader)};byte external=reader.ReadByte();if(external>1)return false;
                    if(external==1){result.External=ReadEndpoint(reader);if(!IsPublic(result.External.Address))return false;}
                    byte[] key=reader.ReadBytes(32);if(key.Length!=32)return false;result.Fingerprint=BitConverter.ToString(key).Replace("-","");
                    if(version==2){result.SteamId=reader.ReadUInt64();if(result.SteamId!=0&&!((result.SteamId>>56)==1&&((result.SteamId>>52)&0xF)==1))return false;}
                    int length=reader.ReadUInt16();if(length<8||length>256||input.Length-input.Position!=length)return false;
                    result.Password=new UTF8Encoding(false,true).GetString(reader.ReadBytes(length));if(result.Password.Length>64)return false;
                    invite=result;error=null;return true;
                }
            }catch(ArgumentException){return false;}catch(IOException){return false;}catch(FormatException){return false;}
        }
        internal static bool ValidEndpoint(IPEndPoint endpoint){
            if(endpoint==null||endpoint.Port<1||endpoint.Port>65535||endpoint.Address.AddressFamily!=AddressFamily.InterNetwork)return false;
            byte[] ip=endpoint.Address.GetAddressBytes();return ip[0]>0&&ip[0]<224&&!endpoint.Address.Equals(IPAddress.Broadcast);
        }
        internal static bool IsPublic(IPAddress address){
            if(address==null||address.AddressFamily!=AddressFamily.InterNetwork)return false;byte[] b=address.GetAddressBytes();
            return b[0]!=0&&b[0]!=10&&b[0]!=127&&b[0]<224&&!(b[0]==100&&b[1]>=64&&b[1]<=127)&&!(b[0]==169&&b[1]==254)&&!(b[0]==172&&b[1]>=16&&b[1]<=31)&&!(b[0]==192&&(b[1]==168||b[1]==0&&b[2]<=2))&&!(b[0]==198&&(b[1]==18||b[1]==19||b[1]==51&&b[2]==100))&&!(b[0]==203&&b[1]==0&&b[2]==113);
        }
        static byte[] Hex(string value){if(value==null||value.Length!=64)throw new ArgumentException("Invalid host identity");var bytes=new byte[32];for(int i=0;i<bytes.Length;i++)bytes[i]=Convert.ToByte(value.Substring(i*2,2),16);return bytes;}
        static void WriteEndpoint(BinaryWriter writer,IPEndPoint endpoint){writer.Write(endpoint.Address.GetAddressBytes());writer.Write((ushort)endpoint.Port);}
        static IPEndPoint ReadEndpoint(BinaryReader reader){byte[] ip=reader.ReadBytes(4);if(ip.Length!=4)throw new EndOfStreamException();var endpoint=new IPEndPoint(new IPAddress(ip),reader.ReadUInt16());if(!ValidEndpoint(endpoint))throw new FormatException();return endpoint;}
    }
}
