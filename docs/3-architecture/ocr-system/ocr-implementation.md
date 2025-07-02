# OCR実装ガイド

## 1. OCRシステム概要

Baketaプロジェクトの OCRシステムは、ゲーム画面からテキストを抽出し、翻訳システムへ渡すための中核コンポーネントです。このシステムでは、OpenCVを活用した画像処理アプローチを採用し、ユーザーが手動設定せずとも高品質なOCRを実現します。

### 1.1 OCRパイプライン

```
スクリーンキャプチャ → 差分検出 → 前処理 → OCR処理 → 後処理 → 翻訳システムへ
                        ↑                  ↑               ↑
                        |                  |               |
                        +------------------+---------------+
                                           |
                                 [OpenCV最適化システム]
```

このパイプラインは、クリーンアーキテクチャの各レイヤーに分散して実装されます：

- **Baketa.Core**: 基本インターフェースと抽象化
- **Baketa.Infrastructure**: OpenCVとPaddleOCRの実装
- **Baketa.Application**: OCR処理フローの管理
- **Baketa.UI**: ユーザーインターフェース

### 1.2 主要コンポーネント

- **OCRエンジン**: テキスト認識の中核機能（PaddleOCR）
- **差分検出**: 画面変化の検出による処理最適化
- **前処理**: OCR精度向上のための画像処理（OpenCVベース）
- **後処理**: OCR結果のフィルタリングと最適化
- **最適化システム**: OpenCVベースの設定最適化
- **プロファイル管理**: ゲーム別の自動検出と設定適用

## 2. OpenCVベースのアプローチ詳細

### 2.1 OpenCVベース処理（リアルタイム）

常時のリアルタイムOCR処理はOpenCVを使用し、ローカルで実行します：

- **画像前処理パイプライン**: 明るさ、コントラスト、シャープネス調整など
- **テキスト領域検出**: エッジ検出、MSERアルゴリズムなどによるテキスト領域特定
- **差分検出最適化**: 画面変化の検出による処理効率の最適化
- **パラメータ適用**: 最適化された設定パラメータの適用

### 2.2 アプローチのメリット

- **低レイテンシ**: リアルタイム処理に適した実装
- **リソース効率**: 最適化された処理による低リソース消費
- **高い適応性**: 様々なゲーム画面に適応可能
- **Windows最適化**: Windows環境に特化した実装

## 3. コンポーネント実装

### 3.1 OpenCV画像処理エンジン

OpenCVを使用したローカル画像処理エンジンの実装例：

```csharp
public class OpenCvImageProcessor : IImageProcessor
{
    private readonly ILogger<OpenCvImageProcessor> _logger;
    
    public OpenCvImageProcessor(ILogger<OpenCvImageProcessor> logger)
    {
        _logger = logger;
    }
    
    public async Task<Bitmap> ProcessImageAsync(Bitmap image, ImageProcessingParameters parameters)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
            
        parameters ??= new ImageProcessingParameters();
        
        try
        {
            return await Task.Run(() =>
            {
                using var srcMat = image.ToMat();
                using var destMat = new Mat();
                
                // 前処理パイプラインの実行
                ApplyPreprocessingPipeline(srcMat, destMat, parameters);
                
                return destMat.ToBitmap();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "画像処理中にエラーが発生しました");
            return image; // エラー時は元の画像を返す
        }
    }
    
    private void ApplyPreprocessingPipeline(Mat src, Mat dest, ImageProcessingParameters parameters)
    {
        // ソース画像をコピー
        src.CopyTo(dest);
        
        // グレースケール変換（必要な場合）
        if (parameters.ConvertToGrayscale)
        {
            Cv2.CvtColor(dest, dest, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(dest, dest, ColorConversionCodes.GRAY2BGR);
        }
        
        // 明るさ調整
        if (Math.Abs(parameters.Brightness) > 0.001f)
        {
            dest.ConvertTo(dest, -1, 1.0, parameters.Brightness);
        }
        
        // コントラスト調整
        if (Math.Abs(parameters.Contrast - 1.0f) > 0.001f)
        {
            dest.ConvertTo(dest, -1, parameters.Contrast, 0);
        }
        
        // シャープネス調整
        if (parameters.Sharpness > 0.001f)
        {
            using var blurred = new Mat();
            Cv2.GaussianBlur(dest, blurred, new Size(0, 0), 3);
            Cv2.AddWeighted(dest, 1.0 + parameters.Sharpness, blurred, -parameters.Sharpness, 0, dest);
        }
        
        // 二値化処理
        if (parameters.EnableBinarization)
        {
            using var grayMat = new Mat();
            Cv2.CvtColor(dest, grayMat, ColorConversionCodes.BGR2GRAY);
            
            if (parameters.UsesAdaptiveThreshold)
            {
                Cv2.AdaptiveThreshold(
                    grayMat, 
                    grayMat, 
                    255, 
                    AdaptiveThresholdType.GaussianC,
                    ThresholdType.Binary, 
                    parameters.AdaptiveBlockSize, 
                    parameters.AdaptiveConstant);
            }
            else
            {
                Cv2.Threshold(
                    grayMat, 
                    grayMat, 
                    parameters.BinarizationThreshold, 
                    255, 
                    ThresholdType.Binary);
            }
            
            Cv2.CvtColor(grayMat, dest, ColorConversionCodes.GRAY2BGR);
        }
    }
    
    public List<Rectangle> DetectTextRegions(Bitmap image, TextDetectionParameters parameters)
    {
        using var mat = image.ToMat();
        
        // MSERアルゴリズムによるテキスト候補領域検出
        using var grayMat = new Mat();
        Cv2.CvtColor(mat, grayMat, ColorConversionCodes.BGR2GRAY);
        
        var regions = new List<Rectangle>();
        
        // ... テキスト領域検出の実装 ...
        
        return regions;
    }
}
```

