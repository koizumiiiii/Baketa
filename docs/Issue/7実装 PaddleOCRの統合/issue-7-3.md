# 実装: OCRモデル管理システム

## 概要
OCRモデルのダウンロード、更新、管理を行うシステムを実装します。

## 目的・理由
PaddleOCRでは言語やタスク（検出、認識など）ごとに複数のモデルファイルが必要であり、それらの管理システムを実装することで、ユーザーが言語を変更する際の自動ダウンロードや、モデルの更新確認などを効率的に行うことができます。

## 詳細
- モデル管理システムの設計と実装
- モデル情報の定義と管理
- モデルのダウンロードとキャッシュ機能
- モデルの自動更新確認機能

## 初期実装での最適なアプローチ

### モデル選択の簡素化
- 初期段階では標準検出モデル(det_db_standard)と日本語認識モデル(rec_japan_standard)のみを実装
- モバイル向け軽量モデルやその他の代替モデルは将来の拡張フェーズに先送り
- 方向分類モデル(cls_standard)は、ゲーム画面では通常テキストの向きが一定なので、必要に応じて後から追加

### 設定UIの簡素化
- Issue 13で計画されている複雑な設定UI機能も初期段階では最小限に
- 基本的なON/OFFスイッチと必須の設定のみを実装
- 詳細な調整機能やプロファイル管理は後のフェーズに延期

### OCRシステムドキュメントとの整合性
現在のOCRシステムドキュメントとIssue 7には、必ずしも全モデルを実装する必要性は明記されていません。むしろIssue 7の親Issueでは基本的なOCR機能の実装が主眼とされており、これは最小限のモデルセットでも達成可能です。

Issue 13についても、設定UIとプロファイル管理の「実装」は記載されていますが、その複雑さのレベルは明示されていません。初期版では基本的な設定項目のみを実装し、後のバージョンで拡張するアプローチも十分に妥当です。

### 推奨される修正点
開発計画を明確にするために、Issue 7およびIssue 13の記述に以下のような注記を追加するとよいでしょう：

- 初期実装では標準検出モデルと日本語認識モデルのみをサポート
- 複雑な設定UI機能は将来のバージョンで段階的に追加
- フェーズ分けされた実装計画を明示（例：基本機能→高度な設定→代替モデル対応）

このように開発の優先順位を明確にすることで、プロジェクトの初期段階での実装複雑性を低減し、より効率的な開発が可能になります。

## タスク分解
- [ ] モデル情報管理の設計
  - [ ] モデル情報クラスの設計
  - [ ] モデルリポジトリ定義の設計
  - [ ] モデル依存関係の管理機能
- [ ] モデルダウンロード機能の実装
  - [ ] ダウンロードマネージャの設計
  - [ ] 進捗通知機能の実装
  - [ ] 再開可能なダウンロード機能の検討
- [ ] モデルキャッシュシステムの実装
  - [ ] モデルファイル保存構造の設計
  - [ ] キャッシュ検証機能の実装
  - [ ] 不要モデルの削除機能
- [ ] モデル更新確認機能の実装
  - [ ] モデルバージョン管理
  - [ ] 更新確認APIの設計
  - [ ] 定期的な更新確認の実装
- [ ] ユーザーインターフェース連携
  - [ ] ダウンロード進捗UI連携
  - [ ] モデル管理UI連携

