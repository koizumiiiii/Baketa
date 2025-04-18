# IGTTImage 実装ガイド

*最終更新: 2025年4月11日*

このガイドは、GTTプロジェクトの画像処理抽象化レイヤー（Issue #76）の実装方法について説明します。新たにIGTTImage抽象化レイヤーを使用するためのベストプラクティスと、既存コードの移行方法を提供します。

## 1. IGTTImage 抽象化レイヤーの概要

IGTTImage抽象化レイヤーは、System.Drawing.BitmapとAvalonia.Media.Imaging.Bitmapの混在による問題を解決し、クロスプラットフォーム対応を実現するためのコンポーネントです。

### 1.1 主要コンポーネント

- **IGTTImage**: プラットフォーム非依存の画像操作インターフェース
- **SystemDrawingImage**: System.Drawing.Bitmap用の実装（Windows限定）
- **AvaloniaImage**: Avalonia.Media.Imaging.Bitmap用の実装（クロスプラットフォーム）
- **アダプター**: 各プラットフォーム固有の型と抽象化レイヤー間の変換
- **GTTImageFactory**: 適切な実装を作成するファクトリー

## 2. コード移行ガイド

### 2.1 既存のBitmap参照の置き換え

既存のコードでSystem.Drawing.BitmapやAvalonia.Media.Imaging.Bitmapを直接参照している箇所を特定し、IGTTImageに移行します。

#### Before（移行前）:

```csharp
// System.Drawingの直接使用
using System.Drawing;

public class ImageProcessor
{
    public Bitmap ProcessImage(Bitmap input)
    {
        // 処理ロジック
        return processedBitmap;
    }
}
```

#### After（移行後）:

```csharp
// プラットフォーム非依存の抽象化を使用
using GTT.Core.Interfaces.Imaging;

public class ImageProcessor
{
    public async Task<IGTTImage> ProcessImageAsync(IGTTImage input)
    {
        // 処理ロジック
        return processedImage;
    }
}
```

### 2.2 既存のメソッドでIGTTImageを使用する例

#### 画像の読み込みと保存:

```csharp
// Before:
public void SaveScreenshot(Bitmap screenshot, string path)
{
    screenshot.Save(path, ImageFormat.Png);
}

// After:
public async Task SaveScreenshotAsync(IGTTImage screenshot, string path)
{
    byte[] data = await screenshot.ToByteArrayAsync(ImageFileFormat.Png);
    await File.WriteAllBytesAsync(path, data);
}
```

#### ピクセルデータの取得:

```csharp
// Before (System.Drawing):
public Color GetPixelColor(Bitmap bitmap, int x, int y)
{
    return bitmap.GetPixel(x, y);
}

// After:
public async Task<GTTColor> GetPixelColorAsync(IGTTImage image, int x, int y)
{
    return await image.GetPixelColorAsync(x, y);
}
```

### 2.3. プラットフォーム固有のコードの処理

プラットフォーム固有の機能が必要な場合は、アダプターを使用してプラットフォーム固有の型に変換します。

```csharp
// プラットフォーム固有の機能が必要な場合
public async Task<IGTTImage> SpecialProcessingAsync(IGTTImage image)
{
    if (OperatingSystem.IsWindows())
    {
        // Windows固有の処理
        var bitmap = await SystemDrawingAdapter.ToNativeAsync(image);
        try
        {
            // System.Drawing.Bitmapの機能を使用
            using (var graphics = Graphics.FromImage(bitmap))
            {
                // 特別な描画処理
            }
            
            // 処理結果をIGTTImageに戻す
            return SystemDrawingAdapter.FromNative(bitmap, true);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
    else
    {
        // クロスプラットフォームの代替実装
        // ...
        return image;
    }
}
```

## 3. API リファレンス

### 3.1 IGTTImage インターフェース

