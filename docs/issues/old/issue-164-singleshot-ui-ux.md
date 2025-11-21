# Issue #164: シングルショットモードのUI/UX改善

**優先度**: 🟠 High
**所要時間**: 1-2日
**Epic**: シングルショット翻訳モード
**ラベル**: `priority: high`, `epic: singleshot`, `type: enhancement`, `layer: ui`

---

## 概要

シングルショットモード（#163で実装）のユーザー体験を向上させるため、ボタンの視覚的フィードバック、アイコン、カラースキーム、アニメーションを実装します。ユーザーが現在の状態（待機中/実行中/非活性）を直感的に理解できるUIを提供します。

---

## 背景・目的

### 現状の課題（#163完了後）
- ボタンが機能するが、視覚的フィードバックが不足
- 実行中/待機中/非活性の状態が区別しにくい
- アイコンがなく、ボタンの機能が直感的に理解できない

### 目指す状態
- ボタンの状態（実行中/非活性/ホバー）が色で明確に区別できる
- Live/Singleshotボタンにアイコンを追加し、機能が一目でわかる
- ホバー時・クリック時のアニメーションで操作フィードバックを提供
- 縮小モード時も状態が分かるレイアウト

---

## スコープ

### 実装タスク

#### 1. ボタン状態管理
- [ ] **ボタン状態のViewModelプロパティ追加**
  - `IsLiveActive` (bool): Live翻訳実行中
  - `IsSingleshotActive` (bool): シングルショット実行中（オーバーレイ表示中）
  - `IsLiveEnabled` (bool): Liveボタンが有効か
  - `IsSingleshotEnabled` (bool): Singleshotボタンが有効か

- [ ] **ReactiveUIバインディング**
  - ボタンのIsEnabledをプロパティにバインド
  - ボタンの背景色・前景色を状態に応じて動的変更

#### 2. カラースキーム実装
- [ ] **状態別カラー定義**（ResourceDictionary）
  - **デフォルト（待機中）**:
    - 背景色: `#2C2C2C` (ダークグレー)
    - テキスト色: `#FFFFFF` (白)
    - 透明度: 1.0
  - **ホバー時**:
    - 背景色: `#404040` (ライトグレー)
    - テキスト色: `#FFFFFF` (白)
    - 透明度: 1.0
  - **実行中**:
    - 背景色: `#2C2C2C` (ダークグレー)
    - テキスト色: `#FF0000` (赤)
    - 透明度: 1.0
  - **非活性**:
    - 背景色: `#2C2C2C` (ダークグレー)
    - テキスト色: `#808080` (グレー)
    - 透明度: 0.5

- [ ] **スタイル定義**
  - `LiveButtonStyle` (Avalonia Style)
  - `SingleshotButtonStyle` (Avalonia Style)
  - 状態トリガーでカラー自動切替

#### 3. アイコン追加
- [ ] **アイコン素材の準備**
  - 形式: **SVG** または **PNG**（提供予定）
  - Liveボタン用アイコン: 🎥 (ビデオカメラ)
  - Singleshotボタン用アイコン: 📷 (カメラ)
  - サイズ: 24x24px（推奨）

- [ ] **Avalonia Pathアイコン定義**（SVGの場合）
  ```xml
  <PathIcon Data="M12,4 L12,20 M4,12 L20,12" ... />
  ```

- [ ] **Imageアイコン配置**（PNGの場合）
  ```xml
  <Image Source="/Assets/Icons/live.png" Width="24" Height="24" />
  ```

- [ ] **ボタンレイアウト調整**
  - アイコン + テキストの配置（横並び or 縦並び）
  - 縮小モード時の表示切替

#### 4. 縮小モード対応
- [ ] **縮小時のレイアウト定義**
  - アイコン非表示、テキストのみ表示
  - ボタンサイズ調整（幅: 100px → 80px）
  - 状態カラーは維持（赤/グレー/白）

- [ ] **拡大/縮小ボタンアイコン**
  - 縮小時: `÷` (縮小アイコン)
  - 拡大時: `+` (拡大アイコン)

#### 5. アニメーション実装
- [ ] **ホバー時のアニメーション**
  - 背景色の滑らかな遷移（0.2秒）
  - わずかな拡大効果（Scale: 1.0 → 1.05）

- [ ] **クリック時のアニメーション**
  - ボタン押下時のスケール縮小（Scale: 1.05 → 0.95 → 1.0）
  - 所要時間: 0.3秒

