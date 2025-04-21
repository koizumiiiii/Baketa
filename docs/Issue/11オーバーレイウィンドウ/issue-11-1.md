# Issue 11-1: 透過ウィンドウとクリックスルー機能の実装

## 概要
Baketaのコア機能であるオーバーレイウィンドウの基盤を実装します。これには、ゲーム画面上に透過表示されるウィンドウとクリックスルー機能（マウス入力をゲームに直接通過させる機能）の実装が含まれます。

## 目的・理由
透過ウィンドウとクリックスルー機能は以下の理由で必要です：

1. ゲームプレイ中に翻訳テキストのみを表示し、ゲームの表示や操作を妨げない
2. ユーザーはオーバーレイの存在を意識せずにゲーム操作を継続できる
3. 最小限の視覚的干渉でゲーム体験を維持しながら翻訳サポートを提供できる

## 詳細
- Windows API（User32, DWM）を使用した透過ウィンドウの実装
- AvaloniaとWindows APIを連携させるためのアダプターの開発
- マウス入力のパススルー（クリックスルー）機能の実装
- 透明度とブレンドモードのカスタマイズ機能の実装

## タスク分解
- [ ] 透過ウィンドウの基本構造
  - [ ] `IOverlayWindow`インターフェースの設計
  - [ ] `OverlayWindow`クラスの実装
  - [ ] 透過背景とコンテンツ表示機能の実装
- [ ] Windowsプラットフォーム統合
  - [ ] Windows API関数のP/Invoke定義
  - [ ] DWMとの連携による透過ウィンドウ設定
  - [ ] 拡張ウィンドウスタイルの実装
- [ ] クリックスルー機能
  - [ ] ウィンドウメッセージフック実装
  - [ ] 透過ヒットテスト実装
  - [ ] 条件付きマウスイベントパススルー機構
- [ ] Avalonia統合
  - [ ] Avalonia `TopLevel` カスタム拡張
  - [ ] Avalonia Renderingシステム連携
  - [ ] オーバーレイウィンドウマネージャー実装
- [ ] パフォーマンス最適化
  - [ ] レンダリングの最適化
  - [ ] バッファ管理の最適化
  - [ ] 画面更新制御の実装
- [ ] ウィンドウ状態管理
  - [ ] ゲームウィンドウとの重ね順管理
  - [ ] アクティブ/非アクティブ状態の制御
  - [ ] ウィンドウモード変更検出と対応
- [ ] 単体テストの実装

