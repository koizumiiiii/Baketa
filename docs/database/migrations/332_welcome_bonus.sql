-- ============================================================
-- Issue #332: ウェルカムボーナス自動付与
-- 実行日: 2026-01-28
-- ============================================================
--
-- 新規ユーザーに対してウェルカムボーナスを自動付与する機能を追加
-- 付与量: 25,000トークン（約10回のAPI利用分）
-- ============================================================

-- ============================================================
-- Step 1: ウェルカムボーナス設定テーブル
-- ============================================================
CREATE TABLE IF NOT EXISTS welcome_bonus_config (
    id SERIAL PRIMARY KEY,
    granted_tokens BIGINT NOT NULL DEFAULT 25000,  -- 付与トークン数
    is_active BOOLEAN NOT NULL DEFAULT true,       -- 有効/無効
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- 初期設定を挿入（存在しない場合のみ）
INSERT INTO welcome_bonus_config (granted_tokens, is_active)
SELECT 25000, true
WHERE NOT EXISTS (SELECT 1 FROM welcome_bonus_config);

-- RLS有効化
ALTER TABLE welcome_bonus_config ENABLE ROW LEVEL SECURITY;

-- 管理者のみ更新可能
CREATE POLICY "Service role can manage welcome bonus config"
    ON welcome_bonus_config FOR ALL
    USING (true)
    WITH CHECK (true);

REVOKE ALL ON welcome_bonus_config FROM PUBLIC;
GRANT SELECT ON welcome_bonus_config TO authenticated;
GRANT ALL ON welcome_bonus_config TO service_role;

-- ============================================================
-- Step 2: ウェルカムボーナス確認・付与関数（サービスロール用）
-- ============================================================
-- 戻り値:
--   NULL: 既にウェルカムボーナスを受け取っている
--   UUID: 新規付与されたボーナスID
-- ============================================================
CREATE OR REPLACE FUNCTION ensure_welcome_bonus_for_user(p_user_id UUID)
RETURNS UUID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_existing_bonus UUID;
    v_new_bonus_id UUID;
    v_granted_tokens BIGINT;
    v_is_active BOOLEAN;
BEGIN
    -- 入力検証
    IF p_user_id IS NULL THEN
        RAISE EXCEPTION 'user_id is required';
    END IF;

    -- 設定を取得
    SELECT granted_tokens, is_active INTO v_granted_tokens, v_is_active
    FROM welcome_bonus_config
    LIMIT 1;

    -- 機能が無効の場合はスキップ
    IF NOT COALESCE(v_is_active, false) THEN
        RETURN NULL;
    END IF;

    -- 既存のウェルカムボーナスをチェック
    SELECT id INTO v_existing_bonus
    FROM bonus_tokens
    WHERE user_id = p_user_id
      AND source_type = 'welcome'
    LIMIT 1;

    -- 既に付与済みの場合はNULLを返す
    IF v_existing_bonus IS NOT NULL THEN
        RETURN NULL;
    END IF;

    -- ウェルカムボーナスを付与
    INSERT INTO bonus_tokens (
        user_id,
        source_type,
        source_id,
        granted_tokens
    ) VALUES (
        p_user_id,
        'welcome',
        NULL,
        COALESCE(v_granted_tokens, 25000)
    )
    RETURNING id INTO v_new_bonus_id;

    RETURN v_new_bonus_id;
END;
$$;

REVOKE ALL ON FUNCTION ensure_welcome_bonus_for_user(UUID) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION ensure_welcome_bonus_for_user(UUID) TO service_role;

-- ============================================================
-- Step 3: ウェルカムボーナス状態確認関数（authenticated用）
-- ============================================================
-- 自分のウェルカムボーナス状態を確認
-- ============================================================
CREATE OR REPLACE FUNCTION check_welcome_bonus_status()
RETURNS TABLE (
    has_welcome_bonus BOOLEAN,
    bonus_id UUID,
    granted_tokens BIGINT,
    remaining_tokens BIGINT
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
        bt.id IS NOT NULL AS has_welcome_bonus,
        bt.id AS bonus_id,
        bt.granted_tokens,
        (bt.granted_tokens - bt.used_tokens)::BIGINT AS remaining_tokens
    FROM (SELECT 1) AS dummy
    LEFT JOIN bonus_tokens bt ON bt.user_id = v_user_id AND bt.source_type = 'welcome';
END;
$$;

GRANT EXECUTE ON FUNCTION check_welcome_bonus_status() TO authenticated;

-- ============================================================
-- コメント
-- ============================================================
COMMENT ON TABLE welcome_bonus_config IS 'Issue #332: ウェルカムボーナス設定';
COMMENT ON FUNCTION ensure_welcome_bonus_for_user(UUID) IS 'Issue #332: ユーザーにウェルカムボーナスを付与（未付与の場合のみ）';
COMMENT ON FUNCTION check_welcome_bonus_status() IS 'Issue #332: 自分のウェルカムボーナス状態を確認';

-- ============================================================
-- 完了メッセージ
-- ============================================================
DO $$
BEGIN
    RAISE NOTICE 'Issue #332 マイグレーション完了: ウェルカムボーナス機能を追加しました';
END;
$$;
