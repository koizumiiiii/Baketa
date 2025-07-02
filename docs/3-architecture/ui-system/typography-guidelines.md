# Baketa アプリケーション フォント設計ガイドライン

## 1. フォント選定方針

Baketaはゲームプレイ中にリアルタイムでテキストを翻訳するアプリケーションとして、以下のフォント選定方針を採用します：

1. **高い可読性と視認性**: OCRで検出された異なる品質のテキストを正確に表示
2. **多言語対応**: 日本語、英語、中国語、韓国語などの多言語をシームレスにサポート
3. **一貫性のあるデザイン**: 言語が切り替わっても一貫した印象を維持
4. **オープンライセンス**: 商用利用可能で開発・配布に制限のないフォント

## 2. 採用フォント

### 2.1 日本語: LINE Seed JP

日本語テキスト表示には、LINEが開発したフォント「**LINE Seed JP**」を採用します。

#### 選定理由:
- 「カドマル」特性により視認性と可読性に優れている
- 日本語で字面バランスが調整されている
- 商用利用可能なSIL Open Font License 1.1で提供
- 4段階のウェイトバリエーション（Thin, Regular, Bold, Extra Bold）
- フレンドリーで現代的なデザイン

#### 利用条件:
- SIL Open Font License 1.1に基づいて使用
- 商用利用の場合は製品・サービスに帰属表記を推奨（`© LINE Corporation`）

### 2.2 英語: LINE Seed EN

英語テキスト表示には、LINEが開発したフォント「**LINE Seed EN**」を採用します。

#### 選定理由:
- 英語に最適化されたデザイン
- 日本語版のLINE Seed JPと視覚的に調和
- 商用利用可能なSIL Open Font License 1.1で提供
- 4段階のウェイトバリエーション（Thin, Regular, Bold, Extra Bold）
- 英語特有のリーダビリティを確保

#### 利用条件:
- SIL Open Font License 1.1に基づいて使用
- 商用利用の場合は製品・サービスに帰属表記を推奨（`© LINE Corporation`）

### 2.3 その他の多言語: Noto Sans

中国語、韓国語、タイ語など他の言語については、GoogleとAdobeが開発した「**Noto Sans**」シリーズを採用します。

#### 選定理由:
- 「No more tofu」をコンセプトに世界中の言語をカバー（約800言語以上）
- 高い可読性と視認性
- LINE Seed JPと視覚的に調和する現代的なデザイン
- 商用利用可能なSIL Open Font License 1.1で提供
- 多くのウェイトバリエーション

#### 言語別フォント:
- 簡体字中国語: Noto Sans SC
- 繁体字中国語: Noto Sans TC / Noto Sans HK
- 韓国語: Noto Sans KR
- タイ語: Noto Sans Thai
- その他の言語: 対応するNoto Sansファミリー

## 3. フォント実装方法

### 3.1 フォントファイルの管理

フォントファイルは以下の構造で管理します：

```
/Baketa.UI/Assets/Fonts/
  ├── LINESeedJP/    # 日本語テキスト用 (OTF形式)
  │   ├── LINESeedJP_OTF_Th.otf
  │   ├── LINESeedJP_OTF_Rg.otf
  │   ├── LINESeedJP_OTF_Bd.otf
  │   └── LINESeedJP_OTF_ExBd.otf
  ├── LINESeedEN/    # 英語テキスト用 (OTF形式)
  │   ├── LINESeedEN_OTF_Th.otf
  │   ├── LINESeedEN_OTF_Rg.otf
  │   ├── LINESeedEN_OTF_Bd.otf
  │   └── LINESeedEN_OTF_ExBd.otf
  ├── NotoSans/      # その他言語用 (TTF形式)
  │   ├── NotoSansSC-Regular.ttf  # 簡体字中国語
  │   ├── NotoSansTC-Regular.ttf  # 繁体字中国語
  │   ├── NotoSansKR-Regular.ttf  # 韓国語
  │   └── ... (その他の言語フォント)
```

### 3.2 Typography.axaml での定義

