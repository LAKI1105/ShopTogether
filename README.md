# 多人操作：可能很混亂 / Multiplayer Control: Probably Chaotic

> 此分支為 **1.3.35-dev 原始碼修正草稿**，工作坊仍是 1.3.34。依要求只做程式碼審查，未編譯、未執行測試，也未發布新版 DLL。修正對話交付保護的旁路、自動屠宰誤用房主占用權，以及房主交易平板直接成交漏查占用。
>
> This branch contains source-only fixes for 1.3.35-dev. Workshop remains at 1.3.34. No build or tests were run for these changes; no new DLL has been published.


A community co-op mod for **Probably Stolen**. Run the game on each computer and operate the same host-owned shop with separate cursors and interfaces. Up to eight players, including the host.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3837477869)

目前工作坊版本：**1.3.34**。所有玩家請更新到同一版本後重新開啟遊戲。

## 1.3.34 有哪些改變？

- 朋友的共用操作交給房主依當下狀態判斷，成功後同步結果；不能執行時，操作者會收到原因，減少被朋友端舊資料誤擋。
- 修正機器換日後的狀態文字與開啟中視窗更新；機台沒有產生結果時會回覆說明。
- 清空機器、交貨或付款前重新檢查內容及占用，避免確認期間物品改變，或被別人拿走。容器裡的物品也會檢查。
- 交易、公司訂貨與兌換使用房主的價格、庫存及資格，並加強避免重複扣款、領獎與消耗的處理。對話交付完成前，待交付物不能被拖走。
- 垃圾場、兌換處與 Dr. Jackson 的外出及回店由全員同行，共用房主的拾荒次數、地上物品、口袋與背包。
- 補齊倉庫分頁、整理、改名、擴容及臨時溢出頁同步；卡帶機播放、暫停、切歌與停止同步也已補強。
- 化學品、酸鹼、水質、液體比例及植物／動物基因維持房主遊戲運算，再同步數值與結果；歸零或停用的狀態也會更新。

Current Workshop release: **1.3.34**. All players should update to the same version and restart the game.

Shared actions are checked against the host's current state, with synchronized results or a reason for refusal. This release improves overnight machine displays, stale confirmations, item conflicts, trades and redemptions, delayed dialogue deliveries, shared outings, storage pages and cassette playback. Chemistry, water quality, liquid proportions and plant/animal genetics remain calculated by the host's game; guests receive the values and results, including zeroed or disabled states.


## 使用方式

1. 所有人訂閱工作坊、更新至同一版本，在遊戲的模組選單啟用。建議啟用的其他模組也與房主一致。
2. 房主從多人入口建立房間，邀請 Steam 好友或分享連線碼。朋友從多人入口加入。
3. 每人可以各自開選單、拖放不同物品。同一物品先取得控制權的人先操作，放開後才換人。
4. 外出與回店由房主判定並帶全員同行；選背包和目的地的畫面各自操作。
5. 多人成就開關由每台電腦自行設定。關閉只影響自己的多人期間成就解鎖與進度。

如果大家都啟用相同版本的「深空串門」2.6.1，朋友也能在自己的畫面與房主店裡的真人訪客交談、選擇交易物品、報價和購買。訪客連線與交易仍由房主處理；交易結算時，庫存會暫時鎖定以保護物品。真人訪客的外部連線尚未經過實機測試。

串門相容功能需要遊戲的「反射」權限，讓本模組讀取串門的訪客與交易狀態；更新後請在各自的遊戲內檢視並核准新增權限。連線請求限於模組列出的操作，沒有提供遠端檔案或執行指令的功能。

外出與回店是全員同行，垃圾場的拾荒次數、地上物品、口袋與背包由大家共用。選中的實體背包及原有內容依原版規則帶出。正常回店時，只帶回背包和口袋裡的物品；留在地上的物品不會帶回。確認期間內容改變或有人拿取物品時，請重新操作。

## How it works

The host runs gameplay simulation and owns the save. Guests render replicated game state and send validated action requests. This is neither desktop streaming nor remote keyboard/mouse injection. Item leases arbitrate competing actions; separate local UI state keeps one player's menus and aiming view from replacing another player's screen.