## インターフェース設計案
```csharp
namespace Baketa.UI.Overlay
{
    /// <summary>
    /// オーバーレイウィンドウインターフェース
    /// </summary>
    public interface IOverlayWindow : IDisposable
    {
        /// <summary>
        /// オーバーレイウィンドウが表示されているかどうか
        /// </summary>
        bool IsVisible { get; }
        
        /// <summary>
        /// ウィンドウハンドル
        /// </summary>
        IntPtr Handle { get; }
        
        /// <summary>
        /// オーバーレイの不透明度（0.0～1.0）
        /// </summary>
        double Opacity { get; set; }
        
        /// <summary>
        /// クリックスルーが有効かどうか
        /// </summary>
        bool IsClickThrough { get; set; }
        
        /// <summary>
        /// コンテンツ領域の有効なクリック領域
        /// </summary>
        IReadOnlyList<Rect> HitTestAreas { get; set; }
        
        /// <summary>
        /// ウィンドウの位置
        /// </summary>
        Point Position { get; set; }
        
        /// <summary>
        /// ウィンドウのサイズ
        /// </summary>
        Size Size { get; set; }
        
        /// <summary>
        /// ターゲットウィンドウハンドル
        /// </summary>
        IntPtr TargetWindowHandle { get; set; }
        
        /// <summary>
        /// コンテンツの表示モード
        /// </summary>
        OverlayContentMode ContentMode { get; set; }
        
        /// <summary>
        /// オーバーレイウィンドウを表示します
        /// </summary>
        void Show();
        
        /// <summary>
        /// オーバーレイウィンドウを非表示にします
        /// </summary>
        void Hide();
        
        /// <summary>
        /// コンテンツを更新します
        /// </summary>
        /// <param name="content">表示するコンテンツ</param>
        void UpdateContent(IVisual content);
        
        /// <summary>
        /// オーバーレイをターゲットウィンドウに合わせて調整します
        /// </summary>
        void AdjustToTargetWindow();
        
        /// <summary>
        /// ウィンドウに効果を適用します
        /// </summary>
        /// <param name="effect">適用する効果</param>
        /// <param name="parameters">効果のパラメータ</param>
        void ApplyEffect(OverlayEffect effect, object? parameters = null);
    }
    
    /// <summary>
    /// オーバーレイコンテンツ表示モード
    /// </summary>
    public enum OverlayContentMode
    {
        /// <summary>
        /// 通常表示
        /// </summary>
        Normal,
        
        /// <summary>
        /// 背景のみ透過
        /// </summary>
        TransparentBackground,
        
        /// <summary>
        /// コンテンツの周囲のみ透過
        /// </summary>
        ContentOnly,
        
        /// <summary>
        /// 特定領域のみ非透過
        /// </summary>
        RegionBased
    }
    
    /// <summary>
    /// オーバーレイ効果
    /// </summary>
    public enum OverlayEffect
    {
        /// <summary>
        /// なし
        /// </summary>
        None,
        
        /// <summary>
        /// ぼかし
        /// </summary>
        Blur,
        
        /// <summary>
        /// 影
        /// </summary>
        Shadow,
        
        /// <summary>
        /// グロー
        /// </summary>
        Glow,
        
        /// <summary>
        /// フェード
        /// </summary>
        Fade
    }
}

namespace Baketa.UI.Overlay.Windows
{
    /// <summary>
    /// Windows用オーバーレイウィンドウの実装
    /// </summary>
    public class WindowsOverlayWindow : IOverlayWindow
    {
        // Win32 API定義
        // 省略
        
        // プライベートフィールド
        private readonly ILogger? _logger;
        private IntPtr _handle;
        private IntPtr _targetWindowHandle;
        private double _opacity = 0.9;
        private bool _isClickThrough = true;
        private bool _isVisible;
        private List<Rect> _hitTestAreas = new();
        private Point _position;
        private Size _size;
        private bool _disposed;
        private OverlayContentMode _contentMode = OverlayContentMode.TransparentBackground;
        
        /// <summary>
        /// 新しいWindows用オーバーレイウィンドウを初期化します
        /// </summary>
        /// <param name="logger">ロガー</param>
        public WindowsOverlayWindow(ILogger? logger = null)
        {
            _logger = logger;
            
            // ウィンドウを作成
            CreateOverlayWindow();
            
            _logger?.LogInformation("Windows用オーバーレイウィンドウが初期化されました。ハンドル: {Handle}", _handle);
        }
        
        /// <inheritdoc />
        public bool IsVisible => _isVisible;
        
        /// <inheritdoc />
        public IntPtr Handle => _handle;
        
        /// <inheritdoc />
        public double Opacity
        {
            get => _opacity;
            set
            {
                if (value < 0.0 || value > 1.0)
                    throw new ArgumentOutOfRangeException(nameof(value), "不透明度は0.0～1.0の範囲で指定してください。");
                    
                if (Math.Abs(_opacity - value) < 0.01)
                    return;
                    
                _opacity = value;
                UpdateLayeredWindowAttributes();
            }
        }
        
        /// <inheritdoc />
        public bool IsClickThrough
        {
            get => _isClickThrough;
            set
            {
                if (_isClickThrough == value)
                    return;
                    
                _isClickThrough = value;
                UpdateWindowStyles();
            }
        }
        
        /// <inheritdoc />
        public IReadOnlyList<Rect> HitTestAreas
        {
            get => _hitTestAreas;
            set => _hitTestAreas = new List<Rect>(value);
        }
        
        /// <inheritdoc />
        public Point Position
        {
            get => _position;
            set
            {
                if (_position == value)
                    return;
                    
                _position = value;
                UpdateWindowPosition();
            }
        }
        
        /// <inheritdoc />
        public Size Size
        {
            get => _size;
            set
            {
                if (_size == value)
                    return;
                    
                _size = value;
                UpdateWindowSize();
            }
        }
        
        /// <inheritdoc />
        public IntPtr TargetWindowHandle
        {
            get => _targetWindowHandle;
            set
            {
                if (_targetWindowHandle == value)
                    return;
                    
                _targetWindowHandle = value;
                AdjustToTargetWindow();
            }
        }
        
        /// <inheritdoc />
        public OverlayContentMode ContentMode
        {
            get => _contentMode;
            set
            {
                if (_contentMode == value)
                    return;
                    
                _contentMode = value;
                UpdateContentMode();
            }
        }
        
        /// <inheritdoc />
        public void Show()
        {
            if (_isVisible)
                return;
                
            ShowWindow(_handle, SW_SHOW);
            _isVisible = true;
            
            _logger?.LogDebug("オーバーレイウィンドウが表示されました。");
        }
        
        /// <inheritdoc />
        public void Hide()
        {
            if (!_isVisible)
                return;
                
            ShowWindow(_handle, SW_HIDE);
            _isVisible = false;
            
            _logger?.LogDebug("オーバーレイウィンドウが非表示になりました。");
        }
        
        /// <inheritdoc />
        public void UpdateContent(IVisual content)
        {
            // Avalonia UIコンテンツの更新処理
            // 省略
        }
        
        /// <inheritdoc />
        public void AdjustToTargetWindow()
        {
            if (_targetWindowHandle == IntPtr.Zero)
                return;
                
            // ターゲットウィンドウの位置とサイズを取得
            GetWindowRect(_targetWindowHandle, out var rect);
            
            var left = rect.Left;
            var top = rect.Top;
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            
            // オーバーレイの位置とサイズを設定
            SetWindowPos(_handle, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE);
            
            _position = new Point(left, top);
            _size = new Size(width, height);
            
            _logger?.LogDebug("オーバーレイウィンドウがターゲットウィンドウに合わせて調整されました。位置: {Position}, サイズ: {Size}",
                _position, _size);
        }
        
        /// <inheritdoc />
        public void ApplyEffect(OverlayEffect effect, object? parameters = null)
        {
            switch (effect)
            {
                case OverlayEffect.Blur:
                    ApplyBlurEffect(parameters);
                    break;
                case OverlayEffect.Shadow:
                    ApplyShadowEffect(parameters);
                    break;
                case OverlayEffect.Glow:
                    ApplyGlowEffect(parameters);
                    break;
                case OverlayEffect.Fade:
                    ApplyFadeEffect(parameters);
                    break;
                case OverlayEffect.None:
                    RemoveAllEffects();
                    break;
            }
        }
        
        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースを解放するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;
                
            if (disposing)
            {
                // マネージドリソースの解放
            }
            
            // アンマネージドリソースの解放
            if (_handle != IntPtr.Zero)
            {
                DestroyWindow(_handle);
                _handle = IntPtr.Zero;
            }
            
            _disposed = true;
        }
        
        /// <summary>
        /// オーバーレイウィンドウを作成します
        /// </summary>
        private void CreateOverlayWindow()
        {
            // ウィンドウクラスの登録
            var wndClass = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                style = CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc = WindowProc,
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
                lpszClassName = "BaketaOverlayWindow"
            };
            
            var atom = RegisterClassEx(ref wndClass);
            if (atom == 0)
            {
                var error = Marshal.GetLastWin32Error();
                _logger?.LogError("ウィンドウクラスの登録に失敗しました。エラーコード: {Error}", error);
                throw new Win32Exception(error);
            }
            
            // ウィンドウ作成
            _handle = CreateWindowEx(
                WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
                "BaketaOverlayWindow",
                "Baketa Overlay",
                WS_POPUP,
                0, 0, 800, 600,
                IntPtr.Zero,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);
                
            if (_handle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                _logger?.LogError("オーバーレイウィンドウの作成に失敗しました。エラーコード: {Error}", error);
                throw new Win32Exception(error);
            }
            
            // レイヤードウィンドウ属性の設定
            UpdateLayeredWindowAttributes();
            
            // DWMの設定
            SetupDwmAttributes();
        }
        
        /// <summary>
        /// ウィンドウプロシージャ
        /// </summary>
        private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_NCHITTEST:
                    // クリックスルー処理
                    if (_isClickThrough)
                    {
                        // マウス座標を取得
                        int x = (short)(lParam.ToInt32() & 0xFFFF);
                        int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                        
                        // スクリーン座標からクライアント座標に変換
                        POINT pt = new POINT { X = x, Y = y };
                        ScreenToClient(_handle, ref pt);
                        
                        // ヒットテスト領域内かチェック
                        foreach (var area in _hitTestAreas)
                        {
                            if (area.Contains(new Point(pt.X, pt.Y)))
                            {
                                // 特定領域内の場合はクリック可能
                                return (IntPtr)HTCLIENT;
                            }
                        }
                        
                        // クリックスルー（透過）
                        return (IntPtr)HTTRANSPARENT;
                    }
                    break;
            }
            
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }
        
        /// <summary>
        /// レイヤードウィンドウ属性を更新します
        /// </summary>
        private void UpdateLayeredWindowAttributes()
        {
            if (_handle == IntPtr.Zero)
                return;
                
            byte alpha = (byte)(_opacity * 255);
            SetLayeredWindowAttributes(_handle, 0, alpha, LWA_ALPHA);
        }
        
        /// <summary>
        /// ウィンドウスタイルを更新します
        /// </summary>
        private void UpdateWindowStyles()
        {
            if (_handle == IntPtr.Zero)
                return;
                
            int exStyle = GetWindowLong(_handle, GWL_EXSTYLE);
            
            if (_isClickThrough)
            {
                exStyle |= WS_EX_TRANSPARENT;
            }
            else
            {
                exStyle &= ~WS_EX_TRANSPARENT;
            }
            
            SetWindowLong(_handle, GWL_EXSTYLE, exStyle);
        }
        
        /// <summary>
        /// ウィンドウ位置を更新します
        /// </summary>
        private void UpdateWindowPosition()
        {
            if (_handle == IntPtr.Zero)
                return;
                
            SetWindowPos(_handle, IntPtr.Zero, (int)_position.X, (int)_position.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        
        /// <summary>
        /// ウィンドウサイズを更新します
        /// </summary>
        private void UpdateWindowSize()
        {
            if (_handle == IntPtr.Zero)
                return;
                
            SetWindowPos(_handle, IntPtr.Zero, 0, 0, (int)_size.Width, (int)_size.Height, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        
        /// <summary>
        /// コンテンツ表示モードを更新します
        /// </summary>
        private void UpdateContentMode()
        {
            // コンテンツ表示モードに応じた処理
            // 省略
        }
        
        /// <summary>
        /// DWM属性を設定します
        /// </summary>
        private void SetupDwmAttributes()
        {
            if (_handle == IntPtr.Zero)
                return;
                
            // Windows 10/11でのDWM透過設定
            MARGINS margins = new MARGINS
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1
            };
            
            DwmExtendFrameIntoClientArea(_handle, ref margins);
            
            // アクリルぼかし効果を適用
            uint accentState = ACCENT_ENABLE_BLURBEHIND;
            int accentFlags = 0;
            
            ACCENT_POLICY accent = new ACCENT_POLICY
            {
                AccentState = accentState,
                AccentFlags = accentFlags,
                GradientColor = 0,
                AnimationId = 0
            };
            
            int accentStructSize = Marshal.SizeOf<ACCENT_POLICY>();
            IntPtr accentPtr = Marshal.AllocHGlobal(accentStructSize);
            
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                
                WINDOWCOMPOSITIONATTRIBDATA data = new WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attrib = WCA_ACCENT_POLICY,
                    pvData = accentPtr,
                    cbData = accentStructSize
                };
                
                SetWindowCompositionAttribute(_handle, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
        
        // エフェクト適用メソッド
        private void ApplyBlurEffect(object? parameters) { /* 実装 */ }
        private void ApplyShadowEffect(object? parameters) { /* 実装 */ }
        private void ApplyGlowEffect(object? parameters) { /* 実装 */ }
        private void ApplyFadeEffect(object? parameters) { /* 実装 */ }
        private void RemoveAllEffects() { /* 実装 */ }
    }
}

namespace Baketa.UI.Overlay.Avalonia
{
    /// <summary>
    /// AvaloniaとWindowsオーバーレイウィンドウを統合するアダプター
    /// </summary>
    public class AvaloniaOverlayWindowAdapter : IOverlayWindow
    {
        private readonly WindowsOverlayWindow _windowsOverlay;
        private readonly ITopLevelImpl _topLevelImpl;
        private readonly IInputRoot _inputRoot;
        private readonly ILogger? _logger;
        private bool _disposed;
        
        /// <summary>
        /// 新しいAvaloniaオーバーレイウィンドウアダプターを初期化します
        /// </summary>
        /// <param name="windowsOverlay">Windowsオーバーレイウィンドウ</param>
        /// <param name="logger">ロガー</param>
        public AvaloniaOverlayWindowAdapter(WindowsOverlayWindow windowsOverlay, ILogger? logger = null)
        {
            _windowsOverlay = windowsOverlay ?? throw new ArgumentNullException(nameof(windowsOverlay));
            _logger = logger;
            
            // Avalonia TopLevelImplの作成
            // 省略
            
            _logger?.LogInformation("Avaloniaオーバーレイウィンドウアダプターが初期化されました。");
        }
        
        // IOverlayWindowインターフェース実装
        // 省略
        
        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースを解放するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;
                
            if (disposing)
            {
                _windowsOverlay?.Dispose();
                _topLevelImpl?.Dispose();
            }
            
            _disposed = true;
        }
    }
}
```

