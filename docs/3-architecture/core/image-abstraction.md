# 画像処理抽象化レイヤー実装ドキュメント

*最終更新: 2025年4月11日*

## 概要

このドキュメントは、GTTプロジェクトの画像処理抽象化レイヤー（Issue #76）の設計と実装について説明します。この抽象化レイヤーは、`System.Drawing.Bitmap`と`Avalonia.Media.Imaging.Bitmap`の混在によるビルドエラーを解消し、クロスプラットフォーム対応を実現することを目的としています。

## 1. アーキテクチャ概要

### 1.1 全体構造

画像処理抽象化レイヤーは以下のコンポーネントで構成されています：

```
GTT.Core/
├── Interfaces/
│   └── Imaging/
│       ├── IGTTImage.cs          # 画像抽象化インターフェース
│       ├── ImageFileFormat.cs    # 画像形式列挙型
│       └── GTTColor.cs           # 色情報構造体
│
├── Common/
│   └── Imaging/
│       ├── SystemDrawingImage.cs  # System.Drawing.Bitmap用実装
│       ├── AvaloniaImage.cs       # Avalonia.Media.Imaging.Bitmap用実装
│       ├── SystemDrawingAdapter.cs # System.Drawing変換アダプター
│       ├── AvaloniaAdapter.cs      # Avalonia変換アダプター
│       └── GTTImageFactory.cs      # 画像作成ファクトリー
│
GTT.OCR/
└── OpenCV/
    ├── GTTImageExtensions.cs     # OpenCVとの連携拡張メソッド
    └── BitmapExtensions.cs       # レガシー互換性用拡張メソッド
```

### 1.2 デザインパターン

抽象化レイヤーでは以下のデザインパターンを活用しています：

- **アダプターパターン**: 異なる画像形式間の変換を提供
- **ファクトリーパターン**: 環境に応じた適切な実装の作成
- **抽象化と実装の分離**: インターフェースと実装を明確に分離

## 2. 主要コンポーネント

### 2.1 IGTTImage インターフェース

`IGTTImage`はプラットフォーム非依存の画像操作のための中核インターフェースです。

```csharp
public interface IGTTImage : IDisposable
{
    int Width { get; }
    int Height { get; }
    IDictionary<string, object> Metadata { get; }
    
    IGTTImage Clone();
    Task<byte[]> ToByteArrayAsync(ImageFileFormat format);
    Task<Stream> ToStreamAsync(ImageFileFormat format);
    Task<byte[]> GetPixelDataAsync();
    Task<GTTColor> GetPixelColorAsync(int x, int y);
}
```

**特徴：**
- 非同期処理を活用した高パフォーマンス設計
- プラットフォーム依存の型を参照していない
- 拡張可能なメタデータサポート
- 基本的な画像操作のすべての機能を包含

### 2.2 実装クラス

#### SystemDrawingImage

`System.Drawing.Bitmap`をラップする`IGTTImage`実装で、Windows環境で使用されます。

```csharp
[SupportedOSPlatform("windows")]
public class SystemDrawingImage(Bitmap bitmap, bool ownsImage = true) : IGTTImage
{
    private Bitmap _bitmap = ownsImage ? bitmap : new Bitmap(bitmap);
    // ...実装...
}
```

**特徴：**
- Windows固有のコードとして明示的にマーク
- リソース管理（IDisposable）の適切な実装
- プライマリコンストラクタを活用したモダンな実装
- 効率的なネイティブビットマップアクセス

#### AvaloniaImage

`Avalonia.Media.Imaging.Bitmap`をラップする`IGTTImage`実装で、クロスプラットフォーム対応です。

```csharp
public class AvaloniaImage : IGTTImage
{
    private Bitmap _bitmap;
    // ...実装...
    
    public AvaloniaImage(Bitmap bitmap, bool ownsImage = true)
    {
        // ...初期化...
    }
}
```

**特徴：**
- クロスプラットフォームで動作
- プラットフォーム依存コードが存在しない
- Avaloniaの制約に対応した実装

