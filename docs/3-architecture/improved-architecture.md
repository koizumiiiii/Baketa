# Baketaプロジェクト - 改善されたアーキテクチャ設計

## 1. 概要

本ドキュメントは、Baketaプロジェクトの名前空間衝突や構造上の問題を解決するための改善されたアーキテクチャ設計について説明します。

**重要事項**: BaketaプロジェクトはWindows専用アプリケーションとして開発を継続し、Linux/macOSへのクロスプラットフォーム対応は行いません。また、OCR最適化にはOpenCVのみを使用します。

## 2. 現状の問題点

現在のBaketaプロジェクトには以下の問題が存在します：

1. **インターフェース定義の重複**：
   - 同名のインターフェース（例：`IScreenCaptureService`、`IBaketaImage`）が複数の名前空間に存在
   - 不明確な責任分担によるコードの重複

2. **アーキテクチャの課題**：
   - 名前空間の境界と責任分担が不明確
   - 依存関係が複雑で追跡困難
   - プラットフォーム依存コードと非依存コードの分離が不十分
   - モジュール間の結合度が高く、テスト容易性が低い

3. **依存性注入の問題**：
   - サービス登録コードの散在と重複
   - 一貫性のないサービスライフタイム管理

## 3. 改善されたアーキテクチャ構成

### 3.1 全体構造

Baketaプロジェクトを以下の4つの主要レイヤーに分割します：

```
Baketa/
├── src/
│   ├── Baketa.Core/                 # プラットフォーム非依存のコア機能
│   ├── Baketa.Infrastructure/       # インフラストラクチャ層
│   ├── Baketa.Application/          # アプリケーションサービス
│   └── Baketa.UI/                   # UI層
│
├── tests/                        # テスト
└── tools/                        # ツール、スクリプト等
```

### 3.2 レイヤー構成の詳細

#### 3.2.1 Baketa.Core (コア層)

プラットフォーム非依存の基本機能と抽象化を提供します：

```
Baketa.Core/
├── Abstractions/         # 基本抽象化（旧Interfaces）
│   ├── Imaging/          # 画像処理抽象化
│   ├── Capture/          # キャプチャ抽象化
│   ├── Translation/      # 翻訳抽象化
│   └── Common/           # 共通抽象化
├── Models/               # データモデル
├── Translation/          # 翻訳関連機能
│   ├── Abstractions/     # 翻訳インターフェース定義
│   ├── Models/           # 翻訳モデル定義
│   │   ├── Common/       # 共通モデル
│   │   ├── Requests/     # リクエスト関連
│   │   ├── Responses/    # レスポンス関連
│   │   └── Events/       # イベント関連
│   ├── Services/         # 翻訳サービス
│   └── Common/           # 翻訳共通ユーティリティ
├── Services/             # コアサービス実装
│   ├── Imaging/          # 画像処理サービス
│   ├── Capture/          # キャプチャサービス
│   └── Translation/      # 翻訳サービス
├── Events/               # イベント定義と集約機構
│   ├── Abstractions/     # イベント抽象化
│   ├── Implementation/   # イベント実装
│   └── EventTypes/       # イベント型定義
└── Common/               # 共通ユーティリティ
```

#### 3.2.2 Baketa.Infrastructure (インフラストラクチャ層)

Windows固有の実装と外部サービス連携を担当します：

```
Baketa.Infrastructure/
├── Abstractions/         # インフラ抽象化
├── Platform/             # プラットフォーム関連機能
│   ├── Abstractions/     # プラットフォーム抽象化インターフェース
│   ├── Common/           # 共通機能
│   └── Windows/          # Windows固有実装
│       ├── Imaging/      # Windows画像処理
│       ├── Capture/      # Windowsキャプチャ
│       └── Adapters/     # Windows用アダプター
├── OCR/                  # OCR機能
│   ├── PaddleOCR/        # PaddleOCR実装
│   ├── Services/         # OCRサービス
│   └── Optimization/     # OpenCVベースのOCR最適化
├── Translation/          # 翻訳機能
│   ├── Engines/          # 翻訳エンジン
│   ├── ONNX/             # ONNXモデル実装
│   └── Cloud/            # クラウドサービス連携
└── Persistence/          # 永続化機能
    ├── Settings/         # 設定保存
    └── Cache/            # キャッシュ機能
```