### 3.2 自動最適化システム

フィードバックに基づいて設定を最適化するシステムの実装例：

```csharp
public class OcrOptimizer : IOcrOptimizer
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<OcrOptimizer> _logger;
    
    public OcrOptimizer(
        ISettingsService settingsService,
        ILogger<OcrOptimizer> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }
    
    public async Task<OcrSettings> OptimizeSettingsAsync(
        GameProfile profile,
        OcrResult ocrResult,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = profile.OcrSettings.Clone();
            
            // 結果に基づく設定の最適化
            if (ocrResult.TextRegions.Count == 0 || ocrResult.AverageConfidence < 0.5)
            {
                // テキストが検出されないか低信頼度の場合
                settings = ApplyEnhancementSettings(settings);
            }
            else if (ocrResult.AverageConfidence > 0.9)
            {
                // 高信頼度の結果を得た場合
                settings = ApplyPerformanceSettings(settings);
            }
            
            // 設定の検証
            settings = ValidateSettings(settings);
            
            // プロファイルの更新
            profile.OcrSettings = settings;
            profile.LastModified = DateTime.UtcNow;
            await _settingsService.SaveGameProfileAsync(profile);
            
            _logger.LogInformation("OCR設定を最適化しました: {ProfileId}", profile.Id);
            
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR設定の最適化に失敗しました");
            return profile.OcrSettings;
        }
    }
    
    private OcrSettings ApplyEnhancementSettings(OcrSettings settings)
    {
        // 精度向上のための設定調整
        settings.ImageProcessing.Contrast *= 1.1f; // コントラスト強化
        settings.ImageProcessing.Sharpness += 5;   // シャープネス強化
        settings.DetectionConfidence -= 0.05f;     // 検出閾値を下げる
        
        return settings;
    }
    
    private OcrSettings ApplyPerformanceSettings(OcrSettings settings)
    {
        // パフォーマンス向上のための設定調整
        settings.DetectionConfidence += 0.02f;    // 検出閾値を上げる
        settings.UseDifferenceDetection = true;   // 差分検出を有効化
        
        return settings;
    }
    
    private OcrSettings ValidateSettings(OcrSettings settings)
    {
        // 値の範囲チェック
        settings.ImageProcessing.Contrast = 
            Math.Clamp(settings.ImageProcessing.Contrast, 0.5f, 2.0f);
            
        settings.ImageProcessing.Sharpness = 
            Math.Clamp(settings.ImageProcessing.Sharpness, 0, 100);
            
        settings.DetectionConfidence = 
            Math.Clamp(settings.DetectionConfidence, 0.1f, 0.95f);
        
        return settings;
    }
}
```

### 3.3 ゲームプロファイル検出システム

ゲームを自動検出し適切なプロファイルを適用するシステムの実装例：

