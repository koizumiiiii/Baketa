# 実装: OCRエンジンインターフェースと実装

## 概要
OCRエンジンの抽象化インターフェースと、PaddleOCRを使用した具体的な実装を作成します。

## 目的・理由
OCRエンジンのインターフェースを適切に設計し、PaddleOCRの具体的な実装を行うことで、将来的に異なるOCRエンジンへの置き換えも可能な拡張性の高い構造を実現します。また、ゲームテキスト認識に最適化されたOCR機能を提供します。

## 詳細
- `IOcrEngine`インターフェースの設計と実装
- `PaddleOcrEngine`の実装
- OCR結果モデルの設計と実装
- OCR処理の非同期実行と進捗通知

## タスク分解

### フェーズ1：基本実装（初期リリース）
- [ ] `IOcrEngine`インターフェースの基本設計
  - [ ] 基本的なテキスト認識メソッドの定義
  - [ ] 最小限の設定パラメータの定義
  - [ ] 日本語・英語の言語指定機能
  - [ ] 標準モデル選択機能の定義
- [ ] 基本的なOCR結果モデルの設計
  - [ ] 認識テキスト保持クラスの設計
  - [ ] テキスト位置情報の保持方法
  - [ ] 認識信頼度の保持方法
- [ ] 基本的な`PaddleOcrEngine`の実装
  - [ ] 標準モデルと日本語モデル対応の実装
  - [ ] 基本的なOCR処理フローの実装
  - [ ] 基本的なエラーハンドリング
- [ ] 基本的なOCRパラメータ管理
  - [ ] 基本パラメータの設定クラスの実装
  - [ ] 最適なデフォルト値の設定
  - [ ] 基本的なパラメータ検証
- [ ] 基本動作確認用テストの作成

### フェーズ2：拡張実装（将来のバージョン）
- [ ] インターフェースの拡張
  - [ ] 高度なテキスト認識オプション
  - [ ] 複数言語対応の拡張
  - [ ] 軽量化モデルなどの代替モデル対応
- [ ] `PaddleOcrEngine`の拡張実装
  - [ ] GPUサポートと高度な最適化
  - [ ] 複数モデルの動的切り替え
  - [ ] パフォーマンスチューニング
- [ ] 高度なOCRパラメータ管理の追加
  - [ ] 詳細な設定オプションの追加
  - [ ] 高度なパラメータ検証

**注**: 初期実装ではフェーズ1の基本機能のみを優先し、フェーズ2の機能は将来の拡張として実装します。