#### 3.2.3 Baketa.Application (アプリケーション層)

ビジネスロジックと機能統合を担当します：

```
Baketa.Application/
├── Abstractions/         # アプリケーション抽象化
├── Services/             # アプリケーションサービス
│   ├── OCR/              # OCRアプリケーションサービス
│   ├── Translation/      # 翻訳アプリケーションサービス
│   ├── Capture/          # キャプチャアプリケーションサービス
│   └── Integration/      # 統合サービス
├── DI/                   # 依存性注入設定
│   ├── Modules/          # DIモジュール
│   └── Extensions/       # DI拡張メソッド
├── Handlers/             # イベントハンドラー
└── Configuration/        # アプリケーション設定
```

#### 3.2.4 Baketa.UI (UI層)

ユーザーインターフェースとプレゼンテーションロジックを担当します：

```
Baketa.UI/
├── Baketa.UI.Abstractions/  # UI抽象化
├── Baketa.UI.Core/          # クロスプラットフォームUI機能
│   ├── Services/         # UI共通サービス
│   ├── Controls/         # 共通コントロール
│   └── ViewModels/       # 共通ビューモデル
├── Baketa.UI.Avalonia/      # Avalonia UI実装
│   ├── Views/            # XAML Views
│   │   ├── Main/         # メイン画面
│   │   ├── Settings/     # 設定画面
│   │   └── Overlay/      # オーバーレイ
│   ├── ViewModels/       # ViewModels
│   ├── Controls/         # カスタムコントロール
│   ├── Behaviors/        # UI振る舞い
│   ├── Converters/       # 値コンバーター
│   └── Services/         # Avalonia固有サービス
└── Baketa.UI.Web/           # （将来的な）Web UI実装用
```

### 3.3 テスト構造

```
tests/
├── Baketa.Core.Tests/
│   ├── Imaging/          # 画像処理テスト
│   ├── Events/           # イベントテスト
│   └── Services/         # サービステスト
├── Baketa.Infrastructure.Tests/
│   ├── Platform/         # プラットフォームテスト
│   ├── OCR/              # OCRテスト
│   └── Translation/      # 翻訳テスト
├── Baketa.Application.Tests/
│   ├── Services/         # アプリケーションサービステスト
│   └── Handlers/         # ハンドラーテスト
└── Baketa.UI.Tests/
    ├── ViewModels/       # ビューモデルテスト
    └── Services/         # UIサービステスト
```

## 4. 命名規則とコーディング基準

### 4.1 インターフェース命名規則

| カテゴリ | 命名パターン | 例 |
|---------|--------------|-----|
| 基本インターフェース | `I[機能名]` | `IImage`, `ICapture` |
| サービスインターフェース | `I[機能名]Service` | `ICaptureService`, `ITranslationService` |
| ファクトリインターフェース | `I[成果物]Factory` | `IImageFactory`, `ICaptureFactory` |
| Windows固有 | `IWindows[機能名]` | `IWindowsImage`, `IWindowsCapture` |

### 4.2 実装クラス命名規則

| カテゴリ | 命名パターン | 例 |
|---------|--------------|-----|
| 基本実装 | `[機能名]` | `Image`, `Capture` |
| サービス実装 | `[機能名]Service` | `CaptureService`, `TranslationService` |
| アダプター | `[機能名]Adapter` | `LegacyImageAdapter`, `WindowsImageAdapter` |
| Windows実装 | `Windows[機能名]` | `WindowsImage`, `WindowsCapture` |

### 4.3 名前空間命名規則

