# イメージ抽象化レイヤー

*最終更新: 2025年4月18日*

## 1. 概要

イメージ抽象化レイヤーは、Baketaアプリケーションにおける画像操作の基盤となる重要なコンポーネントです。このレイヤーは、プラットフォーム非依存の画像操作機能を提供し、OCRや翻訳オーバーレイなどの上位機能のための共通基盤を確立します。

本ドキュメントでは、イメージ抽象化レイヤーの設計原則、インターフェース階層、および実装パターンについて説明します。

## 2. 設計目標と原則

イメージ抽象化レイヤーの設計には、以下の目標と原則を適用します：

1. **プラットフォーム中立性**: 基本的な画像操作はプラットフォーム固有の実装に依存しない
2. **階層的抽象化**: 基本的な機能から高度な機能まで適切に階層化する
3. **パフォーマンス考慮**: メモリ効率と処理速度を考慮した設計
4. **拡張性**: 新しい画像処理アルゴリズムやフィルターを容易に追加できる構造
5. **リソース管理**: 大きな画像リソースの適切な管理と解放を保証する

## 3. インターフェース階層

イメージ抽象化レイヤーは以下の主要インターフェースで構成されます：

```
IImageBase
    ↑
IImage
    ↑
IAdvancedImage
```

### 3.1 IImageBase

基本的な画像プロパティとメモリ管理を定義する最も基本的なインターフェース：

```csharp
namespace Baketa.Core.Abstractions.Imaging
{
    /// <summary>
    /// 基本的な画像プロパティを提供する基底インターフェース
    /// </summary>
    public interface IImageBase : IDisposable
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
        /// 画像をバイト配列に変換します。
        /// </summary>
        /// <returns>画像データを表すバイト配列</returns>
        Task<byte[]> ToByteArrayAsync();
        
        /// <summary>
        /// 画像のフォーマット
        /// </summary>
        ImageFormat Format { get; }
    }
}
```

### 3.2 IImage

標準的な画像操作機能を提供するインターフェース：

```csharp
namespace Baketa.Core.Abstractions.Imaging
{
    /// <summary>
    /// 標準的な画像操作機能を提供するインターフェース
    /// </summary>
    public interface IImage : IImageBase
    {
        /// <summary>
        /// 画像のクローンを作成します。
        /// </summary>
        /// <returns>元の画像と同じ内容を持つ新しい画像インスタンス</returns>
        IImage Clone();
        
        /// <summary>
        /// 画像のサイズを変更します。
        /// </summary>
        /// <param name="width">新しい幅</param>
        /// <param name="height">新しい高さ</param>
        /// <returns>リサイズされた新しい画像インスタンス</returns>
        Task<IImage> ResizeAsync(int width, int height);
        
        /// <summary>
        /// 画像を指定されたパスに保存します。
        /// </summary>
        /// <param name="path">保存先のパス</param>
        /// <param name="format">保存フォーマット（省略時は画像の元のフォーマット）</param>
        /// <returns>保存処理の完了を表すTask</returns>
        Task SaveAsync(string path, ImageFormat? format = null);
        
        /// <summary>
        /// 画像の一部を切り取ります。
        /// </summary>
        /// <param name="rectangle">切り取る領域</param>
        /// <returns>切り取られた新しい画像インスタンス</returns>
        Task<IImage> CropAsync(Rectangle rectangle);
        
        /// <summary>
        /// 指定された範囲のピクセルデータを取得します。
        /// </summary>
        /// <param name="x">開始X座標</param>
        /// <param name="y">開始Y座標</param>
        /// <param name="width">幅</param>
        /// <param name="height">高さ</param>
        /// <returns>ピクセルデータの配列</returns>
        Task<byte[]> GetPixelsAsync(int x, int y, int width, int height);
    }
}
```

### 3.3 IAdvancedImage

高度な画像処理機能を提供するインターフェース：

