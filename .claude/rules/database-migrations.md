---
description: Supabase SQLマイグレーション編集時の注意事項
globs:
  - "docs/database/**/*.sql"
  - "supabase/**/*.sql"
---

# データベースマイグレーションルール

## 新規テーブル作成時のGRANT必須化（2026-10-30〜）

Supabaseの破壊的変更により、`public`スキーマで新規作成するテーブルは明示的な`GRANT`がないとData API（PostgREST/GraphQL/supabase-js）から不可視になる。Baketaは Relay Server (`relay-server/src/*.ts`) が`supabase-js`経由でテーブルにアクセスしているため、GRANT忘れは本番障害につながる。

参照: [Supabase Discussion #45329](https://github.com/orgs/supabase/discussions/45329)

## 必須テンプレート

新規テーブルを作成する際は、以下の4ステップを**同一マイグレーション内**に含めること:

```sql
-- ① テーブル作成
CREATE TABLE public.your_table (
  id BIGSERIAL PRIMARY KEY,
  -- ...
);

-- ② 明示的GRANT（必須）
GRANT SELECT, INSERT, UPDATE, DELETE ON public.your_table TO service_role;
-- 必要に応じて authenticated / anon にも付与
-- GRANT SELECT ON public.your_table TO authenticated;

-- シーケンスを使う場合は usage,select も付与
-- GRANT USAGE, SELECT ON SEQUENCE public.your_table_id_seq TO service_role;

-- ③ RLS有効化
ALTER TABLE public.your_table ENABLE ROW LEVEL SECURITY;

-- ④ ポリシー
CREATE POLICY "..." ON public.your_table FOR ... TO ... USING (...);
```

## チェックリスト

新規テーブル追加PR時に以下を確認:

- [ ] 該当ロール（`service_role` / `authenticated` / `anon`）への`GRANT`文を含めたか
- [ ] RLS有効化とポリシー定義を含めたか
- [ ] シーケンスがある場合は`USAGE, SELECT`の`GRANT`を付与したか
- [ ] Relay Server (`relay-server/src/*.ts`) からアクセスする場合は`service_role`への`GRANT`が必須

## 既存テーブルへの影響

既存テーブルは現在の`GRANT`設定が維持されるため、2026-10-30以降も動作継続。**新規テーブル作成時のみ**注意。

## 緊急時の対応

`permission denied for table`エラーが発生した場合、Supabase SQL Editorで該当テーブルに`GRANT`を実行することで即座に復旧可能。PostgRESTエラーレスポンスの`hint`フィールドに必要な`GRANT`文が含まれる。

## 詳細ドキュメント

- `docs/3-architecture/data-storage/supabase-schema.md` - スキーマ全体像と運用ルール
