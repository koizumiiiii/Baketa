# Avaloniaスタイリング優先度調査レポート

## 調査概要

InPlaceTranslationOverlayWindowのBorder角丸無効化を試みる過程で、Avaloniaフレームワークにおけるスタイル優先度の詳細な動作を調査した。

## 調査背景

- **目的**: InPlaceTranslationOverlayWindowのBorderコンポーネントで `CornerRadius="0"` を確実に適用したい
- **問題**: 複数の方法で角丸を無効化しても、FluentThemeのデフォルト角丸が優先されてしまう
- **調査期間**: 2025年7月31日

## スタイル適用優先度の調査結果

### 1. スタイルファイルの構成と対象コンポーネント

| ファイル名 | 対象コンポーネント | 角丸設定 | 使用状況 |
|-----------|------------------|----------|----------|
| **BasicStyles.axaml** | 全体の基本コンポーネント | `Border.notification: CornerRadius="4"` | ❌ **未使用** (App.axamlで読み込み無し) |
| **OverlayStyles.axaml** | OverlayTextBlockコントロール専用 | `CornerRadius="0"` | ✅ 使用中 |
| **MainOverlayStyles.axaml** | MainOverlayView専用 | 一部に角丸設定あり | ✅ 使用中 |
| **OperationalControlStyles.axaml** | 操作UIコントロール専用 | `Border.operational-container: CornerRadius="8"` | ✅ 使用中 |
| **TranslationSettingsStyles.axaml** | 翻訳設定UI専用 | 複数の角丸定義あり | ❌ **未使用** (App.axamlで読み込み無し) |
| **FontStyles.axaml** | フォント定義専用 | 角丸設定なし | ✅ 使用中 |

### 2. App.axamlでのスタイル読み込み順序

```xml
<Application.Styles>
    <FluentTheme />                                    <!-- 1. Avaloniaデフォルト -->
    <StyleInclude Source="/Styles/FontStyles.axaml"/>  <!-- 2. フォント -->
    <StyleInclude Source="/Styles/OverlayStyles.axaml"/> <!-- 3. オーバーレイ -->
    <StyleInclude Source="/Styles/OperationalControlStyles.axaml"/> <!-- 4. 操作UI -->
    <StyleInclude Source="/Styles/MainOverlayStyles.axaml"/>        <!-- 5. メインオーバーレイ -->
</Application.Styles>
```

### 3. 試行した角丸無効化手法と結果

#### 3.1 XAMLレベルでの試行

| 手法 | コード例 | 結果 |
|------|---------|------|
| **インライン属性** | `<Border CornerRadius="0">` | ❌ 効果なし |
| **詳細指定** | `<Border CornerRadius="0,0,0,0">` | ❌ 効果なし |
| **Window.Styles内セレクター** | `<Style Selector="Border">` | ❌ 効果なし |
| **ID指定セレクター** | `<Style Selector="Border#InPlaceOverlayBorder">` | ❌ 効果なし |
| **属性指定セレクター** | `<Style Selector="Border[Name=InPlaceOverlayBorder]">` | ❌ 効果なし |
| **Border内ローカルスタイル** | `<Border.Styles><Style Selector="Border">` | ❌ 効果なし |
| **App.axaml内グローバルスタイル** | FluentTheme後に配置 | ❌ 効果なし |

#### 3.2 C#コードレベルでの試行

| 手法 | コード例 | 結果 |
|------|---------|------|
| **直接設定** | `border.CornerRadius = new CornerRadius(0);` | ❌ 効果なし |
| **プロパティ設定** | `border.SetValue(Border.CornerRadiusProperty, new CornerRadius(0));` | ❌ 効果なし |
| **現在値設定** | `border.SetCurrentValue(Border.CornerRadiusProperty, new CornerRadius(0));` | ❌ 効果なし |
| **遅延実行** | `Dispatcher.UIThread.Post()` で後から設定 | ❌ 効果なし |
| **タイマー実行** | 100ms後に設定 | ❌ 効果なし |
| **プロパティ変更監視** | `PropertyChanged`イベントで強制上書き | ❌ 効果なし |

### 4. FluentTheme優先度の分析

#### 4.1 検証結果
- **FluentThemeの優先度は極めて高い**: 全ての手法を試しても角丸を無効化できない
- **レンダリング時に強制適用**: C#での実行時設定も上書きされる
- **プロパティ変更監視も無効**: PropertyChangedイベントでの強制上書きも効果なし

