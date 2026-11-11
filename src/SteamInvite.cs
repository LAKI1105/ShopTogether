using System;
using Steamworks;

namespace ShopTogether {
    /// <summary>
    /// Steam friends can join from the Steam friends list ("Join Game"), and the host can right-click a friend and
    /// choose "Invite to Game" in Steam itself. The mod only publishes a join string (the host's Steam account,
    /// no password); the host lets in only accounts that are on its own Steam friends list, and the usual mod
    /// check still runs before anyone sees the shop. A friend still needs this mod installed: without it the
    /// game has nothing that understands the invitation.
    /// </summary>
    internal static class SteamInvite {
        internal const string FriendToken = "@steam-friend";
        const string Marker = "+shoptogether";
        static Callback<GameRichPresenceJoinRequested_t> joinCallback;
        static bool launchChecked;
        internal static string Pending;                 // a join string waiting for the main menu
        internal static bool Advertising;

        internal static void Hook() {
            if (joinCallback == null && SteamRelay.Available) joinCallback = Callback<GameRichPresenceJoinRequested_t>.Create(r => Pending = r.m_rgchConnect);
            if (!launchChecked) {
                launchChecked = true;
                try {
                    var args = Environment.GetCommandLineArgs();
                    for (int i = 0; i + 1 < args.Length; i++) if (args[i] == Marker) { Pending = Marker + " " + args[i + 1]; break; }
                } catch { }
            }
        }

        /// <summary>Only "+shoptogether &lt;Steam account of a person&gt;" is understood; anything else is ignored.</summary>
        internal static bool TryParse(string connect, out ulong host) {
            host = 0;
            if (string.IsNullOrEmpty(connect) || connect.Length > 64) return false;
            var parts = connect.Trim().Split(' ');
            return parts.Length == 2 && parts[0] == Marker && ulong.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out host) && SteamRelay.ValidPerson(host);
        }

        /// <summary>
        /// What Steam friends see: a status line ("一同經營商店中"), players of one shop grouped together in the
        /// friends list, and on the host (when allowed) the join string used by "Join Game" / "Invite to Game".
        /// </summary>
        internal static void Presence(bool host, bool joinable, string group, int players, string hostName) {
            if (!SteamRelay.Available) { Advertising = false; return; }
            try {
                string status = host ? "~SMC022~" + players + "~SMC005~" + (joinable ? "~SMC013~" : "")
                                     : "~SMC157~" + hostName + "~SMC004~" + players + "~SMC005~";
                SteamFriends.SetRichPresence("status", status);
                SteamFriends.SetRichPresence("connect", host && joinable ? Marker + " " + SteamRelay.LocalId : "");
                SteamFriends.SetRichPresence("steam_player_group", group ?? "");
                SteamFriends.SetRichPresence("steam_player_group_size", players > 0 ? players.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
                Advertising = host && joinable; presence = true;
            } catch { Advertising = false; }
        }
        static bool presence;
        internal static string CleanGroup(string g) { if (g == null) return ""; var b = new System.Text.StringBuilder(); foreach (char c in g) { if (c < 128 && char.IsLetterOrDigit(c)) b.Append(c); if (b.Length >= 32) break; } return b.ToString(); }
        internal static void ClearPresence() {
            Advertising = false;
            if (!presence) return; presence = false;
            try { if (SteamRelay.Available) SteamFriends.ClearRichPresence(); } catch { }
        }

        /// <summary>Host: Steam friends the host has invited for this room.</summary>
        internal static readonly System.Collections.Generic.HashSet<ulong> Invited = new System.Collections.Generic.HashSet<ulong>();

        internal sealed class Friend { internal ulong Id; internal string Name; internal bool InGame; }

        /// <summary>Online Steam friends, those playing this game first.</summary>
        internal static System.Collections.Generic.List<Friend> OnlineFriends() {
            var list = new System.Collections.Generic.List<Friend>();
            if (!SteamRelay.Available) return list;
            try {
                var app = SteamUtils.GetAppID();
                int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < n && list.Count < 60; i++) {
                    var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    if (SteamFriends.GetFriendPersonaState(id) == EPersonaState.k_EPersonaStateOffline && !Invited.Contains(id.m_SteamID)) continue;
                    FriendGameInfo_t game; bool playing = SteamFriends.GetFriendGamePlayed(id, out game) && game.m_gameID.AppID() == app;
                    list.Add(new Friend { Id = id.m_SteamID, Name = PlayerNames.Clean(SteamFriends.GetFriendPersonaName(id)), InGame = playing });
                }
            } catch { }
            list.Sort((a, b) => a.InGame != b.InGame ? (a.InGame ? -1 : 1) : string.CompareOrdinal(a.Name, b.Name));
            return list;
        }

        internal static string InvitedNames() {
            var names = new System.Collections.Generic.List<string>();
            try { foreach (var id in Invited) names.Add(PlayerNames.Clean(SteamFriends.GetFriendPersonaName(new CSteamID(id)))); } catch { }
            return string.Join("、", names.ToArray());
        }

        internal static bool IsFriend(ulong id) {
            try { return SteamRelay.Available && SteamRelay.ValidPerson(id) && SteamFriends.HasFriend(new CSteamID(id), EFriendFlags.k_EFriendFlagImmediate); } catch { return false; }
        }
    }
}
