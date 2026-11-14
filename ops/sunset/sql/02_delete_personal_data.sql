-- 破壊的処理。本番実行は所有者のみ。初期状態は必ずROLLBACK。
-- 前提: READMEの受付停止・直接アクセス遮断・Storage実体削除・01_inventory完了。
-- 本番FK/トリガー/追加テーブルを照合してから、全体を一括実行する。
-- ROLLBACKでも外部通知などトリガーの副作用は取り消せないため先に無効化する。
-- 初回の件数を確認後、最後のROLLBACKだけCOMMITへ変更して全体を再実行。
-- COMMIT後の復元は不可。個人データのバックアップを残さない。
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '5min';
SET LOCAL row_security = off;

-- SET NULLのusage_events / promotion_code_redemptionsも匿名・NULL行を含め全削除。
DELETE FROM public.usage_events;
DELETE FROM public.promotion_code_redemptions;
-- user_idのないIP・ログ・スタックトレース、Patreonのみの履歴も全削除。
DELETE FROM public.crash_reports;
DELETE FROM public.license_history;
DELETE FROM public.consent_records;
DELETE FROM public.token_usage;
DELETE FROM public.bonus_tokens;
-- コードdescriptionにも自由記述があり得る。redemptionsより後に削除。
DELETE FROM public.promotion_codes;
DELETE FROM public.profiles;

-- 非個人の運用設定。存在する場合のみ削除（適用状況は要確認）。
DO $optional$
DECLARE name text;
BEGIN
    FOREACH name IN ARRAY ARRAY['keepalive', 'welcome_bonus_config']
    LOOP
        IF to_regclass('public.' || name) IS NOT NULL THEN
            EXECUTE format('DELETE FROM public.%I', name);
        ELSE
            RAISE NOTICE 'public.% absent: deployment history must be checked', name;
        END IF;
    END LOOP;
END
$optional$;

-- auth内部を直接変更することはスキーマ文書では非推奨。
-- この撤収用SQLは実DBのFKと管理権限を確認した専用プロジェクトでのみ使用する。
-- 確認できなければここを実行せずAuth管理画面/Admin APIのhard deleteを使い、
-- 同じ削除後検証を実施する（soft deleteだけでは完了にならない）。
-- 2026-11-14に本番FKを確認済み。users削除でidentities / mfa_factors /
-- one_time_tokens / oauth_authorizations / oauth_consents / webauthn_credentials /
-- webauthn_challenges / mfa_recovery_code_setsがCASCADE削除される。
-- sessionsもusersからCASCADE、refresh_tokens / mfa_amr_claimsはsessionsからCASCADE。
-- users削除でSET NULLとなるauth.scim_usersは同日の確認で0件。
-- flow_stateにはusersへのFKがなく、user_idやOAuthプロバイダートークンが残り得るため、
-- ユーザー削除に先立ち全件削除する。その後refresh_tokens -> sessions -> usersの順。
DELETE FROM auth.flow_state;
DELETE FROM auth.refresh_tokens;
DELETE FROM auth.sessions;
DELETE FROM auth.users;

-- その他のauth内部テーブルは03で残存を確認し、個人データがあれば
-- 管理機能で消去するか、期限内にプロジェクトを削除する。
-- auth.schema_migrations / storage.migrationsはスキーマ管理用で個人データではなく削除しない。
-- StorageはSQLでmetadataだけ削除しない。先にStorage管理機能で実体を削除。
-- latest_consent_statusは通常VIEWで、consent_records削除が反映される。
-- 未知のテーブルやmaterialized viewがあれば別途削除/更新が必要。
-- 行の内容・メール・ID・IP・トークンは出力しない。SQL Editor の結果表で件数を確認。
-- auth/storage の追加テーブルは本番版依存。全件数を調べ、残存を見落とさない。
SELECT ns.nspname AS schema_name, c.relname AS table_name,
       (xpath('/row/c/text()', query_to_xml(
           format('SELECT count(*) AS c FROM %I.%I', ns.nspname, c.relname),
           false, true, '')))[1]::text::bigint AS rows
FROM pg_class c JOIN pg_namespace ns ON ns.oid = c.relnamespace
WHERE ns.nspname IN ('public', 'auth', 'storage')
  AND c.relkind IN ('r', 'p', 'm')
  AND NOT c.relispartition
ORDER BY 1, 2;

-- 初回は変更を破棄する。上の結果表の件数が0でも、ROLLBACK後は元の件数に戻る。
ROLLBACK;
