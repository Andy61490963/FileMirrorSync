# 📁 FileMirrorSync
**檔案鏡像同步系統（Server / Client 參考架構）**

FileMirrorSync 是一套以 **Manifest Diff + Chunk Upload** 為核心的檔案鏡像同步系統，支援 **單 Client / 多 Client** 同步到同一 Dataset，並可依策略控制覆蓋與刪除行為。

適合使用於：
- 檔案備份 / 同步
- 多台機器彙整資料
- 不可接受整包重傳的大型資料集

---

##  核心特色

- Manifest 差異比對（O(N)）
- Chunk-based 上傳（可中斷、可重送）
- LWW（Last Write Wins）版本控制
- 可關閉刪除以避免誤砍
- 完整路徑安全防護
- 架構模組化，易於替換儲存層

---

## 架構概觀

### Server（ASP.NET Core Web API）
- API Base Path：`/api/sync/*`
- 認證方式：`X-Api-Key`
- 儲存結構：
text
  InboundRoot/
    └─ {DatasetId}/             # 最終資料
  TempRoot/
    └─ {DatasetId}/{UploadId}/  # Chunk 暫存

### Client（Console App）

* 掃描本機檔案並產生 Manifest
    
* 根據 Server Diff 結果執行：
    
    * Chunk Upload
        
    * Complete / Merge
        
* 本地同步狀態儲存於：
    
    sync-state.json
    

* * *

## Client / Dataset 設計模式

### 1️ Multi-Client → One Dataset

多台 Client 同步到同一 Dataset（**預設禁止刪除**）

```text
Client A ─┐
Client B ─┼─▶ InboundRoot/{DatasetId}
Client C ─┘
```

適用場景：

* 多台機器上傳資料集中處理
    
* 共用資料池
    

* * *

### 2️ One Client → One Dataset

每台 Client 對應自己的 Dataset

```text
Client A → Dataset A
Client B → Dataset B
```

適用場景：

* 備份
    
* 個人鏡像同步
    

* * *

## 核心同步流程

### Step 1️ Manifest 比對

Client 送出：

```json
{
  "datasetId": "...",
  "clientId": "...",
  "files": [
    {
      "path": "a/b/file.txt",
      "size": 12345,
      "lastWriteUtc": "2026-01-01T12:00:00Z"
    }
  ]
}
```

Server 透過 `ManifestDiffService` 計算差異並回傳：

```json
{
  "upload": [
    { "path": "a/b/file.txt", "uploadId": "uuid" }
  ],
  "delete": []
}
```

* * *

### Step 2️ Chunk Upload

* Client 將檔案切為固定大小 Chunk
    
* API：
    
    ```
    POST /api/sync/files/{base64Path}/uploads/{uploadId}/chunks/{index}
    ```
    

* * *

### Step 3️ Complete（合併）

* Client 上傳完所有 Chunk 後呼叫 Complete
    
* Server：
    
    * 合併 Chunk
        
    * 驗證 size / sha256
        
    * 套用 LWW 規則
        

**LWW 行為**

| 情況 | 行為 |
| --- | --- |
| Server 較新 | 忽略（204 No Content） |
| Client 較新 | 原子替換並更新時間 |

* * *

### Step 4️ Delete（鏡像刪除）

* **DeleteDisabled（預設）**
    
    * 不執行任何刪除
        
* **LwwDelete**
    
    * 需 `DeletedAtUtc > LastWriteTimeUtc` 才允許
        

> Multi-Client 環境 **強烈建議關閉刪除**

* * *

## Manifest Diff 演算法

### 採用方案：Dictionary 比對（以 Path 為 Key）

* **時間複雜度**：O(N)
    
* **空間複雜度**：O(N)
    

優點：

* 可線性擴展
    
* 適合大量檔案
    
* 無巢狀迴圈
    

* * *

### 替代方案比較

| 方法 | 時間 | 空間 | 備註 |
| --- | --- | --- | --- |
| 排序 + 雙指標 | O(N log N) | O(1~N) | 記憶體低、排序慢 |
| 增量 State Diff | O(Δ) | 高 | 實作複雜、適合低變動 |

* * *

## 併發與競態控制

* 每次上傳綁定 `uploadId`
    
* Chunk 不會混寫
    
* `FileMergeService` 使用：
    
    ```csharp
    ConcurrentDictionary<string, SemaphoreSlim>
    ```
    
* 以 **檔案路徑為鎖粒度**，避免同時覆蓋
    

* * *

## 安全性設計

### 路徑安全

* 禁止：
    
    * 絕對路徑
        
    * UNC
        
    * `..` Path Traversal
        
* PathMapper 進行：
    
    * 相對路徑驗證
        
    * Root Boundary Check（雙層防護）
        

### API Key

* Key 可綁定：
    
    * DatasetId
        
    * ClientId
        
* 未授權一律拒絕
    
* * *

##  設定範例

### Multi-Client → One Dataset

```json
{
  "DatasetId": "pdf-dataset",
  "ClientId": "pc-001",
  "ApiKey": "demo-secret-key"
}
```

### One Client → One Dataset

```json
{
  "DatasetId": "pc-001",
  "ClientId": "pc-001",
  "ApiKey": "demo-secret-key"
}
```

* * *
