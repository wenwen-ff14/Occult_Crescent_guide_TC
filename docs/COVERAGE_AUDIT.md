# 寶箱覆蓋稽核 · 0.3.0

**0.4.0 行為更新：** 預設「全島已知位置」，保留本場曾偵測為可選取的目標；未載入位置標為待確認，不能保證個人開箱權限。可切換「目前可選取」或「全島候選巡查」，僅後者顯示下方候選搜尋流程。新增個人全島探查數量快照與 13 個南區探索地點，見 [新增資料稽核](EXPLORATION_AND_SURVEY.md)。核心驗證現為 173 項；寶箱靜態資料覆蓋稽核結果不變。

日期：2026-09-29。繁中客戶端資料版本：`2026.09.14.0000.0000`。

0.4.2 補充（2026-09-30）：空點確認改以有無可用箱／開啟狀態判定，預設 60 公尺可調；新增等待原因與未巡查優先續巡，核心驗證為 227 項。靜態資料與本頁覆蓋結論不變。略過只代表範圍內連續沒有可用箱，不是開箱或個人權限證據。

**結論：舊版未包含全部寶箱類別。南區固定野外箱點已驗證 68／68，這不等於所有魔法罐、兔子或伺服器生成寶箱的位置已全部驗證。**

| 範圍 | 核對結果 | 0.3.0 行為 |
|---|---|---|
| 南區野外固定箱 | `Field_Treasure` 68 個；與內建資料逐點一對一比對，最大 XYZ 誤差 0 | 保留 60 銅箱、8 銀箱候選點 |
| 南區塔內／獎勵箱 | `BA_treasure` 14 個；舊版未收錄 | 補入獨立分類，預設不加入野外巡查 |
| 南區魔法罐候選 | 公開固定版本有 60 個一般、20 個加碼點，共 80 個不同位置 | 新增候選搜尋與取得狀態後自動插旗；不宣稱已完成伺服器點位全集核對 |
| 魔法罐現身寶箱 | 本機 `EObjName` 確認 `2014741/42/43` 分別為金／銀／銅財寶箱；BOCCHI 識別為罐子現身箱 | 新增可互動事件物件偵測；個人發現提示後才嘗試自動標實際座標 |
| 兔子金箱 | `2012936` 為黃金財寶箱；技術來源辨識為幸運胡蘿蔔的幸福兔箱 | 新增事件物件偵測；不將兔子箱當成罐子箱自動鎖定 |
| 其他 Treasure 物件 | 舊版僅接受 SGB 1596、1597 | 補金箱模型 1598；未知模型歸「其他寶箱」，不再直接漏掉 |
| 北區固定點 | 公開來源為 60 銅、8 銀、25 蘿蔔點；本機無 Territory 1346 | 保留資料，但標示繁中客戶端未核對 |
| 北區魔法罐 | 62 個一般、21 個加碼紀錄；一個位置同時屬兩組，因此共 82 個不同位置 | 保留 83 筆組別資料；合併搜尋時去除重複位置，保留加碼歸屬；未經本機驗證 |

## 核對方法與限制

以 Lumina 讀取本機 EXD 與南區 `bg/ex5/03_ocn_o6/btl/o6b1/level/` 的 LGB 場景。實際可讀的場景檔為 `bg.lgb`、`planmap.lgb`、`planevent.lgb`、`planlive.lgb`。82 個 `Treasure` placement 都出現在 `planmap.lgb`。

68 個野外點採 **三維座標一對一核對**，不能只比較數量；程式也拒絕重複配對。Excel `Treasure` 的 1789–1796 對應銀箱模型 1597，1797–1856 對應銅箱模型 1596，另行確認資料中的 DataId／模型。使用的 Lumina 場景讀取器將 placement 的 ParentData.BaseId 讀為 0，故**沒有把場景 BaseId 與資料表 ID 的對應宣稱為已驗證**。塔內快照使用場景 instanceId 作識別，DataId 0 表示未取得，執行時依位置分類。

本次靜態資料核對涵蓋上述 LGB 的頂層 Treasure placement。事件物件、伺服器控制的生成位置、未開放區域、未來補丁與多人共享寶箱的歸屬，不能由這份離線快照證明完整。

保留的證據：

- [本機場景座標快照](audit/southhorn-scene.json)
- [本機狀態、事件物件、訊息頻道與模型稽核輸出](audit/client-data-audit.txt)
- [內建八份資料 SHA-256](audit/catalog-sha256.json)
- [讀取工具](../tools/GameDataAudit/Program.cs)
- [一對一比對腳本](../scripts/Audit-LocalCatalog.ps1)
- [候選資料轉換腳本](../scripts/Import-PotCandidates.ps1)

重現靜態比對（在專案根目錄執行；會重新產生同一份塔內候選檔）：

```powershell
./scripts/Audit-LocalCatalog.ps1 -Snapshot docs/audit/southhorn-scene.json
./scripts/Import-PotCandidates.ps1 -SourceDirectory third-party/BOCCHI-source
```

重新讀取遊戲資料需將 Lumina.dll、Lumina.Excel.dll、Microsoft.Extensions.ObjectPool.dll 放入 `.sdk/`：