#### 4.2 推定される優先度順序
1. **FluentTheme内部設定** (最高優先度)
2. App.axaml内スタイル
3. Window.Styles内スタイル
4. インライン属性
5. C#での実行時設定

## 実用的な解決策の提案

### 1. FluentTheme以外のテーマ使用
```xml
<Application.Styles>
    <!-- FluentThemeの代わりに別のテーマを使用 -->
    <SimpleTheme />
    <!-- または自作テーマ -->
</Application.Styles>
```

### 2. カスタムBorderコントロール作成
```csharp
public class NoCornerRadiusBorder : Border
{
    static NoCornerRadiusBorder()
    {
        CornerRadiusProperty.OverrideDefaultValue<NoCornerRadiusBorder>(new CornerRadius(0));
    }
    
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == CornerRadiusProperty)
        {
            // 角丸設定を強制的に0に戻す
            SetCurrentValue(CornerRadiusProperty, new CornerRadius(0));
            return;
        }
        base.OnPropertyChanged(change);
    }
}
```

### 3. CSSライクな!important相当の実装
Avaloniaには現在CSSの`!important`に相当する機能は存在しない。

## 学んだこと

### 1. Avaloniaスタイルシステムの特徴
- **テーマベースシステム**: FluentThemeなどのテーマが最優先
- **カスケード性**: 複数のスタイルソースが階層的に適用
- **実行時上書き制限**: テーマレベルの設定は実行時変更が困難

### 2. デバッグ手法
- **視覚的確認**: `Background="Red"` `BorderBrush="Red"` による表示確認
- **段階的テスト**: 複数の手法を組み合わせて優先度を特定
- **ログ出力**: プロパティ変更イベントの監視

### 3. プログラム設計への影響
- **テーマ選択の重要性**: UIフレームワーク選択時にテーマのカスタマイズ性を考慮
- **カスタムコントロールの活用**: 標準コントロールの制限を回避
- **フレームワーク制約の受容**: 完全なカスタマイズが不可能な場合の妥協点

## 追加考察: 他のスタイル設定への影響

### FluentTheme優先度による影響範囲

今回の調査でFluentThemeの優先度が極めて高いことが判明したため、**他のスタイル設定も効いていない可能性**がある。

#### 検証が必要な項目
1. **OverlayStyles.axaml**: OverlayTextBlockの各種設定
2. **MainOverlayStyles.axaml**: ボタンのCornerRadius設定
3. **OperationalControlStyles.axaml**: Border.operational-containerの設定
4. **FontStyles.axaml**: フォント設定（比較的効きやすい）

#### 不要になる可能性のあるスタイル設定
```xml
<!-- これらの設定がFluentThemeに上書きされている可能性 -->
<Setter Property="CornerRadius" Value="8"/>
<Setter Property="BorderThickness" Value="1"/>
<Setter Property="BoxShadow" Value="0 2 8 0 #40000000"/>
```

## FluentTheme非使用時の対応策

### 1. FluentThemeを使用しない場合の設定

```xml
<Application.Styles>
    <!-- FluentThemeをコメントアウト -->
    <!-- <FluentTheme /> -->
    
    <!-- 基本的なデフォルトスタイルを手動定義 -->
    <Style Selector="Button">
        <Setter Property="Background" Value="#E1E1E1"/>
        <Setter Property="Foreground" Value="Black"/>
        <Setter Property="Padding" Value="8,4"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="MinHeight" Value="24"/>
    </Style>
    
    <Style Selector="TextBox">
        <Setter Property="Background" Value="White"/>
        <Setter Property="Foreground" Value="Black"/>
        <Setter Property="BorderBrush" Value="Gray"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="4"/>
    </Style>
    
    <!-- カスタムスタイル適用 -->
    <StyleInclude Source="/Styles/FontStyles.axaml"/>
    <StyleInclude Source="/Styles/OverlayStyles.axaml"/>
    <StyleInclude Source="/Styles/OperationalControlStyles.axaml"/>
    <StyleInclude Source="/Styles/MainOverlayStyles.axaml"/>
</Application.Styles>
```

### 2. FluentTheme非使用時の注意事項