| レイヤー | 命名パターン | 例 |
|---------|--------------|-----|
| コア層 | `Baketa.Core.[機能分野]` | `Baketa.Core.Imaging`, `Baketa.Core.Capture` |
| インフラ層 | `Baketa.Infrastructure.[機能/プラットフォーム]` | `Baketa.Infrastructure.Platform.Windows` |
| アプリケーション層 | `Baketa.Application.[機能]` | `Baketa.Application.Services.OCR` |
| UI層 | `Baketa.UI.[フレームワーク].[機能]` | `Baketa.UI.Avalonia.ViewModels` |
| 抽象化 | `[レイヤー].Abstractions.[機能]` | `Baketa.Core.Abstractions.Imaging` |
| 機能ドメイン | `Baketa.Core.[機能ドメイン]` | `Baketa.Core.Translation.Models` |

## 5. インターフェース設計の例

### 5.1 画像処理インターフェース階層

```csharp
namespace Baketa.Core.Abstractions.Imaging
{
    // IImageBase、IImage、IAdvancedImageの定義は省略...

    /// <summary>
    /// 色チャンネルを表す列挙型
    /// </summary>
    public enum ColorChannel
    {
        /// <summary>
        /// 赤チャンネル
        /// </summary>
        Red,
        
        /// <summary>
        /// 緑チャンネル
        /// </summary>
        Green,
        
        /// <summary>
        /// 青チャンネル
        /// </summary>
        Blue,
        
        /// <summary>
        /// アルファチャンネル（透明度）
        /// </summary>
        Alpha,
        
        /// <summary>
        /// 輝度（明るさ）
        /// </summary>
        Luminance
    }

    /// <summary>
    /// 画像フィルターを表すインターフェース
    /// </summary>
    public interface IImageFilter
    {
        /// <summary>
        /// フィルター名
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// フィルターの説明
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// フィルターパラメータ
        /// </summary>
        IReadOnlyDictionary<string, object> Parameters { get; }
        
        /// <summary>
        /// フィルターを画像データに適用します
        /// </summary>
        /// <param name="sourceData">元の画像データ</param>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <param name="stride">ストライド（行ごとのバイト数）</param>
        /// <returns>フィルター適用後の画像データ</returns>
        byte[] Apply(byte[] sourceData, int width, int height, int stride);
    }
    
    /// <summary>
    /// 高度な画像処理機能を提供するインターフェース
    /// </summary>
    public interface IAdvancedImage : IImage
    {
        /// <summary>
        /// 指定座標のピクセル値を取得します
        /// </summary>
        /// <param name="x">X座標</param>
        /// <param name="y">Y座標</param>
        /// <returns>ピクセル値</returns>
        Color GetPixel(int x, int y);
        
        /// <summary>
        /// 指定座標にピクセル値を設定します
        /// </summary>
        /// <param name="x">X座標</param>
        /// <param name="y">Y座標</param>
        /// <param name="color">設定する色</param>
        void SetPixel(int x, int y, Color color);
        
        /// <summary>
        /// 画像にフィルターを適用します
        /// </summary>
        /// <param name="filter">適用するフィルター</param>
        /// <returns>フィルター適用後の新しい画像</returns>
        Task<IAdvancedImage> ApplyFilterAsync(IImageFilter filter);
        
        /// <summary>
        /// 複数のフィルターを順番に適用します
        /// </summary>
        /// <param name="filters">適用するフィルターのコレクション</param>
        /// <returns>フィルター適用後の新しい画像</returns>
        Task<IAdvancedImage> ApplyFiltersAsync(IEnumerable<IImageFilter> filters);
        
        /// <summary>
        /// 画像のヒストグラムを生成します
        /// </summary>
        /// <param name="channel">対象チャンネル</param>
        /// <returns>ヒストグラムデータ</returns>
        Task<int[]> ComputeHistogramAsync(ColorChannel channel = ColorChannel.Luminance);
        
        /// <summary>
        /// 画像をグレースケールに変換します
        /// </summary>
        /// <returns>グレースケール変換された新しい画像</returns>
        Task<IAdvancedImage> ToGrayscaleAsync();
        
        /// <summary>
        /// 画像を二値化します
        /// </summary>
        /// <param name="threshold">閾値（0～255）</param>
        /// <returns>二値化された新しい画像</returns>
        Task<IAdvancedImage> ToBinaryAsync(byte threshold);
        
        /// <summary>
        /// 画像の特定領域を抽出します
        /// </summary>
        /// <param name="rectangle">抽出する領域</param>
        /// <returns>抽出された新しい画像</returns>
        Task<IAdvancedImage> ExtractRegionAsync(Rectangle rectangle);
        
        /// <summary>
        /// OCR前処理の最適化を行います
        /// </summary>
        /// <returns>OCR向けに最適化された新しい画像</returns>
        Task<IAdvancedImage> OptimizeForOcrAsync();
        
        /// <summary>
        /// OCR前処理の最適化を指定されたオプションで行います
        /// </summary>
        /// <param name="options">最適化オプション</param>
        /// <returns>OCR向けに最適化された新しい画像</returns>
        Task<IAdvancedImage> OptimizeForOcrAsync(OcrImageOptions options);
        
        /// <summary>
        /// 2つの画像の類似度を計算します
        /// </summary>
        /// <param name="other">比較対象の画像</param>
        /// <returns>0.0〜1.0の類似度（1.0が完全一致）</returns>
        Task<float> CalculateSimilarityAsync(IImage other);
        
        /// <summary>
        /// 画像の特定領域におけるテキスト存在可能性を評価します
        /// </summary>
        /// <param name="rectangle">評価する領域</param>
        /// <returns>テキスト存在可能性（0.0〜1.0）</returns>
        Task<float> EvaluateTextProbabilityAsync(Rectangle rectangle);
        
        /// <summary>
        /// 画像の回転を行います
        /// </summary>
        /// <param name="degrees">回転角度（度数法）</param>
        /// <returns>回転された新しい画像</returns>
        Task<IAdvancedImage> RotateAsync(float degrees);
    }

    /// <summary>
    /// OCR画像最適化オプション
    /// </summary>
    public class OcrImageOptions
    {
        /// <summary>
        /// 二値化閾値 (0〜255、0で無効)
        /// </summary>
        public int BinarizationThreshold { get; set; } = 0;
        
        /// <summary>
        /// 適応的二値化を使用するかどうか
        /// </summary>
        public bool UseAdaptiveThreshold { get; set; } = true;
        
        /// <summary>
        /// 適応的二値化のブロックサイズ
        /// </summary>
        public int AdaptiveBlockSize { get; set; } = 11;
        
        /// <summary>
        /// ノイズ除去レベル (0.0〜1.0)
        /// </summary>
        public float NoiseReduction { get; set; } = 0.3f;
        
        /// <summary>
        /// コントラスト強調 (0.0〜2.0、1.0で変更なし)
        /// </summary>
        public float ContrastEnhancement { get; set; } = 1.2f;
        
        /// <summary>
        /// シャープネス強調 (0.0〜1.0)
        /// </summary>
        public float SharpnessEnhancement { get; set; } = 0.3f;
        
        /// <summary>
        /// 境界を膨張させる画素数
        /// </summary>
        public int DilationPixels { get; set; } = 0;
        
        /// <summary>
        /// テキスト方向の検出と修正
        /// </summary>
        public bool DetectAndCorrectOrientation { get; set; } = false;
    }
}
```