```csharp
namespace Baketa.Core.Abstractions.Imaging
{
    /// <summary>
    /// 高度な画像処理機能を提供するインターフェース
    /// </summary>
    public interface IAdvancedImage : IImage
    {
        /// <summary>
        /// 画像にフィルターを適用します。
        /// </summary>
        /// <param name="filter">適用するフィルター</param>
        /// <returns>フィルターが適用された新しい画像インスタンス</returns>
        Task<IImage> ApplyFilterAsync(IImageFilter filter);
        
        /// <summary>
        /// 2つの画像の類似度を計算します。
        /// </summary>
        /// <param name="other">比較対象の画像</param>
        /// <returns>0.0〜1.0の類似度（1.0が完全一致）</returns>
        Task<float> CalculateSimilarityAsync(IImage other);
        
        /// <summary>
        /// 画像の特定領域におけるテキスト可能性を評価します。
        /// </summary>
        /// <param name="rectangle">評価する領域</param>
        /// <returns>テキスト存在可能性（0.0〜1.0）</returns>
        Task<float> EvaluateTextProbabilityAsync(Rectangle rectangle);
        
        /// <summary>
        /// 画像の強調処理を行います。
        /// </summary>
        /// <param name="options">強調オプション</param>
        /// <returns>強調処理された新しい画像インスタンス</returns>
        Task<IImage> EnhanceAsync(ImageEnhancementOptions options);
        
        /// <summary>
        /// 画像から自動的にテキスト領域を検出します。
        /// </summary>
        /// <returns>検出されたテキスト領域の矩形リスト</returns>
        Task<List<Rectangle>> DetectTextRegionsAsync();
    }
}
```

### 3.4 補助インターフェース

#### IImageFilter

画像フィルターを表すインターフェース：

```csharp
namespace Baketa.Core.Abstractions.Imaging
{
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
        /// フィルターを画像データに適用します。
        /// </summary>
        /// <param name="sourceData">元の画像データ</param>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <param name="stride">ストライド（行ごとのバイト数）</param>
        /// <returns>フィルター適用後の画像データ</returns>
        byte[] Apply(byte[] sourceData, int width, int height, int stride);
    }
}
```

#### IImageFactory

画像インスタンスを作成するファクトリインターフェース：

```csharp
namespace Baketa.Core.Abstractions.Imaging
{
    /// <summary>
    /// 画像インスタンスを作成するファクトリインターフェース
    /// </summary>
    public interface IImageFactory
    {
        /// <summary>
        /// ファイルパスから画像を作成します。
        /// </summary>
        /// <param name="filePath">画像ファイルパス</param>
        /// <returns>作成された画像インスタンス</returns>
        Task<IImage> CreateFromFileAsync(string filePath);
        
        /// <summary>
        /// バイト配列から画像を作成します。
        /// </summary>
        /// <param name="data">画像データのバイト配列</param>
        /// <returns>作成された画像インスタンス</returns>
        Task<IImage> CreateFromBytesAsync(byte[] data);
        
        /// <summary>
        /// 指定されたサイズの空の画像を作成します。
        /// </summary>
        /// <param name="width">幅</param>
        /// <param name="height">高さ</param>
        /// <param name="format">画像フォーマット</param>
        /// <returns>作成された画像インスタンス</returns>
        Task<IImage> CreateEmptyAsync(int width, int height, ImageFormat format = ImageFormat.Rgb24);
        
        /// <summary>
        /// プラットフォーム固有の画像からIImageを作成します。
        /// </summary>
        /// <param name="platformImage">プラットフォーム固有の画像</param>
        /// <returns>作成された画像インスタンス</returns>
        IImage CreateFromPlatformImage(object platformImage);
    }
}
```

#### 列挙型とオプション

