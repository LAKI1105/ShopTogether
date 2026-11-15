using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// A player action pressed on a friend's machine is never executed there. It is encoded as
    /// "which game function + which items + plain values", sent to the host, validated, executed by the
    /// host's own game code, and the result comes back through world sync. Only functions listed here are
    /// forwarded; every other world change on a friend's machine stays blocked by the replica barriers.
    /// </summary>
    [Serializable] internal sealed class RemoteCall { public long sequence, customer; public int offerSet = -1; public string key; public string instance; public string[] kinds; public string[] values; }

    internal static class RemoteActions {
        internal sealed class Entry {
            internal string Key;
            internal MethodInfo Method;
            internal ParameterInfo[] Parameters;
            internal Func<object, string> InstanceKey;     // friend side: identify the target object
            internal int MaxString = 200;                  // longest text argument accepted
            internal Func<string, object> ResolveInstance; // host side: find the same object
            internal string HostOnly;                       // non-null: friends are told only the host can do this
            internal Func<string> Guard;                    // host side: the same checks the game's own button does, null = allowed
        }

        const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        static readonly Dictionary<string, Entry> byKey = new Dictionary<string, Entry>();
        static readonly Dictionary<MethodBase, Entry> byMethod = new Dictionary<MethodBase, Entry>();
        internal static int Forwarded, Refused;
        /// <summary>Host side: the player whose action is running right now (-1 outside a forwarded action).</summary>
        internal static int CurrentOwner = -1;
        /// <summary>Host side: a message for the friend whose action just ran (sent with the result).</summary>
        internal static string PendingNote;
        internal static string TakeNote() { string n = PendingNote; PendingNote = null; if (n != null && n.Length > 120) n = n.Substring(0, 120); return n; }
        // Host: the same conditions the dismiss button and the alarm button check in the base game.
        internal static string DismissGuard() {
            var s = PlayerStore.Instance;
            if (s == null || !s.IsClientPresent()) return "~SMC231~";
            if (s.lockNextBellFlag > 0 || !(s.IsClientDimissable() || s.devMode)) return "~SMC302~";
            return null;
        }
        // The bell sends the customer away, or calls the next one; when it would do nothing, say why.
        static string BellGuard() {
            var s = PlayerStore.Instance;
            if (s == null || s.lockNextBellFlag > 0 || s.storeState != PlayerStore.StoreState.OPEN) return "~SMC125~";
            if (s.IsClientPresent()) return s.IsClientDimissable() || s.devMode ? null : "~SMC302~";
            return s.CanNextClient() ? null : "~SMC308~";
        }
        internal static string AlarmGuard() {
            var s = PlayerStore.Instance;
            if (s == null || !s.IsClientPresent()) return "~SMC231~";
            if (StoreReputation.IsPerkUnlocked("SEC_ALARM_IGNORED")) return "~SMC125~";
            if (s.isClientBeingArrested || s.isHandlingShootout || s.currentClientInstance.GetClientBlueprint().reported) return "~SMC337~";
            var button = UnityEngine.Object.FindObjectOfType<StoreSecButton>();
            if (button == null || !button.IsClientArrestable()) return "~SMC337~";
            return null;
        }
        static string AtCommissary() { return OutingKits.HostAt("commissary") ? null : "~SMC214~"; }
        internal static string LastNote = "";
        internal static float NoteUntil;

        static Connection Session { get { return Connection.Current; } }

        /// <summary>Finds one method by name; a missing or overloaded name yields null so a game update never breaks loading.</summary>
        static MethodInfo M(Func<MethodInfo> find) {
            try { return find(); } catch (AmbiguousMatchException) { return null; }
        }

        static void Add(MethodInfo method, Func<object, string> instanceKey = null, Func<string, object> resolve = null, string hostOnly = null, Func<string> guard = null) {
            if (method == null) return; // renamed in a game update: simply not forwarded
            var e = new Entry { Key = method.DeclaringType.Name + "." + method.Name + "/" + method.GetParameters().Length, Method = method, Parameters = method.GetParameters(), InstanceKey = instanceKey, ResolveInstance = resolve, HostOnly = hostOnly, Guard = guard };
            if (byKey.ContainsKey(e.Key)) return;
            byKey.Add(e.Key, e); byMethod.Add(method, e);
        }
        static Func<object, string> Singleton() { return o => "-"; }
        /// <summary>A ShopTogether helper executed on the host but never intercepted (the friend side calls Forward).</summary>
        static void AddAdapter(MethodInfo method, int maxString = 200) {
            if (method == null) return;
            var e = new Entry { Key = "ShopTogether." + method.Name + "/" + method.GetParameters().Length, Method = method, Parameters = method.GetParameters(), MaxString = maxString };
            if (!byKey.ContainsKey(e.Key)) byKey.Add(e.Key, e);
        }
        internal static Entry Adapter(string name, int parameters) { Build(); Entry e; return byKey.TryGetValue("ShopTogether." + name + "/" + parameters, out e) ? e : null; }

        /// <summary>Friend side: send an action to the host. Returns false when it could not be expressed.</summary>
        internal static bool Forward(Entry e, object instance, object[] args) {
            var session = Session;
            if (session == null || !session.IsClient) return false;
            // A friend's click that cannot be sent says so instead of doing nothing.
            if (!session.Ready) { Notice("~SMC242~"); return false; }
            if (e == null) { Refused++; Notice("~SMC128~"); return false; }
            var call = Encode(e, instance, args);
            if (call == null) { Refused++; Notice("~SMC128~"); return true; }
            session.SendRemote(call);
            return true;
        }

        internal static IEnumerable<MethodBase> Targets() { Build(); foreach (var m in byMethod.Keys) yield return m; }

        static bool built;
        static void Build() {
            if (built) return; built = true;

            // --- machines, repairs and item tools: every argument is an item, an inventory or a plain value ---
            // Repairs are effects of the host-validated item Target action. Do not
            // expose their lower-level helpers, which assume material/type checks ran.
            Add(M(() => typeof(MachineAutoButcher).GetMethod("ButcherContent", BF)));
            // The butcher's copy/paste: copying fills each player's own clipboard; pasting sends the copied
            // settings (MachineButtonPress), so neither is forwarded as it is.
            Add(M(() => typeof(MachineAutoButcher).GetMethod("Run", BF)));
            Add(M(() => typeof(MachineCassettePlayer).GetMethod("OnNextButtonPressed", BF)));
            Add(M(() => typeof(MachineCassettePlayer).GetMethod("OnPlayButtonPressed", BF)));
            Add(M(() => typeof(MachineCassettePlayer).GetMethod("OnStopButtonPressed", BF)));
            Add(M(() => typeof(MachineDealMaker).GetMethod("TryActivate", BF)));
            Add(M(() => typeof(MachineFeedDispenser).GetMethod("Blend", BF)));
            Add(M(() => typeof(MachineFeedDispenser).GetMethod("EmptyStorage", BF)));
            Add(M(() => typeof(HopperHelper).GetMethod("RunAndRecord", BF)));
            Add(M(() => typeof(ScratcherHelper).GetMethod("MarkSymbolScratched", BF)));
            AddAdapter(M(() => typeof(ScratchInteraction).GetMethod("Start", BF)));
            Add(M(() => typeof(StorageExpansionHelper).GetMethod("SetTabName", BF)));
            Add(M(() => typeof(VendingMachineHelper).GetMethod("TransferCashToPlayer", BF)));
            // VendingMachineHelper.UnlockVendingMachine is a host dialogue/upgrade effect, not a standalone request.
            // VendingFountainHelper.UnlockVendingFountain is a host dialogue/upgrade effect, not a standalone request.

            // --- store-level player actions: the target object is a singleton both machines have ---
            Add(M(() => typeof(PlayerStore).GetMethod("TryPlayerDismissCurrentClient", BF)), Singleton(), k => PlayerStore.Instance, null, DismissGuard);
            // These are dialogue effects, not independent player intents. The host's
            // current, enabled dialogue choice invokes them; exposing the raw methods
            // would bypass mortgage eligibility and permit repeated deductions.
            // The commissary: everyone is there with the host; what is bought lands on the host's outing ground.
            Add(M(() => typeof(CommissaryData).GetMethod("BuyItem", BF)), Singleton(), k => PlayerStore.Instance != null ? PlayerStore.Instance.commissaryData : null, null, AtCommissary);
            Add(M(() => typeof(CommissaryUIManager).GetMethod("OnRedeemAllButtonClick", BF)), Singleton(), k => CommissaryUIManager.Instance, null, AtCommissary);
            Add(M(() => typeof(CommissaryUIManager).GetMethod("OnRedeemAllLottery", BF)), Singleton(), k => CommissaryUIManager.Instance, null, AtCommissary);
            // NetworkUpgrade.Unlock is not forwarded on its own: the Wild network pays in its UI first (ShopActions.BuyWild).
            Add(M(() => typeof(StoreBell).GetMethod("RingBell", BF)), Singleton(), k => UnityEngine.Object.FindObjectOfType<StoreBell>(), null, BellGuard);
            AddAdapter(M(() => typeof(CounterTransferIntent).GetMethod("TransferCounter", BF)));

            // --- names a player types on an item, and the fake-condition labeler ---
            Add(M(() => typeof(GeneralHelper).GetMethod("SetCustomName", BF)));
            Add(M(() => typeof(GeneralHelper).GetMethod("RemoveCustomName", BF)));
            // SetCustomNameModel has no player caller in this installed version.
            AddAdapter(M(() => typeof(LabelAdapters).GetMethod("SetLabel", BF)));
            AddAdapter(M(() => typeof(LabelAdapters).GetMethod("SetAppraisal", BF)));
            AddAdapter(M(() => typeof(RequestAdapters).GetMethod("Fill", BF)));
            AddAdapter(M(() => typeof(ScannerIntent).GetMethod("RecordScan", BF)));
            Add(M(() => typeof(AppraisalServiceHelper).GetMethod("OnEstimate", BF)));
            AddAdapter(M(() => typeof(LabelAdapters).GetMethod("RepeatLabels", BF)), 2000);

            // --- counter conversation: the host's own panel checks the current customer before acting ---
            foreach (string name in new[] { "OnWholesaleClick", "OnInterogationButtonClick", "OnGlasseButtonClick", "OnLeftArrowButtonClick", "OnRightArrowButtonClick" })
                Add(M(() => typeof(NegociationUIManager).GetMethod(name, BF)), Singleton(), k => NegociationUIManager.Instance);
            foreach (string name in new[] { "Blackmail", "DemandBuyingDiscount", "OfferBuyingMarkup", "OfferDiscount", "OfferMarkup", "ThreatenAug" })
                Add(M(() => typeof(BargainUIManager).GetMethod(name, BF)), Singleton(), k => BargainUIManager.Instance);

            // --- the day flow: anyone can open, close, sleep and go out; the host re-checks the game's own conditions ---
            Add(M(() => typeof(StoreShutterButton).GetMethod("OpenShutter", BF)), Singleton(), k => StoreShutterButton.Instance, null, DayGuards.Open);
            Add(M(() => typeof(StoreShutterButton).GetMethod("EndAndClose", BF)), Singleton(), k => StoreShutterButton.Instance, null, DayGuards.Close);
            Add(M(() => typeof(EndOfDayUIManager).GetMethod("OnEndDayButtonClick", BF)), Singleton(), k => EndOfDayUIManager.Instance, null, DayGuards.Sleep);
            // Night outings: one outing for everyone. Anyone may start it or end it; it runs on the host and every screen goes along.
            AddAdapter(M(() => typeof(OutingKits).GetMethod("Scavenge", BF)));
            AddAdapter(M(() => typeof(OutingKits).GetMethod("GoOut", BF)));
            AddAdapter(M(() => typeof(OutingKits).GetMethod("ReturnHome", BF)));
            AddAdapter(M(() => typeof(OutingAdapters).GetMethod("Press", BF)));
            AddAdapter(M(() => typeof(OutingAdapters).GetMethod("ToggleService", BF)));
            AddAdapter(M(() => typeof(OutingAdapters).GetMethod("Confront", BF)));
            AddAdapter(M(() => typeof(OutingAdapters).GetMethod("ConfrontWater", BF)));

            // --- settings on machine panels (butcher, heating plate, feed dispenser, marking pen) ---
            AddAdapter(M(() => typeof(MachineSettings).GetMethod("SetTag", BF)), 4000);
            AddAdapter(M(() => typeof(MachineSettings).GetMethod("SetSettings", BF)), 16000);
            AddAdapter(M(() => typeof(ControlIntent).GetMethod("SetControl", BF)));
            AddAdapter(M(() => typeof(SceneObjects).GetMethod("LampClick", BF)));
            AddAdapter(M(() => typeof(SceneObjects).GetMethod("DebugBellClick", BF)));
            AddAdapter(M(() => typeof(MachineButtonPress).GetMethod("PressButton", BF)));
            AddAdapter(M(() => typeof(CompanyIntent).GetMethod("CompanyAction", BF)),16000);
            AddAdapter(M(() => typeof(CustomPanelRelay).GetMethod("CustomPanelAction", BF)),1000);
            AddAdapter(M(() => typeof(CargoIntent).GetMethod("CargoSave", BF)),16000);
            AddAdapter(M(() => typeof(StoragePagesIntent).GetMethod("StoragePageAction", BF)));
            AddAdapter(M(() => typeof(StoragePagesIntent).GetMethod("UndoPage", BF)));

            // --- found by the full audit ---
            AddAdapter(M(() => typeof(ShopActions).GetMethod("Inspect", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("MatchLabel", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("ConfrontChem", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("Discover", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("BuyWild", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("AcceptDeal", BF)));
            AddAdapter(M(() => typeof(CounterTrades).GetMethod("CounterAction", BF)));
            AddAdapter(M(() => typeof(CounterTrades).GetMethod("ResolveConfirmation", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("UndoSort", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("SetAppraisalAllowed", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("ApplyPerk", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("Weigh", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("CloseShop", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("ShutterIntent", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("Expose", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("BrowseUpgrades", BF)));
            AddAdapter(M(() => typeof(PanelRelay).GetMethod("ConfirmPanel", BF)));
            AddAdapter(M(() => typeof(PanelRelay).GetMethod("CancelPanel", BF)));





            // The shop phone: one phone, calls run on the host.
            AddAdapter(M(() => typeof(PhoneShare).GetMethod("Pick", BF)));
            AddAdapter(M(() => typeof(PhoneShare).GetMethod("HangUp", BF)));
            AddAdapter(M(() => typeof(PhoneShare).GetMethod("Key", BF)));
            AddAdapter(M(() => typeof(PhoneShare).GetMethod("Dial", BF)));
            AddAdapter(M(() => typeof(PhoneShare).GetMethod("Advance", BF)));
            AddAdapter(M(() => typeof(PhoneShare).GetMethod("Choose", BF)));
            // Item behaviours (organs and behaviours added by other mods) when one item is used on another.
            // Target is sent once through the unified GameItem intent, including behaviour callbacks.
            Add(M(() => typeof(ItemBehaviourManager).GetMethod("Use", BF)), Singleton(), k => ItemBehaviourManager.current);
            // The alarm button under the counter, and the hopper outlet's "transfer now".
            Add(M(() => typeof(PlayerStore).GetMethod("OnCustomerReported", BF)), Singleton(), k => PlayerStore.Instance, null, AlarmGuard);
            Add(M(() => typeof(HopperHelper).GetMethod("RunOutlet", BF)));
            AddAdapter(M(() => typeof(CoordinatorPresets).GetMethod("Save", BF)), CoordinatorPresets.MaxJson);

            // --- the counter shotgun: each player aims on their own screen; everyone watches the shot ---
            AddAdapter(M(() => typeof(ShotgunView).GetMethod("Raise", BF)));
            AddAdapter(M(() => typeof(ShotgunView).GetMethod("Lower", BF)));
            AddAdapter(M(() => typeof(ShotgunView).GetMethod("Fire", BF)));
        }

        internal static Entry Find(MethodBase method) { Build(); Entry e; return method != null && byMethod.TryGetValue(method, out e) ? e : null; }

        // ---------------- friend side ----------------

        static bool CustomerAction(Entry e) {
            var type = e.Method.DeclaringType;
            return type == typeof(CounterTrades) || type == typeof(NegociationUIManager) || type == typeof(BargainUIManager) ||
                type == typeof(AppraisalServiceHelper) || type == typeof(StoreBell) ||
                type == typeof(PlayerStore) && e.Method.Name == "TryPlayerDismissCurrentClient" ||
                type == typeof(ShopActions) && e.Method.Name == "AcceptDeal" ||
                type == typeof(ShotgunView);
        }
        internal static RemoteCall Encode(Entry e, object instance, object[] args) {
            var call = new RemoteCall { key = e.Key, customer = Session.World.Presentation.CustomerRevision, kinds = new string[args.Length], values = new string[args.Length] };
            if (e.Method.DeclaringType == typeof(ShopActions) && e.Method.Name == "AcceptDeal") {
                var current = PlayerStore.Instance.currentClientInstance;
                var offer = current == null ? null : current.GetClientBlueprint().barterOffer;
                call.offerSet = offer == null ? -1 : offer.currentSetIndex;
            }
            if (!e.Method.IsStatic) {
                call.instance = e.InstanceKey == null ? null : e.InstanceKey(instance);
                if (call.instance == null) return null;
            }
            var world = Session.World;
            for (int i = 0; i < args.Length; i++) {
                object a = args[i];
                if (a == null) { call.kinds[i] = "null"; continue; }
                if (a is GameItem) { int id = world.Id((GameItem)a); if (id == 0) return null; call.kinds[i] = "item"; call.values[i] = id.ToString(CultureInfo.InvariantCulture); continue; }
                if (a is GameInventory) { var address = world.Address((GameInventory)a); if (address == null) return null; call.kinds[i] = "inv"; call.values[i] = address.Key; continue; }
                if (a is bool) { call.kinds[i] = "b"; call.values[i] = (bool)a ? "1" : "0"; continue; }
                if (a is int) { call.kinds[i] = "i"; call.values[i] = ((int)a).ToString(CultureInfo.InvariantCulture); continue; }
                if (a is long) { call.kinds[i] = "l"; call.values[i] = ((long)a).ToString(CultureInfo.InvariantCulture); continue; }
                if (a is float) { call.kinds[i] = "f"; call.values[i] = ((float)a).ToString("R", CultureInfo.InvariantCulture); continue; }
                if (a is double) { call.kinds[i] = "d"; call.values[i] = ((double)a).ToString("R", CultureInfo.InvariantCulture); continue; }
                if (a is string) { var s = (string)a; if (s.Length > e.MaxString) return null; call.kinds[i] = "s"; call.values[i] = s; continue; }
                if (a is Enum) { call.kinds[i] = "e"; call.values[i] = Convert.ToInt64(a, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture); continue; }
                return null; // any other argument type is not forwarded
            }
            return call;
        }

        static string lastNotice; static float lastNoticeAt;
        internal static void Notice(string text) {
            if (text == null) return;
            if (text.Length > 120) text = text.Substring(0, 120);
            float now = Time.realtimeSinceStartup;
            // Every response refreshes the action status, even when identical popup text is throttled.
            LastNote = Loc.Text(text); NoteUntil = now + 4f;
            if (text == lastNotice && now - lastNoticeAt < 1.5f) return;
            lastNotice = text; lastNoticeAt = now;
            try { if (StoreUIManager.Instance != null) StoreUIManager.Instance.Notify(Loc.Text(text)); } catch { }
        }

        // ---------------- host side ----------------

        /// <summary>Validates and executes a friend's action. Returns a reason when refused.</summary>
        internal static string Execute(Connection session, int owner, RemoteCall call) {
            CurrentOwner = owner; PendingNote = null; PanelRelay.Owner = owner; PanelRelay.NotifiedDuringAction = false;
            try { using (HostNativeScope.Begin()) return Run(session, owner, call); }
            catch (Exception error) { Diagnostics.Report("remote action",error); return "~SMC085~"; }
            finally { session.SyncStateSoon(); CurrentOwner = -1; PanelRelay.Owner = 0; PanelRelay.SourceItem = null; PanelRelay.SourceIsActivation = false; }
        }
        static string Run(Connection session, int owner, RemoteCall call) {
            Build();
            Entry e;
            if (call == null || call.key == null || !byKey.TryGetValue(call.key, out e)) return "~SMC030~";
            if (CustomerAction(e) && !session.World.Presentation.CurrentCustomer(call.customer)) return "~SMC363~";
            if (e.Method.DeclaringType == typeof(ShopActions) && e.Method.Name == "AcceptDeal") {
                var current = PlayerStore.Instance.currentClientInstance;
                var offer = current == null ? null : current.GetClientBlueprint().barterOffer;
                if ((offer == null ? -1 : offer.currentSetIndex) != call.offerSet) return "~SMC365~";
            }
            if (e.HostOnly != null) return e.HostOnly;
            if ((e.Method.DeclaringType == typeof(NegociationUIManager) || e.Method.DeclaringType == typeof(BargainUIManager) || e.Method.DeclaringType == typeof(CounterTrades)) && !CounterTrades.Allowed(owner)) return "~SMC217~";
            string busy = HostPhase.Busy(session.Ready); if (busy != null) return busy;
            if (e.Guard != null) { string blocked; try { blocked = e.Guard(); } catch { blocked = "~SMC125~"; } if (blocked != null) return blocked; }
            int n = e.Parameters.Length;
            if (call.kinds == null || call.values == null || call.kinds.Length != n || call.values.Length != n) return "~SMC171~";
            object target = null;
            if (!e.Method.IsStatic) {
                if (e.ResolveInstance == null || call.instance == null || call.instance.Length > 128) return "~SMC174~";
                target = e.ResolveInstance(call.instance);
                if (target == null) return "~SMC173~";
            }
            var args = new object[n];
            double now = Time.realtimeSinceStartup;
            for (int i = 0; i < n; i++) {
                Type want = e.Parameters[i].ParameterType;
                if (e.Method.DeclaringType == typeof(CounterTrades) && e.Method.Name == "CounterAction" && i == 1 && args[0] is int && (int)args[0] == 1 && !CounterTrades.UsesBuySelection) { args[i] = null; continue; }
                string kind = call.kinds[i], value = call.values[i];
                switch (kind) {
                    case "null": if (want.IsValueType) return "~SMC171~"; args[i] = null; break;
                    case "item": {
                        int id; GameItem item;
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || !session.World.Items.TryGetValue(id, out item) || !AuthorityIntent.Live(item)) return "~SMC095~";

                        if (!want.IsInstanceOfType(item)) return "~SMC171~";
                        if (!DialogueIntent.CanTouch(session, item)) return "~SMC353~";
                        if (!session.Leases.Available(owner, id, now)) return "~SMC096~";
                        var at = session.World.Address(item.parentInventory); if(at != null && !OutingKits.MayUse(owner,at.root)) return "~SMC217~";
                        args[i] = item; break;
                    }
                    case "inv": {
                        GameInventory inv;
                        if (value == null || !session.World.Inventories.TryGetValue(value, out inv) || inv == null || !want.IsInstanceOfType(inv)) return "~SMC111~";
                        if (!DialogueIntent.CanTouchInventory(session, inv)) return "~SMC353~";
                        var at = session.World.Address(inv); if(at != null && !OutingKits.MayUse(owner,at.root)) return "~SMC217~";
                        args[i] = inv; break;
                    }
                    case "b": if (want != typeof(bool) || (value != "0" && value != "1")) return "~SMC171~"; args[i] = value == "1"; break;
                    case "i": { int v; if (want != typeof(int) || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return "~SMC171~"; args[i] = v; break; }
                    case "l": { long v; if (want != typeof(long) || !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return "~SMC171~"; args[i] = v; break; }
                    case "f": { float v; if (want != typeof(float) || !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || float.IsNaN(v) || float.IsInfinity(v)) return "~SMC171~"; args[i] = v; break; }
                    case "d": { double v; if (want != typeof(double) || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsNaN(v) || double.IsInfinity(v)) return "~SMC171~"; args[i] = v; break; }
                    case "s": if (want != typeof(string) || value == null || value.Length > e.MaxString) return "~SMC171~"; args[i] = value; break;
                    case "e": {
                        long v; if (!want.IsEnum || !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || !Enum.IsDefined(want, Enum.ToObject(want, v))) return "~SMC171~";
                        args[i] = Enum.ToObject(want, v); break;
                    }
                    default: return "~SMC171~";
                }
            }
            if (!e.Method.IsStatic && target is GameItem) {

                int id = session.World.Id((GameItem)target);
                var at = session.World.Address(((GameItem)target).parentInventory); if(at != null && !OutingKits.MayUse(owner,at.root)) return "~SMC217~";
                if (id == 0 || !session.Leases.Available(owner, id, now)) return "~SMC096~";
            }
            if (e.Method.DeclaringType == typeof(ScratcherHelper) && e.Method.Name == "MarkSymbolScratched") {
                var ticket = args[0] as GameItem; int slot = (int)args[1];
                if (ticket == null || !ticket.IsTag("SCRATCHER_TAG") || !GeneralHelper.IsItemOwned(ticket) || slot < 1 || slot > 3 || ticket.GetTagReadonly("SCRATCHER_STATE" + slot).GetInt() == 1) return "~SMC171~";
            }
            foreach (var arg in args) if (arg is GameItem) { PanelRelay.SourceItem = (GameItem)arg; break; }
            if (e.Method.DeclaringType == typeof(CommissaryData) && e.Method.Name == "BuyItem") {
                var data = target as CommissaryData; var product = data != null ? data.GetItemData(args[0] as string) : null;
                if (product == null || product.StockCount <= 0) return "~SMC325~";
                if (data.redeemedCredit < product.price) return "~SMC324~";
            }
            try {
                object result = e.Method.Invoke(target, args);
                if (e.Method.DeclaringType == typeof(ShotgunView) && e.Method.Name == "Raise" && PendingNote == null) ShotgunView.ConfirmRaise(call.customer);
                if (result is bool && !(bool)result && PendingNote == null) return "~SMC321~";
                if (e.Method.DeclaringType == typeof(HopperHelper) && e.Method.Name == "RunAndRecord" && result is int && (int)result == 0 && PendingNote == null) return "~SMC321~";
            }
            catch (TargetInvocationException error) { Debug.LogWarning("[ShopTogether] remote " + e.Key + " failed on host: " + error.InnerException); return PendingNote??"~SMC085~"; }
            Forwarded++;
            return null;
        }
    }

    // The host owns the ticket's value and the shop's started-ticket counter. The guest only draws the scratch mask.
    internal static class ScratchInteraction {
        static readonly Dictionary<int,float> requested = new Dictionary<int,float>();
        internal static void Reset() { requested.Clear(); }
        internal static void Start(GameItem item) {
            if (item == null || item.parentInventory == null) { AuthorityIntent.Refuse("~SMC095~"); return; }
            if (!item.IsTag("SCRATCHER_TAG") || !GeneralHelper.IsItemOwned(item)) { AuthorityIntent.Refuse("~SMC347~"); return; }
            if (!item.IsTag("SCRATCHER_NEW_TAG")) return;
            item.DisableTag("SCRATCHER_NEW_TAG"); item.unitValue = 0L; item.RemoveGameItemType("LOTTERY"); LotteryHelper.OnScratcherStarted();
        }
        internal static void Request() {
            var s = Connection.Current; var ui = ScratcherUIManager.Instance; var item = ui != null ? ui.currentActiveScratcherItem : null;
            if (s == null || !s.IsClient || !s.Ready || item == null || !item.IsTag("SCRATCHER_NEW_TAG") || !LocalControl.UserAction) return;
            int id = s.World.Id(item); if (id <= 0) return; float next;
            if (requested.TryGetValue(id, out next) && Time.realtimeSinceStartup < next) return;
            requested[id] = Time.realtimeSinceStartup + .5f;
            RemoteActions.Forward(RemoteActions.Adapter("Start", 1), null, new object[] { item });
        }
    }
    [HarmonyPatch(typeof(ScratchInput), "OnAnyScratch")]
    internal static class ClientScratchStarted {
        static bool Prefix() { if (!ReplicaSession.ClientStore || WorldReplica.Applying) return true; ScratchInteraction.Request(); return false; }
    }

    /// <summary>Intercepts the forwarded functions on a friend's machine.</summary>
    [HarmonyPatch]
    internal static class RemoteActionPatch {
        static IEnumerable<MethodBase> TargetMethods() { return RemoteActions.Targets(); }

        [HarmonyPriority(Priority.First + 200)]
        static bool Prefix(MethodBase __originalMethod, object __instance, object[] __args) {
            var session = Connection.Current;
            if (!ReplicaSession.ClientStore) {
                // Host: its own clicks respect items a friend is currently working on.
                if (session == null || !session.IsHost || !session.Ready || !LocalControl.UserAction || RemoteActions.CurrentOwner > 0 || NativeActions.Executing) return true;
                double now = Time.realtimeSinceStartup;
                if (__instance is GameItem && !session.Leases.Available(0, session.World.Id((GameItem)__instance), now)) { RemoteActions.Notice("~SMC096~"); return false; }
                if (__args != null) foreach (var a in __args) if (a is GameItem && !session.Leases.Available(0, session.World.Id((GameItem)a), now)) { RemoteActions.Notice("~SMC096~"); return false; }
                var hostEntry = RemoteActions.Find(__originalMethod);
                if (hostEntry != null && hostEntry.Guard != null) { string reason = hostEntry.Guard(); if (reason != null) { RemoteActions.Notice(reason); return false; } }
                return true;
            }
            if (WorldReplica.Applying) return true;                                              // applying host data
            var e = RemoteActions.Find(__originalMethod);
            if (e == null) return true;
            if (!LocalControl.UserAction) return false;                                       // the replica never acts on its own
            if (session == null || !session.Ready || !ReplicaSession.SceneReady) { RemoteActions.Notice("~SMC242~"); return false; }
            if (e.HostOnly != null && !HostPhase.Away) { RemoteActions.Refused++; RemoteActions.Notice(e.HostOnly); return false; }
            RemoteActions.Forward(e, __instance, __args ?? new object[0]);
            return false;
        }
    }

    /// <summary>The labeler edits an item's fake conditions from a dropdown; the host applies the same change.</summary>
    internal static class LabelAdapters {
        static ItemFeature Editable(GameItem item, string category) {
            if (item == null || category == null || item.itemFeatures == null) return null;
            foreach (var f in item.itemFeatures) if (f != null && f.category == category && LabelerHelper.IsEditableLabelFeature(f)) return f;
            return null;
        }
        /// <summary>Host side: same as choosing an entry in the labeler dropdown.</summary>
        internal static void SetLabel(GameItem item, string category, int index) {
            if (item == null || !GeneralHelper.IsItemOwned(item)) { AuthorityIntent.Refuse("~SMC347~"); return; }
            var feature = Editable(item, category);
            List<ItemCondition> list;
            if (feature == null || !ItemConditionList.conditionLookup.TryGetValue(category, out list) || index < 0 || index >= list.Count) { AuthorityIntent.Refuse("~SMC305~"); return; }
            feature.fakeCondition = list[index];
            feature.SetExposed(false);
            WaterFeatureHelper.OnWaterLabelUpdated(feature, index);
        }
        /// <summary>Host side: the price typed in the appraisal-service field of the inspection panel.</summary>
        internal static void SetAppraisal(GameItem item, int value) {
            if (item == null || value < 0 || value > 1000000000) { AuthorityIntent.Refuse("~SMC305~"); return; }
            item.ModifyTag("APPRAISAL_DEFAULT_VALUE", state => state.Enable().SetInt(value));
        }
        /// <summary>Host side: same as the labeler's repeat button, with the friend's remembered labels ("category=identifier" lines).</summary>
        internal static void RepeatLabels(GameItem item, string packed) {
            if (item != null && !GeneralHelper.IsItemOwned(item)) { AuthorityIntent.Refuse("~SMC347~"); return; }
            if (item == null || packed == null || packed.Length > 2000) { AuthorityIntent.Refuse("~SMC305~"); return; }
            var selected = new Dictionary<string,int>();
            foreach (var line in packed.Split('\n')) {
                if (line.Length == 0) continue;
                int cut = line.IndexOf('=');
                if (cut <= 0) { AuthorityIntent.Refuse("~SMC305~"); return; }
                var feature = Editable(item, line.Substring(0, cut));
                List<ItemCondition> list;
                if (feature == null || !ItemConditionList.conditionLookup.TryGetValue(feature.category, out list)) continue;
                string identifier = line.Substring(cut + 1);
                int index = list.FindIndex(condition => condition.identifier == identifier);
                if (index < 0) { AuthorityIntent.Refuse("~SMC305~"); return; }
                selected[feature.category] = index;
            }
            if (selected.Count == 0) { AuthorityIntent.Refuse("~SMC321~"); return; }
            foreach (var pair in selected) SetLabel(item,pair.Key,pair.Value);
        }
    }

    internal static class LabelerState { internal static GameItem Current; }

    [HarmonyPatch(typeof(LabelerUIManager), "OpenUI")]
    internal static class LabelerOpen { static void Postfix(GameItem gameItem) { LabelerState.Current = gameItem; } }

    [HarmonyPatch(typeof(LabelerElement), "OnDropdownValueChanged")]
    internal static class LabelerChoose {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(LabelerElement __instance, ItemFeature ___thisFeature) {
            if (!ReplicaSession.ClientStore || WorldReplica.Applying) {
                var s = Connection.Current; var item = LabelerState.Current;
                if (!WorldReplica.Applying && s != null && s.IsHost && s.Ready && LocalControl.UserAction && item != null) {
                    if (!GeneralHelper.IsItemOwned(item)) { RemoteActions.Notice("~SMC347~"); return false; }
                    if (!AuthorityIntent.FreeTree(s, Math.Max(0, RemoteActions.CurrentOwner), item)) { RemoteActions.Notice("~SMC096~"); return false; }
                }
                return true;
            }
            if (!ReplicaSession.IsReplica || ___thisFeature == null || LabelerState.Current == null || !LocalControl.Focused) return false;
            List<ItemCondition> list;
            int index = __instance.dropdown.value;
            if (!ItemConditionList.conditionLookup.TryGetValue(___thisFeature.category, out list) || index < 0 || index >= list.Count) return false;
            var current = ___thisFeature.GetCurrentItemCondition(); if (current != null && current.identifier == list[index].identifier) return false;                       // opening the panel re-selects the current value
            RemoteActions.Forward(RemoteActions.Adapter("SetLabel", 3), null, new object[] { LabelerState.Current, ___thisFeature.category, index });
            return false;
        }
    }

    /// <summary>The appraisal-service price a friend types is kept on the item on the host (and shown here at once).</summary>
    [HarmonyPatch]
    internal static class FriendAppraisalPrice {
        static IEnumerable<MethodBase> TargetMethods() {
            foreach (var name in new[] { "FinishEditingAppraisalField", "ChangeAppraisalServicePrice" }) { var m = AccessTools.Method(typeof(InspectionUIManager), name); if (m != null) yield return m; }
        }
        static bool Prefix(InspectionUIManager __instance, GameItem ___currentInspectingItem, MethodBase __originalMethod, object[] __args) {
            if (!ReplicaSession.IsReplica) return true;
            if (!LocalControl.Focused) return false;
            long value;
            if (!long.TryParse(__instance.apprisalServiceInputField.text,out value)) { RemoteActions.Notice("~SMC305~"); return false; }
            if (__originalMethod.Name == "ChangeAppraisalServicePrice") value += (int)__args[0];
            else { AccessTools.Field(typeof(InspectionUIManager),"isEditingAppraisalField").SetValue(__instance,false); StoreUIManager.Instance.StopTyping(); }
            if (value < 0 || value > 1000000000) { RemoteActions.Notice("~SMC305~"); return false; }
            RemoteActions.Forward(RemoteActions.Adapter("SetAppraisal", 2), null, new object[] { ___currentInspectingItem, (int)value });
            return false;
        }
    }

    /// <summary>
    /// Marketplace requests on the shop PC: anyone may deliver. The friend's click is sent to the host, which takes
    /// the requested items from the shop and pays the reward exactly as if the host had clicked.
    /// </summary>
    internal static class RequestAdapters {
        internal static string Key(StoreRequest r) { return r == null ? "" : (r.id ?? "") + "|" + (r.title ?? "") + "|" + (r.requiredItem ?? "") + "|" + r.requiredItemCount + "|" + r.creditReward; }
        internal static void Fill(int index, string key) {
            var list = PlayerStore.Instance != null ? PlayerStore.Instance.storeRequests : null;
            if (list == null || index < 0 || index >= list.Count || key == null || Key(list[index]) != key) { AuthorityIntent.Refuse("~SMC334~"); return; }
            if(!CanFill(list[index]))return;
            list[index].FillRequest();
            if (PCUIManager.Instance != null) { PCUIManager.Instance.OnChange(); if (PCUIManager.Instance.storePC != null) PCUIManager.Instance.storePC.RefreshState(); }
        }
        internal static bool CanFill(StoreRequest request) {
            if(request==null||PlayerStore.Instance==null||!PlayerStore.Instance.storeRequests.Contains(request))return AuthorityIntent.Refuse("~SMC334~");
            if (!request.CanFillRequest()) return AuthorityIntent.Refuse("~SMC335~");
            var session = Connection.Current;
            int owner = RemoteActions.CurrentOwner > 0 ? RemoteActions.CurrentOwner : 0;
            var owned = EmporiumEntry.Instance.GetAllOwnedItems(); int needed = request.requiredItemCount;
            if(needed<=0)return AuthorityIntent.Refuse("~SMC335~");
            for (int n = owned.Count - 1; n >= 0 && needed > 0; n--) if (request.CanThisItemFillRequest(owned[n])) {
                if(!DialogueIntent.MayChange(session,owned[n]) || !AuthorityIntent.FreeTree(session,owner,owned[n]))return false;
                needed--;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(StoreRequest), "FillRequest")]
    internal static class FriendFillRequest {
        [HarmonyPriority(Priority.First + 200)]
        static bool Prefix(StoreRequest __instance) {
            if(WorldReplica.Applying)return true;
            if(!ReplicaSession.ClientStore){
                var session=Connection.Current;
                if(session==null||!session.IsHost||!session.Ready||RemoteActions.CurrentOwner>=0)return true;
                string previous=RemoteActions.PendingNote;
                try{if(RequestAdapters.CanFill(__instance))return true;RemoteActions.Notice(RemoteActions.PendingNote??"~SMC335~");return false;}
                finally{RemoteActions.PendingNote=previous;}
            }
            var list = PlayerStore.Instance != null ? PlayerStore.Instance.storeRequests : null;
            // The list on this machine is refreshed from the host, so find the request by its content, not by object.
            int index = -1; string key = RequestAdapters.Key(__instance);
            if (list != null) for (int i = 0; i < list.Count; i++) if (RequestAdapters.Key(list[i]) == key) { index = i; break; }
            if (ReplicaSession.IsReplica && LocalControl.Focused) RemoteActions.Forward(RemoteActions.Adapter("Fill", 2), null, new object[] { index, key });
            return false;
        }
    }

    [HarmonyPatch(typeof(LabelerUIManager), "Repeat")]
    internal static class LabelerRepeat {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(GameItem ___curItem, Dictionary<string, string> ___lastLabels) {
            if (!ReplicaSession.ClientStore || WorldReplica.Applying) return true;
            if (!ReplicaSession.IsReplica || ___curItem == null || ___lastLabels == null || ___lastLabels.Count == 0 || !LocalControl.Focused) return false;
            var packed = new System.Text.StringBuilder();
            foreach (var p in ___lastLabels) if (p.Key != null && p.Value != null && p.Key.IndexOf('=') < 0 && p.Key.IndexOf('\n') < 0 && p.Value.IndexOf('\n') < 0) packed.Append(p.Key).Append('=').Append(p.Value).Append('\n');
            if (packed.Length > 0 && packed.Length <= 2000) RemoteActions.Forward(RemoteActions.Adapter("RepeatLabels", 2), null, new object[] { ___curItem, packed.ToString() });
            return false;
        }
    }

    /// <summary>
    /// Typing a name, choosing from a dropdown or ticking a box is a player action just like a click, so the
    /// forwarded functions those controls call are treated as user actions.
    /// </summary>
    [HarmonyPatch]
    internal static class UiInputScope {
        static IEnumerable<MethodBase> TargetMethods() {
            foreach (var m in new MethodBase[] { AccessTools.Method(typeof(TMPro.TMP_InputField), "SendOnEndEdit"), AccessTools.Method(typeof(TMPro.TMP_InputField), "SendOnSubmit"), AccessTools.Method(typeof(TMPro.TMP_Dropdown), "OnSelectItem"), AccessTools.Method(typeof(UnityEngine.UI.Toggle), "InternalToggle"), AccessTools.Method(typeof(UnityEngine.UI.Slider), "OnPointerDown"), AccessTools.Method(typeof(UnityEngine.UI.Slider), "OnDrag"), AccessTools.Method(typeof(UnityEngine.UI.Slider), "OnMove") })
                if (m != null) yield return m;
        }
        static void Prefix(ref bool __state) { __state = LocalControl.Focused; if (__state) { LocalControl.depth++; MachineSettings.Input++; } }
        static Exception Finalizer(Exception __exception, bool __state) { if (__state) { LocalControl.depth--; if (MachineSettings.Input > 0) MachineSettings.Input--; } return __exception; }
    }
}