### 5.2 翻訳モデルの名前空間構造

2025年5月に完了した名前空間統一プロジェクトにより、翻訳関連のデータモデルはすべて `Baketa.Core.Translation.Models` 名前空間に統一されました。

```csharp
namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// 言語情報を表すクラス
    /// </summary>
    public class Language
    {
        /// <summary>
        /// 言語コード（例: "en-US", "ja-JP", "zh-CN"）
        /// </summary>
        public string Code { get; set; }
        
        /// <summary>
        /// 表示名（例: "英語 (米国)", "日本語", "中国語 (簡体字)"）
        /// </summary>
        public string DisplayName { get; set; }
        
        /// <summary>
        /// 現地語での言語名（例: "English", "日本語", "中文"）
        /// </summary>
        public string NativeName { get; set; }
        
        /// <summary>
        /// 自動検出フラグ
        /// </summary>
        public bool IsAutoDetect { get; set; }
        
        /// <summary>
        /// 右から左への書字方向かどうか
        /// </summary>
        public bool IsRightToLeft { get; set; }
        
        /// <summary>
        /// 言語コードから新しいインスタンスを作成
        /// </summary>
        public static Language FromCode(string code)
        {
            // 実装
        }
    }
    
    /// <summary>
    /// 言語ペアを表すクラス
    /// </summary>
    public class LanguagePair
    {
        /// <summary>
        /// ソース言語
        /// </summary>
        public Language Source { get; set; }
        
        /// <summary>
        /// ターゲット言語
        /// </summary>
        public Language Target { get; set; }
        
        /// <summary>
        /// 言語コードから言語ペアを作成
        /// </summary>
        public static LanguagePair Create(string sourceCode, string targetCode)
        {
            // 実装
        }
        
        /// <summary>
        /// "en-US:ja-JP" 形式の文字列から言語ペアを作成
        /// </summary>
        public static LanguagePair FromString(string pairString)
        {
            // 実装
        }
    }
    
    /// <summary>
    /// 翻訳リクエストを表すクラス
    /// </summary>
    public class TranslationRequest
    {
        /// <summary>
        /// 翻訳元テキスト
        /// </summary>
        public string Text { get; set; }
        
        /// <summary>
        /// 言語ペア
        /// </summary>
        public LanguagePair Languages { get; set; }
        
        /// <summary>
        /// リクエスト作成タイムスタンプ
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// 翻訳コンテキスト
        /// </summary>
        public TranslationContext Context { get; set; }
        
        /// <summary>
        /// テキストと言語ペアから翻訳リクエストを作成
        /// </summary>
        public static TranslationRequest Create(string text, LanguagePair languages)
        {
            // 実装
        }
        
        /// <summary>
        /// コンテキスト情報を含む翻訳リクエストを作成
        /// </summary>
        public static TranslationRequest CreateWithContext(
            string text, LanguagePair languages, TranslationContext context)
        {
            // 実装
        }
        
        /// <summary>
        /// リクエストのクローンを作成
        /// </summary>
        public TranslationRequest Clone()
        {
            // 実装
        }
        
        /// <summary>
        /// キャッシュキーを生成
        /// </summary>
        public string GenerateCacheKey()
        {
            // 実装
        }
    }
    
    /// <summary>
    /// 翻訳レスポンスを表すクラス
    /// </summary>
    public class TranslationResponse
    {
        /// <summary>
        /// 元のリクエスト
        /// </summary>
        public TranslationRequest Request { get; set; }
        
        /// <summary>
        /// 翻訳結果テキスト
        /// </summary>
        public string TranslatedText { get; set; }
        
        /// <summary>
        /// 翻訳の成功フラグ
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// レスポンス作成タイムスタンプ
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// エラー情報（失敗時）
        /// </summary>
        public TranslationError Error { get; set; }
        
        /// <summary>
        /// 翻訳信頼度（0.0〜1.0）
        /// </summary>
        public float Confidence { get; set; }
        
        /// <summary>
        /// レスポンスのクローンを作成
        /// </summary>
        public TranslationResponse Clone()
        {
            // 実装
        }
        
        /// <summary>
        /// 成功レスポンスを作成
        /// </summary>
        public static TranslationResponse CreateSuccess(
            TranslationRequest request, string translatedText)
        {
            // 実装
        }
        
        /// <summary>
        /// 信頼度付きの成功レスポンスを作成
        /// </summary>
        public static TranslationResponse CreateSuccessWithConfidence(
            TranslationRequest request, string translatedText, float confidence)
        {
            // 実装
        }
        
        /// <summary>
        /// エラーレスポンスを作成
        /// </summary>
        public static TranslationResponse CreateError(
            TranslationRequest request, TranslationError error)
        {
            // 実装
        }
    }
}
```

