using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ShopTogether {
    // ------------------------------------------------------------------------------------------
    // Mod parity. Gameplay and content mods must be identical on every machine: content mods
    // define the items the host sends, gameplay mods must at least exist so their item types and
    // rules resolve the same way. Pure presentation mods (translations, portraits) may differ.
    // ------------------------------------------------------------------------------------------
    internal static class ModParity {
        // Mods that only change how things look or read, never what is in the world.
        static readonly HashSet<string> PresentationOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "seacucu.zh-tw", "hd_character_portraits", "Alley42"
        };
        // Other multiplayer mods are never part of the comparison (they must simply be off).
        static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "shop_together", "local.lan_shared_shop", "humangenome_accomplice", "shop_coop", "local.shop_coop",
            OtherMultiplayer.ModId   // paused while the shop is shared, so it may differ
        };

        /// <summary>
        /// A mod without any code (only pictures, sounds or translations) cannot change what happens in the shop,
        /// so it may differ between players. Mods with code are compared, because code can add items, resize
        /// inventories or change rules, and the friend's machine must be able to show exactly the host's shop.
        /// </summary>
        internal static bool LooksOnly(LoadedMod mod) { return (mod.Assembly == null && (mod.Manifest.Assemblies == null || mod.Manifest.Assemblies.Count == 0)) || LooksByName(mod.Manifest.ID, mod.Manifest.Name); }

        // Mods that say they only change looks (portraits, shop themes, skins, translations, sounds). The friend's
        // machine never runs shop logic and shows the host's items, so players may use different looks.
        static readonly string[] LookWords = { "portrait", "theme", "skin", "texture", "retexture", "sprite", "translation", "localization", "zhtw", "font", "icon", "music", "sound", "audio", "voice", "outfit", "costume" };
        static readonly string[] LookWordsCjk = { "立繪", "立绘", "外觀", "外观", "主題", "主题", "皮膚", "皮肤", "頭像", "头像", "翻譯", "翻译", "中文", "漢化", "汉化", "字體", "字体", "音樂", "音乐", "音效", "服裝", "服装" };
        /// <summary>Whole words only ("skincare" is not "skin"); camelCase and separators split words.</summary>
        internal static bool LooksByName(string id, string name) {
            string raw = (id ?? "") + " " + (name ?? "");
            foreach (var w in LookWordsCjk) if (raw.Contains(w)) return true;
            var words = new List<string>(); var cur = new System.Text.StringBuilder(); char prev = ' ';
            foreach (char c in raw) {
                bool letter = c < 128 && char.IsLetterOrDigit(c);
                if (!letter || (char.IsUpper(c) && char.IsLower(prev))) { if (cur.Length > 0) { words.Add(cur.ToString().ToLowerInvariant()); cur.Length = 0; } }
                if (letter) cur.Append(c);
                prev = c;
            }
            if (cur.Length > 0) words.Add(cur.ToString().ToLowerInvariant());
            foreach (var t in words) foreach (var w in LookWords) if (t == w || t == w + "s" || t == w + "es") return true;
            for (int k = 0; k + 1 < words.Count; k++) if (words[k] == "zh" && words[k + 1] == "tw") return true;
            return false;
        }

        internal static bool Compared(string id) { return !string.IsNullOrEmpty(id) && !Ignored.Contains(id) && !PresentationOnly.Contains(id); }

        internal static string[] Local() {
            var list = new List<string>();
            foreach (var mod in ModLoader.Mods) {
                if (mod == null || !mod.IsEnabled || mod.Manifest == null) continue;
                string id = mod.Manifest.ID ?? "";
                if (id.Length == 0 || Ignored.Contains(id) || PresentationOnly.Contains(id) || LooksOnly(mod)) continue;
                list.Add(id + "@" + (mod.Manifest.ModVersion ?? ""));
            }
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        /// <summary>Null when identical; otherwise a short readable list of differences.</summary>
        internal static string Compare(string[] host, string[] guest) {
            var h = new HashSet<string>(host ?? new string[0]);
            var g = new HashSet<string>(guest ?? new string[0]);
            var hostById = Index(h); var guestById = Index(g);
            var lines = new List<string>();
            foreach (var p in hostById) {
                string v;
                if (!guestById.TryGetValue(p.Key, out v)) lines.Add("你缺少：" + p.Key + " " + p.Value);
                else if (v != p.Value) lines.Add("版本不同：" + p.Key + "（房主 " + p.Value + "，你 " + v + "）");
            }
            foreach (var p in guestById) if (!hostById.ContainsKey(p.Key)) lines.Add("房主沒有開：" + p.Key + "（請你先關閉）");
            if (lines.Count == 0) return null;
            if (lines.Count > 12) { int more = lines.Count - 12; lines = lines.GetRange(0, 12); lines.Add("…還有 " + more + " 項"); }
            return "雙方的 mod 不一致，為了避免不同步，已拒絕連線：\n" + string.Join("\n", lines.ToArray());
        }

        static Dictionary<string, string> Index(IEnumerable<string> entries) {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries) {
                if (string.IsNullOrEmpty(e) || e.Length > 200) continue;
                int at = e.LastIndexOf('@');
                string id = at < 0 ? e : e.Substring(0, at), ver = at < 0 ? "" : e.Substring(at + 1);
                if (!d.ContainsKey(id)) d.Add(id, ver);
            }
            return d;
        }
    }

    [Serializable] internal sealed class ModList { public string[] mods; public ModEntry[] entries; public string name; }
    [Serializable] internal sealed class ModEntry { public string id, version, name, workshop; }
    /// <summary>What a friend must change to match the host: install/enable, update, or turn off.</summary>
    [Serializable] internal sealed class ModReport { public ModEntry[] missing, update, extra; }

    internal static class ModRequirements {
        const int MaxEach = 40;
        internal const string GameId = "probably_stolen_game";
        internal static ModEntry[] Local() {
            var list = new List<ModEntry>();
            foreach (var mod in ModLoader.Mods) {
                if (mod == null || !mod.IsEnabled || mod.Manifest == null) continue;
                string id = mod.Manifest.ID ?? "";
                if (!ModParity.Compared(id) || ModParity.LooksOnly(mod)) continue;
                list.Add(new ModEntry { id = id, version = mod.Manifest.ModVersion ?? "", name = mod.Manifest.Name ?? id, workshop = mod.Source == ModSource.Workshop && mod.WorkshopId != 0 ? mod.WorkshopId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "" });
            }
            // The game itself must be the same build too: item art, item rules and saved fields change between builds.
            list.Add(new ModEntry { id = GameId, version = Application.version ?? "", name = "~SMC270~", workshop = "" });
            list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return list.ToArray();
        }

        /// <summary>Host side. Null when the friend's enabled mods match the host's.</summary>
        internal static ModReport Diff(ModEntry[] host, ModEntry[] guest) {
            var h = Index(host); var g = Index(guest);
            var missing = new List<ModEntry>(); var update = new List<ModEntry>(); var extra = new List<ModEntry>();
            foreach (var p in h) { ModEntry mine; if (!g.TryGetValue(p.Key, out mine)) missing.Add(p.Value); else if (mine.version != p.Value.version) update.Add(new ModEntry { id = p.Value.id, name = p.Value.name, workshop = p.Value.workshop, version = p.Value.version + "|" + mine.version }); }
            foreach (var p in g) if (!h.ContainsKey(p.Key)) extra.Add(p.Value);
            if (missing.Count + update.Count + extra.Count == 0) return null;
            return new ModReport { missing = Cap(missing), update = Cap(update), extra = Cap(extra) };
        }

        /// <summary>Friend side: drop anything malformed in the host's report.</summary>
        internal static ModReport Clean(ModReport r) {
            if (r == null) return null;
            return new ModReport { missing = Sanitize(r.missing), update = Sanitize(r.update), extra = Sanitize(r.extra) };
        }

        internal static ulong Workshop(ModEntry e) { ulong id; return e != null && ulong.TryParse(e.workshop, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out id) ? id : 0; }

        static ModEntry[] Sanitize(ModEntry[] list) {
            var result = new List<ModEntry>();
            if (list != null) foreach (var e in list) { if (e == null || string.IsNullOrEmpty(e.id) || result.Count >= MaxEach) continue; result.Add(new ModEntry { id = Clip(e.id, 80), version = Clip(e.version, 60), name = Clip(string.IsNullOrEmpty(e.name) ? e.id : e.name, 60), workshop = Workshop(e) == 0 ? "" : e.workshop }); }
            return result.ToArray();
        }
        static string Clip(string s, int n) { s = (s ?? "").Replace("\n", " ").Replace("<", "‹").Replace(">", "›"); return s.Length > n ? s.Substring(0, n) : s; }
        static ModEntry[] Cap(List<ModEntry> list) { return (list.Count > MaxEach ? list.GetRange(0, MaxEach) : list).ToArray(); }
        static Dictionary<string, ModEntry> Index(ModEntry[] entries) {
            var d = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
            if (entries != null) foreach (var e in entries) if (e != null && !string.IsNullOrEmpty(e.id) && e.id.Length <= 200 && ModParity.Compared(e.id) && !ModParity.LooksByName(e.id, e.name) && !d.ContainsKey(e.id)) d.Add(e.id, e);
            return d;
        }

        internal static string Summary(ModReport r) {
            var parts = new List<string>();
            foreach (var e in r.missing) parts.Add("缺少 " + e.id);
            foreach (var e in r.update) parts.Add("版本不同 " + e.id);
            foreach (var e in r.extra) parts.Add("多開 " + e.id);
            return string.Join("、", parts.ToArray());
        }
    }

    // ------------------------------------------------------------------------------------------
    // Drift check. Every few seconds the host sends a fingerprint of everything it synced:
    // one hash per item (only fields that sync actually writes) plus the store and station state.
    // A friend's machine compares its own state against it. Any difference, whatever caused it
    // (another mod's leftover side effect, a missed update, a future mod nobody planned for),
    // is answered by asking the host for those exact items again. Items that appear on the friend's
    // side without coming from the host are removed. Nothing can drift for more than a few seconds.
    // ------------------------------------------------------------------------------------------
    [Serializable] internal sealed class Digest { public long version; public int slot; public long store, station; public int[] ids; public long[] hashes; }
    [Serializable] internal sealed class ResyncRequest { public long version; public int[] ids; public bool store, station; }

    internal static class Fingerprint {
        internal static long Hash(string s) {
            if (s == null) return 0;
            unchecked {
                ulong h = 14695981039346656037UL;
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 1099511628211UL; }
                return (long)h;
            }
        }

        /// <summary>Exactly the fields WorldReplica.Apply writes, so host and friend compute the same value.</summary>
        internal static long Item(WorldReplica world, int id, GameItem item) {
            var node = new SaveItemNode {
                identifier = item.identifier, unitCount = item.unitCount, uuid = id,
                itemState = item.state, itemShape = new GridShapeBuilder(item.shape), itemModifiedShape = new GridShapeBuilder(item.modifiedShape),
                name = item.name, shortDescription = item.shortDescription, flavorText = item.flavorText,
                unitBaseValue = item.unitBaseValue, unitValue = item.unitValue, lateUnitValue = item.lateUnitValue, backupUnitValue = item.backupUnitValue,
                itemTypes = new List<string>(item.itemTypes ?? new List<string>()),
                childItems = new List<long>(), childItemInventoryNode = new List<int>()
            };
            var sb = new StringBuilder();
            var parent = world.Address(item.parentInventory);
            sb.Append(parent == null ? "-" : parent.Key).Append('|').Append(JsonUtility.ToJson(node));
            if (item.itemFeatures != null)
                foreach (var f in item.itemFeatures)
                    sb.Append('|').Append(f == null ? "null" : Convert.ToBase64String(ES3.Serialize(f, new ES3Settings(false) { prettyPrint = false })));
            return Hash(sb.ToString());
        }
    }

    /// <summary>Runs on a friend's machine. Spreads the comparison over several frames to avoid hitches.</summary>
    internal sealed class DriftCheck {
        Digest pending;
        int cursor;
        readonly List<int> mismatched = new List<int>();
        readonly HashSet<int> seen = new HashSet<int>();
        readonly Dictionary<int, int> repeat = new Dictionary<int, int>();
        readonly HashSet<int> unstable = new HashSet<int>();
        readonly HashSet<GameItem> strayOnce = new HashSet<GameItem>();
        bool storeAsked, stationAsked, storeUnstable, stationUnstable;
        internal int Corrections, Removed;
        internal string LastNote = "";

        internal void Offer(Digest digest) { if (digest != null && digest.ids != null && digest.hashes != null && digest.ids.Length == digest.hashes.Length) { pending = digest; cursor = 0; mismatched.Clear(); seen.Clear(); } }

        /// <summary>Returns a resync request when the comparison finished and found differences.</summary>
        internal ResyncRequest Step(WorldReplica world, double budgetMs) {
            if (pending == null) return null;
            if (world.Version < pending.version) return null;               // the matching update is still being applied
            // Newer updates may arrive while we compare; anything they touched is skipped, not misjudged.
            var timer = System.Diagnostics.Stopwatch.StartNew();
            if (cursor == 0) world.Index();
            while (cursor < pending.ids.Length) {
                int id = pending.ids[cursor]; long expect = pending.hashes[cursor]; cursor++;
                seen.Add(id);
                if (unstable.Contains(id) || world.ChangedSince(id, pending.version)) continue;
                GameItem item;
                if (!world.Items.TryGetValue(id, out item) || item == null) { mismatched.Add(id); continue; }
                long mine;
                try { mine = Fingerprint.Item(world, id, item); } catch { continue; }
                if (mine != expect) mismatched.Add(id); else repeat.Remove(id);
                if (timer.Elapsed.TotalMilliseconds > budgetMs) return null;
            }
            var digest = pending; pending = null;

            // Items the host does not have: tracked ids the host dropped, or untracked items that were created locally.
            var extras = new List<GameItem>();
            foreach (var p in world.Items) if (!seen.Contains(p.Key) && p.Value != null && !world.ChangedSince(p.Key, digest.version)) extras.Add(p.Value);
            var strays = world.Untracked();
            var keep = new HashSet<GameItem>();
            foreach (var s in strays) { if (strayOnce.Contains(s)) extras.Add(s); else keep.Add(s); }
            strayOnce.Clear(); foreach (var s in keep) strayOnce.Add(s);
            if (extras.Count > 0) { Removed += world.RemoveLocal(extras); LastNote = "移除了 " + extras.Count + " 個不是房主產生的物品"; }

            var request = new ResyncRequest { version = digest.version };
            var ids = new List<int>();
            foreach (int id in mismatched) {
                int n; repeat.TryGetValue(id, out n); repeat[id] = ++n;
                if (n >= 3) { unstable.Add(id); Debug.LogWarning("[ShopTogether] item " + id + " keeps differing after resync; ignoring it in drift checks"); continue; }
                if (ids.Count < 512) ids.Add(id);
            }
            request.ids = ids.ToArray();

            if (!storeUnstable && world.StoreAppliedAt <= digest.version) {
                long mine = world.StoreHash(digest.slot);
                if (mine != 0 && mine != digest.store) { if (storeAsked) { storeUnstable = true; Debug.LogWarning("[ShopTogether] store state keeps differing after resync; ignoring it in drift checks"); } else { request.store = true; storeAsked = true; } }
                else storeAsked = false;
            }
            if (!stationUnstable && world.StationAppliedAt <= digest.version) {
                long mine = world.StationHash();
                if (mine != 0 && mine != digest.station) { if (stationAsked) { stationUnstable = true; Debug.LogWarning("[ShopTogether] station state keeps differing after resync; ignoring it in drift checks"); } else { request.station = true; stationAsked = true; } }
                else stationAsked = false;
            }
            if (request.ids.Length == 0 && !request.store && !request.station) return null;
            Corrections++;
            LastNote = "發現 " + request.ids.Length + " 個物品" + (request.store ? "、店鋪資料" : "") + (request.station ? "、站點資料" : "") + "與房主不同，已向房主重新同步";
            Debug.Log("[ShopTogether] drift: " + LastNote);
            return request;
        }
    }
}
