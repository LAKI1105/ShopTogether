# 多人操作：可能很混亂 / Multiplayer Control: Probably Chaotic

A community co-op mod for **Probably Stolen**. Up to eight players, including the host, run their own game and share one host-owned shop with separate cursors and menus.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3837477869)

版本：**1.3.39**，適用遊戲 **0.46D**。所有玩家須更新至相同版本並重新啟動遊戲。店鋪存檔由房主持有；朋友送出操作請求，房主依當下條件執行，再同步結果與拒絕原因。

## 1.3.39 更新內容

- 修正拖曳途中影像消失或旋轉被重設；單件與多選都保留手上影像。實物失效或完整重新同步時，仍會取消拖曳。
- 修正拖曳中右鍵旋轉／翻面誤改拿取物品，以及舊拒絕回覆取消新一輪操作。
- 修正機台按鈕漏掉下一次點擊；水瓶打印按鈕也使用此流程。
- 修正拖曳裝水因溫度等資料變動被誤判過期；使用條件與液體運算仍由房主的原版遊戲決定。
- 收購偏好使用與原版交易相同的入口，納入該入口上的模組規則，朋友讀取房主答案。
- 修正裝著物品的容器跨庫存搬移後，朋友找不到內部庫存。
- 修正晚加入／重新同步的完整資料更新共用紀錄，卻漏送變化給已在線玩家。
- 改善加入期間的連線維持、場景載入失敗與逾時處理；隔離直連接入失敗，避免連帶中止整間房。
- 同步睡前結算與睡後報告明細；外出被拒後保留背包選擇，直到房主確認抵達。
- 補齊部分延後付款保護、真正的交易拒絕原因及酒吧英文文件／兌獎操作。
- 優化網路收包與多選拖曳核對，減少重複搬移、掃描及暫時字串。
- 移除深空串門（Probably Multiplayer）相容功能。安裝包只含 DLL、manifest 與封面；原始碼在 GitHub，不再把 ZIP 放進遊戲掃描的模組資料夾。

1.3.28 報告中的普通買賣「客人還在說話」是舊版誤用介紹狀態；現版已保留正確的買賣條件，沒有重新加入該限制。上述修正針對確認的程式缺口，不代表所有同類症狀或第三方模組情境都已排除。

Version **1.3.39**, for game **0.46D**. Fixes cover drag visuals and stale replies, missed machine clicks, stale-version pouring, selected buying preferences, filled-container moves, join/resync handling, daily reports, backpack retries and payment feedback. Network parsing and group-drag checks do less repeated work. Probably Multiplayer integration has been removed. Runtime packages contain only the DLL, manifest and preview; source is distributed through this repository. All players must use the same build and restart.

## 本版驗證範圍

已在確認隔離的 Windows 桌面，以本機雙程序驗證正式包掃描、TLS 加入、錯誤接入不拖垮房主、商品重同步後顯示與買賣、容器搬移、連續打印兩瓶、舊資料版本的裝水請求、拖曳影像／旋轉、多選與重新同步取消，以及外出拒絕後背包重試。完整快照分送與舊回覆使用受控資料檢查，不當作三台實機晚加入的驗證。

初輪整合測試在日結確認逾時；測試通道的報告長度處理修正後，只補驗未完成的日結與效能。睡前介面、換日、四條中文明細及獨立報告摘要比對通過。沒有宣稱完整腳本從頭到尾一次全過，也沒有因此修改正式日結邏輯。

本機雙端、41 件物品、12 秒取樣：房主增量快照 54 筆，中位數 **4.89 ms**、第 95 百分位 **7.90 ms**；20 次操作經房主執行後回傳，中位數 **17.59 ms**、第 95 百分位 **517.71 ms**。沒有改前對照，不宣稱提升百分比。這不是不同 Steam 帳號的外網測試，也不涵蓋八人房、大型店面或所有第三方模組。

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
```

The runtime harness needs Windows, Steam, the game and the enabled Workshop mod. Close the game before running it. It runs a disposable game copy on a verified private Windows desktop with isolated test profiles, and closes only its own processes. It does not replace the subscribed Workshop files. Do not run two harness instances concurrently. The runner currently targets Workshop item 3837477869.

Each process must report `[SMC COMPLETE]`. The outing138 continuation covers native travel and return at the dumping ground, commissary and Dr. Jackson, changed-ground and occupied-item refusals, and commissary purchases/redemptions. Both processes completed this suite. The larger completion138 suite passed its earlier machine-confirmation, controlled-lamp, wholesale and dialogue cases but did not complete as a whole; outing138 continued the outing cases. The authority134 scenario covers the 21 overnight machine panels, a rejected grey context action, placement refusal, a machine button with no effect, and rejected recipe settings. A successful build alone is not a runtime pass. Local dual-process tests do not verify two distinct Steam accounts, Internet routers, every third-party mod or eight-player performance. Real Steam achievement writes are suppressed in the test harness.

`src/Tests/RunGroup.ps1 -Players 4 -SavePath <day70-save.es3>` (or `-Players 6`) runs a local group test from a disposable copy of a day-70 save. It reserves two counters for test items, checks independent menus, concurrent drags and first-holder ownership, and records frame and snapshot timings. Every game runs on a verified private desktop with its own disposable settings, and the runner checks that the original save and Workshop files remain unchanged. Add `-PerformanceOnly` to measure idle and concurrent rename-request latency without the functional drag sequence. All processes share one computer, so these timings do not measure separate computers or Internet latency.

## License

Mod source is available under the [MIT License](LICENSE). The license does not cover the game, its assets, Steam or other mods. No game binaries, saves, account credentials or generated cover art are included in this repository.
