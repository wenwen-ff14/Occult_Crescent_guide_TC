# 玩家顯示（0.10.6）

在 `/crescent` 的「設定」頁勾選「隱藏非小隊／好友玩家（倒地仍顯示）」。預設關閉，設定可儲存；只在新月島南部或北部運作。

首次使用請先在島外開啟遊戲好友名單，等待約 2 秒，確認設定頁的「好友快取 N 人」符合名單後再進島。島內無法查看好友名單，因此快取按登入角色分開儲存於本機插件設定；進島、重新登入或重新載入插件後仍可使用。若遊戲已保留可讀的名單，會直接建立快取。沒有此角色的有效快取時會暫停隱藏，避免把未組隊好友誤判為陌生人。

新增／移除好友後，在島外重新開啟名單以更新快取；島內保留最後一份快取，不用空白或不可讀名單覆蓋。快取只含好友角色 ID、角色名稱及原始伺服器，不上傳或列入日誌。

| 目前載入的角色 | 開啟時的行為 |
|---|---|
| 自己、小隊成員、好友 | 保留顯示 |
| 倒地玩家，或 HP 為 0 | 保留顯示；若之前由本功能隱藏則恢復 |
| 其他存活玩家 | 隱藏模型 |
| 復活的其他玩家 | 重新判斷小隊／好友關係，未符合保留條件就再次隱藏 |
| NPC、寶箱、蘿蔔、寵物、坐騎 | 不另外修改 |

小隊與好友是兩種獨立保留條件，符合其中任一種就顯示。其他聯盟小隊成員如果不是本小隊或好友，仍屬隱藏對象。這只控制本機玩家模型，不修改名字牌、聊天、選取功能或其他人的畫面；不會增加遊戲引擎可載入的人數或強制顯示遠方角色。

每次框架更新重新判定。關閉、離島、登出、傳送讀取、過場與合照模式會撤回本功能加上的隱藏位元；恢復島內正常畫面後，若開關仍開啟就重新套用。關閉插件視窗仍持續運作。一般卸載時回到框架執行緒還原目前載入的物件；遊戲本身正在關閉時不再排程記憶體操作。

## 實作與還原範圍

使用本機繁中 SDK 的 `VisibilityFlags.Model` 列舉，僅修改 `GameObject.RenderFlags` 的模型位元。此版 SDK 欄位型別為 `int`，以原欄位型別寫入，不自行推測偏移或把上游新版結構套到繁中客戶端。上游 [FFXIVClientStructs GameObject 定義](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Object/GameObject.cs) 提供列舉用途，本機中繼資料已確認 `Model = 2`。

小隊判定合併 `StatusFlags.PartyMember` 與 `IPartyList.EntityId`。原本好友只使用 SDK 的 `StatusFlags.Friend`，該旗標來自角色的關係欄位，沒有獨立查詢好友名單；使用者回報新月島內非小隊好友仍被隱藏。0.10.6 另外以 `InfoProxyFriendList` 的本機名單建立快取，與關係旗標取聯集；按 Content ID 比對，缺少 ID 時使用「完整角色名稱＋HomeWorld」，不使用目前造訪的伺服器，也不只比對名字。已知且不相同的 Content ID 不因同名而視為同一人。倒地仍使用 `IsDead` 與 `CurrentHp == 0`。

名單每秒讀取一次，連續兩次一致且間隔至少 1 秒才取代快取；排序或重複資料不影響比對。排除待核准的好友請求，驗證本機角色 ID、名單指標與最多 200 筆的界限。空名單僅在島外好友視窗已開啟且就緒時可取代快取；進島後不刷新已有快取。關閉隱藏與還原模型不依賴好友讀取成功。

上游依據：[Dalamud 關係旗標](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Game/ClientState/Objects/Types/Character.cs)、[InfoProxyFriendList](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Info/InfoProxyFriendList.cs)、[好友資料欄位](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Info/InfoProxyCommonList.cs)。實際編譯使用本機繁中 API 13 SDK；未套用上游新版偏移。

本機離線核對：`InfoModule.GetInfoProxyById` 為 RVA `90E9D0`（代理陣列 `0x1978`）、`GetLocalContentId` 為 `90EAD0`（本機 Content ID `0x1A90`）、`InfoProxyCommonList.GetEntry` 為 `912550`（數量 `0x10`、資料指標 `0xB0`、每筆 `0x70`）。三個 SDK 特徵碼皆唯一匹配 SHA256 `837B9E2893D45D22C1DDE3D1A134AF2749E0C9FFF6E2EF7246B84860F855E247` 的客戶端。可用 `tools/VisibilityAudit --native <ffxiv_dx11.exe>` 重查。

只記錄本功能實際加上的位元。原本已隱藏的模型不取得還原權，其他繪圖位元保持不變。每次寫入前重新以遊戲物件 ID 查找載入物件，比對玩家類型、位址、實體 ID 與物件索引；不透過上次保存的指標直接寫入。物件離開載入範圍後丟棄紀錄，避免位址重用時還原錯人。還原未成功時保留紀錄供下一幀重試。其他插件同時改寫相同模型位元時，無法判定其寫入來源，需實機確認共存情形。

1197 項核心檢查通過。玩家顯示相關檢查涵蓋倒地／復活、非小隊好友且無關係旗標、同名不同服、缺少 Content ID、已隱藏好友恢復、角色隔離、快取序列化重載、島內空名單、刪除好友，以及既有的模型位元與位址重用保護。這不等於實際遊戲渲染已驗證；仍需在島外建立快取後，於島內用未組隊好友驗收。