## XAML設計案

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:local="using:Baketa.UI.Controls">
    <!-- オーバーレイウィンドウのスタイル定義 -->
    <Style Selector="local|OverlayTextBlock">
        <Setter Property="FontSize" Value="16"/>
        <Setter Property="FontWeight" Value="Normal"/>
        <Setter Property="Foreground" Value="White"/>
        <Setter Property="Background" Value="#80000000"/>
        <Setter Property="Padding" Value="10"/>
        <Setter Property="CornerRadius" Value="5"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="BorderBrush" Value="#AAFFFFFF"/>
        <Setter Property="TextWrapping" Value="Wrap"/>
        <Setter Property="Template">
            <ControlTemplate>
                <Panel>
                    <!-- 背景効果（ブラー、影など） -->
                    <Border Name="PART_Background"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="{TemplateBinding CornerRadius}"
                            BoxShadow="0 0 10 0 #40000000">
                        <Border.Transitions>
                            <Transitions>
                                <DoubleTransition Property="Opacity" Duration="0:0:0.2"/>
                            </Transitions>
                        </Border.Transitions>
                    </Border>
                    
                    <!-- テキストコンテンツ -->
                    <TextBlock Name="PART_TextBlock"
                               Text="{TemplateBinding Text}"
                               Foreground="{TemplateBinding Foreground}"
                               FontSize="{TemplateBinding FontSize}"
                               FontWeight="{TemplateBinding FontWeight}"
                               TextWrapping="{TemplateBinding TextWrapping}"
                               Margin="{TemplateBinding Padding}">
                        <TextBlock.Transitions>
                            <Transitions>
                                <DoubleTransition Property="Opacity" Duration="0:0:0.2"/>
                            </Transitions>
                        </TextBlock.Transitions>
                    </TextBlock>
                </Panel>
            </ControlTemplate>
        </Setter>
    </Style>
