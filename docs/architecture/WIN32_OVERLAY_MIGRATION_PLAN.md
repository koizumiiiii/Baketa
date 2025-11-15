# Win32 Layered Window オーバーレイ移行計画

## 📋 **背景と問題**

### **現在の問題**
- Avalonia 11.2.7の翻訳オーバーレイウィンドウで、FluentThemeによる角丸とシャドウが除去できない
- 以下のすべてのアプローチが失敗:
  1. ✅ Window.Resources with WindowCornerRadius=0
  2. ✅ Window.Styles with Window.Template override (ContentPresenter only)
  3. ✅ Border inline properties (CornerRadius=0, BoxShadow=none)
  4. ✅ Code-behind force styling (border.CornerRadius, border.BoxShadow)
  5. ✅ App.axaml global style (Window.InPlaceOverlay class selector)
  6. ✅ Code-behind Template forcing in constructor

### **根本原因** (Gemini専門家レビュー結果)
- **Self-Styling Limitation**: Window内のWindow.Stylesは自分自身に適用されない（Avalonia/WPF共通の設計制約）
- **FluentTheme Precedence**: 特定のシナリオでFluentThemeが高特異性スタイルより優先される
- **Avalonia 11.2.7 Architectural Constraint**: フレームワークレベルの制約であり、実装エラーではない

### **ユーザー要件**
1. **必須**: 完全透明、角丸なし、シャドウなしのオーバーレイウィンドウ
2. **希望**: すりガラス風のブラー効果

---

## 🎯 **採用方針: ハイブリッドアーキテクチャ**

### **基本戦略**
- **Avaloniaを削除しない** - メインUIは問題なく動作しているため継続使用
- **オーバーレイのみWin32 Layered Windowに移行** - 問題箇所を最小限の変更で解決

### **技術選択**

| コンポーネント | 技術 | 理由 |
|--------------|------|------|
| **メインウィンドウ** | Avalonia ✅ | 問題なく動作、ReactiveUI活用 |
| **設定画面** | Avalonia ✅ | 問題なく動作、変更不要 |
| **ViewModels** | Avalonia ✅ | MVVM + ReactiveUIパターン継続 |
| **翻訳オーバーレイ** | **Win32 Layered Window** ⭐ | Avaloniaスタイル問題を根本解決 |

---

## 📐 **実装ロードマップ**

### **Phase 1: Win32 Layered Window基盤** (3-5営業日)

#### **Step 1.1: P/Invoke定義作成** (0.5日)
**ファイル**: `Baketa.Infrastructure.Platform/Windows/NativeMethods.cs`

**実装内容**:
```csharp
using System;
using System.Runtime.InteropServices;

namespace Baketa.Infrastructure.Platform.Windows;

internal static class NativeMethods
{
    // Window Styles
    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_EX_LAYERED = 0x00080000;
    internal const uint WS_EX_TRANSPARENT = 0x00000020;
    internal const uint WS_EX_NOACTIVATE = 0x08000000;
    internal const uint WS_EX_TOPMOST = 0x00000008;

    // UpdateLayeredWindow flags
    internal const uint ULW_ALPHA = 0x00000002;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll")]
    internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref POINT pptDst,
        ref SIZE psize,
        IntPtr hdcSrc,
        ref POINT pptSrc,
        uint crKey,
        ref BLENDFUNCTION pblend,
        uint dwFlags);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    // Structures
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
}
```

#### **Step 1.2: インターフェース定義** (0.5日)
**ファイル**: `Baketa.Core.Abstractions/UI/ILayeredOverlayWindow.cs`

```csharp
namespace Baketa.Core.Abstractions.UI;

/// <summary>
/// Win32 Layered Windowベースのオーバーレイウィンドウインターフェース
/// </summary>
public interface ILayeredOverlayWindow : IDisposable
{
    /// <summary>
    /// ウィンドウを表示
    /// </summary>
    void Show();

    /// <summary>
    /// ウィンドウを非表示
    /// </summary>
    void Hide();

    /// <summary>
    /// ウィンドウを閉じる
    /// </summary>
    void Close();

    /// <summary>
    /// テキストを設定
    /// </summary>
    void SetText(string text);

    /// <summary>
    /// 位置を設定（スクリーン座標）
    /// </summary>
    void SetPosition(int x, int y);

    /// <summary>
    /// サイズを設定
    /// </summary>
    void SetSize(int width, int height);

    /// <summary>
    /// 背景色を設定
    /// </summary>
    void SetBackgroundColor(byte r, byte g, byte b, byte alpha);
}
```

#### **Step 1.3: LayeredOverlayWindow実装** (2-3日)
**ファイル**: `Baketa.Infrastructure.Platform/Windows/LayeredOverlayWindow.cs`

**主要機能**:
1. **🔥 [CRITICAL] Win32専用STAスレッド作成**
   - ウィンドウメッセージ処理用の専用`Thread`を生成
   - `Thread.SetApartmentState(ApartmentState.STA)` 設定必須
   - メッセージループ (`GetMessage`, `TranslateMessage`, `DispatchMessage`) 実装
   - スレッド間通信でUI操作を安全に実行