### 5.3 Windows画像インターフェース

```csharp
namespace Baketa.Infrastructure.Platform.Abstractions
{
    /// <summary>
    /// Windows画像機能を定義するインターフェース
    /// </summary>
    public interface IWindowsImage : IDisposable
    {
        /// <summary>
        /// 画像の幅（ピクセル単位）
        /// </summary>
        int Width { get; }

        /// <summary>
        /// 画像の高さ（ピクセル単位）
        /// </summary>
        int Height { get; }

        /// <summary>
        /// Windows固有の画像オブジェクトを取得
        /// </summary>
        /// <returns>Windows Bitmap</returns>
        Bitmap GetNativeImage();

        /// <summary>
        /// Windowsの形式でファイルに保存
        /// </summary>
        /// <param name="path">保存先のパス</param>
        Task SaveAsync(string path);
    }

    /// <summary>
    /// Windows画像作成ファクトリ
    /// </summary>
    public interface IWindowsImageFactory
    {
        /// <summary>
        /// ファイルから画像を作成
        /// </summary>
        /// <param name="path">ファイルパス</param>
        /// <returns>Windows画像</returns>
        Task<IWindowsImage> CreateFromFileAsync(string path);

        /// <summary>
        /// バイト配列から画像を作成
        /// </summary>
        /// <param name="data">画像データ</param>
        /// <returns>Windows画像</returns>
        Task<IWindowsImage> CreateFromBytesAsync(byte[] data);
    }
}
```

