using System;
using System.Collections.Generic;

namespace ShopTogether {
    internal sealed class ProtocolGuard {
        double tokens = 800, last;
        bool started;
        internal bool Admit(double seconds) {
            if (!started) { last = seconds; started = true; }
            tokens = Math.Min(800, tokens + Math.Max(0, seconds - last) * 400); last = seconds;
            if (tokens < 1) return false; tokens--; return true;
        }
        internal static int MaxLength(bool host, bool authenticated) { return !authenticated ? 257 : host ? 65537 : 4 * 1024 * 1024 + 1; }
        internal static bool Valid(bool host, bool authenticated, bool versionMatched, byte type, int length) {
            if (!authenticated) {
                if (type == 16) return !versionMatched && length > 0 && length <= 64;
                if (host) return versionMatched && type == 1 && length >= 8 && length <= 256;
                if (type == 17 || versionMatched && type == 15) return length == 0;
                return versionMatched && type == 2 && length == 4;
            }
            if (host) return type == 24 && length == 0 || type == 21 && length > 0 && length <= 65536
                || type == 30 && length > 0 && length <= 16384 || type == 33 && length > 0 && length <= 16384 || type == 36 && length > 0 && length <= 65536 || type == 38 && length == 8 || type == 41 && length == 1;
            return type == 20 && length > 0 && length <= 4 * 1024 * 1024 || type == 22 && length > 0 && length <= 4096
                || type == 31 && length > 0 && length <= 32768 || type == 32 && length > 0 && length <= 4 * 1024 * 1024
                || type == 34 && length > 0 && length <= 65536 || type == 39 && length >= 4 && length <= 4 + 12 * 8 || type == 40 && length > 0 && length <= 4096
                || type == 26 && length > LargeTransfer.Header && length <= LargeTransfer.Header + LargeTransfer.Chunk || type == 37 && length > 0 && length <= 1024 || type == 42 && length > 0 && length <= 512 || type == 43 && length > 0 && length <= 16384;
        }
        internal static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
    internal sealed class AdmissionGuard {
        sealed class Entry { internal double last, tokens = 8; }
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        internal bool Admit(string address, double now) {
            foreach (var pair in new List<KeyValuePair<string, Entry>>(entries)) if (now - pair.Value.last > 120) entries.Remove(pair.Key);
            Entry entry;
            if (!entries.TryGetValue(address, out entry)) {
                if (entries.Count >= 128) return false;
                entry = new Entry { last = now }; entries.Add(address, entry);
            }
            entry.tokens = Math.Min(8, entry.tokens + Math.Max(0, now - entry.last) / 10); entry.last = now;
            if (entry.tokens < 1) return false; entry.tokens--; return true;
        }
    }
}