```csharp
public class GameProfileManager : IGameProfileManager
{
    private readonly IGameDetector _gameDetector;
    private readonly IOcrOptimizer _ocrOptimizer;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<GameProfileManager> _logger;
    
    public GameProfileManager(
        IGameDetector gameDetector,
        IOcrOptimizer ocrOptimizer,
        ISettingsService settingsService,
        ILogger<GameProfileManager> logger)
    {
        _gameDetector = gameDetector;
        _ocrOptimizer = ocrOptimizer;
        _settingsService = settingsService;
        _logger = logger;
    }
    
    public async Task<GameProfile> DetectAndApplyProfileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 現在実行中のゲームを検出
            var detectedGame = await _gameDetector.DetectCurrentGameAsync(cancellationToken);
            
            if (detectedGame != null)
            {
                _logger.LogInformation("ゲームを検出しました: {GameName}", detectedGame.Name);
                
                // 既存プロファイルを検索
                var existingProfile = await _settingsService.GetGameProfileAsync(detectedGame.Id);
                
                if (existingProfile != null)
                {
                    // 既存プロファイルを適用
                    _logger.LogInformation("既存プロファイルを適用します: {ProfileId}", existingProfile.Id);
                    await ApplyProfileAsync(existingProfile, cancellationToken);
                    return existingProfile;
                }
                else
                {
                    // 新規ゲーム検出時は初期プロファイルを作成
                    _logger.LogInformation("新規ゲーム検出: 初期プロファイルを作成します");
                    return await CreateInitialProfileAsync(detectedGame, cancellationToken);
                }
            }
            else
            {
                _logger.LogInformation("ゲームが検出されませんでした: デフォルトプロファイルを使用します");
                return await _settingsService.GetDefaultProfileAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ゲームプロファイル検出中にエラーが発生しました");
            return await _settingsService.GetDefaultProfileAsync();
        }
    }
    
    private async Task<GameProfile> CreateInitialProfileAsync(
        DetectedGame game,
        CancellationToken cancellationToken = default)
    {
        // 画面キャプチャ
        var screenshot = await ScreenCapture.CaptureScreenAsync(cancellationToken);
        
        // 画面の基本分析
        var characteristics = AnalyzeGameScreen(screenshot);
            
        // 初期OCR設定を生成
        var ocrSettings = GenerateInitialSettings(characteristics);
            
        // プロファイル作成
        var profile = new GameProfile
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            GameName = game.Name,
            Created = DateTime.UtcNow,
            LastModified = DateTime.UtcNow,
            OcrSettings = ocrSettings,
            Characteristics = characteristics
        };
        
        // プロファイルの保存
        await _settingsService.SaveGameProfileAsync(profile);
        
        // プロファイルの適用
        await ApplyProfileAsync(profile, cancellationToken);
        
        return profile;
    }
    
    private GameScreenCharacteristics AnalyzeGameScreen(Bitmap screenshot)
    {
        // OpenCVを使用した画面分析
        // 輝度、コントラスト、テキスト分布などを解析
        // ...
        
        return new GameScreenCharacteristics
        {
            // 分析結果を設定
        };
    }
    
    private OcrSettings GenerateInitialSettings(GameScreenCharacteristics characteristics)
    {
        // 画面特性に基づいた初期設定を生成
        var settings = new OcrSettings();
        
        // 画面の明るさに基づいたコントラスト設定
        if (characteristics.AverageBrightness < 100)
        {
            settings.ImageProcessing.Brightness = 20;
            settings.ImageProcessing.Contrast = 1.3f;
        }
        else if (characteristics.AverageBrightness > 200)
        {
            settings.ImageProcessing.Brightness = -10;
            settings.ImageProcessing.Contrast = 0.9f;
        }
        
        // その他の設定調整
        // ...
        
        return settings;
    }
    
    private async Task ApplyProfileAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        // OcrServiceに設定を適用
        // ...
        
        await Task.CompletedTask;
    }
}
```

## 4. OCRシステムの設計課題

### 4.1 優先度の高い課題

1. **OpenCV画像処理エンジンの実装**
   - 画像前処理パイプライン構築
   - テキスト領域検出アルゴリズム
   - 差分検出システムの高度化

2. **自動最適化システムの実装**
   - フィードバックベースの最適化アルゴリズム
   - 継続的な設定調整機能
   - パフォーマンスと精度のバランス調整

