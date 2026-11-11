# 多人操作：可能很混亂 / Multiplayer Control: Probably Chaotic

A community co-op mod for **Probably Stolen**. Run the game on each computer and operate the same host-owned shop with separate cursors and interfaces. Up to eight players, including the host.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3837477869)

## 使用方式

1. 所有人訂閱工作坊、更新至同一版本，在遊戲的模組選單啟用。建議啟用的其他模組也與房主一致。
2. 房主從多人入口建立房間，邀請 Steam 好友或分享連線碼。朋友從多人入口加入。
3. 每人可以各自開選單、拖放不同物品。同一物品先取得控制權的人先操作，放開後才換人。
4. 外出拾荒使用自己的外出欄。Dr. Jackson 與其他模組地點須等房主先抵達，再選同一地點跟隨。
5. 多人成就開關由每台電腦自行設定。關閉只影響自己的多人期間成就解鎖與進度。

## How it works

The host runs gameplay simulation and owns the save. Guests render replicated game state and send validated action requests. This is neither desktop streaming nor remote keyboard/mouse injection. Item leases arbitrate competing actions; separate local UI state keeps one player's menus and aiming view from replacing another player's screen.

Compatibility with arbitrary third-party mods is not guaranteed. A mod with a new interface or custom state may need an adapter. Matching enabled mods and versions is recommended.

Direct connections use pinned TLS. Steam transport uses Steam networking identities. Incoming messages have direction, size and rate checks; actions are limited to registered game operations and valid accessible items. This does not expose a shell or general file access. The mod and game still run with your account's local permissions; these checks are not a security audit or a guarantee against every bug.

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
./src/Tests/RunDual.ps1
```

The runtime harness needs Windows, Steam, the game and the enabled Workshop mod. Close the game before running it. It temporarily replaces the installed Workshop DLL, runs isolated test shops without saving, then restores the original files and closes only its own processes. Do not run two harness instances concurrently. The runner currently targets Workshop item 3837477869.

Each process must report `[SMC COMPLETE]`. A successful build alone is not a runtime pass. Local dual-process tests do not verify two distinct Steam accounts, Internet routers, every third-party mod or eight-player performance. Real Steam achievement writes are suppressed in the test harness.

## License

Mod source is available under the [MIT License](LICENSE). The license does not cover the game, its assets, Steam or other mods. No game binaries, saves, account credentials or generated cover art are included in this repository.