## モデル管理システム設計案
```csharp
namespace Baketa.Infrastructure.OCR.Models
{
    /// <summary>
    /// OCRモデルの種類
    /// </summary>
    public enum OcrModelType
    {
        /// <summary>
        /// テキスト検出モデル
        /// </summary>
        Detection,
        
        /// <summary>
        /// テキスト認識モデル
        /// </summary>
        Recognition,
        
        /// <summary>
        /// テキスト方向分類モデル
        /// </summary>
        Classification,
        
        /// <summary>
        /// レイアウト分析モデル
        /// </summary>
        Layout
    }
    
    /// <summary>
    /// OCRモデル情報
    /// </summary>
    public class OcrModelInfo
    {
        /// <summary>
        /// モデルID
        /// </summary>
        public string Id { get; }
        
        /// <summary>
        /// モデル名
        /// </summary>
        public string Name { get; }
        
        /// <summary>
        /// モデル種類
        /// </summary>
        public OcrModelType Type { get; }
        
        /// <summary>
        /// モデルファイル名
        /// </summary>
        public string FileName { get; }
        
        /// <summary>
        /// ダウンロードURL
        /// </summary>
        public string DownloadUrl { get; }
        
        /// <summary>
        /// モデルサイズ（バイト）
        /// </summary>
        public long FileSize { get; }
        
        /// <summary>
        /// モデルバージョン
        /// </summary>
        public string Version { get; }
        
        /// <summary>
        /// モデルハッシュ（検証用）
        /// </summary>
        public string Hash { get; }
        
        /// <summary>
        /// 関連する言語コード
        /// </summary>
        public string? LanguageCode { get; }
        
        /// <summary>
        /// 高速モデルか（軽量版）
        /// </summary>
        public bool IsFast { get; }
        
        /// <summary>
        /// モデル説明
        /// </summary>
        public string Description { get; }
        
        public OcrModelInfo(
            string id,
            string name,
            OcrModelType type,
            string fileName,
            string downloadUrl,
            long fileSize,
            string version,
            string hash,
            string? languageCode = null,
            bool isFast = false,
            string description = "")
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Type = type;
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            DownloadUrl = downloadUrl ?? throw new ArgumentNullException(nameof(downloadUrl));
            FileSize = fileSize;
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Hash = hash ?? throw new ArgumentNullException(nameof(hash));
            LanguageCode = languageCode;
            IsFast = isFast;
            Description = description ?? string.Empty;
        }
    }
    
    /// <summary>
    /// モデルダウンロード状態
    /// </summary>
    public enum ModelDownloadStatus
    {
        /// <summary>
        /// 初期状態
        /// </summary>
        None,
        
        /// <summary>
        /// 待機中
        /// </summary>
        Pending,
        
        /// <summary>
        /// ダウンロード中
        /// </summary>
        Downloading,
        
        /// <summary>
        /// 検証中
        /// </summary>
        Validating,
        
        /// <summary>
        /// インストール中
        /// </summary>
        Installing,
        
        /// <summary>
        /// 完了
        /// </summary>
        Completed,
        
        /// <summary>
        /// エラー
        /// </summary>
        Error
    }
    
    /// <summary>
    /// モデルダウンロード進捗情報
    /// </summary>
    public class ModelDownloadProgress
    {
        /// <summary>
        /// 対象モデル情報
        /// </summary>
        public OcrModelInfo ModelInfo { get; }
        
        /// <summary>
        /// ダウンロード状態
        /// </summary>
        public ModelDownloadStatus Status { get; }
        
        /// <summary>
        /// 進捗率（0.0～1.0）
        /// </summary>
        public double Progress { get; }
        
        /// <summary>
        /// 現在のアクション説明
        /// </summary>
        public string StatusMessage { get; }
        
        /// <summary>
        /// エラー情報（エラー時のみ）
        /// </summary>
        public string? ErrorMessage { get; }
        
        public ModelDownloadProgress(
            OcrModelInfo modelInfo,
            ModelDownloadStatus status,
            double progress,
            string statusMessage,
            string? errorMessage = null)
        {
            ModelInfo = modelInfo ?? throw new ArgumentNullException(nameof(modelInfo));
            Status = status;
            Progress = progress;
            StatusMessage = statusMessage ?? string.Empty;
            ErrorMessage = errorMessage;
        }
    }
    
    /// <summary>
    /// OCRモデル管理インターフェース
    /// </summary>
    public interface IOcrModelManager
    {
        /// <summary>
        /// 利用可能なすべてのモデル情報を取得
        /// </summary>
        /// <returns>モデル情報のリスト</returns>
        Task<IReadOnlyList<OcrModelInfo>> GetAvailableModelsAsync();
        
        /// <summary>
        /// 言語コードに対応するモデルを取得
        /// </summary>
        /// <param name="languageCode">言語コード</param>
        /// <returns>モデル情報のリスト</returns>
        Task<IReadOnlyList<OcrModelInfo>> GetModelsForLanguageAsync(string languageCode);
        
        /// <summary>
        /// モデルが既にダウンロード済みかを確認
        /// </summary>
        /// <param name="modelInfo">モデル情報</param>
        /// <returns>ダウンロード済みの場合はtrue</returns>
        Task<bool> IsModelDownloadedAsync(OcrModelInfo modelInfo);
        
        /// <summary>
        /// モデルを非同期でダウンロード
        /// </summary>
        /// <param name="modelInfo">モデル情報</param>
        /// <param name="progressCallback">進捗通知コールバック</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>ダウンロードが成功した場合はtrue</returns>
        Task<bool> DownloadModelAsync(
            OcrModelInfo modelInfo,
            IProgress<ModelDownloadProgress>? progressCallback = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 複数のモデルを一括ダウンロード
        /// </summary>
        /// <param name="modelInfos">モデル情報のリスト</param>
        /// <param name="progressCallback">進捗通知コールバック</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>すべてのダウンロードが成功した場合はtrue</returns>
        Task<bool> DownloadModelsAsync(
            IEnumerable<OcrModelInfo> modelInfos,
            IProgress<ModelDownloadProgress>? progressCallback = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// モデルの更新確認
        /// </summary>
        /// <param name="modelInfo">モデル情報</param>
        /// <returns>更新がある場合は新しいモデル情報、ない場合はnull</returns>
        Task<OcrModelInfo?> CheckForUpdateAsync(OcrModelInfo modelInfo);
        
        /// <summary>
        /// ダウンロード済みモデルのリストを取得
        /// </summary>
        /// <returns>ダウンロード済みモデル情報のリスト</returns>
        Task<IReadOnlyList<OcrModelInfo>> GetDownloadedModelsAsync();
        
        /// <summary>
        /// モデルを削除
        /// </summary>
        /// <param name="modelInfo">モデル情報</param>
        /// <returns>削除が成功した場合はtrue</returns>
        Task<bool> DeleteModelAsync(OcrModelInfo modelInfo);
        
        /// <summary>
        /// モデルリポジトリの更新情報を取得
        /// </summary>
        /// <returns>更新が成功した場合はtrue</returns>
        Task<bool> UpdateModelRepositoryAsync();
    }
}
```

