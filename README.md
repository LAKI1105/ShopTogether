# 多人操作：可能很混亂 / Multiplayer Control: Probably Chaotic

A community co-op mod for **Probably Stolen**. Up to eight players, including the host, run their own game and share one host-owned shop with separate cursors and menus.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3837477869)

版本：**1.3.42**，適用遊戲 **0.46D**。所有玩家須更新至相同版本並重新啟動遊戲。店鋪存檔由房主持有；朋友送出操作請求，房主依當下條件執行，再同步結果與拒絕原因。

## 1.3.42 更新內容

- 修正一個會反覆卡住約半秒的原因：原本每秒都重新搜尋沒有安裝的外部模組，讓遊戲和操作請求一起等待。
- 現在只有載入的程式庫有變化時才重新檢查，避免一直做相同的搜尋；後載入的模組仍會被檢查。
- 保留既有相容功能與主端操作判定；沒有改動連線握手、封包順序、逾時或同步頻率。

1.3.41 的對話、舉槍與機台同步修正保留。化學品、水質、液體比例與基因仍由房主遊戲運算並同步。深空串門（Probably Multiplayer）相容功能已移除；安裝包只有 DLL、manifest 與封面，原始碼在 GitHub。

Version **1.3.42**, for game **0.46D**. Fixes recurring stalls caused by searching every second for optional mods that were not installed. Discovery now repeats when the loaded assembly count changes, retaining detection of later-loaded mods and existing authority patches. Handshakes, packet ordering, timeouts and sync frequency are unchanged. The dialogue, aiming and machine sync fixes from 1.3.41 remain included. Everyone must update to the same build and restart. Probably Multiplayer integration is removed; runtime packages contain only the DLL, manifest and preview.

## 本版核對範圍

發布前先做一輪分段計時定位，再對修正原始碼做一輪獨立桌面的本機雙程序驗證（`perf1340`）。8 個缺席模組型別的舊搜尋工作實測耗時 515～522 ms；定位輪最慢操作約 612 ms，其中客端等待送出就占 525 ms。這個停頓來源已修正，不再將其列為未定位。

| 同情境本機取樣（237 個同步物件、12 秒） | 修正前 | 修正後 |
|---|---:|---:|
| 操作回覆中位數 | 114.28 ms | 35.32 ms |
| 操作回覆 P95 | 612.11 ms | 41.02 ms |
| 完成操作數 | 15 | 24 |

修正後取樣沒有再出現每秒約半秒的停頓。加入、對話等待與推進、交付期間拒絕出售並回覆原因、交付後成交與收益同步、容器搬移、格子尺寸變化及完整重同步均完成，沒有斷線或套用錯誤。發布時只更新版號與說明、編譯正式包，未再次重跑遊戲測試。

測試是同機兩程序、原版加本模組的 TCP/TLS 連線；不是跨帳號 Steam 外網、八人長時間或所有第三方模組組合的驗證，也沒有重新測完全部 84 項操作。首次搜尋或新模組載入後仍需檢查；修正的是沒有變化時反覆搜尋造成的停頓，不保證任何環境都零延遲。下方較早的測試結果屬歷史範圍。

A diagnostic run identified a 515–522 ms optional-mod search recurring on the main thread. One local two-process verification run after the fix passed joining, dialogue, sale rejection during delivery, successful sale and synchronized proceeds after delivery, container moves, grid resizing and full resync, with no disconnects or apply errors. In 12-second samples with 237 items, median response fell from 114.28 to 35.32 ms and P95 from 612.11 to 41.02 ms. These are local measurements, not guarantees for Internet play, eight players or arbitrary mods. Initial discovery and newly loaded assemblies still require a scan. Release preparation changes only version/documentation and compiles the package.

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