```csharp
namespace Baketa.Core.Abstractions.Imaging
{
    /// <summary>
    /// 画像フォーマット
    /// </summary>
    public enum ImageFormat
    {
        /// <summary>
        /// 不明なフォーマット
        /// </summary>
        Unknown = 0,
        
        /// <summary>
        /// RGB24フォーマット
        /// </summary>
        Rgb24 = 1,
        
        /// <summary>
        /// RGBA32フォーマット
        /// </summary>
        Rgba32 = 2,
        
        /// <summary>
        /// グレースケール
        /// </summary>
        Grayscale8 = 3,
        
        /// <summary>
        /// PNG
        /// </summary>
        Png = 10,
        
        /// <summary>
        /// JPEG
        /// </summary>
        Jpeg = 11,
        
        /// <summary>
        /// BMP
        /// </summary>
        Bmp = 12
    }
    
    /// <summary>
    /// 画像強調オプション
    /// </summary>
    public class ImageEnhancementOptions
    {
        /// <summary>
        /// 明るさ調整 (-1.0〜1.0)
        /// </summary>
        public float Brightness { get; set; } = 0.0f;
        
        /// <summary>
        /// コントラスト調整 (0.0〜2.0)
        /// </summary>
        public float Contrast { get; set; } = 1.0f;
        
        /// <summary>
        /// シャープネス調整 (0.0〜1.0)
        /// </summary>
        public float Sharpness { get; set; } = 0.0f;
        
        /// <summary>
        /// ノイズ除去レベル (0.0〜1.0)
        /// </summary>
        public float NoiseReduction { get; set; } = 0.0f;
        
        /// <summary>
        /// 二値化閾値 (0〜255、0で無効)
        /// </summary>
        public int BinarizationThreshold { get; set; } = 0;
        
        /// <summary>
        /// 適応的二値化を使用するかどうか
        /// </summary>
        public bool UseAdaptiveThreshold { get; set; } = false;
        
        /// <summary>
        /// 適応的二値化のブロックサイズ
        /// </summary>
        public int AdaptiveBlockSize { get; set; } = 11;
        
        /// <summary>
        /// テキスト検出のための最適化を行うかどうか
        /// </summary>
        public bool OptimizeForTextDetection { get; set; } = false;
    }
}
```

## 4. 実装ガイドライン

### 4.1 基本クラス構造

```csharp
namespace Baketa.Core.Services.Imaging
{
    /// <summary>
    /// IImageの基本実装
    /// </summary>
    public class CoreImage : IImage
    {
        private byte[] _pixelData;
        private readonly int _width;
        private readonly int _height;
        private readonly ImageFormat _format;
        private bool _disposed;
        
        public CoreImage(byte[] pixelData, int width, int height, ImageFormat format)
        {
            _pixelData = pixelData ?? throw new ArgumentNullException(nameof(pixelData));
            _width = width;
            _height = height;
            _format = format;
        }
        
        public int Width => _width;
        
        public int Height => _height;
        
        public ImageFormat Format => _format;
        
        public Task<byte[]> ToByteArrayAsync()
        {
            ThrowIfDisposed();
            return Task.FromResult(_pixelData.ToArray());
        }
        
        public IImage Clone()
        {
            ThrowIfDisposed();
            return new CoreImage(_pixelData.ToArray(), _width, _height, _format);
        }
        
        // その他のメソッド実装
        
        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CoreImage));
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _pixelData = null;
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
        
        // その他の実装...
    }
}
```

### 4.2 画像フィルタの実装例

```csharp
namespace Baketa.Core.Services.Imaging.Filters
{
    /// <summary>
    /// コントラスト調整フィルター
    /// </summary>
    public class ContrastFilter : IImageFilter
    {
        public string Name => "Contrast Adjustment";
        
        public string Description => "画像のコントラストを調整します";
        
        public float Factor { get; }
        
        public IReadOnlyDictionary<string, object> Parameters => 
            new Dictionary<string, object> { ["Factor"] = Factor };
        
        public ContrastFilter(float factor)
        {
            if (factor < 0)
                throw new ArgumentOutOfRangeException(nameof(factor), "コントラスト係数は0以上である必要があります");
                
            Factor = factor;
        }
        
        public byte[] Apply(byte[] sourceData, int width, int height, int stride)
        {
            if (sourceData == null)
                throw new ArgumentNullException(nameof(sourceData));
                
            // コントラスト調整アルゴリズムの実装
            var result = new byte[sourceData.Length];
            Array.Copy(sourceData, result, sourceData.Length);
            
            if (Math.Abs(Factor - 1.0f) < float.Epsilon)
                return result; // 変更なし
                
            for (int i = 0; i < result.Length; i++)
            {
                // 画素値を0-1の範囲に正規化
                float normalizedValue = result[i] / 255.0f;
                
                // コントラスト調整
                normalizedValue = (normalizedValue - 0.5f) * Factor + 0.5f;
                
                // 値を0-1の範囲にクリップ
                normalizedValue = Math.Clamp(normalizedValue, 0, 1);
                
                // バイト値に戻す
                result[i] = (byte)(normalizedValue * 255);
            }
            
            return result;
        }
    }
}
```

