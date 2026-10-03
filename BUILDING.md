# 開發與建置

## 環境與手動建置

需要 .NET SDK 9.0.300 以上的 9.x 版本，及繁中版 API 13 的 Dalamud 參考 DLL。

設定 `DALAMUD_HOME` 為 SDK 目錄，或在 `.sdk/` 放入啟動器提供的參考 DLL。`.sdk/` 不會包含於原始碼或發行壓縮檔。

```powershell
dotnet build CrescentCompass/CrescentCompass.csproj -c Release -p:RestoreLockedMode=true
dotnet run --project tests/CrescentCompass.Validation/CrescentCompass.Validation.csproj -c Release
./scripts/Package.ps1
```

### 維護自訂套件庫

修改專案版本後，執行下列命令。腳本使用獨立輸出資料夾編譯，產生版本固定的 `packages/CrescentCompass/<組件版本>.zip` 與根目錄 `pluginmaster.json`；會核對 DLL、包內 manifest、API 13 與安裝／更新網址。已存在的版本包不覆寫，後續更新需提高專案版本。

```powershell
./scripts/Prepare-PluginRepository.ps1 -Changelog '本次更新說明'
./scripts/Verify-PluginRepository.ps1
# 將原始碼、packages/ 與 pluginmaster.json 一起提交至 main 後：
./scripts/Verify-PluginRepository.ps1 -Online
```

套件庫格式依照 [Dalamud 自訂套件庫文件](https://dalamud.dev/plugin-publishing/custom-repositories/)。只發佈插件 DLL 與必要相依檔；本機 `.sdk/`、研究暫存 `.references/`、建置暫存與舊版 `dist/` 不上傳。

驗證程式涵蓋最短路線與暴力枚舉結果比對、120 站效能、候選點狀態、離開載入範圍、同區域新場次、物件重新出現、垂直樓層、地圖座標轉換、八份點位資料、探索地點獨立篩選、個人探查數量解析、開箱自動下一站、空點等待與中斷、略過與重新出現、旗標重試與取消，以及魔法罐方向解析、候選縮小、加碼與現身比對。遊戲中的物件辨識、場景繪圖、旗標座標及轉場需依 [遊戲驗收清單](docs/IN_GAME_CHECKS.md) 驗證。

### 離線介面預覽

`tools/UiPreview` 直接編譯插件共用的 `Ui/` 程式，使用本機 ImGui 繪圖資料與字型產生 PNG，不啟動遊戲。需在 `.sdk/` 額外放入同版 `cimgui.dll`，並使用 Windows 內建微軟正黑體。

```powershell
dotnet run --project tools/UiPreview/UiPreview.csproj -c Release
```

輸出至 `artifacts/ui-preview/`：標準、窄版、150% 字體、等待、魔法罐、探索筆記與全島已知位置預覽。這些預覽使用示範狀態，不能替代遊戲物件與地圖旗標驗證。

幻影職業的圖標可由本機遊戲資料匯出供離線預覽使用（不提交或打包匯出的原始圖標）；未匯出時預覽會顯示編號備援。唯讀工具同時核對職業資料及 SDK 特徵碼：

```powershell
dotnet run --project tools/PhantomAudit -c Release -- '<遊戲目錄>/game/sqpack' '<遊戲目錄>/game/ffxiv_dx11.exe' artifacts/phantom-icons
dotnet run --project tools/UiPreview -c Release -- artifacts/ui-preview phantom-jobs
```


## 統一驗證入口

從專案根目錄執行 `./scripts/Validate.ps1`：Release 編譯、核心回歸及本機套件庫驗證。加上 `-Preview` 執行 UI 互動檢查並更新文件截圖；加上 `-Package` 產生安裝包與原始碼包。腳本也可從其他目錄呼叫，會自動切換至專案根目錄。

`global.json` 使用 .NET 9.0.100 作最低 SDK feature band，允許使用已安裝的較新 .NET 9 feature band；本機已驗證 9.0.101。