- [ ] **実行中の点滅効果（オプション）**
  - 赤色テキストの微妙なパルス効果
  - ユーザーに「実行中」であることを視覚的に伝える

---

## 技術仕様

### ボタンスタイル定義（Avalonia XAML）

```xml
<!-- Baketa.UI/Styles/ButtonStyles.axaml -->
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- Live翻訳ボタンスタイル -->
  <Style Selector="Button.LiveButton">
    <Setter Property="Background" Value="#2C2C2C" />
    <Setter Property="Foreground" Value="#FFFFFF" />
    <Setter Property="Width" Value="120" />
    <Setter Property="Height" Value="50" />
    <Setter Property="FontSize" Value="14" />
    <Setter Property="Transitions">
      <Transitions>
        <BrushTransition Property="Background" Duration="0:0:0.2" />
        <BrushTransition Property="Foreground" Duration="0:0:0.2" />
        <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.3" />
      </Transitions>
    </Setter>
  </Style>

  <!-- ホバー時 -->
  <Style Selector="Button.LiveButton:pointerover">
    <Setter Property="Background" Value="#404040" />
    <Setter Property="RenderTransform" Value="scale(1.05)" />
  </Style>

  <!-- 押下時 -->
  <Style Selector="Button.LiveButton:pressed">
    <Setter Property="RenderTransform" Value="scale(0.95)" />
  </Style>

  <!-- 実行中（カスタムクラス） -->
  <Style Selector="Button.LiveButton.Active">
    <Setter Property="Foreground" Value="#FF0000" />
  </Style>

  <!-- 非活性 -->
  <Style Selector="Button.LiveButton:disabled">
    <Setter Property="Foreground" Value="#808080" />
    <Setter Property="Opacity" Value="0.5" />
  </Style>

</Styles>
```

---

### ViewModelプロパティ

```csharp
// Baketa.UI/ViewModels/MainWindowViewModel.cs

public class MainWindowViewModel : ViewModelBase
{
    private readonly ITranslationModeService _translationModeService;

    // ボタン状態プロパティ
    [Reactive] public bool IsLiveActive { get; private set; }
    [Reactive] public bool IsSingleshotActive { get; private set; }
    [Reactive] public bool IsLiveEnabled { get; private set; }
    [Reactive] public bool IsSingleshotEnabled { get; private set; }

    public MainWindowViewModel(ITranslationModeService translationModeService)
    {
        _translationModeService = translationModeService;

        // モード変更イベントをサブスクライブ
        _translationModeService.ModeChanged += OnModeChanged;

        // 初期状態設定
        UpdateButtonStates();
    }

    private void OnModeChanged(object? sender, TranslationModeChangedEventArgs e)
    {
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        var mode = _translationModeService.CurrentMode;
        var isSingleshotOverlayVisible = _translationModeService.IsSingleshotActive;

        // Live翻訳の状態
        IsLiveActive = mode == TranslationMode.Live;
        IsLiveEnabled = mode != TranslationMode.Live && !isSingleshotOverlayVisible;

        // シングルショットの状態
        IsSingleshotActive = isSingleshotOverlayVisible;
        IsSingleshotEnabled = mode != TranslationMode.Live;
    }
}
```

---

### XAMLバインディング

```xml
<!-- Baketa.UI/Views/MainWindow.axaml -->

<!-- Liveボタン -->
<Button Classes="LiveButton"
        Classes.Active="{Binding IsLiveActive}"
        IsEnabled="{Binding IsLiveEnabled}"
        Command="{Binding SwitchToLiveCommand}">
  <StackPanel Orientation="Horizontal" Spacing="8">
    <PathIcon Data="M12,4 L12,20 M4,12 L20,12" Width="24" Height="24" />
    <TextBlock Text="Live" VerticalAlignment="Center" />
  </StackPanel>
</Button>

<!-- Singleshotボタン -->
<Button Classes="LiveButton"
        Classes.Active="{Binding IsSingleshotActive}"
        IsEnabled="{Binding IsSingleshotEnabled}"
        Command="{Binding ExecuteSingleshotCommand}">
  <StackPanel Orientation="Horizontal" Spacing="8">
    <PathIcon Data="M8,4 L16,4 L16,16 L8,16 Z" Width="24" Height="24" />
    <TextBlock Text="Singleshot" VerticalAlignment="Center" />
  </StackPanel>
</Button>
```

---

## 動作確認基準

### 必須動作確認項目

