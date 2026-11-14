-- 管理者で実行する読み取り専用の削除後確認。
-- COMMIT後の別セッションで実行し、さらに時間を置いて再実行する。
-- 必須テーブル欠落はゼロ扱いしない。理由を記録する。
-- publicの削除対象・auth.users/sessions/refresh_tokensは全て0件が合格。
-- その他のauth/storageの非ゼロ行は要調査。設定行と個人データを区別し、
-- 個人データが残れば未完了。プロジェクト削除なら先にこの結果を記録。
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

-- VIEWの行数も確認。通常ビュー以外なら01の棚卸しと合わせ要確認。
DO $view$
DECLARE n bigint;
BEGIN
    IF to_regclass('public.latest_consent_status') IS NOT NULL THEN
        SELECT count(*) INTO n FROM public.latest_consent_status;
        RAISE NOTICE 'public.latest_consent_status: % rows', n;
    ELSE
        RAISE NOTICE 'public.latest_consent_status absent: verify deployment history';
    END IF;
END
$view$;
-- Supabaseバックアップ/ログ、Discord投稿、KV、Storage実体はこのSQLでは検証できない。
ROLLBACK;
