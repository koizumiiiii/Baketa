# Baketaプロジェクト 新インターフェース階層設計

**作成日**: 2025年4月20日  
**作成者**: BaketaプロジェクトチームID  
**関連Issue**: #2 改善: 名前空間構造設計と移行計画  
**ステータス**: ドラフト

## 1. 概要

このドキュメントは、Baketaプロジェクトの新しいインターフェース階層設計を定義します。インターフェースの責任範囲と継承関係を明確にし、将来の拡張性を確保することを目的としています。

## 2. 基本設計原則

### 2.1 インターフェース責任分離

1. **単一責任の原則**: 各インターフェースは明確に定義された単一の責任を持つ
2. **インターフェース分離の原則**: クライアントに不要なメソッドを強制しない
3. **依存関係逆転の原則**: 上位モジュールは下位モジュールに依存しない

### 2.2 階層設計の原則

1. **基本から特化への階層**: 汎用的な機能を基底インターフェースに、特化した機能を派生インターフェースに配置
2. **プラットフォーム依存の分離**: プラットフォーム依存の機能を専用のインターフェースに分離
3. **サービス層の抽象化**: ビジネスロジックをサービスインターフェースとして抽象化

## 3. 新しいインターフェース階層

### 3.1 画像処理関連インターフェース

```
IImageBase (基本画像プロパティ)
└── IImage (一般的な画像機能)
    └── IAdvancedImage (高度な画像処理)
```

#### 3.1.1 IImageBase

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
    }
}
```

#### 3.1.2 IImage

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
    }
}
```

#### 3.1.3 IAdvancedImage

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
    }
}
```

### 3.2 プラットフォーム関連インターフェース

#### 3.2.1 Windows画像インターフェース

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
}
```

#### 3.2.2 Windows画像ファクトリー

```csharp
namespace Baketa.Infrastructure.Platform.Abstractions
{
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

#### 3.2.3 キャプチャサービスインターフェース

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
```

#### 3.2.4 Windows固有のキャプチャサービス

```csharp
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

### 3.3 イベント関連インターフェース

#### 3.3.1 イベント基本インターフェース

```csharp
namespace Baketa.Core.Abstractions.Events
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
    }
}
```

#### 3.3.2 イベントハンドラーインターフェース

```csharp
namespace Baketa.Core.Abstractions.Events
{
    /// <summary>
    /// イベントハンドラインターフェース
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    public interface IEventHandler<in TEvent> where TEvent : IEvent
    {
        /// <summary>
        /// イベント処理
        /// </summary>
        /// <param name="event">イベント</param>
        Task HandleAsync(TEvent @event);
    }
}
```

#### 3.3.3 イベント集約インターフェース

```csharp
namespace Baketa.Core.Abstractions.Events
{
    /// <summary>
    /// イベント集約インターフェース
    /// </summary>
    public interface IEventAggregator
    {
        /// <summary>
        /// イベントの発行
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="event">イベント</param>
        Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent;
        
        /// <summary>
        /// イベントハンドラの登録
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="handler">ハンドラ</param>
        void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
        
        /// <summary>
        /// イベントハンドラの登録解除
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="handler">ハンドラ</param>
        void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
    }
}
```

### 3.4 サービスモジュールインターフェース

```csharp
namespace Baketa.Application.DI
{
    /// <summary>
    /// サービス登録モジュールインターフェース
    /// </summary>
    public interface IServiceModule
    {
        /// <summary>
        /// サービスの登録
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        void RegisterServices(IServiceCollection services);
    }
}
```

## 4. インターフェース間の相互作用

### 4.1 画像処理フロー

1. `IWindowsCaptureService` により画面がキャプチャされ、`IWindowsImage` が生成される
2. `WindowsImageAdapter` により `IWindowsImage` が `IImage` に変換される
3. アプリケーションサービスがこの `IImage` を処理する

### 4.2 イベント伝播フロー

1. アプリケーションサービスが `IEventAggregator` を通じてイベントを発行
2. 登録された `IEventHandler<TEvent>` がイベントを処理
3. UI層のハンドラーがユーザーインターフェースを更新

## 5. 実装のガイドライン

### 5.1 インターフェースの実装ガイドライン

1. メソッドの確実な動作を保証
2. 例外発生時の適切なハンドリング
3. 非同期メソッドの適切な実装
4. リソースの適切な解放

### 5.2 インターフェース間のアダプター実装

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

## 6. インターフェース設計の検証

### 6.1 確認するべき点

1. インターフェースが単一責任に従っているか
2. プラットフォーム依存部分が適切に分離されているか
3. インターフェース間の依存関係が明確か
4. 将来の拡張に対応できる柔軟性があるか
5. 適切なドキュメントコメントが付与されているか

### 6.2 メリット

1. **責任の明確な分離**: 各インターフェースの責任範囲が明確
2. **拡張性の向上**: 階層構造により機能拡張がしやすい
3. **プラットフォーム分離**: Windows固有コードとの明確な境界
4. **テスト容易性**: インターフェースに対するモックが容易

## 7. 将来の拡張ポイント

1. **高度な画像処理**: `IAdvancedImage` インターフェースの拡張
2. **プラットフォーム追加**: 将来的な他プラットフォーム対応（予定はないが設計上の可能性として）
3. **新しいサービスの追加**: イベントシステムを活用したサービス拡張

## 8. 次のステップ

1. 詳細なインターフェース設計レビュー
2. インターフェース間の依存関係の確認
3. 移行計画の詳細化
4. 実装の優先順位付け

## 9. 付録

### 9.1 移行マッピング表

| 現在のインターフェース | 新しいインターフェース | 備考 |
|----------------------|---------------------|------|
| `Baketa.Core.Interfaces.Image.IImage` | `Baketa.Core.Abstractions.Imaging.IImage` | 責任を分割 |
| `Baketa.Core.Interfaces.Platform.IScreenCapturer` | `Baketa.Infrastructure.Platform.Abstractions.IWindowsCaptureService` | Windows依存を明確化 |
| `Baketa.Core.Interfaces.Platform.IWindowManager` | `Baketa.Infrastructure.Platform.Abstractions.IWindowManager` | 名前空間のみ移動 |

### 9.2 プロジェクト構造との整合性

新しいインターフェース構造は、「improved-architecture.md」で定義されたプロジェクト構造と整合性があります。各インターフェースは適切なプロジェクトと名前空間に配置されます。
