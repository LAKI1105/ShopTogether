# 多人操作：可能很混亂 / Multiplayer Control: Probably Chaotic

A community co-op mod for **Probably Stolen**. Up to eight players, including the host, run their own game and share one host-owned shop with separate cursors and menus.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3837477869)

版本：**1.3.43**，適用遊戲 **0.46D**。所有玩家須更新至相同版本並重新啟動遊戲。店鋪存檔由房主持有；朋友送出操作請求，房主依當下條件執行，再同步結果與拒絕原因。

## 1.3.43 更新內容

- 減少同步顧客資料時的重複查詢，降低房主與朋友每次更新資料的負擔。
- 顧客的交易條件、金額與商品清單仍照常同步；沒有為了加速而省略資料或降低同步頻率。
- 原版顧客使用較快的欄位存取方式；模組自訂的衍生顧客保留原本處理方式。
- 保留 1.3.42 修正的反覆半秒停頓，以及 1.3.41 的對話、舉槍與機台同步修正；連線握手、封包順序與逾時未改。

1.3.41 的對話、舉槍與機台同步修正保留。化學品、水質、液體比例與基因仍由房主遊戲運算並同步。深空串門（Probably Multiplayer）相容功能已移除；安裝包只有 DLL、manifest 與封面，原始碼在 GitHub。

Version **1.3.43**, for game **0.46D**. Reduces repeated lookups when capturing and applying customer data. All customer fields and authoritative updates remain intact, with the existing lookup behavior retained for derived customer classes. Handshakes, packet ordering, timeouts and sync frequency are unchanged. Includes the recurring-stall fix from 1.3.42 and the dialogue, aiming and machine sync fixes from 1.3.41. Everyone must update to the same build and restart. Probably Multiplayer integration is removed; runtime packages contain only the DLL, manifest and preview.

## 本版核對範圍

發布前對修正原始碼做一輪獨立桌面的本機雙程序驗證（`perf1340`）。同一輪交錯比較新舊顧客欄位存取流程、排除暖機後，82 個欄位的擷取＋套用中位耗時從 **14.167 ms 降到 0.239 ms**。逐欄比對傳送及套用值一致，也確認相同主端資料仍能修復客端偏差、商品清單變更可替換舊值，以及衍生顧客的同名欄位行為保持一致。這是該段工作量的比較，不代表整個遊戲加速相同比例。

| 本輪本機取樣（237 個同步物件、12 秒） | 結果 |
|---|---:|
| 操作回覆中位數 | 25.85 ms |
| 操作回覆 P95 | 32.26 ms |
| 完成操作數 | 24 |
| 房主同步擷取中位數／P95 | 9.45／11.66 ms |

取樣未記到達 50 ms 的更新間隔或達 25 ms 的多人主更新工作耗時。加入、對話等待與推進、交付期間拒絕出售並回覆原因、交付後成交與收益同步、容器搬移、格子尺寸變化及完整重同步均完成，沒有斷線或套用錯誤。正式候選及測試模組通過原版掃描器。發布時只更新版號與說明、編譯正式包，未再次重跑遊戲測試。

測試是同機兩程序、原版加本模組的 TCP/TLS 連線；不是跨帳號 Steam 外網、八人長時間或所有第三方模組組合的驗證，也沒有重新測完全部 84 項操作，不保證任何環境都零延遲。下方較早的測試結果屬歷史範圍。

One local two-process verification run compared the old and new capture/apply paths for all 82 customer fields: median combined work fell from 14.167 to 0.239 ms, with matching values and derived-class behavior preserved. Joining, dialogue, sale refusal during delivery, successful sale and synchronized proceeds after delivery, container moves, grid resizing and full resync passed without disconnects or apply errors. A 12-second sample with 237 items completed 24 actions, with median response 25.85 ms and P95 32.26 ms. These are local measurements, not whole-game speedup figures or guarantees for Internet play, eight players or arbitrary mods. Release preparation changes only version/documentation and compiles the package.

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