</ResourceDictionary>
```

## カスタムコントロール例

```csharp
namespace Baketa.UI.Controls
{
    /// <summary>
    /// オーバーレイテキストブロックコントロール
    /// </summary>
    public class OverlayTextBlock : ContentControl
    {
        /// <summary>
        /// テキストプロパティ
        /// </summary>
        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<OverlayTextBlock, string>(
                nameof(Text),
                defaultValue: string.Empty);
                
        /// <summary>
        /// コーナー半径プロパティ
        /// </summary>
        public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
            AvaloniaProperty.Register<OverlayTextBlock, CornerRadius>(
                nameof(CornerRadius),
                defaultValue: new CornerRadius(5));
                
        /// <summary>
        /// テキスト折り返しプロパティ
        /// </summary>
        public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
            AvaloniaProperty.Register<OverlayTextBlock, TextWrapping>(
                nameof(TextWrapping),
                defaultValue: TextWrapping.Wrap);
                
        /// <summary>
        /// エフェクトタイププロパティ
        /// </summary>
        public static readonly StyledProperty<OverlayEffectType> EffectTypeProperty =
            AvaloniaProperty.Register<OverlayTextBlock, OverlayEffectType>(
                nameof(EffectType),
                defaultValue: OverlayEffectType.Normal);
                
