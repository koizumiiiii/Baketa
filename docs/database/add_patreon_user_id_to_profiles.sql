-- ============================================================
-- Issue #295: profiles テーブルに patreon_user_id カラムを追加
-- ============================================================
--
-- 目的:
-- - license_history と profiles を JOIN できるようにする
-- - user_id と patreon_user_id の紐付けを profiles で一元管理
--
-- 実行手順:
-- 1. Supabase Console → SQL Editor でこのSQLを実行
-- 2. Relay Server: Patreon OAuth完了時に profiles.patreon_user_id を更新
--
-- ============================================================

-- ============================================================
-- 1. patreon_user_id カラム追加
-- ============================================================
ALTER TABLE public.profiles
ADD COLUMN IF NOT EXISTS patreon_user_id TEXT;

-- ============================================================
-- 2. ユニークインデックス作成（1 Patreon = 1 User）
-- ============================================================
CREATE UNIQUE INDEX IF NOT EXISTS idx_profiles_patreon_user_id
ON public.profiles(patreon_user_id)
WHERE patreon_user_id IS NOT NULL;

-- ============================================================
-- 3. 検索用インデックス
-- ============================================================
CREATE INDEX IF NOT EXISTS idx_profiles_patreon_lookup
ON public.profiles(patreon_user_id)
WHERE patreon_user_id IS NOT NULL;

-- ============================================================
-- 4. コメント
-- ============================================================
COMMENT ON COLUMN public.profiles.patreon_user_id IS 'Patreon ユーザーID（Patreon OAuth連携時に設定）';

-- ============================================================
-- 5. RPC関数: Patreon連携更新（サービスロール用）
-- ============================================================
-- Relay Server の Patreon OAuth 完了時に呼び出される
CREATE OR REPLACE FUNCTION link_patreon_user(
    p_user_id UUID,
    p_patreon_user_id TEXT
)
RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
    IF p_user_id IS NULL THEN
        RAISE EXCEPTION 'user_id is required';
    END IF;

    IF p_patreon_user_id IS NULL OR p_patreon_user_id = '' THEN
        RAISE EXCEPTION 'patreon_user_id is required';
    END IF;

    UPDATE profiles
    SET patreon_user_id = p_patreon_user_id
    WHERE id = p_user_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'User not found: %', p_user_id;
    END IF;
END;
$$;

REVOKE ALL ON FUNCTION link_patreon_user(UUID, TEXT) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION link_patreon_user(UUID, TEXT) TO service_role;

-- ============================================================
-- 6. RPC関数: Patreon ID から User ID を取得
-- ============================================================
CREATE OR REPLACE FUNCTION get_user_id_by_patreon(
    p_patreon_user_id TEXT
)
RETURNS UUID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_user_id UUID;
BEGIN
    SELECT id INTO v_user_id
    FROM profiles
    WHERE patreon_user_id = p_patreon_user_id;

    RETURN v_user_id;  -- NULL if not found
END;
$$;

REVOKE ALL ON FUNCTION get_user_id_by_patreon(TEXT) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION get_user_id_by_patreon(TEXT) TO service_role;