```csharp
/// <summary>
/// プラットフォーム非依存の画像操作インターフェース
/// </summary>
public interface IGTTImage : IDisposable
{
    /// <summary>画像の幅（ピクセル単位）</summary>
    int Width { get; }
    
    /// <summary>画像の高さ（ピクセル単位）</summary>
    int Height { get; }
    
    /// <summary>画像のメタデータ</summary>
    IDictionary<string, object> Metadata { get; }
    
    /// <summary>画像のクローンを作成</summary>
    IGTTImage Clone();
    
    /// <summary>画像をバイト配列に変換</summary>
    Task<byte[]> ToByteArrayAsync(ImageFileFormat format);
    
    /// <summary>画像をストリームに変換</summary>
    Task<Stream> ToStreamAsync(ImageFileFormat format);
    
    /// <summary>RGBA形式のピクセルデータを取得</summary>
    Task<byte[]> GetPixelDataAsync();
    
    /// <summary>特定位置のピクセル色を取得</summary>
    Task<GTTColor> GetPixelColorAsync(int x, int y);
}
```

### 3.2 GTTImageFactory メソッド

```csharp
/// <summary>空のIGTTImageを作成</summary>
public static IGTTImage Create(int width, int height);

/// <summary>ファイルからIGTTImageを作成</summary>
public static IGTTImage FromFile(string filePath);

/// <summary>ストリームからIGTTImageを作成</summary>
public static IGTTImage FromStream(Stream stream);

/// <summary>バイト配列からIGTTImageを作成</summary>
public static IGTTImage FromByteArray(byte[] imageData);

/// <summary>別のIGTTImageからコピーを作成</summary>
public static IGTTImage FromExisting(IGTTImage source);
```

### 3.3 アダプターメソッド

```csharp
// SystemDrawingAdapter (Windows限定)
public static IGTTImage FromNative(Bitmap bitmap, bool ownsImage = true);
public static async Task<Bitmap> ToNativeAsync(IGTTImage image);

// AvaloniaAdapter
public static IGTTImage FromNative(Bitmap bitmap, bool ownsImage = true);
public static async Task<Bitmap> ToNativeAsync(IGTTImage image);
```

## 4. ベストプラクティス

### 4.1 リソース管理

IGTTImageはIDisposableを実装しているため、適切にリソースを解放することが重要です。

```csharp
// 推奨: using文の使用
using (IGTTImage image = GTTImageFactory.Create(800, 600))
{
    // 処理...
}

// または
using IGTTImage image = GTTImageFactory.Create(800, 600);
// 処理...
```

### 4.2 非同期パターンの活用

IGTTImageの多くのメソッドは非同期で、UIスレッドのブロックを回避します。

```csharp
// 推奨: await キーワードの使用
public async Task ProcessAsync()
{
    IGTTImage image = GTTImageFactory.FromFile("input.png");
    using var processedImage = await ProcessImageAsync(image);
    byte[] data = await processedImage.ToByteArrayAsync(ImageFileFormat.Png);
    await File.WriteAllBytesAsync("output.png", data);
}
```

### 4.3 クロスプラットフォームコードの記述

クロスプラットフォーム対応コードを書く場合は、プラットフォーム固有の実装への依存を避けます。

```csharp
// 非推奨: プラットフォーム固有の型への依存
if (image is SystemDrawingImage)
{
    // Windows固有のコード...
}

// 推奨: プラットフォーム検出
if (OperatingSystem.IsWindows())
{
    // Windows固有のコード...
}
else
{
    // クロスプラットフォームコード...
}
```

### 4.4 スレッドセーフティ

IGTTImageの実装はスレッドセーフではないため、複数のスレッドから同じインスタンスに同時アクセスしないようにします。

```csharp
// 非推奨: 同じインスタンスへの複数スレッドからの同時アクセス
IGTTImage sharedImage = GTTImageFactory.FromFile("input.png");
Parallel.ForEach(items, item => 
{
    Process(sharedImage); // 複数スレッドから同時アクセスは危険
});

// 推奨: 各スレッドが独自のコピーを使用
IGTTImage templateImage = GTTImageFactory.FromFile("input.png");
Parallel.ForEach(items, item => 
{
    using IGTTImage localCopy = templateImage.Clone();
    Process(localCopy); // 各スレッドが独自のコピーを使用
});
```

## 5. コード例

### 5.1 基本的な画像処理

```csharp
public async Task<IGTTImage> ApplyGrayscaleFilterAsync(string inputPath, string outputPath)
{
    // 画像読み込み
    IGTTImage image = GTTImageFactory.FromFile(inputPath);
    
    try
    {
        // OpenCVを使った処理
        using var mat = await image.ToMatAsync();
        Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(mat, mat, ColorConversionCodes.GRAY2BGR);
        
        // 結果をIGTTImageに変換
        IGTTImage result = await mat.ToGTTImageAsync();
        
        // 保存
        byte[] data = await result.ToByteArrayAsync(ImageFileFormat.Png);
        await File.WriteAllBytesAsync(outputPath, data);
        
        return result;
    }
    finally
    {
        image.Dispose();
    }
}
```

