# Issue #57 実装状況報告 - メインウィンドウUIデザイン

## 1. 実装概要

Issue #57「メインウィンドウUIデザインの実装」について、作業を進め、以下の項目を実装しました。UIデザインの整合性とユーザビリティの改善を目的として、各ビューの実装とスタイル/リソース定義の強化を行いました。

## 2. 更新ファイル一覧

### 2.1 メインUI
- `MainWindow.axaml` - タブアイコンの追加、フォントファミリーの適用
- `HomeView.axaml` - ホーム画面の完全実装、情報表示部分の強化
- `CaptureView.axaml` - 既存実装の確認
- `TranslationView.axaml` - 既存実装の確認
- `OverlayView.axaml` - 既存実装の確認
- `HistoryView.axaml` - 既存実装の確認

### 2.2 スタイルとリソース
- `Typography.axaml` - フォントファミリー定義の追加
- `BasicStyles.axaml` - 新規作成、基本コントロールスタイルの定義
- `Colors.axaml` - テーマ対応機能の追加（ライト/ダークテーマ）
- `Icons.axaml` - アイコン定義の追加

### 2.3 アプリケーション設定
- `App.axaml` - BasicStyles.axamlの参照追加

## 3. 実装内容の詳細

### 3.1 タブナビゲーションの改良

MainWindow.axamlにおいて、各タブにアイコンを追加し、視覚的な分かりやすさを向上させました：

```xml
<TabItem ToolTip.Tip="ホーム画面">
    <TabItem.Header>
        <StackPanel Orientation="Horizontal" Spacing="8">
            <PathIcon Data="{StaticResource HomeIcon}" Width="16" Height="16"/>
            <TextBlock Text="ホーム" VerticalAlignment="Center"/>
        </StackPanel>
    </TabItem.Header>
    <local:HomeView DataContext="{Binding HomeViewModel}"/>
</TabItem>
```

### 3.2 ホーム画面の改良

HomeView.axamlを拡張し、より使いやすく情報量の多い画面に改善しました：

1. **クイックスタートセクション**の実装
   - 主要機能へのアクセスをアイコン付きボタンで提供
   - 各ボタンにアイコンを追加してより直感的に

2. **最近の翻訳**セクションの実装
   - 最近の翻訳履歴の表示
   - 「すべて表示」ボタンの追加

3. **使用状況**セクションの改良
   - キャプチャ状態のカラーインジケーター追加
   - メモリ使用量、実行時間の表示

4. **ヒント**セクションの視覚的改善
   - アイコン付きリスト形式に変更
   - バージョン情報の追加

### 3.3 テーマ対応の強化

Colors.axamlにテーマ対応機能を追加し、ライト/ダークテーマの切り替えに対応：

```xml
<ResourceDictionary.ThemeDictionaries>
    <!-- ライトテーマ -->
    <ResourceDictionary x:Key="Light">
        <!-- 背景色 -->
        <Color x:Key="BackgroundColor">#F5F5F5</Color>
        <!-- 他の色定義... -->
    </ResourceDictionary>
    
    <!-- ダークテーマ -->
    <ResourceDictionary x:Key="Dark">
        <!-- 背景色 -->
        <Color x:Key="BackgroundColor">#303030</Color>
        <!-- 他の色定義... -->
    </ResourceDictionary>
</ResourceDictionary.ThemeDictionaries>
```

### 3.4 フォント設定の改善

Typography.axamlにフォント定義を追加し、フォントファイルを実際に導入しました：

```
/Baketa.UI/Assets/Fonts/
  ├── LINESeedJP/    # 日本語用 (OTF形式)
  ├── LINESeedEN/    # 英語用 (OTF形式)
  ├── NotoSans/      # その他言語用 (TTF形式)
      ├── NotoSansSC-Regular.ttf
      └── ...
```

フォント定義を更新し、日本語用と英語用に個別のLINE Seedフォントを設定：

