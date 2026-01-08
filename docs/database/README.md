# Database SQL Scripts

Supabase データベースのスキーマ定義・マイグレーション・メンテナンス用SQLスクリプト集。

## ファイル一覧

| ファイル | 説明 | 実行タイミング |
|---------|------|---------------|
| `profiles_trigger.sql` | auth.users → profiles 自動作成トリガー | 初回セットアップ / トリガー消失時 |
| `integrity_checks.sql` | データ整合性チェッククエリ | 月1回 / 問題発生時 |
| `usage_analytics.sql` | 使用統計テーブル (Issue #269) | 初回セットアップ |
| `promotion_codes.sql` | プロモーションコードテーブル | 初回セットアップ |
| `consent_records.sql` | 同意記録テーブル (Issue #261) | 初回セットアップ |
| `crash_reports.sql` | クラッシュレポートテーブル | 初回セットアップ |

## 重要: profiles_trigger.sql

**このトリガーがないと、OAuth認証でユーザー作成時にprofilesテーブルにレコードが作成されません。**

### 確認方法
```sql
SELECT tgname FROM pg_trigger WHERE tgname = 'on_auth_user_created';
```

結果が0件の場合、`profiles_trigger.sql` を実行してください。

### 問題が発生した場合

1. `integrity_checks.sql` で整合性を確認
2. 問題があれば `profiles_trigger.sql` の修復クエリを実行

## 定期メンテナンス

### 月1回の整合性チェック

Supabase SQL Editor で `integrity_checks.sql` の検証クエリを実行し、すべて `OK` であることを確認。

```sql
-- 簡易チェック
SELECT
    (SELECT COUNT(*) FROM auth.users) as auth_users,
    (SELECT COUNT(*) FROM public.profiles) as profiles;
```

両方の数が一致していれば正常です。