### 2.3 アダプタークラス

#### SystemDrawingAdapter

`System.Drawing.Bitmap`と`IGTTImage`間の変換を提供します。

```csharp
[SupportedOSPlatform("windows")]
public static class SystemDrawingAdapter
{
    public static IGTTImage FromNative(Bitmap bitmap, bool ownsImage = true);
    public static IGTTImage FromFile(string filePath);
    public static IGTTImage FromStream(Stream stream);
    public static async Task<Bitmap> ToNativeAsync(IGTTImage image);
    // ...その他のメソッド...
}
```

#### AvaloniaAdapter

`Avalonia.Media.Imaging.Bitmap`と`IGTTImage`間の変換を提供します。

```csharp
public static class AvaloniaAdapter
{
    public static IGTTImage FromNative(Bitmap bitmap, bool ownsImage = true);
    public static IGTTImage FromFile(string filePath);
    public static IGTTImage FromStream(Stream stream);
    public static async Task<Bitmap> ToNativeAsync(IGTTImage image);
    // ...その他のメソッド...
}
```

### 2.4 ファクトリークラス

`GTTImageFactory`は環境に応じて適切な`IGTTImage`実装を作成します。

```csharp
public static class GTTImageFactory
{
    public static IGTTImage Create(int width, int height);
    public static IGTTImage FromFile(string filePath);
    public static IGTTImage FromStream(Stream stream);
    public static IGTTImage FromByteArray(byte[] imageData);
    public static IGTTImage FromExisting(IGTTImage source);
}
```

**特徴：**
- 実行時プラットフォーム検出
- 統一されたファクトリーAPIの提供
- 複数のソースからの画像作成をサポート

### 2.5 OpenCV統合拡張メソッド

OpenCVとの連携を容易にする拡張メソッドを提供します。

```csharp
public static class GTTImageExtensions
{
    public static async Task<Mat> ToMatAsync(this IGTTImage image);
    public static async Task<IGTTImage> ToGTTImageAsync(this Mat mat);
}
```

**特徴：**
- OpenCVとの自然な統合
- クロスプラットフォーム対応
- 効率的な変換パスの提供

## 3. 使用方法

### 3.1 基本的な使用例

```csharp
// 新しい画像を作成
IGTTImage image = GTTImageFactory.Create(800, 600);

// ファイルから読み込み
IGTTImage fileImage = GTTImageFactory.FromFile("image.png");

// バイト配列からの読み込み
byte[] imageData = await File.ReadAllBytesAsync("image.jpg");
IGTTImage bytesImage = GTTImageFactory.FromByteArray(imageData);

// 画像の保存
byte[] pngData = await image.ToByteArrayAsync(ImageFileFormat.Png);
await File.WriteAllBytesAsync("output.png", pngData);

// リソース解放
image.Dispose();
fileImage.Dispose();
bytesImage.Dispose();
```

### 3.2 OpenCVとの連携

```csharp
// IGTTImageからOpenCV Matへの変換
IGTTImage image = GTTImageFactory.FromFile("input.png");
using Mat mat = await image.ToMatAsync();

// 画像処理
Cv2.GaussianBlur(mat, mat, new Size(5, 5), 0);

// 処理結果をIGTTImageに戻す
IGTTImage processedImage = await mat.ToGTTImageAsync();
```

### 3.3 プラットフォーム固有の処理

```csharp
// プラットフォーム固有の処理が必要な場合
if (OperatingSystem.IsWindows())
{
    // Windows固有のコード
    IGTTImage image = /* ... */;
    var bitmap = await SystemDrawingAdapter.ToNativeAsync(image);
    try
    {
        // System.Drawing.Bitmapを使用した処理
        // ...
    }
    finally
    {
        bitmap.Dispose();
    }
}
else
{
    // クロスプラットフォームコード
    IGTTImage image = /* ... */;
    var avaloniaBitmap = await AvaloniaAdapter.ToNativeAsync(image);
    try
    {
        // Avalonia.Media.Imaging.Bitmapを使用した処理
        // ...
    }
    finally
    {
        avaloniaBitmap.Dispose();
    }
}
```

