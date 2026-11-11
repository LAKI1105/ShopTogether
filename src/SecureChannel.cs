using System;
using System.Collections;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks; // ReadAsync/WriteAsync results only; no background tasks are started.

namespace ShopTogether {
    internal sealed class RoomIdentity : IDisposable {
        internal readonly X509Certificate2 Certificate;
        internal readonly string Fingerprint;
        internal RoomIdentity() {
            // Use the certificate builder already bundled with Unity's Mono runtime.
            // CertificateRequest in this game's System.dll is an unsupported stub.
            using (var rsa = new RSACryptoServiceProvider(2048)) {
                rsa.PersistKeyInCsp = false;
                byte[] serial = new byte[16];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(serial);
                serial[0] &= 0x7f; serial[0] |= 1;
                var builder = new Mono.Security.X509.X509CertificateBuilder(3) {
                    SerialNumber = serial, SubjectName = "CN=SharedShop", IssuerName = "CN=SharedShop",
                    NotBefore = DateTime.UtcNow.AddMinutes(-5), NotAfter = DateTime.UtcNow.AddDays(7),
                    SubjectPublicKey = rsa, Hash = "SHA256"
                };
                byte[] der = builder.Sign(rsa);
                // Mono distinguishes an unset PKCS#12 password from an empty password.
                // Use the same explicit transient password for both encryption and import.
                string transportPassword = Convert.ToBase64String(serial);
                var bag = new Mono.Security.X509.PKCS12 { Password = transportPassword };
                var attributes = new Hashtable();
                attributes.Add("1.2.840.113549.1.9.21", new ArrayList { new byte[] { 1 } });
                bag.AddCertificate(new Mono.Security.X509.X509Certificate(der), attributes);
                bag.AddPkcs8ShroudedKeyBag(rsa, attributes);
                byte[] pfx = bag.GetBytes();
                // Do not set PersistKeySet or install a certificate in a trust store.
                // Schannel requires a transient key container, released with Certificate.Dispose().
                try { Certificate = new X509Certificate2(pfx, transportPassword, X509KeyStorageFlags.DefaultKeySet); }
                finally { Array.Clear(pfx, 0, pfx.Length); }
            }
            if (!Certificate.HasPrivateKey) throw new CryptographicException("~SMC146~");
            Fingerprint = SecureChannel.Fingerprint(Certificate);
        }
        public void Dispose() { Certificate.Dispose(); }
    }

    /// <summary>
    /// One connection, whatever carries it: TLS over a direct TCP socket, or Steam's peer-to-peer network.
    /// The game protocol above it (length-prefixed packets, password, protocol guard) is the same for both.
    /// </summary>
    internal interface IChannel : IDisposable {
        bool Ready { get; }
        bool Writing { get; }
        int Receive(byte[] destination);   // -1 nothing yet, 0 closed, otherwise bytes copied
        void Send(byte[] packet);
    }

    internal sealed class SecureChannel : IChannel {
        readonly Socket socket;
        readonly SslStream stream;
        readonly IAsyncResult handshake;
        bool handshakeChecked;
        readonly bool isServer;
        Task<int> read;
        Task write;
        readonly byte[] input = new byte[32768];
        bool disposed;
        internal SecureChannel(Socket socket, X509Certificate2 serverCertificate, string expectedFingerprint) {
            this.socket = socket; socket.Blocking = true; socket.NoDelay = true;
            socket.ReceiveTimeout = 12000; socket.SendTimeout = 12000;
            bool server = serverCertificate != null;
            string expected = NormalizeFingerprint(expectedFingerprint);
            if (!server && expected == null) throw new AuthenticationException("~SMC166~");
            stream = new SslStream(new NetworkStream(socket, false), false,
                delegate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) {
                    if (server) return certificate == null; // Clients authenticate with the room password inside TLS.
                    return certificate != null && string.Equals(Fingerprint(certificate), expected, StringComparison.Ordinal);
                });
            // The TLS handshake runs through the socket's own asynchronous I/O and is polled from the game's
            // main thread (Ready). The mod starts no threads or tasks of its own.
            isServer = server;
            handshake = server
                ? stream.BeginAuthenticateAsServer(serverCertificate, false, SslProtocols.Tls12, false, null, null)
                : stream.BeginAuthenticateAsClient("SharedShop", null, SslProtocols.Tls12, false, null, null);
        }
        public bool Ready {
            get {
                if (disposed) return false;
                if (!handshake.IsCompleted) return false;
                if (!handshakeChecked) {
                    handshakeChecked = true;
                    if (isServer) stream.EndAuthenticateAsServer(handshake); else stream.EndAuthenticateAsClient(handshake);
                    if (!stream.IsEncrypted || !stream.IsSigned) throw new AuthenticationException("~SMC133~");
                }
                return true;
            }
        }
        public int Receive(byte[] destination) {
            if (!Ready) return -1;
            if (read == null) read = stream.ReadAsync(input, 0, input.Length);
            if (!read.IsCompleted) return -1;
            int count = read.GetAwaiter().GetResult(); read = null;
            Buffer.BlockCopy(input, 0, destination, 0, count); return count;
        }
        public bool Writing {
            get { if (write == null) return false; if (!write.IsCompleted) return true; write.GetAwaiter().GetResult(); write = null; return false; }
        }
        public void Send(byte[] packet) {
            if (!Ready || Writing) throw new InvalidOperationException("~SMC041~");
            write = stream.WriteAsync(packet, 0, packet.Length);
        }
        internal static string NormalizeFingerprint(string value) {
            if (string.IsNullOrEmpty(value)) return null;
            string normalized = value.Trim().Replace("-", "").Replace(" ", "").ToUpperInvariant();
            if (normalized.Length != 64) return null;
            foreach (char c in normalized) if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))) return null;
            return normalized;
        }
        internal static string Fingerprint(X509Certificate certificate) {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(certificate.GetRawCertData())).Replace("-", "");
        }
        public void Dispose() {
            if (disposed) return; disposed = true;
            // Closing the socket interrupts pending handshake/read/write operations.
            try { socket.Close(); } catch { }
            try { stream.Dispose(); } catch { }
            if (!handshakeChecked && handshake.IsCompleted) try { if (isServer) stream.EndAuthenticateAsServer(handshake); else stream.EndAuthenticateAsClient(handshake); } catch { }
            if (read != null) try { read.GetAwaiter().GetResult(); } catch { }
            if (write != null) try { write.GetAwaiter().GetResult(); } catch { }
        }
    }
}