3. **プロファイルベース管理システムの構築**
   - ゲーム検出機能
   - プロファイル永続化
   - 設定適用メカニズム

### 4.2 適用する設計パターン

- **ストラテジーパターン**: 異なる画像処理アルゴリズムの切り替え
- **ファクトリーパターン**: プロファイルに基づく最適な実装の生成
- **オブザーバーパターン**: OCR状態の変更通知
- **テンプレートメソッド**: 処理パイプラインの定義
- **コマンドパターン**: 画像処理操作のカプセル化

## 5. 開発者向けガイド

### 5.1 OCRシステムの使い方

OCRシステムの基本的な使用方法：

```csharp
// サービスの取得
var ocrService = serviceProvider.GetRequiredService<IOcrService>();
var gameProfileManager = serviceProvider.GetRequiredService<IGameProfileManager>();

// ゲーム検出と自動最適化
var gameProfile = await gameProfileManager.DetectAndApplyProfileAsync();
Console.WriteLine($"検出されたゲーム: {gameProfile.GameName}");

// OCR処理の実行
var screenshot = await ScreenCapture.CaptureScreenAsync();
var ocrResult = await ocrService.RecognizeTextAsync(screenshot);

// 結果の処理
foreach (var textRegion in ocrResult.TextRegions)
{
    Console.WriteLine($"テキスト: {textRegion.Text}, 信頼度: {textRegion.Confidence}");
}

// 問題があった場合の診断と改善
if (ocrResult.TextRegions.Count == 0 || ocrResult.AverageConfidence < 0.5)
{
    var optimizer = serviceProvider.GetRequiredService<IOcrOptimizer>();
    var optimizedSettings = await optimizer.OptimizeSettingsAsync(
        gameProfile, 
        ocrResult);
        
    Console.WriteLine("設定を最適化しました");
}
```

### 5.2 カスタム前処理の追加方法

アプリケーションに新しい画像前処理ステップを追加する方法：

```csharp
public class CustomImageProcessor : IImageProcessor
{
    private readonly OpenCvImageProcessor _baseProcessor;
    
    public CustomImageProcessor(OpenCvImageProcessor baseProcessor)
    {
        _baseProcessor = baseProcessor;
    }
    
    public async Task<Bitmap> ProcessImageAsync(Bitmap image, ImageProcessingParameters parameters)
    {
        // 基本処理を実行
        var processedImage = await _baseProcessor.ProcessImageAsync(image, parameters);
        
        // カスタム処理を追加
        if (parameters.EnableCustomProcessing)
        {
            using var mat = processedImage.ToMat();
            
            // カスタム処理を実装
            ApplyCustomFilter(mat, parameters.CustomFilterStrength);
            
            // Bitmapに戻す
            return mat.ToBitmap();
        }
        
        return processedImage;
    }
    
    private void ApplyCustomFilter(Mat image, float strength)
    {
        // カスタムフィルタの実装
        // ...
    }
}
```

## 6. パフォーマンス最適化

OCRシステムのパフォーマンスを最適化するポイント：

### 6.1 OpenCVベース処理の最適化

- マルチスレッド処理の活用
- GPU加速の検討（適用可能な場合）
- 差分検出による処理スキップ
- メモリ使用の最適化

### 6.2 差分検出の高度化

- サンプリングによる高速比較
- 構造的特徴の分析
- テキスト領域の重点分析
- 変化量に応じた処理制御

### 6.3 プロファイル管理の効率化

- プロファイルデータの効率的な保存と読み込み
- 頻繁に使用するプロファイルのメモリ内キャッシュ
- 定期的なプロファイル最適化

## 7. クリーンアーキテクチャとの整合性

このOCR実装は、Baketaプロジェクトのクリーンアーキテクチャと完全に整合します：

### 7.1 レイヤー構成

- **Baketa.Core.Abstractions.OCR**: 基本的なOCR抽象化とインターフェース
- **Baketa.Infrastructure.OCR**: OpenCV実装とPaddleOCR統合
- **Baketa.Application.Services.OCR**: OCRサービスとプロファイル管理
- **Baketa.UI.Avalonia**: 設定画面とユーザーインターフェース

### 7.2 インターフェース設計と実装

