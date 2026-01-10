-- ============================================================
-- Issue #271: プラン変更履歴テーブル
-- ユーザーのプラン変更（アップグレード/ダウングレード）を追跡
-- ============================================================
--
-- 注意: このテーブルはPatreon user_idをプライマリ識別子として使用。
-- 理由: Patreon WebhookはPatreon IDのみ提供し、Supabase user_idへの
--       マッピングが現状では存在しないため。
-- 将来的にprofilesテーブルにpatreon_idカラムを追加した場合、
-- user_idカラムを更新してリンク可能。
-- ============================================================

-- ============================================================
-- Phase 2: license_historyテーブル
-- ============================================================

CREATE TABLE IF NOT EXISTS license_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- ユーザー識別子（いずれかが必須）
    user_id UUID REFERENCES auth.users(id) ON DELETE CASCADE,  -- Supabase user_id（NULL許可）
    patreon_user_id TEXT,  -- Patreon user_id（Webhook用）

    -- プラン変更情報
    old_plan TEXT,  -- NULLは初回登録または不明
    new_plan TEXT NOT NULL,

    -- タイムスタンプ
    changed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- 変更元（監査用）
    source TEXT NOT NULL CHECK (source IN (
        'patreon_sync',      -- クライアントからの同期時に検知
        'patreon_webhook',   -- PatreonからのWebhook通知
        'promotion_code',    -- プロモーションコード適用
        'admin',             -- 管理者による手動変更
        'system'             -- システムによる自動変更（期限切れ等）
    )),

    -- Patreonトレース情報（Patreon経由の場合のみ）
    patreon_pledge_id TEXT,

    -- 追加メタデータ（柔軟な情報格納）
    metadata JSONB DEFAULT '{}'::jsonb,

    -- 少なくとも1つの識別子が必須
    CONSTRAINT at_least_one_user_id CHECK (user_id IS NOT NULL OR patreon_user_id IS NOT NULL),

    -- 同じプランへの変更は禁止（変更がなければ記録しない）
    CONSTRAINT valid_plan_change CHECK (old_plan IS DISTINCT FROM new_plan)
);

-- パフォーマンス用インデックス
CREATE INDEX IF NOT EXISTS idx_license_history_user_id
    ON license_history(user_id) WHERE user_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_license_history_patreon_user_id
    ON license_history(patreon_user_id) WHERE patreon_user_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_license_history_changed_at
    ON license_history(changed_at DESC);

CREATE INDEX IF NOT EXISTS idx_license_history_new_plan
    ON license_history(new_plan);

-- 複合インデックス: ユーザー別の履歴を時系列で取得
CREATE INDEX IF NOT EXISTS idx_license_history_user_timeline
    ON license_history(user_id, changed_at DESC) WHERE user_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_license_history_patreon_timeline
    ON license_history(patreon_user_id, changed_at DESC) WHERE patreon_user_id IS NOT NULL;

-- [Gemini Review] 冪等性について
-- Webhook再試行時の重複登録は以下のロジックで防止:
-- 1. get_latest_plan_by_patreon() で前回プランを取得
-- 2. oldPlan !== newPlan の場合のみ記録
-- 3. record_plan_change_by_patreon() でも IS DISTINCT FROM チェック
-- これにより、同じWebhookイベントが再送されても重複記録されない。
-- 注意: pledge_id + source のUNIQUEインデックスは、同じpledge_idで
-- 複数回のプラン変更（Pro→Premium→Ultimate）が発生する場合に問題となるため不採用。

-- RLS有効化
ALTER TABLE license_history ENABLE ROW LEVEL SECURITY;

-- ユーザーは自分の履歴のみ参照可能
CREATE POLICY "Users can view own license history"
    ON license_history FOR SELECT
    USING (auth.uid() = user_id);

-- INSERT/UPDATE/DELETEはサービスロールのみ（クライアントからの直接変更禁止）
-- RLSポリシーを作成しないことで、authenticated roleからのINSERT/UPDATE/DELETEを禁止