### 5.4 アダプターの実装例

```csharp
namespace Baketa.Infrastructure.Platform.Windows.Adapters
{
    /// <summary>
    /// WindowsImageをIImageインターフェースに適応させるアダプター
    /// </summary>
    public class WindowsImageAdapter : IImage
    {
        private readonly IWindowsImage _windowsImage;
        private bool _disposed;

        public WindowsImageAdapter(IWindowsImage windowsImage)
        {
            _windowsImage = windowsImage ?? throw new ArgumentNullException(nameof(windowsImage));
        }

        public int Width => _windowsImage.Width;
        public int Height => _windowsImage.Height;

        public IImage Clone()
        {
            // Windows固有の実装を使用して画像をクローン
            var nativeImage = _windowsImage.GetNativeImage();
            var clonedNative = new Bitmap(nativeImage);
            var clonedWindows = new WindowsImage(clonedNative);
            
            return new WindowsImageAdapter(clonedWindows);
        }

        public async Task<byte[]> ToByteArrayAsync()
        {
            using var stream = new MemoryStream();
            var nativeImage = _windowsImage.GetNativeImage();
            nativeImage.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }

        public async Task<IImage> ResizeAsync(int width, int height)
        {
            var nativeImage = _windowsImage.GetNativeImage();
            var resized = new Bitmap(nativeImage, new Size(width, height));
            var resizedWindows = new WindowsImage(resized);
            
            return new WindowsImageAdapter(resizedWindows);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _windowsImage.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
```

## 6. サービス層設計

### 6.1 キャプチャサービス抽象化

```csharp
namespace Baketa.Core.Abstractions.Capture
{
    /// <summary>
    /// キャプチャサービスの抽象化
    /// </summary>
    public interface ICaptureService
    {
        /// <summary>
        /// 画面全体をキャプチャ
        /// </summary>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureScreenAsync();

        /// <summary>
        /// 指定した領域をキャプチャ
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureRegionAsync(Rectangle region);
    }
}

namespace Baketa.Infrastructure.Platform.Abstractions
{
    /// <summary>
    /// Windows固有のキャプチャ機能
    /// </summary>
    public interface IWindowsCaptureService
    {
        /// <summary>
        /// 画面全体をキャプチャ
        /// </summary>
        /// <returns>Windows画像</returns>
        Task<IWindowsImage> CaptureScreenAsync();

        /// <summary>
        /// 指定した領域をキャプチャ
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>Windows画像</returns>
        Task<IWindowsImage> CaptureRegionAsync(Rectangle region);
    }
}
```

### 6.2 アプリケーションサービス実装