## モデル管理システム実装例
```csharp
namespace Baketa.Infrastructure.OCR.Models
{
    /// <summary>
    /// OCRモデル管理の実装
    /// </summary>
    public class OcrModelManager : IOcrModelManager
    {
        private readonly string _modelsDirectory;
        private readonly string _tempDirectory;
        private readonly HttpClient _httpClient;
        private readonly ILogger<OcrModelManager>? _logger;
        
        // 設定ファイルからロードするモデルリポジトリ情報
        private string _modelRepositoryUrl = "https://example.com/paddleocr/models/repository.json";
        private List<OcrModelInfo> _availableModels = new();
        private readonly object _syncLock = new();
        
        public OcrModelManager(
            string modelsDirectory,
            string tempDirectory,
            HttpClient httpClient,
            ILogger<OcrModelManager>? logger = null)
        {
            _modelsDirectory = modelsDirectory ?? throw new ArgumentNullException(nameof(modelsDirectory));
            _tempDirectory = tempDirectory ?? throw new ArgumentNullException(nameof(tempDirectory));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
            
            // ディレクトリの存在確認
            Directory.CreateDirectory(_modelsDirectory);
            Directory.CreateDirectory(_tempDirectory);
        }
        
        public async Task<IReadOnlyList<OcrModelInfo>> GetAvailableModelsAsync()
        {
            // 初回呼び出し時にリポジトリを読み込む
            if (_availableModels.Count == 0)
            {
                await UpdateModelRepositoryAsync();
            }
            
            return _availableModels.AsReadOnly();
        }
        
        public async Task<IReadOnlyList<OcrModelInfo>> GetModelsForLanguageAsync(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
                throw new ArgumentException("言語コードを指定してください", nameof(languageCode));
                
            // 利用可能なモデルを取得
            var allModels = await GetAvailableModelsAsync();
            
            // 指定された言語に対応するモデルをフィルタリング
            return allModels
                .Where(m => m.LanguageCode == languageCode || m.LanguageCode == null)
                .ToList();
        }
        
        public async Task<bool> IsModelDownloadedAsync(OcrModelInfo modelInfo)
        {
            if (modelInfo == null)
                throw new ArgumentNullException(nameof(modelInfo));
                
            var filePath = GetModelFilePath(modelInfo);
            
            if (!File.Exists(filePath))
                return false;
                
            // ファイルサイズ確認
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length != modelInfo.FileSize)
                return false;
                
            // ハッシュ検証（オプション、パフォーマンスへの影響を考慮して選択的に実施）
            if (!string.IsNullOrEmpty(modelInfo.Hash))
            {
                var hash = await CalculateFileHashAsync(filePath);
                return string.Equals(hash, modelInfo.Hash, StringComparison.OrdinalIgnoreCase);
            }
            
            return true;
        }
        
        public async Task<bool> DownloadModelAsync(
            OcrModelInfo modelInfo,
            IProgress<ModelDownloadProgress>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (modelInfo == null)
                throw new ArgumentNullException(nameof(modelInfo));
                
            // 既にダウンロード済みの場合はスキップ
            if (await IsModelDownloadedAsync(modelInfo))
            {
                _logger?.LogInformation("モデル {ModelName} は既にダウンロード済みです", modelInfo.Name);
                progressCallback?.Report(new ModelDownloadProgress(
                    modelInfo, ModelDownloadStatus.Completed, 1.0, "モデルは既にダウンロード済みです"));
                return true;
            }
            
            var filePath = GetModelFilePath(modelInfo);
            var tempFilePath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString() + ".tmp");
            
            progressCallback?.Report(new ModelDownloadProgress(
                modelInfo, ModelDownloadStatus.Pending, 0, "ダウンロードを準備中..."));
                
            try
            {
                _logger?.LogInformation("モデル {ModelName} のダウンロードを開始: {Url}", 
                    modelInfo.Name, modelInfo.DownloadUrl);
                
                // ダウンロード開始
                using var response = await _httpClient.GetAsync(
                    modelInfo.DownloadUrl, 
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                
                response.EnsureSuccessStatusCode();
                
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                
                // ダウンロードの進捗報告用
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                
                var buffer = new byte[8192];
                var bytesRead = 0;
                var totalBytesRead = 0L;
                
                progressCallback?.Report(new ModelDownloadProgress(
                    modelInfo, ModelDownloadStatus.Downloading, 0, "ダウンロード中..."));
                
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    
                    totalBytesRead += bytesRead;
                    
                    if (totalBytes > 0)
                    {
                        var progress = (double)totalBytesRead / totalBytes;
                        progressCallback?.Report(new ModelDownloadProgress(
                            modelInfo, 
                            ModelDownloadStatus.Downloading, 
                            progress, 
                            $"ダウンロード中... ({FormatBytes(totalBytesRead)}/{FormatBytes(totalBytes)})"));
                    }
                }
                
                await fileStream.FlushAsync(cancellationToken);
                
                // ハッシュ検証
                progressCallback?.Report(new ModelDownloadProgress(
                    modelInfo, ModelDownloadStatus.Validating, 0.95, "ファイルを検証中..."));
                
                if (!string.IsNullOrEmpty(modelInfo.Hash))
                {
                    var hash = await CalculateFileHashAsync(tempFilePath);
                    if (!string.Equals(hash, modelInfo.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"モデルファイルのハッシュが一致しません: {hash} != {modelInfo.Hash}");
                    }
                }
                
                // インストール（ファイル移動）
                progressCallback?.Report(new ModelDownloadProgress(
                    modelInfo, ModelDownloadStatus.Installing, 0.98, "モデルをインストール中..."));
                
                // 最終ディレクトリの準備
                var modelDirectory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(modelDirectory) && !Directory.Exists(modelDirectory))
                {
                    Directory.CreateDirectory(modelDirectory);
                }
                
                // 既存ファイルがあれば削除
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                
                // 一時ファイルを最終的な場所に移動
                File.Move(tempFilePath, filePath);
                
                progressCallback?.Report(new ModelDownloadProgress(
                    modelInfo, ModelDownloadStatus.Completed, 1.0, "モデルのダウンロードが完了しました"));
                
                _logger?.LogInformation("モデル {ModelName} のダウンロードが完了しました: {FilePath}",
                    modelInfo.Name, filePath);
                
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("モデル {ModelName} のダウンロードがキャンセルされました", modelInfo.Name);
                progressCallback?.Report(new ModelDownloadProgress(
                    modelInfo, ModelDownloadStatus.Error, 0, "ダウンロードがキャンセルされました"));
                
                // 一時ファイルの削除
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
                
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "モデル {ModelName} のダウンロード中にエラーが発生しました", modelInfo.Name);
                progressCallback?.Report(new ModelDownloadProgress(
                    modelInfo, ModelDownloadStatus.Error, 0, "ダウンロード中にエラーが発生", ex.Message));
                
                // 一時ファイルの削除
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
                
                return false;
            }
        }
        
        public async Task<bool> DownloadModelsAsync(
            IEnumerable<OcrModelInfo> modelInfos,
            IProgress<ModelDownloadProgress>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (modelInfos == null)
                throw new ArgumentNullException(nameof(modelInfos));
                
            var models = modelInfos.ToList();
            if (models.Count == 0)
                return true;
                
            _logger?.LogInformation("{Count} 個のモデルのダウンロードを開始", models.Count);
            
            var success = true;
            for (int i = 0; i < models.Count; i++)
            {
                var model = models[i];
                
                try
                {
                    var result = await DownloadModelAsync(model, progressCallback, cancellationToken);
                    if (!result)
                    {
                        success = false;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "モデル {ModelName} のダウンロード中にエラーが発生しました", model.Name);
                    success = false;
                }
            }
            
            return success;
        }
        
        public async Task<OcrModelInfo?> CheckForUpdateAsync(OcrModelInfo modelInfo)
        {
            if (modelInfo == null)
                throw new ArgumentNullException(nameof(modelInfo));
                
            // モデルリポジトリから最新情報を取得
            await UpdateModelRepositoryAsync();
            
            // 同じIDのモデルを探す
            var latestModel = _availableModels.FirstOrDefault(m => m.Id == modelInfo.Id);
            if (latestModel == null)
                return null;
                
            // バージョン比較
            if (latestModel.Version != modelInfo.Version)
            {
                return latestModel;
            }
            
            return null;
        }
        
        public async Task<IReadOnlyList<OcrModelInfo>> GetDownloadedModelsAsync()
        {
            // 利用可能なモデル情報を取得
            var allModels = await GetAvailableModelsAsync();
            
            // ダウンロード済みのモデルをフィルタリング
            var downloadedModels = new List<OcrModelInfo>();
            
            foreach (var model in allModels)
            {
                if (await IsModelDownloadedAsync(model))
                {
                    downloadedModels.Add(model);
                }
            }
            
            return downloadedModels;
        }
        
        public async Task<bool> DeleteModelAsync(OcrModelInfo modelInfo)
        {
            if (modelInfo == null)
                throw new ArgumentNullException(nameof(modelInfo));
                
            var filePath = GetModelFilePath(modelInfo);
            
            if (!File.Exists(filePath))
                return true; // 既に存在しない場合は成功とみなす
                
            try
            {
                File.Delete(filePath);
                
                _logger?.LogInformation("モデル {ModelName} を削除しました: {FilePath}",
                    modelInfo.Name, filePath);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "モデル {ModelName} の削除中にエラーが発生しました", modelInfo.Name);
                return false;
            }
        }
        
        public async Task<bool> UpdateModelRepositoryAsync()
        {
            try
            {
                _logger?.LogInformation("モデルリポジトリの更新を開始: {Url}", _modelRepositoryUrl);
                
                var json = await _httpClient.GetStringAsync(_modelRepositoryUrl);
                
                lock (_syncLock)
                {
                    // モデルリポジトリJSONの解析（実際の実装はフォーマットに依存）
                    // 例: _availableModels = JsonSerializer.Deserialize<List<OcrModelInfo>>(json);
                    
                    // ここではダミーデータを使用
                    _availableModels = GetDummyModelRepository();
                }
                
                _logger?.LogInformation("{Count} 個のモデル情報を読み込みました", _availableModels.Count);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "モデルリポジトリの更新中にエラーが発生しました");
                
                // 初期データが無い場合はダミーデータを使用
                if (_availableModels.Count == 0)
                {
                    lock (_syncLock)
                    {
                        _availableModels = GetDummyModelRepository();
                    }
                }
                
                return false;
            }
        }
        
        private string GetModelFilePath(OcrModelInfo modelInfo)
        {
            // モデルのカテゴリディレクトリを作成
            string categoryDir = modelInfo.Type.ToString().ToLowerInvariant();
            
            // 言語固有のモデルの場合は言語サブディレクトリを追加
            if (!string.IsNullOrEmpty(modelInfo.LanguageCode))
            {
                return Path.Combine(_modelsDirectory, categoryDir, modelInfo.LanguageCode, modelInfo.FileName);
            }
            else
            {
                return Path.Combine(_modelsDirectory, categoryDir, modelInfo.FileName);
            }
        }
        
        private async Task<string> CalculateFileHashAsync(string filePath)
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(fileStream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        
        private string FormatBytes(long bytes)
        {
            string[] suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblBytes = bytes;
            
            for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblBytes = bytes / 1024.0;
            }
            
            return $"{dblBytes:0.##} {suffix[i]}";
        }
        
        // テスト用ダミーデータ（実際の実装では外部リポジトリから取得）
        private List<OcrModelInfo> GetDummyModelRepository()
        {
            return new List<OcrModelInfo>
            {
                // 検出モデル
                new OcrModelInfo(
                    "det_db_standard",
                    "DB Text Detection (Standard)",
                    OcrModelType.Detection,
                    "det_db.onnx",
                    "https://example.com/paddleocr/models/det_db_standard.onnx",
                    10485760, // 10MB
                    "1.0",
                    "1234567890abcdef1234567890abcdef",
                    null,
                    false,
                    "標準的なDBテキスト検出モデル"),
                    
                // モバイル向け軽量検出モデル
                new OcrModelInfo(
                    "det_db_mobile",
                    "DB Text Detection (Mobile)",
                    OcrModelType.Detection,
                    "det_db_mobile.onnx",
                    "https://example.com/paddleocr/models/det_db_mobile.onnx",
                    5242880, // 5MB
                    "1.0",
                    "abcdef1234567890abcdef1234567890",
                    null,
                    true,
                    "モバイル向け軽量DBテキスト検出モデル"),
                    
                // 日本語認識モデル
                new OcrModelInfo(
                    "rec_japan_standard",
                    "Japanese Recognition (Standard)",
                    OcrModelType.Recognition,
                    "rec_japan.onnx",
                    "https://example.com/paddleocr/models/rec_japan_standard.onnx",
                    31457280, // 30MB
                    "1.0",
                    "9876543210fedcba9876543210fedcba",
                    "jpn",
                    false,
                    "日本語テキスト認識モデル"),
                    
                // 英語認識モデル
                new OcrModelInfo(
                    "rec_english_standard",
                    "English Recognition (Standard)",
                    OcrModelType.Recognition,
                    "rec_english.onnx",
                    "https://example.com/paddleocr/models/rec_english_standard.onnx",
                    20971520, // 20MB
                    "1.0",
                    "fedcba9876543210fedcba9876543210",
                    "eng",
                    false,
                    "英語テキスト認識モデル"),
                    
                // 方向分類モデル
                new OcrModelInfo(
                    "cls_standard",
                    "Text Direction Classification",
                    OcrModelType.Classification,
                    "cls_standard.onnx",
                    "https://example.com/paddleocr/models/cls_standard.onnx",
                    2097152, // 2MB
                    "1.0",
                    "abcd1234efgh5678ijkl9012mnop3456",
                    null,
                    false,
                    "テキスト方向分類モデル")
            };
        }
    }
}
```

## 関連Issue/参考
- 親Issue: #7 実装: PaddleOCRの統合
- 依存: #7.1 実装: PaddleOCR統合基盤の構築
- 関連: #7.2 実装: OCRエンジンインターフェースと実装
- 参照: E:\dev\Baketa\docs\3-architecture\ocr-system\ocr-implementation.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (4.4 try-catch ブロックの範囲を最小限に)

## マイルストーン
マイルストーン2: キャプチャとOCR基盤

## ラベル
- `type: feature`
- `priority: high`
- `component: ocr`
