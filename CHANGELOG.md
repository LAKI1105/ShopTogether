## 1.3.48 — Official reflection API migration

- Replaced computed reflection targets with the game-approved `ModReflect` API; ordinary game-type reflection remains explicit and scanner-readable. The production DLL no longer needs `reflection-external`.
- Preserved host-authoritative actions, separate menus/cursors and existing bartender, company, storage-page and overflow integrations. The four optional integration mods are declared as prerequisites to authorize cross-mod access; missing integrations do not stop ShopTogether from loading.
- Fixed a machine-setting bug found during the migration checks: after applying a batch, the host now runs the native shape/state refresh, so a selected chemical recipe becomes active as well as being synchronized.
- Customer movement uses DOTween's public pause API, including on the current stable game where `ModReflect` does not yet allow DOTween.
- Updated the game-version declaration to `1.0.0` as requested by the official API guide. No changes to the transport protocol's permission checks or player save format.

### 繁體中文

- 改用遊戲官方允許的反射介面，正式 DLL 不再需要 `reflection-external`，處理 12 月 10 日更新的拒載風險。
- 保留房主運算、各自選單與游標，以及酒吧、公司、主倉分頁和溢出倉儲的相容功能。四個相容模組列入官方允許存取的前置清單，沒有安裝它們仍可使用多人功能，但遊戲會顯示缺少前置的提醒。
- 修正測試中發現的機台設定問題：房主寫入整批設定後，會更新機台實際使用的狀態，避免化學配方名稱已同步、機台卻仍判定未設定。
- 客人移動改用 DOTween 的公開介面暫停，兼顧目前穩定版遊戲。
- 本次不變更連線操作權限檢查或存檔格式。其他模組本身仍須各自符合遊戲的新安全規則。

# 更新紀錄 / Changelog

## 1.3.47 — 2026-11-23

- 修正大量同步資料堆積時，客機可能被模組判成連線失敗；現在會等資料消化後再繼續接收。
- 修正物品交換槽位或替換舊物品時，客機可能同步失敗。
- 修正全員出門時，尚未關閉的背包選擇畫面可能讓操作持續被鎖住。
- 修正錄音機、議價面板隨整個介面開關時出現的介面排序錯誤。
- 遊戲正常關閉時，不再把載入器一併關閉記成遊玩中的模組故障；遊戲中真的發生的錯誤仍會記錄。
- 加入遭拒且連線隨即關閉時，保留已收到的拒絕原因；Steam 回報的連線失敗原因也會保留。
- 水純度異常增加實際呼叫來源及水量紀錄。原回報的觸發原因仍未定位，沒有改動水質計算，也不宣稱已修好。

### English

- Wait for the guest to process buffered sync data instead of treating a full local receive queue as a connection failure.
- Fix guest synchronization failures when items swap slots or replace an old occupant.
- Close an outstanding backpack-choice window when the group leaves, releasing its input lock.
- Defer recorder and negotiation panel reordering until parent-interface activation has finished.
- Do not record the native loader's normal application-shutdown message as an in-game mod failure. Actual in-session errors are still reported.
- Preserve an already-received join refusal when the connection closes immediately afterward, and retain Steam's connection-failure reason.
- Add caller and liquid-quantity context to water-classification errors. The original player's trigger remains unlocated; water calculations are unchanged and this is not claimed as a fix.

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