```csharp
namespace Baketa.Application.Services.Capture
{
    /// <summary>
    /// キャプチャサービスの実装
    /// </summary>
    public class CaptureService : ICaptureService
    {
        private readonly IWindowsCaptureService _windowsCaptureService;
        private readonly IImageFactory _imageFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly ILogger<CaptureService> _logger;

        public CaptureService(
            IWindowsCaptureService windowsCaptureService,
            IImageFactory imageFactory,
            IEventAggregator eventAggregator,
            ILogger<CaptureService> logger)
        {
            _windowsCaptureService = windowsCaptureService;
            _imageFactory = imageFactory;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public async Task<IImage> CaptureScreenAsync()
        {
            try
            {
                _logger.LogInformation("画面全体のキャプチャを開始");
                
                // Windows固有の実装を使用
                var windowsImage = await _windowsCaptureService.CaptureScreenAsync();
                
                // コアインターフェースに変換
                var image = _imageFactory.CreateFromWindowsImage(windowsImage);
                
                // キャプチャ完了イベントを発行
                await _eventAggregator.PublishAsync(new CaptureCompletedEvent(image));
                
                _logger.LogInformation("画面キャプチャが完了: {Width}x{Height}", image.Width, image.Height);
                
                return image;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "画面キャプチャ中にエラーが発生");
                throw new CaptureException("画面キャプチャに失敗しました", ex);
            }
        }

        public async Task<IImage> CaptureRegionAsync(Rectangle region)
        {
            try
            {
                _logger.LogInformation("領域キャプチャを開始: {Region}", region);
                
                // Windows固有の実装を使用
                var windowsImage = await _windowsCaptureService.CaptureRegionAsync(region);
                
                // コアインターフェースに変換
                var image = _imageFactory.CreateFromWindowsImage(windowsImage);
                
                // キャプチャ完了イベントを発行
                await _eventAggregator.PublishAsync(new CaptureCompletedEvent(image));
                
                _logger.LogInformation("領域キャプチャが完了: {Width}x{Height}", image.Width, image.Height);
                
                return image;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "領域キャプチャ中にエラーが発生: {Region}", region);
                throw new CaptureException($"領域キャプチャに失敗しました: {region}", ex);
            }
        }
    }
}
```

## 7. イベント集約機構の設計

イベント集約機構により、モジュール間の疎結合なコミュニケーションを実現します。この機構は既に実装済みであり、主要なインターフェースと実装クラスは以下の通りです：

### 7.1 主要インターフェース

```csharp
namespace Baketa.Core.Events
{
    /// <summary>
    /// 基本イベントインターフェース
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// イベントID
        /// </summary>
        Guid Id { get; }
        
        /// <summary>
        /// イベント発生時刻
        /// </summary>
        DateTime Timestamp { get; }
        
        /// <summary>
        /// イベント名
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        string Category { get; }
    }

    /// <summary>
    /// イベント処理インターフェース
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    public interface IEventProcessor<in TEvent> where TEvent : IEvent
    {
        /// <summary>
        /// イベント処理
        /// </summary>
        /// <param name="eventData">イベント</param>
        /// <returns>処理の完了を表すTask</returns>
        Task HandleAsync(TEvent eventData);
    }

    /// <summary>
    /// イベント集約インターフェース
    /// </summary>
    public interface IEventAggregator
    {
        /// <summary>
        /// イベントの発行
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="eventData">イベント</param>
        /// <returns>イベント発行の完了を表すTask</returns>
        Task PublishAsync<TEvent>(TEvent eventData) where TEvent : IEvent;
        
        /// <summary>
        /// イベントプロセッサの登録
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="processor">イベントプロセッサ</param>
        void Subscribe<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent;
        
        /// <summary>
        /// イベントプロセッサの登録解除
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="processor">イベントプロセッサ</param>
        void Unsubscribe<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent;
    }
}
```

### 7.2 実装クラス

イベント集約機構は`EventAggregator`クラスに実装されており、以下の機能を提供しています：