### 4.3 ファクトリ実装例

```csharp
namespace Baketa.Core.Services.Imaging
{
    /// <summary>
    /// 基本的な画像ファクトリ実装
    /// </summary>
    public class CoreImageFactory : IImageFactory
    {
        public async Task<IImage> CreateFromFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが無効です", nameof(filePath));
                
            if (!File.Exists(filePath))
                throw new FileNotFoundException("指定されたファイルが見つかりません", filePath);
                
            // ファイルからピクセルデータの読み込み実装
            // 実際の実装ではプラットフォーム依存することが多い
            
            // ここでは仮実装
            byte[] fileData = await File.ReadAllBytesAsync(filePath);
            return await CreateFromBytesAsync(fileData);
        }
        
        public Task<IImage> CreateFromBytesAsync(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("無効な画像データです", nameof(data));
                
            // デコードロジックの実装
            // 実際の実装ではプラットフォーム依存することが多い
            
            // ここでは仮実装
            var image = new CoreImage(data, 100, 100, ImageFormat.Rgb24);
            return Task.FromResult<IImage>(image);
        }
        
        public Task<IImage> CreateEmptyAsync(int width, int height, ImageFormat format = ImageFormat.Rgb24)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "幅は正の値である必要があります");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "高さは正の値である必要があります");
                
            // フォーマットに基づいてピクセルあたりのバイト数を計算
            int bytesPerPixel = format switch
            {
                ImageFormat.Rgb24 => 3,
                ImageFormat.Rgba32 => 4,
                ImageFormat.Grayscale8 => 1,
                _ => throw new ArgumentException("サポートされていないフォーマットです", nameof(format))
            };
            
            // 空の画像データを作成
            int dataSize = width * height * bytesPerPixel;
            var pixelData = new byte[dataSize];
            
            var image = new CoreImage(pixelData, width, height, format);
            return Task.FromResult<IImage>(image);
        }
        
        public IImage CreateFromPlatformImage(object platformImage)
        {
            if (platformImage == null)
                throw new ArgumentNullException(nameof(platformImage));
                
            // プラットフォーム固有の画像からの変換ロジック
            // 実際の実装ではプラットフォーム依存
            
            throw new NotImplementedException("この基本実装ではサポートされていません");
        }
    }
}
```

## 5. プラットフォーム抽象化との連携

イメージ抽象化レイヤーはプラットフォーム抽象化レイヤーと連携して動作します。インターフェース階層は独立していますが、特定のプラットフォーム実装が必要となる場合は、アダプターパターンを用いて連携します。

### 5.1 アダプターの使用例

```csharp
// Windows Image → Core Image アダプター
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
    
    public ImageFormat Format => ImageFormat.Rgba32; // Windowsビットマップはデフォルトでこのフォーマット
    
    // インターフェース実装
    // ...
}

// Windows Image Factory
public class WindowsImageAdapterFactory : IImageFactory
{
    private readonly IWindowsImageFactory _windowsImageFactory;
    
    public WindowsImageAdapterFactory(IWindowsImageFactory windowsImageFactory)
    {
        _windowsImageFactory = windowsImageFactory;
    }
    
    public async Task<IImage> CreateFromFileAsync(string filePath)
    {
        var windowsImage = await _windowsImageFactory.CreateFromFileAsync(filePath);
        return new WindowsImageAdapter(windowsImage);
    }
    
    // 他のメソッド実装
    // ...
}
```

## 6. OCR関連の画像前処理

OCR処理のための特殊な画像前処理機能を提供する拡張インターフェース：

