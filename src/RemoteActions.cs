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
    [Serializable] internal sealed class RemoteCall { public long sequence; public string key; public string instance; public string[] kinds; public string[] values; }

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
        static string AtCommissary() { return OutingKits.FriendAt(CurrentOwner, "commissary") ? null : "~SMC214~"; }
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
            if (HostPhase.Away) { Notice(HostPhase.Text); return true; }
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
            Add(M(() => typeof(AnimalCage).GetMethod("Repair", BF)));
            Add(M(() => typeof(ReactorHelper).GetMethod("RepairWithJunk", BF)));
            Add(M(() => typeof(ReactorHelper).GetMethod("RepairWithScrap", BF)));
            Add(M(() => typeof(ModuleHelper).GetMethod("RepairExtractor", BF)));
            Add(M(() => typeof(MachineAutoButcher).GetMethod("ButcherContent", BF)));
            Add(M(() => typeof(MachineAutoButcher).GetMethod("CopySettings", BF)));
            Add(M(() => typeof(MachineAutoButcher).GetMethod("PasteSettings", BF)));
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
            Add(M(() => typeof(VendingMachineHelper).GetMethod("UnlockVendingMachine", BF)));
            Add(M(() => typeof(VendingFountainHelper).GetMethod("UnlockVendingFountain", BF)));

            // --- store-level player actions: the target object is a singleton both machines have ---
            Add(M(() => typeof(PlayerStore).GetMethod("TryPlayerDismissCurrentClient", BF)), Singleton(), k => PlayerStore.Instance);
            Add(M(() => typeof(PlayerStore).GetMethod("ArrestCurrentClientForStolenGoods", BF)), Singleton(), k => PlayerStore.Instance);
            Add(M(() => typeof(PlayerStore).GetMethod("TakeMortgage", BF)), Singleton(), k => PlayerStore.Instance);
            Add(M(() => typeof(PlayerStore).GetMethod("PayWeeklyMortgage", BF)), Singleton(), k => PlayerStore.Instance);
            Add(M(() => typeof(PlayerStore).GetMethod("BuyOutStore", BF)), Singleton(), k => PlayerStore.Instance);
            // The commissary: a friend trades there on their own outing; what they buy lands on their own ground.
            Add(M(() => typeof(CommissaryData).GetMethod("BuyItem", BF)), Singleton(), k => PlayerStore.Instance != null ? PlayerStore.Instance.commissaryData : null, null, AtCommissary);
            Add(M(() => typeof(CommissaryUIManager).GetMethod("OnRedeemAllButtonClick", BF)), Singleton(), k => CommissaryUIManager.Instance, null, AtCommissary);
            Add(M(() => typeof(CommissaryUIManager).GetMethod("OnRedeemAllLottery", BF)), Singleton(), k => CommissaryUIManager.Instance, null, AtCommissary);
            // NetworkUpgrade.Unlock is not forwarded on its own: the Wild network pays in its UI first (ShopActions.BuyWild).
            Add(M(() => typeof(StoreBell).GetMethod("RingBell", BF)), Singleton(), k => UnityEngine.Object.FindObjectOfType<StoreBell>());
            Add(M(() => typeof(StoreTransferButton).GetMethod("OnClick", BF)), Singleton(), k => UnityEngine.Object.FindObjectOfType<StoreTransferButton>());

            // --- names a player types on an item, and the fake-condition labeler ---
            Add(M(() => typeof(GeneralHelper).GetMethod("SetCustomName", BF)));
            Add(M(() => typeof(GeneralHelper).GetMethod("RemoveCustomName", BF)));
            Add(M(() => typeof(GeneralHelper).GetMethod("SetCustomNameModel", BF)));
            AddAdapter(M(() => typeof(LabelAdapters).GetMethod("SetLabel", BF)));
            AddAdapter(M(() => typeof(LabelAdapters).GetMethod("SetAppraisal", BF)));
            AddAdapter(M(() => typeof(RequestAdapters).GetMethod("Fill", BF)));
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
            // Night outings: a friend goes out and comes back on their own (the host is never moved by a friend).
            AddAdapter(M(() => typeof(OutingKits).GetMethod("Go", BF)));
            AddAdapter(M(() => typeof(OutingKits).GetMethod("Back", BF)));
            AddAdapter(M(() => typeof(OutingKits).GetMethod("Scavenge", BF)));
            AddAdapter(M(() => typeof(OutingKits).GetMethod("Join", BF)));
            AddAdapter(M(() => typeof(OutingKits).GetMethod("Unjoin", BF)));
            AddAdapter(M(() => typeof(OutingAdapters).GetMethod("Press", BF)));
            AddAdapter(M(() => typeof(OutingAdapters).GetMethod("ToggleService", BF)));
            AddAdapter(M(() => typeof(OutingAdapters).GetMethod("Confront", BF)));
            AddAdapter(M(() => typeof(OutingAdapters).GetMethod("ConfrontWater", BF)));

            // --- settings on machine panels (butcher, heating plate, feed dispenser, marking pen) ---
            AddAdapter(M(() => typeof(MachineSettings).GetMethod("SetTag", BF)), 4000);
            AddAdapter(M(() => typeof(MachineButtonPress).GetMethod("PressButton", BF)));

            // --- found by the full audit ---
            AddAdapter(M(() => typeof(ShopActions).GetMethod("Inspect", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("MatchLabel", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("ConfrontChem", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("Discover", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("BuyWild", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("AcceptDeal", BF)));
            AddAdapter(M(() => typeof(CounterTrades).GetMethod("CounterAction", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("UndoSort", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("SetAppraisalAllowed", BF)));
            AddAdapter(M(() => typeof(ShopActions).GetMethod("ApplyPerk", BF)));
            AddAdapter(M(() => typeof(OutingSupplies).GetMethod("UseBandage", BF)));
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
            Add(M(() => typeof(ItemBehaviourManager).GetMethod("Target", BF)), Singleton(), k => ItemBehaviourManager.current);
            Add(M(() => typeof(ItemBehaviourManager).GetMethod("Use", BF)), Singleton(), k => ItemBehaviourManager.current);
            // The alarm button under the counter, and the hopper outlet's "transfer now".
            Add(M(() => typeof(PlayerStore).GetMethod("OnCustomerReported", BF)), Singleton(), k => PlayerStore.Instance, null, () => PlayerStore.Instance != null && PlayerStore.Instance.IsClientPresent() ? null : "~SMC231~");
            Add(M(() => typeof(HopperHelper).GetMethod("RunOutlet", BF)));
            AddAdapter(M(() => typeof(CoordinatorPresets).GetMethod("Save", BF)), CoordinatorPresets.MaxJson);

            // --- the counter shotgun: each player aims on their own screen; everyone watches the shot ---
            AddAdapter(M(() => typeof(ShotgunView).GetMethod("Raise", BF)));
            AddAdapter(M(() => typeof(ShotgunView).GetMethod("Lower", BF)));
            AddAdapter(M(() => typeof(ShotgunView).GetMethod("Fire", BF)));
        }

        internal static Entry Find(MethodBase method) { Build(); Entry e; return method != null && byMethod.TryGetValue(method, out e) ? e : null; }

        // ---------------- friend side ----------------

        internal static RemoteCall Encode(Entry e, object instance, object[] args) {
            var call = new RemoteCall { key = e.Key, kinds = new string[args.Length], values = new string[args.Length] };
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
            if (text == lastNotice && now - lastNoticeAt < 1.5f) return;   // the same message again at once (a held button, every frame) is shown once
            lastNotice = text; lastNoticeAt = now;
            LastNote = Loc.Text(text); NoteUntil = Time.realtimeSinceStartup + 4f;
            try { if (StoreUIManager.Instance != null) StoreUIManager.Instance.Notify(Loc.Text(text)); } catch { }
        }

        // ---------------- host side ----------------

        /// <summary>Validates and executes a friend's action. Returns a reason when refused.</summary>
        internal static string Execute(Connection session, int owner, RemoteCall call) {
            CurrentOwner = owner; PendingNote = null; PanelRelay.Owner = owner;
            try { return Run(session, owner, call); } finally { CurrentOwner = -1; PanelRelay.Owner = 0; PanelRelay.SourceItem = null; PanelRelay.SourceIsActivation = false; }
        }
        static string Run(Connection session, int owner, RemoteCall call) {
            Build();
            Entry e;
            if (call == null || call.key == null || !byKey.TryGetValue(call.key, out e)) return "~SMC030~";
            if (e.HostOnly != null) return e.HostOnly;
            if ((e.Method.DeclaringType == typeof(NegociationUIManager) || e.Method.DeclaringType == typeof(BargainUIManager) || e.Method.DeclaringType == typeof(CounterTrades)) && !CounterTrades.Allowed(owner)) return "~SMC217~";
            if (HostPhase.Busy(session.Ready) != null) return "~SMC080~";
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
                string kind = call.kinds[i], value = call.values[i];
                switch (kind) {
                    case "null": if (want.IsValueType) return "~SMC171~"; args[i] = null; break;
                    case "item": {
                        int id; GameItem item;
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || !session.World.Items.TryGetValue(id, out item) || item == null) return "~SMC095~";
                        if (!want.IsInstanceOfType(item)) return "~SMC171~";
                        if (!session.Leases.Available(owner, id, now)) return "~SMC096~";
                        var at = session.World.Address(item.parentInventory); if(at != null && !OutingKits.MayUse(owner,at.root)) return "~SMC217~";
                        args[i] = item; break;
                    }
                    case "inv": {
                        GameInventory inv;
                        if (value == null || !session.World.Inventories.TryGetValue(value, out inv) || inv == null || !want.IsInstanceOfType(inv)) return "~SMC111~";
                        var at = session.World.Address(inv); if(at != null && !OutingKits.MayUse(owner,at.root)) return "~SMC217~";
                        args[i] = inv; break;
                    }
                    case "b": if (want != typeof(bool)) return "~SMC171~"; args[i] = value == "1"; break;
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
            try { e.Method.Invoke(target, args); }
            catch (TargetInvocationException error) { Debug.LogWarning("[ShopTogether] remote " + e.Key + " failed on host: " + error.InnerException); return "~SMC085~"; }
            Forwarded++;
            return null;
        }
    }

    // The host owns the ticket's value and the shop's started-ticket counter. The guest only draws the scratch mask.
    internal static class ScratchInteraction {
        static readonly Dictionary<int,float> requested = new Dictionary<int,float>();
        internal static void Reset() { requested.Clear(); }
        internal static void Start(GameItem item) {
            if (item == null || item.parentInventory == null || !item.IsTag("SCRATCHER_TAG") || !item.IsTag("SCRATCHER_NEW_TAG") || !GeneralHelper.IsItemOwned(item)) return;
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

        [HarmonyPriority(Priority.First)]
        static bool Prefix(MethodBase __originalMethod, object __instance, object[] __args) {
            var session = Connection.Current;
            if (!ReplicaSession.ClientStore) {
                // Host: its own clicks respect items a friend is currently working on.
                if (session == null || !session.IsHost || !session.Ready || !LocalControl.UserAction) return true;
                double now = Time.realtimeSinceStartup;
                if (__instance is GameItem && !session.Leases.Available(0, session.World.Id((GameItem)__instance), now)) { RemoteActions.Notice("~SMC096~"); return false; }
                if (__args != null) foreach (var a in __args) if (a is GameItem && !session.Leases.Available(0, session.World.Id((GameItem)a), now)) { RemoteActions.Notice("~SMC096~"); return false; }
                return true;
            }
            if (WorldReplica.Applying) return true;                                              // applying host data
            var e = RemoteActions.Find(__originalMethod);
            if (e == null) return true;
            if (session == null || !session.Ready || !ReplicaSession.SceneReady) return false;
            if (!LocalControl.UserAction) return false;                                       // the replica never acts on its own
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
            var feature = Editable(item, category);
            List<ItemCondition> list;
            if (feature == null || !ItemConditionList.conditionLookup.TryGetValue(category, out list) || index < 0 || index >= list.Count) return;
            feature.fakeCondition = list[index];
            feature.SetExposed(false);
            WaterFeatureHelper.OnWaterLabelUpdated(feature, index);
        }
        /// <summary>Host side: the price typed in the appraisal-service field of the inspection panel.</summary>
        internal static void SetAppraisal(GameItem item, int value) {
            if (item == null || value < 0 || value > 1000000000) return;
            item.ModifyTag("APPRAISAL_DEFAULT_VALUE", state => state.Enable().SetInt(value));
        }
        /// <summary>Host side: same as the labeler's repeat button, with the friend's remembered labels ("category=identifier" lines).</summary>
        internal static void RepeatLabels(GameItem item, string packed) {
            if (item == null || packed == null || packed.Length > 2000) return;
            foreach (var line in packed.Split('\n')) {
                int cut = line.IndexOf('=');
                if (cut <= 0) continue;
                var feature = Editable(item, line.Substring(0, cut));
                List<ItemCondition> list;
                if (feature == null || !ItemConditionList.conditionLookup.TryGetValue(feature.category, out list)) continue;
                string identifier = line.Substring(cut + 1);
                foreach (var condition in list) if (condition.identifier == identifier) { feature.fakeCondition = condition; feature.SetExposed(false); break; }
            }
        }
    }

    internal static class LabelerState { internal static GameItem Current; }

    [HarmonyPatch(typeof(LabelerUIManager), "OpenUI")]
    internal static class LabelerOpen { static void Postfix(GameItem gameItem) { LabelerState.Current = gameItem; } }

    [HarmonyPatch(typeof(LabelerElement), "OnDropdownValueChanged")]
    internal static class LabelerChoose {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(LabelerElement __instance, ItemFeature ___thisFeature) {
            if (!ReplicaSession.ClientStore || WorldReplica.Applying) return true;
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
        static void Postfix(GameItem ___currentInspectingItem) {
            if (!ReplicaSession.ClientStore || WorldReplica.Applying || !ReplicaSession.IsReplica || ___currentInspectingItem == null) return;
            var tag = ___currentInspectingItem.state != null ? ___currentInspectingItem.GetTagReadonly("APPRAISAL_DEFAULT_VALUE") : null;
            if (tag == null) return;
            RemoteActions.Forward(RemoteActions.Adapter("SetAppraisal", 2), null, new object[] { ___currentInspectingItem, tag.GetInt() });
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
            if (list == null || index < 0 || index >= list.Count || key == null || Key(list[index]) != key || !list[index].CanFillRequest()) return;
            list[index].FillRequest();
            if (PCUIManager.Instance != null) { PCUIManager.Instance.OnChange(); if (PCUIManager.Instance.storePC != null) PCUIManager.Instance.storePC.RefreshState(); }
        }
    }

    [HarmonyPatch(typeof(StoreRequest), "FillRequest")]
    internal static class FriendFillRequest {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(StoreRequest __instance) {
            if (!ReplicaSession.ClientStore || WorldReplica.Applying) return true;
            var list = PlayerStore.Instance != null ? PlayerStore.Instance.storeRequests : null;
            // The list on this machine is refreshed from the host, so find the request by its content, not by object.
            int index = -1; string key = RequestAdapters.Key(__instance);
            if (list != null) for (int i = 0; i < list.Count; i++) if (RequestAdapters.Key(list[i]) == key) { index = i; break; }
            if (ReplicaSession.IsReplica && index >= 0 && LocalControl.Focused) RemoteActions.Forward(RemoteActions.Adapter("Fill", 2), null, new object[] { index, key });
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