```csharp
namespace Baketa.Core.Events.Implementation
{
    /// <summary>
    /// イベント集約機構の実装
    /// </summary>
    public class EventAggregator : IEventAggregator
    {
        private readonly ILogger<EventAggregator>? _logger;
        private readonly Dictionary<Type, List<object>> _processors = new Dictionary<Type, List<object>>();
        private readonly object _syncRoot = new object();

        /// <summary>
        /// イベント集約機構を初期化します
        /// </summary>
        /// <param name="logger">ロガー（オプション）</param>
        public EventAggregator(ILogger<EventAggregator>? logger = null)
        {
            _logger = logger;
        }
        
        /// <inheritdoc />
        public async Task PublishAsync<TEvent>(TEvent eventData) where TEvent : IEvent
        {
            // イベント発行ロジック
        }
        
        /// <summary>
        /// キャンセレーション対応のイベント発行
        /// </summary>
        public async Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken) 
            where TEvent : IEvent
        {
            // キャンセレーション対応のイベント発行ロジック
        }
        
        /// <inheritdoc />
        public void Subscribe<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent
        {
            // プロセッサ登録ロジック
        }
        
        /// <inheritdoc />
        public void Unsubscribe<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent
        {
            // プロセッサ登録解除ロジック
        }
    }
}
```

この革新的なイベント集約機構は、モジュール間の疎結合なコミュニケーションを実現し、系統だったイベント処理を可能にします。

## 8. 翻訳名前空間の最適化（2025年5月更新）

2025年5月に完了した名前空間統一プロジェクトにより、以下の改善が実現されました：

1. **名前空間の統一**
   - 翻訳関連のすべてのモデルが `Baketa.Core.Translation.Models` に統一
   - 型参照の曖昧さを完全に排除
   - 名前空間エイリアスが不要に

2. **標準的な表現方法の採用**
   - 言語コードの表現を国際標準（ISO 639-1/ISO 3166）に準拠
   - 例: `zh-CN` 形式での一貫した表現

3. **機能強化と統合**
   - `Language`クラスに `IsRightToLeft` プロパティなどの機能を追加
   - `TranslationRequest`に `Timestamp` や `Context` のサポートを追加
   - 全体的なAPIの一貫性と使いやすさの向上

詳細は[名前空間統一完了報告](../development-notes/namespace-unification-completion-report.md)を参照してください。

## 9. 名前空間の衝突が起きにくいようにする提案

### 9.1 階層的な名前空間構造の徹底

```csharp
Baketa.Core.Abstractions.Imaging
Baketa.Core.Abstractions.Capture
Baketa.Infrastructure.Platform.Windows
```

のように、機能ごとに明確に区分けされた階層構造を作り、一貫性を保ちます。

### 9.2 インターフェースと実装の命名規則の厳格化

- インターフェース: `IImageProcessor`
- 実装クラス: `WindowsImageProcessor`
- アダプター: `WindowsImageProcessorAdapter`

明確な接頭辞/接尾辞のパターンを使用することで、同名の衝突を防ぎます。

### 9.3 機能ドメインに基づく名前空間構造

```
Baketa.Core.[機能ドメイン]                 // 例: Translation, OCR, Imaging
├── Baketa.Core.[機能ドメイン].Abstractions // インターフェース定義
├── Baketa.Core.[機能ドメイン].Models       // モデル定義
├── Baketa.Core.[機能ドメイン].Services     // サービス実装
└── Baketa.Core.[機能ドメイン].Common       // 共通ユーティリティ
```

この構造により、名前空間競合を早期に検出し対応できるようになります。

## 10. まとめ

本ドキュメントで提案した改善されたアーキテクチャは、以下の利点をもたらします：

1. **明確な責任分担**: 各レイヤーとモジュールの役割が明確に定義されている
2. **名前空間の一貫性**: 名前空間の衝突がなく、責任範囲が明確
3. **Windows依存部分の整理**: Windows依存コードが適切に分離され、管理が容易になっている
4. **拡張性**: インターフェースを通じた機能拡張が容易
5. **テスト容易性**: 依存関係が明確で、各コンポーネントの単体テストが容易

2025年5月に完了した名前空間統一プロジェクトにより、特に翻訳モデル関連の名前空間の問題が解決され、コードベースの一貫性と保守性が大幅に向上しました。今後も新機能の追加時には、ここで提案した設計原則を遵守することで、名前空間の衝突を防ぎ、保守性の高いコードベースを維持していくことが可能です。