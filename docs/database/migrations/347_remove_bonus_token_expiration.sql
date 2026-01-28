-- ============================================================
-- Issue #347: ボーナストークンの有効期限削除マイグレーション
-- 実行日: 2026-01-28
-- ============================================================
--
-- 変更内容:
-- 1. bonus_tokens.expires_at カラム削除
-- 2. 関連インデックス更新
-- 3. RPC関数から期限関連パラメータ削除
--
-- 注意: 本番環境で実行前にバックアップを取得すること
-- ============================================================

-- ============================================================
-- Step 1: 旧インデックスの削除
-- ============================================================
DROP INDEX IF EXISTS idx_bonus_tokens_user_expires;
DROP INDEX IF EXISTS idx_bonus_tokens_active;

-- ============================================================
-- Step 2: expires_at カラムの削除
-- ============================================================
ALTER TABLE bonus_tokens DROP COLUMN IF EXISTS expires_at;

-- ============================================================
-- Step 3: 新しいインデックス作成
-- ============================================================
-- ユーザーのボーナスを作成日順に取得
CREATE INDEX IF NOT EXISTS idx_bonus_tokens_user_created
ON bonus_tokens(user_id, created_at DESC);

-- 有効なボーナスのみ取得（部分インデックス）
CREATE INDEX IF NOT EXISTS idx_bonus_tokens_active
ON bonus_tokens(user_id)
WHERE used_tokens < granted_tokens;

-- ============================================================
-- Step 4: RPC関数の更新 - get_bonus_tokens()
-- ============================================================
CREATE OR REPLACE FUNCTION get_bonus_tokens()
RETURNS TABLE (
    id UUID,
    source_type VARCHAR(50),
    granted_tokens BIGINT,
    used_tokens BIGINT,
    remaining_tokens BIGINT,
    created_at TIMESTAMPTZ
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
        bt.created_at
    FROM bonus_tokens bt
    WHERE bt.user_id = v_user_id
    ORDER BY bt.created_at DESC;
END;
$$;

-- ============================================================
-- Step 5: RPC関数の更新 - get_bonus_tokens_for_user()
-- ============================================================
CREATE OR REPLACE FUNCTION get_bonus_tokens_for_user(p_user_id UUID)
RETURNS TABLE (
    id UUID,
    source_type VARCHAR(50),
    granted_tokens BIGINT,
    used_tokens BIGINT,
    remaining_tokens BIGINT,
    created_at TIMESTAMPTZ
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
        bt.created_at
    FROM bonus_tokens bt
    WHERE bt.user_id = p_user_id
    ORDER BY bt.created_at DESC;
END;
$$;

-- ============================================================
-- Step 6: RPC関数の更新 - grant_bonus_tokens()
-- p_expires_at パラメータを削除
-- ============================================================
-- 旧関数を削除（シグネチャが変わるため）
DROP FUNCTION IF EXISTS grant_bonus_tokens(UUID, VARCHAR, UUID, BIGINT, TIMESTAMPTZ);

CREATE OR REPLACE FUNCTION grant_bonus_tokens(
    p_user_id UUID,
    p_source_type VARCHAR(50),
    p_source_id UUID,
    p_granted_tokens BIGINT
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

    -- ボーナス付与（有効期限なし）
    INSERT INTO bonus_tokens (
        user_id,
        source_type,
        source_id,
        granted_tokens
    ) VALUES (
        p_user_id,
        p_source_type,
        p_source_id,
        p_granted_tokens
    )
    RETURNING id INTO v_bonus_id;

    RETURN v_bonus_id;
END;
$$;

REVOKE ALL ON FUNCTION grant_bonus_tokens(UUID, VARCHAR, UUID, BIGINT) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION grant_bonus_tokens(UUID, VARCHAR, UUID, BIGINT) TO service_role;

-- ============================================================
-- Step 7: コメント更新
-- ============================================================
COMMENT ON TABLE bonus_tokens IS 'Issue #280+#281+#347: プロモーション等で付与されたボーナストークン（有効期限なし・永続）';
COMMENT ON COLUMN bonus_tokens.created_at IS '付与日時';

-- ============================================================
-- 完了メッセージ
-- ============================================================
DO $$
BEGIN
    RAISE NOTICE 'Issue #347 マイグレーション完了: bonus_tokens.expires_at を削除しました';
END;
$$;