```xml
<!-- フォントファミリー定義 -->
<FontFamily x:Key="JapaneseFontFamily">avares://Baketa.UI/Assets/Fonts/LINESeedJP/LINESeedJP_OTF_Rg.otf#LINE Seed JP</FontFamily>
<FontFamily x:Key="EnglishFontFamily">avares://Baketa.UI/Assets/Fonts/LINESeedEN/LINESeedEN_OTF_Rg.otf#LINE Seed EN</FontFamily>
<FontFamily x:Key="SecondaryFontFamily">avares://Baketa.UI/Assets/Fonts/NotoSans/NotoSansSC-Regular.ttf#Noto Sans SC</FontFamily>
```

### 3.5 グローバルスタイルの導入

新規にBasicStyles.axamlを作成し、基本コントロールにフォント設定を適用：

```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 基本TextBoxスタイル -->
    <Style Selector="TextBox">
        <Setter Property="FontFamily" Value="{DynamicResource FallbackFontFamily}"/>
        <Setter Property="FontSize" Value="{DynamicResource FontSizeNormal}"/>
    </Style>
    
    <!-- 他のコントロールスタイル... -->
</Styles>
```

## 4. 実装上の工夫

### 4.1 リソース参照方式の最適化

Avalonia UIの仕様に合わせて、リソース参照方式を整理：

- `<StyleInclude>` - Stylesファイル用 (BasicStyles.axaml, Buttons.axaml, Controls.axaml)
- `<ResourceInclude>` - ResourceDictionaryファイル用 (Colors.axaml, Typography.axaml, Icons.axaml)

### 4.2 テーマ切り替え対応

DynamicResourceを使用した参照により、テーマ切り替えにスムーズに対応：

```xml
<SolidColorBrush x:Key="BackgroundBrush" Color="{DynamicResource BackgroundColor}"/>
```

### 4.3 レスポンシブ対応

レスポンシブ対応を考慮したレイアウト設計：

- スクロールビュー使用による小画面対応
- グリッドレイアウトの柔軟な設定
- コンテンツの適応的な表示

## 5. 今後の課題

1. ✅ **フォントファイルの導入**
   - ✅ LINE Seed JPの追加（OTFファイル）
   - ✅ LINE Seed ENの追加（OTFファイル）
   - ✅ Noto Sansフォントの一部追加（TTFファイル）
   - ◻ その他言語用Noto Sansフォントの追加
   - ◻ Typography.axamlの更新と適用

2. **アプリケーションロゴの作成**
   - ◻ Baketa専用のロゴデザイン作成
   - ◻ アイコンファイル（.ico/.png）の準備

3. **各タブビューの連携テスト**
   - ◻ 各タブ間のナビゲーション動作確認
   - ◻ ステータス表示の連携確認
   - ◻ イベント発行・購読の動作確認

4. **テーマ切り替え機能のUIサポート**
   - ◻ テーマ切り替えUI要素の追加
   - ◻ テーマ選択の永続化対応

## 6. 注意点とTips

1. **Avalonia UIのスタイル定義**
   - ResourceDictionaryのスタイルはすべて `x:Key` が必要
   - グローバルスタイルはStyles要素のファイルで定義

2. **XAMLバインディングの注意点**
   - DataTemplateでのバインディングは相対パスに注意
   - 親要素の参照には `RelativeSource` を使用

3. **テーマ切り替え時の考慮点**
   - 固定色を使用する部分は `DynamicResource` ではなく `StaticResource` を使用
   - コントロールスタイルはテーマに応じた自動切り替えを考慮

## 7. 結論

Issue #57の実装を進め、メインウィンドウUIデザインの基本部分を実装しました。タブナビゲーション、ホーム画面の改善、テーマ対応の強化など、ユーザビリティとビジュアルの改善を行いました。また、フォントファイルの導入も完了し、日本語用・英語用に別々のLINE Seedフォントを導入しました。今後はアプリケーションロゴの作成とUIのさらなるブラッシュアップを継続します。