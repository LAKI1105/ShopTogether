using HarmonyLib;
namespace ShopTogether {
    [HarmonyPatch]
    internal static class ObserverAimLayers {
        static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods() {
            foreach(var name in new[]{"WoundAnimationRoutine","SurvivedShotRoutine","BodyFallRoutine"}) yield return AccessTools.Method(typeof(StoreUIManager),name);
        }
        static void Postfix(StoreUIManager __instance,System.Reflection.MethodBase __originalMethod, ref System.Collections.IEnumerator __result) {
            if(!ShotgunView.Headless || __instance.isShootingMode) return;
            __result=Hidden(__result,__originalMethod.Name=="BodyFallRoutine"?__instance.shootingBodyRenderer:__instance.shootingWoundRenderer);
        }
        static System.Collections.IEnumerator Hidden(System.Collections.IEnumerator original,UnityEngine.SpriteRenderer layer) {
            bool enabled=layer.enabled; layer.enabled=false;
            try { while(original.MoveNext()) yield return original.Current; }
            finally { if(layer!=null){layer.gameObject.SetActive(false);layer.enabled=enabled;} var disposable=original as System.IDisposable;if(disposable!=null)disposable.Dispose(); }
        }
    }
    [HarmonyPatch(typeof(EmporiumEntry), "DeselectDragItem")]
    internal static class PreserveObserverDrag {
        internal static int CounterCleanupDepth;
        static bool Prefix() { return CounterCleanupDepth == 0; }
    }
    [HarmonyPatch(typeof(PlayerStore), "RemoveNotOwnedItemFromTable")]
    internal static class TargetedCustomerCleanup {
        static void Prefix(out bool __state) { __state = Connection.Current != null && Connection.Current.Ready; if (__state) PreserveObserverDrag.CounterCleanupDepth++; }
        static System.Exception Finalizer(System.Exception __exception, bool __state) { if (__state) PreserveObserverDrag.CounterCleanupDepth--; return __exception; }
    }
    [HarmonyPatch(typeof(AugHelper), "OnShootoutLeave")]
    internal static class ObserverSecurityCleanup {
        static bool Prefix() {
            if (!ShotgunView.ObserverDeath || ReplicaSession.ClientStore) return true;
            var ui = StoreUIManager.Instance;
            if (ui == null || ui.isShootingMode) return true;
            try { ui.body.SetActive(false); ui.OnArrestDialoguePlayed(); }
            finally { ShotgunView.ObserverDeath = false; }
            return false;
        }
    }
    [HarmonyPatch(typeof(GameItemElement), "Destroy")]
    internal static class ReleaseDestroyedItem {
        static void Prefix(GameItem __instance) {
            var session = Connection.Current;
            if (session == null || !session.Ready || session.World == null) return;
            int id = session.World.Id(__instance);
            if (id > 0) NativeActions.CancelItems(new int[] { id });
        }
    }
}
