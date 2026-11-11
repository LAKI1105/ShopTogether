using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ShopTogether {
    [Serializable] internal sealed class PhoneView {
        public bool active;
        public string number = "", status = "", name = "", line = "";
        public bool player;
        public string[] choices; public bool[] enabled;
        public int sig;
    }

    /// <summary>
    /// The shop phone is one phone. Calls always run on the host (who answers, the conversation, orders and their
    /// consequences); anyone who picks up the phone sees the same number, status and conversation and can dial, talk
    /// and choose. While only friends are using it, the host's phone runs out of sight on the host's screen.
    /// </summary>
    internal static class PhoneShare {
        static readonly FieldInfo numberField = typeof(PhoneUIManager).GetField("currentNumber", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly HashSet<int> users = new HashSet<int>();
        static bool hostUsing, hidden;

        static bool Friend { get { return ReplicaSession.ClientStore && !WorldReplica.Applying; } }
        static bool Act { get { return ReplicaSession.IsReplica && LocalControl.Focused; } }
        static void Send(string name, params object[] args) { RemoteActions.Forward(RemoteActions.Adapter(name, args.Length), null, args); }

        // ---------------- host side ----------------

        static void Show(bool visible) {
            var phone = PhoneUIManager.Instance;
            if (phone == null || phone.phonePanel == null) return;
            var group = phone.phonePanel.GetComponent<CanvasGroup>() ?? phone.phonePanel.AddComponent<CanvasGroup>();
            group.alpha = visible ? 1f : 0f; group.blocksRaycasts = visible; group.interactable = visible;
            hidden = !visible;
        }
        /// <summary>The host's phone must be switched on for a call to run (its conversation is driven by the screen).</summary>
        static void Ready() {
            var phone = PhoneUIManager.Instance;
            if (phone == null || phone.phonePanel == null) return;
            if (!phone.phonePanel.activeSelf) {
                Show(hostUsing);
                phone.phonePanel.SetActive(true);
                phone.statusDisplay.text = LocHelper.GetLocalizedUI("ui_phone_ready");
            }
        }
        static bool User() { return users.Contains(RemoteActions.CurrentOwner); }

        internal static void Pick() { users.Add(RemoteActions.CurrentOwner); Ready(); }
        internal static void HangUp() {
            users.Remove(RemoteActions.CurrentOwner);
            var phone = PhoneUIManager.Instance;
            if (users.Count > 0 || hostUsing || phone == null || phone.phonePanel == null) return;
            phone.StopCall();
            phone.phonePanel.SetActive(false);
            Show(true);
        }
        internal static void Key(int number) {
            var phone = PhoneUIManager.Instance;
            if (phone == null || !User() || number < -1 || number > 9) return;
            Ready(); phone.PressNumber(number);
        }
        internal static void Dial(long number) {
            var phone = PhoneUIManager.Instance;
            if (phone == null || !User() || !StorePhoneClient.IsNumberExist(number)) return;
            Ready(); phone.AutoCall(number);
        }
        internal static void Advance() { if (User() && DialoguePhoneUIManager.Instance != null) DialoguePhoneUIManager.Instance.OnAdvancePressed(); }
        internal static void Choose(int index, int sig) {
            var talk = DialoguePhoneUIManager.Instance;
            if (!User() || talk == null || talk.currentDialog == null || sig != lastSig || !ChoicesShown(talk)
                || talk.currentDialog.choices == null || index < 0 || index >= talk.currentDialog.choices.Count
                || !talk.currentDialog.IsChoiceEnabledAt(index)) return;
            talk.SelectChoice(index);
        }

        static bool ChoicesShown(DialoguePhoneUIManager talk) {
            if (talk.dialogChoiceBubbles == null || talk.currentDialog == null || !talk.IsChoice()) return false;
            foreach (var b in talk.dialogChoiceBubbles) if (b != null && b.gameObject.activeInHierarchy) return true;
            return false;
        }

        static int lastSig;
        internal static void Capture(PhaseState state) {
            var phone = PhoneUIManager.Instance; var talk = DialoguePhoneUIManager.Instance;
            if (phone == null || phone.phonePanel == null || talk == null) return;
            var v = new PhoneView { active = phone.phonePanel.activeSelf };
            if (v.active) {
                long n = numberField != null ? (long)numberField.GetValue(phone) : 0;
                v.number = n != 0 ? n.ToString() : "";
                v.status = phone.statusDisplay != null ? phone.statusDisplay.text ?? "" : "";
                var d = talk.currentDialog;
                bool talking = talk.clientBubble != null && talk.playerBubble != null && (talk.clientBubble.gameObject.activeSelf || talk.playerBubble.gameObject.activeSelf);
                if (d != null && talking) {
                    v.line = Clip(d.plainText); v.player = d.title == "PLAYER";
                    v.name = phone.currentPhoneClient != null ? Clip(phone.currentPhoneClient.GetLocalizedDisplayName()) : "";
                    if (ChoicesShown(talk)) {
                        int count = Math.Min(d.choices.Count, talk.dialogChoiceBubbles.Count);
                        v.choices = new string[count]; v.enabled = new bool[count];
                        for (int i = 0; i < count; i++) { v.choices[i] = Clip(d.choices[i].Item2 != null ? d.choices[i].Item2.plainText : ""); v.enabled[i] = d.IsChoiceEnabledAt(i); }
                    }
                }
            }
            v.sig = Fingerprint.Hash(v.line + "\n" + v.player + "\n" + (v.choices != null ? string.Join("\n", v.choices) : "")).GetHashCode();
            lastSig = v.sig;
            state.phone = v;
        }
        static string Clip(string s) { s = s ?? ""; return s.Length > 400 ? s.Substring(0, 400) : s; }

        internal static void HostTick(Connection session) { if (users.RemoveWhere(id => !session.HasPeer(id)) > 0) PutDown(); }
        /// <summary>Host side: nobody is on the phone any more and the host never picked it up: hang up and put it away.</summary>
        static void PutDown() {
            var phone = PhoneUIManager.Instance;
            if (users.Count > 0 || hostUsing || !hidden || phone == null || phone.phonePanel == null) return;
            phone.StopCall();
            phone.phonePanel.SetActive(false);
            Show(true);
        }
        internal static void Reset() { users.Clear(); PutDown(); hostUsing = false; if (hidden) Show(true); shownSig = 0; }

        // ---------------- friend side ----------------

        static int shownSig;
        internal static void Follow(PhaseState s) {
            var phone = PhoneUIManager.Instance; var talk = DialoguePhoneUIManager.Instance; var v = s.phone;
            if (phone == null || talk == null || v == null || phone.phonePanel == null || !phone.phonePanel.activeSelf) return;
            long n; if (numberField != null) numberField.SetValue(phone, long.TryParse(v.number, out n) ? n : 0L);
            if (phone.statusDisplay != null && !string.IsNullOrEmpty(v.status)) phone.statusDisplay.text = v.status;
            if (v.sig == shownSig) return;
            shownSig = v.sig;
            bool was = WorldReplica.Applying; WorldReplica.Applying = true;
            try {
                talk.HideAllDialogUI();
                if (string.IsNullOrEmpty(v.line)) { talk.currentDialog = null; return; }
                var line = new Dialogue().SetText(v.player ? "PLAYER" : "", v.line);
                if (v.player) talk.DisplayerPlayerText(line);
                else {
                    talk.DisplayClientText(line);
                    if (!string.IsNullOrEmpty(v.name)) { talk.clientNameBubble.SetActive(true); talk.clientNameTMP.gameObject.SetActive(true); talk.clientNameTMP.text = "<i>" + v.name + "</i>"; }
                }
                if (v.choices != null && v.enabled != null && v.choices.Length > 0) {
                    var asked = new Dialogue().SetText(v.player ? "PLAYER" : "", v.line);
                    asked.choices = new List<ValueTuple<GameItem, Dialogue, Action>>();
                    for (int i = 0; i < v.choices.Length && i < v.enabled.Length; i++) { var c = new Dialogue().SetText("PLAYER", v.choices[i] ?? ""); c.forceDisabled = !v.enabled[i]; asked.choices.Add(new ValueTuple<GameItem, Dialogue, Action>(null, c, null)); }
                    talk.currentDialog = asked;
                    talk.DisplayChoice();
                } else talk.currentDialog = line;
            } catch (Exception e) { Debug.LogWarning("[ShopTogether] phone view: " + e.Message); }
            finally { WorldReplica.Applying = was; }
        }

        // ---------------- interception ----------------

        [HarmonyPatch(typeof(PhoneUIManager), "OpenUI")]
        internal static class OpenPhone {
            static void Postfix() {
                if (Friend) { if (Act) { shownSig = 0; Send("Pick"); } return; }
                var session = Connection.Current;
                if (session != null && session.IsHost) { hostUsing = true; if (hidden) Show(true); }
            }
        }
        [HarmonyPatch(typeof(PhoneUIManager), "CloseUI")]
        internal static class ClosePhone {
            static void Postfix() {
                if (Friend) { if (Act) Send("HangUp"); return; }
                var session = Connection.Current;
                if (session == null || !session.IsHost) return;
                hostUsing = false;
                // Friends still on the phone: it stays on, out of sight.
                if (users.Count > 0) Ready();
            }
        }
        /// <summary>Host side: number keys on the keyboard do not dial on a phone the host has not picked up.</summary>
        [HarmonyPatch(typeof(PhoneUIManager), "PressKeypad")]
        internal static class HiddenKeypad { static bool Prefix() { return !hidden; } }
        [HarmonyPatch(typeof(PhoneUIManager), "PressNumber")]
        internal static class PhoneKey {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(int number) { if (!Friend) return true; if (Act) Send("Key", number); return false; }
        }
        [HarmonyPatch(typeof(PhoneUIManager), "AutoCall")]
        internal static class PhoneDial {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(long number) { if (!Friend) return true; if (Act) Send("Dial", number); return false; }
        }
        [HarmonyPatch(typeof(DialoguePhoneUIManager), "OnAdvancePressed")]
        internal static class PhoneAdvance {
            [HarmonyPriority(Priority.First)]
            static bool Prefix() { if (!Friend) return true; if (Act) Send("Advance"); return false; }
        }
        [HarmonyPatch(typeof(DialoguePhoneUIManager), "SelectChoice")]
        internal static class PhoneChoice {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(DialoguePhoneUIManager __instance, int index) {
                if (!Friend) return true;
                if (Act) { Send("Choose", index, shownSig); __instance.HideChoiceBubble(); }
                return false;
            }
        }
        /// <summary>On a friend's screen the conversation only shows what the host's phone says; it never moves on by itself.</summary>
        [HarmonyPatch]
        internal static class PhoneFlow {
            static IEnumerable<MethodBase> TargetMethods() {
                foreach (var name in new[] { "OnCurrentTextDisplayed", "ContinueConversation", "EndConversation", "ResumeIfWaitingForAdvance" }) {
                    var m = typeof(DialoguePhoneUIManager).GetMethod(name);
                    if (m != null) yield return m;
                }
            }
            [HarmonyPriority(Priority.First)]
            static bool Prefix() { return !ReplicaSession.ClientStore; }
        }
    }
}
