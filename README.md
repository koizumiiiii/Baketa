# Baketa

Baketa（バケタ）は、ゲームプレイ中にリアルタイムでテキストを翻訳するWindows専用オーバーレイアプリケーションです。

## プロジェクト概要

OCR技術によりゲーム画面からテキストを検出し、翻訳結果を透過オーバーレイとして表示します。高度な画像処理とOCR最適化により、様々なゲームシナリオで効果的なテキスト検出と翻訳を実現します。

## 主要機能

- **OCR処理**: PaddleOCRによるテキスト検出
- **OpenCVベース最適化**: 画像前処理による精度向上
- **差分検出**: 画面変更の高精度検出によるパフォーマンス最適化
- **多言語翻訳**: 複数の翻訳エンジン対応（ローカル＆クラウド）
- **オーバーレイ表示**: 透過的なUI、クリックスルー対応
- **ゲームプロファイル**: ゲーム別の最適設定

## アーキテクチャ概要

Baketaは5つの主要レイヤーから構成されるクリーンアーキテクチャを採用しています：

1. **Baketa.Core**: プラットフォーム非依存のコア機能と抽象化
2. **Baketa.Infrastructure**: インフラストラクチャ層（OCR、翻訳など）
3. **Baketa.Infrastructure.Platform**: プラットフォーム依存機能（Windows実装）
4. **Baketa.Application**: ビジネスロジックと機能統合
5. **Baketa.UI**: ユーザーインターフェース（Avalonia UI）

## 開発アプローチ

- **イベント集約機構**: 疎結合なモジュール間通信
- **モジュールベースのDI**: 機能ごとの依存性注入モジュール
- **アダプターレイヤー**: インターフェース間の互換性確保
- **OpenCVベースの画像処理**: ゲーム特性に合わせた最適化

## 🤖 Claude-Gemini自動化システム

このプロジェクトには、Claude CodeとGemini CLIを連携した完全自動開発システムが組み込まれています。

### 🚀 クイックスタート

#### 1. 前提条件のインストール

**WSL環境で:**
```bash
# .NET SDK インストール
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update
sudo apt install -y dotnet-sdk-8.0

# Claude Code インストール
# https://claude.ai/code からインストール

# Gemini CLI インストール
npm install -g @google/generative-ai-cli
# または適切な方法でインストール
```

#### 2. 自動化システムの使用

```bash
# WSL環境でプロジェクトディレクトリに移動
cd /mnt/e/dev/Baketa/claude-gemini

# 実行権限付与
chmod +x *.sh

# 自動開発実行
./baketa-auto-dev.sh "機能名" "機能説明"

# 例: OCR最適化機能の実装
./baketa-auto-dev.sh "OCR最適化" "OpenCVフィルタによるテキスト検出精度向上"
```

#### 3. 対話モード

```bash
# 対話形式で機能開発
./baketa-auto-dev.sh interactive

# コードレビュー実行
./baketa-code-review.sh --interactive
```

### 🔄 自動化フロー

```
機能要求入力 → Claude Code実装 → ビルド確認 → テスト実行 → エラー時自動修正 → 完了
                    ↓
               Gemini分析（エラー時）→ 修正提案 → Claude Code修正
```

### 📋 利用可能なコマンド

#### 自動開発スクリプト
```bash
# 基本実行
./baketa-auto-dev.sh "機能名" "説明"

# 対話モード
./baketa-auto-dev.sh interactive

# 前提条件確認
./baketa-auto-dev.sh check

# ログ確認
./baketa-auto-dev.sh logs
```

#### コードレビュースクリプト
```bash
# ファイルレビュー
./baketa-code-review.sh --file Baketa.Core/Services/OcrService.cs --focus performance

# ディレクトリレビュー
./baketa-code-review.sh --dir Baketa.Infrastructure --focus architecture

# プロジェクト全体レビュー
./baketa-code-review.sh --target . --focus full

# 対話モード
./baketa-code-review.sh --interactive
```

### 🎯 レビューフォーカス領域

- `architecture` - クリーンアーキテクチャ・設計パターン
- `performance` - 非同期処理・OCR最適化・メモリ効率
- `security` - 入力検証・エラーハンドリング・セキュリティ
- `style` - C#規約・可読性・命名規則
- `documentation` - XMLコメント・API仕様・ドキュメント
- `testing` - テスタビリティ・テスト網羅性・モック
- `full` - 包括的レビュー（全領域）

### 📁 自動化システム構成

