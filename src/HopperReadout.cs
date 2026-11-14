using System.Reflection;
using HarmonyLib;
using UnityEngine.UI;

namespace ShopTogether {
    // This custom window is not part of item.contentWindow. Refresh presentation
    // from replicated tags without invoking any slider/toggle gameplay callback.
    internal static class HopperReadout {
        static readonly FieldInfo open = AccessTools.Field(typeof(HopperHelper), "openItem");
        static readonly MethodInfo orderText = AccessTools.Method(typeof(HopperHelper), "OrderText");
        static readonly MethodInfo keepText = AccessTools.Method(typeof(HopperHelper), "KeepText");
        static readonly MethodInfo refreshLink = AccessTools.Method(typeof(HopperHelper), "RefreshLink");
        static readonly MethodInfo rebuildList = AccessTools.Method(typeof(HopperHelper), "RebuildList");
        static GameItem lastItem;
        static string lastFilter;
        internal static void Tick() {
            var item = open.GetValue(null) as GameItem;
            var window = CustomUIManager.Instance == null ? null : CustomUIManager.Instance.GetWindow("intake_hopper");
            if (item == null || window == null || !window.IsVisible) { lastItem = null; return; }
            window.Get("order").SetText((string)orderText.Invoke(null,new object[]{item}));
            window.Get("keep").SetText((string)keepText.Invoke(null,new object[]{item}));
            var sliders = window.ContentRoot.GetComponentsInChildren<Slider>(true);
            if (sliders.Length == 2) {
                sliders[0].SetValueWithoutNotify(HopperHelper.GetOrder(item));
                sliders[1].SetValueWithoutNotify(HopperHelper.GetKeep(item));
            }
            var toggles = window.ContentRoot.GetComponentsInChildren<Toggle>(true);
            if (toggles.Length == 5) {
                toggles[0].SetIsOnWithoutNotify(HopperHelper.IsOnAdd(item));
                toggles[1].SetIsOnWithoutNotify(HopperHelper.IsOnMove(item));
                for (int i=0;i<3;i++) toggles[i+2].SetIsOnWithoutNotify(HopperHelper.GetFilterMode(item)==i);
            }
            refreshLink.Invoke(null,null);
            // Stale link state may change the hint, never prevent the request.
            window.Get("unlink").SetInteractable(true);
            string filter=item.GetTagReadonly(HopperHelper.FILTER_STRING).GetString()+"|"+HopperHelper.GetFilterMode(item);
            if (item != lastItem || filter != lastFilter) { rebuildList.Invoke(null,null); lastItem=item; lastFilter=filter; }
        }
    }
}