#### 2.1 失われる機能
- **テーマ機能**: ダークモード、ライトモードの自動切り替え
- **アクセシビリティ**: 高コントラストテーマ
- **OS連携**: システムテーマとの同期
- **レスポンシブ**: 画面サイズに応じたスタイル調整

#### 2.2 手動定義が必要なコンポーネント
```xml
<!-- 基本コンポーネントの最低限スタイル -->
<Style Selector="Window">
    <Setter Property="Background" Value="White"/>
    <Setter Property="Foreground" Value="Black"/>
</Style>

<Style Selector="Grid">
    <Setter Property="Background" Value="Transparent"/>
</Style>

<Style Selector="StackPanel">
    <Setter Property="Background" Value="Transparent"/>
</Style>

<Style Selector="Border">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="BorderBrush" Value="Gray"/>
</Style>

<Style Selector="ScrollViewer">
    <Setter Property="Background" Value="Transparent"/>
</Style>

<Style Selector="ListBox">
    <Setter Property="Background" Value="White"/>
    <Setter Property="BorderBrush" Value="Gray"/>
    <Setter Property="BorderThickness" Value="1"/>
</Style>

<Style Selector="MenuItem">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Padding" Value="8,4"/>
</Style>
```

#### 2.3 クロスプラットフォーム対応の課題
- **Windows**: 比較的問題なし
- **macOS**: ネイティブルック&フィールの喪失
- **Linux**: フォント指定の重要性増加

#### 2.4 パフォーマンスへの影響
- **メリット**: テーマ処理のオーバーヘッド削減
- **デメリット**: スタイル定義の増加によるメモリ使用量増加

### 3. 段階的移行戦略

#### Phase 1: 調査・検証
```xml
<!-- テスト用: FluentThemeと併用して効果を確認 -->
<Application.Styles>
    <FluentTheme />
    <!-- テスト用スタイル -->
    <Style Selector="Button.test-button">
        <Setter Property="Background" Value="Red"/>
        <Setter Property="CornerRadius" Value="0"/>
    </Style>
</Application.Styles>
```

#### Phase 2: 部分置換
```xml
<!-- 特定コンポーネントのみFluentTheme無効化 -->
<Style Selector="Border.custom-border">
    <Setter Property="CornerRadius" Value="0"/>
    <Setter Property="Background" Value="Transparent"/>
</Style>
```

#### Phase 3: 完全移行
```xml
<!-- FluentTheme完全削除 + 全スタイル再定義 -->
```

### 4. 代替テーマの選択肢

#### 4.1 SimpleTheme
```xml
<Application.Styles>
    <SimpleTheme />
</Application.Styles>
```
- **メリット**: 軽量、カスタマイズしやすい
- **デメリット**: 機能が限定的

#### 4.2 カスタムテーマ作成
```csharp
public class BaketaTheme : Styles
{
    public BaketaTheme()
    {
        // カスタムテーマの実装
    }
}
```

#### 4.3 Material Design Theme
```xml
<StyleInclude Source="avares://Material.Avalonia/Material.xaml" />
```

## 推奨アプローチ

### 短期的対応
1. **現状維持**: FluentThemeを使用し、角丸を受け入れる
2. **影響調査**: 他のスタイル設定の効果を検証

### 長期的対応
1. **段階的移行**: 重要なカスタマイズから順次FluentTheme依存を削減
2. **カスタムテーマ**: Baketaプロジェクト専用テーマの開発

## 結論

InPlaceTranslationOverlayWindowでの角丸無効化は、FluentThemeの制約により技術的に困難であることが判明した。
さらに、**FluentThemeの高い優先度により、他のカスタムスタイルも効いていない可能性**がある。

実用的な解決策としては：

1. **現状維持**: FluentThemeの制約を受け入れる（推奨）
2. **影響調査**: 他のスタイル設定の効果を検証し、不要な設定を削除
3. **段階的移行**: 重要なカスタマイズから順次FluentTheme非依存に移行
4. **カスタムテーマ**: 完全なコントロールが必要な場合はカスタムテーマを開発

今回の調査により、Avaloniaにおけるテーマシステムの強力さと制約、そしてカスタマイズの難しさを深く理解できた。

---
*調査者: Claude Code Assistant*  
*調査日: 2025年7月31日*  
*更新日: 2025年7月31日*