## インターフェース設計案
```csharp
namespace Baketa.Core.Abstractions.OCR
{
    /// <summary>
    /// OCR処理の進捗状況を表すクラス
    /// </summary>
    public class OcrProgress
    {
        /// <summary>
        /// 進捗率（0.0～1.0）
        /// </summary>
        public double Progress { get; }
        
        /// <summary>
        /// 現在の処理ステータス
        /// </summary>
        public string Status { get; }
        
        public OcrProgress(double progress, string status)
        {
            Progress = progress;
            Status = status ?? string.Empty;
        }
    }
    
    /// <summary>
    /// OCR結果のテキスト領域情報
    /// </summary>
    public class OcrTextRegion
    {
        /// <summary>
        /// 認識されたテキスト
        /// </summary>
        public string Text { get; }
        
        /// <summary>
        /// テキスト領域の境界矩形
        /// </summary>
        public Rectangle Bounds { get; }
        
        /// <summary>
        /// 認識信頼度（0.0～1.0）
        /// </summary>
        public double Confidence { get; }
        
        /// <summary>
        /// テキスト領域の詳細な輪郭点（オプション）
        /// </summary>
        public Point[]? Contour { get; }
        
        public OcrTextRegion(string text, Rectangle bounds, double confidence, Point[]? contour = null)
        {
            Text = text ?? string.Empty;
            Bounds = bounds;
            Confidence = confidence;
            Contour = contour;
        }
    }
    
    /// <summary>
    /// OCR結果を表すクラス
    /// </summary>
    public class OcrResult
    {
        /// <summary>
        /// 認識されたテキスト領域のリスト
        /// </summary>
        public IReadOnlyList<OcrTextRegion> TextRegions { get; }
        
        /// <summary>
        /// 処理対象の画像
        /// </summary>
        public IImage SourceImage { get; }
        
        /// <summary>
        /// OCR処理時間
        /// </summary>
        public TimeSpan ProcessingTime { get; }
        
        /// <summary>
        /// 画像内のすべてのテキストを結合（改行区切り）
        /// </summary>
        public string Text => string.Join(Environment.NewLine, TextRegions.Select(r => r.Text));
        
        public OcrResult(IReadOnlyList<OcrTextRegion> textRegions, IImage sourceImage, TimeSpan processingTime)
        {
            TextRegions = textRegions ?? throw new ArgumentNullException(nameof(textRegions));
            SourceImage = sourceImage ?? throw new ArgumentNullException(nameof(sourceImage));
            ProcessingTime = processingTime;
        }
    }
    
    /// <summary>
    /// OCRエンジンの設定
    /// </summary>
    public class OcrEngineSettings
    {
        /// <summary>
        /// 認識する言語
        /// </summary>
        public string Language { get; set; } = "jpn"; // デフォルトは日本語
        
        /// <summary>
        /// テキスト検出の信頼度閾値（0.0～1.0）
        /// </summary>
        public double DetectionThreshold { get; set; } = 0.3;
        
        /// <summary>
        /// テキスト認識の信頼度閾値（0.0～1.0）
        /// </summary>
        public double RecognitionThreshold { get; set; } = 0.5;
        
        /// <summary>
        /// 使用するモデル名
        /// </summary>
        public string ModelName { get; set; } = "standard";
        
        /// <summary>
        /// GPU加速を使用するか
        /// </summary>
        public bool UseGpu { get; set; } = false;
        
        /// <summary>
        /// GPUデバイスID
        /// </summary>
        public int GpuDeviceId { get; set; } = 0;
        
        /// <summary>
        /// 最大テキスト検出数
        /// </summary>
        public int MaxDetections { get; set; } = 100;
    }
    
    /// <summary>
    /// OCRエンジンインターフェース
    /// </summary>
    public interface IOcrEngine : IDisposable
    {
        /// <summary>
        /// OCRエンジンの名前
        /// </summary>
        string EngineName { get; }
        
        /// <summary>
        /// OCRエンジンのバージョン
        /// </summary>
        string EngineVersion { get; }
        
        /// <summary>
        /// エンジンが初期化済みかどうか
        /// </summary>
        bool IsInitialized { get; }
        
        /// <summary>
        /// OCRエンジンを初期化します
        /// </summary>
        /// <returns>初期化が成功した場合はtrue</returns>
        Task<bool> InitializeAsync();
        
        /// <summary>
        /// 画像からテキストを認識します
        /// </summary>
        /// <param name="image">画像</param>
        /// <param name="progressCallback">進捗通知コールバック（オプション）</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>OCR結果</returns>
        Task<OcrResult> RecognizeAsync(
            IImage image,
            IProgress<OcrProgress>? progressCallback = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 画像の指定領域からテキストを認識します
        /// </summary>
        /// <param name="image">画像</param>
        /// <param name="region">認識領域</param>
        /// <param name="progressCallback">進捗通知コールバック（オプション）</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>OCR結果</returns>
        Task<OcrResult> RecognizeRegionAsync(
            IImage image,
            Rectangle region,
            IProgress<OcrProgress>? progressCallback = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// OCRエンジンの設定を取得します
        /// </summary>
        /// <returns>現在の設定</returns>
        OcrEngineSettings GetSettings();
        
        /// <summary>
        /// OCRエンジンの設定を適用します
        /// </summary>
        /// <param name="settings">設定</param>
        void ApplySettings(OcrEngineSettings settings);
        
        /// <summary>
        /// 使用可能な言語のリストを取得します
        /// </summary>
        /// <returns>言語コードのリスト</returns>
        IReadOnlyList<string> GetAvailableLanguages();
        
        /// <summary>
        /// 使用可能なモデルのリストを取得します
        /// </summary>
        /// <returns>モデル名のリスト</returns>
        IReadOnlyList<string> GetAvailableModels();
    }
}
```

