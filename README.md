# 多人操作：可能很混亂 / Multiplayer Control: Probably Chaotic

A community co-op mod for **Probably Stolen**. Up to eight players, including the host, run their own game and share one host-owned shop with separate cursors and menus.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3837477869)

版本：**1.3.45**，適用遊戲 **0.46D**。所有玩家須更新至相同版本並重新啟動遊戲。店鋪存檔由房主持有；朋友送出操作請求，房主依當下條件執行，再同步結果與拒絕原因。

顧客進出店的移動與對話內容跟隨房主同步。外出與回店採全員同行；雙手槽位、背包與地面物品共用，各自的游標和選單仍可獨立操作。化學品、水質、液體比例與基因由房主遊戲運算並同步數值。安裝包只有 DLL、manifest 與封面；原始碼在本儲存庫。

本次修正摘要請見 [1.3.45 更新紀錄](CHANGELOG.md)。

Version **1.3.45**, for game **0.46D**. Everyone must use the same build and restart. The host owns gameplay and the save; guests send requests and receive authoritative results or refusal reasons. Customer movement and dialogue follow the host. Travel, hand slots, the backpack and ground items are shared, while cursors and menus remain individual. Runtime packages contain only the DLL, manifest and preview. See the [changelog](CHANGELOG.md) for release notes.

## 本版核對範圍

1.3.45 的拾荒與離開場景修正，已在獨立 Windows 桌面完成 `outing1344` 本機雙程序驗證：房主及朋友拾荒、過時客端資料仍由房主拒絕並回覆受傷原因、繃帶治療、地面物品放入共用背包、完整重新同步、原版選單離開及重新加入、強制中斷直連後釋放物品控制權、重新加入恢復物品，以及朋友發起全員回店。物品編號、種類及數量核對一致。

房主拖不上檯面且顯示「其他玩家使用中」的玩家回報，已確認並修正拒絕提示誤報，尚未定位造成持續無法搬移的原因。新增的 `hostdrag1345` 測試在輸入工具初始化時中止，尚未執行拖放；測試工具已調整但未重跑，不列為通過。

這是本機雙程序直連驗證，未涵蓋不同 Steam 帳號的外網、八人長時間或所有第三方模組組合，也沒有重新測完全部 84 項。玩家回報的 ModLoader 被銷毀及多模組環境的全部空物件錯誤，尚未確認同一根因，不能視為全部修復。

Local two-process outing checks covered host and guest scavenging, host refusal with stale guest health, bandage use, shared-backpack transfers, full resync, menu exit/rejoin, forced direct-connection loss, lease cleanup, rejoining with preserved items and returning home. These results do not establish cross-account Steam Internet connectivity, eight-player stability or universal mod compatibility. The reported ModLoader destruction and all null-reference errors in multi-mod sessions have not been traced to a single confirmed cause.

## 使用方式

1. 所有人訂閱工作坊、更新至同一版本，在遊戲的模組選單啟用。建議啟用的其他模組也與房主一致。
2. 房主從多人入口建立房間，邀請 Steam 好友或分享連線碼。朋友從多人入口加入。
3. 每人可以各自開選單、拖放不同物品。同一物品先取得控制權的人先操作，放開後才換人。
4. 外出與回店由房主判定並帶全員同行；選背包和目的地的畫面各自操作。
5. 多人成就開關由每台電腦自行設定。關閉只影響自己的多人期間成就解鎖與進度。

外出與回店是全員同行，垃圾場的拾荒次數、地上物品、口袋與背包由大家共用。選中的實體背包及原有內容依原版規則帶出。正常回店時，只帶回背包和口袋裡的物品；留在地上的物品不會帶回。確認期間內容改變或有人拿取物品時，請重新操作。

## 連不上時，先看這裡

1. **兩邊都更新、重開遊戲。** 確認遊戲和多人模組是同一版；畫面若說缺少模組或版本不同，先照提示處理。
2. **不在同一個區網，就開一般房間。** 雙方登入 Steam，房主開房後進入店鋪，朋友用 Steam 好友加入，或貼上這次房間的完整連線碼。房主重開房後，舊連線碼不要再用。
3. **防火牆要允許 Steam 和遊戲連線。** 到「Windows 安全性 → 防火牆與網路保護 → 允許應用程式通過防火牆」，確認 Steam 和 Probably Stolen 已獲允許。若用其他防毒或防火牆，也檢查有沒有擋住它們；不用把整個防火牆關掉。
4. **一般 Steam 連線通常不用自己開路由器連接埠。** 手動 IP 的外網直連才需要另外設定。不熟悉設定就先用 Steam 好友加入；公司、學校等受限制的網路，請管理員確認能不能使用 Steam 連線。
5. **還是連不上，請附錯誤文字。** 告訴我們兩邊版本、怎麼加入，以及卡在哪一步。不要公開完整連線碼，裡面含房間密碼。連不上不一定就是防火牆，也可能需要依錯誤訊息繼續查。

