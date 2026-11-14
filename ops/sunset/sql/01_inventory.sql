-- Supabase SQL Editor: 管理者で実行。読み取り専用。RLSによる過少集計を避ける。
-- 結果のうち全期間の件数のみポートフォリオ用に保存。小集団・属性別集計は公開しない。
-- auth.users = アカウント数、usage_events = 利用イベント数、
-- crash_reports = クラッシュ報告数（利用人数とは異なる）。
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL row_security = off;
-- 欠落は「0件」と扱わない。未適用か削除済みかを所有者が確認。
SELECT name AS expected_relation, to_regclass(name) IS NOT NULL AS present
FROM unnest(ARRAY[
    'public.usage_events',
    'public.promotion_code_redemptions',
    'public.crash_reports',
    'public.license_history',
    'public.consent_records',
    'public.token_usage',
    'public.bonus_tokens',
    'public.profiles',
    'public.promotion_codes',
    'public.keepalive',
    'public.welcome_bonus_config',
    'public.latest_consent_status',
    'auth.users',
    'auth.sessions',
    'auth.refresh_tokens'
]) AS expected(name);

-- 行の内容・メール・ID・IP・トークンは出力しない。SQL Editor の Messages/NOTICE を確認。
-- auth/storage の追加テーブルは本番版依存。全件数を調べ、残存を見落とさない。
DO $counts$
DECLARE
    r record;
    n bigint;
BEGIN
    FOR r IN
        SELECT ns.nspname AS schema_name, c.relname AS table_name
        FROM pg_class c JOIN pg_namespace ns ON ns.oid = c.relnamespace
        WHERE ns.nspname IN ('public', 'auth', 'storage')
          AND c.relkind IN ('r', 'p', 'm')
          AND NOT c.relispartition
        ORDER BY ns.nspname, c.relname
    LOOP
        EXECUTE format('SELECT count(*) FROM %I.%I', r.schema_name, r.table_name) INTO n;
        RAISE NOTICE '%.%: % rows', r.schema_name, r.table_name, n;
    END LOOP;
END
$counts$;

-- 追加スキーマ・テーブルを棚卸し。名前のみ出力し、内容は取得しない。
SELECT n.nspname AS schema_name, c.relname AS relation_name, c.relkind
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
  AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp%'
  AND c.relkind IN ('r', 'p', 'm', 'v')
ORDER BY 1, 2;

-- 実DBのFKを確認。文書より実DBを優先し、未確認の依存があれば削除を止める。
SELECT conrelid::regclass AS child_table, confrelid::regclass AS parent_table,
       conname, pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE contype = 'f'
ORDER BY 1, 2;

-- 削除トリガーが外部通知・再挿入を起こさないか所有者が別途定義を確認。
SELECT event_object_schema, event_object_table, trigger_name,
       event_manipulation, action_timing
FROM information_schema.triggers
WHERE event_object_schema IN ('public', 'auth', 'storage')
ORDER BY 1, 2, 3;
ROLLBACK;
