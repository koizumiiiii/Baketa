using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

namespace Baketa.UI.Controls;

/// <summary>
/// オーバーレイテキストブロック（基盤版）
/// </summary>
public class OverlayTextBlock : ContentControl
{
    /// <summary>表示テキストプロパティ</summary>
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<OverlayTextBlock, string>(
            nameof(Text), 
            defaultValue: string.Empty);

    /// <summary>テーマプリセットプロパティ</summary>
    public new static readonly StyledProperty<OverlayTheme> ThemeProperty =
        AvaloniaProperty.Register<OverlayTextBlock, OverlayTheme>(
            nameof(Theme), 
            defaultValue: OverlayTheme.Auto);

    /// <summary>表示テキスト</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>テーマプリセット</summary>
    public new OverlayTheme Theme
    {
        get => GetValue(ThemeProperty);
        set
        {
            SetValue(ThemeProperty, value);
            // テーマ変更時にスタイル再適用
            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyTheme();
            }
            else
            {
                Dispatcher.UIThread.Post(ApplyTheme);
            }
        }
    }

    /// <summary>アニメーション有効/無効プロパティ</summary>
    public static readonly StyledProperty<bool> AnimationEnabledProperty =
        AvaloniaProperty.Register<OverlayTextBlock, bool>(
            nameof(AnimationEnabled),
            defaultValue: true);

    /// <summary>表示/非表示トグル機能有効/無効プロパティ</summary>
    public static readonly StyledProperty<bool> ToggleVisibilityEnabledProperty =
        AvaloniaProperty.Register<OverlayTextBlock, bool>(
            nameof(ToggleVisibilityEnabled),
            defaultValue: true);
            
    /// <summary>行間プロパティ</summary>
    public static readonly StyledProperty<double> LineHeightProperty =
        AvaloniaProperty.Register<OverlayTextBlock, double>(
            nameof(LineHeight),
            defaultValue: 1.4);
            
    /// <summary>テキスト折り返しプロパティ</summary>
    public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
        AvaloniaProperty.Register<OverlayTextBlock, TextWrapping>(
            nameof(TextWrapping),
            defaultValue: TextWrapping.Wrap);
            
    /// <summary>ボックスシャドウプロパティ</summary>
    public static readonly StyledProperty<BoxShadows> BoxShadowProperty =
        AvaloniaProperty.Register<OverlayTextBlock, BoxShadows>(
            nameof(BoxShadow),
            defaultValue: default);
            
    /// <summary>段落間スペーシングプロパティ</summary>
    public static readonly StyledProperty<double> ParagraphSpacingProperty =
        AvaloniaProperty.Register<OverlayTextBlock, double>(
            nameof(ParagraphSpacing),
            defaultValue: 8.0);

    /// <summary>アニメーション有効/無効</summary>
    public bool AnimationEnabled
    {
        get => GetValue(AnimationEnabledProperty);
        set => SetValue(AnimationEnabledProperty, value);
    }

    /// <summary>表示/非表示トグル機能有効/無効</summary>
    public bool ToggleVisibilityEnabled
    {
        get => GetValue(ToggleVisibilityEnabledProperty);
        set => SetValue(ToggleVisibilityEnabledProperty, value);
    }
    
    /// <summary>行間</summary>
    public double LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }
    
    /// <summary>テキスト折り返し</summary>
    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }
    
    /// <summary>ボックスシャドウ</summary>
    public BoxShadows BoxShadow
    {
        get => GetValue(BoxShadowProperty);
        set => SetValue(BoxShadowProperty, value);
    }
    
    /// <summary>段落間スペーシング</summary>
    public double ParagraphSpacing
    {
        get => GetValue(ParagraphSpacingProperty);
        set => SetValue(ParagraphSpacingProperty, value);
    }

    // アニメーション制御用フィールド
    private bool _isVisible = true;
    private readonly object _animationLock = new();
    
    /// <summary>
    /// コンストラクター
    /// </summary>
    public OverlayTextBlock()
    {
        // 初期化時にテーマを適用
        this.Loaded += (_, _) => ApplyTheme();
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        
        // テーマに基づくスタイル適用
        ApplyTheme();
    }
    
    /// <summary>
    /// テーマを適用します
    /// </summary>
    private void ApplyTheme()
    {
        var themeClass = Theme switch
        {
            OverlayTheme.Light => "Light",
            OverlayTheme.Dark => "Dark",
            OverlayTheme.HighContrast => "HighContrast",
            OverlayTheme.Auto => DetectAutoTheme(),
            _ => "Dark"
        };
        
        Classes.Clear();
        Classes.Add(themeClass);
    }
    
    /// <summary>
    /// 自動テーマ検出
    /// </summary>
    private string DetectAutoTheme()
    {
        // 将来的にはゲーム画面の明度解析等を行う
        // MVP版では現在時刻ベースの簡易実装
        var hour = DateTime.Now.Hour;
        return (hour >= 6 && hour < 18) ? "Light" : "Dark";
    }

    /// <summary>
    /// オーバーレイの表示/非表示をトグルします
    /// </summary>
    public void ToggleVisibility()
    {
        if (!ToggleVisibilityEnabled) return;
        
        lock (_animationLock)
        {
            if (_isVisible)
            {
                HideWithAnimation();
            }
            else
            {
                ShowWithAnimation();
            }
            _isVisible = !_isVisible;
        }
    }
    
    /// <summary>
    /// アニメーション付きで表示
    /// </summary>
    private void ShowWithAnimation()
    {
        if (AnimationEnabled)
        {
            // シンプルなフェードイン
            var fadeIn = new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new QuadraticEaseOut()
            };
            
            Opacity = 0;
            IsVisible = true;
            Transitions = [fadeIn];
            Opacity = DefaultOverlayAppearance.Opacity;
        }
        else
        {
            IsVisible = true;
            Opacity = DefaultOverlayAppearance.Opacity;
        }
    }
    
    /// <summary>
    /// アニメーション付きで非表示
    /// </summary>
    private void HideWithAnimation()
    {
        if (AnimationEnabled)
        {
            // シンプルなフェードアウト
            var fadeOut = new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new QuadraticEaseIn()
            };
            
            Transitions = [fadeOut];
            Opacity = 0;
            
            // アニメーション完了後に非表示（TaskScheduler.Defaultを明示指定）
            Task.Delay(200).ContinueWith(_ =>
            {
                Dispatcher.UIThread.Post(() => IsVisible = false);
            }, TaskScheduler.Default);
        }
        else
        {
            IsVisible = false;
        }
    }
}
