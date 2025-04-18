# Baketa 開発環境セットアップガイド

このガイドは、Baketaプロジェクト（ゲーム翻訳オーバーレイアプリケーション）の開発環境を新しく構築するための手順を説明します。

## 1. 必要なソフトウェア

### 1.1 必須ツール

- **Cursor** - 開発に使用するエディタ（VS Codeベース）
- **.NET 8 SDK** - [ダウンロードリンク](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Git** - バージョン管理システム

### 1.2 推奨ツール

- **Visual Studio 2022** - 補助的なIDE（オプション）
- **PowerShell 7** - スクリプト実行環境
- **Avalonia UI DevTools** - Avalonia UIデバッグ用

## 2. 開発環境のセットアップ

### 2.1 リポジトリのクローン

```powershell
# リポジトリをクローン
git clone https://github.com/koizumiiiii/Baketa.git E:\dev\Baketa
cd E:\dev\Baketa

# サブモジュールがある場合は初期化
git submodule update --init --recursive
```

### 2.2 .NET 8 SDKの確認

```powershell
# .NET SDKのバージョンを確認
dotnet --version

# 8.0.0以上であることを確認
# バージョンが古い場合は最新の.NET 8 SDKをインストール
```

### 2.3 Cursorの設定

1. Cursorを起動し、クローンしたリポジトリを開く
2. 推奨拡張機能をインストール（下記「推奨拡張機能」セクション参照）
3. ワークスペース設定を適用

### 2.4 NuGetパッケージの復元

```powershell
# NuGetパッケージを復元
dotnet restore E:\dev\Baketa\Baketa.sln
```

### 2.5 初期ビルド

```powershell
# ソリューションをビルド
dotnet build E:\dev\Baketa\Baketa.sln -c Debug

# ビルドエラーが発生した場合は「ビルドトラブルシューティング」セクションを参照
```

## 3. 推奨拡張機能

### 3.1 Cursor用拡張機能

以下の拡張機能をCursorにインストールしてください：

#### 必須の拡張機能

- **C# Dev Kit** - .NET開発に必要な包括的なツールセット
- **Visual Studio IntelliCode** - AI補完機能
- **.NET Core Tools** - .NETプロジェクト管理
- **XAML Styler** - Avalonia/XAMLコード整形
- **C# Extensions** - C#生産性向上ツール
- **Avalonia for VS Code** - Avalonia UIのサポート

#### コード品質向上ツール

- **Roslynator** - C#コード分析
- **SonarLint** - コード品質チェック
- **EditorConfig** - コードスタイル統一

#### 生産性向上ツール

- **GitLens** - Git機能強化
- **indent-rainbow** - インデント可視化
- **Code Spell Checker** - スペルチェック
- **Todo Tree** - TODOコメント管理

#### プロジェクト特有ツール

- **REST Client** - APIテスト
- **Markdown All in One** - マークダウン編集
- **JSON Tools** - JSON操作
- **Bracket Pair Colorizer 2** - 括弧の色分け

#### デバッグとテスト

- **C# Test Explorer** - テスト実行UI
- **Debugger for .NET Core** - デバッグサポート

### 3.2 EditorConfigの設定

プロジェクトルートに`.editorconfig`ファイルが含まれており、コードスタイルを自動的に適用します。EditorConfig拡張機能がインストールされていることを確認してください。

## 4. プロジェクト構造

Baketaプロジェクトは以下の構造になっています：

```
Baketa/
├── Baketa.Core/               # コア機能と抽象化
│   ├── Common/                # 共通ユーティリティ
│   ├── Interfaces/            # インターフェース
│   │   ├── Image/             # 画像抽象化インターフェース
│   │   └── Platform/          # プラットフォーム抽象化インターフェース
│   └── Models/                # モデルクラス
│
├── Baketa.Infrastructure/     # インフラストラクチャ層
│   ├── OCR/                   # OCR機能実装
│   ├── Translation/           # 翻訳機能実装
│   └── Services/              # サービス実装
│
├── Baketa.Infrastructure.Platform/  # プラットフォーム依存機能
│   ├── Abstractions/          # プラットフォーム抽象化インターフェース
│   ├── Windows/               # Windows実装
│   │   └── NativeMethods/     # P/Invoke定義
│   └── Adapters/              # アダプターレイヤー
│
├── Baketa.Application/        # アプリケーション層 
│   ├── Services/              # アプリケーションサービス
│   ├── DI/                    # 依存性注入管理
│   └── Events/                # イベント処理
│
├── Baketa.UI/                 # UI層
│   ├── Avalonia/              # Avalonia UI実装
│   │   ├── ViewModels/        # MVVMビューモデル
│   │   ├── Views/             # XAMLビュー
│   │   ├── Controls/          # カスタムコントロール
│   │   └── Services/          # UIサービス
│   └── Abstractions/          # UI抽象化
│
└── docs/                      # プロジェクトドキュメント
```

## 5. ビルドとデバッグ

### 5.1 ビルド構成

- **Debug** - 開発用ビルド（デフォルト）
- **Release** - 最適化されたリリース用ビルド

```powershell
# デバッグビルド
dotnet build E:\dev\Baketa\Baketa.sln -c Debug

# リリースビルド
dotnet build E:\dev\Baketa\Baketa.sln -c Release
```

### 5.2 デバッグの開始

#### Cursorでのデバッグ

1. `F5`キーを押すか、「実行とデバッグ」サイドバーから「.NET Core Launch」を選択
2. 必要に応じてブレークポイントを設定

#### コマンドラインからのデバッグ

```powershell
# Baketa.UI.Avaloniaプロジェクトをデバッグ実行
cd E:\dev\Baketa\Baketa.UI\Avalonia
dotnet run --project Baketa.UI.Avalonia.csproj
```

### 5.3 単体テストの実行

```powershell
# すべてのテストを実行
dotnet test E:\dev\Baketa\Baketa.sln

# 特定のテストプロジェクトを実行
dotnet test E:\dev\Baketa\Baketa.Tests\Baketa.Tests.csproj
```

## 6. ビルドトラブルシューティング

### 6.1 一般的なビルドエラーの解決

- **パッケージの復元問題**
  ```powershell
  dotnet restore --force --no-cache
  ```

- **ビルドキャッシュのクリア**
  ```powershell
  # bin, objフォルダを削除
  Get-ChildItem -Path E:\dev\Baketa -Include bin,obj -Directory -Recurse | Remove-Item -Recurse -Force
  ```

- **PlatformTarget エラー**
  - すべてのプロジェクト(.csproj)ファイルでx64設定が一致しているか確認

### 6.2 特定のエラーと解決策

#### OCR関連のエラー

PaddleOCRに関連するエラーが発生した場合:

```powershell
# OCRモデルファイルの存在を確認
$modelPath = "E:\dev\Baketa\Baketa.Infrastructure\OCR\Models"
if (-not (Test-Path $modelPath)) {
    New-Item -Path $modelPath -ItemType Directory -Force
}
```

#### Avalonia UI関連のエラー

Avaloniaコンパイルエラーが発生した場合:

- XAMLファイルのネームスペース参照を確認
- 参照しているアセンブリが正しく読み込まれているか確認
- コンパイル済みバインディングの問題は、`ItemsSource`を使用するか、`x:DataType`を明示的に指定
- GroupBoxコントロールを使用する場合は`Teast.Controls.GroupBox`パッケージの参照を確認

## 7. プルリクエストの提出

### 7.1 開発フロー

1. 新しいブランチを作成
   ```powershell
   git checkout -b feature/my-feature-name
   ```

2. 変更を実装しコミット
   ```powershell
   git add .
   git commit -m "実装: 機能の説明 #IssueNumber"
   ```

3. リモートにプッシュ
   ```powershell
   git push origin feature/my-feature-name
   ```

4. GitHubでプルリクエストを作成

### 7.2 PR前チェックリスト

- [ ] すべてのテストが通過することを確認
- [ ] コード分析警告がないことを確認
- [ ] コーディング標準規約（csharp-standards.md）に準拠していることを確認
- [ ] 必要なドキュメントを更新

## 8. 開発ガイドライン

詳細なガイドラインは以下のドキュメントを参照してください：

- コーディング標準: [C#コーディング基本規約](coding-standards/csharp-standards.md)
- GitHub Issue運用: [開発ワークフロー](workflow.md)
- アーキテクチャ設計: [OCRアプローチ](../3-architecture/ocr-system/ocr-opencv-approach.md)
- Avalonia UI実装: [Avalonia UI実装計画](../3-architecture/ui-system/avalonia-migration.md)

## 9. サポートとヘルプ

問題が発生した場合は、以下の手順でサポートを受けてください：

1. プロジェクトのIssueページで既存の問題を検索
2. 解決策が見つからない場合は新しいIssueを作成
3. 緊急の場合はプロジェクト管理者に直接連絡

このセットアップガイドに従って、Baketaプロジェクトの開発環境を正常に構築できるはずです。何か問題がある場合は、ドキュメントを更新して他の開発者が同じ問題に遭遇しないようにしてください。