## 4. パフォーマンス最適化

抽象化レイヤーには、パフォーマンスを最適化するための複数の手法が実装されています：

### 4.1 型認識による最適化

実装タイプを検出して最適なパスを選択する最適化が行われています。

```csharp
public static async Task<Bitmap> ToNativeAsync(IGTTImage image)
{
    // 既にSystemDrawingImageならば直接アクセス（効率的）
    if (image is SystemDrawingImage sysImage)
    {
        return new Bitmap(sysImage.GetNativeBitmap());
    }
    
    // 異なる型の場合は汎用変換（非効率だが互換性あり）
    // ...
}
```

### 4.2 非同期処理

画像処理の多くは非同期メソッドとして実装され、UIスレッドのブロックを回避しています。

```csharp
public async Task<byte[]> ToByteArrayAsync(ImageFileFormat format)
{
    ThrowIfDisposed();
    return await Task.Run(() =>
    {
        using var stream = new MemoryStream();
        _bitmap.Save(stream, GetImageFormat(format));
        return stream.ToArray();
    });
}
```

### 4.3 リソース管理

すべての実装クラスが`IDisposable`を実装し、適切にリソースを解放します。

```csharp
public void Dispose()
{
    Dispose(true);
    GC.SuppressFinalize(this);
}

protected virtual void Dispose(bool disposing)
{
    if (_disposed) return;

    if (disposing)
    {
        _bitmap?.Dispose();
        _bitmap = null!;
    }

    _disposed = true;
}
```

## 5. クロスプラットフォーム対応

### 5.1 プラットフォーム検出

適切な実装を選択するために一貫してプラットフォーム検出を使用しています。

```csharp
if (OperatingSystem.IsWindows())
{
    return CreateSystemDrawingImage(width, height);
}
else
{
    return CreateAvaloniaImage(width, height);
}
```

### 5.2 プラットフォーム固有コードの分離

Windows固有のコードは明示的に属性でマークされています。

```csharp
[SupportedOSPlatform("windows")]
public class SystemDrawingImage : IGTTImage
{
    // ...
}
```

### 5.3 ファクトリーによる抽象化

ファクトリーパターンにより、コードはプラットフォーム固有の実装の詳細から隔離されています。

```csharp
// クライアントコードはプラットフォームを気にする必要がない
IGTTImage image = GTTImageFactory.Create(800, 600);
```

## 6. メンテナンスと拡張性

### 6.1 新しい画像形式のサポート

`ImageFileFormat`列挙型に新しいフォーマットを追加し、各実装クラスでサポートを実装できます。

### 6.2 新しいプラットフォームのサポート

新しいプラットフォームをサポートするには：

1. プラットフォーム固有の`IGTTImage`実装クラスを作成
2. 対応するアダプタークラスを実装
3. `GTTImageFactory`のプラットフォーム検出ロジックを更新

### 6.3 メタデータサポート

`IGTTImage.Metadata`プロパティを使用して、カスタムメタデータを保存できます。

```csharp
// メタデータの設定
image.Metadata["Author"] = "GTT Developer";
image.Metadata["CreationDate"] = DateTime.Now;

// メタデータの取得
var author = image.Metadata.TryGetValue("Author", out var value) 
    ? value?.ToString() 
    : null;
```

## 7. まとめ

画像処理抽象化レイヤーの実装により、以下の成果が得られました：

1. **型競合の解消**: `System.Drawing.Bitmap`と`Avalonia.Media.Imaging.Bitmap`の混在によるビルドエラーの解消
2. **クロスプラットフォーム対応**: Windows以外のプラットフォームでの動作を可能にする基盤構築
3. **統合API**: 一貫した画像処理APIの提供
4. **効率的な実装**: 型認識による最適パスの選択で変換オーバーヘッドを最小化
5. **拡張性**: 新しいプラットフォームや技術への対応を容易にする設計

これにより、GTTプロジェクトは将来的なクロスプラットフォーム展開への準備が整いました。
