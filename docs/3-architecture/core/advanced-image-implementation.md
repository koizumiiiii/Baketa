# IAdvancedImage インターフェース実装仕様

## 1. 概要

Baketaプロジェクトでは、OCR処理の強化を目的として`IAdvancedImage`インターフェースを設計・実装しました。このインターフェースは基本的な`IImage`を拡張し、より高度な画像処理機能を提供します。

## 2. インターフェース設計

### 2.1 主要インターフェース

```csharp
public interface IAdvancedImage : IImage
{
    // ピクセル操作
    Color GetPixel(int x, int y);
    void SetPixel(int x, int y, Color color);
    
    // フィルター処理
    Task<IAdvancedImage> ApplyFilterAsync(IImageFilter filter);
    Task<IAdvancedImage> ApplyFiltersAsync(IEnumerable<IImageFilter> filters);
    
    // 画像分析
    Task<int[]> ComputeHistogramAsync(ColorChannel channel = ColorChannel.Luminance);
    
    // 画像変換
    Task<IAdvancedImage> ToGrayscaleAsync();
    Task<IAdvancedImage> ToBinaryAsync(byte threshold);
    Task<IAdvancedImage> ExtractRegionAsync(Rectangle rectangle);
    Task<IAdvancedImage> RotateAsync(float degrees);
    
    // OCR固有処理
    Task<IAdvancedImage> OptimizeForOcrAsync();
    Task<IAdvancedImage> OptimizeForOcrAsync(OcrImageOptions options);
    
    // 画像分析
    Task<float> CalculateSimilarityAsync(IImage other);
    Task<float> EvaluateTextProbabilityAsync(Rectangle rectangle);
}
```

### 2.2 関連インターフェース

```csharp
// 画像フィルタインターフェース
public interface IImageFilter
{
    string Name { get; }
    string Description { get; }
    IReadOnlyDictionary<string, object> Parameters { get; }
    IReadOnlyList<byte> Apply(IReadOnlyList<byte> imageData, int width, int height, int stride);
}

// OCR最適化オプション
public class OcrImageOptions
{
    public float ContrastEnhancement { get; set; } = 1.0f;
    public float NoiseReduction { get; set; } = 0.0f;
    public int BinarizationThreshold { get; set; } = 128;
    public bool UseAdaptiveThreshold { get; set; } = false;
    public float SharpnessEnhancement { get; set; } = 0.0f;
    public bool DetectAndCorrectOrientation { get; set; } = false;
}

// カラーチャンネル定義
public enum ColorChannel
{
    Red,
    Green,
    Blue,
    Alpha,
    Luminance
}

// 画像フォーマット定義
public enum ImageFormat
{
    Rgb24,
    Rgba32,
    Grayscale8
}
```

## 3. 実装クラス

### 3.1 AdvancedImage クラス

`AdvancedImage`は`CoreImage`を拡張し、純粋な.NET実装として動作します：

- **メモリ効率**: バイトバッファをハンドル
- **マルチスレッド対応**: 長時間処理に`Task`を活用
- **拡張性**: 複数のフィルターをチェーン可能
- **IDisposable実装**: リソース管理の最適化

### 3.2 WindowsImageAdapter クラス

Windowsプラットフォーム固有の実装のためのアダプター：

- **System.Drawing.Bitmap**と`IAdvancedImage`間のブリッジ
- **高速処理**: BitmapのLockBitsを活用したピクセル操作
- **プラットフォーム最適化**: Windows APIを活用
- **アダプター責務**: インターフェース変換と型変換を担当

## 4. 設計上の注意点

### 4.1 コレクションインターフェースの使用

- **IReadOnlyList&lt;byte&gt;**: 配列返却に関するCA1819警告対応
- **変換方法**: `.ToArray()`メソッドで明示的に配列に変換
- **パフォーマンス考慮**: 内部実装では`byte[]`を使用し、公開メソッドでのみ`IReadOnlyList<byte>`を使用

### 4.2 非同期処理

- **UI応答性確保**: 計算負荷の高い処理は非同期で実行
- **ConfigureAwait(false)**: UI依存性を回避
- **キャンセレーション対応**: 将来的な拡張で対応予定

### 4.3 エラー処理

- **引数検証**: すべての公開メソッドで引数を検証
- **例外明示**: 適切な例外種類と明確なメッセージの使用
- **境界チェック**: 座標やサイズの範囲検証

## 5. 使用例

```csharp
// 基本使用例
IAdvancedImage image = new AdvancedImage(imageData, width, height, ImageFormat.Rgb24);

// グレースケール変換
IAdvancedImage grayImage = await image.ToGrayscaleAsync();

// フィルター適用
IImageFilter binarizationFilter = new BinarizationFilter(128);
IAdvancedImage binaryImage = await grayImage.ApplyFilterAsync(binarizationFilter);

// OCR最適化
var options = new OcrImageOptions
{
    ContrastEnhancement = 1.2f,
    NoiseReduction = 0.3f,
    BinarizationThreshold = 150
};
IAdvancedImage ocrImage = await image.OptimizeForOcrAsync(options);
```

## 6. 今後の拡張予定

1. **適応的二値化**: 照明条件に応じた二値化処理
2. **領域分析**: 画像内のテキスト領域自動検出
3. **画像変形補正**: 歪み・傾き補正
4. **パフォーマンス最適化**: SIMD命令活用による並列処理

## 7. 実装上の考慮事項

1. **メモリ使用量**: 大きな画像処理時のメモリ最適化
2. **プラットフォーム依存性**: Windows以外への拡張可能性
3. **エッジケース処理**: 異常な画像入力への堅牢な対応
4. **テスト容易性**: モック可能な設計による単体テスト

## 8. 単体テスト

### 8.1 テスト設計方針

- **モッククラスによるテスト**: インターフェース実装のモッククラスによる検証
- **境界値テスト**: 無効な引数や範囲外の値に対する動作検証
- **非同期メソッドのテスト**: 非同期メソッドの返値とライフサイクルの検証
- **コードパスカバレッジ**: すべてのインターフェースメソッドをカバー

### 8.2 テスト実装構成

1. **AdvancedImageTests.cs**: IAdvancedImageインターフェースの各メソッドテスト
   - GetPixel/SetPixelのパラメータ検証
   - 画像変換メソッドの実行確認
   - 非同期操作の動作検証

2. **OcrImageOptionsTests.cs**: OcrImageOptionsクラスのテスト
   - デフォルト値の検証
   - プリセット作成機能のテスト
   - 無効なプリセット指定時の例外検証

3. **ImageFilterTests.cs**: IImageFilterインターフェースの実装テスト
   - フィルター適用の動作確認
   - エッジケース（空データ、nullなど）の処理検証
   
### 8.3 コード品質の改善

実装時に次のコード品質改善を行いました：

- **未使用パラメーターの最適化**: アンダースコア付き連番(`_1`, `_2`, `_3`)を用いたディスカードパラメーターの明示的な指定
- **最新のコレクション初期化構文の導入**: C# 12の `[]` 構文を活用とLINQ式の活用
- **単体テストの可読性向上**: 簡潔で明示的なテストコードの実装

これらの改善により、メンテナンス性とコード品質が向上し、IDE警告（IDE0060、IDE0300、IDE0301、IDE0305）を解消しました。
