using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ShopTogether {
    internal static class CounterTransferIntent {
        internal static void TransferCounter(bool frontVisible) {
            var s = Connection.Current; var e = EmporiumEntry.Instance;
            if (s == null || e == null) { AuthorityIntent.Refuse("~SMC242~"); return; }
            if (DialogueIntent.Committing) { AuthorityIntent.Refuse("~SMC353~"); return; }
            string busy = HostPhase.Busy(s.Ready); if (busy != null) { AuthorityIntent.Refuse(busy); return; }
            var items = new List<GameItem>(); GameInventory destination = e.invElement;
            if (frontVisible && e.frontInvinvElement.childItems.Count != 0) {
                items.AddRange(e.frontInvinvElement.childItems); destination = e.backInvinvElement;
            } else {
                foreach (var item in e.backInvinvElement.childItems) if (GeneralHelper.IsItemOwned(item)) items.Add(item);
                if (frontVisible) foreach (var item in e.frontInvinvElement.childItems) if (GeneralHelper.IsItemOwned(item)) items.Add(item);
            }
            if (items.Count == 0) { AuthorityIntent.Refuse("~SMC321~"); return; }
            int owner = Math.Max(0,RemoteActions.CurrentOwner);
            foreach (var item in items) {
                if (!AuthorityIntent.Free(s,owner,s.World.Id(item))) return;
                foreach (var child in item.FindAllChildItems()) if (!AuthorityIntent.Free(s,owner,s.World.Id(child))) return;
            }
            bool remainder = false;
            foreach (var item in items) if (GraphUtils.TryAcceptAll(destination,item) != 0) remainder = true;
            if (remainder) AuthorityIntent.Refuse("~SMC314~");
        }
    }
    [HarmonyPatch(typeof(StoreTransferButton),"OnClick")]
    internal static class CounterTransferClick {
        static bool Prefix() {
            var s = Connection.Current;
            if (s == null || !s.Ready || WorldReplica.Applying || NativeActions.Executing || RemoteActions.CurrentOwner > 0) return !ReplicaSession.IsReplica;
            if (!LocalControl.UserAction) return !s.IsClient;
            var e = EmporiumEntry.Instance;
            bool front = e != null && e.frontInvWindow != null && e.frontInvWindow.IsVisible();
            if (s.IsClient) RemoteActions.Forward(RemoteActions.Adapter("TransferCounter",1),null,new object[] { front });
            else { RemoteActions.PendingNote = null; CounterTransferIntent.TransferCounter(front); string note = RemoteActions.TakeNote(); if (note != null) RemoteActions.Notice(note); }
            return false;
        }
    }
}