Avalonia UIでのフォント定義は以下のように行います：

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- フォントファミリーの定義 -->
    <FontFamily x:Key="JapaneseFontFamily">avares://Baketa.UI/Assets/Fonts/LINESeedJP/LINESeedJP_OTF_Rg.otf#LINE Seed JP</FontFamily>
    <FontFamily x:Key="EnglishFontFamily">avares://Baketa.UI/Assets/Fonts/LINESeedEN/LINESeedEN_OTF_Rg.otf#LINE Seed EN</FontFamily>
    <FontFamily x:Key="SecondaryFontFamily">avares://Baketa.UI/Assets/Fonts/NotoSans/NotoSans-Regular.ttf#Noto Sans</FontFamily>
    
    <!-- フォールバックフォント定義 -->
    <FontFamily x:Key="FallbackFontFamily">
        avares://Baketa.UI/Assets/Fonts/LINESeedJP/LINESeedJP_OTF_Rg.otf#LINE Seed JP,
        avares://Baketa.UI/Assets/Fonts/LINESeedEN/LINESeedEN_OTF_Rg.otf#LINE Seed EN,
        avares://Baketa.UI/Assets/Fonts/NotoSans/NotoSansSC-Regular.ttf#Noto Sans SC,
        avares://Baketa.UI/Assets/Fonts/NotoSans/NotoSansTC-Regular.ttf#Noto Sans TC,
        avares://Baketa.UI/Assets/Fonts/NotoSans/NotoSansKR-Regular.ttf#Noto Sans KR,
        Segoe UI,
        Yu Gothic UI,
        Meiryo UI,
        MS UI Gothic,
        sans-serif
    </FontFamily>
    
    <!-- テキストスタイルの定義 -->
    <Style Selector="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource FallbackFontFamily}" />
        <Setter Property="FontSize" Value="14" />
        <Setter Property="TextWrapping" Value="Wrap" />
    </Style>
    
    <!-- 見出しスタイル -->
    <Style Selector="TextBlock.h1">
        <Setter Property="FontFamily" Value="{StaticResource JapaneseFontFamily}" />
        <Setter Property="FontSize" Value="24" />
        <Setter Property="FontWeight" Value="Bold" />
        <Setter Property="Margin" Value="0,0,0,8" />
    </Style>
    
    <Style Selector="TextBlock.h2">
        <Setter Property="FontFamily" Value="{StaticResource JapaneseFontFamily}" />
        <Setter Property="FontSize" Value="20" />
        <Setter Property="FontWeight" Value="Bold" />
        <Setter Property="Margin" Value="0,0,0,8" />
    </Style>
    
    <Style Selector="TextBlock.h3">
        <Setter Property="FontFamily" Value="{StaticResource JapaneseFontFamily}" />
        <Setter Property="FontSize" Value="16" />
        <Setter Property="FontWeight" Value="Bold" />
        <Setter Property="Margin" Value="0,0,0,8" />
    </Style>
    
    <!-- 本文スタイル -->
    <Style Selector="TextBlock.body">
        <Setter Property="FontFamily" Value="{StaticResource FallbackFontFamily}" />
        <Setter Property="FontSize" Value="14" />
        <Setter Property="LineHeight" Value="20" />
    </Style>
    
    <Style Selector="TextBlock.caption">
        <Setter Property="FontFamily" Value="{StaticResource FallbackFontFamily}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Opacity" Value="0.7" />
    </Style>
    
    <!-- 翻訳オーバーレイ表示用スタイル -->
    <Style Selector="TextBlock.overlay">
        <Setter Property="FontFamily" Value="{StaticResource FallbackFontFamily}" />
        <Setter Property="FontSize" Value="16" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="Background" Value="#CC000000" />
        <Setter Property="Foreground" Value="White" />
        <Setter Property="Padding" Value="8" />
        <Setter Property="TextWrapping" Value="Wrap" />
    </Style>
</ResourceDictionary>
```

## 4. 言語自動判定とフォント切り替え

Baketaでは、以下の方法で言語を自動判定し、適切なフォントへ切り替えます：

1. **Unicode範囲による判定**: テキストの文字のUnicode範囲を解析し、言語を推定
2. **翻訳エンジンからの言語情報**: 翻訳元・翻訳先の言語情報を活用
3. **優先フォント切り替え**: 言語に応じて最適なフォントを設定

```csharp
// 言語に基づいたフォントスタイル適用例
public void ApplyFontByLanguage(string text, TextBlock textBlock)
{
    string detectedLanguage = DetectLanguage(text);
    
    switch (detectedLanguage)
    {
        case "ja":
            textBlock.FontFamily = new FontFamily("avares://Baketa.UI/Assets/Fonts/LINESeedJP/LINESeedJP_OTF_Rg.otf#LINE Seed JP");
            break;
            
        case "en":
            textBlock.FontFamily = new FontFamily("avares://Baketa.UI/Assets/Fonts/LINESeedEN/LINESeedEN_OTF_Rg.otf#LINE Seed EN");
            break;
            
        case "zh-CN":
            textBlock.FontFamily = new FontFamily("avares://Baketa.UI/Assets/Fonts/NotoSans/NotoSansSC-Regular.ttf#Noto Sans SC");
            break;
            
        case "zh-TW":
        case "zh-HK":
            textBlock.FontFamily = new FontFamily("avares://Baketa.UI/Assets/Fonts/NotoSans/NotoSansTC-Regular.ttf#Noto Sans TC");
            break;
            
        case "ko":
            textBlock.FontFamily = new FontFamily("avares://Baketa.UI/Assets/Fonts/NotoSans/NotoSansKR-Regular.ttf#Noto Sans KR");
            break;
            
        default:
            // フォールバックフォント
            textBlock.FontFamily = App.Current.Resources["FallbackFontFamily"] as FontFamily;
            break;
    }
}
```

## 5. フォントライセンスと帰属表記

### 5.1 LINE Seed JP / LINE Seed EN

- ライセンス: SIL Open Font License 1.1
- 公式サイト: 
  - 日本語版: https://seed.line.me/index_jp.html
  - 英語版: https://seed.line.me/index_en.html
- 帰属表記: 商用利用の場合は `© LINE Corporation` の表記を推奨

### 5.2 Noto Sans

- ライセンス: SIL Open Font License 1.1
- 公式サイト: https://fonts.google.com/noto
- 帰属表記: 不要（オプション）

## 6. 実装ロードマップ

1. ✅ フォントファイルのダウンロードと整理
   - ✅ LINE Seed JP (OTF)
   - ✅ LINE Seed EN (OTF)
   - ✅ Noto Sans SC (TTF)
   - ◻ その他のNoto Sansフォント
2. ◻ Typography.axaml の更新
3. ◻ App.axaml へのリソース登録
4. ◻ フォント言語自動判定機能の実装
5. ◻ 各ビューでの適用とテスト