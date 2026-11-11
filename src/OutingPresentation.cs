using HarmonyLib;

namespace ShopTogether {
    // Leaving the shop hides local windows, but its native helper also deletes front-counter
    // merchandise. Replica scene changes must never delete the host's replicated stock.
    [HarmonyPatch(typeof(EmporiumEntry), "ClearFrontInv")]
    internal static class ReplicaKeepsCounterStock {
        [HarmonyPriority(Priority.First)]
        static bool Prefix() { return !ReplicaSession.ClientStore; }
    }
}