### 5.2 メタデータの使用

```csharp
public async Task AddMetadataAndSaveAsync(IGTTImage image, string outputPath)
{
    // メタデータ追加
    image.Metadata["ProcessedBy"] = "GTT Application";
    image.Metadata["ProcessingDate"] = DateTime.Now;
    
    // OCR結果も保存できる
    image.Metadata["OcrText"] = "検出されたテキスト";
    
    // 保存
    byte[] data = await image.ToByteArrayAsync(ImageFileFormat.Png);
    await File.WriteAllBytesAsync(outputPath, data);
}
```

### 5.3 画像の合成

```csharp
public async Task<IGTTImage> OverlayImagesAsync(IGTTImage background, IGTTImage foreground)
{
    // OpenCVを使って合成
    using var bgMat = await background.ToMatAsync();
    using var fgMat = await foreground.ToMatAsync();
    
    // サイズを合わせる
    if (bgMat.Size() != fgMat.Size())
    {
        Cv2.Resize(fgMat, fgMat, bgMat.Size());
    }
    
    // アルファブレンド
    using var result = new Mat();
    Cv2.AddWeighted(bgMat, 0.7, fgMat, 0.3, 0, result);
    
    // 結果をIGTTImageに変換
    return await result.ToGTTImageAsync();
}
```

## 6. トラブルシューティング

### 6.1 よくある問題

#### 画像が空または壊れている

**症状**: `IGTTImage`のプロパティにアクセスすると`ObjectDisposedException`が発生する。

**原因**: 画像が既に`Dispose`されている。

**解決策**: 
- `using`ブロックの外で`IGTTImage`にアクセスしないようにする。
- 非同期メソッドでは`await`を使用して完了を待つ。

```csharp
// 問題のあるコード
IGTTImage image = GTTImageFactory.FromFile("input.png");
image.Dispose();
int width = image.Width; // ObjectDisposedException

// 修正
using (IGTTImage image = GTTImageFactory.FromFile("input.png"))
{
    int width = image.Width; // OK
    // 処理...
}
```

#### プラットフォーム依存の例外

**症状**: Windows以外のプラットフォームで`PlatformNotSupportedException`が発生する。

**原因**: Windows固有のコードを他のプラットフォームで実行しようとしている。

**解決策**:
- プラットフォーム検出を使用して適切なコードパスを選択する。
- クロスプラットフォームの代替実装を提供する。

```csharp
// 修正例
if (OperatingSystem.IsWindows())
{
    // Windows固有のコード
}
else
{
    // クロスプラットフォームの代替処理
}
```

### 6.2 デバッグとロギング

問題を診断するために、以下のようなメタデータを使用できます：

```csharp
// 画像情報のログ出力
private void LogImageInfo(IGTTImage image, ILogger logger)
{
    logger.LogInformation(
        "画像情報: 幅={Width}, 高さ={Height}, 実装={Implementation}",
        image.Width,
        image.Height,
        image.GetType().Name);
        
    // メタデータの出力
    foreach (var entry in image.Metadata)
    {
        logger.LogDebug("メタデータ: {Key}={Value}", entry.Key, entry.Value);
    }
}
```

## 7. 移行チェックリスト

既存のコードを`IGTTImage`に移行する際のチェックリスト：

### 7.1 クラスの移行

- [ ] `using System.Drawing;` または `using Avalonia.Media.Imaging;` の依存を特定
- [ ] `using GTT.Core.Interfaces.Imaging;` に置き換え
- [ ] `Bitmap` 型を `IGTTImage` に変更
- [ ] 同期メソッドを非同期パターンに変更 (`Async` サフィックスを追加)
- [ ] リソース解放のために `using` ステートメントを追加

### 7.2 メソッドの移行

- [ ] `Bitmap` パラメーターを `IGTTImage` に変更
- [ ] 戻り値の型を必要に応じて `IGTTImage` に変更
- [ ] プラットフォーム固有のコードを `OperatingSystem.IsWindows()` で条件分岐
- [ ] `Save` メソッドを `ToByteArrayAsync` + `File.WriteAllBytesAsync` に変更
- [ ] `GetPixel` などの直接メソッドを対応する非同期バージョンに変更

