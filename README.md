# Baketa

[![CI Build and Test](https://github.com/koizumiiiii/Baketa/actions/workflows/ci.yml/badge.svg)](https://github.com/koizumiiiii/Baketa/actions/workflows/ci.yml)
[![CodeQL](https://github.com/koizumiiiii/Baketa/actions/workflows/codeql.yml/badge.svg)](https://github.com/koizumiiiii/Baketa/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Windows](https://img.shields.io/badge/Platform-Windows-blue.svg)](https://www.microsoft.com/windows)

Baketa（バケタ）は、ゲームプレイ中にリアルタイムでテキストを翻訳するWindows専用オーバーレイアプリケーションです。

## 📋 プロジェクト概要

OCR技術によりゲーム画面からテキストを検出し、翻訳結果を透過オーバーレイとして表示します。高度な画像処理とOCR最適化により、様々なゲームシナリオで効果的なテキスト検出と翻訳を実現します。

**現在のバージョン**: v0.1.0 (Alpha)

## ✨ 主要機能

### 🔍 OCR・画像処理
- **PaddleOCR**: 高精度なテキスト検出エンジン
- **OpenCV画像フィルタ**: モルフォロジー、ガウシアンフィルタによる前処理最適化
- **差分検出**: 画面変更の高速検出によるパフォーマンス向上
- **アダプティブ閾値**: ゲーム画面に応じた動的調整

### 🌐 翻訳機能
- **OPUS-MT**: ローカル翻訳エンジン（オフライン対応）
- **Google Gemini**: クラウド翻訳エンジン（高精度翻訳）
- **多言語対応**: 日英中韓など主要言語ペア
- **翻訳履歴**: 過去の翻訳結果管理

### 🖥️ UI・UX
- **透過オーバーレイ**: ゲームプレイを妨げない表示
- **Avalonia UI**: モダンなクロスプラットフォームUI
- **テーマ対応**: ダーク・ライトテーマ切り替え
- **設定管理**: ゲーム別プロファイル保存

### 🔒 セキュリティ・プライバシー
- **GDPR準拠**: プライバシー同意管理システム
- **データ保護**: ローカルファースト設計
- **フィードバック機能**: GitHub Issues連携
- **自動更新**: セキュアな更新チェック機能

## 🏗️ アーキテクチャ概要

Baketaは5層クリーンアーキテクチャとモジュラー設計を採用しています：

### 📦 レイヤー構成
1. **Baketa.Core**: プラットフォーム非依存のコア機能・抽象化層
2. **Baketa.Infrastructure**: インフラストラクチャ層（OCR、翻訳、設定管理）
3. **Baketa.Infrastructure.Platform**: Windows専用プラットフォーム実装
4. **Baketa.Application**: ビジネスロジック・機能統合層
5. **Baketa.UI**: Avalonia UIによるプレゼンテーション層

### ⚡ 設計パターン
- **イベント集約**: `IEventAggregator`による疎結合通信
- **依存性注入**: モジュール単位のDIコンテナ設計
- **アダプターパターン**: レイヤー間の互換性確保
- **リポジトリパターン**: データアクセス抽象化
- **ReactiveUI**: MVVM + リアクティブプログラミング

## ダウンロード・インストール

### 最新リリース
- **Stable Release**: [GitHub Releases](https://github.com/koizumiiiii/Baketa/releases/latest)
- **Alpha Test**: [Pre-releases](https://github.com/koizumiiiii/Baketa/releases?q=prerelease%3Atrue)

### 💻 システム要件
- **OS**: Windows 10/11 (64-bit)
- **メモリ**: 4GB RAM 推奨（8GB以上を強く推奨）
- **ストレージ**: 2GB 空き容量（OPUS-MTモデル含む）
- **ランタイム**: .NET 8.0 Windows Desktop Runtime（自己完結型）
- **GPU**: CUDA対応GPU（オプション、OCR高速化用）

### インストール手順
1. [Releases](https://github.com/koizumiiiii/Baketa/releases)から最新版をダウンロード
2. ZIPファイルを任意のフォルダに展開
3. `Baketa.UI.exe`を実行
4. 初期設定ウィザードに従って設定完了

## 🛠️ 開発環境

### 📋 開発要件
- **IDE**: Visual Studio 2022 / JetBrains Rider / VS Code
- **SDK**: .NET 8.0 SDK
- **OS**: Windows 10/11（開発・テスト）、WSL2（ビルドのみ）
- **言語**: C# 12.0（最新機能活用）

### 🚀 クイックスタート


```bash
# 1. リポジトリクローン
git clone https://github.com/koizumiiiii/Baketa.git
cd Baketa

# 2. 依存関係復元
dotnet restore

# 3. ビルド実行
dotnet build --configuration Debug

# 4. テスト実行
dotnet test

# 5. アプリケーション実行
dotnet run --project Baketa.UI
```

### 🔧 開発用スクリプト（PowerShell）
```powershell
# ビルド
.\scripts\run_build.ps1
.\scripts\run_build.ps1 -Configuration Release
.\scripts\run_build.ps1 -Clean

# テスト
.\scripts\run_tests.ps1
.\scripts\run_tests.ps1 -Project "Baketa.Core.Tests"

# アプリケーション実行
.\scripts\run_app.ps1
```

### 📦 リリースビルド
```bash
# 自己完結型パッケージ生成
dotnet publish Baketa.UI/Baketa.UI.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --output ./publish/Baketa
```

### 🔄 CI/CD パイプライン

**GitHub Actions**による自動化：
- ✅ **ビルド**: Windows Server 2022、.NET 8.0 SDK
- ✅ **テスト**: 1,300+ テストケース、カバレッジ収集
- ✅ **セキュリティ**: CodeQL静的解析、依存関係チェック
- ✅ **品質**: コード品質チェック、パフォーマンステスト
- ✅ **リリース**: 自動パッケージング・配布

## 📈 プロジェクト状況

### 🧪 テスト状況
- **テストケース数**: 1,300+ 
- **カバレッジ**: Core 85%+、Infrastructure 80%+、UI 70%+
- **パフォーマンステスト**: OCR・翻訳速度ベンチマーク
- **統合テスト**: エンドツーエンド翻訳フロー

### 📊 技術スタック
- **言語**: C# 12.0（最新機能活用）
- **フレームワーク**: .NET 8.0 Windows
- **UI**: Avalonia 11.2.7 + ReactiveUI
- **OCR**: PaddleOCR + OpenCV最適化
- **翻訳**: OPUS-MT（ローカル）、Google Gemini（クラウド）
- **テスト**: xUnit + Moq + Avalonia.Headless

## 📁 プロジェクト構造

```
Baketa/
├── 📂 .claude/                 # Claude Code設定
├── 📂 .github/workflows/       # CI/CDパイプライン
├── 📂 scripts/                 # 開発用PowerShellスクリプト
├── 📂 docs/                    # プロジェクトドキュメント
├── 📂 Baketa.Core/            # 🎯 コア機能・抽象化
├── 📂 Baketa.Infrastructure/   # ⚙️ インフラストラクチャ実装
├── 📂 Baketa.Infrastructure.Platform/ # 🖥️ Windows専用実装
├── 📂 Baketa.Application/      # 📋 ビジネスロジック
├── 📂 Baketa.UI/              # 🎨 ユーザーインターフェース
└── 📂 tests/                   # 🧪 テストプロジェクト
    ├── Baketa.Core.Tests/
    ├── Baketa.Infrastructure.Tests/
    ├── Baketa.UI.Tests/
    └── Baketa.Integration.Tests/
```

## 🤝 コントリビューション

### 🚀 開発への参加方法
1. **Issues確認**: [GitHub Issues](https://github.com/koizumiiiii/Baketa/issues)で課題を確認
2. **フォーク**: リポジトリをフォークして作業ブランチ作成
3. **実装**: クリーンアーキテクチャとコーディング規約に従って実装
4. **テスト**: すべてのテストが通ることを確認
5. **プルリクエスト**: 変更内容の詳細説明と共にPR作成

### 📏 コーディング規約
- **C# 12**: 最新機能を積極的に活用
- **クリーンアーキテクチャ**: 依存関係の方向性を厳守
- **SOLID原則**: 単一責任、開放閉鎖原則の徹底
- **テスト駆動**: 新機能にはテストコードを必須で追加
- **非同期プログラミング**: `ConfigureAwait(false)`の使用

## 📜 ライセンス

このプロジェクトは[MITライセンス](LICENSE)の下で公開されています。

## 🙏 謝辞

- **PaddleOCR**: 高精度OCRエンジン
- **OpenCV**: 画像処理ライブラリ
- **Avalonia**: クロスプラットフォームUIフレームワーク
- **OPUS-MT**: オープンソース翻訳モデル

---

**Baketa** - ゲーム翻訳をもっと身近に 🎮✨