需要詳細設定時，請看 [Windows 防火牆說明](https://support.microsoft.com/en-au/windows/security/firewall/risks-of-allowing-apps-through-windows-firewall) 與 [Steam 官方連線說明](https://help.steampowered.com/en/faqs/view/2EA8-4D75-DA21-31EB)。

### Can't connect? Start here

1. Update and restart both games. Match game/mod versions and follow any missing-mod or version warning.
2. For players on different networks, use a normal room, not a LAN room. Both players sign into Steam; the host opens a room and enters the shop. Join through Steam friends or the current room code. Get a new code after the host recreates the room.
3. Allow Steam and Probably Stolen through Windows Firewall. Open Windows Security → Firewall & network protection → Allow an app through firewall. Check any other antivirus/firewall too; keep the firewall on.
4. Steam connections usually need no manual router port forwarding. Internet connections by manual IP need separate setup. On school or work networks, ask the administrator whether Steam connections are allowed.
5. Still stuck? Share both versions, how you joined, where it stopped and the exact error. Keep room codes private: they contain the room password. A failed connection does not by itself identify the cause.

Detailed help: [Windows Firewall](https://support.microsoft.com/en-au/windows/security/firewall/risks-of-allowing-apps-through-windows-firewall) / [Steam connection requirements](https://help.steampowered.com/en/faqs/view/2EA8-4D75-DA21-31EB).

## How it works

The host runs gameplay simulation and owns the save. Guests render replicated game state and send validated action requests. This is neither desktop streaming nor remote keyboard/mouse injection. Item leases arbitrate competing actions; separate local UI state keeps one player's menus and aiming view from replacing another player's screen.

Compatibility with arbitrary third-party mods is not guaranteed. A mod with a new interface or custom state may need an adapter. Matching enabled mods and versions is recommended.

During multiplayer, travel and return are shared: the host validates the requested destination and backpack, and all players follow the resulting location. Custom third-party interfaces still require compatible adapters. Scene lamp clicks use host state; the installed Bartender worklamp description opens for the requesting player. The host can hear a guest's phone call even while its panel is hidden.

Travel and return are shared, including search attempts, ground items, pockets and the outing backpack. The selected real backpack and its existing contents travel under the base game's rules. On a normal return, items in the backpack and pockets come home; items left on the ground do not. If items change or someone handles them during confirmation, request the action again.

Direct connections use pinned TLS. Steam transport uses Steam networking identities. Incoming messages have direction, size and rate checks; actions are limited to registered game operations and valid accessible items. This does not expose a shell or general file access. The mod and game still run with your account's local permissions; these checks are not a security audit or a guarantee against every bug.

## Build

Game assemblies are **not** included. Install your own copy of Probably Stolen. On Windows, use PowerShell 7.4 or newer:

```powershell
$env:PROBABLY_STOLEN_PATH = 'D:\SteamLibrary\steamapps\common\Probably Stolen'
./src/Build.ps1 -OutputPath ./ShopTogether.dll
```

Use `src/Package.ps1 -PreviewPath <preview.jpg>` to produce `out/Workshop` with only `ShopTogether.dll`, `manifest.xml` and `preview.jpg`. Never put source ZIPs, scripts, or build output folders in the runtime package. The game scans every installed file. Network, Steam and reflection capabilities remain declared because shared-shop transport and the remaining game/mod adapters use them. For a .NET SDK build, set the same environment variable and run `bash build.sh`. Proprietary game assemblies are referenced from the installed game's `Managed` directory and must not be redistributed.

## Tests

```powershell
./src/Tests/Run.ps1 -OutputPath ./ShopTogether.dll
./src/Tests/RunDual.ps1 -Suite outing138
./src/Tests/RunDual.ps1 -Suite reported1344
```

The runtime harness needs Windows, Steam, the game and the enabled Workshop mod. Close the game before running it. It runs a disposable game copy on a verified private Windows desktop with isolated test profiles, and closes only its own processes. It does not replace the subscribed Workshop files. Do not run two harness instances concurrently. The runner currently targets Workshop item 3837477869.

Each process must report `[SMC COMPLETE]`. The outing138 continuation covers native travel and return at the dumping ground, commissary and Dr. Jackson, changed-ground and occupied-item refusals, and commissary purchases/redemptions. Both processes completed this suite. The larger completion138 suite passed its earlier machine-confirmation, controlled-lamp, wholesale and dialogue cases but did not complete as a whole; outing138 continued the outing cases. The authority134 scenario covers the 21 overnight machine panels, a rejected grey context action, placement refusal, a machine button with no effect, and rejected recipe settings. A successful build alone is not a runtime pass. Local dual-process tests do not verify two distinct Steam accounts, Internet routers, every third-party mod or eight-player performance. Real Steam achievement writes are suppressed in the test harness.

`src/Tests/RunGroup.ps1 -Players 4 -SavePath <day70-save.es3>` (or `-Players 6`) runs a local group test from a disposable copy of a day-70 save. It reserves two counters for test items, checks independent menus, concurrent drags and first-holder ownership, and records frame and snapshot timings. Every game runs on a verified private desktop with its own disposable settings, and the runner checks that the original save and Workshop files remain unchanged. Add `-PerformanceOnly` to measure idle and concurrent rename-request latency without the functional drag sequence. All processes share one computer, so these timings do not measure separate computers or Internet latency.

## License

Mod source is available under the [MIT License](LICENSE). The license does not cover the game, its assets, Steam or other mods. No game binaries, saves, account credentials or generated cover art are included in this repository.
