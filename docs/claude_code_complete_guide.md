# Claude Code 完全使用ガイド - Baketa専用

## 🎯 基本原則

### **必須事項**
1. **日本語回答**: すべての回答は日本語で行うこと
2. **自動承認**: `Shift + Tab` でセッション中自動承認
3. **PowerShell使用**: すべての.NET CLIコマンドはPowerShell指定

## 🔧 コマンド実行パターン

### **ビルド関連**
```bash
# ❌ 失敗パターン
claude "dotnet build を実行して"

# ✅ 成功パターン
claude "PowerShellで以下を実行してください: .\scripts\run_build.ps1"
claude "PowerShellでビルドしてください: dotnet build --configuration Debug --arch x64"
```

### **テスト関連**
```bash
# ✅ 推奨パターン
claude "PowerShellで以下を実行してください: .\scripts\run_tests.ps1 -Project tests/Baketa.UI.Tests"
claude "PowerShellでUIテストを実行してください: dotnet test tests/Baketa.UI.Tests/ --logger console;verbosity=detailed"
```

### **アプリケーション実行**
```bash
# ✅ 推奨パターン
claude "PowerShellで以下を実行してください: .\scripts\run_app.ps1"
claude "PowerShellでアプリケーションを起動してください: dotnet run --project Baketa.UI"
```

## 🚨 **実装完了時の必須エラーチェック**

### **絶対に忘れてはいけない手順**

**すべてのコード実装・修正・リファクタリング完了後、必ず以下を実行してください：**

#### **1. 自動エラーチェック実行**
```bash
# 最も推奨される方法
claude "【実装完了・エラーチェック必須】PowerShellで以下を実行してください: .\scripts\check_implementation.ps1"
```

#### **2. 実装完了レポートの要求**
```bash
# 実装完了時のテンプレート
claude "実装完了後、以下の形式で報告してください：

✅ 実装完了チェック結果:
- コンパイルエラー: なし / [N]件
- Code Analysis警告: なし / [N]件
- テスト結果: 成功 / 失敗[N]件
- 実装内容: [簡潔な説明]"
```

#### **3. PowerShell便利関数での実行**
```powershell
# 実装完了チェック
ccomplete "実装内容の説明"

# 標準エラーチェック
cc

# 詳細エラーチェック
cc -Detailed
```

### **エラーがある場合の対応**

#### **コンパイルエラー発見時**
```bash
claude "【緊急修正必要】コンパイルエラーが発見されました。根本原因を分析して修正し、再度チェックしてください"
```

#### **警告発見時**
```bash
claude "【警告対応】Code Analysis警告を分析して、適切な修正または抑制を実施してください"
```

#### **テスト失敗時**
```bash
claude "【テスト修正】失敗したテストを分析し、実装またはテストの修正を行ってください"
```

## 🚀 効率的な開発ワークフロー

### **1. セッション開始時の設定**
```bash
# 最初のコマンドで自動承認設定
claude "【日本語必須・自動承認モード】PowerShellでビルド状態を確認してください"
# → 確認ダイアログで Shift + Tab を押下
```

### **2. エラー修正ワークフロー**
```bash
# Step 1: エラー収集
claude "PowerShellで以下を実行してエラーを収集してください: .\scripts\run_build.ps1 > build_errors.txt 2>&1"

# Step 2: エラー修正
claude "【自動承認】このファイルのエラーを根本的に修正してください" --file build_errors.txt

# Step 3: 確認
claude "PowerShellで修正を確認してください: .\scripts\run_build.ps1"
```

### **3. 新機能開発ワークフロー**
```bash
# Step 1: 設計確認
claude "【日本語必須】新しいOCRフィルターの設計を、Baketaアーキテクチャに従って提案してください"

# Step 2: 実装
claude "【自動承認】提案された設計に基づいてOCRフィルターを実装してください"

# Step 3: テスト
claude "PowerShellで新機能のテストを実行してください: .\scripts\run_tests.ps1 -Filter NewOcrFilter"
```

## 📋 専用コマンドテンプレート

### **ビルド系**
```bash
# 通常ビルド
claude "PowerShellで通常ビルドを実行してください: .\scripts\run_build.ps1"

# クリーンビルド
claude "PowerShellでクリーンビルドを実行してください: .\scripts\run_build.ps1 -Clean -Verbosity detailed"

# リリースビルド  
claude "PowerShellでリリースビルドを実行してください: .\scripts\run_build.ps1 -Configuration Release"

# 特定プロジェクトビルド
claude "PowerShellでUIプロジェクトをビルドしてください: .\scripts\run_build.ps1 -Project Baketa.UI"
```

### **テスト系**
```bash
# 全テスト実行
claude "PowerShellで全テストを実行してください: .\scripts\run_tests.ps1"

# UIテスト実行
claude "PowerShellでUIテストを実行してください: .\scripts\run_tests.ps1 -Project tests/Baketa.UI.Tests"

# 特定テストクラス
claude "PowerShellで特定テストを実行してください: .\scripts\run_tests.ps1 -Project tests/Baketa.UI.Tests -Filter EnhancedSettingsWindowViewModelIntegrationTests"

# 詳細ログでテスト
claude "PowerShellで詳細ログでテストを実行してください: .\scripts\run_tests.ps1 -Verbosity detailed"
```

### **実行系**
```bash
# アプリケーション起動
claude "PowerShellでBaketaを起動してください: .\scripts\run_app.ps1"

# ファイル監視モードで起動
claude "PowerShellでファイル監視モードで起動してください: .\scripts\run_app.ps1 -Watch"

# リリース版起動
claude "PowerShellでリリース版を起動してください: .\scripts\run_app.ps1 -Configuration Release"
```

## 🔍 トラブルシューティング

### **PATH問題の解決**
```bash
# 問題の症状
/bin/bash: line 1: dotnet: command not found

# 解決方法
claude "PowerShellで実行してください: [dotnetコマンド]"

# または
claude "フルパスで実行してください: 'C:\Program Files\dotnet\dotnet.exe' [コマンド]"
```

### **自動承認が効かない場合**
```bash
# セッション開始時に必ず実行
claude "【テスト】日本語で現在時刻を教えてください"
# → 確認ダイアログで Shift + Tab
```

### **エラー修正が適用されない場合**
```bash
# Git状態確認
claude "PowerShellでGit状態を確認してください: git status"

# 強制的な再ビルド
claude "PowerShellで強制再ビルドしてください: .\scripts\run_build.ps1 -Clean"
```

## 📚 参考リンク

- **設定ファイル**: `.claude/instructions.md`
- **プロジェクト設定**: `.claude/project.json`
- **コーディング規約**: `.editorconfig`
- **日本語設定ガイド**: `docs/claude_code_japanese_setup.md`
- **MCP設定ガイド**: `docs/claude_code_mcp_setup.md`

## 🎉 成功パターンの例

### **日常的なエラー修正**
```bash
claude "【自動承認・日本語回答】PowerShellでビルドして、エラーがあれば根本的に修正してください: .\scripts\run_build.ps1"
```

### **新機能実装**
```bash
claude "【自動承認・日本語回答】Baketaアーキテクチャに従って新しい翻訳エンジンを実装し、PowerShellでテストしてください"
```

### **パフォーマンス最適化**
```bash
claude "【自動承認・日本語回答】OCR処理のパフォーマンスを根本的に改善し、PowerShellでベンチマークテストを実行してください"
```

このガイドに従うことで、Claude CodeでBaketaプロジェクトの開発を効率的に行えます。