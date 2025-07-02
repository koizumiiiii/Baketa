# Baketa 名前空間移行プロセス

**作成日**: 2025年4月20日  
**作成者**: BaketaプロジェクトチームID  
**関連Issue**: #2 改善: 名前空間構造設計と移行計画  
**ステータス**: ドラフト

## 1. 概要

このドキュメントは、Baketaプロジェクトの名前空間とインターフェース構造を移行するための具体的なプロセスとガイドラインを提供します。

## 2. 移行の基本方針

1. **段階的アプローチ**: 一度にすべてを移行するのではなく、段階的に進める
2. **互換性の維持**: 移行中も既存コードの動作を維持する
3. **テスト駆動**: 各ステップで単体テストを実行し、機能の維持を確認する
4. **チーム連携**: 移行状況を常に共有し、チーム全体で一貫性を保つ

## 3. 移行の大まかな流れ

1. 新しいインターフェース定義の作成
2. アダプターの実装
3. 実装クラスの作成/移行
4. 既存コードの参照更新
5. 古いインターフェースの非推奨化
6. 完全移行の検証

## 4. 詳細なステップと手順

### 4.1 Step 1: 新しいインターフェース定義の作成

#### 4.1.1 ディレクトリ構造の準備

```powershell
# Core層の抽象化ディレクトリ作成
mkdir -p Baketa.Core/Abstractions/Imaging
mkdir -p Baketa.Core/Abstractions/Capture
mkdir -p Baketa.Core/Abstractions/Translation
mkdir -p Baketa.Core/Abstractions/Events
mkdir -p Baketa.Core/Abstractions/Common

# Infrastructure層の抽象化ディレクトリ作成
mkdir -p Baketa.Infrastructure/Abstractions
mkdir -p Baketa.Infrastructure/Platform/Abstractions
mkdir -p Baketa.Infrastructure/Platform/Windows/Adapters
```

#### 4.1.2 基本インターフェースの作成

例: `IImageBase` インターフェースの作成

```csharp
// Baketa.Core/Abstractions/Imaging/IImageBase.cs
using System;
using System.Threading.Tasks;

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

#### 4.1.3 派生インターフェースの作成

例: `IImage` インターフェースの作成

```csharp
// Baketa.Core/Abstractions/Imaging/IImage.cs
using System.Threading.Tasks;

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

#### 4.1.4 プラットフォーム固有インターフェースの作成

例: `IWindowsImage` インターフェースの作成

```csharp
// Baketa.Infrastructure/Platform/Abstractions/IWindowsImage.cs
using System;
using System.Drawing;
using System.Threading.Tasks;

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

### 4.2 Step 2: アダプターの実装

#### 4.2.1 基本アダプターパターン

例: WindowsImageAdapter の実装

```csharp
// Baketa.Infrastructure/Platform/Windows/Adapters/WindowsImageAdapter.cs
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Infrastructure.Platform.Abstractions;
using Baketa.Infrastructure.Platform.Windows.Imaging;

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

#### 4.2.2 ファクトリーパターンの実装

例: ImageFactory の実装

```csharp
// Baketa.Core/Services/Imaging/ImageFactory.cs
using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Infrastructure.Platform.Abstractions;
using Baketa.Infrastructure.Platform.Windows.Adapters;

namespace Baketa.Core.Services.Imaging
{
    /// <summary>
    /// 画像ファクトリーの実装
    /// </summary>
    public class ImageFactory : IImageFactory
    {
        private readonly IWindowsImageFactory _windowsImageFactory;

        public ImageFactory(IWindowsImageFactory windowsImageFactory)
        {
            _windowsImageFactory = windowsImageFactory ?? throw new ArgumentNullException(nameof(windowsImageFactory));
        }

        public async Task<IImage> CreateFromFileAsync(string path)
        {
            var windowsImage = await _windowsImageFactory.CreateFromFileAsync(path);
            return new WindowsImageAdapter(windowsImage);
        }

        public async Task<IImage> CreateFromBytesAsync(byte[] data)
        {
            var windowsImage = await _windowsImageFactory.CreateFromBytesAsync(data);
            return new WindowsImageAdapter(windowsImage);
        }

        public IImage CreateFromWindowsImage(IWindowsImage windowsImage)
        {
            return new WindowsImageAdapter(windowsImage ?? throw new ArgumentNullException(nameof(windowsImage)));
        }
    }
}
```

### 4.3 Step 3: 実装クラスの作成/移行

#### 4.3.1 Windows固有の実装クラス

例: WindowsImage の実装

```csharp
// Baketa.Infrastructure/Platform/Windows/Imaging/WindowsImage.cs
using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Infrastructure.Platform.Abstractions;

namespace Baketa.Infrastructure.Platform.Windows.Imaging
{
    /// <summary>
    /// Windows固有の画像実装
    /// </summary>
    public class WindowsImage : IWindowsImage
    {
        private readonly Bitmap _bitmap;
        private bool _disposed;

        public WindowsImage(Bitmap bitmap)
        {
            _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        }

        public int Width => _bitmap.Width;
        public int Height => _bitmap.Height;

        public Bitmap GetNativeImage() => _bitmap;

        public Task SaveAsync(string path)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WindowsImage));
                
            _bitmap.Save(path);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _bitmap.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
```

#### 4.3.2 サービス実装クラス

例: CaptureService の実装

