# Windows Graphics Capture API 統合調査レポート

## 概要

Baketa プロジェクトにおいて、従来の PrintWindow API に加えて Windows Graphics Capture API を統合し、DirectX/OpenGL ゲームの高品質キャプチャを実現するための調査と実装を行った。

## 調査期間

2025年1月

## 背景と目的

### 従来の制限
- **PrintWindow API**: 一部のゲームやDirectX/OpenGLアプリケーションで黒画面になる問題
- **BitBlt API**: ウィンドウの内容ではなく画面座標をキャプチャするため、オーバーレイが映り込む

### 目標
- Discord のような高品質なウィンドウキャプチャの実現
- ゲームアプリケーションでの安定したテキスト検出
- フォールバック機構による高い互換性

## 技術仕様

### 対象環境
- **OS**: Windows 11 (Windows 10 バージョン 1903 以降でも対応)
- **フレームワーク**: .NET 8.0-windows10.0.19041.0
- **CsWinRT**: Version 2.2.0
- **SharpDX**: Version 4.2.0

### 依存パッケージ
```xml
<PackageReference Include="Microsoft.Windows.CsWinRT" Version="2.2.0" />
<PackageReference Include="SharpDX" Version="4.2.0" />
<PackageReference Include="SharpDX.Direct3D11" Version="4.2.0" />
<PackageReference Include="SharpDX.DXGI" Version="4.2.0" />
```

## 実装アプローチ

### 1. アーキテクチャ設計

#### キャプチャ優先順位
```
1. Windows Graphics Capture API（最優先）
2. PrintWindow API（フォールバック）
3. PrintWindow + Foreground（最終フォールバック）
```

#### 実装場所
- **主要実装**: `WinRTWindowCapture.cs`
- **統合箇所**: `CoreWindowManagerAdapterStub.cs`
- **フォールバック**: `GdiScreenCapturer.cs`（未使用判明）

### 2. COM 相互運用の実装

#### IGraphicsCaptureItemInterop インターフェース
```csharp
[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    int CreateForWindow(IntPtr window, [In] ref Guid riid, out IntPtr result);
    int CreateForMonitor(IntPtr monitor, [In] ref Guid riid, out IntPtr result);
}
```

#### 複数の COM 相互運用方式を試行
1. **直接的なWinRT相互運用**: WindowsRuntimeMarshal + vtable 直接呼び出し
2. **安全なCOM相互運用**: 従来のActivationFactory + QueryInterface
3. **ComWrappers経由**: .NET 5+ の新しい相互運用方式

### 3. キャプチャパイプライン

#### Direct3D11 統合
```csharp
private async Task<Bitmap> CaptureFrameAsync(GraphicsCaptureItem captureItem)
{
    var d3dDevice = CreateDirect3DDevice();
    using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
        d3dDevice,
        DirectXPixelFormat.B8G8R8A8UIntNormalized,
        1,
        captureItem.Size);
    
    // フレームキャプチャとビットマップ変換
}
```

#### テクスチャ変換処理
- GPU → CPU メモリコピー
- ステージングテクスチャ作成
- BGRA → ARGB ピクセル変換
- System.Drawing.Bitmap への変換

## 発生した問題と調査結果

### 主要問題: MarshalDirectiveException

#### 症状
```
例外がスローされました: 'System.Runtime.InteropServices.MarshalDirectiveException' 
(Baketa.Infrastructure.Platform.dll の中)
```

#### 発生箇所
- `IGraphicsCaptureItemInterop.CreateForWindow` 呼び出し時
- `WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi` 呼び出し時

#### 調査した原因
1. **.NET 8 の COM 相互運用制限**
   - CsWinRT 2.2.0 でも完全に解決されていない既知の問題
   - `[ComImport]` 属性と .NET 8 の新しいマーシャリングシステムの競合

2. **WinRT オブジェクトの生成問題**
   - GraphicsCaptureItem の COM → WinRT 変換で失敗
   - ActivationFactory の取得は成功するが、オブジェクト作成時に失敗

3. **権限・環境問題の除外**
   - Windows 11 環境で実行（Windows 10の制限ではない）
   - GraphicsCaptureSession.IsSupported() は true を返す
   - 必要なWinRT DLLは正常に読み込まれている

### 試行した解決策

#### 1. COM 相互運用の改良
```csharp
// vtable から関数ポインタを直接取得（マーシャリング回避）
var vtable = Marshal.ReadIntPtr(interopPtr);
var createForWindowPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
var createForWindow = Marshal.GetDelegateForFunctionPointer<CreateForWindowDelegate>(createForWindowPtr);
```

#### 2. 複数のActivationFactory取得方式
- `RoGetActivationFactory` 直接呼び出し
- `WindowsRuntimeMarshal.GetActivationFactory` 使用
- QueryInterface による段階的取得

