# Issue 11-3: オーバーレイUIデザインとアニメーションの実装

## 概要
オーバーレイウィンドウのUIデザインとアニメーション効果を実装します。これにより、翻訳テキストが視覚的に魅力的な方法で表示され、読みやすく、かつゲームプレイを邪魔しないようになります。

## 目的・理由
効果的なオーバーレイUIデザインとアニメーションは以下の理由で重要です：

1. 翻訳テキストの視認性と可読性を向上させる
2. 翻訳内容とゲーム画面を明確に区別する
3. スムーズで非侵入的なユーザー体験を提供する
4. ユーザーの好みに合わせたカスタマイズ可能なデザインを提供する
5. 注意を引き過ぎず、ゲームへの没入感を保つ

## 詳細
- 基本的なオーバーレイテキストコントロールの設計と実装
- 複数のテーマとスタイルの実装
- テキスト表示アニメーションの実装
- カスタマイズ可能なスタイル設定の実装

## タスク分解
- [ ] 基本UIコントロールの設計
  - [ ] `OverlayTextBlock`カスタムコントロールの設計と実装
  - [ ] `OverlayPanel`カスタムコントロールの設計と実装
  - [ ] テキスト関連プロパティ（フォント、サイズ、カラー等）の実装
  - [ ] 背景関連プロパティ（色、透明度、ブラー等）の実装
- [ ] デザインテーマの実装
  - [ ] ライトテーマの実装
  - [ ] ダークテーマの実装
  - [ ] ゲームフレンドリーテーマの実装
  - [ ] 高コントラストテーマの実装（アクセシビリティ対応）
- [ ] テキスト表示アニメーション
  - [ ] フェードインアニメーションの実装
  - [ ] タイピングアニメーションの実装
  - [ ] スライドインアニメーションの実装
  - [ ] 文字単位のアニメーション実装
- [ ] UIデザインエフェクト
  - [ ] 背景ぼかしエフェクトの実装
  - [ ] 影効果の実装
  - [ ] グロー効果の実装
  - [ ] 枠線と角丸効果の実装
- [ ] テキストレイアウトの最適化
  - [ ] 長文テキストの自動折り返し
  - [ ] 最適な行数とテキストサイズ調整
  - [ ] テキスト間の適切な余白設定
  - [ ] 複数段落レイアウトの実装
- [ ] インタラクティブ要素
  - [ ] ホバー効果の実装
  - [ ] 拡大/縮小機能の実装
  - [ ] テキスト選択と検索機能の実装
  - [ ] ピン留め機能の実装
- [ ] カスタマイズ設定との連携
  - [ ] ユーザー設定からのスタイル読み込み機能
  - [ ] リアルタイムプレビュー機能
  - [ ] プリセット設定の管理
- [ ] 単体テストの実装

