-- ============================================================
-- Issue #280 + #281: ボーナストークンテーブル
-- プロモーションコード等で付与されたボーナストークンを管理
-- ============================================================
--
-- 設計背景:
-- - プロモーションを「プラン付与」ではなく「トークン付与」として扱う
-- - 複数のプロモーションを個別に管理可能
-- - 有効期限が近い順に消費
-- - CRDT G-Counterパターンで競合解決（大きい方を採用）
-- ============================================================

-- ============================================================
-- テーブル定義
-- ============================================================
CREATE TABLE IF NOT EXISTS bonus_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,

    -- ボーナスの出所
    source_type VARCHAR(50) NOT NULL,  -- 'promotion', 'campaign', 'referral' 等
    source_id UUID,                     -- promotion_code_redemptions.id 等

    -- トークン管理
    granted_tokens BIGINT NOT NULL,     -- 付与トークン数
    used_tokens BIGINT NOT NULL DEFAULT 0,  -- 使用済み（サーバー同期対象）

    -- 有効期限
    expires_at TIMESTAMPTZ NOT NULL,

    -- メタデータ
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- 制約
    CONSTRAINT positive_granted CHECK (granted_tokens > 0),
    CONSTRAINT valid_usage CHECK (used_tokens >= 0 AND used_tokens <= granted_tokens)
);

-- インデックス: ユーザーのボーナスを有効期限順に取得
CREATE INDEX IF NOT EXISTS idx_bonus_tokens_user_expires
ON bonus_tokens(user_id, expires_at ASC);

-- インデックス: 有効なボーナスのみ取得（部分インデックス）
CREATE INDEX IF NOT EXISTS idx_bonus_tokens_active
ON bonus_tokens(user_id, expires_at)
WHERE used_tokens < granted_tokens;

-- インデックス: source_idで検索（プロモーションコードとの紐付け）
CREATE INDEX IF NOT EXISTS idx_bonus_tokens_source
ON bonus_tokens(source_type, source_id)
WHERE source_id IS NOT NULL;

-- RLS有効化
ALTER TABLE bonus_tokens ENABLE ROW LEVEL SECURITY;

-- ユーザーは自分のボーナスのみ参照可能
CREATE POLICY "Users can view own bonus tokens"
    ON bonus_tokens FOR SELECT
    USING (auth.uid() = user_id);

-- INSERT/UPDATE/DELETEはサービスロールのみ

