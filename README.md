# 多人操作：可能很混亂 / Multiplayer Control: Probably Chaotic

A community co-op mod for **Probably Stolen**. Up to eight players, including the host, run their own game and share one host-owned shop with separate cursors and menus.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3837477869)

版本：**1.3.41**，適用遊戲 **0.46D**。所有玩家須更新至相同版本並重新啟動遊戲。店鋪存檔由房主持有；朋友送出操作請求，房主依當下條件執行，再同步結果與拒絕原因。

## 1.3.41 更新內容

- 修正朋友那邊的客人對話提早消失，畫面以為講完了，房主卻還在等對話，導致出售被擋；現在由房主同步對話的推進與結束。
- 修正收槍後收到較晚的舉槍同意訊息，又重新進入瞄準。
- 修正多人同時舉槍時，房主收槍可能提早恢復對話，以及重複舉槍覆蓋原本暫停的對話。
- 減少機台讀數、滑桿及庫存格尺寸的重複處理；沒有變動的格子尺寸不再整批重傳，降低同步負擔。

化學品、酸鹼、水質、液體比例及植物／動物基因，仍由房主的遊戲運算，再同步實際數值。這次沒有改動連線握手、逾時或降低同步頻率。深空串門（Probably Multiplayer）相容功能已移除；安裝包只含 DLL、manifest 與封面，原始碼在 GitHub。

Version **1.3.41**, for game **0.46D**. Fixes guest dialogue disappearing while the host still awaits completion, delayed aiming approvals reopening the view after holstering, and shared aiming interrupting dialogue bookkeeping. Reduces redundant machine readout comparisons, unchanged slider updates and unchanged grid-size data. Handshakes, timeouts and sync frequency are unchanged. Everyone must use the same build and restart. Chemistry, water and genetics remain host-calculated. Probably Multiplayer integration is removed; runtime packages contain only the DLL, manifest and preview.

## 本版核對範圍

本次修正原始碼已完成一輪獨立桌面的本機雙程序背景測試（`perf1340`）：經遊戲掃描器載入、朋友加入、對話等待與推進、交付未完成時拒絕出售並回覆原因、完成後出售成功及同步收益、容器搬移、格子尺寸變化與完整重同步。237 個同步物件下，朋友完成 19 次房主確認操作，未發生斷線或套用錯誤。發布時僅更新版號與說明並編譯正式包，沒有重跑整套遊戲測試。

同輪比較中，33 組機台讀數每樣本比對 10 輪，舊 JSON 比對中位耗時 7.505 ms，新欄位比對 0.206 ms；這只代表該階段，不是整個遊戲加速比例。49 個庫存格的尺寸資料，完整為 4,236 bytes，無變動增量為 2 bytes；也不是整包網路流量。

**卡頓尚未完全解決。** 本機操作回覆中位 52.54 ms，P95 為 545.49 ms，仍有約半秒的延遲；房主同步擷取中位 14.01 ms。尚未定位這段偶發延遲的完整原因。測試是同機兩程序、原版加本模組的 TCP/TLS 連線，不能代表跨帳號 Steam 外網、八人長時間遊玩或任意模組組合。本輪也沒有重跑所有 84 項操作；下方較早的測試結果屬歷史範圍。

One isolated local two-process run of the pre-release source passed joining, host-controlled dialogue, refusal during pending delivery, sale after delivery, container moves, changed grid dimensions and full resync, with no disconnects or apply errors. Publication changes only the version and documentation, followed by a release build. Performance measurements cover specific processing stages, not a full old/new FPS comparison. Operation response median was 52.54 ms and P95 was 545.49 ms: occasional latency remains unresolved. This does not validate cross-account Steam relay, Internet play, eight players, arbitrary mods or every operation.

## 使用方式

1. 所有人訂閱工作坊、更新至同一版本，在遊戲的模組選單啟用。建議啟用的其他模組也與房主一致。
2. 房主從多人入口建立房間，邀請 Steam 好友或分享連線碼。朋友從多人入口加入。
3. 每人可以各自開選單、拖放不同物品。同一物品先取得控制權的人先操作，放開後才換人。
4. 外出與回店由房主判定並帶全員同行；選背包和目的地的畫面各自操作。
5. 多人成就開關由每台電腦自行設定。關閉只影響自己的多人期間成就解鎖與進度。

外出與回店是全員同行，垃圾場的拾荒次數、地上物品、口袋與背包由大家共用。選中的實體背包及原有內容依原版規則帶出。正常回店時，只帶回背包和口袋裡的物品；留在地上的物品不會帶回。確認期間內容改變或有人拿取物品時，請重新操作。

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
./src/Tests/RunDual.ps1 -Suite perf1340
```

The runtime harness needs Windows, Steam, the game and the enabled Workshop mod. Close the game before running it. It runs a disposable game copy on a verified private Windows desktop with isolated test profiles, and closes only its own processes. It does not replace the subscribed Workshop files. Do not run two harness instances concurrently. The runner currently targets Workshop item 3837477869.

Each process must report `[SMC COMPLETE]`. The outing138 continuation covers native travel and return at the dumping ground, commissary and Dr. Jackson, changed-ground and occupied-item refusals, and commissary purchases/redemptions. Both processes completed this suite. The larger completion138 suite passed its earlier machine-confirmation, controlled-lamp, wholesale and dialogue cases but did not complete as a whole; outing138 continued the outing cases. The authority134 scenario covers the 21 overnight machine panels, a rejected grey context action, placement refusal, a machine button with no effect, and rejected recipe settings. A successful build alone is not a runtime pass. Local dual-process tests do not verify two distinct Steam accounts, Internet routers, every third-party mod or eight-player performance. Real Steam achievement writes are suppressed in the test harness.

`src/Tests/RunGroup.ps1 -Players 4 -SavePath <day70-save.es3>` (or `-Players 6`) runs a local group test from a disposable copy of a day-70 save. It reserves two counters for test items, checks independent menus, concurrent drags and first-holder ownership, and records frame and snapshot timings. Every game runs on a verified private desktop with its own disposable settings, and the runner checks that the original save and Workshop files remain unchanged. Add `-PerformanceOnly` to measure idle and concurrent rename-request latency without the functional drag sequence. All processes share one computer, so these timings do not measure separate computers or Internet latency.

## License

Mod source is available under the [MIT License](LICENSE). The license does not cover the game, its assets, Steam or other mods. No game binaries, saves, account credentials or generated cover art are included in this repository.