## スタイル設計案
```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:controls="using:Baketa.UI.Controls">

    <!-- 基本的なオーバーレイテキストブロックスタイル -->
    <Style Selector="controls|OverlayTextBlock">
        <Setter Property="FontFamily" Value="{DynamicResource DefaultFontFamily}"/>
        <Setter Property="FontSize" Value="{DynamicResource DefaultFontSize}"/>
        <Setter Property="Foreground" Value="{DynamicResource OverlayTextForeground}"/>
        <Setter Property="Background" Value="{DynamicResource OverlayTextBackground}"/>
        <Setter Property="Padding" Value="12"/>
        <Setter Property="CornerRadius" Value="8"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="BorderBrush" Value="{DynamicResource OverlayBorderBrush}"/>
        <Setter Property="HorizontalAlignment" Value="Left"/>
        <Setter Property="VerticalAlignment" Value="Top"/>
        <Setter Property="MaxWidth" Value="500"/>
        <Setter Property="TextWrapping" Value="Wrap"/>
        <Setter Property="Template">
            <ControlTemplate>
                <Panel>
                    <!-- 背景効果 -->
                    <Border Name="PART_Background"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="{TemplateBinding CornerRadius}"
                            BoxShadow="{TemplateBinding BoxShadow}">
                        <Border.Transitions>
                            <Transitions>
                                <DoubleTransition Property="Opacity" Duration="0:0:0.2"/>
                                <ThicknessTransition Property="Margin" Duration="0:0:0.2"/>
                            </Transitions>
                        </Border.Transitions>
                    </Border>
                    
                    <!-- テキストコンテンツ -->
                    <TextBlock Name="PART_TextBlock"
                               Text="{TemplateBinding Text}"
                               Foreground="{TemplateBinding Foreground}"
                               FontFamily="{TemplateBinding FontFamily}"
                               FontSize="{TemplateBinding FontSize}"
                               FontWeight="{TemplateBinding FontWeight}"
                               TextWrapping="{TemplateBinding TextWrapping}"
                               LineHeight="{TemplateBinding LineHeight}"
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
    
    <!-- ダークテーマのオーバーレイスタイル -->
    <Style Selector="controls|OverlayTextBlock.Dark">
        <Setter Property="Foreground" Value="White"/>
        <Setter Property="Background" Value="#B0000000"/>
        <Setter Property="BorderBrush" Value="#40FFFFFF"/>
        <Setter Property="BoxShadow" Value="0 0 10 0 #40000000"/>
    </Style>
    
    <!-- ライトテーマのオーバーレイスタイル -->
    <Style Selector="controls|OverlayTextBlock.Light">
        <Setter Property="Foreground" Value="Black"/>
        <Setter Property="Background" Value="#B0FFFFFF"/>
        <Setter Property="BorderBrush" Value="#40000000"/>
        <Setter Property="BoxShadow" Value="0 0 10 0 #40000000"/>
    </Style>
    
    <!-- ゲームフレンドリーテーマのオーバーレイスタイル -->
    <Style Selector="controls|OverlayTextBlock.GameFriendly">
        <Setter Property="Foreground" Value="#EEFFEE"/>
        <Setter Property="Background" Value="#80202040"/>
        <Setter Property="BorderBrush" Value="#6080C0FF"/>
        <Setter Property="BoxShadow" Value="0 0 15 0 #4000A0FF"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>
    
    <!-- 高コントラストテーマのオーバーレイスタイル -->
    <Style Selector="controls|OverlayTextBlock.HighContrast">
        <Setter Property="Foreground" Value="Yellow"/>
        <Setter Property="Background" Value="Black"/>
        <Setter Property="BorderBrush" Value="White"/>
        <Setter Property="BorderThickness" Value="2"/>
        <Setter Property="FontWeight" Value="Bold"/>
    </Style>
    
    <!-- アニメーション適用スタイル - フェードイン -->
    <Style Selector="controls|OverlayTextBlock.FadeIn">
        <Style.Animations>
            <Animation Duration="0:0:0.3" FillMode="Forward">
                <KeyFrame Cue="0%">
                    <Setter Property="Opacity" Value="0"/>
                </KeyFrame>
                <KeyFrame Cue="100%">
                    <Setter Property="Opacity" Value="1"/>
                </KeyFrame>
            </Animation>
        </Style.Animations>
    </Style>
    
    <!-- アニメーション適用スタイル - スライドイン -->
    <Style Selector="controls|OverlayTextBlock.SlideIn">
        <Style.Animations>
            <Animation Duration="0:0:0.3" FillMode="Forward">
                <KeyFrame Cue="0%">
                    <Setter Property="TranslateTransform.Y" Value="20"/>
                    <Setter Property="Opacity" Value="0"/>
                </KeyFrame>
                <KeyFrame Cue="100%">
                    <Setter Property="TranslateTransform.Y" Value="0"/>
                    <Setter Property="Opacity" Value="1"/>
                </KeyFrame>
            </Animation>
        </Style.Animations>
    </Style>
</ResourceDictionary>
```

