using System;
using System.Diagnostics;
using System.Text;
using HarmonyLib;

namespace ShopTogether {
    // Observe the native classifier only after it actually fails. Do not replace
    // its result, change liquid quantities, or invent a water category for zero.
    [HarmonyPatch(typeof(WaterFeatureHelper), "GetWaterIndex")]
    internal static class WaterDiagnostics {
        static void Postfix(GameItem gameItem, int __result) {
            var session = Connection.Current;
            if (__result >= 0 || gameItem == null || session == null || (!session.IsHost && !session.IsClient)) return;
            try {
                string caller = Caller();
                int water = gameItem.GetTagReadonly(Liquid.GetCurrentPartFromId("water")).GetInt();
                int total = WaterHelper.GetTotalVolume(gameItem);
                Diagnostics.Report("water classification", new InvalidOperationException(
                    "caller=" + caller + "; item=" + gameItem.identifier + "; water=" + water +
                    "; total=" + total + "; container=" + gameItem.IsTag("LIQUID_CONTAINER_TAG") +
                    "; applying=" + WorldReplica.Applying));
            } catch (Exception error) { Diagnostics.Note("water classification context", error.GetType().Name); }
        }
        static string Caller() {
            var result = new StringBuilder();
            foreach (var frame in new StackTrace(1, false).GetFrames()) {
                var method = frame.GetMethod(); var type = method == null ? null : method.DeclaringType;
                if (type == null || type == typeof(WaterDiagnostics) || method.Name.Contains("GetWaterIndex")) continue;
                string space = type.Namespace ?? "";
                if (space.StartsWith("System", StringComparison.Ordinal) || space.StartsWith("Harmony", StringComparison.Ordinal)) continue;
                string name = type.Assembly.GetName().Name + ":" + type.Name + "." + method.Name;
                if (result.Length > 0) result.Append(" <- ");
                result.Append(name);
                if (result.Length >= 110) break;
            }
            if (result.Length > 150) result.Length = 150;
            return result.Length == 0 ? "unavailable" : result.ToString();
        }
    }
}
