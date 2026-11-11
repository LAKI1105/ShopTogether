using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShopTogether {
    /// <summary>
    /// The counter shotgun. Raising, aiming and lowering it only change the screen of the player holding it; the
    /// customer still reacts on the host as in the base game. When a shot is fired, the shooter sees the game's own shot
    /// through the sights; everyone else sees the customer hit and fall at the counter without their screen changing
    /// (open bags, drags and menus stay as they are). The host runs the real shot (customer, security, reputation,
    /// achievements); the others only replay what it shows.
    /// </summary>
    internal static class ShotgunView {
        static readonly FieldInfo pendingSurvivedField = typeof(StoreUIManager).GetField("pendingSurvivedShot", BindingFlags.NonPublic | BindingFlags.Instance);

        // ---------------- host side ----------------
        internal static int Shots;
        internal static bool LastSurvived;
        /// <summary>Friends currently aiming (their player numbers).</summary>
        static readonly HashSet<int> aimers = new HashSet<int>();

        internal static void Capture(PhaseState state) {
            var ui = StoreUIManager.Instance;
            if (ui == null) return;
            state.body = ui.body != null && ui.body.activeSelf;
            state.shots = Shots;
            state.survived = LastSurvived;
        }

        static StoreClient Customer() { var store = PlayerStore.Instance; return store != null && store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null; }
        static bool AnyoneAiming(StoreUIManager ui) { return aimers.Count > 0 || ui.isShootingMode; }

        /// <summary>Host side: a friend raised the gun. The customer reacts as when the host draws it.</summary>
        internal static void Raise() {
            var ui = StoreUIManager.Instance; var gun = UnityEngine.Object.FindObjectOfType<Shotgun>(); var session = Connection.Current;
            if (ui == null || gun == null || session == null || ui.isShot || !gun.CanPickup()) return;
            bool first = !AnyoneAiming(ui);
            aimers.Add(RemoteActions.CurrentOwner);
            if (!first) return;
            var customer = Customer(); var dialog = DialogUIManager.Instance;
            if (ui.clientGameObject != null) ui.clientGameObject.SetImmediatlyArrive();
            if (dialog != null) {
                if (customer != null && customer.holsterResumesDialogue) dialog.PauseConversation();
                else { dialog.InterruptDialog(); dialog.RemoveCustomerDialogue(); }
            }
            session.PlayEffect(After(0.35f, () => { if (AnyoneAiming(ui) && !ui.isShot && DialogUIManager.Instance != null) DialogUIManager.Instance.StartDialogue(AugHelper.GetClientReactionUnholster()); }));
        }

        /// <summary>Host side: a friend lowered the gun without firing. When nobody is aiming any more, the customer
        /// carries on or leaves exactly as when the host holsters.</summary>
        internal static void Lower() {
            var ui = StoreUIManager.Instance;
            if (ui == null || !aimers.Remove(RemoteActions.CurrentOwner) || AnyoneAiming(ui) || ui.isShot) return;
            var customer = Customer();
            bool pendingSurvivor = pendingSurvivedField != null && pendingSurvivedField.GetValue(ui) != null;
            if (DialogUIManager.Instance == null || PlayerStore.Instance == null || customer == null) return;
            if (!pendingSurvivor && !DialogUIManager.Instance.ResumeConversation() && !customer.holsterResumesDialogue) PlayerStore.Instance.DismissCurrentClient();
        }

        /// <summary>Host side: an aiming friend pulled the trigger.</summary>
        internal static void Fire() {
            var ui = StoreUIManager.Instance;
            if (ui == null || !aimers.Contains(RemoteActions.CurrentOwner) || !ui.CanShoot() || Customer() == null) return;
            aimers.Clear();
            // The game's own shot. If the host is not aiming, the host's screen is left alone: the customer is hit and
            // falls in the shop view, and the game's "lower the gun" step at the end only closes the deal window.
            Headless = !ui.isShootingMode;
            ObserverDeath = Headless && !Customer().survivesShot;
            try { ui.Shoot(); } catch { Headless = false; throw; }
            if (LastSurvived) Headless = false;   // a survivor's shot never lowers the gun
        }

        internal static void ForgetAimers() { aimers.Clear(); }
        /// <summary>Host side: a friend's shot is running while the host is not aiming.</summary>
        internal static bool Headless;
        internal static bool ObserverDeath;

        static IEnumerator After(float seconds, Action action) { yield return new WaitForSeconds(seconds); action(); }

        // ---------------- shared screen switching ----------------
        static void CounterView(StoreUIManager ui, bool on) {
            ui.mainCamera.SetActive(!on); ui.shootingCamera.SetActive(on);
            ui.arm.SetActive(on); ui.inventoryCanvas.SetActive(!on); UIFamilyLayer.SetRaisedCanvasActive(!on);
            ui.worldInteractCanvas.SetActive(!on); ui.paperUICanvasObject.SetActive(!on);
            ui.shootingWoundRenderer.gameObject.SetActive(false); ui.shootingBodyRenderer.gameObject.SetActive(false);
            if (on) ui.body.SetActive(false); else ui.shootingUI.SetActive(false);
            if (DialogUIManager.Instance != null) { if (on) DialogUIManager.Instance.SetShootingMode(); else DialogUIManager.Instance.SetNormalMode(); }
        }

        // ---------------- friend side ----------------
        static bool aiming, shown, animating, primed;
        static int seen;
        internal static bool Aiming { get { return aiming; } }

        /// <summary>Friend side: this player clicked the shotgun.</summary>
        internal static void Toggle(Shotgun gun) {
            var ui = StoreUIManager.Instance;
            if (ui == null || animating) return;
            if (aiming) { LowerHere(ui, true); return; }
            if (gun == null || !gun.CanPickup() || HostPhase.Away) return;
            aiming = true; shown = true;
            ui.StartBlackscreenSequence(true, delegate {
                if (!aiming) return;
                CounterView(ui, true); ui.isShootingMode = true; ui.isShot = false;
                if (ui.clientGameObject != null && ui.clientGameObject.image != null) ui.clientGameObject.image.color = Color.white;
                AudioManager.Instance.Play2("shotgun_pump");
            }, delegate { if (aiming && !animating) ui.shootingUI.SetActive(true); }, 0.25f, 0.1f);
            RemoteActions.Forward(RemoteActions.Adapter("Raise", 0), null, new object[0]);
        }

        /// <summary>Friend side: lower the gun on this screen (and tell the host, unless the customer is already gone).</summary>
        internal static void LowerHere(StoreUIManager ui, bool tellHost) {
            if (!aiming) return;
            aiming = false;
            if (tellHost) RemoteActions.Forward(RemoteActions.Adapter("Lower", 0), null, new object[0]);
            BackToShop(ui, true);
        }

        internal static void FireHere() { if (aiming && !animating) RemoteActions.Forward(RemoteActions.Adapter("Fire", 0), null, new object[0]); }

        static void BackToShop(StoreUIManager ui, bool sound) {
            ui.StartBlackscreenSequence(true, delegate {
                if (aiming || animating) return;
                shown = false; CounterView(ui, false); ui.isShootingMode = false; ui.isShot = false;
                if (ui.clientGameObject != null && ui.clientGameObject.image != null && !(ui.body != null && ui.body.activeSelf)) ui.clientGameObject.image.color = Color.white;
                if (sound) AudioManager.Instance.Play2("shotgun_cock");
            }, null, 0.25f, 0.1f);
        }

        internal static void Follow(PhaseState state) {
            var ui = StoreUIManager.Instance;
            if (ui == null || state == null || !ReplicaSession.SceneReady) return;
            if (!primed) { primed = true; seen = state.shots; }
            if (state.shots != seen) { seen = state.shots; if (!animating) Play(ui, state.survived); }
            // The customer left while this player was still aiming: just put the gun away here.
            if (aiming && !animating && PlayerStore.Instance != null && PlayerStore.Instance.currentClientInstance == null) LowerHere(ui, false);
            if (!animating && !shown && ui.body != null && ui.body.activeSelf != state.body) ui.body.SetActive(state.body);
        }

        static void Play(StoreUIManager ui, bool survived) {
            var session = Connection.Current;
            if (session == null) return;
            animating = true;
            AudioManager.Instance.Play2("shotgun_fire");
            if (!shown) {
                // Watching, not aiming: this screen stays as it is (open bags, drags and menus are untouched); the customer
                // at the counter is hit, falls, and the body lies there, with the same sounds.
                session.PlayEffect(survived ? Flinch(ui) : Fall(ui));
                return;
            }
            aiming = false;
            ui.shootingUI.SetActive(false);
            ui.isShot = !survived;
            var arm = ui.arm != null ? ui.arm.GetComponent<Arm>() : null;
            if (arm != null) { arm.TriggerMuzzleFlash(); arm.TriggerRecoil(); }
            session.PlayEffect(survived ? Survived(ui) : Killed(ui));
        }

        // Bystanders: the shop view only, same timings as the game's own shot.
        static IEnumerator Fall(StoreUIManager ui) {
            var client = ui.clientGameObject;
            int frames = Mathf.Max(3, ui.shootingWoundSprites.Count);
            for (int i = 0; i < frames; i++) {
                if (client != null && client.image != null) {
                    if (i == 0) client.image.color = Color.white;
                    else if (i == 1) client.image.color = ui.shotDarkenColor;
                    else if (i == 2) client.gameObject.SetActive(false);
                }
                yield return new WaitForSeconds(ui.woundFrameDuration);
            }
            if (client != null) client.gameObject.SetActive(false);
            int collapse = Mathf.Max(0, ui.shootingBodySprites.Count - 2);
            for (int i = 0; i < ui.shootingBodySprites.Count; i++) {
                if (i == collapse) AudioManager.Instance.Play2("body_collapse_floor");
                yield return new WaitForSeconds(ui.bodyFrameDuration);
            }
            if (ui.body != null) ui.body.SetActive(true);
            if (client != null && client.image != null) client.image.color = Color.white;
            animating = false;
        }
        static IEnumerator Flinch(StoreUIManager ui) {
            var client = ui.clientGameObject;
            if (client != null) client.FreezeIdle();
            yield return new WaitForSeconds(0.6f);
            animating = false;
        }

        // Same frames and timings as the game's own shot.
        static IEnumerator Killed(StoreUIManager ui) {
            var client = ui.clientGameObject;
            ui.shootingWoundRenderer.gameObject.SetActive(true);
            bool fallen = false;
            for (int i = 0; i < ui.shootingWoundSprites.Count; i++) {
                ui.shootingWoundRenderer.sprite = ui.shootingWoundSprites[i];
                if (client != null) {
                    if (i == 0) client.image.color = Color.white;
                    else if (i == 1) client.image.color = ui.shotDarkenColor;
                    else if (i == 2) { fallen = true; client.gameObject.SetActive(false); }
                }
                yield return new WaitForSeconds(ui.woundFrameDuration);
            }
            ui.shootingWoundRenderer.gameObject.SetActive(false);
            if (!fallen && client != null) client.gameObject.SetActive(false);
            ui.shootingBodyRenderer.gameObject.SetActive(true);
            int collapse = Mathf.Max(0, ui.shootingBodySprites.Count - 2);
            for (int i = 0; i < ui.shootingBodySprites.Count; i++) {
                ui.shootingBodyRenderer.sprite = ui.shootingBodySprites[i];
                if (i == collapse) AudioManager.Instance.Play2("body_collapse_floor");
                yield return new WaitForSeconds(ui.bodyFrameDuration);
            }
            ui.body.SetActive(true);
            ui.shootingBodyRenderer.gameObject.SetActive(false);
            animating = false;
            BackToShop(ui, false);
        }

        static IEnumerator Survived(StoreUIManager ui) {
            var client = ui.clientGameObject;
            if (client != null) { client.FreezeIdle(); client.image.color = Color.white; }
            ui.shootingWoundRenderer.gameObject.SetActive(true);
            int frames = Mathf.Min(3, ui.shootingWoundSprites.Count);
            for (int i = 0; i < frames; i++) {
                ui.shootingWoundRenderer.sprite = ui.shootingWoundSprites[i];
                if (i < frames - 1) yield return new WaitForSeconds(ui.woundFrameDuration);
            }
            yield return new WaitForSeconds(0.6f);
            animating = false;
            BackToShop(ui, true);
        }

        internal static void Reset() {
            Headless = ObserverDeath = false;
            var ui = StoreUIManager.Instance;
            if (shown && ui != null) { try { CounterView(ui, false); ui.isShootingMode = false; ui.isShot = false; } catch (Exception e) { Debug.LogWarning("[ShopTogether] shotgun view: " + e.Message); } }
            aiming = shown = animating = primed = false; seen = 0; aimers.Clear();
        }
    }

    [HarmonyPatch(typeof(Shotgun), "OnClick")]
    internal static class ShotgunClick {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Shotgun __instance) {
            if (!ReplicaSession.ClientStore) return true;
            if (LocalControl.Focused) ShotgunView.Toggle(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(StoreUIManager), "Shoot")]
    internal static class ShotgunFire {
        [HarmonyPriority(Priority.First)]
        static bool Prefix() {
            if (ReplicaSession.ClientStore) {
                if (Input.GetMouseButtonDown(0) && LocalControl.Focused) ShotgunView.FireHere();
                return false;
            }
            var store = PlayerStore.Instance;
            var client = store != null && store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null;
            ShotgunView.LastSurvived = client != null && client.survivesShot;
            ShotgunView.Shots++;
            ShotgunView.ForgetAimers();
            return true;
        }
    }

    [HarmonyPatch(typeof(StoreUIManager), "ExitShootingMode")]
    internal static class ShotgunHolster {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(StoreUIManager __instance) {
            if (!ReplicaSession.ClientStore) {
                if (!ShotgunView.Headless) return true;
                ShotgunView.Headless = false;
                if (__instance.isShootingMode) return true;
                if (NegociationUIManager.Instance != null) NegociationUIManager.Instance.CloseUI();
                __instance.shootingUI.SetActive(false); __instance.shootingWoundRenderer.gameObject.SetActive(false);
                return false;
            }
            // Right click puts the gun away on this screen; the game's other callers wait for the host.
            if (ShotgunView.Aiming && Input.GetMouseButtonDown(1) && LocalControl.Focused) ShotgunView.LowerHere(__instance, true);
            return false;
        }
    }

    /// <summary>The gun's flash and recoil only play when the gun is on screen.</summary>
    [HarmonyPatch]
    internal static class HiddenArm {
        static IEnumerable<MethodBase> TargetMethods() { yield return typeof(Arm).GetMethod("TriggerRecoil"); yield return typeof(Arm).GetMethod("TriggerMuzzleFlash"); }
        static bool Prefix(Arm __instance) { return __instance != null && __instance.gameObject.activeInHierarchy; }
    }

    [HarmonyPatch(typeof(StoreUIManager), "EnterShootingMode")]
    internal static class ShotgunRaise {
        [HarmonyPriority(Priority.First)]
        static bool Prefix() { return !ReplicaSession.ClientStore; }
    }
}