```
claude-gemini/
├── baketa-auto-dev.sh         # 自動開発メインスクリプト
├── baketa-code-review.sh      # コードレビュー専用スクリプト
├── config.json               # システム設定（自動生成）
├── logs/                     # 実行ログ（gitignore）
│   ├── auto-dev.log          # 開発ログ
│   ├── code-review.log       # レビューログ
│   ├── build_attempt_*.log   # ビルドログ
│   └── test_attempt_*.log    # テストログ
└── reviews/                  # レビュー結果（gitignore）
    └── review_*.md           # Markdownレビューレポート
```

### 🛠️ 設定とカスタマイズ

システム設定は `claude-gemini/config.json` で管理されます（初回実行時に自動生成）：

```json
{
  "development": {
    "maxRetries": 3,
    "buildConfiguration": "Release",
    "autoFix": true
  },
  "gemini": {
    "dailyLimit": 1000,
    "defaultModel": "gemini-pro"
  },
  "review": {
    "defaultFocus": "full",
    "maxFilesPerReview": 10,
    "saveResults": true
  }
}
```

### 🔍 トラブルシューティング

```bash
# 前提条件確認
./baketa-auto-dev.sh check

# ログ確認
tail -f claude-gemini/logs/auto-dev.log

# 手動ビルド確認
cd /mnt/e/dev/Baketa
dotnet build

# 手動テスト確認
dotnet test
```

## Claude Code 開発環境

