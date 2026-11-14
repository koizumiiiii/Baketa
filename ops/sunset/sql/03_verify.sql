-- 管理者で実行する読み取り専用の削除後確認。
-- COMMIT後の別セッションで実行し、さらに時間を置いて再実行する。
-- 必須テーブル欠落はゼロ扱いしない。理由を記録する。
-- publicの削除対象・auth.users/sessions/refresh_tokensに加え、
-- auth.flow_state/identities/one_time_tokens/mfa_amr_claimsも全て0件が合格。
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

-- latest_consent_status は存在する場合のみ同じ結果表に含める。欠落は冒頭の存在確認SELECTを個別実行して確認。
-- 通常ビュー以外なら01の棚卸しと合わせ要確認。
-- 行の内容・メール・ID・IP・トークンは出力しない。SQL Editor の結果表で件数を確認。
-- auth/storage の追加テーブルは本番版依存。全件数を調べ、残存を見落とさない。
SELECT ns.nspname AS schema_name, c.relname AS table_name,
       (xpath('/row/c/text()', query_to_xml(
           format('SELECT count(*) AS c FROM %I.%I', ns.nspname, c.relname),
           false, true, '')))[1]::text::bigint AS rows
FROM pg_class c JOIN pg_namespace ns ON ns.oid = c.relnamespace
WHERE ns.nspname IN ('public', 'auth', 'storage')
  AND (c.relkind IN ('r', 'p', 'm')
       OR (ns.nspname = 'public' AND c.relname = 'latest_consent_status' AND c.relkind = 'v'))
  AND NOT c.relispartition
ORDER BY 1, 2;

-- Supabaseバックアップ/ログ、Discord投稿、KV、Storage実体はこのSQLでは検証できない。
ROLLBACK;
