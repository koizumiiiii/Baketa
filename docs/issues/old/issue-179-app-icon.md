# Issue #179: アプリケーションアイコン設定

**優先度**: 🟡 Medium
**所要時間**: 4時間
**Epic**: UI/UXの刷新
**ラベル**: `priority: medium`, `epic: ui-ux`, `type: enhancement`, `layer: ui`

---

## 📋 概要

Baketaのアプリケーションアイコンを設定し、SVGアイコンファイルを共有します。Windows環境でのタスクバー、スタートメニュー、エクスプローラーでの視認性を向上させます。

---

## 🎯 目的

- アプリケーションの識別性向上
- プロフェッショナルなブランドイメージの確立
- Windows環境での統一感のあるUI体験
- SVGアイコンの提供による拡張性確保

---

## 📦 Epic

**Epic 3: UI/UXの刷新** (#166 - #173)

---

## 🔗 依存関係

- **Blocks**: なし
- **Blocked by**: なし
- **Related**: #171 (メインウィンドウUI刷新)

---

## 📝 要件

### 機能要件

#### 1. アイコンファイル形式
- **SVGファイル**: `baketa-icon.svg` (マスターファイル)
- **.ICOファイル**: `baketa.ico` (複数サイズ埋め込み)
  - 16x16px (タスクバー小アイコン)
  - 32x32px (標準アイコン)
  - 48x48px (エクスプローラー大アイコン)
  - 256x256px (高解像度ディスプレイ対応)
- **PNGファイル**: 各サイズの個別ファイル
  - `baketa-16.png`
  - `baketa-32.png`
  - `baketa-48.png`
  - `baketa-256.png`

#### 2. アイコンデザイン要件
- **コンセプト**: ゲーム翻訳を象徴するデザイン
- **推奨モチーフ**:
  - 言語記号（A→あ、翻訳を象徴）
  - ゲームコントローラー + 吹き出し
  - 地球儀 + テキスト
- **カラースキーム**:
  - プライマリーカラー: `#007ACC` (青)
  - セカンダリーカラー: `#FFFFFF` (白)
  - アクセントカラー: `#FF6B35` (オレンジ)
- **視認性**:
  - 16x16pxでも識別可能なシンプルなデザイン
  - ダーク/ライトモードどちらでも視認性確保

#### 3. アイコン配置場所
```
Baketa.UI/
├── Assets/
│   ├── Icons/
│   │   ├── baketa-icon.svg      (マスターファイル)
│   │   ├── baketa.ico           (Windows用)
│   │   ├── baketa-16.png
│   │   ├── baketa-32.png
│   │   ├── baketa-48.png
│   │   └── baketa-256.png
│   └── baketa-logo.png          (既存ロゴ、200x200px)
```

#### 4. プロジェクト設定
- **Baketa.UI.csproj**:
  ```xml
  <PropertyGroup>
    <ApplicationIcon>Assets\Icons\baketa.ico</ApplicationIcon>
  </PropertyGroup>
  ```

- **App.axaml.cs** (ウィンドウアイコン設定):
  ```csharp
  public override void OnFrameworkInitializationCompleted()
  {
      if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
      {
          var icon = new WindowIcon("Assets/Icons/baketa.ico");

          desktop.MainWindow = new MainWindow
          {
              Icon = icon,
              DataContext = _serviceProvider.GetRequiredService<MainViewModel>()
          };
      }

      base.OnFrameworkInitializationCompleted();
  }
  ```

### 非機能要件

1. **ファイルサイズ**
   - SVG: <50KB
   - .ICO (全サイズ込み): <200KB
   - 各PNG: <50KB

2. **品質基準**
   - ベクター形式（SVG）で拡縮可能
   - アンチエイリアス処理済み
   - 透明背景（PNGアルファチャンネル）

---

## 🏗️ 実装方針

### 1. SVGアイコンのデザイン例

**オプション A: 翻訳シンボル**
```svg
<svg width="256" height="256" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <linearGradient id="grad1" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" style="stop-color:#007ACC;stop-opacity:1" />
      <stop offset="100%" style="stop-color:#0095E8;stop-opacity:1" />
    </linearGradient>
  </defs>

  <!-- 背景円 -->
  <circle cx="128" cy="128" r="120" fill="url(#grad1)" />

  <!-- 左側: 英字 "A" -->
  <text x="70" y="150" font-size="80" font-weight="bold" fill="white" text-anchor="middle">A</text>

  <!-- 矢印 -->
  <path d="M 120 128 L 136 128 M 130 122 L 136 128 L 130 134" stroke="white" stroke-width="4" fill="none" />

  <!-- 右側: 日本語 "あ" -->
  <text x="186" y="150" font-size="80" font-weight="bold" fill="white" text-anchor="middle">あ</text>
</svg>
```

**オプション B: ゲーム+翻訳**
```svg
<svg width="256" height="256" xmlns="http://www.w3.org/2000/svg">
  <!-- 背景 -->
  <rect width="256" height="256" rx="40" fill="#007ACC" />

  <!-- ゲームコントローラー -->
  <path d="M 80 120 Q 70 100 100 90 L 156 90 Q 186 100 176 120 L 168 160 Q 165 180 140 180 L 116 180 Q 91 180 88 160 Z"
        fill="white" />

  <!-- 十字キー -->
  <rect x="100" y="110" width="10" height="30" fill="#007ACC" />
  <rect x="95" y="115" width="20" height="10" fill="#007ACC" />

  <!-- ボタン -->
  <circle cx="150" cy="120" r="8" fill="#FF6B35" />
  <circle cx="165" cy="135" r="8" fill="#FF6B35" />

  <!-- 吹き出し（翻訳シンボル） -->
  <path d="M 180 80 L 240 80 L 240 140 L 220 140 L 210 155 L 210 140 L 180 140 Z"
        fill="white" />
  <text x="210" y="120" font-size="24" font-weight="bold" fill="#007ACC" text-anchor="middle">翻</text>
</svg>
```

### 2. ICOファイル生成手順

#### 方法1: ImageMagickを使用
```bash
# SVGから複数サイズのPNGを生成
magick convert -density 300 -background none baketa-icon.svg -resize 16x16 baketa-16.png
magick convert -density 300 -background none baketa-icon.svg -resize 32x32 baketa-32.png
magick convert -density 300 -background none baketa-icon.svg -resize 48x48 baketa-48.png
magick convert -density 300 -background none baketa-icon.svg -resize 256x256 baketa-256.png

# PNGから.ICOファイルを生成
magick convert baketa-16.png baketa-32.png baketa-48.png baketa-256.png baketa.ico
```

#### 方法2: オンラインツール
- [RealFaviconGenerator](https://realfavicongenerator.net/)
- [Favicon.io](https://favicon.io/)
- [ConvertICO](https://convertico.com/)

#### 方法3: Inkscape (SVG編集)
```bash
# Inkscapeで各サイズをエクスポート
inkscape --export-type=png --export-width=16 --export-filename=baketa-16.png baketa-icon.svg
inkscape --export-type=png --export-width=32 --export-filename=baketa-32.png baketa-icon.svg
inkscape --export-type=png --export-width=48 --export-filename=baketa-48.png baketa-icon.svg
inkscape --export-type=png --export-width=256 --export-filename=baketa-256.png baketa-icon.svg
```

### 3. ビルド時のアイコン埋め込み

**Baketa.UI.csproj**:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <ApplicationIcon>Assets\Icons\baketa.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <AvaloniaResource Include="Assets\Icons\**" />
  </ItemGroup>
</Project>
```

### 4. ウィンドウアイコン動的設定

**App.axaml.cs**:
```csharp
public class App : Application
{
    private IServiceProvider _serviceProvider = null!;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // アイコンファイルロード
            var iconStream = AssetLoader.Open(new Uri("avares://Baketa.UI/Assets/Icons/baketa.ico"));
            var icon = new WindowIcon(iconStream);

            // メインウィンドウにアイコン設定
            var mainWindow = new MainWindow
            {
                Icon = icon,
                DataContext = _serviceProvider.GetRequiredService<MainViewModel>()
            };

            desktop.MainWindow = mainWindow;
            mainWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

---

## ✅ 受け入れ基準

### 機能テスト
- [ ] アプリケーションビルド後、実行ファイルにアイコンが表示される
- [ ] タスクバーにBaketaアイコンが表示される
- [ ] エクスプローラーでBaketa.exeのアイコンが表示される
- [ ] Alt+Tabでのウィンドウ切り替え時にアイコンが表示される
- [ ] スタートメニューのピン留め時にアイコンが表示される

### UIテスト
- [ ] 16x16pxサイズで識別可能
- [ ] 32x32pxサイズで美しく表示される
- [ ] 256x256pxサイズで高精細に表示される
- [ ] ダークテーマ/ライトテーマどちらでも視認性が高い

### ファイルテスト
- [ ] SVGファイルが50KB以下
- [ ] .ICOファイルが200KB以下
- [ ] 各PNGファイルが50KB以下
- [ ] 透明背景が正しく適用されている

### 8. アイコンファイル検証ツール

```csharp
namespace Baketa.UI.Utilities;

public static class IconValidator
{
    private static readonly ILogger _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("IconValidator");

    /// <summary>
    /// アイコンファイルのサイズを検証
    /// </summary>
    public static List<string> ValidateIconFileSizes()
    {
        var violations = new List<string>();
        var fileSizeLimits = new Dictionary<string, long>
        {
            { "Assets/Icons/baketa-icon.svg", 50 * 1024 },      // 50KB
            { "Assets/Icons/baketa.ico", 200 * 1024 },          // 200KB
            { "Assets/Icons/baketa-16.png", 50 * 1024 },        // 50KB
            { "Assets/Icons/baketa-32.png", 50 * 1024 },        // 50KB
            { "Assets/Icons/baketa-48.png", 50 * 1024 },        // 50KB
            { "Assets/Icons/baketa-256.png", 50 * 1024 }        // 50KB
        };

        foreach (var (filePath, maxSize) in fileSizeLimits)
        {
            if (!File.Exists(filePath))
            {
                violations.Add($"{filePath}: ファイルが存在しません");
                _logger.LogWarning("アイコンファイルが見つかりません: {FilePath}", filePath);
                continue;
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > maxSize)
            {
                violations.Add($"{filePath}: {fileInfo.Length / 1024}KB > {maxSize / 1024}KB");
                _logger.LogWarning(
                    "アイコンファイルがサイズ制限を超過: {FilePath} ({ActualSize}KB > {MaxSize}KB)",
                    filePath, fileInfo.Length / 1024, maxSize / 1024);
            }
        }

        return violations;
    }

    /// <summary>
    /// PNGファイルの透明背景を検証
    /// </summary>
    public static List<string> ValidateTransparency()
    {
        var violations = new List<string>();
        var pngFiles = new[]
        {
            "Assets/Icons/baketa-16.png",
            "Assets/Icons/baketa-32.png",
            "Assets/Icons/baketa-48.png",
            "Assets/Icons/baketa-256.png"
        };

        foreach (var filePath in pngFiles)
        {
            if (!File.Exists(filePath))
            {
                violations.Add($"{filePath}: ファイルが存在しません");
                continue;
            }

            try
            {
                using var image = Image.Load<Rgba32>(filePath);
                var hasTransparency = false;

                // 透明ピクセルの存在を確認
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        var pixel = image[x, y];
                        if (pixel.A < 255)
                        {
                            hasTransparency = true;
                            break;
                        }
                    }
                    if (hasTransparency) break;
                }

                if (!hasTransparency)
                {
                    violations.Add($"{filePath}: 透明背景が検出されませんでした");
                    _logger.LogWarning("アイコンに透明背景がありません: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                violations.Add($"{filePath}: 読み込みエラー - {ex.Message}");
                _logger.LogError(ex, "アイコンファイルの読み込みに失敗: {FilePath}", filePath);
            }
        }

        return violations;
    }

    /// <summary>
    /// アイコン解像度を検証
    /// </summary>
    public static List<string> ValidateResolutions()
    {
        var violations = new List<string>();
        var expectedResolutions = new Dictionary<string, (int Width, int Height)>
        {
            { "Assets/Icons/baketa-16.png", (16, 16) },
            { "Assets/Icons/baketa-32.png", (32, 32) },
            { "Assets/Icons/baketa-48.png", (48, 48) },
            { "Assets/Icons/baketa-256.png", (256, 256) }
        };

        foreach (var (filePath, (expectedWidth, expectedHeight)) in expectedResolutions)
        {
            if (!File.Exists(filePath))
            {
                violations.Add($"{filePath}: ファイルが存在しません");
                continue;
            }

            try
            {
                using var image = Image.Load(filePath);
                if (image.Width != expectedWidth || image.Height != expectedHeight)
                {
                    violations.Add($"{filePath}: 解像度不一致 ({image.Width}x{image.Height} != {expectedWidth}x{expectedHeight})");
                    _logger.LogWarning(
                        "アイコン解像度が期待値と異なります: {FilePath} ({ActualWidth}x{ActualHeight} != {ExpectedWidth}x{ExpectedHeight})",
                        filePath, image.Width, image.Height, expectedWidth, expectedHeight);
                }
            }
            catch (Exception ex)
            {
                violations.Add($"{filePath}: 読み込みエラー - {ex.Message}");
                _logger.LogError(ex, "アイコンファイルの読み込みに失敗: {FilePath}", filePath);
            }
        }

        return violations;
    }
}
```

### 自動テスト

```csharp
public class IconValidationTests
{
    [Fact]
    public void IconFiles_すべて存在する()
    {
        // Arrange
        var requiredFiles = new[]
        {
            "Assets/Icons/baketa-icon.svg",
            "Assets/Icons/baketa.ico",
            "Assets/Icons/baketa-16.png",
            "Assets/Icons/baketa-32.png",
            "Assets/Icons/baketa-48.png",
            "Assets/Icons/baketa-256.png"
        };

        // Act & Assert
        foreach (var filePath in requiredFiles)
        {
            File.Exists(filePath).Should().BeTrue($"{filePath} が存在しません");
        }
    }

    [Theory]
    [InlineData("Assets/Icons/baketa-icon.svg", 50)]
    [InlineData("Assets/Icons/baketa.ico", 200)]
    [InlineData("Assets/Icons/baketa-16.png", 50)]
    [InlineData("Assets/Icons/baketa-32.png", 50)]
    [InlineData("Assets/Icons/baketa-48.png", 50)]
    [InlineData("Assets/Icons/baketa-256.png", 50)]
    public void IconFiles_ファイルサイズ制限内(string filePath, int maxKB)
    {
        // Arrange
        var maxSize = maxKB * 1024;

        // Act
        var fileInfo = new FileInfo(filePath);

        // Assert
        fileInfo.Exists.Should().BeTrue($"{filePath} が存在しません");
        fileInfo.Length.Should().BeLessOrEqualTo(maxSize,
            $"{filePath} がサイズ制限を超過: {fileInfo.Length / 1024}KB > {maxKB}KB");
    }

    [Theory]
    [InlineData("Assets/Icons/baketa-16.png", 16, 16)]
    [InlineData("Assets/Icons/baketa-32.png", 32, 32)]
    [InlineData("Assets/Icons/baketa-48.png", 48, 48)]
    [InlineData("Assets/Icons/baketa-256.png", 256, 256)]
    public void PngIcons_解像度が正しい(string filePath, int expectedWidth, int expectedHeight)
    {
        // Arrange & Act
        using var image = Image.Load(filePath);

        // Assert
        image.Width.Should().Be(expectedWidth, $"{filePath} の幅が不正");
        image.Height.Should().Be(expectedHeight, $"{filePath} の高さが不正");
    }

    [Theory]
    [InlineData("Assets/Icons/baketa-16.png")]
    [InlineData("Assets/Icons/baketa-32.png")]
    [InlineData("Assets/Icons/baketa-48.png")]
    [InlineData("Assets/Icons/baketa-256.png")]
    public void PngIcons_透明背景を持つ(string filePath)
    {
        // Arrange & Act
        using var image = Image.Load<Rgba32>(filePath);
        var hasTransparency = false;

        for (int y = 0; y < image.Height && !hasTransparency; y++)
        {
            for (int x = 0; x < image.Width && !hasTransparency; x++)
            {
                if (image[x, y].A < 255)
                {
                    hasTransparency = true;
                }
            }
        }

        // Assert
        hasTransparency.Should().BeTrue($"{filePath} に透明背景がありません");
    }

    [Fact]
    public void IconValidator_ファイルサイズ検証_違反なし()
    {
        // Act
        var violations = IconValidator.ValidateIconFileSizes();

        // Assert
        violations.Should().BeEmpty("すべてのアイコンファイルがサイズ制限内である必要があります");
    }

    [Fact]
    public void IconValidator_透明背景検証_違反なし()
    {
        // Act
        var violations = IconValidator.ValidateTransparency();

        // Assert
        violations.Should().BeEmpty("すべてのPNGアイコンに透明背景が必要です");
    }

    [Fact]
    public void IconValidator_解像度検証_違反なし()
    {
        // Act
        var violations = IconValidator.ValidateResolutions();

        // Assert
        violations.Should().BeEmpty("すべてのPNGアイコンが正しい解像度である必要があります");
    }
}
```

---

## 📊 見積もり

- **作業時間**: 6時間
  - デザイン: 2時間（SVGマスターファイル作成）
  - ファイル生成: 1時間（複数サイズのPNG、ICO生成）
  - 統合: 1時間（csproj設定、コード統合）
  - テスト: 1時間（表示確認、ファイルサイズ検証）
  - ドキュメント: 1時間（使用ガイドライン作成）
- **優先度**: 🟡 Medium
- **リスク**: 🟢 Low
  - **主要リスク**: デザイン品質、複数サイズでの視認性、ファイルサイズ超過
  - **軽減策**: 16x16pxでの視認性テスト、自動ファイルサイズ検証、デザインレビュー

---

## 📌 備考

### SVGアイコンの共有方法
1. **GitHubリポジトリ**: `Baketa.UI/Assets/Icons/baketa-icon.svg`
2. **リリースアセット**: GitHub Releasesに添付
3. **ドキュメント**: `docs/design/icon-guidelines.md` に使用ガイドライン記載

### デザインガイドライン
- **最小サイズ**: 16x16pxで識別可能なシンプルさ
- **カラーバリエーション**: 単色版も用意（モノクロ環境対応）
- **アクセシビリティ**: 色覚異常者でも識別可能な配色

### 将来的な拡張
- macOS用 `.icns` ファイル（将来のクロスプラットフォーム対応）
- Linux用 `.png` ファイル（複数サイズ）
- ファビコン（Webサイト用）

---

## 変更ファイル

### 新規作成
- `Baketa.UI/Assets/Icons/baketa-icon.svg`
- `Baketa.UI/Assets/Icons/baketa.ico`
- `Baketa.UI/Assets/Icons/baketa-16.png`
- `Baketa.UI/Assets/Icons/baketa-32.png`
- `Baketa.UI/Assets/Icons/baketa-48.png`
- `Baketa.UI/Assets/Icons/baketa-256.png`
- `docs/design/icon-guidelines.md`

### 修正
- `Baketa.UI/Baketa.UI.csproj` (ApplicationIcon設定)
- `Baketa.UI/App.axaml.cs` (ウィンドウアイコン設定)

---

**作成日**: 2025-11-18
**作成者**: Claude Code
**関連ドキュメント**: `docs/BETA_DEVELOPMENT_PLAN.md`
