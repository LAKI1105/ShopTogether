# 多人操作：可能很混亂 / Multiplayer Control: Probably Chaotic

A community co-op mod for **Probably Stolen**. Up to eight players, including the host, run their own game and share one host-owned shop with separate cursors and menus.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3837477869)

版本：**1.3.44**，適用遊戲 **0.46D**。所有玩家須更新至相同版本並重新啟動遊戲。店鋪存檔由房主持有；朋友送出操作請求，房主依當下條件執行，再同步結果與拒絕原因。

顧客進出店的移動與對話內容跟隨房主同步。外出與回店採全員同行；雙手槽位、背包與地面物品共用，各自的游標和選單仍可獨立操作。化學品、水質、液體比例與基因由房主遊戲運算並同步數值。安裝包只有 DLL、manifest 與封面；原始碼在本儲存庫。

本次修正摘要請見 [1.3.44 更新紀錄](CHANGELOG.md)。

Version **1.3.44**, for game **0.46D**. Everyone must use the same build and restart. The host owns gameplay and the save; guests send requests and receive authoritative results or refusal reasons. Customer movement and dialogue follow the host. Travel, hand slots, the backpack and ground items are shared, while cursors and menus remain individual. Runtime packages contain only the DLL, manifest and preview. See the [changelog](CHANGELOG.md) for release notes.

## 本版核對範圍

修正程式已在獨立 Windows 桌面，以本機雙程序完成 `reported1344` 驗證：房主垃圾袋與背包選擇、全員出門及返回、共用背包操作、顧客進店及兩種離店路徑、連續三句台詞按序顯示、重新同步不重播。同行完成朋友加入、對話等待及推進、交付前拒絕出售與交付後成交、收益同步、容器搬移、格子尺寸變化及完整重新同步。

| 本輪本機取樣（239 個同步物件、12 秒） | 結果 |
|---|---:|
| 操作回覆中位數／P95 | 24.61／29.70 ms |
| 完成操作數 | 24 |
| 房主同步擷取中位數／P95 | 8.52／11.45 ms |

顧客同步與中槍動畫的交界修正另以一次 `death` 專項完成驗證：中槍變色、倒下期間隱藏及動畫後恢復房主狀態正確；房主旁觀者的鏡頭、背包和拖曳未被拉走。兩項完成時皆無客端套用失敗或房主診斷回報。首次驗證工具未等待原版淡出後開圖而中止，修正等待條件後才完成，不將中止算通過。

這些是同機雙程序驗證，不是跨帳號 Steam 外網、八人長時間或所有第三方模組組合的驗證，也沒有重新測完全部 84 項操作。發布階段只更新版號與文件並編譯正式包，不重跑遊戲測試。測試程序及暫存由執行器清理，原有玩家存檔、設定與訂閱 DLL 未被測試替換。

Local two-process checks covered host backpack selection, shared outings and return, customer arrival/departure, ordered dialogue, joining, sale refusals and successful sales, container moves and full resync. A separate fatal-shot check covered the interaction between replicated movement and local shot animations. These results do not establish Internet, eight-player or universal mod compatibility. Release preparation updates the version and documentation and compiles the production package.

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
./src/Tests/RunDual.ps1 -Suite reported1344
```

The runtime harness needs Windows, Steam, the game and the enabled Workshop mod. Close the game before running it. It runs a disposable game copy on a verified private Windows desktop with isolated test profiles, and closes only its own processes. It does not replace the subscribed Workshop files. Do not run two harness instances concurrently. The runner currently targets Workshop item 3837477869.

Each process must report `[SMC COMPLETE]`. The outing138 continuation covers native travel and return at the dumping ground, commissary and Dr. Jackson, changed-ground and occupied-item refusals, and commissary purchases/redemptions. Both processes completed this suite. The larger completion138 suite passed its earlier machine-confirmation, controlled-lamp, wholesale and dialogue cases but did not complete as a whole; outing138 continued the outing cases. The authority134 scenario covers the 21 overnight machine panels, a rejected grey context action, placement refusal, a machine button with no effect, and rejected recipe settings. A successful build alone is not a runtime pass. Local dual-process tests do not verify two distinct Steam accounts, Internet routers, every third-party mod or eight-player performance. Real Steam achievement writes are suppressed in the test harness.

`src/Tests/RunGroup.ps1 -Players 4 -SavePath <day70-save.es3>` (or `-Players 6`) runs a local group test from a disposable copy of a day-70 save. It reserves two counters for test items, checks independent menus, concurrent drags and first-holder ownership, and records frame and snapshot timings. Every game runs on a verified private desktop with its own disposable settings, and the runner checks that the original save and Workshop files remain unchanged. Add `-PerformanceOnly` to measure idle and concurrent rename-request latency without the functional drag sequence. All processes share one computer, so these timings do not measure separate computers or Internet latency.

## License

Mod source is available under the [MIT License](LICENSE). The license does not cover the game, its assets, Steam or other mods. No game binaries, saves, account credentials or generated cover art are included in this repository.
