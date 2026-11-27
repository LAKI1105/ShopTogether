# 多人操作：可能很混亂 / Multiplayer Control: Probably Chaotic

A community co-op mod for **Probably Stolen**. Up to eight players, including the host, run their own game and share one host-owned shop with separate cursors and menus.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3837477869)

版本：**1.3.50**，適用遊戲 **1.0.0 系列**。所有玩家須更新至相同版本並重新啟動遊戲。店鋪存檔由房主持有；朋友送出操作請求，房主依當下條件執行，再同步結果與拒絕原因。

顧客進出店的移動與對話內容跟隨房主同步。外出與回店採全員同行；雙手槽位、背包與地面物品共用，各自的游標和選單仍可獨立操作。化學品、水質、液體比例與基因由房主遊戲運算並同步數值。安裝包只有 DLL、manifest 與封面；原始碼在本儲存庫。

本次修正摘要請見 [1.3.50 更新紀錄](CHANGELOG.md)。

Version **1.3.50**, for the **1.0.0 game series**. Everyone must use the same build and restart. The host owns gameplay and the save; guests send requests and receive authoritative results or refusal reasons. Customer movement and dialogue follow the host. Travel, hand slots, the backpack and ground items are shared, while cursors and menus remain individual. Runtime packages contain only the DLL, manifest and preview. See the [changelog](CHANGELOG.md) for release notes.

## 1.3.50 本次更新

修正處理大量可靠資料時的誤斷線判斷，接上朋友的智慧整理右鍵操作，並補上濾芯造成拒收的明確提示。整批復原會檢查所有受影響容器，避免搬走其他人拿著的物品。`release1349` 在獨立桌面的兩個遊戲程序完成；智慧整理使用官方 3.17.76，純水成交與金額同步亦通過。外網原始事故及智慧整理舊版 3.17.63 未重現，詳見更新紀錄。

This release corrects receive-progress timeout handling, forwards Smart Inventory Arrange requests to the host, and explains native water-filter refusals. The isolated dual-process check passed with Smart Inventory Arrange 3.17.76. It does not reproduce the players' original external-network incidents or test the reported older 3.17.63 version.

## 12 月 10 日相容性 / December 10 compatibility

1.3.48 的正式 DLL 改用官方 `ModReflect` 及公開 API，不宣告或使用 `reflection-external`。跨模組存取依官方規則列出可選相容模組：`CorpPresence`、`bartender_action`、`player.shop_storage_pages`、`modkit.multipage_store_fix`，1.3.49 另加入 `smart_inventory_arrange`。未安裝時遊戲可能顯示缺少前置的警告，多人本體仍能載入。對方模組本身也必須符合新規則；本模組不會略過或修改它們的安全檢查。

The production DLL uses official `ModReflect` and public APIs instead of deprecated external reflection. The optional integrations above are declared for authorized cross-mod access. They remain optional: missing integrations may produce dependency warnings, but do not prevent this mod from loading. Integrated mods must independently comply with the game's security rules.

## 1.3.48 核對範圍

在不切換畫面的獨立 Windows 桌面完成七組相關雙程序套件：`core`、`gun`、`clock`、`custom135`、`cargo135`、`equipment136`、`overflow136`，房主與朋友兩端皆完成。涵蓋獨立選單、重疊拖放與先到先得、房主運算與偏差修正、射擊旁觀者、溫度和配方、同時倒水與淨水反應，以及酒吧、企業訂單、貨倉、主倉分頁、溢出頁面與錄音機相容路徑。測試中修正了分頁改名的多載定位、貨倉弱參照表讀取，以及機台設定寫入後未刷新實際狀態的問題。

正式 DLL 通過遊戲掃描器，未使用廢止的外部反射權限；GNU `strings -a ShopTogether.dll | grep -c RuntimeProbe` 為 `0`。診斷程式只放在臨時測試副本，正式安裝包不包含診斷程式。使用者正式存檔、問題回報偏好與已訂閱模組檔案未被測試修改。

These seven targeted suites completed on both local game processes. This is not a run of all historical suites, every third-party mod version, or a two-account Internet session. Optional integrations were exercised with the installed versions; other mods must independently comply with the official API rules.

## 先前版本核對範圍

1.3.47 已在獨立 Windows 桌面完成一輪 `release1347` 本機雙程序驗證。接收輪詢在佇列滿時保留下一包資料，空間足夠後依序繼續；真正的連線失敗仍會被處理，房主端的容量限制也保留。Steam 收取介面使用受控封包來確認輪詢邏輯，並非兩個 Steam 帳號的外網實測。

兩個遊戲程序經本機 TLS 連線，完成加入、房主移動物品、拒絕失效操作權、群組放開、背包內容物占用、完整重新同步、強制直連斷線及重新加入；斷線與重加入後沒有殘留物品占用。先前的 `slots1346`、`lifecycle1346`、`closewater1346` 與 `naturalwater1346` 核對範圍列於下方測試說明。

