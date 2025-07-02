# 画像処理フィルター設計と実装

## 概要

Baketaのゲーム画面のOCR処理の前処理として使用される画像処理フィルターの設計とアーキテクチャについて説明します。画像処理フィルターは、テキスト認識の精度向上のために画像を最適化する役割を担います。

## アーキテクチャ

### 抽象化レイヤー

画像処理フィルターは以下の抽象化レイヤーで構成されています：

1. **IImageFilter インターフェース**：すべてのフィルターの基本インターフェース
2. **ImageFilterBase 抽象クラス**：フィルターの共通実装を提供する基底クラス
3. **具体的なフィルター実装**：各種画像処理アルゴリズムの実装
4. **FilterChain**：複数のフィルターを連結するためのコンポジットパターン実装

### フィルターカテゴリ

フィルターは以下のカテゴリに分類されます：

- **ColorAdjustment**：色調変換（グレースケール、明度・コントラスト調整など）
- **Blur**：ぼかし・ノイズ除去（ガウシアンぼかしなど）
- **EdgeDetection**：エッジ検出（ソーベル、Cannyなど）
- **Threshold**：二値化処理（単純二値化、適応的二値化など）
- **Morphology**：形態学的処理（膨張、収縮など）
- **Sharpen**：シャープ化
- **Effect**：特殊効果
- **Composite**：複合フィルター

## 主要コンポーネント

### IImageFilter インターフェース

```csharp
public interface IImageFilter
{
    string Name { get; }
    string Description { get; }
    FilterCategory Category { get; }
    
    Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage);
    void ResetParameters();
    IDictionary<string, object> GetParameters();
    void SetParameter(string name, object value);
    bool SupportsFormat(ImageFormat format);
    ImageInfo GetOutputImageInfo(IAdvancedImage inputImage);
}
```

### ImageEnhancementOptions

フィルター設定を統合的に管理するクラスです：

```csharp
public class ImageEnhancementOptions
{
    public float Brightness { get; set; }
    public float Contrast { get; set; }
    public float Sharpness { get; set; }
    public float NoiseReduction { get; set; }
    public int BinarizationThreshold { get; set; }
    public bool UseAdaptiveThreshold { get; set; }
    public int AdaptiveBlockSize { get; set; }
    public bool OptimizeForTextDetection { get; set; }
}
```

### FilterChain（フィルターチェーン）

複数のフィルターを連結して一連の処理として適用するためのコンポジットパターン実装：

```csharp
public class FilterChain : IImageFilter
{
    private readonly List<IImageFilter> _filters = new();
    
    public void AddFilter(IImageFilter filter) { ... }
    public void RemoveFilter(IImageFilter filter) { ... }
    public IImageFilter GetFilter(int index) { ... }
    public IImageFilter GetFilterByName(string name) { ... }
    public async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage) { ... }
}
```

## 実装済みフィルター

- **GrayscaleFilter**：グレースケール変換
- **BrightnessContrastFilter**：明度・コントラスト調整
- **GaussianBlurFilter**：ガウシアンぼかし
- **ThresholdFilter**：単純二値化
- **AdaptiveThresholdFilter**：適応的二値化

## 実装予定のフィルター

- **MedianBlurFilter**：メディアンぼかし
- **SobelEdgeFilter**：ソーベルエッジ検出
- **CannyEdgeFilter**：Cannyエッジ検出
- **MorphologyFilter**：形態学的処理（膨張、収縮）

## 使用例

### 単一フィルターの適用

```csharp
// グレースケール変換フィルターの作成
var grayscaleFilter = new GrayscaleFilter();

// 画像にフィルターを適用
var processedImage = await inputImage.ApplyFilterAsync(grayscaleFilter);
```

### フィルターチェーンの使用

```csharp
// フィルターチェーンの作成
var filterChain = new FilterChain();

// フィルターの追加
filterChain.AddFilter(new GrayscaleFilter());
filterChain.AddFilter(new GaussianBlurFilter());
filterChain.AddFilter(new AdaptiveThresholdFilter());

// フィルターチェーンを一括適用
var processedImage = await inputImage.ApplyFilterAsync(filterChain);
```

### パラメータの設定

```csharp
// 二値化フィルターの作成
var thresholdFilter = new ThresholdFilter();

// パラメータの設定
thresholdFilter.SetParameter("Threshold", 128);
thresholdFilter.SetParameter("MaxValue", 255);

// フィルターの適用
var binaryImage = await inputImage.ApplyFilterAsync(thresholdFilter);
```

## OCR最適化プロファイル

OCR処理のための推奨フィルターチェーン設定：

1. **グレースケール変換**：カラー情報を削除して処理を単純化
2. **ガウシアンぼかし**：ノイズの軽減（軽度のぼかし）
3. **コントラスト強調**：テキストと背景のコントラスト向上
4. **適応的二値化**：テキスト領域の抽出

これらのフィルターを組み合わせることで、多くのゲーム画面のテキスト認識精度が向上します。ただし、ゲームの特性に応じてカスタマイズが必要な場合があります。

## テスト

フィルターの単体テストは `Baketa.Core.Tests.Imaging.ImageFilterTests` クラスで実装されています。各フィルターの基本機能と、フィルターチェーンの動作が検証されています。

## 関連イシュー

- Issue #30: 画像処理フィルターの抽象化
- Issue #40: OpenCVベースのOCR前処理最適化
- Issue #42: 画像前処理パイプラインの設計と実装

## 今後の拡張

- 追加フィルターの実装（メディアンぼかし、エッジ検出など）
- OpenCVベースの高度なフィルター実装
- フィルター設定のUI実装
- ゲームプロファイルに基づくフィルター設定の自動選択