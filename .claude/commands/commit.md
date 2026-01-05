---
description: 標準フォーマットでGitコミットを作成
---

# コミット作成

以下の手順でコミットを作成してください:

## 1. 変更内容の確認
```bash
git status
git diff --stat
```

## 2. コミットメッセージ作成ルール

### プレフィックス
- `feat:` - 新機能
- `fix:` - バグ修正
- `refactor:` - リファクタリング
- `test:` - テスト追加/修正
- `docs:` - ドキュメント
- `chore:` - 雑務（ビルド設定等）
- `perf:` - パフォーマンス改善

### フォーマット
```
<prefix>: <簡潔な説明（日本語可）>

<詳細説明（必要な場合）>

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
```

## 3. コミット実行
```bash
git add <files>
git commit -m "<message>"
```

## 注意事項
- `.env` や `*.key` などの機密ファイルはコミットしない
- ビルドエラーがある状態ではコミットしない
