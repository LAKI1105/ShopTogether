using System;
using System.Collections.Generic;

namespace ShopTogether {
    internal enum RequestKind { Claim=1, Release=2, Renew=3, Move=4, ItemAction=5, ShopAction=6, AutoPlace=7, Sort=8 }
    internal enum ItemOperation { Activate=1, Toggle=2, Unload=3, Use=4, Equip=5, Target=6, Pin=7, Configure=8 }
    internal enum ShopOperation { NextCustomer=1, Buy=2, Sell=3, BuyAll=4, SellAll=5, AdvanceDialogue=6, ChooseDialogue=7 }
    [Serializable] internal sealed class InventoryAddress { public int root, container, ordinal; public string Key { get { return root+":"+container+":"+ordinal; } } }
    [Serializable] internal sealed class Placement { public int item, count, x, y, rotation, target; public bool flip; public long version, lease; public InventoryAddress inventory; }
    [Serializable] internal sealed class Request { public long sequence; public RequestKind kind; public int[] items; public Placement[] moves; public long lease; public int operation, target, index; public long worldVersion; public InventoryAddress inventory; }
    [Serializable] internal sealed class LeaseReply { public long sequence, token; public int owner; public int[] items; public bool granted; public string reason; }
    [Serializable] internal sealed class LeaseState { public int item, owner; public long token; }
    internal sealed class LeaseBook {
        sealed class Lease { internal int owner; internal long token; internal double until; }
        readonly Dictionary<int,Lease> leases=new Dictionary<int,Lease>();
        readonly Func<int,int,bool> related;
        long nextToken;
        internal LeaseBook(Func<int,int,bool> related) { this.related=related; }
        internal void Expire(double now) { foreach(var p in new List<KeyValuePair<int,Lease>>(leases)) if(p.Value.until<=now)leases.Remove(p.Key); }
        internal bool Claim(int owner,int[] ids,double now,out long token) {
            token=0; Expire(now);
            if(ids==null || ids.Length<1 || ids.Length>128)return false;
            var unique=new HashSet<int>(ids);if(unique.Count!=ids.Length)return false;
            foreach(int id in ids) {
                if(id<=0)return false;
                foreach(var p in leases)if(p.Value.owner!=owner && (p.Key==id || related(p.Key,id)))return false;
            }
            token=++nextToken;
            foreach(int id in ids)leases[id]=new Lease{owner=owner,token=token,until=now+20};
            return true;
        }
        internal bool Owns(int owner,int id,long token,double now) { Expire(now);Lease l;return leases.TryGetValue(id,out l)&&l.owner==owner&&l.token==token; }
        internal bool Available(int owner,int id,double now) { Expire(now);foreach(var p in leases)if(p.Value.owner!=owner&&(p.Key==id||related(p.Key,id)))return false;return true; }
        /// <summary>The other player holding this item (or its container/contents), or -1 when it is free for the requester.</summary>
        internal int OwnerOf(int requester,int id,double now) { Expire(now);foreach(var p in leases)if(p.Value.owner!=requester&&(p.Key==id||related(p.Key,id)))return p.Value.owner;return -1; }
        internal void Renew(int owner,double now) { foreach(var p in leases)if(p.Value.owner==owner)p.Value.until=now+20; }
        internal void Release(int owner,int[] ids) { if(ids==null)return;foreach(int id in ids){Lease l;if(leases.TryGetValue(id,out l)&&l.owner==owner)leases.Remove(id);} }
        internal void Disconnect(int owner) { foreach(var p in new List<KeyValuePair<int,Lease>>(leases))if(p.Value.owner==owner)leases.Remove(p.Key); }
        internal LeaseState[] Snapshot(double now) { Expire(now);var result=new List<LeaseState>();foreach(var p in leases)result.Add(new LeaseState{item=p.Key,owner=p.Value.owner,token=p.Value.token});return result.ToArray(); }
    }
}