**尚未確認：** 玩家「水純度 0」的原始觸發原因、所有外網斷線原因，以及原存檔或其他模組組合中的所有持續拖不動情況。已確認正常關閉遊戲會產生原版載入器銷毀訊息；不能因此將遊戲中發生的載入器銷毀也視為正常。本輪沒有重跑全部 84 項、八人長時間遊玩或所有第三方模組組合。

The local release check covered receive polling under capacity pressure and real two-process TLS join, item control, resync, forced disconnection and rejoining. The Steam polling check used controlled packets; it does not validate external Steam delivery. The player's original water-error trigger and all environment-specific disconnect/drag reports are not claimed resolved.

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
./src/Tests/RunDual.ps1 -Suite slots1346
./src/Tests/RunDual.ps1 -Suite lifecycle1346
./src/Tests/RunDual.ps1 -Suite closewater1346
./src/Tests/RunDual.ps1 -Suite naturalwater1346
./src/Tests/RunDual.ps1 -Suite release1347
```

The runtime harness needs Windows, Steam, the game and the enabled Workshop mod. Close the game before running it. It runs a disposable game copy on a verified private Windows desktop with isolated test profiles, and closes only its own processes. It does not replace the subscribed Workshop files. Do not run two harness instances concurrently. The runner currently targets Workshop item 3837477869.

`slots1346` covers occupied hand-slot swaps in incremental and full snapshots, removed-slot occupants, backpack replacement while preserving a moved child, and explicit resync afterward. Both local processes completed this focused check without new diagnostic reports. This covers the corrected replica placement order in the working source; it does not establish that every player-reported `SMC142` has the same cause or resolve the separate UI, water-purity, loader-destruction and disconnect reports.

`lifecycle1346` completed on both local processes: the native `_RecorderPanel` and `NegotiationTablet` retained their front/back ordering without sibling errors during parent-canvas toggles; departure closed an open backpack-choice window and released input before the watchdog ran. The same native ModLoader survived play and the guest's return to the menu. On actual application exit, the game emitted its loader-destruction error while the host connection flags were still active, confirming the shutdown-reporting race. The working source excludes that specific message from gameplay reports only after application shutdown begins; it does not suppress in-session loader destruction or change the game's safety mechanisms. Water-purity and unexplained remote-disconnect reports remain unlocated. These changes are included in 1.3.47.

`closewater1346` completed on both local processes. Controlled channels exercised complete refusals followed immediately by EOF, refusals behind a frame-budget backlog, truncated and invalid frames, empty closes, and the host's existing refusal to execute actions after detecting EOF. Steam callback reasons now remain attached to their own channel. This repairs lost refusal details, not the underlying cause of every disconnect. A deliberate call to the native water classifier on a replicated empty bottle verified failure context (caller, item and liquid amounts) without changing its result or contents; a subsequent host fill replicated normally. That deliberate invocation does not identify the player's original water-error trigger, and the check does not validate external Steam connectivity.

`naturalwater1346` completed on both local processes. A controlled 80 MB stream reproduced the old receive-queue overflow and verified that waiting for capacity preserves order and completes without marking the stream failed. The friend now checks queue capacity before removing the next reliable Steam packet; the 64 MB receive limit and host-side guards remain unchanged. This fixes a confirmed local disconnect path, but does not identify the cause of the player's earlier disconnect or validate external Steam delivery. The two game processes also completed native empty-container scanning, host pouring, guest test-strip use, full-container scanning, and full resynchronization with the scanner open. Liquid quantities remained authoritative, and none of these actions called the failing `GetWaterIndex` classifier or produced its zero-purity error. The player's original water-error trigger remains unlocated; no water values or classification rules were changed. These changes are included in 1.3.47.

Each process must report `[SMC COMPLETE]`. The outing138 continuation covers native travel and return at the dumping ground, commissary and Dr. Jackson, changed-ground and occupied-item refusals, and commissary purchases/redemptions. Both processes completed this suite. The larger completion138 suite passed its earlier machine-confirmation, controlled-lamp, wholesale and dialogue cases but did not complete as a whole; outing138 continued the outing cases. The authority134 scenario covers the 21 overnight machine panels, a rejected grey context action, placement refusal, a machine button with no effect, and rejected recipe settings. A successful build alone is not a runtime pass. Local dual-process tests do not verify two distinct Steam accounts, Internet routers, every third-party mod or eight-player performance. Real Steam achievement writes are suppressed in the test harness.

`src/Tests/RunGroup.ps1 -Players 4 -SavePath <day70-save.es3>` (or `-Players 6`) runs a local group test from a disposable copy of a day-70 save. It reserves two counters for test items, checks independent menus, concurrent drags and first-holder ownership, and records frame and snapshot timings. Every game runs on a verified private desktop with its own disposable settings, and the runner checks that the original save and Workshop files remain unchanged. Add `-PerformanceOnly` to measure idle and concurrent rename-request latency without the functional drag sequence. All processes share one computer, so these timings do not measure separate computers or Internet latency.

## License

Mod source is available under the [MIT License](LICENSE). The license does not cover the game, its assets, Steam or other mods. No game binaries, saves, account credentials or generated cover art are included in this repository.