```powershell
dotnet run --project tools/GameDataAudit/GameDataAudit.csproj -c Release -- 'C:/Program Files/USERJOY GAMES/FINAL FANTASY XIV TC/game/sqpack'
```

## 魔法罐自動標點的確切含義

1. 在新月島內取得狀態 `1531`「指引財寶」，自動建立搜尋並標出最近的**候選搜尋點**。載入插件時若已有狀態也能開始。初始位置不是隱藏寶箱答案。
2. 玩家自行使用聖靈藥；收到繁中系統方向提示後，以提示到達時的玩家位置為起點，交叉篩選八方向候選。遠近文字顯示在介面，**沒有假設未知的距離區間來刪除候選**。
3. 收到「第二處財寶」提示時自動切換加碼池並更新旗標。衝突提示使目前搜尋目標失效，下一次有效提示會恢復自動更新；手動重設僅為備援。
4. 個人「發現了財寶」提示後 8 秒內，若原點 30 世界單位內只有一個新增、可互動的魔法罐寶箱，改用實際物件座標。30 是本插件保守關聯門檻，不是遊戲規則。模糊情況不猜測歸屬，仍可從附近列表手動插旗。
5. 狀態消失、換區或換分流會重設搜尋。事件箱不再載入時，不繼續顯示為目前已現身，也不直接當作完成。原生地圖既有旗標可能保留，插件不會清除玩家之後手動設定的旗標。

0.4.3 起自動插旗獨立於一般寶箱顯示範圍，預設開啟，可在介面關閉；原生地圖只容納一個目的地旗標，會被新的魔法罐搜尋目標更新。尋寶期間暫停一般巡查自動換旗。插旗失敗先間隔至少 1.5 秒嘗試，三次仍失敗後改為每 10 秒自動重試，直到成功、目標失效或功能關閉。插件不會自動使用道具、移動或開箱。

繁中 API 13 沒有較新版的 LogMessage ID 訂閱介面，因此使用系統／NPC 訊息事件及本機確認過的繁中文字模板；聊天來源拒絕玩家頻道與 Echo。本機上述 LogMessage 的 LogKind 均為 57，與本機 Dalamud `XivChatType.SystemMessage` 的值一致。0.4.3 另讀取 IToastGui 的一般、任務及錯誤通知作為備援，僅接受相同完整模板、不修改遊戲訊息；同提示 1 秒內合併。狀態尚未掃描到時暫存提示最多 3 秒，換區／分流後丟棄。實際渲染後文字與事件到達仍須實機驗證，介面保留手動方向備援。

### 0.6.1 完整顯示文字修正

本機 `LogMessage` 10994 前方含 `BNpcName` 巨集，實際為「撒嬌甕似乎能夠告知第二處財寶所在地！」；10986–10989 的方向巨集包含「正北／正東／正南／正西」。先前僅檢查純文字抽取，遺漏了這些替換值。0.6.1 改為接受已核對的名稱與完整方向，並補上 10990 的「魔法聖靈藥」道具名稱；任意前綴、玩家引述及多餘後綴仍拒絕。

已核對 `BNpcName` 13742「撒嬌甕」、712「魔法甕」與 `EventItem` 2003296「魔法聖靈藥」。核心累計 **605 項**驗證通過，包含實際訊息到第二箱自動旗標的完整離線流程；未宣稱遊戲內第二次尋寶已驗收。原始巨集與來源見 [查核紀錄](audit/pot-hint-templates.txt)。

## 驗證狀態（0.3.0）

核心驗證通過 **142 項**，包含最短路線與枚舉對照、所有內建資料、八方向與四種遠近文字、取得／失去狀態、加碼池、重複組別、提示衝突、錯區、舊寶箱、模糊寶箱、逾時及物件離開。

API 13 編譯與離線 ImGui 寬版／窄版／150% 字體預覽已檢查。**尚未實際登入遊戲驗證取得魔法罐、方向提示到達與地圖插旗。** 詳見 [遊戲內驗收](IN_GAME_CHECKS.md)。

## 公開來源

- [BOCCHI 固定提交](https://github.com/OhKannaDuh/BOCCHI/tree/aa4f6efce52d78c3c3992e88d2bcd7bb218904f2)：地圖與候選資料、罐子狀態和事件識別、方向篩選參考。
- [BOCCHI 南區資料](https://github.com/OhKannaDuh/BOCCHI/blob/aa4f6efce52d78c3c3992e88d2bcd7bb218904f2/BOCCHI.Common/Data/Zones/Implementations/SouthHorn/SouthHorn.cs)
- [BOCCHI 北區資料](https://github.com/OhKannaDuh/BOCCHI/blob/aa4f6efce52d78c3c3992e88d2bcd7bb218904f2/BOCCHI.Common/Data/Zones/Implementations/NorthHorn/NorthHorn.cs)
- [LimLoToolkit 技術記錄](https://github.com/Sansflaire/LimLoToolkit/blob/main/docs/occult-crescent.md)：金箱模型、兔子事件箱與 Treasure 物件的區別；此來源本身引用 BOCCHI，不能算獨立遊戲實測。