#### 3. 例外ハンドリングの強化
- 各段階での詳細なHRESULT ログ出力
- 安全なリソース解放
- 複数フォールバック経路の実装

## 実装結果

### 成功した要素
✅ **統合アーキテクチャ**: 優先順位付きフォールバック機構  
✅ **ビルドシステム**: .NET 8 + CsWinRT 2.2.0 での正常なコンパイル  
✅ **エラーハンドリング**: 例外発生時の安全なPrintWindowフォールバック  
✅ **ログ出力**: 詳細な診断情報の提供  

### 未解決の問題
❌ **MarshalDirectiveException**: .NET 8 環境での根本的な制限  
❌ **GraphicsCaptureItem 作成失敗**: 複数のアプローチでも解決できず  
❌ **COM 相互運用**: 現在の .NET ランタイムでは技術的困難  

## パフォーマンス評価

### 現在の動作
```
🖼️ キャプチャ試行: Handle=65936, Size=2560x1080, Thumb=160x67
🚀 Windows Graphics Capture API 試行開始: Handle=65936
❌ Windows Graphics Capture API 失敗: MarshalDirectiveException
✅ PrintWindow成功: Handle=65936
```

### 測定結果
- **Windows Graphics Capture API 試行時間**: ~50ms (失敗)
- **PrintWindow フォールバック**: ~10ms (成功)
- **総キャプチャ時間**: ~60ms (オーバーヘッド含む)

## 結論と推奨事項

### 現状の評価
Windows Graphics Capture API の統合は技術的に実装されたが、.NET 8 の COM 相互運用制限により実用化には至らなかった。しかし、フォールバック機構により実用上の問題は発生していない。

### 短期的推奨事項（即時対応）
1. **Windows Graphics Capture API の一時無効化**
   ```csharp
   // TryWindowsGraphicsCapture の呼び出しを無効化
   // PrintWindow のみを使用
   ```

2. **設定による制御機能の追加**
   ```json
   {
     "CaptureSettings": {
       "EnableWindowsGraphicsCapture": false
     }
   }
   ```

### 中長期的推奨事項
1. **.NET 9 での再評価**: Microsoft の COM 相互運用改善を待つ
2. **CsWinRT の将来バージョン**: より安定したWinRT相互運用の提供を期待
3. **ネイティブライブラリ**: C++/WinRT による Windows Graphics Capture API の実装検討

### 代替アプローチ
1. **PrintWindow の最適化**: より効率的な実装
2. **ゲーム別プロファイル**: 特定ゲームでの最適化設定
3. **Windows API フック**: より低レベルな画面キャプチャ手法

## 技術的学習

### .NET 8 + WinRT 相互運用の制限
- CsWinRT 2.2.0 でも完全ではないCOM相互運用サポート
- `[ComImport]` 属性と新しいマーシャリングシステムの競合
- Source Generator ベースの相互運用への移行推奨

### 実用的なフォールバック設計
- 複数のキャプチャ手法の組み合わせ
- エラー時の詳細ログ出力
- 安全なリソース管理

### Windows 11 環境での開発
- Windows Graphics Capture API のサポート状況
- 権限とセキュリティ要件
- パフォーマンス特性

## 関連ファイル

### 実装ファイル
- `Baketa.Infrastructure.Platform/Windows/Capture/WinRTWindowCapture.cs`
- `Baketa.Infrastructure.Platform/Adapters/CoreWindowManagerAdapterStub.cs`
- `Baketa.Infrastructure.Platform/Windows/Capture/GdiScreenCapturer.cs`

### 設定ファイル
- `Baketa.Infrastructure.Platform/Baketa.Infrastructure.Platform.csproj`
- 各プロジェクトの NoWarn 設定

### テストファイル
- `tests/Baketa.Infrastructure.Platform.Tests/Windows/Capture/`

## 参考資料

### Microsoft ドキュメント
- [Windows Graphics Capture API](https://docs.microsoft.com/en-us/windows/win32/api/winrt.graphics.capture/)
- [CsWinRT Documentation](https://docs.microsoft.com/en-us/windows/apps/develop/platform/csharp-winrt/)
- [.NET 8 Interop Changes](https://docs.microsoft.com/en-us/dotnet/core/compatibility/interop)

### 関連Issue
- [CsWinRT MarshalDirectiveException Issues](https://github.com/microsoft/CsWinRT/issues)
- [.NET 8 COM Interop Limitations](https://github.com/dotnet/runtime/issues)

---

**最終更新**: 2025年1月  
**ステータス**: 技術調査完了、実用化保留  
**次回レビュー**: .NET 9 リリース後