```csharp
// Baketa.Application/Services/Capture/CaptureService.cs
using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Infrastructure.Platform.Abstractions;
using Microsoft.Extensions.Logging;

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
            _windowsCaptureService = windowsCaptureService ?? throw new ArgumentNullException(nameof(windowsCaptureService));
            _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

### 4.4 Step 4: 既存コードの参照更新

既存のコードを、新しいインターフェースを使用するように更新します。

#### 4.4.1 名前空間のエイリアスの使用

```csharp
// 移行期間中の名前空間エイリアス
using CoreImage = Baketa.Core.Abstractions.Imaging.IImage;
using LegacyImage = Baketa.Core.Interfaces.Image.IImage;
```

#### 4.4.2 依存性注入コンテナの更新

```csharp
// DIコンテナの更新例
public void ConfigureServices(IServiceCollection services)
{
    // 新しいインターフェースの登録
    services.AddSingleton<Baketa.Core.Abstractions.Imaging.IImageFactory, ImageFactory>();
    services.AddSingleton<Baketa.Infrastructure.Platform.Abstractions.IWindowsImageFactory, WindowsImageFactory>();
    services.AddSingleton<Baketa.Core.Abstractions.Capture.ICaptureService, CaptureService>();
    
    // レガシーインターフェースのブリッジ登録 (移行期間中のみ)
    services.AddSingleton<Baketa.Core.Interfaces.Image.IImageFactory>(sp => 
        new LegacyImageFactoryAdapter(sp.GetRequiredService<Baketa.Core.Abstractions.Imaging.IImageFactory>()));
}
```

### 4.5 Step 5: 古いインターフェースの非推奨化

#### 4.5.1 Obsolete属性の使用

```csharp
// Baketa.Core/Interfaces/Image/IImage.cs
using System;
using System.Threading.Tasks;

namespace Baketa.Core.Interfaces.Image
{
    /// <summary>
    /// 画像抽象化の基本インターフェース
    /// </summary>
    [Obsolete("このインターフェースは非推奨です。代わりに Baketa.Core.Abstractions.Imaging.IImage を使用してください。")]
    public interface IImage : IDisposable
    {
        // 既存の定義...
    }
}
```

#### 4.5.2 レガシーアダプターの実装

```csharp
// 移行期間中のレガシーアダプター
public class LegacyImageAdapter : Baketa.Core.Interfaces.Image.IImage
{
    private readonly Baketa.Core.Abstractions.Imaging.IImage _newImage;
    
    public LegacyImageAdapter(Baketa.Core.Abstractions.Imaging.IImage newImage)
    {
        _newImage = newImage;
    }
    
    // IImage インターフェースのメンバー実装...
}
```

### 4.6 Step 6: 完全移行の検証

#### 4.6.1 テスト実行

```powershell
# 全テストの実行
dotnet test

# 特定の領域のテスト実行
dotnet test --filter "Category=Image"
```

#### 4.6.2 非推奨警告の確認

コンパイラ警告を確認し、まだ古いインターフェースを使用しているコードを特定します：

```powershell
# 警告を表示するビルド
dotnet build /p:TreatWarningsAsErrors=false /clp:WarningsOnly
```

## 5. 移行のチェックリスト

### 5.1 Step 1: 新しいインターフェース定義

- [ ] コア抽象化インターフェースの作成
- [ ] プラットフォーム抽象化インターフェースの作成
- [ ] アプリケーションレベルインターフェースの作成
- [ ] 適切なXMLドキュメントコメントの追加

### 5.2 Step 2: アダプターの実装

- [ ] WindowsImageAdapter の実装
- [ ] 他のアダプターの実装
- [ ] ファクトリークラスの実装

### 5.3 Step 3: 実装クラスの作成/移行

- [ ] コアサービスの実装
- [ ] Windows固有実装の作成
- [ ] アプリケーションサービスの実装

### 5.4 Step 4: 既存コードの参照更新

- [ ] 新しいインターフェースへの参照更新
- [ ] DIコンテナの設定更新
- [ ] テストコードの更新

### 5.5 Step 5: 古いインターフェースの非推奨化

- [ ] 古いインターフェースへのObsolete属性の追加
- [ ] レガシーアダプターの実装（必要に応じて）

### 5.6 Step 6: 完全移行の検証

- [ ] 全テストの実行と確認
- [ ] 非推奨警告の確認
- [ ] パフォーマンステスト実行
- [ ] ユーザー操作テスト実行

## 6. トラブルシューティングガイド

### 6.1 一般的な問題と解決策

| 問題 | 原因 | 解決策 |
|------|------|-------|
| コンパイルエラー | インターフェース参照の不一致 | 新旧インターフェースの整合性を確認 |
| 実行時エラー | アダプターの実装ミス | アダプターの動作を単体テストで検証 |
| メモリリーク | Disposeパターンの不適切な実装 | リソース解放パターンを確認 |
| パフォーマンス低下 | 過剰なアダプター層 | アダプター設計の最適化 |

### 6.2 依存関係の解決

依存関係の循環参照がある場合は、適切なインターフェース抽象化やファクトリーパターンを使用して解決します。

## 7. 移行後の注意点

### 7.1 古いコードの完全削除

完全な移行が確認できた後は、以下の手順で古いコードを削除します：

1. 非推奨属性とともに残していたレガシーインターフェースの削除
2. レガシーアダプターの削除
3. DIコンテナからのレガシー登録の削除

### 7.2 ドキュメントの更新

1. クラス図の更新
2. 開発者向けガイドの更新
3. コメントの更新

## 8. 付録

### 8.1 サンプルコード

詳細なサンプルコードはコードリポジトリの以下の場所を参照してください：
- `E:\dev\Baketa\docs\2-development\migration\samples\`

### 8.2 フィードバック

移行プロセスに関するフィードバックやイシューは、以下の方法で報告してください：
- GitHub Issues (プロジェクトリポジトリ)
- チーム内コミュニケーションツール
