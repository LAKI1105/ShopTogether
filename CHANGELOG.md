# 更新紀錄 / Changelog

## 1.3.46 — 2026-11-22

- 修正已過期的物品占用，仍可能被遲到的續期重新延長，讓房主繼續被擋住。
- 房主記住的操作權失效時，現在會重新取得，不再拿舊的操作權搬物品。
- 沒有其他玩家占用、只是操作權失效時，改成提示「操作權已失效，請放開物品再重新操作」。
- 修正房主一次取得多件物品的操作權時，部分失敗卻留下占用，可能持續擋住朋友的問題。

已完成相關本機雙人驗證，包含過期處理、群組放開、背包、重新同步、強制直連斷線及重新加入。玩家回報的所有持續拖不動情況尚未在原環境重現，本次不宣稱全部解決。

### English

- Fixed expired item holds being extended by a late renewal, potentially continuing to block the host.
- The host now reacquires item control when its cached permission is no longer valid.
- An expired permission without another player holding the item now asks you to release it and try again, instead of saying another player is using it.
- Fixed failed multi-item control acquisition leaving earlier holds behind and potentially blocking guests.

Local two-process checks covered expiry, group release, backpacks, resync, forced direct-connection loss and rejoining. Not all reported persistent drag failures have been reproduced in players' environments; this release does not claim to resolve them all.

## 1.3.45 — 2026-11-20

- 修正房主在垃圾場拾荒時，搜尋次數用了，物品卻沒有出現在地上的問題。
- 修正朋友從遊戲選單回主畫面後，模組仍繼續同步店內資料，可能引發錯誤或斷線的問題。
- 機台畫面等待連線及場景準備完成後才更新。
- 修正物品狀態改變、交易尚未結束等操作拒絕，被誤顯示成「其他玩家使用中」；現在保留實際拒絕原因。

### English

- Fixed host scavenging consuming a search without placing the generated items on the ground.
- Stop guest synchronization before returning through the game's main-menu button, avoiding updates to a shop scene that is being unloaded.
- Wait for the connection and shop scene to be ready before refreshing machine panels.
- Stop mislabeling rejected item actions, such as changed item state or pending dialogue, as another player using the item. Show the actual refusal reason.

## 1.3.44 — 2026-11-17

- 修正房主晚上出門時，背包選不到、連垃圾袋選項都不見的問題。
- 修正朋友看到顧客突然出現、突然消失；現在會跟著房主顯示進店與離店的移動。
- 修正對話連續推進時，朋友漏掉前面一兩句的問題；重新同步也不會重播已顯示的同一句。
- 避免顧客移動同步干擾中槍、變色與倒下動畫。
- 維持全員同行，以及雙手槽位、背包和地面物品共用的規則。

### English

- Fixed the host being unable to choose an outing backpack or see the garbage-bag option.
- Guests now see customers moving into and out of the shop instead of appearing or disappearing instantly.
- Preserved dialogue lines when the host advances several lines between updates. Resync does not replay the same displayed line.
- Kept customer movement updates from overriding the local wounded and falling animations.
- Travel remains shared, as do hand slots, the backpack and ground items.