        /// <summary>
        /// エフェクト強度プロパティ
        /// </summary>
        public static readonly StyledProperty<double> EffectIntensityProperty =
            AvaloniaProperty.Register<OverlayTextBlock, double>(
                nameof(EffectIntensity),
                defaultValue: 0.5);
                
        /// <summary>
        /// テキスト
        /// </summary>
        public string Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
        
        /// <summary>
        /// コーナー半径
        /// </summary>
        public CornerRadius CornerRadius
        {
            get => GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }
        
        /// <summary>
        /// テキスト折り返し
        /// </summary>
        public TextWrapping TextWrapping
        {
            get => GetValue(TextWrappingProperty);
            set => SetValue(TextWrappingProperty, value);
        }
        
        /// <summary>
        /// エフェクトタイプ
        /// </summary>
        public OverlayEffectType EffectType
        {
            get => GetValue(EffectTypeProperty);
            set => SetValue(EffectTypeProperty, value);
        }
        
        /// <summary>
        /// エフェクト強度
        /// </summary>
        public double EffectIntensity
        {
            get => GetValue(EffectIntensityProperty);
            set => SetValue(EffectIntensityProperty, value);
        }
        
        /// <summary>
        /// 新しいオーバーレイテキストブロックを初期化します
        /// </summary>
        public OverlayTextBlock()
        {
            // クラスにスタイルを適用
        }
        
