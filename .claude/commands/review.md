---
description: Gemini AIによるコードレビューを実行
---

# コードレビュー

変更されたコードについてGemini AIでレビューを実行します。

## 実行手順

### 1. 変更ファイルの特定
```bash
git diff --name-only HEAD~1
# または
git diff --name-only main
```

### 2. Geminiレビュー実行

**⚠️ 重要: Git Bashからは直接実行不可。PowerShell経由で実行すること。**
**⚠️ モデル指定: `~/.gemini/settings.json` で `gemini-2.5-flash` をデフォルトに設定済み。変更する場合は `-m` オプションを使用。**

```powershell
# PowerShellから直接実行（推奨）
# デフォルトモデル: gemini-2.5-flash（settings.jsonで設定済み）
gemini "以下のコードについてレビューをお願いします。

## レビュー観点
1. アーキテクチャ準拠（Clean Architecture）
2. コーディング規約（C# 12, .NET 8）
3. パフォーマンス問題
4. セキュリティリスク
5. テストカバレッジ

## 変更内容
<変更されたコードをここに貼り付け>

問題点と改善提案を具体的に示してください。"
```

```bash
# Git Bashから実行する場合（PowerShell経由必須）
powershell -Command "gemini '以下のコードについてレビューをお願いします。...'"
```

**注意事項**:
- `-p` オプションは非推奨（動作しない）
- 位置引数としてプロンプトを渡す
- 出力が空の場合は「Geminiレビュー取得失敗」と明確に報告

### 3. 静的解析（Gemini不可時）
```powershell
.\scripts\code-review-simple.ps1 -Detailed
```

## レビュー結果の対応
- 重大な問題: 即座に修正
- 改善提案: 必要に応じて対応
- スタイル指摘: 次回以降に反映