2. `WS_EX_LAYERED` スタイルでウィンドウ作成
3. GDIでメモリビットマップ作成
4. テキストレンダリング（GDI+ TextRenderer使用）
5. `UpdateLayeredWindow`でピクセル単位アルファブレンド
6. リソース管理とDispose実装（HDC, HBITMAP等のGDI Handle解放）

**実装例**:
```csharp
private Thread? _windowThread;
private IntPtr _hwnd;
private BlockingCollection<Action> _messageQueue = new();

public LayeredOverlayWindow(ILogger<LayeredOverlayWindow> logger)
{
    _logger = logger;

    // Win32専用STAスレッド起動
    _windowThread = new Thread(WindowThreadProc)
    {
        IsBackground = true
    };
    _windowThread.SetApartmentState(ApartmentState.STA);
    _windowThread.Start();
}

private void WindowThreadProc()
{
    // ウィンドウクラス登録
    RegisterWindowClass();

    // ウィンドウ作成
    _hwnd = NativeMethods.CreateWindowEx(
        NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE,
        // ...
    );

    if (_hwnd == IntPtr.Zero)
    {
        var error = Marshal.GetLastWin32Error();
        _logger.LogError("CreateWindowEx失敗 - Error Code: {ErrorCode}", error);
        return;
    }

    // メッセージループ
    while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
    {
        NativeMethods.TranslateMessage(ref msg);
        NativeMethods.DispatchMessage(ref msg);

        // カスタムメッセージキュー処理（SetText, SetPosition等）
        while (_messageQueue.TryTake(out var action, 0))
        {
            action();
        }
    }
}

// スレッドセーフなUI操作
public void SetText(string text)
{
    _messageQueue.Add(() =>
    {
        // GDI描画処理
        UpdateWindowContent(text);
    });
}
```

#### **Step 1.4: Factory実装** (0.5日)
**ファイル**: `Baketa.Infrastructure.Platform/Windows/LayeredOverlayWindowFactory.cs`

```csharp
namespace Baketa.Infrastructure.Platform.Windows;

public interface ILayeredOverlayWindowFactory
{
    ILayeredOverlayWindow Create();
}

public class LayeredOverlayWindowFactory : ILayeredOverlayWindowFactory
{
    private readonly ILogger<LayeredOverlayWindow> _logger;

    public LayeredOverlayWindowFactory(ILogger<LayeredOverlayWindow> logger)
    {
        _logger = logger;
    }

    public ILayeredOverlayWindow Create()
    {
        return new LayeredOverlayWindow(_logger);
    }
}
```

#### **Step 1.5: SimpleInPlaceOverlayManager書き換え** (1日)
**ファイル**: `Baketa.Application/Services/Overlay/SimpleInPlaceOverlayManager.cs`

```csharp
using System.Collections.Concurrent;

public class SimpleInPlaceOverlayManager : IInPlaceTranslationOverlayManager, IDisposable
{
    private readonly ILayeredOverlayWindowFactory _windowFactory;
    private readonly ILogger<SimpleInPlaceOverlayManager> _logger;
    // 🔥 [GEMINI_RECOMMENDATION] スレッドセーフティ確保
    private readonly ConcurrentBag<ILayeredOverlayWindow> _activeWindows = new();

    public SimpleInPlaceOverlayManager(
        ILayeredOverlayWindowFactory windowFactory,
        ILogger<SimpleInPlaceOverlayManager> logger)
    {
        _windowFactory = windowFactory;
        _logger = logger;
    }

    public Task ShowInPlaceOverlayAsync(TextChunk chunk, CancellationToken ct = default)
    {
        _logger.LogInformation("🔥 [WIN32_OVERLAY] ShowInPlaceOverlayAsync - ChunkId: {ChunkId}", chunk.ChunkId);

        var window = _windowFactory.Create();
        window.SetText(chunk.TranslatedText);
        window.SetPosition(chunk.X, chunk.Y);
        window.SetBackgroundColor(240, 255, 255, 242); // すりガラス風半透明白
        window.Show();

        _activeWindows.Add(window);
        return Task.CompletedTask;
    }

    public Task HideAllOverlaysAsync(CancellationToken ct = default)
    {
        // ConcurrentBagからの取り出しはスレッドセーフ
        while (_activeWindows.TryTake(out var window))
        {
            window.Close();
            window.Dispose();
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        while (_activeWindows.TryTake(out var window))
        {
            window.Dispose();
        }
    }
}
```

#### **Step 1.6: DI登録** (0.5日)
**ファイル**: `Baketa.Infrastructure.Platform/DI/Modules/PlatformModule.cs`

```csharp
public override void RegisterServices(IServiceCollection services)
{
    // Win32 Layered Window Factory登録
    services.AddSingleton<ILayeredOverlayWindowFactory, LayeredOverlayWindowFactory>();

    // 既存のAvalonia UIサービスはそのまま継続
    // ...
}
```