このプロジェクトは[Claude Code](https://claude.ai/code)での開発にも最適化されています。

### 🚀 クイックセットアップ

```powershell
# Baketa開発環境のセットアップ
.\scripts\setup_claude_code.ps1

# 便利な関数をPowerShellプロファイルに追加
.\scripts\setup_claude_code.ps1 -AddToProfile
```

### 📋 基本的な使用方法

```bash
# Claude Codeでの基本的な指示パターン
claude "【日本語必須・自動承認】PowerShellで以下を実行してください: .\scripts\run_build.ps1"

# 自動承認の設定
# Claude Codeの確認ダイアログで Shift + Tab を押下
```

### 🔧 便利なコマンド（PowerShellプロファイル追加後）

```powershell
cb           # ビルド
ct           # テスト実行
cr           # アプリケーション起動
ca "タスク"   # Claude Codeに自動承認で指示
bhelp        # ヘルプ表示
```

### 📚 詳細ガイド

- [完全使用ガイド](docs/claude_code_complete_guide.md) - Claude Codeの効率的な使用方法
- [日本語設定ガイド](docs/claude_code_japanese_setup.md) - 日本語回答の設定方法
- [MCP設定ガイド](docs/claude_code_mcp_setup.md) - Model Context Protocolの設定

## 開発要件

### システム要件
- **OS**: Windows 10/11 (x64)
- **.NET**: 8.0 Windows Target Framework
- **開発環境**: Visual Studio 2022 推奨
- **Claude Code**: 効率的な開発のため推奨
- **WSL**: 自動化システム使用時に必要

### 依存関係
- **UI Framework**: Avalonia 11.2.7 + ReactiveUI
- **OCR Engine**: PaddleOCR
- **画像処理**: OpenCV (Windows wrapper)
- **翻訳**: OPUS-MT (ローカル), Google Gemini (クラウド)
- **DI Container**: Microsoft.Extensions.DependencyInjection
- **テストフレームワーク**: xUnit + Moq

## 開発ワークフロー

### 自動化システム使用時の推奨フロー

1. **機能要求**: WSL環境で自動化スクリプト実行
2. **自動実装**: Claude Codeによる実装
3. **自動検証**: ビルド・テストの自動実行
4. **自動修正**: エラー時のGemini分析・Claude修正
5. **コードレビュー**: 完了後のGeminiレビュー実行
6. **統合**: 手動での最終確認・コミット

### 基本的な開発フロー

1. **要件確認**: Issues と関連ドキュメントを確認
2. **Claude Code開発**: 自動承認モードで効率的に実装
3. **テスト**: PowerShellスクリプトで自動化されたテスト実行
4. **統合**: 既存システムとの統合確認
5. **コミット**: Gitで変更を保存

### Claude Code使用時の推奨パターン

```bash
# エラー修正
claude "【自動承認・日本語回答】PowerShellでビルドして、エラーがあれば根本的に修正してください"

# 新機能実装
claude "【自動承認・日本語回答】Baketaアーキテクチャに従って新しいOCRフィルターを実装してください"

# テスト実行
claude "PowerShellで以下を実行してください: .\scripts\run_tests.ps1 -Project tests/Baketa.UI.Tests"
```

## ビルドとテスト

### 自動化スクリプト使用（推奨）

```bash
# WSL環境での自動ビルド・テスト
./claude-gemini/baketa-auto-dev.sh "テスト実装" "新機能のテスト追加"
```

### PowerShellスクリプト使用

```powershell
# ビルド
.\scripts\run_build.ps1
.\scripts\run_build.ps1 -Configuration Release
.\scripts\run_build.ps1 -Clean

# テスト
.\scripts\run_tests.ps1
.\scripts\run_tests.ps1 -Project tests/Baketa.UI.Tests
.\scripts\run_tests.ps1 -Filter "SpecificTestName"

# 実行
.\scripts\run_app.ps1
.\scripts\run_app.ps1 -Watch
```

### 従来のdotnet CLI

```bash
# ビルド
dotnet build --configuration Debug --arch x64
dotnet build --configuration Release

# テスト
dotnet test --logger "console;verbosity=detailed"
dotnet test tests/Baketa.Core.Tests/

# 実行
dotnet run --project Baketa.UI
```

### OPUS-MT モデルセットアップ

翻訳機能使用前に必要なモデルをダウンロード：

```powershell
.\scripts\download_opus_mt_models.ps1
.\scripts\verify_opus_mt_models.ps1
```

## プロジェクト構造

```
Baketa/
├── .claude/                    # Claude Code設定
│   ├── project.json           # プロジェクト設定
│   ├── instructions.md        # 開発指示
│   └── context.md             # コンテキスト設定
├── claude-gemini/             # 自動化システム
│   ├── baketa-auto-dev.sh     # 自動開発スクリプト
│   ├── baketa-code-review.sh  # コードレビュースクリプト
│   ├── config.json            # システム設定
│   ├── logs/                  # ログファイル（gitignore）
│   └── reviews/               # レビュー結果（gitignore）
├── scripts/                   # 開発用スクリプト
│   ├── run_build.ps1          # ビルドスクリプト
│   ├── run_tests.ps1          # テストスクリプト
│   ├── run_app.ps1            # 実行スクリプト
│   ├── baketa_functions.ps1   # 便利関数
│   └── setup_claude_code.ps1  # セットアップスクリプト
├── docs/                      # ドキュメント
│   ├── claude_code_complete_guide.md
│   ├── claude_code_japanese_setup.md
│   ├── claude_code_mcp_setup.md
│   └── Baketa プロジェクトナレッジベース（完全版）.md
├── Baketa.Core/               # コアレイヤー
├── Baketa.Infrastructure/     # インフラストラクチャレイヤー
├── Baketa.Infrastructure.Platform/ # プラットフォームレイヤー
├── Baketa.Application/        # アプリケーションレイヤー
├── Baketa.UI/                 # UIレイヤー
└── tests/                     # テストプロジェクト
    ├── Baketa.Core.Tests/
    ├── Baketa.Infrastructure.Tests/
    ├── Baketa.Application.Tests/
    └── Baketa.UI.Tests/
```

## 現在の開発状況

現在、名前空間構造の改善とインターフェース設計の最適化を進めています。主な取り組みは以下の通りです：

- `Baketa.Core.Interfaces` → `Baketa.Core.Abstractions` への名前空間移行
- インターフェース階層の再構築と責任分離の明確化
- プラットフォーム依存コードの整理
- アダプターパターンによる互換性維持
- Claude Code開発環境の最適化
- Claude-Gemini自動化システムの統合

## 開発設計原則

### 根本原因解決アプローチ
- 表面的な修正ではなく、根本的な問題解決を重視
- 症状ではなく原因に対処するシステム設計
- 将来の類似問題を防ぐアーキテクチャ設計

### Code Quality Standards
- **C# 12 / .NET 8.0準拠**: 最新の言語機能を活用
- **Clean Architecture**: 責任分離と依存関係の管理
- **Test-Driven Development**: 包括的なテストカバレッジ
- **EditorConfig準拠**: 一貫したコーディングスタイル

## 注意事項

- Baketaは**Windows専用アプリケーション**として設計されています
- クロスプラットフォーム対応の予定はありません
- OCR最適化にはOpenCVのみを使用します
- Claude Codeでの開発時は、PowerShell環境での実行を前提としています
- 自動化システムはWSL環境で.NET SDKとGemini CLIが必要です

## ライセンス

[LICENSE](LICENSE) ファイルを参照してください。

## 貢献

プロジェクトへの貢献を歓迎します。Claude Code自動化システムを使用した効率的な開発ワークフローに従って、プルリクエストを提出してください。

詳細な開発ガイドラインについては、[docs/claude_code_complete_guide.md](docs/claude_code_complete_guide.md) を参照してください。