```csharp
namespace Baketa.Core.Abstractions.Imaging.Ocr
{
    /// <summary>
    /// OCR用画像前処理インターフェース
    /// </summary>
    public interface IOcrImageProcessor
    {
        /// <summary>
        /// OCR処理に最適化された画像を生成します
        /// </summary>
        /// <param name="image">元の画像</param>
        /// <param name="options">最適化オプション</param>
        /// <returns>OCR用に最適化された画像</returns>
        Task<IImage> OptimizeForOcrAsync(IImage image, OcrImageOptions options);
        
        /// <summary>
        /// 画像からテキスト領域候補を検出します
        /// </summary>
        /// <param name="image">分析する画像</param>
        /// <returns>テキスト領域候補のリスト</returns>
        Task<List<TextRegionCandidate>> DetectTextRegionsAsync(IImage image);
        
        /// <summary>
        /// 2つの画像間のテキスト領域の変化を検出します
        /// </summary>
        /// <param name="previous">前の画像</param>
        /// <param name="current">現在の画像</param>
        /// <returns>変化したと思われるテキスト領域</returns>
        Task<List<Rectangle>> DetectTextChangesAsync(IImage previous, IImage current);
    }
    
    /// <summary>
    /// テキスト領域候補
    /// </summary>
    public class TextRegionCandidate
    {
        /// <summary>
        /// 領域の位置と大きさ
        /// </summary>
        public Rectangle Bounds { get; set; }
        
        /// <summary>
        /// テキスト存在の確信度 (0.0〜1.0)
        /// </summary>
        public float Confidence { get; set; }
        
        /// <summary>
        /// テキスト方向 (水平、垂直など)
        /// </summary>
        public TextOrientation Orientation { get; set; }
    }
    
    /// <summary>
    /// テキスト方向
    /// </summary>
    public enum TextOrientation
    {
        /// <summary>
        /// 水平方向 (左から右)
        /// </summary>
        Horizontal = 0,
        
        /// <summary>
        /// 垂直方向 (上から下)
        /// </summary>
        Vertical = 1,
        
        /// <summary>
        /// 不明または検出不可
        /// </summary>
        Unknown = 2
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
        
        /// <summary>
        /// プリセットプロファイル
        /// </summary>
        public static OcrImageOptions CreatePreset(OcrPreset preset)
        {
            return preset switch
            {
                OcrPreset.Default => new OcrImageOptions(),
                OcrPreset.HighContrast => new OcrImageOptions
                {
                    UseAdaptiveThreshold = true,
                    ContrastEnhancement = 1.4f,
                    NoiseReduction = 0.4f
                },
                OcrPreset.SmallText => new OcrImageOptions
                {
                    AdaptiveBlockSize = 7,
                    SharpnessEnhancement = 0.5f,
                    NoiseReduction = 0.5f
                },
                OcrPreset.LightText => new OcrImageOptions
                {
                    UseAdaptiveThreshold = true,
                    ContrastEnhancement = 1.6f,
                    NoiseReduction = 0.4f,
                    DilationPixels = 1
                },
                _ => throw new ArgumentException("不明なプリセットです", nameof(preset))
            };
        }
    }
    
    /// <summary>
    /// OCRプリセット
    /// </summary>
    public enum OcrPreset
    {
        /// <summary>
        /// デフォルト設定
        /// </summary>
        Default = 0,
        
        /// <summary>
        /// 高コントラスト設定
        /// </summary>
        HighContrast = 1,
        
        /// <summary>
        /// 小さいテキスト向け設定
        /// </summary>
        SmallText = 2,
        
        /// <summary>
        /// 薄いテキスト向け設定
        /// </summary>
        LightText = 3
    }
}
```

## 7. 単体テスト

### 7.1 テスト用モック実装

