// Standalone transport tests compile the real diagnostics constants without loading Unity.
namespace UnityEngine {
    internal enum LogType { Error, Exception }
    internal static class Application {
        internal static event System.Action<string,string,LogType> logMessageReceived;
    }
    internal static class Time { internal static float realtimeSinceStartup { get { return 0; } } }
    internal static class Debug { internal static void LogWarning(object value) {} }
}
namespace ShopTogether {
    internal sealed class Connection {
        internal static Connection Current { get { return null; } }
        internal bool IsClient { get { return false; } }
        internal bool IsHost { get { return false; } }
        internal void SendToHost(byte type,byte[] data) {}
    }
    internal static class IssueReport {
        internal static void Add(string text) {}
        internal static string Sanitize(string text) { return text; }
    }
}
