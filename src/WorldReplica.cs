using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShopTogether {
    [Serializable] internal sealed class ItemRecord { public int id; public long version; public InventoryAddress parent; public SaveItemNode node; public ItemFeature[] features; }
    [Serializable] internal sealed class GridRecord { public InventoryAddress address; public int width,height; }
    [Serializable] internal sealed class WorldDelta { public long version;public bool full;public int player;public ItemRecord[] items;public int[] removed;public GridRecord[] grids;public string store,station;public LeaseState[] leases;public StoreView presentation; }
    internal sealed class WorldReplica {
        internal readonly Dictionary<int,GameItem> Items=new Dictionary<int,GameItem>();
        internal readonly Dictionary<string,GameInventory> Inventories=new Dictionary<string,GameInventory>();
        readonly Dictionary<GameInventory,InventoryAddress> addresses=new Dictionary<GameInventory,InventoryAddress>();
        readonly Dictionary<int,string> signatures=new Dictionary<int,string>();
        readonly Dictionary<GameItem,int> itemIds=new Dictionary<GameItem,int>();
        int nextId;bool replicaMode;
        internal readonly Dictionary<int,long> Versions=new Dictionary<int,long>();
        internal long Version;internal StorePresentation Presentation=new StorePresentation();
        string lastStore,lastStation;float nextState;
        // Host: items to compare first (just changed), the round-robin position, and each item's last sent fingerprint.
        readonly HashSet<int> dirty=new HashSet<int>();readonly List<int> order=new List<int>();int orderCursor;readonly Dictionary<int,long> fingerprints=new Dictionary<int,long>();
        internal void MarkDirty(GameItem item){int id;if(item==null)return;if(itemIds.TryGetValue(item,out id))dirty.Add(id);else structureDirty=true;}
        // Host side: walking the whole shop only when its shape may have changed. Inventory addresses are (container
        // item id, inventory number inside that container), so they do not shift when items move; a new walk is needed
        // only for new or removed items and containers. It happens when an unknown item changes, after a friend's
        // request, when any known item sits outside the known inventories, and at least every two seconds.
        bool structureDirty=true;float lastIndex=-100f;
        internal void Refresh(bool force=false){
            if(replicaMode||force||structureDirty||Time.realtimeSinceStartup-lastIndex>=2f||StaleParents()){Index();return;}
        }
        bool StaleParents(){foreach(var p in Items){var item=p.Value;if(item==null||item.parentInventory==null||!addresses.ContainsKey(item.parentInventory))return true;}return false;}
        internal static bool Applying;
        /// <summary>
        /// Friend: placing or removing an item runs the game's container callbacks (feed dispensers blend what is
        /// put in, traps and cages react, machines spawn or tag things). The host already ran them and sent their
        /// results, so while one of the host's moves is applied here only that move itself may change the world:
        /// anything the callbacks try to move, spawn, destroy or tag is skipped. Display updates still run.
        /// </summary>
        internal static int Sandbox; internal static GameItem SandboxItem; internal static bool SandboxMovePermit;
        static bool Placed(GameItem item,Func<bool> move){var was=SandboxItem;bool permit=SandboxMovePermit;Sandbox++;SandboxItem=item;SandboxMovePermit=true;try{return move();}finally{Sandbox--;SandboxItem=was;SandboxMovePermit=permit;}}
        static void Remove(GameItem item){if(item==null)return;var inv=item.parentInventory;if(inv!=null)Placed(item,()=>inv.Expel(item));item.Destroy();}
        internal void Reset(){structureDirty=true;LastSentVersion=0;StoreAppliedAt=0;StationAppliedAt=0;Items.Clear();Inventories.Clear();addresses.Clear();signatures.Clear();Versions.Clear();itemIds.Clear();RemoteLeases.Clear();nextId=0;replicaMode=false;Version=0;Presentation=new StorePresentation();lastStore=null;lastStation=null;nextState=0;dirty.Clear();order.Clear();orderCursor=0;fingerprints.Clear();}
        internal int Id(GameItem item){int id;return item!=null&&itemIds.TryGetValue(item,out id)?id:0;}
        internal void Prime(WorldDelta seed){
            if(seed==null||!seed.full||seed.store==null||seed.station==null)throw new InvalidOperationException("~SMC117~");
            Applying=true;
            try{StoreState.Apply(PlayerStore.Instance,Convert.FromBase64String(seed.store));PlayerStore.Instance.saveSlotId=-777;StoreState.ApplyStation(StoreStation.Instance,Convert.FromBase64String(seed.station));}
            finally{Applying=false;}
        }
        internal static IEnumerable<KeyValuePair<int,GameInventory>> Roots() {
            var e=EmporiumEntry.Instance;if(e==null)yield break;
            GameInventory[] roots={e.invElement,e.backInvinvElement,e.frontInvinvElement,e.showcaseElement,e.soldElement,e.hiddenElement,e.hirelingInv,e.vendingMachineElement,e.vendingFountainElement,e.cassettePlayerElement,e.docInvElement,e.trashInvElement,e.faucetElement,e.drainInvElement,StorePresentation.CustomerInventory()};
            for(int i=0;i<roots.Length;i++)if(roots[i]!=null)yield return new KeyValuePair<int,GameInventory>(i+1,roots[i]);
            foreach(var extra in OutingKits.Roots())yield return extra;   // outing grounds and pockets (host and friends)
        }
        internal void Index() {
            Items.Clear();Inventories.Clear();addresses.Clear();structureDirty=false;lastIndex=Time.realtimeSinceStartup;
            var visited=new HashSet<GraphNodeStorage>();
            foreach(var root in Roots())Walk(root.Value,root.Key,0,new int[]{0},visited);
            foreach(var pair in new List<KeyValuePair<GameItem,int>>(itemIds))if(!Items.ContainsKey(pair.Value))itemIds.Remove(pair.Key);
        }
        internal void Committed(Request request){structureDirty=true;long revision=++Version;var changed=new HashSet<int>(request.items??new int[0]);if(request.target>0)changed.Add(request.target);if(request.moves!=null)foreach(var move in request.moves)if(move.target>0)changed.Add(move.target);foreach(int id in changed){Versions[id]=revision;signatures[id]=null;}}
        void Walk(GraphNodeStorage node,int root,int owner,int[] ordinal,HashSet<GraphNodeStorage> visited) {
            if(node==null||!visited.Add(node))return;
            var item=node as GameItem;
            if(item!=null){int id;if(!itemIds.TryGetValue(item,out id)){if(replicaMode)return;id=++nextId;itemIds.Add(item,id);}Items[id]=item;if(!replicaMode&&item.uniqueId==0&&item.contentWindow!=null&&PlayerStore.Instance!=null)item.GetUniqueID();owner=id;ordinal=new int[]{0};}
            // Containers get their save-wide number now, on the host, so a friend can add them to storage-coordinator presets.
            var inventory=node as GameInventory;
            if(inventory!=null){var address=new InventoryAddress{root=root,container=owner,ordinal=ordinal[0]++};addresses[inventory]=address;Inventories[address.Key]=inventory;}
            foreach(var child in node.children)Walk(child,root,owner,ordinal,visited);
        }
        internal InventoryAddress Address(GameInventory inventory){InventoryAddress a;return inventory!=null&&addresses.TryGetValue(inventory,out a)?a:null;}
        /// <summary>Friend side: who is holding which item right now, as last reported by the host.</summary>
        internal readonly Dictionary<int,int> RemoteLeases=new Dictionary<int,int>();
        internal bool Related(int a,int b) {
            GameItem left,right;if(!Items.TryGetValue(a,out left)||!Items.TryGetValue(b,out right))return false;
            return Contains(left,right)||Contains(right,left);
        }
        static bool Contains(GameItem parent,GameItem child){var inv=child.parentInventory;int limit=128;while(inv!=null&&limit-->0){var p=inv.GetParentItem();if(p==null)return false;if(p==parent)return true;inv=p.parentInventory;}return false;}
        internal WorldDelta Capture(bool full,int player,LeaseBook leases,double now) {
#if LAN_PROBE
            var captureTimer=System.Diagnostics.Stopwatch.StartNew();
#endif
            Refresh(full);long version=++Version;var changed=new List<ItemRecord>();var removed=new List<int>();
            // A big store has thousands of items: a normal update compares new and just-changed items first, then
            // as many others as fit in a few milliseconds, continuing where the last update stopped.
            var timer=System.Diagnostics.Stopwatch.StartNew();var done=new HashSet<int>();
            Action<int,GameItem> check=(id,item)=>{
                if(!done.Add(id))return;
                SaveItemNode node=EncodeItem(item,id);
                // Relationships are explicit inventory addresses; never deserialize an arbitrary graph from a client.
                node.childItems=new List<long>();node.childItemInventoryNode=new List<int>();node.tempLink=null;
                var record=new ItemRecord{id=id,parent=Address(item.parentInventory),node=node,features=item.itemFeatures.ToArray()};
                string signature=WireJson.ToJson(record);string old;
                if(!signatures.TryGetValue(id,out old)||old!=signature){Versions[id]=version;signatures[id]=signature;changed.Add(record);try{fingerprints[id]=Fingerprint.Item(this,id,item);}catch{fingerprints.Remove(id);}}else if(full)changed.Add(record);
                record.version=Versions[id];
            };
            if(full){foreach(var pair in Items)check(pair.Key,pair.Value);}
            else{
                foreach(var pair in Items){string s;if(!signatures.TryGetValue(pair.Key,out s)||s==null)check(pair.Key,pair.Value);}
                foreach(int id in dirty){GameItem item;if(Items.TryGetValue(id,out item))check(id,item);}
                if(order.Count!=Items.Count){order.Clear();order.AddRange(Items.Keys);if(orderCursor>=order.Count)orderCursor=0;}
                for(int k=0;k<order.Count&&timer.Elapsed.TotalMilliseconds<4;k++){int id=order[orderCursor];orderCursor=(orderCursor+1)%order.Count;GameItem item;if(Items.TryGetValue(id,out item))check(id,item);}
            }
            dirty.Clear();
            foreach(int id in new List<int>(signatures.Keys))if(!Items.ContainsKey(id)){removed.Add(id);signatures.Remove(id);Versions.Remove(id);fingerprints.Remove(id);}
            var grids=new List<GridRecord>();foreach(var p in addresses){var grid=p.Key as GameGridInventory;if(grid!=null)grids.Add(new GridRecord{address=p.Value,width=grid.inventoryShape.width,height=grid.inventoryShape.height});}
            // The game's typed save codec operates on memory only. No network-supplied file path is accepted.
            // Store and station data: twice a second is plenty for counters, cash and services.
            string store=lastStore,station=lastStation;
            if(full||store==null||station==null||UnityEngine.Time.realtimeSinceStartup>=nextState){nextState=UnityEngine.Time.realtimeSinceStartup+.5f;store=Convert.ToBase64String(StoreState.Serialize(PlayerStore.Instance));station=Convert.ToBase64String(StoreState.SerializeStation(StoreStation.Instance));}
            var result=new WorldDelta{version=version,full=full,player=player,items=changed.ToArray(),removed=removed.ToArray(),grids=grids.ToArray(),leases=leases.Snapshot(now),presentation=Presentation.Capture(this),store=full||store!=lastStore?store:null,station=full||station!=lastStation?station:null};
            lastStore=store;lastStation=station;
#if LAN_PROBE
            if(!full)RuntimeProbe.CaptureMilliseconds.Add(captureTimer.Elapsed.TotalMilliseconds);
#endif
            return result;
        }
        // ---- drift check support ----
        internal long LastSentVersion,StoreAppliedAt,StationAppliedAt;
        internal bool ChangedSince(int id,long version){long v;return Versions.TryGetValue(id,out v)&&v>version;}
        internal Digest BuildDigest(){
            var ids=new List<int>(Items.Count);var hashes=new List<long>(Items.Count);
            foreach(var p in Items){if(p.Value==null)continue;long h;if(!fingerprints.TryGetValue(p.Key,out h))continue;hashes.Add(h);ids.Add(p.Key);}
            return new Digest{version=LastSentVersion,slot=PlayerStore.Instance!=null?PlayerStore.Instance.saveSlotId:0,store=Fingerprint.Hash(lastStore),station=Fingerprint.Hash(lastStation),ids=ids.ToArray(),hashes=hashes.ToArray()};
        }
        internal void ForceResend(ResyncRequest request){
            if(request==null)return;
            if(request.ids!=null)foreach(int id in request.ids)signatures.Remove(id);
            if(request.store)lastStore=null;
            if(request.station)lastStation=null;
        }
        internal long StoreHash(int hostSlot){
            var store=PlayerStore.Instance;if(store==null)return 0;int slot=store.saveSlotId;
            try{store.saveSlotId=hostSlot;return Fingerprint.Hash(Convert.ToBase64String(StoreState.Serialize(store)));}
            catch{return 0;}finally{store.saveSlotId=slot;}
        }
        internal long StationHash(){var station=StoreStation.Instance;if(station==null)return 0;try{return Fingerprint.Hash(Convert.ToBase64String(StoreState.SerializeStation(station)));}catch{return 0;}}
        /// <summary>Items under the store's inventories that never came from the host (replica side only).</summary>
        internal List<GameItem> Untracked(){
            var found=new List<GameItem>();if(!replicaMode)return found;
            var all=new Dictionary<GameItem,int>();var visited=new HashSet<GraphNodeStorage>();
            foreach(var root in Roots())Collect(root.Value,root.Key,all,visited);
            foreach(var item in all.Keys)if(item!=null&&!itemIds.ContainsKey(item))found.Add(item);
            return found;
        }
        internal int RemoveLocal(List<GameItem> items){
            int n=0;Applying=true;
            try{foreach(var item in items){if(item==null)continue;try{if(item.parentInventory!=null)item.parentInventory.Expel(item);item.Destroy();n++;}catch(Exception e){Debug.LogWarning("[ShopTogether] could not remove local item: "+e.Message);}}}
            finally{Applying=false;}
            Index();return n;
        }

        static SaveItemNode EncodeItem(GameItem item,int id){
            // SaveManager.EncodeNodes appends to the host's savedItemFeatureList. A network snapshot must be read-only.
            return new SaveItemNode{identifier=item.identifier,unitCount=item.unitCount,uuid=id,itemType=item.GetType().FullName,itemState=item.state.Clone(),itemShape=new GridShapeBuilder(item.shape),itemModifiedShape=new GridShapeBuilder(item.modifiedShape),name=item.name,shortDescription=item.shortDescription,flavorText=item.flavorText,uniqueId=item.uniqueId,unitBaseValue=item.unitBaseValue,unitValue=item.unitValue,lateUnitValue=item.lateUnitValue,backupUnitValue=item.backupUnitValue,spriteAtlasPath=item.spriteAtlasPath,spritePath=item.spritePath,itemTypes=new List<string>(item.itemTypes),childItems=new List<long>(),childItemInventoryNode=new List<int>()};
        }
        static void Collect(GraphNodeStorage node,int root,Dictionary<GameItem,int> items,HashSet<GraphNodeStorage> visited){if(node==null||!visited.Add(node))return;var item=node as GameItem;if(item!=null)items[item]=root;foreach(var child in node.children)Collect(child,root,items,visited);}
        void BindInitial(WorldDelta delta){
            var native=new Dictionary<GameItem,int>();var visited=new HashSet<GraphNodeStorage>();foreach(var root in Roots())Collect(root.Value,root.Key,native,visited);
            var used=new HashSet<GameItem>();
            foreach(var r in delta.items){GameItem found=null;foreach(var pair in native)if(!used.Contains(pair.Key)&&pair.Key.identifier==r.node.identifier&&pair.Value==r.parent.root&&(pair.Key.uniqueId==r.node.uniqueId||pair.Key.uniqueId==0)){found=pair.Key;break;}if(found!=null){used.Add(found);itemIds[found]=r.id;}}
            foreach(var item in native.Keys)if(!used.Contains(item))item.Destroy();
        }
        internal void Apply(WorldDelta delta) {
            if(delta==null||delta.items==null||delta.items.Length>200000||delta.version<=Version)throw new InvalidOperationException("~SMC074~");
            Applying=true;
            try {
                if(delta.store!=null){int slot=PlayerStore.Instance.saveSlotId;StoreState.Apply(PlayerStore.Instance,Convert.FromBase64String(delta.store));PlayerStore.Instance.saveSlotId=slot;StoreAppliedAt=delta.version;}
                if(delta.station!=null){StoreState.ApplyStation(StoreStation.Instance,Convert.FromBase64String(delta.station));StationAppliedAt=delta.version;}
                Presentation.ApplyCustomer(delta.presentation);var incoming=new HashSet<int>();foreach(var r in delta.items){if(r.id<=0||r.node==null||!incoming.Add(r.id)||r.parent==null)throw new InvalidOperationException("~SMC100~");}
                if(!replicaMode){if(!delta.full)throw new InvalidOperationException("~SMC118~");itemIds.Clear();BindInitial(delta);replicaMode=true;}
                Index();
                if(delta.full){foreach(var p in new List<KeyValuePair<int,GameItem>>(Items))if(!incoming.Contains(p.Key))Remove(p.Value);Index();}
                else if(delta.removed!=null)foreach(int id in delta.removed){GameItem item;if(Items.TryGetValue(id,out item)){Remove(item);Items.Remove(id);Versions.Remove(id);}}
                foreach(var r in delta.items){GameItem item;if(!Items.TryGetValue(r.id,out item)){
                        r.node.childItems=new List<long>();r.node.childItemInventoryNode=new List<int>();
                        item=SaveManager.DecodeNodes(new List<SaveItemNode>{r.node});if(item==null)throw new InvalidOperationException("~SMC119~"+r.node.identifier);Items[r.id]=item;itemIds[item]=r.id;
                    }
                    if(item.identifier!=r.node.identifier)throw new InvalidOperationException("~SMC099~");
                    item.state=r.node.itemState;item.shape=r.node.itemShape;item.modifiedShape=r.node.itemModifiedShape;
                    item.unitCount=r.node.unitCount;item.unitBaseValue=r.node.unitBaseValue;item.unitValue=r.node.unitValue;item.lateUnitValue=r.node.lateUnitValue;item.backupUnitValue=r.node.backupUnitValue;
                    item.name=r.node.name;item.shortDescription=r.node.shortDescription;item.flavorText=r.node.flavorText;
                    item.itemTypes=r.node.itemTypes;item.itemFeatures=new List<ItemFeature>(r.features??new ItemFeature[0]);Versions[r.id]=r.version;
                }
                // Newly created containers must be indexed before their children are attached.
                var visited=new HashSet<GraphNodeStorage>();foreach(var root in Roots())Walk(root.Value,root.Key,0,new int[]{0},visited);
                foreach(var r in delta.items){GameItem item=Items[r.id];Walk(item,r.parent.root,r.id,new int[]{0},visited);}
                if(delta.grids!=null)foreach(var g in delta.grids){GameInventory inv;if(g.width<1||g.height<1||g.width>512||g.height>512)throw new InvalidOperationException("~SMC112~");if(Inventories.TryGetValue(g.address.Key,out inv)){var grid=inv as GameGridInventory;if(grid!=null&&(grid.inventoryShape.width!=g.width||grid.inventoryShape.height!=g.height))grid.SetShape(g.width,g.height);}}
                foreach(var r in delta.items){GameItem item=Items[r.id];GameInventory inv;if(!Inventories.TryGetValue(r.parent.Key,out inv))throw new InvalidOperationException("~SMC069~");if(item.parentInventory!=inv){if(item.parentInventory!=null)Placed(item,()=>item.parentInventory.Expel(item));if(!Placed(item,()=>inv.UncheckedAccept(item)))throw new InvalidOperationException("~SMC142~");}item.Validate();}
                // Tags the game reads (custom names, conditions...) live in each item's derived state, which the game
                // recomputes from its own and its children's state. Recompute it for every applied item and its containers.
                var derived=new HashSet<GameItem>();foreach(var r in delta.items){GameItem item;if(!Items.TryGetValue(r.id,out item))continue;int limit=64;while(item!=null&&limit-->0&&derived.Add(item)){var inv=item.parentInventory;item=inv==null?null:inv.GetParentItem() as GameItem;}}
                foreach(var item in derived){try{item.modifiedState=item.ComputeModifiedShapeState(null,null,null,null,null).Item4;}catch(Exception e){Debug.LogWarning("[ShopTogether] derived state: "+e.Message);}}
                if(delta.leases!=null){RemoteLeases.Clear();foreach(var l in delta.leases)if(l!=null)RemoteLeases[l.item]=l.owner;}
                Version=delta.version;Index();Presentation.ApplyDialogue(delta.presentation,this);
                ReplicaVisuals.Refresh(delta.store!=null||delta.full);
            } finally {Applying=false;}
        }
    }
}