        /// <override />
        protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
        {
            base.OnPropertyChanged(change);
            
            if (change.Property == EffectTypeProperty || change.Property == EffectIntensityProperty)
            {
                UpdateEffect();
            }
        }
        
        /// <summary>
        /// エフェクトを更新します
        /// </summary>
        private void UpdateEffect()
        {
            // エフェクト更新ロジック
            // 省略
        }
    }
    
    /// <summary>
    /// オーバーレイエフェクトタイプ
    /// </summary>
    public enum OverlayEffectType
    {
        /// <summary>
        /// 通常
        /// </summary>
        Normal,
        
        /// <summary>
        /// ぼかし
        /// </summary>
        Blur,
        
        /// <summary>
        /// 影
        /// </summary>
        Shadow,
        
        /// <summary>
        /// グロー
        /// </summary>
        Glow,
        
        /// <summary>
        /// グラデーション
        /// </summary>
        Gradient
    }
}
```

## 実装上の注意点
- Windows APIとの直接連携部分は、プラットフォーム固有コードとして適切に分離する
- クリックスルー機能の実装は、ゲーム操作への干渉を最小限に抑える
- パフォーマンスを最適化し、特にグラフィックス処理の負荷を抑える
- 様々なスクリーンタイプとDPIスケーリングに適切に対応する
- フルスクリーンゲームとの互換性を確保する
- ウィンドウの重ね順とアクティブ状態を適切に管理する
- リソースを確実に解放し、メモリリークを防止する

## 関連Issue/参考
- 親Issue: #11 オーバーレイウィンドウ
- 関連Issue: #10-2 メインウィンドウUIデザインの実装
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\overlay-window.md
- 参照: E:\dev\Baketa\docs\2-development\platform-interop\windows-overlay.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (4.2 リソース解放とDisposable)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: high`
- `component: ui`
