# FileMirrorSync 架構說明

本文描述 HTTP-based 檔案鏡像同步（類似 rsync mirror）的 Server 與 Client 範例，以及 Manifest Diff 的核心演算法。

## Server（ASP.NET Core Web API）
- 路由：`/api/sync/*`，強制 HTTPS。
- 認證：Header `X-Api-Key`，依 `clientId` 對應。
- 儲存：`Storage.InboundRoot` 作為鏡像根目錄（每個 clientId 一個子資料夾），`Storage.TempRoot` 作為 chunk 暫存。
- 路徑安全：所有相對路徑經 `Path.GetFullPath` 確認後，必須位於對應的 client 根目錄內，拒絕 `..`、絕對路徑與 UNC。
- 上傳流程：
  1. Client 呼叫 `POST /api/sync/manifest`，Server 透過 `FileSyncService.Diff` 計算差異，回傳需上傳與刪除的清單。
  2. 對於需上傳的檔案，Client 以 `PUT /api/sync/files/{base64Path}/chunks/{index}?clientId=` 送出 8MB chunk，Server 直接覆寫暫存 chunk 檔，支援重送。
  3. 上傳完畢後呼叫 `POST /api/sync/files/{base64Path}/complete`，Server 驗證 chunk 數量、檔案大小與 Hash，最後以原子 Move 取代正式檔並清除 chunk。
  4. 對需刪除的檔案呼叫 `POST /api/sync/delete`，Server 僅刪除鏡像根目錄內的目標。

## Client（C# Console）
- 掃描設定的根目錄，產生 manifest（`path`、`size`、`lastWriteUtc`，若檔案有變動才重新計算 `sha256`）。
- 將 manifest 送到 Server，依差異結果逐檔分 chunk 上傳並完成驗證，最後發送刪除請求同步鏡像。
- 透過 JSON 檔案保存前一次同步狀態，避免重複計算 Hash；Chunk 上傳採 `HttpClient` 可重送策略。

## Manifest Diff 核心演算法
1. 將 Client 與 Server 的檔案清單以路徑為 key 建立字典（忽略大小寫）。
2. 對於 Client 端的每個檔案：
   - 若 Server 沒有該路徑 → 加入 `upload` 清單。
   - 若 `size` 或 `lastWriteUtc` 不同 → 加入 `upload` 清單。
   - 若提供 `sha256` 且與 Server 不同 → 加入 `upload` 清單。
3. `delete` 清單 = Server 檔案集合減去 Client 檔案集合（路徑差集）。
4. 複雜度：
   - 時間：O(n)，以字典查找為主；適合大量小檔案。
   - 空間：O(n)，儲存字典以快速比對。可透過串流列舉與分批處理降低峰值記憶體。

## Bug 預防與重構建議
- **路徑安全**：所有外來路徑統一透過 `PathMapper.GetSafeAbsolutePath` 處理，避免遺漏檢查。
- **Chunk 清理**：`CompleteUploadAsync` 成功後清除所有 chunk，並在驗證失敗時刪除暫存檔避免累積垃圾。
- **Hash 計算成本**：僅在 `size` 或 `lastWriteUtc` 變更時重算；狀態檔記錄 hash，避免每次全量計算。
- **可測性**：服務層為純邏輯，便於以單元測試覆蓋 Diff 與路徑驗證。未來可將 chunk 儲存抽象化以支援雲端儲存。
- **可重用性**：`SyncRunner`、`ManifestBuilder`、`FileSyncService` 皆為可注入的獨立元件，方便改為 Windows Service 或 WebJob 執行。

## Logging 與可觀測性
- Server 與 Client 均使用 Serilog，啟動時先建立 Console bootstrap logger，再依 `AppLogging` 設定決定最低層級、檔案與 Seq Sink。
- 檔案 Sink 以每日滾動檔案與大小上限控制體積，失敗時會寫入 `serilog-selflog.txt` 自我診斷。
- 主要業務流程（Manifest 比對、chunk 上傳、合併、刪除、狀態檔存取）皆有資訊層級紀錄；Hash 重用與重算則以 Debug/Information 區分成本與行為。
- 設定檔自訂：`AppLogging.ApplicationName` 用於標記來源，`File` 與 `Seq` 區段可獨立啟用/停用並調整保留天數與傳送週期。