## カスタムコントロール設計案
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
                defaultValue: new CornerRadius(8));
                
        /// <summary>
        /// ボックスシャドウプロパティ
        /// </summary>
        public static readonly StyledProperty<BoxShadows> BoxShadowProperty =
            AvaloniaProperty.Register<OverlayTextBlock, BoxShadows>(
                nameof(BoxShadow),
                defaultValue: default);
                
        /// <summary>
        /// 行の高さプロパティ
        /// </summary>
        public static readonly StyledProperty<double> LineHeightProperty =
            AvaloniaProperty.Register<OverlayTextBlock, double>(
                nameof(LineHeight),
                defaultValue: 1.4);
                
        /// <summary>
        /// アニメーションタイププロパティ
        /// </summary>
        public static readonly StyledProperty<OverlayAnimationType> AnimationTypeProperty =
            AvaloniaProperty.Register<OverlayTextBlock, OverlayAnimationType>(
                nameof(AnimationType),
                defaultValue: OverlayAnimationType.FadeIn);
                
        /// <summary>
        /// アニメーション速度プロパティ
        /// </summary>
        public static readonly StyledProperty<double> AnimationSpeedProperty =
            AvaloniaProperty.Register<OverlayTextBlock, double>(
                nameof(AnimationSpeed),
                defaultValue: 1.0);
                
        /// <summary>
        /// ブラー効果プロパティ
        /// </summary>
        public static readonly StyledProperty<double> BlurRadiusProperty =
            AvaloniaProperty.Register<OverlayTextBlock, double>(
                nameof(BlurRadius),
                defaultValue: 0.0);
                
        /// <summary>
        /// テキストエフェクトプロパティ
        /// </summary>
        public static readonly StyledProperty<OverlayTextEffect> TextEffectProperty =
            AvaloniaProperty.Register<OverlayTextBlock, OverlayTextEffect>(
                nameof(TextEffect),
                defaultValue: OverlayTextEffect.None);
                
        /// <summary>
        /// テキストアウトラインプロパティ
        /// </summary>
        public static readonly StyledProperty<double> TextOutlineWidthProperty =
            AvaloniaProperty.Register<OverlayTextBlock, double>(
                nameof(TextOutlineWidth),
                defaultValue: 0.0);
                
        /// <summary>
        /// テキストアウトラインカラープロパティ
        /// </summary>
        public static readonly StyledProperty<IBrush> TextOutlineColorProperty =
            AvaloniaProperty.Register<OverlayTextBlock, IBrush>(
                nameof(TextOutlineColor),
                defaultValue: Brushes.Transparent);
                
        /// <summary>
        /// マウスホバーで強調表示プロパティ
        /// </summary>
        public static readonly StyledProperty<bool> HighlightOnHoverProperty =
            AvaloniaProperty.Register<OverlayTextBlock, bool>(
                nameof(HighlightOnHover),
                defaultValue: false);
                
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
        /// ボックスシャドウ
        /// </summary>
        public BoxShadows BoxShadow
        {
            get => GetValue(BoxShadowProperty);
            set => SetValue(BoxShadowProperty, value);
        }
        
        /// <summary>
        /// 行の高さ
        /// </summary>
        public double LineHeight
        {
            get => GetValue(LineHeightProperty);
            set => SetValue(LineHeightProperty, value);
        }
        
        /// <summary>
        /// アニメーションタイプ
        /// </summary>
        public OverlayAnimationType AnimationType
        {
            get => GetValue(AnimationTypeProperty);
            set => SetValue(AnimationTypeProperty, value);
        }
        
        /// <summary>
        /// アニメーション速度
        /// </summary>
        public double AnimationSpeed
        {
            get => GetValue(AnimationSpeedProperty);
            set => SetValue(AnimationSpeedProperty, value);
        }
        
        /// <summary>
        /// ブラー効果
        /// </summary>
        public double BlurRadius
        {
            get => GetValue(BlurRadiusProperty);
            set => SetValue(BlurRadiusProperty, value);
        }
        
        /// <summary>
        /// テキストエフェクト
        /// </summary>
        public OverlayTextEffect TextEffect
        {
            get => GetValue(TextEffectProperty);
            set => SetValue(TextEffectProperty, value);
        }
        
        /// <summary>
        /// テキストアウトライン幅
        /// </summary>
        public double TextOutlineWidth
        {
            get => GetValue(TextOutlineWidthProperty);
            set => SetValue(TextOutlineWidthProperty, value);
        }
        
        /// <summary>
        /// テキストアウトラインカラー
        /// </summary>
        public IBrush TextOutlineColor
        {
            get => GetValue(TextOutlineColorProperty);
            set => SetValue(TextOutlineColorProperty, value);
        }
        
        /// <summary>
        /// マウスホバーで強調表示
        /// </summary>
        public bool HighlightOnHover
        {
            get => GetValue(HighlightOnHoverProperty);
            set => SetValue(HighlightOnHoverProperty, value);
        }
        
        // アニメーション用プライベートフィールド
        private TextBlock? _textBlock;
        private Border? _background;
        private bool _isAnimating;
        private CancellationTokenSource? _animationCts;
        
        /// <summary>
        /// 新しいオーバーレイテキストブロックを初期化します
        /// </summary>
        public OverlayTextBlock()
        {
            this.GetObservable(TextProperty).Subscribe(OnTextChanged);
            this.GetObservable(AnimationTypeProperty).Subscribe(OnAnimationTypeChanged);
            
            this.PointerEntered += OverlayTextBlock_PointerEntered;
            this.PointerExited += OverlayTextBlock_PointerExited;
        }
        
        /// <inheritdoc />
        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            
            _textBlock = e.NameScope.Find<TextBlock>("PART_TextBlock");
            _background = e.NameScope.Find<Border>("PART_Background");
            
            ApplyEffects();
        }
        
        /// <inheritdoc />
        protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
        {
            base.OnPropertyChanged(change);
            
            if (change.Property == TextEffectProperty ||
                change.Property == BlurRadiusProperty ||
                change.Property == TextOutlineWidthProperty ||
                change.Property == TextOutlineColorProperty)
            {
                ApplyEffects();
            }
        }
        
        /// <summary>
        /// テキスト変更時の処理
        /// </summary>
        private void OnTextChanged(string newText)
        {
            if (IsInitialized && AnimationType != OverlayAnimationType.None && !string.IsNullOrEmpty(newText))
            {
                StartAnimation();
            }
        }
        
        /// <summary>
        /// アニメーションタイプ変更時の処理
        /// </summary>
        private void OnAnimationTypeChanged(OverlayAnimationType newAnimationType)
        {
            UpdateAnimationClasses();
        }
        
        /// <summary>
        /// エフェクトを適用します
        /// </summary>
        private void ApplyEffects()
        {
            if (_textBlock == null || _background == null)
                return;
                
            // ブラー効果の適用
            if (BlurRadius > 0)
            {
                var blurEffect = new BlurEffect
                {
                    Radius = BlurRadius
                };
                
                _background.Effect = blurEffect;
            }
            else
            {
                _background.Effect = null;
            }
            
            // テキストエフェクトの適用
            switch (TextEffect)
            {
                case OverlayTextEffect.Shadow:
                    _textBlock.Effect = new DropShadowEffect
                    {
                        ShadowDepth = 1,
                        BlurRadius = 2,
                        Color = Colors.Black
                    };
                    break;
                    
                case OverlayTextEffect.Glow:
                    _textBlock.Effect = new DropShadowEffect
                    {
                        ShadowDepth = 0,
                        BlurRadius = 4,
                        Color = Colors.White
                    };
                    break;
                    
                case OverlayTextEffect.Outline:
                    if (TextOutlineWidth > 0)
                    {
                        // アウトラインエフェクトの実装
                        // 省略
                    }
                    break;
                    
                case OverlayTextEffect.None:
                default:
                    _textBlock.Effect = null;
                    break;
            }
        }
        
        /// <summary>
        /// アニメーション関連のクラスを更新します
        /// </summary>
        private void UpdateAnimationClasses()
        {
            var animationClass = AnimationType switch
            {
                OverlayAnimationType.FadeIn => "FadeIn",
                OverlayAnimationType.SlideIn => "SlideIn",
                OverlayAnimationType.Typing => "Typing",
                _ => null
            };
            
            if (animationClass != null)
            {
                Classes.Add(animationClass);
            }
        }
        
        /// <summary>
        /// アニメーションを開始します
        /// </summary>
        private void StartAnimation()
        {
            if (_isAnimating)
            {
                // 既存のアニメーションをキャンセル
                _animationCts?.Cancel();
                _animationCts?.Dispose();
                _animationCts = null;
            }
            
            _isAnimating = true;
            _animationCts = new CancellationTokenSource();
            
            switch (AnimationType)
            {
                case OverlayAnimationType.Typing:
                    StartTypingAnimation(_animationCts.Token);
                    break;
                    
                case OverlayAnimationType.FadeIn:
                case OverlayAnimationType.SlideIn:
                    // Avaloniaスタイルアニメーションで処理
                    UpdateAnimationClasses();
                    _isAnimating = false;
                    break;
                    
                case OverlayAnimationType.None:
                default:
                    _isAnimating = false;
                    break;
            }
        }
        
        /// <summary>
        /// タイピングアニメーションを開始します
        /// </summary>
        private async void StartTypingAnimation(CancellationToken cancellationToken)
        {
            if (_textBlock == null || string.IsNullOrEmpty(Text))
                return;
                
            string fullText = Text;
            // テキストを一時的に空にする
            _textBlock.Text = string.Empty;
            
            try
            {
                // 文字ごとに表示
                for (int i = 0; i < fullText.Length; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                        
                    _textBlock.Text = fullText.Substring(0, i + 1);
                    
                    // アニメーション速度に基づく遅延
                    int delay = (int)(30 / AnimationSpeed);
                    await Task.Delay(delay, cancellationToken);
                }
                
                // 完了時には完全なテキストを設定
                if (!cancellationToken.IsCancellationRequested)
                {
                    _textBlock.Text = fullText;
                }
            }
            catch (TaskCanceledException)
            {
                // キャンセルされた場合は完全なテキストを表示
                _textBlock.Text = fullText;
            }
            finally
            {
                _isAnimating = false;
            }
        }
        
        /// <summary>
        /// ポインターエンター時の処理
        /// </summary>
        private void OverlayTextBlock_PointerEntered(object? sender, PointerEventArgs e)
        {
            if (HighlightOnHover && _background != null)
            {
                // ホバー時の強調表示
                _background.Opacity = 1.0;
                _background.Margin = new Thickness(-2);
            }
        }
        
        /// <summary>
        /// ポインター退出時の処理
        /// </summary>
        private void OverlayTextBlock_PointerExited(object? sender, PointerEventArgs e)
        {
            if (HighlightOnHover && _background != null)
            {
                // ホバー解除時の表示を戻す
                _background.Opacity = 0.9;
                _background.Margin = new Thickness(0);
            }
        }
    }
    
    /// <summary>
    /// オーバーレイアニメーションタイプ
    /// </summary>
    public enum OverlayAnimationType
    {
        /// <summary>
        /// なし
        /// </summary>
        None,
        
        /// <summary>
        /// フェードイン
        /// </summary>
        FadeIn,
        
        /// <summary>
        /// スライドイン
        /// </summary>
        SlideIn,
        
        /// <summary>
        /// タイピング
        /// </summary>
        Typing
    }
    
    /// <summary>
    /// オーバーレイテキストエフェクト
    /// </summary>
    public enum OverlayTextEffect
    {
        /// <summary>
        /// なし
        /// </summary>
        None,
        
        /// <summary>
        /// 影
        /// </summary>
        Shadow,
        
        /// <summary>
        /// グロー
        /// </summary>
        Glow,
        
        /// <summary>
        /// アウトライン
        /// </summary>
        Outline
    }
}
```

## 使用例
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="using:Baketa.UI.Controls"
        xmlns:vm="using:Baketa.UI.ViewModels"
        x:Class="Baketa.UI.Views.OverlayTestWindow"
        Title="オーバーレイテスト"
        Background="Transparent"
        TransparencyLevelHint="Transparent"
        WindowStartupLocation="CenterScreen"
        Width="800" Height="600">
    
    <Window.DataContext>
        <vm:OverlayTestViewModel/>
    </Window.DataContext>
    
    <Panel>
        <!-- テスト用背景画像 -->
        <Image Source="/Assets/game-screenshot.jpg" Stretch="UniformToFill"/>
        
        <!-- オーバーレイテキストの表示 -->
        <Grid>
            <controls:OverlayTextBlock
                Text="{Binding TranslatedText}"
                Classes="GameFriendly"
                Margin="20"
                Width="400"
                AnimationType="FadeIn"
                HorizontalAlignment="Left"
                VerticalAlignment="Bottom"
                HighlightOnHover="True"/>
            
            <!-- テーマ切り替えコントロール -->
            <StackPanel Orientation="Horizontal"
                        HorizontalAlignment="Right"
                        VerticalAlignment="Top"
                        Margin="10">
                <Button Content="ダーク" Command="{Binding SetThemeCommand}" CommandParameter="Dark"/>
                <Button Content="ライト" Command="{Binding SetThemeCommand}" CommandParameter="Light"/>
                <Button Content="ゲーム" Command="{Binding SetThemeCommand}" CommandParameter="GameFriendly"/>
                <Button Content="高コントラスト" Command="{Binding SetThemeCommand}" CommandParameter="HighContrast"/>
            </StackPanel>
            
            <!-- アニメーション切り替えコントロール -->
            <StackPanel Orientation="Horizontal"
                        HorizontalAlignment="Right"
                        VerticalAlignment="Bottom"
                        Margin="10">
                <Button Content="なし" Command="{Binding SetAnimationCommand}" CommandParameter="None"/>
                <Button Content="フェード" Command="{Binding SetAnimationCommand}" CommandParameter="FadeIn"/>
                <Button Content="スライド" Command="{Binding SetAnimationCommand}" CommandParameter="SlideIn"/>
                <Button Content="タイピング" Command="{Binding SetAnimationCommand}" CommandParameter="Typing"/>
            </StackPanel>
        </Grid>
    </Panel>
</Window>
```

## 実装上の注意点
- フォントの可読性とレンダリング品質を重視したデザイン
- アニメーションのパフォーマンス最適化とスムーズな表示
- 複数のテキストスタイルとエフェクトのメモリとCPU使用量の最適化
- レンダリングに関わるAvalonia UIの制約への対応
- ゲームのフレームレートに影響を与えない軽量な実装
- 可能な限りハードウェアアクセラレーションを活用
- 異なるDPIとスクリーンサイズでの表示の一貫性確保
- アクセシビリティを考慮したコントラストと読みやすさの確保

## 関連Issue/参考
- 親Issue: #11 オーバーレイウィンドウ
- 依存Issue: #11-1 透過ウィンドウとクリックスルー機能の実装
- 関連Issue: #10-5 テーマと国際化対応の基盤実装
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\overlay-design-guidelines.md
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\avalonia-guidelines.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (5.1 メソッドの静的化)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: high`
- `component: ui`