```csharp
namespace Baketa.Core.Tests.Imaging
{
    public class MockImage : IImage
    {
        public int Width { get; }
        
        public int Height { get; }
        
        public ImageFormat Format { get; }
        
        public MockImage(int width, int height, ImageFormat format = ImageFormat.Rgb24)
        {
            Width = width;
            Height = height;
            Format = format;
        }
        
        public Task<byte[]> ToByteArrayAsync()
        {
            // テスト用のダミーデータを返す
            return Task.FromResult(new byte[Width * Height * 3]);
        }
        
        public IImage Clone()
        {
            return new MockImage(Width, Height, Format);
        }
        
        public Task<IImage> ResizeAsync(int width, int height)
        {
            return Task.FromResult<IImage>(new MockImage(width, height, Format));
        }
        
        // その他の実装
        
        public void Dispose()
        {
            // モックなので何もしない
        }
    }
    
    public class MockImageFactory : IImageFactory
    {
        public Task<IImage> CreateFromFileAsync(string filePath)
        {
            return Task.FromResult<IImage>(new MockImage(100, 100));
        }
        
        public Task<IImage> CreateFromBytesAsync(byte[] data)
        {
            return Task.FromResult<IImage>(new MockImage(100, 100));
        }
        
        public Task<IImage> CreateEmptyAsync(int width, int height, ImageFormat format = ImageFormat.Rgb24)
        {
            return Task.FromResult<IImage>(new MockImage(width, height, format));
        }
        
        public IImage CreateFromPlatformImage(object platformImage)
        {
            return new MockImage(100, 100);
        }
    }
}
```

### 7.2 単体テスト例

```csharp
namespace Baketa.Core.Tests.Imaging
{
    public class ImageTests
    {
        private readonly IImageFactory _imageFactory;
        
        public ImageTests()
        {
            _imageFactory = new MockImageFactory();
        }
        
        [Fact]
        public async Task ResizeAsync_ValidDimensions_ReturnsResizedImage()
        {
            // Arrange
            var originalImage = await _imageFactory.CreateEmptyAsync(100, 200);
            
            // Act
            var resizedImage = await originalImage.ResizeAsync(50, 100);
            
            // Assert
            Assert.NotNull(resizedImage);
            Assert.Equal(50, resizedImage.Width);
            Assert.Equal(100, resizedImage.Height);
        }
        
        [Fact]
        public async Task Clone_ReturnsExactCopy()
        {
            // Arrange
            var originalImage = await _imageFactory.CreateEmptyAsync(100, 200);
            
            // Act
            var clonedImage = originalImage.Clone();
            
            // Assert
            Assert.NotNull(clonedImage);
            Assert.Equal(originalImage.Width, clonedImage.Width);
            Assert.Equal(originalImage.Height, clonedImage.Height);
            Assert.Equal(originalImage.Format, clonedImage.Format);
            
            // 参照が異なることを確認
            Assert.NotSame(originalImage, clonedImage);
        }
        
        [Fact]
        public async Task CreateEmptyAsync_InvalidDimensions_ThrowsException()
        {
            // Arrange & Act & Assert
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await _imageFactory.CreateEmptyAsync(-1, 100));
                
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await _imageFactory.CreateEmptyAsync(100, -1));
        }
        
        // その他のテスト
    }
}
```

## 8. パフォーマンス最適化

### 8.1 メモリ管理

大きな画像データを扱う場合のメモリ最適化戦略：

1. **早期リソース解放**: 不要になった画像リソースを明示的にDisposeする
2. **ピクセルデータの共有**: 可能な場合、クローンではなく参照を共有する
3. **画像の適切なリサイズ**: 必要な解像度にダウンサンプリングして処理する
4. **ストリームベースの処理**: 大きな画像はストリームで扱い、完全なロードを避ける

```csharp
// メモリ最適化例
public class OptimizedImage : IImage
{
    private readonly byte[] _pixelData;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _ownsData; // データの所有権フラグ
    private bool _disposed;
    
    // 既存データを参照するコンストラクタ（データのコピーなし）
    public OptimizedImage(byte[] pixelData, int width, int height, bool ownsData = true)
    {
        _pixelData = pixelData;
        _width = width;
        _height = height;
        _ownsData = ownsData;
    }
    
    // 部分ビューを作成するメソッド（データのコピーなし）
    public IImage CreateView(Rectangle rectangle)
    {
        // 元の画像の一部を参照する新しいインスタンスを返す
        // 実際にはもう少し複雑な実装が必要
        return new OptimizedImage(_pixelData, rectangle.Width, rectangle.Height, false);
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsData)
            {
                // データの所有権がある場合のみリソースを解放
                // 必要に応じてネイティブリソースのクリーンアップなど
            }
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
    
    // その他の実装
}
```