- [ ] **デフォルト状態**: ボタンが白色テキスト、ダークグレー背景で表示される
- [ ] **ホバー時**: ボタン背景がライトグレーに変わり、わずかに拡大する
- [ ] **Live実行中**: Liveボタンのテキストが赤色になる
- [ ] **Singleshot実行中**: Singleshotボタンのテキストが赤色になる
- [ ] **非活性時**: ボタンがグレーアウト（テキスト色: #808080、透明度: 0.5）
- [ ] **縮小モード**: アイコンが非表示になり、テキストのみ表示される
- [ ] **アイコン表示**: Live/Singleshotボタンにアイコンが表示される
- [ ] **アニメーション**: ホバー時・クリック時にスムーズなアニメーションが動作する
- [ ] **キーボードフォーカス**: Tabキーでボタン間を移動でき、フォーカス時にボーダーが表示される

### デザインレビュー基準

- [ ] カラーコントラスト比がWCAG 2.1 AA基準（4.5:1以上）を満たす
- [ ] アイコンサイズが適切で、テキストと調和している
- [ ] アニメーション速度が自然で、ユーザーを煩わせない

---

## 依存関係

### Blocked by（先行して完了すべきissue）
- #163: シングルショットモードのコア機能実装

### Blocks（このissue完了後に着手可能なissue）
- #171: メインウィンドウUI刷新（全体的なレイアウト調整）

---

## 変更ファイル

### 新規作成
- `Baketa.UI/Styles/ButtonStyles.axaml`
- `Baketa.UI/Assets/Icons/live.svg` または `live.png`
- `Baketa.UI/Assets/Icons/singleshot.svg` または `singleshot.png`
- `Baketa.UI/Assets/Icons/minimize.svg` または `minimize.png`
- `Baketa.UI/Assets/Icons/maximize.svg` または `maximize.png`

### 修正
- `Baketa.UI/ViewModels/MainWindowViewModel.cs` (+4プロパティ)
- `Baketa.UI/Views/MainWindow.axaml` (ボタンスタイル適用)
- `Baketa.UI/App.axaml` (ButtonStyles.axamlをインポート)

---

## 実装ガイドライン

### アイコン形式について
- **SVG推奨**: スケーラブルで高解像度ディスプレイに対応
- **PNG代替**: SVGが使えない場合は24x24px、48x48px、96x96pxの3サイズを用意
- **アイコンライブラリ**: [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) 等を参考

### カラーパレットの拡張性
- 将来的にLight/Darkテーマ切替に対応するため、カラーはResourceDictionaryで定義
- ハードコードせず、`{StaticResource ButtonBackgroundBrush}` 等を使用

### パフォーマンス考慮
- アニメーションはGPUアクセラレーション対応（`RenderTransform`使用）
- 不要なレンダリングを避けるため、状態変更時のみ再描画

---

## デザインモックアップ（参考）

### 通常モード

```
┌─────────────────┐
│       ÷         │  (縮小ボタン)
│                 │
│   🎥  Live      │  (白色 - デフォルト)
│                 │
│   📷 Singleshot │  (白色 - デフォルト)
│                 │
│   👁️  Visible   │  (白色 - デフォルト)
│                 │
│   ⚙️  Settings  │
│                 │
│  ─────────────  │
│   ⏻  Exit      │
└─────────────────┘
```

### Live実行中

```
┌─────────────────┐
│       ÷         │
│                 │
│   🎥  Live      │  (赤色 - 実行中)
│                 │
│   📷 Singleshot │  (グレー - 非活性, opacity: 0.5)
│                 │
│   👁️  Visible   │  (白色)
│                 │
│   ⚙️  Settings  │  (グレー - 非活性)
│                 │
│  ─────────────  │
│   ⏻  Exit      │  (グレー - 非活性)
└─────────────────┘
```

### 縮小モード

```
┌───────────┐
│     +     │  (拡大ボタン)
│           │
│   Live    │  (赤色 - 実行中)
│           │
│Singleshot │  (グレー - 非活性)
└───────────┘
```

---

## 備考

### アイコン素材の提供について
- アイコン素材（SVG or PNG）はユーザーから提供予定
- 提供されるまでは仮のPathIconで実装を進める

### Avalonia WebViewとの互換性
- #174（広告表示）でWebViewを使用するため、ボタンスタイルが競合しないよう注意
- WebView内のボタンと区別するため、独自のクラス名（`.LiveButton`）を使用

---

**作成日**: 2025-11-18
**作成者**: Claude Code
**関連ドキュメント**: `docs/BETA_DEVELOPMENT_PLAN.md`, `docs/issues/issue-163-singleshot-core.md`
