# Baketa Analytics

アプリ利用状況の分析と開発改善のためのディレクトリ。

## ディレクトリ構成

```
analytics/
├── notebooks/       # Jupyter Notebook（分析実行）
├── scripts/         # 分析スクリプト（Python/SQL）
├── reports/         # 分析レポート（Markdown）
└── data/            # 生データ（.gitignore対象）
```

## データソース

| ソース | 説明 | 取得方法 |
|--------|------|----------|
| Supabase | ユーザー行動ログ、トークン使用量 | SQL直接クエリ or エクスポート |
| Cloudflare KV | セッション情報 | Workers API |
| クラッシュレポート | エラー情報 | Relay Server API |

## 分析→開発フロー

```
1. データ収集 → data/
2. 分析実行 → notebooks/
3. レポート作成 → reports/
4. Issue作成 → GitHub Issues
5. 機能開発 → Baketa.*
6. 効果測定 → 1に戻る
```

## 注意事項

- `data/` 配下のファイルはGit管理対象外
- 機密データ（ユーザーID、メールアドレス等）は匿名化してから分析
- 分析結果のサマリーのみコミット