### 8.2 並列処理

画像処理の高速化のための並列処理戦略：

```csharp
// 並列画像処理の例
public async Task<IImage> ApplyParallelFilterAsync(IImage image, IImageFilter filter)
{
    // 画像データの取得
    byte[] pixelData = await image.ToByteArrayAsync();
    int width = image.Width;
    int height = image.Height;
    
    // 処理するデータのサイズに基づいて並列度を決定
    int parallelism = Math.Min(Environment.ProcessorCount, height);
    
    // 処理結果を格納する配列
    byte[] resultData = new byte[pixelData.Length];
    
    // 並列処理の実行
    await Task.Run(() => {
        Parallel.For(0, parallelism, (workerId) => {
            // 各スレッドが担当する行の範囲を計算
            int rowsPerWorker = height / parallelism;
            int startRow = workerId * rowsPerWorker;
            int endRow = (workerId == parallelism - 1) ? height : startRow + rowsPerWorker;
            
            // 各行のピクセルを処理
            for (int y = startRow; y < endRow; y++)
            {
                int rowOffset = y * width * 3; // RGB (3バイト/ピクセル) を仮定
                
                for (int x = 0; x < width; x++)
                {
                    int pixelOffset = rowOffset + x * 3;
                    
                    // ピクセルの処理（簡略化）
                    resultData[pixelOffset] = ProcessPixel(pixelData[pixelOffset], filter);
                    resultData[pixelOffset + 1] = ProcessPixel(pixelData[pixelOffset + 1], filter);
                    resultData[pixelOffset + 2] = ProcessPixel(pixelData[pixelOffset + 2], filter);
                }
            }
        });
    });
    
    // 処理結果から新しい画像を作成
    return await _imageFactory.CreateFromBytesAsync(resultData);
}

// ピクセル処理（フィルター適用など）
private byte ProcessPixel(byte value, IImageFilter filter)
{
    // フィルターのピクセル処理ロジック
    return value; // 簡略化
}
```

## 9. DI登録例

```csharp
public static class ImageServiceExtensions
{
    public static IServiceCollection AddImageServices(this IServiceCollection services)
    {
        // コア画像サービスの登録
        services.AddSingleton<IImageFactory, CoreImageFactory>();
        services.AddSingleton<IOcrImageProcessor, OpenCvOcrImageProcessor>();
        
        // フィルタの登録
        services.AddTransient<IBinarizationFilter, BinarizationFilter>();
        services.AddTransient<IGaussianBlurFilter, GaussianBlurFilter>();
        services.AddTransient<IContrastFilter, ContrastFilter>();
        
        return services;
    }
}
```

## 10. まとめ

イメージ抽象化レイヤーは、Baketaアプリケーションにおける画像処理の基盤として重要な役割を果たします。プラットフォームに依存しないインターフェースを提供しながらも、必要に応じてプラットフォーム固有の最適化を取り入れることで、柔軟性と性能を両立しています。

適切な抽象化レベルと明確な責任分担により、拡張性とテスト容易性を確保し、高品質な画像処理機能を実現します。また、OCR処理など、上位レイヤーでの特殊な要件にも対応できる設計となっています。

イメージ抽象化レイヤーの主な特徴：

1. **階層的抽象化**: 基本的な画像機能から高度な処理まで適切な階層構造
2. **明確なインターフェース**: 適切に定義されたインターフェースとファクトリパターン
3. **パフォーマンス考慮**: メモリ管理や並列処理などの最適化戦略
4. **拡張性と柔軟性**: 新しい画像処理アルゴリズムやフィルターの追加が容易
5. **OCR特化機能**: OCR処理のための特殊な画像前処理機能