```csharp
// Core層のインターフェース
namespace Baketa.Core.Abstractions.OCR
{
    /// <summary>
    /// OCRサービスの基本インターフェース
    /// </summary>
    public interface IOcrService
    {
        Task<OcrResult> RecognizeTextAsync(IImage image, CancellationToken cancellationToken = default);
    }
    
    /// <summary>
    /// OCR設定インターフェース
    /// </summary>
    public interface IOcrSettings
    {
        // OCR設定プロパティ
        ImageProcessingParameters ImageProcessing { get; }
        bool UseDifferenceDetection { get; set; }
        float DetectionConfidence { get; set; }
    }
}

// Infrastructure層の実装
namespace Baketa.Infrastructure.OCR
{
    /// <summary>
    /// PaddleOCRベースのOCRサービス実装
    /// </summary>
    public class PaddleOcrService : IOcrService
    {
        private readonly IImageProcessor _imageProcessor;
        private readonly IOcrProcessor _ocrProcessor;
        private readonly IOcrCache _ocrCache;

        public PaddleOcrService(
            IImageProcessor imageProcessor,
            IOcrProcessor ocrProcessor,
            IOcrCache ocrCache)
        {
            _imageProcessor = imageProcessor;
            _ocrProcessor = ocrProcessor;
            _ocrCache = ocrCache;
        }

        public async Task<OcrResult> RecognizeTextAsync(IImage image, CancellationToken cancellationToken = default)
        {
            // PaddleOCRとOpenCVを使用した実装
        }
    }

    /// <summary>
    /// OpenCVベースの画像処理実装
    /// </summary>
    public class OpenCvImageProcessor : IImageProcessor
    {
        // OpenCVを使用した画像処理の実装
    }
}

// Application層のサービス
namespace Baketa.Application.Services.OCR
{
    /// <summary>
    /// OCRアプリケーションサービス
    /// </summary>
    public class OcrApplicationService
    {
        private readonly IOcrService _ocrService;
        private readonly IGameProfileManager _profileManager;
        private readonly IOcrOptimizer _ocrOptimizer;

        public OcrApplicationService(
            IOcrService ocrService,
            IGameProfileManager profileManager,
            IOcrOptimizer ocrOptimizer)
        {
            _ocrService = ocrService;
            _profileManager = profileManager;
            _ocrOptimizer = ocrOptimizer;
        }

        // アプリケーションレベルのサービスメソッド
    }
}

// UI層のビューモデル
namespace Baketa.UI.Avalonia.ViewModels
{
    /// <summary>
    /// OCR設定画面のビューモデル
    /// </summary>
    public class OcrSettingsViewModel : ViewModelBase
    {
        // OCR設定画面のビューモデル実装
    }
}
```

### 7.3 依存性注入の設定

```csharp
namespace Baketa.Application.DI.Modules
{
    /// <summary>
    /// OCR関連のサービス登録モジュール
    /// </summary>
    public class OcrModule : IServiceModule
    {
        public void RegisterServices(IServiceCollection services)
        {
            // Core層のサービス登録
            
            // Infrastructure層のサービス登録
            services.AddSingleton<IImageProcessor, OpenCvImageProcessor>();
            services.AddSingleton<IOcrProcessor, PaddleOcrProcessor>();
            services.AddSingleton<IOcrService, PaddleOcrService>();
            
            // Application層のサービス登録
            services.AddSingleton<IOcrOptimizer, OcrOptimizer>();
            services.AddSingleton<IGameProfileManager, GameProfileManager>();
            services.AddSingleton<OcrApplicationService>();
        }
    }
}
```

## 8. 参考資料

- [PaddleOCR GitHub](https://github.com/PaddlePaddle/PaddleOCR)
- [PaddleOCRSharp NuGet](https://www.nuget.org/packages/PaddleOCRSharp/)
- [OpenCV Documentation](https://docs.opencv.org/)
- [OCR精度向上テクニック集](https://github.com/tesseract-ocr/tesseract/wiki/ImproveQuality)

## 9. 関連ドキュメント

- [OCR前処理システム設計ドキュメント](./preprocessing/index.md)
- [OCR OpenCV最適化アプローチ](./ocr-opencv-approach.md)
- [キャプチャシステム実装ガイド](../capture-system/capture-implementation.md)
- [プラットフォーム抽象化レイヤー](../platform/platform-abstraction.md)