### 7.3 テスト

- [ ] Windows環境で正常に動作することを確認
- [ ] 可能であれば Linux/macOS 環境でもテスト
- [ ] パフォーマンス劣化がないことを確認
- [ ] メモリリークがないことを確認（Dispose呼び出しの確認）

## 8. よくある質問

### Q: IGTTImageを使うとパフォーマンスは低下しませんか？

**A**: 適切に実装されている場合、パフォーマンスへの影響は最小限です。`SystemDrawingAdapter.ToNativeAsync`などのメソッドには、型を認識して効率的な変換パスを選択する最適化が組み込まれています。同じ型の間での変換は高速です。

### Q: プラットフォーム固有の機能にアクセスする方法は？

**A**: アダプターを使用してプラットフォーム固有の型に変換し、処理後に再び`IGTTImage`に戻します。

```csharp
if (OperatingSystem.IsWindows())
{
    var bitmap = await SystemDrawingAdapter.ToNativeAsync(image);
    try
    {
        // プラットフォーム固有の処理
        return SystemDrawingAdapter.FromNative(bitmap);
    }
    finally
    {
        bitmap.Dispose();
    }
}
```

### Q: IGTTImageは外部ライブラリとの連携に使えますか？

**A**: はい。OpenCVとの連携のための拡張メソッド（`ToMatAsync`/`ToGTTImageAsync`）が提供されています。他のライブラリとの連携にも同様のパターンを適用できます。

### Q: 非同期メソッドは常に必要ですか？

**A**: 大きな画像を扱う場合には非同期メソッドを使用することをお勧めします。UIスレッドのブロックを避け、アプリケーションの応答性を維持できます。ただし、小さな画像や非UIコンテキストでは、必要に応じて同期メソッド（`.GetAwaiter().GetResult()`を使用）も選択できます。

## 9. IGTTImageによる画像処理アルゴリズムの実装例

### 9.1 画像フィルタの実装

```csharp
public async Task<IGTTImage> ApplyBlurFilterAsync(IGTTImage image, int kernelSize)
{
    using var mat = await image.ToMatAsync();
    
    // ガウスぼかしフィルタ適用
    Cv2.GaussianBlur(mat, mat, new Size(kernelSize, kernelSize), 0);
    
    return await mat.ToGTTImageAsync();
}
```

### 9.2 画像解析

```csharp
public async Task<double> CalculateAverageBrightnessAsync(IGTTImage image)
{
    using var mat = await image.ToMatAsync();
    using var grayMat = new Mat();
    
    // グレースケール変換
    Cv2.CvtColor(mat, grayMat, ColorConversionCodes.BGR2GRAY);
    
    // 平均輝度を計算
    Scalar meanValue = Cv2.Mean(grayMat);
    return meanValue.Val0; // グレースケール画像の場合、Val0が平均輝度
}
```

### 9.3 テキスト描画

```csharp
public async Task<IGTTImage> DrawTextAsync(
    IGTTImage image, 
    string text, 
    int x, int y, 
    GTTColor color, 
    double fontScale = 1.0)
{
    using var mat = await image.ToMatAsync();
    
    // OpenCVでテキスト描画
    var position = new Point(x, y);
    var fontFace = HersheyFonts.HersheySimplex;
    var scalar = new Scalar(color.B, color.G, color.R, color.A);
    
    Cv2.PutText(mat, text, position, fontFace, fontScale, scalar);
    
    return await mat.ToGTTImageAsync();
}
```

## 10. まとめ

`IGTTImage`抽象化レイヤーの導入により、GTTプロジェクトは以下の利点を得ました：

1. **クロスプラットフォーム対応**: Windows、Linux、macOSで動作可能な画像処理基盤
2. **型競合の解消**: System.DrawingとAvaloniaの画像型の混在問題を解決
3. **一貫したAPI**: プラットフォームに依存しない統一されたインターフェース
4. **拡張性**: 新しいプラットフォームや実装の追加が容易
5. **最適化**: 型認識による効率的な変換パスの提供

適切なパターンとベストプラクティスに従うことで、`IGTTImage`抽象化レイヤーを効果的に活用し、クロスプラットフォーム対応の高品質なアプリケーションを開発できます。
