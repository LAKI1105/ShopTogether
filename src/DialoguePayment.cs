using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ShopTogether {
    // These native stories quote funds before a later dialogue line deducts them.
    // Keep their original amounts and effects; serialize competing spending until
    // the host finishes that conversation instead of implementing its accounting.
    internal static class DialoguePayment {
        static bool PaymentCallback(Delegate callback) {
            if (callback == null) return false;
            foreach (var action in callback.GetInvocationList()) {
                var type = action.Method.DeclaringType;
                while (type != null && type.IsNested) type = type.DeclaringType;
                string name = action.Method.Name;
                if (type == typeof(StoreClientUniqueList) && name.StartsWith("<CreateGeorgioLoan>", StringComparison.Ordinal) ||
                    type != null && type.FullName == "NpcWeekly.Eleanor" && name.StartsWith("<CreateScav>", StringComparison.Ordinal) ||
                    type != null && type.FullName == "MercDispatch.Visits" && name.StartsWith("<AddBriefChoices>", StringComparison.Ordinal)) return true;
            }
            return false;
        }
        static bool ContainsPayment(Dialogue line, HashSet<Dialogue> seen) {
            if (line == null || !seen.Add(line)) return false;
            if (PaymentCallback(line.startAction) || !line.isEndActionExecuted && PaymentCallback(line.endAction)) return true;
            if (line.choices != null) foreach (var choice in line.choices)
                if (PaymentCallback(choice.Item3) || ContainsPayment(choice.Item2, seen)) return true;
            return ContainsPayment(line.nextDialogue, seen);
        }
        internal static bool Pending {
            get {
                var s = Connection.Current; var ui = DialogUIManager.Instance;
                if (s == null || !s.IsHost || !s.Ready || WorldReplica.Applying || ui == null) return false;
                var seen = new HashSet<Dialogue>();
                if (ContainsPayment(ui.currentDialog, seen)) return true;
                var lines = ModReflect.Get<List<Dialogue>>(ui, "scheduledEndActionLines");
                if (lines != null) foreach (var line in lines) if (ContainsPayment(line, seen)) return true;
                return false;
            }
        }
        internal static bool AllowSpending() {
            bool visitor = ProbablyMultiplayerCompat.TradeLocked;
            if (!visitor && !Pending) return true;
            RemoteActions.PendingNote = visitor ? "~SMC271~" : "~SMC364~";
            if (RemoteActions.CurrentOwner < 0) RemoteActions.Notice(RemoteActions.PendingNote);
            return false;
        }
    }
    [HarmonyPatch(typeof(CurrentConverterHelper), "CanSupply")]
    internal static class ConverterPaymentGate {
        // Supply checks this before deducting cash. A blocked machine does not run.
        static void Postfix(ref bool __result) { if (__result && (DialoguePayment.Pending || ProbablyMultiplayerCompat.TradeLocked)) __result = false; }
    }
}
