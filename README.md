# CrescentCompass｜新月島尋寶羅盤

繁中服 Dalamud API 13 插件，支援寶箱巡查、魔法罐、FATE／CE、探索筆記、標點預設與幻影職業巨集。輸入 `/crescent` 開啟。

## 0.10.5 更新內容 · 2026-10-05

- **背包整理**：`/crescent loot` 開啟 292 種已記錄寶箱物品清單；逐項保留／垃圾、搜尋與背包數量。
- **自動處理**：島內自動丟棄、開啟一般 NPC 商店後自動售出；預設全部保留，明確啟動後才處理。詳見 [背包整理說明](docs/LOOT_CLEANUP.md)。
- **連續售出修正**：支援停留在出售或回購列表售出背包垃圾；售出後自動切到回購頁也會繼續，不需切換分頁。

既有功能：

- **持續開箱**：2 公尺內立即嘗試，每秒最多一次，取消停穩等待與三次上限，支援地面騎乘。
- **幻影職業巨集**：`/crescent job 騎士` 可切換職業；`/crescent jobs` 提供 13 種職業圖標與複製巨集。
- **快捷列職業圖示**：單行職業巨集的預設 M 自動換成對應圖示；關閉巨集編輯視窗後套用，也可輸入 `/crescent jobicons` 立即更新。
- **標點預覽**：新增俯視圖與力之塔四個王房的場地輪廓示意。

1172 項核心檢查與離線介面檢查通過；遊戲內丟棄／售出及巨集圖示保存仍待實測。[詳細更新紀錄](CHANGELOG.md)

## 安裝

在 Dalamud「自訂套件庫」加入：

```text
https://raw.githubusercontent.com/wenwen-ff14/Occult_Crescent_guide_TC/main/pluginmaster.json
```

重新整理插件列表，搜尋 **CrescentCompass** 安裝或更新。地形路線需搭配相容 API 13 的 vnavmesh。

[下載 0.9.7](https://github.com/wenwen-ff14/Occult_Crescent_guide_TC/raw/refs/heads/main/packages/CrescentCompass/0.9.7.0.zip) · [使用說明](USER_GUIDE.md) · [職業巨集說明](docs/PHANTOM_JOBS.md) · [建置方式](BUILDING.md)

自動開箱預設關閉，需在「巡查」或「設定」勾選啟用。

![幻影職業與巨集介面（離線示範）](docs/previews/phantom-jobs.png)

AGPL-3.0-or-later。第三方資料與授權見 [NOTICE](third-party/NOTICE.md)。
