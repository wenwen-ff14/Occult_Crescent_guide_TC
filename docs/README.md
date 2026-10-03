# 專案結構與文件索引

## 程式碼

| 路徑 | 責任 |
|---|---|
| `CrescentCompass/` | Dalamud 插件、遊戲介面、設定與資料 |
| `CrescentCompass/Plugin.*.cs` | 各功能的遊戲狀態與原生操作整合 |
| `CrescentCompass/Ui/` | 共用 ImGui 介面，供插件與離線預覽使用 |
| `CrescentCompass.Core/` | 不依賴 Dalamud 的路線、追蹤與規則 |
| `tests/CrescentCompass.Validation/` | 核心行為回歸檢查 |
| `tools/UiPreview/` | 離線介面渲染及模擬互動 |
| `tools/*Audit/` | 遊戲資料、寶箱與標點研究工具；需個別提供本機資料 |
| `scripts/` | 驗證、打包、套件庫與資料匯入工具 |
| `third-party/` | 第三方原始資料與授權，保留出處 |

介面依責任拆分：`CompassView.cs` 負責頁面組合與巡查內容；`Design` 負責導覽及視覺分區；`Map` 負責地圖與下一站；`Pot` 負責魔法罐；`Settings` 負責設定；`Widgets` 負責共用元件。其他頁面保留各自的功能檔，命名空間及類別不變。

## 使用與驗收

- [自動開箱](AUTO_CHESTS.md)
- [幻影職業與巨集](PHANTOM_JOBS.md)
- [標點預設](WAYMARKS.md)
- [標點俯視與力之塔場地預覽](WAYMARK_PREVIEW.md)
- [68 點圖表巡查](CHEST_CHART.md)
- [地形步行路線](WALKING_ROUTES.md)
- [CE 冷卻](CE_COOLDOWNS.md)
- [FATE 自動旗標](FATE_AUTO_FLAGS.md)
- [魔法罐 FATE 倒數](POT_FATE_TIMERS.md)
- [探索與探查](EXPLORATION_AND_SURVEY.md)
- [玩家顯示](PLAYER_VISIBILITY.md)
- [遊戲內驗收](IN_GAME_CHECKS.md)
- [資料覆蓋查核](COVERAGE_AUDIT.md)

## 產物管理

- `docs/previews/`：文件使用的示範截圖，透過 `scripts/Validate.ps1 -Preview` 更新。
- `docs/audit/`：資料查核依據，保留原始記錄。
- `artifacts/ui-preview/`：完整 UI 驗證輸出，不提交、不打包。
- `dist/`：本機安裝包與原始碼包，不提交。
- `packages/` 與 `pluginmaster.json`：已發布套件庫資料，整理專案不改動發布版本。
- `.sdk/`、`.references/`、`bin/`、`obj/`：本機依賴與中間產物，不提交、不打包。

開發命令見 [BUILDING](../BUILDING.md)，歷史變更見 [CHANGELOG](../CHANGELOG.md)。