## 実装例（PaddleOcrEngine）
```csharp
namespace Baketa.Infrastructure.OCR.PaddleOCR
{
    /// <summary>
    /// PaddleOCRエンジンの実装
    /// </summary>
    public class PaddleOcrEngine : IOcrEngine
    {
        private readonly PaddleOcrInitializer _initializer;
        private readonly ILogger<PaddleOcrEngine>? _logger;
        private OcrEngineSettings _settings = new();
        private bool _disposed = false;
        private readonly object _syncLock = new();
        
        // PaddleOCRエンジンのインスタンス（ライブラリに依存）
        private object? _paddleOcrInstance;
        
        public string EngineName => "PaddleOCR";
        public string EngineVersion => "2.6"; // 使用するPaddleOCRのバージョン
        public bool IsInitialized => _paddleOcrInstance != null;
        
        public PaddleOcrEngine(
            PaddleOcrInitializer initializer,
            ILogger<PaddleOcrEngine>? logger = null)
        {
            _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
            _logger = logger;
        }
        
        public async Task<bool> InitializeAsync()
        {
            if (IsInitialized)
                return true;
                
            _logger?.LogInformation("PaddleOCRエンジンの初期化を開始");
            
            try
            {
                // 基盤の初期化
                if (!_initializer.Initialize())
                {
                    _logger?.LogError("PaddleOCR基盤の初期化に失敗");
                    return false;
                }
                
                // モデルの存在確認
                await EnsureModelFilesExistAsync();
                
                // エンジンインスタンスの生成
                // (PaddleOCRラッパーライブラリに依存するため、実際の実装は異なる)
                lock (_syncLock)
                {
                    _paddleOcrInstance = CreatePaddleOcrInstance();
                }
                
                _logger?.LogInformation("PaddleOCRエンジンの初期化が完了");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "PaddleOCRエンジンの初期化に失敗");
                return false;
            }
        }
        
        private object CreatePaddleOcrInstance()
        {
            // 実際のPaddleOCRラッパーライブラリに依存する実装
            // 例えば、PaddleOCR.Netを使用する場合の疑似コード
            
            // var paddleOcr = new PaddleOCR.PaddleOCREngine();
            // paddleOcr.DetectionThreshold = _settings.DetectionThreshold;
            // paddleOcr.RecognitionThreshold = _settings.RecognitionThreshold;
            // paddleOcr.UseGpu = _settings.UseGpu;
            // ...
            
            // ダミー実装（実際の実装ではなく）
            return new object();
        }
        
        private async Task EnsureModelFilesExistAsync()
        {
            // モデルファイルの存在確認と、必要に応じてダウンロード
            var modelsDir = _initializer.GetModelsDirectory();
            var modelFiles = GetRequiredModelFiles(_settings.Language, _settings.ModelName);
            
            foreach (var file in modelFiles)
            {
                var filePath = Path.Combine(modelsDir, file.Name);
                if (!File.Exists(filePath))
                {
                    _logger?.LogInformation("モデルファイル {FileName} をダウンロード中...", file.Name);
                    
                    // ダウンロード処理
                    await DownloadModelFileAsync(file.Url, filePath);
                }
            }
        }
        
        private IEnumerable<(string Name, string Url)> GetRequiredModelFiles(string language, string modelName)
        {
            // 言語とモデル名に基づいて必要なファイルを返す
            // 実際の実装では、設定ファイルやリソースから情報を取得
            
            // 例：検出、向き検出、認識モデルの3種類
            return new[]
            {
                ($"det_{modelName}.onnx", "https://example.com/paddleocr/models/det_model.onnx"),
                ($"cls_{modelName}.onnx", "https://example.com/paddleocr/models/cls_model.onnx"),
                ($"rec_{language}_{modelName}.onnx", $"https://example.com/paddleocr/models/rec_{language}_model.onnx"),
            };
        }
        
        private async Task DownloadModelFileAsync(string url, string filePath)
        {
            // モデルファイルのダウンロード処理
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            
            var tempFile = Path.Combine(_initializer.GetTempDirectory(), Path.GetFileName(filePath) + ".tmp");
            
            using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fileStream);
            }
            
            // ダウンロード完了後、最終的な場所に移動
            if (File.Exists(filePath))
                File.Delete(filePath);
                
            File.Move(tempFile, filePath);
            
            _logger?.LogInformation("モデルファイル {FileName} のダウンロードが完了", Path.GetFileName(filePath));
        }
        
        public async Task<OcrResult> RecognizeAsync(
            IImage image,
            IProgress<OcrProgress>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
                
            if (!IsInitialized)
                throw new InvalidOperationException("OCRエンジンが初期化されていません");
                
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                progressCallback?.Report(new OcrProgress(0.1, "OCR処理を開始"));
                
                // 前処理：画像をPaddleOCRで処理可能な形式に変換
                // （実際の実装はライブラリによって異なる）
                
                // OCR処理の実行
                progressCallback?.Report(new OcrProgress(0.3, "テキスト検出"));
                // テキスト検出処理（実際の実装はライブラリによって異なる）
                
                progressCallback?.Report(new OcrProgress(0.6, "テキスト認識"));
                // テキスト認識処理（実際の実装はライブラリによって異なる）
                
                progressCallback?.Report(new OcrProgress(0.9, "結果処理"));
                
                // ダミー結果（実際の実装では、PaddleOCRの結果を変換）
                var textRegions = new List<OcrTextRegion>
                {
                    new OcrTextRegion("サンプルテキスト", new Rectangle(10, 10, 100, 30), 0.95)
                };
                
                stopwatch.Stop();
                
                // 結果の変換と返却
                var result = new OcrResult(textRegions, image, stopwatch.Elapsed);
                
                progressCallback?.Report(new OcrProgress(1.0, "OCR処理完了"));
                
                _logger?.LogInformation("OCR処理完了: {ElapsedMs}ms, 検出テキスト数: {TextCount}",
                    stopwatch.ElapsedMilliseconds, result.TextRegions.Count);
                
                return result;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                _logger?.LogInformation("OCR処理がキャンセルされました");
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger?.LogError(ex, "OCR処理中にエラーが発生");
                throw new OcrException("OCR処理に失敗しました", ex);
            }
        }
        
        public async Task<OcrResult> RecognizeRegionAsync(
            IImage image,
            Rectangle region,
            IProgress<OcrProgress>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
                
            // 指定領域の抽出
            IImage regionImage = await ExtractRegionAsync(image, region);
            
            try
            {
                // 抽出した領域に対してOCR実行
                var result = await RecognizeAsync(regionImage, progressCallback, cancellationToken);
                
                // 領域の座標をオリジナル画像の座標系に変換
                var adjustedRegions = result.TextRegions.Select(tr => 
                    new OcrTextRegion(
                        tr.Text,
                        new Rectangle(tr.Bounds.X + region.X, tr.Bounds.Y + region.Y, tr.Bounds.Width, tr.Bounds.Height),
                        tr.Confidence,
                        tr.Contour?.Select(p => new Point(p.X + region.X, p.Y + region.Y)).ToArray()
                    )).ToList();
                
                return new OcrResult(adjustedRegions, image, result.ProcessingTime);
            }
            finally
            {
                // 一時画像の破棄
                regionImage.Dispose();
            }
        }
        
        private async Task<IImage> ExtractRegionAsync(IImage image, Rectangle region)
        {
            // 画像から指定領域を抽出
            IAdvancedImage? advancedImage = image as IAdvancedImage;
            
            if (advancedImage != null)
            {
                // IAdvancedImageの機能を使用
                return await advancedImage.ExtractRegionAsync(region);
            }
            else
            {
                // 基本的な抽出処理（実装の詳細は省略）
                throw new NotImplementedException("基本画像からの領域抽出が実装されていません");
            }
        }
        
        public OcrEngineSettings GetSettings()
        {
            return new OcrEngineSettings
            {
                Language = _settings.Language,
                DetectionThreshold = _settings.DetectionThreshold,
                RecognitionThreshold = _settings.RecognitionThreshold,
                ModelName = _settings.ModelName,
                UseGpu = _settings.UseGpu,
                GpuDeviceId = _settings.GpuDeviceId,
                MaxDetections = _settings.MaxDetections
            };
        }
        
        public void ApplySettings(OcrEngineSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            bool requiresReinitialization = _settings.Language != settings.Language ||
                                             _settings.ModelName != settings.ModelName ||
                                             _settings.UseGpu != settings.UseGpu ||
                                             _settings.GpuDeviceId != settings.GpuDeviceId;
                                            
            _settings = new OcrEngineSettings
            {
                Language = settings.Language,
                DetectionThreshold = settings.DetectionThreshold,
                RecognitionThreshold = settings.RecognitionThreshold,
                ModelName = settings.ModelName,
                UseGpu = settings.UseGpu,
                GpuDeviceId = settings.GpuDeviceId,
                MaxDetections = settings.MaxDetections
            };
            
            _logger?.LogInformation("OCRエンジン設定を更新: 言語={Language}, モデル={Model}, GPU={UseGpu}",
                _settings.Language, _settings.ModelName, _settings.UseGpu);
                
            // 重要なパラメータが変更された場合は再初期化が必要
            if (requiresReinitialization && IsInitialized)
            {
                _logger?.LogInformation("設定変更により再初期化が必要");
                
                lock (_syncLock)
                {
                    // 古いインスタンスの破棄
                    DisposeInternalInstance();
                    
                    // 新しいインスタンスの作成
                    _paddleOcrInstance = CreatePaddleOcrInstance();
                }
            }
        }
        
        public IReadOnlyList<string> GetAvailableLanguages()
        {
            // サポートされている言語リストを返す
            // 実際の実装では、設定ファイルやリソースから情報を取得
            // 初期実装では日本語と英語のみをサポート
            return new[] { "eng", "jpn" };
            
            // 将来の拡張では以下のような複数言語をサポート予定
            // return new[] { "eng", "jpn", "chi_sim", "chi_tra", "kor" };
        }
        
        public IReadOnlyList<string> GetAvailableModels()
        {
            // サポートされているモデルリストを返す
            // 実際の実装では、設定ファイルやリソースから情報を取得
            // 初期実装では標準モデルのみをサポート
            return new[] { "standard" };
            
            // 将来の拡張では以下のような複数モデルをサポート予定
            // return new[] { "standard", "lite", "mobile" };
        }
        
        private void DisposeInternalInstance()
        {
            if (_paddleOcrInstance != null)
            {
                // PaddleOCRインスタンスの破棄
                // （実際の実装はライブラリによって異なる）
                
                _paddleOcrInstance = null;
            }
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            DisposeInternalInstance();
            
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
    
    /// <summary>
    /// OCR例外
    /// </summary>
    public class OcrException : Exception
    {
        public OcrException(string message) : base(message)
        {
        }
        
        public OcrException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
```

## 実装アプローチ

### フェーズ分けの言及
初期実装では標準検出モデルと日本語認識モデルのみをサポートし、GPUサポートや軽量化モデル、その他言語モデルなどの高度な機能は将来の拡張として実装します。これにより、初期実装の完了までの時間を短縮し、安定的なコア機能を早期に実装することが可能になります。

### インターフェース設計のアプローチ
インターフェース設計では、将来の拡張を見据えた柔軟性を維持しつつ、初期実装では最小限の実装に集中します。拡張性を持つ基本インターフェースを設計し、初期段階では最小限の実装を行います。

## 関連Issue/参考
- 親イシュー: #7 実装: PaddleOCRの統合
- 依存: #7.1 実装: PaddleOCR統合基盤の構築
- 関連: #5.1 実装: IAdvancedImageインターフェースの設計と実装
- 参照: E:\dev\Baketa\docs\3-architecture\ocr-system\ocr-implementation.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3. 非同期プログラミング)

## マイルストーン
マイルストーン2: キャプチャとOCR基盤

## ラベル
- `type: feature`
- `priority: high`
- `component: ocr`