-- ============================================================
-- RPC関数: ボーナストークン状態取得（authenticated用）
-- ============================================================
CREATE OR REPLACE FUNCTION get_bonus_tokens()
RETURNS TABLE (
    id UUID,
    source_type VARCHAR(50),
    granted_tokens BIGINT,
    used_tokens BIGINT,
    remaining_tokens BIGINT,
    expires_at TIMESTAMPTZ,
    is_expired BOOLEAN
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
        RAISE EXCEPTION 'Not authenticated';
    END IF;

    RETURN QUERY
    SELECT
        bt.id,
        bt.source_type,
        bt.granted_tokens,
        bt.used_tokens,
        (bt.granted_tokens - bt.used_tokens)::BIGINT AS remaining_tokens,
        bt.expires_at,
        (bt.expires_at < NOW())::BOOLEAN AS is_expired
    FROM bonus_tokens bt
    WHERE bt.user_id = v_user_id
    ORDER BY bt.expires_at ASC;
END;
$$;

GRANT EXECUTE ON FUNCTION get_bonus_tokens() TO authenticated;

-- ============================================================
-- RPC関数: ボーナストークン同期（authenticated用）
-- ============================================================
-- [Gemini Review] CRDT G-Counterパターン: 各ボーナスで大きい方を採用
CREATE OR REPLACE FUNCTION sync_bonus_tokens(p_bonuses JSONB)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_user_id UUID;
    v_bonus RECORD;
    v_result JSONB := '[]'::JSONB;
    v_synced_bonus JSONB;
BEGIN
    -- 認証チェック
    v_user_id := auth.uid();
    IF v_user_id IS NULL THEN
        RAISE EXCEPTION 'Not authenticated';
    END IF;

    -- 入力検証
    IF p_bonuses IS NULL OR jsonb_array_length(p_bonuses) = 0 THEN
        RETURN v_result;
    END IF;

    -- 各ボーナスを同期
    FOR v_bonus IN SELECT * FROM jsonb_to_recordset(p_bonuses) AS x(id UUID, used_tokens BIGINT)
    LOOP
        -- 入力値検証
        IF v_bonus.used_tokens < 0 THEN
            RAISE EXCEPTION 'used_tokens must be non-negative';
        END IF;

        -- CRDT G-Counter: 大きい方を採用
        UPDATE bonus_tokens bt
        SET
            used_tokens = GREATEST(bt.used_tokens, v_bonus.used_tokens),
            updated_at = NOW()
        WHERE bt.id = v_bonus.id
          AND bt.user_id = v_user_id
        RETURNING jsonb_build_object(
            'id', bt.id,
            'used_tokens', bt.used_tokens,
            'remaining_tokens', bt.granted_tokens - bt.used_tokens
        ) INTO v_synced_bonus;

        IF v_synced_bonus IS NOT NULL THEN
            v_result := v_result || v_synced_bonus;
        END IF;
    END LOOP;

    RETURN v_result;
END;
$$;

GRANT EXECUTE ON FUNCTION sync_bonus_tokens(JSONB) TO authenticated;

-- ============================================================
-- RPC関数: ボーナストークン状態取得（サービスロール用）
-- ============================================================
CREATE OR REPLACE FUNCTION get_bonus_tokens_for_user(p_user_id UUID)
RETURNS TABLE (
    id UUID,
    source_type VARCHAR(50),
    granted_tokens BIGINT,
    used_tokens BIGINT,
    remaining_tokens BIGINT,
    expires_at TIMESTAMPTZ,
    is_expired BOOLEAN
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
        bt.id,
        bt.source_type,
        bt.granted_tokens,
        bt.used_tokens,
        (bt.granted_tokens - bt.used_tokens)::BIGINT AS remaining_tokens,
        bt.expires_at,
        (bt.expires_at < NOW())::BOOLEAN AS is_expired
    FROM bonus_tokens bt
    WHERE bt.user_id = p_user_id
    ORDER BY bt.expires_at ASC;
END;
$$;

REVOKE ALL ON FUNCTION get_bonus_tokens_for_user(UUID) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION get_bonus_tokens_for_user(UUID) TO service_role;

-- ============================================================
-- RPC関数: ボーナストークン同期（サービスロール用）
-- ============================================================
CREATE OR REPLACE FUNCTION sync_bonus_tokens_for_user(p_user_id UUID, p_bonuses JSONB)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_bonus RECORD;
    v_result JSONB := '[]'::JSONB;
    v_synced_bonus JSONB;
BEGIN
    IF p_user_id IS NULL THEN
        RAISE EXCEPTION 'user_id is required';
    END IF;

    IF p_bonuses IS NULL OR jsonb_array_length(p_bonuses) = 0 THEN
        RETURN v_result;
    END IF;

    FOR v_bonus IN SELECT * FROM jsonb_to_recordset(p_bonuses) AS x(id UUID, used_tokens BIGINT)
    LOOP
        IF v_bonus.used_tokens < 0 THEN
            RAISE EXCEPTION 'used_tokens must be non-negative';
        END IF;

        UPDATE bonus_tokens bt
        SET
            used_tokens = GREATEST(bt.used_tokens, v_bonus.used_tokens),
            updated_at = NOW()
        WHERE bt.id = v_bonus.id
          AND bt.user_id = p_user_id
        RETURNING jsonb_build_object(
            'id', bt.id,
            'used_tokens', bt.used_tokens,
            'remaining_tokens', bt.granted_tokens - bt.used_tokens
        ) INTO v_synced_bonus;

        IF v_synced_bonus IS NOT NULL THEN
            v_result := v_result || v_synced_bonus;
        END IF;
    END LOOP;

    RETURN v_result;
END;
$$;

REVOKE ALL ON FUNCTION sync_bonus_tokens_for_user(UUID, JSONB) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sync_bonus_tokens_for_user(UUID, JSONB) TO service_role;

-- ============================================================
-- RPC関数: ボーナストークン付与（サービスロール用）
-- ============================================================
-- プロモーションコード適用時に呼び出される
CREATE OR REPLACE FUNCTION grant_bonus_tokens(
    p_user_id UUID,
    p_source_type VARCHAR(50),
    p_source_id UUID,
    p_granted_tokens BIGINT,
    p_expires_at TIMESTAMPTZ
)
RETURNS UUID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_bonus_id UUID;
BEGIN
    -- 入力検証
    IF p_user_id IS NULL THEN
        RAISE EXCEPTION 'user_id is required';
    END IF;

    IF p_granted_tokens <= 0 THEN
        RAISE EXCEPTION 'granted_tokens must be positive';
    END IF;

    IF p_expires_at <= NOW() THEN
        RAISE EXCEPTION 'expires_at must be in the future';
    END IF;

    -- ボーナス付与
    INSERT INTO bonus_tokens (
        user_id,
        source_type,
        source_id,
        granted_tokens,
        expires_at
    ) VALUES (
        p_user_id,
        p_source_type,
        p_source_id,
        p_granted_tokens,
        p_expires_at
    )
    RETURNING id INTO v_bonus_id;

    RETURN v_bonus_id;
END;
$$;

REVOKE ALL ON FUNCTION grant_bonus_tokens(UUID, VARCHAR, UUID, BIGINT, TIMESTAMPTZ) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION grant_bonus_tokens(UUID, VARCHAR, UUID, BIGINT, TIMESTAMPTZ) TO service_role;

-- ============================================================
-- コメント
-- ============================================================
COMMENT ON TABLE bonus_tokens IS 'Issue #280+#281: プロモーション等で付与されたボーナストークン';
COMMENT ON COLUMN bonus_tokens.source_type IS 'ボーナスの出所: promotion, campaign, referral等';
COMMENT ON COLUMN bonus_tokens.source_id IS '出所の識別子（例: promotion_code_redemptions.id）';
COMMENT ON COLUMN bonus_tokens.granted_tokens IS '付与されたトークン数';
COMMENT ON COLUMN bonus_tokens.used_tokens IS '使用済みトークン数（CRDT G-Counterで同期）';
COMMENT ON COLUMN bonus_tokens.expires_at IS '有効期限（これを過ぎると消費不可）';
