# FileMirrorSync 架構與流程說明

本文描述「檔案鏡像同步系統」的 Server / Client 參考實作重點與設計考量。

## 1. 架構概觀
- **Server（ASP.NET Core Web API）**
  - API 路徑：`/api/sync/*`
  - 認證：Header `X-Api-Key`，綁定 `datasetId` 或 `clientId`
  - 儲存：`InboundRoot/{datasetId}` 作為資料集根目錄
  - 暫存：`TempRoot/{datasetId}/{uploadId}` 作為 chunk 暫存
- **Client（Console App）**
  - 透過 manifest 回報本機檔案清單
  - 依差異結果執行 chunk upload + complete
  - 本地狀態儲存在 JSON（`sync-state.json`）

## 2. ClientScope / Dataset 設計
支援兩種模式並使用相同程式碼：
1. **Multi-Client → One Dataset**
   - 多台 Client 使用相同 `DatasetId`
   - Server 實體路徑為 `InboundRoot/{DatasetId}/...`
2. **One Client → One Dataset**
   - 每台 Client 使用不同 `DatasetId`
   - Server 仍採 `InboundRoot/{DatasetId}/...`

## 3. 核心流程
1. **Manifest 比對**
   - Client 送出：`DatasetId`、`ClientId`、檔案清單（`path`、`size`、`lastWriteUtc`）
   - Server 透過 `ManifestDiffService` 建立差異
   - 回傳：
     - `upload[]`：每筆包含 `path` + `uploadId`
     - `delete[]`：僅在 LWW 刪除策略下回傳
2. **Chunk Upload**
   - Client 將檔案切成固定大小 `ChunkSize`
   - URL：`/api/sync/files/{base64Path}/uploads/{uploadId}/chunks/{index}`
3. **Complete**
   - Client 上傳完所有 chunk 後呼叫 complete
   - Server 合併 chunk、驗證 size/sha256
   - LWW 規則：
     - 若 Server 版本較新 → 忽略（回 204）
     - 若 Client 較新 → 原子替換並設定 `LastWriteTimeUtc`
4. **Delete（鏡像刪除）**
   - DeleteDisabled：不允許刪除（預設）
   - LwwDelete：需 `DeletedAtUtc > LastWriteTimeUtc` 才刪除

## 4. Manifest Diff 演算法與比較
### 4.1 採用演算法（Dictionary 比對）
- **時間複雜度**：O(N)  
- **空間複雜度**：O(N)  
- **優點**：
  - 適合大量檔案
  - 避免 nested loops 的 O(N²)
  - 以 path 作 key，符合檔案 identity

### 4.2 替代方案
1. **排序後雙指標比對**
   - 時間複雜度：O(N log N)
   - 空間：O(1) 或 O(N)
   - 優點：記憶體較低，但排序成本高
2. **增量掃描（上次狀態 diff）**
   - 需維護完整 state，實作較複雜
   - 適合超大量檔案、低變動率情境

## 5. 併發與競態處理
- Upload Session 以 `uploadId` 綁定相對路徑，避免 chunk 混寫
- `FileMergeService` 以 `ConcurrentDictionary<string, SemaphoreSlim>` 進行 per-path lock
- 避免兩個 Client 同時 complete 覆蓋同一檔案

## 6. 安全性
- 路徑驗證：
  - 禁止絕對路徑 / UNC
  - 禁止 `..` 路徑穿越
  - 檔名非法字元檢查
- API Key 驗證：
  - 可依 `datasetId` 或 `clientId` 綁定
  - 未授權請求一律拒絕

## 7. 潛在 Bug 與防護
- **Chunk 遺失/重送**：Complete 會驗證 `chunkCount` 與 size
- **舊版覆蓋新版**：LWW 比對 `LastWriteUtc`，舊版直接忽略
- **路徑穿越**：PathMapper 進行雙層檢核（相對路徑 + root boundary）
- **刪除誤判**：
  - Multi-Client 環境預設禁止刪除
  - LWW Delete 需 `DeletedAtUtc` 才允許

## 8. 可重用性與模組化
- PathMapper、ManifestDiffService、UploadSessionService、FileMergeService、VersionPolicy 各自單一責任
- 易於替換儲存介面（例如改為雲端 Blob）
- Client 可擴充為 Worker Service / Windows Service

## 9. 可行的 Refactor
1. **抽象 IStorageProvider**
   - 支援本機/雲端儲存切換
2. **Chunk 清理 Background Service**
   - 定期清理過期 upload session
3. **Manifest 分頁/增量**
   - 減少超大規模同步時的記憶體負載

## 10. 設定示例
### 10.1 Multi-Client → One Dataset
```json
// Client appsettings.json
{
  "DatasetId": "pdf-dataset",
  "ClientId": "pc-001",
  "ApiKey": "demo-secret-key"
}
```

### 10.2 One Client → One Dataset
```json
// Client appsettings.json
{
  "DatasetId": "pc-001",
  "ClientId": "pc-001",
  "ApiKey": "demo-secret-key"
}
```
