using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    // Optional bridge for Probably Multiplayer 2.6.1. Its market runs only on the shop host; a guest
    // remains a replica and never starts a second visit against the host's copied save.
    internal static class ProbablyMultiplayerCompat {
        const string ModId = "lemocuber_probablymultiplayer";
        const string Portrait = "lemocuber_probablymultiplayer_player_sprite";
        const string PatchId = "shop_together.probably_multiplayer_compat";
        static Harmony patches;
        static FieldInfo active, externalConnection, externalSession;
        static PropertyInfo locked;
        static MethodInfo preview, selected, registerPortrait, disconnect;
        static bool bound, failed, clientSuspended;
        static float nextCheck, nextClientCheck;
#if LAN_PROBE
        internal static bool ProbeEnabled;
        internal static bool ProbeBound { get { return bound; } }
#endif
        internal static bool Enabled() {
#if LAN_PROBE
            if (ProbeEnabled) return true;
#endif
            foreach (var mod in ModLoader.Mods)
                if (mod != null && mod.IsEnabled && mod.Manifest != null && string.Equals(mod.Manifest.ID, ModId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        internal static void Tick() {
            if (bound || failed || Time.realtimeSinceStartup < nextCheck || !Enabled()) return;
            nextCheck = Time.realtimeSinceStartup + 1f;
            var core = AccessTools.TypeByName("ProbablyMultiplayer.Core");
            if (core == null) return; // The loader may not have reached the other mod yet.
            try {
                var assembly = core.Assembly;
                if (assembly.GetName().Name != "ProbablyMultiplayer" || assembly.GetName().Version.ToString(3) != "2.6.1")
                    throw new InvalidOperationException("Unsupported Probably Multiplayer version");
                var visit = assembly.GetType("ProbablyMultiplayer.NativeVisit");
                var transfer = assembly.GetType("ProbablyMultiplayer.ItemTransfer");
                var sprite = assembly.GetType("ProbablyMultiplayer.PlayerSprite");
                var runtime = assembly.GetType("ProbablyMultiplayer.MultiplayerRuntime");
                active = AccessTools.Field(visit, "Active");
                externalConnection=AccessTools.Field(runtime,"Connection");externalSession=AccessTools.Field(runtime,"Session");
                disconnect=AccessTools.Method(runtime,"Disconnect",new[]{typeof(bool),typeof(bool)});
                locked = AccessTools.Property(transfer, "Locked");
                preview = AccessTools.Method(visit, "IsPreview", new[] { typeof(GameItem) });
                selected = AccessTools.Method(visit, "IsSelectedOriginal", new[] { typeof(GameItem) });
                registerPortrait = AccessTools.Method(sprite, "Register", new[] { typeof(string) });
                var prepare = AccessTools.Method(transfer, "Prepare");
                var update = AccessTools.Method(core, "Update");
                if (active == null || externalConnection==null || externalSession==null || disconnect==null || locked == null || preview == null || selected == null || registerPortrait == null || prepare == null || update == null)
                    throw new InvalidOperationException("Probably Multiplayer interface changed");
                patches = new Harmony(PatchId);
                patches.Patch(prepare, prefix: new HarmonyMethod(AccessTools.Method(typeof(ProbablyMultiplayerCompat), "BeforePrepare")));
                patches.Patch(update, prefix: new HarmonyMethod(AccessTools.Method(typeof(ProbablyMultiplayerCompat), "AllowUpdate")));
                bound = true;
                Debug.Log("[ShopTogether] Probably Multiplayer 2.6.1 compatibility bridge ready");
            } catch (Exception error) {
                if (patches != null) { patches.UnpatchAll(PatchId); patches = null; }
                failed = true;
                Debug.LogWarning("[ShopTogether] Probably Multiplayer compatibility unavailable: " + error.Message);
            }
        }
        internal static void Shutdown() {
            if (patches != null) patches.UnpatchAll(PatchId);
            patches = null; bound = failed = clientSuspended = false; active = externalConnection = externalSession = null; locked = null; preview = selected = registerPortrait = disconnect = null;
        }
        static void Fault(Exception error) {
            if (!failed) Debug.LogWarning("[ShopTogether] Probably Multiplayer compatibility error: " + error.Message);
            failed = true;
        }
        static bool AllowUpdate() {
            if(!ReplicaSession.ClientStore){clientSuspended=false;return true;}
            if(!clientSuspended||Time.realtimeSinceStartup>=nextClientCheck){
                clientSuspended=true;nextClientCheck=Time.realtimeSinceStartup+5f;
                try{if(disconnect!=null&&((externalConnection!=null&&externalConnection.GetValue(null)!=null)||(externalSession!=null&&externalSession.GetValue(null)!=null)))disconnect.Invoke(null,new object[]{false,true});}
                catch(Exception error){Debug.LogWarning("[ShopTogether] could not close friend's independent visitor connection: "+error.Message);}
            }
            return false;
        }
        // The other mod reserves the receiving inventory while preparing a deal. Do not start that
        // reservation over another cooperative player's existing hold; its normal failure path cancels it.
        static void BeforePrepare() {
            var session = Connection.Current;
            if (session == null || !session.IsHost || !session.Ready || session.Leases == null) return;
            foreach (var lease in session.Leases.Snapshot(Time.realtimeSinceStartup))
                if (lease.owner > 0) throw new InvalidOperationException("Another player is moving an item. Please try again.");
        }
        internal static bool Available { get {
            return bound && !failed;
        } }
        internal static bool VisitActive { get {
            if (!bound || failed) return false;
            try { return (bool)active.GetValue(null); } catch (Exception error) { Fault(error); return false; }
        } }
        internal static bool TradeLocked { get {
            if (!bound || failed) return false;
            try { return (bool)locked.GetValue(null, null); } catch (Exception error) { Fault(error); return false; }
        } }
        internal static bool Protected(GameItem item) {
            if (item == null) return false;
            if (!bound || failed) return failed && Enabled();
            try { return (bool)preview.Invoke(null, new object[] { item }) || (bool)selected.Invoke(null, new object[] { item }); }
            catch (Exception error) { Fault(error); return true; }
        }
        static bool IsPreview(GameItem item) {
            if (item == null || !bound || failed) return false;
            try { return (bool)preview.Invoke(null, new object[] { item }); }
            catch (Exception error) { Fault(error); return true; }
        }
        static bool IsSelected(GameItem item) {
            if (item == null || !bound || failed) return false;
            try { return (bool)selected.Invoke(null, new object[] { item }); }
            catch (Exception error) { Fault(error); return true; }
        }
        static bool Counter(GameInventory inventory) {
            var e=EmporiumEntry.Instance;
            return e!=null&&inventory!=null&&(inventory==e.frontInvinvElement||inventory==e.backInvinvElement||inventory==e.backInvinvElementCounter);
        }
        // A visitor preview may only travel intact between the two trade counters. It can never be put
        // in the shop, stacked, split, used, or moved as part of a group; the visitor mod still owns it.
        static bool AllowedPreviewRequest(Connection session,Request request,GameItem item) {
            if(item==null||!IsPreview(item)||!Counter(item.parentInventory)||request.items==null||request.items.Length!=1)return false;
            if(request.kind==RequestKind.Claim)return true;
            if(request.kind!=RequestKind.Move||request.moves==null||request.moves.Length!=1)return false;
            var move=request.moves[0];GameInventory target;
            return move!=null&&move.item==session.World.Id(item)&&move.count==item.unitCount&&move.target==0&&move.inventory!=null&&session.World.Inventories.TryGetValue(move.inventory.Key,out target)&&Counter(target);
        }
        // Vanilla's TryAcceptOnce and group placement bypass the visitor mod's AcceptUnchecked guard.
        // Keep the host's normal counter-to-counter selection, but never let a preview escape or split.
        internal static bool AllowsHostPlacement(SlotMarker slot, int amount) {
            if (!Enabled()) return true;
            if (!Available) return false;
            if (!VisitActive || slot == null) return !failed;
            if (TradeLocked || IsSelected(slot.item) || IsSelected(slot.targetItem) || IsPreview(slot.targetItem)) return false;
            if (!IsPreview(slot.item)) return !failed;
            var e = EmporiumEntry.Instance;
            if (e == null || slot.targetItem != null || (slot.inventory != e.frontInvinvElement && slot.inventory != e.backInvinvElement && slot.inventory != e.backInvinvElementCounter)) return false;
            int count = amount < 0 ? slot.numTransfer : Math.Min(amount, slot.numTransfer);
            return !failed && count >= slot.item.unitCount;
        }
        internal static bool InventoryProtected(GameInventory inventory) {
            if (inventory == null) return false;
            if (Enabled() && !Available) return true;
            if (!VisitActive) return false;
            foreach (var item in GraphUtils.FindChildrenType<GameItem>(inventory, null, null)) if (Protected(item)) return true;
            return false;
        }
        internal static bool BlockRemote(Connection session, int owner) {
            if (owner <= 0 || session == null || !session.IsHost || !Enabled()) return false;
            if (!Available) return true;
            bool tradeLocked = TradeLocked;
            return failed || tradeLocked;
        }
        internal static bool BlockRequest(Connection session, int owner, Request request) {
            if (owner <= 0 || !Enabled()) return false;
            if (BlockRemote(session, owner)) return true;
            if (VisitActive && request.kind == RequestKind.ShopAction) return true;
            if (request.items != null) foreach (int id in request.items) { GameItem item; if (session.World.Items.TryGetValue(id, out item) && Protected(item) && !AllowedPreviewRequest(session,request,item)) return true; }
            if (request.target > 0) { GameItem target; if (session.World.Items.TryGetValue(request.target, out target) && Protected(target)) return true; }
            if (request.inventory != null && request.inventory.container > 0) { GameItem container; if (session.World.Items.TryGetValue(request.inventory.container, out container) && Protected(container)) return true; }
            if (request.moves != null) foreach (var move in request.moves) {
                if (move == null) return true;
                GameItem item;
                if (move.target > 0 && session.World.Items.TryGetValue(move.target, out item) && Protected(item)) return true;
                if (move.inventory != null && move.inventory.container > 0 && session.World.Items.TryGetValue(move.inventory.container, out item) && Protected(item)) return true;
            }
            return false;
        }
        internal static void EnsurePortrait(string sprite) {
            if (!ReplicaSession.ClientStore || sprite != Portrait || !bound) return;
            if (SpriteDict.Instance != null && SpriteDict.Instance.spriteDictionary.ContainsKey(Portrait)) return;
            try { registerPortrait.Invoke(null, new object[] { "" }); } // Bundled default; do not fetch another player's image.
            catch (Exception error) { Diagnostics.Report("visitor portrait", error); }
        }
        internal static bool ReplicaVisitor {
            get {
                if (!ReplicaSession.ClientStore || PlayerStore.Instance == null || PlayerStore.Instance.currentClientInstance == null) return false;
                var customer = PlayerStore.Instance.currentClientInstance.GetClientBlueprint();
                return customer != null && customer.identifier == "lemocuber_probablymultiplayer_human";
            }
        }
#if LAN_PROBE
        internal static void ProbeBind() {
            active = AccessTools.Field(typeof(TestVisitState), "Active");
            locked = AccessTools.Property(typeof(TestVisitTransfer), "Locked");
            preview = AccessTools.Method(typeof(TestVisitState), "IsPreview");
            selected = AccessTools.Method(typeof(TestVisitState), "IsSelectedOriginal");
            registerPortrait = AccessTools.Method(typeof(TestVisitSprite), "Register");
            patches = new Harmony(PatchId);
            patches.Patch(AccessTools.Method(typeof(TestVisitTransfer), "Prepare"), prefix: new HarmonyMethod(AccessTools.Method(typeof(ProbablyMultiplayerCompat), "BeforePrepare")));
            patches.Patch(AccessTools.Method(typeof(TestVisitCore), "Update"), prefix: new HarmonyMethod(AccessTools.Method(typeof(ProbablyMultiplayerCompat), "AllowUpdate")));
            bound = true;
        }
#endif
    }

    // The visitor's custom trade window belongs to the host's market connection, not the replica.
    [HarmonyPatch]
    internal static class ReplicaVisitorTradePanel {
        static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(StoreTablet), "OnClick");
            yield return AccessTools.Method(typeof(NegociationUIManager), "OpenUI");
            yield return AccessTools.Method(typeof(BargainUIManager), "OpenUI");
            yield return AccessTools.Method(typeof(BargainUIManager), "ToggleUI");
        }
        [HarmonyPriority(Priority.First)] static bool Prefix() {
            if (!ProbablyMultiplayerCompat.ReplicaVisitor) return true;
            if (LocalControl.UserAction) VisitorShare.Open();
            return false;
        }
    }
}