Compatibility with arbitrary third-party mods is not guaranteed. A mod with a new interface or custom state may need an adapter. Matching enabled mods and versions is recommended.

When everyone has Probably Multiplayer 2.6.1 enabled, guests can use their own screen to chat, select items, quote and buy from the host's human visitor. The host owns the visitor connection and the transaction; inventory is briefly locked during the visitor mod's transaction settlement. Other versions of that mod are not supported by this adapter. A live connection with an external human visitor has not been tested in-game.

During multiplayer, travel and return are shared: the host validates the requested destination and backpack, and all players follow the resulting location. Custom third-party interfaces still require compatible adapters. Scene lamp clicks use host state; the installed Bartender worklamp description opens for the requesting player. The host can hear a guest's phone call even while its panel is hidden.

Travel and return are shared, including search attempts, ground items, pockets and the outing backpack. The selected real backpack and its existing contents travel under the base game's rules. On a normal return, items in the backpack and pockets come home; items left on the ground do not. If items change or someone handles them during confirmation, request the action again.

Direct connections use pinned TLS. Steam transport uses Steam networking identities. Incoming messages have direction, size and rate checks; actions are limited to registered game operations and valid accessible items. This does not expose a shell or general file access. The mod and game still run with your account's local permissions; these checks are not a security audit or a guarantee against every bug.

The optional visitor adapter declares the game's `reflection-external` capability to access the other mod's internal visit and trade state. Each player must review and approve newly requested permissions in the game. The adapter accepts only bounded chat and trade requests and does not provide a remote file or command interface.

## Build

Game assemblies are **not** included. Install your own copy of Probably Stolen. On Windows, use PowerShell 7.4 or newer:

```powershell
$env:PROBABLY_STOLEN_PATH = 'D:\SteamLibrary\steamapps\common\Probably Stolen'
./src/Build.ps1 -OutputPath ./ShopTogether.dll
```

Package `ShopTogether.dll` with `src/manifest.xml`. For a .NET SDK build, set the same environment variable and run `bash build.sh`. Proprietary game assemblies are referenced from the installed game's `Managed` directory and must not be redistributed.

## Tests

```powershell
./src/Tests/Run.ps1 -OutputPath ./ShopTogether.dll
./src/Tests/RunDual.ps1 -Suite outing138
```

The runtime harness needs Windows, Steam, the game and the enabled Workshop mod. Close the game before running it. It runs a disposable game copy on a verified private Windows desktop with isolated test profiles, and closes only its own processes. It does not replace the subscribed Workshop files. Do not run two harness instances concurrently. The runner currently targets Workshop item 3837477869.

Each process must report `[SMC COMPLETE]`. The outing138 continuation covers native travel and return at the dumping ground, commissary and Dr. Jackson, changed-ground and occupied-item refusals, and commissary purchases/redemptions. Both processes completed this suite. The larger completion138 suite passed its earlier machine-confirmation, controlled-lamp, wholesale and dialogue cases but did not complete as a whole; outing138 continued the outing cases. The authority134 scenario covers the 21 overnight machine panels, a rejected grey context action, placement refusal, a machine button with no effect, and rejected recipe settings. A successful build alone is not a runtime pass. Local dual-process tests do not verify two distinct Steam accounts, Internet routers, every third-party mod or eight-player performance. Real Steam achievement writes are suppressed in the test harness.

`src/Tests/RunGroup.ps1 -Players 4 -SavePath <day70-save.es3>` (or `-Players 6`) runs a local group test from a disposable copy of a day-70 save. It reserves two counters for test items, checks independent menus, concurrent drags and first-holder ownership, and records frame and snapshot timings. Every game runs on a verified private desktop with its own disposable settings, and the runner checks that the original save and Workshop files remain unchanged. Add `-PerformanceOnly` to measure idle and concurrent rename-request latency without the functional drag sequence. All processes share one computer, so these timings do not measure separate computers or Internet latency.

## License

Mod source is available under the [MIT License](LICENSE). The license does not cover the game, its assets, Steam or other mods. No game binaries, saves, account credentials or generated cover art are included in this repository.