-- ============================================================
-- RPC関数: プラン変更を記録（サービスロール用 - Patreon ID版）
-- ============================================================
-- Relay Server Webhookから呼び出される
--
-- 使用例:
-- SELECT record_plan_change_by_patreon(
--     'patreon-user-id',
--     'Free',           -- old_plan (NULL可)
--     'Pro',            -- new_plan
--     'patreon_webhook',
--     'pledge-id',      -- patreon_pledge_id (NULL可)
--     '{"event": "members:pledge:update"}'::jsonb
-- );
-- ============================================================
CREATE OR REPLACE FUNCTION record_plan_change_by_patreon(
    p_patreon_user_id TEXT,
    p_old_plan TEXT,
    p_new_plan TEXT,
    p_source TEXT,
    p_patreon_pledge_id TEXT DEFAULT NULL,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS UUID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_record_id UUID;
BEGIN
    -- 入力検証
    IF p_patreon_user_id IS NULL OR p_patreon_user_id = '' THEN
        RAISE EXCEPTION 'patreon_user_id is required';
    END IF;

    IF p_new_plan IS NULL OR p_new_plan = '' THEN
        RAISE EXCEPTION 'new_plan is required';
    END IF;

    -- 同じプランへの変更は記録しない（CHECK制約で弾かれるが、明示的にチェック）
    IF p_old_plan IS NOT DISTINCT FROM p_new_plan THEN
        RETURN NULL;  -- 変更なしの場合はNULLを返す
    END IF;

    -- レコード挿入
    INSERT INTO license_history (
        patreon_user_id,
        old_plan,
        new_plan,
        source,
        patreon_pledge_id,
        metadata
    ) VALUES (
        p_patreon_user_id,
        p_old_plan,
        p_new_plan,
        p_source,
        p_patreon_pledge_id,
        p_metadata
    )
    RETURNING id INTO v_record_id;

    RETURN v_record_id;
END;
$$;

-- サービスロールのみ実行可能
REVOKE ALL ON FUNCTION record_plan_change_by_patreon(TEXT, TEXT, TEXT, TEXT, TEXT, JSONB) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION record_plan_change_by_patreon(TEXT, TEXT, TEXT, TEXT, TEXT, JSONB) TO service_role;

-- ============================================================
-- RPC関数: プラン変更を記録（サービスロール用 - Supabase user_id版）
-- ============================================================
-- クライアントからのプロモーション適用時に使用
--
-- 使用例:
-- SELECT record_plan_change(
--     'user-uuid',
--     'Free',           -- old_plan (NULL可)
--     'Pro',            -- new_plan
--     'promotion_code',
--     NULL,             -- patreon_pledge_id
--     '{"promotion_code": "BAKETA-XXXX-XXXX"}'::jsonb
-- );
-- ============================================================
CREATE OR REPLACE FUNCTION record_plan_change(
    p_user_id UUID,
    p_old_plan TEXT,
    p_new_plan TEXT,
    p_source TEXT,
    p_patreon_pledge_id TEXT DEFAULT NULL,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS UUID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_record_id UUID;
BEGIN
    -- 入力検証
    IF p_user_id IS NULL THEN
        RAISE EXCEPTION 'user_id is required';
    END IF;

    IF p_new_plan IS NULL OR p_new_plan = '' THEN
        RAISE EXCEPTION 'new_plan is required';
    END IF;

    -- 同じプランへの変更は記録しない（CHECK制約で弾かれるが、明示的にチェック）
    IF p_old_plan IS NOT DISTINCT FROM p_new_plan THEN
        RETURN NULL;  -- 変更なしの場合はNULLを返す
    END IF;

    -- レコード挿入
    INSERT INTO license_history (
        user_id,
        old_plan,
        new_plan,
        source,
        patreon_pledge_id,
        metadata
    ) VALUES (
        p_user_id,
        p_old_plan,
        p_new_plan,
        p_source,
        p_patreon_pledge_id,
        p_metadata
    )
    RETURNING id INTO v_record_id;

    RETURN v_record_id;
END;
$$;

-- サービスロールのみ実行可能
REVOKE ALL ON FUNCTION record_plan_change(UUID, TEXT, TEXT, TEXT, TEXT, JSONB) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION record_plan_change(UUID, TEXT, TEXT, TEXT, TEXT, JSONB) TO service_role;

-- ============================================================
-- RPC関数: 自身のプラン変更履歴を取得（authenticated用）
-- ============================================================
-- クライアントから呼び出される
--
-- 使用例:
-- SELECT * FROM get_plan_history(10);
-- ============================================================
CREATE OR REPLACE FUNCTION get_plan_history(
    p_limit INTEGER DEFAULT 20
)
RETURNS TABLE (
    id UUID,
    old_plan TEXT,
    new_plan TEXT,
    changed_at TIMESTAMPTZ,
    source TEXT
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_user_id UUID;
BEGIN
    v_user_id := auth.uid();

    IF v_user_id IS NULL THEN
        -- 未認証: 空の結果を返す
        RETURN;
    END IF;

    RETURN QUERY
    SELECT
        lh.id,
        lh.old_plan,
        lh.new_plan,
        lh.changed_at,
        lh.source
    FROM license_history lh
    WHERE lh.user_id = v_user_id
    ORDER BY lh.changed_at DESC
    LIMIT p_limit;
END;
$$;

GRANT EXECUTE ON FUNCTION get_plan_history(INTEGER) TO authenticated;

-- ============================================================
-- RPC関数: 特定ユーザーの履歴取得（サービスロール用）
-- ============================================================
-- Relay Serverから呼び出される（管理者機能など）
--
-- 使用例:
-- SELECT * FROM get_plan_history_for_user('user-uuid', 10);
-- ============================================================
CREATE OR REPLACE FUNCTION get_plan_history_for_user(
    p_user_id UUID,
    p_limit INTEGER DEFAULT 20
)
RETURNS TABLE (
    id UUID,
    old_plan TEXT,
    new_plan TEXT,
    changed_at TIMESTAMPTZ,
    source TEXT,
    patreon_pledge_id TEXT,
    metadata JSONB
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
    IF p_user_id IS NULL THEN
        RAISE EXCEPTION 'user_id is required';
    END IF;

    RETURN QUERY
    SELECT
        lh.id,
        lh.old_plan,
        lh.new_plan,
        lh.changed_at,
        lh.source,
        lh.patreon_pledge_id,
        lh.metadata
    FROM license_history lh
    WHERE lh.user_id = p_user_id
    ORDER BY lh.changed_at DESC
    LIMIT p_limit;
END;
$$;

REVOKE ALL ON FUNCTION get_plan_history_for_user(UUID, INTEGER) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION get_plan_history_for_user(UUID, INTEGER) TO service_role;

-- ============================================================
-- RPC関数: ユーザーの最新プランを取得（サービスロール用 - Supabase user_id版）
-- ============================================================
-- プロモーションコード適用時に前回のプランを取得するために使用
--
-- 使用例:
-- SELECT get_latest_plan_for_user('user-uuid');
-- ============================================================
CREATE OR REPLACE FUNCTION get_latest_plan_for_user(
    p_user_id UUID
)
RETURNS TEXT
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_latest_plan TEXT;
BEGIN
    IF p_user_id IS NULL THEN
        RETURN NULL;
    END IF;

    SELECT lh.new_plan INTO v_latest_plan
    FROM license_history lh
    WHERE lh.user_id = p_user_id
    ORDER BY lh.changed_at DESC
    LIMIT 1;

    RETURN v_latest_plan;  -- レコードがなければNULL
END;
$$;

REVOKE ALL ON FUNCTION get_latest_plan_for_user(UUID) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION get_latest_plan_for_user(UUID) TO service_role;

-- ============================================================
-- RPC関数: Patreonユーザーの最新プランを取得（サービスロール用）
-- ============================================================
-- Webhook処理時に前回のプランを取得するために使用
--
-- 使用例:
-- SELECT get_latest_plan_by_patreon('patreon-user-id');
-- ============================================================
CREATE OR REPLACE FUNCTION get_latest_plan_by_patreon(
    p_patreon_user_id TEXT
)
RETURNS TEXT
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_latest_plan TEXT;
BEGIN
    IF p_patreon_user_id IS NULL OR p_patreon_user_id = '' THEN
        RETURN NULL;
    END IF;

    SELECT lh.new_plan INTO v_latest_plan
    FROM license_history lh
    WHERE lh.patreon_user_id = p_patreon_user_id
    ORDER BY lh.changed_at DESC
    LIMIT 1;

    RETURN v_latest_plan;  -- レコードがなければNULL
END;
$$;

REVOKE ALL ON FUNCTION get_latest_plan_by_patreon(TEXT) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION get_latest_plan_by_patreon(TEXT) TO service_role;

-- ============================================================
-- コメント（ドキュメント用）
-- ============================================================
COMMENT ON TABLE license_history IS 'Issue #271: プラン変更履歴。アップグレード/ダウングレード/プロモーション適用を追跡。';
COMMENT ON COLUMN license_history.old_plan IS 'NULLは初回登録または変更前プラン不明を示す';
COMMENT ON COLUMN license_history.source IS '変更のトリガー元を識別';
COMMENT ON COLUMN license_history.patreon_pledge_id IS 'Patreon経由の変更時のみ設定、デバッグ/監査用';
COMMENT ON COLUMN license_history.metadata IS 'Webhookペイロード等の追加情報をJSON形式で格納';