---

### **Phase 2: ブラー効果実装** (1-2営業日)

#### **Step 2.1: SetWindowCompositionAttribute実装** (推奨)
**ファイル**: `Baketa.Infrastructure.Platform/Windows/NativeMethods.cs` (追加)

```csharp
// Windows 10/11 Acrylic/Mica効果用
[DllImport("user32.dll")]
internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

internal enum AccentState
{
    ACCENT_DISABLED = 0,
    ACCENT_ENABLE_GRADIENT = 1,
    ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
    ACCENT_ENABLE_BLURBEHIND = 3,
    ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
    ACCENT_ENABLE_HOSTBACKDROP = 5
}

[StructLayout(LayoutKind.Sequential)]
internal struct AccentPolicy
{
    public AccentState AccentState;
    public int AccentFlags;
    public uint GradientColor;
    public int AnimationId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowCompositionAttributeData
{
    public WindowCompositionAttribute Attribute;
    public IntPtr Data;
    public int SizeOfData;
}

internal enum WindowCompositionAttribute
{
    WCA_ACCENT_POLICY = 19
}
```

**LayeredOverlayWindow.csに追加**:
```csharp
public void EnableAcrylicBlur()
{
    var accent = new AccentPolicy
    {
        AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
        AccentFlags = 2,
        GradientColor = 0x01FFFFFF // 半透明白
    };

    var accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf(accent));
    Marshal.StructureToPtr(accent, accentPtr, false);

    var data = new WindowCompositionAttributeData
    {
        Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
        Data = accentPtr,
        SizeOfData = Marshal.SizeOf(accent)
    };

    NativeMethods.SetWindowCompositionAttribute(_hwnd, ref data);
    Marshal.FreeHGlobal(accentPtr);
}
```

---

## 🛡️ **リスク分析と対策**

| リスク | 影響度 | 対策 |
|--------|--------|------|
| **Avaloniaとの併存** | 高 | インターフェース経由で依存分離、Clean Architecture準拠 |
| **レンダリングパフォーマンス** | 中 | GDI → GDI+ → Direct2D段階最適化 |
| **非公開API依存** | 中 | SetWindowCompositionAttribute失敗時のGraceful Degradation実装 |
| **Windows 10未満の互換性** | 低 | Windows 10+ 限定（Baketa要件に合致） |

---

## ✅ **メリット**

1. **最小限の変更** - オーバーレイ部分のみ（全体の約5%）
2. **確実な解決** - OS ネイティブレベルで透明化・角丸なし・シャドウなしを保証
3. **Avalonia継続活用** - メインUIはReactiveUI + MVVMパターンで快適開発継続
4. **段階的実装** - Phase 1（透明化）→ Phase 2（ブラー）の安全な段階実装
5. **パフォーマンス** - Win32ネイティブによる軽量・高速動作
6. **保守性** - Win32 API極めて安定、高い後方互換性

---

## 📊 **削除対象ファイル**

以下のAvalonia Windowベースのオーバーレイ実装は削除対象:

1. `Baketa.UI/Views/Overlay/InPlaceTranslationOverlayWindow.axaml`
2. `Baketa.UI/Views/Overlay/InPlaceTranslationOverlayWindow.axaml.cs`
3. `Baketa.UI/App.axaml` - InPlaceOverlayスタイル定義削除

**保持するファイル**:
- `Baketa.Core.Abstractions/Overlay/IInPlaceTranslationOverlayManager.cs` - インターフェースは継続使用
- すべてのViewModels、Services、メインウィンドウ関連ファイル

---

## 🔄 **実装スケジュール**

| フェーズ | 所要時間 | 開始条件 |
|---------|---------|---------|
| **Phase 1: Win32基盤** | 3-5営業日 | Geminiレビュー承認後 |
| **Phase 2: ブラー効果** | 1-2営業日 | Phase 1完了後 |
| **合計** | **4-7営業日** | - |

---

## 📝 **実装後の検証項目**

### **Phase 1検証**
- [ ] オーバーレイウィンドウが完全透明背景で表示される
- [ ] 角丸・シャドウが完全に除去されている
- [ ] テキストが正しく表示される
- [ ] 座標位置が正確
- [ ] マウスイベントが背後のウィンドウに透過される
- [ ] 複数オーバーレイの同時表示が正常動作
- [ ] HideAllOverlaysAsyncで全て閉じられる

### **Phase 2検証**
- [ ] すりガラス風ブラー効果が適用される
- [ ] 背景がぼかされて見える
- [ ] パフォーマンスに影響がない（60fps維持）
- [ ] Windows 10/11で正常動作

---

## 🎯 **成功基準**

1. ✅ 完全透明、角丸なし、シャドウなしのオーバーレイ表示
2. ✅ すりガラス風ブラー効果の実現
3. ✅ 既存Avaloniaメインウィンドウとの共存
4. ✅ パフォーマンス劣化なし
5. ✅ Clean Architecture原則の遵守
