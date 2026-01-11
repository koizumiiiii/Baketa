-- ============================================================
-- Issue #261: 同意記録テーブル
-- GDPR/CCPA監査ログ用
-- ============================================================

CREATE TABLE IF NOT EXISTS consent_records (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,

    -- 同意の種類: 'privacy_policy', 'terms_of_service'
    consent_type VARCHAR(50) NOT NULL,

    -- 同意状況: 'granted' (同意), 'revoked' (撤回)
    status VARCHAR(20) NOT NULL DEFAULT 'granted',

    -- 同意したドキュメントのバージョン (例: '2026-01')
    version VARCHAR(20) NOT NULL,

    -- 記録日時 (UTC)
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- クライアントバージョン (監査用)
    client_version VARCHAR(20),

    -- その他の監査情報 (User-Agent, IPなど)
    metadata JSONB DEFAULT '{}'::jsonb,

    -- 制約
    CONSTRAINT valid_consent_type CHECK (consent_type IN ('privacy_policy', 'terms_of_service')),
    CONSTRAINT valid_status CHECK (status IN ('granted', 'revoked'))
);

-- 最新の同意状況を高速に取得するためのインデックス
CREATE INDEX IF NOT EXISTS idx_consent_lookup
    ON consent_records (user_id, consent_type, recorded_at DESC);

-- RLS (Row Level Security) 有効化
ALTER TABLE consent_records ENABLE ROW LEVEL SECURITY;

-- ユーザーは自分の同意記録のみ参照可能
CREATE POLICY "Users can view own consent records"
    ON consent_records FOR SELECT
    USING (auth.uid() = user_id);

-- ユーザーは自分の同意記録を作成可能
CREATE POLICY "Users can insert own consent records"
    ON consent_records FOR INSERT
    WITH CHECK (auth.uid() = user_id);

-- 同意記録の更新・削除は禁止（監査ログの完全性保持）
-- UPDATE/DELETE ポリシーは作成しない

-- ============================================================
-- 最新の同意状況を取得するビュー（便利関数）
-- ============================================================
CREATE OR REPLACE VIEW latest_consent_status AS
SELECT DISTINCT ON (user_id, consent_type)
    user_id,
    consent_type,
    status,
    version,
    recorded_at
FROM consent_records
ORDER BY user_id, consent_type, recorded_at DESC;

-- ============================================================
-- Issue #277: 同意状態取得RPC関数
-- ============================================================
-- ユーザーの現在の同意状態を取得
-- ローカルファイル依存からの脱却
--
-- 使用例:
-- SELECT * FROM get_consent_status();
-- ============================================================
CREATE OR REPLACE FUNCTION get_consent_status()
RETURNS TABLE (
    consent_type VARCHAR(50),
    status VARCHAR(20),
    version VARCHAR(20),
    recorded_at TIMESTAMPTZ
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_user_id UUID;
BEGIN
    -- 現在のユーザーIDを取得
    v_user_id := auth.uid();

    IF v_user_id IS NULL THEN
        -- 未認証ユーザー: 空の結果を返す
        RETURN;
    END IF;

    -- latest_consent_statusビューから取得
    RETURN QUERY
    SELECT
        lcs.consent_type,
        lcs.status,
        lcs.version,
        lcs.recorded_at
    FROM latest_consent_status lcs
    WHERE lcs.user_id = v_user_id;
END;
$$;

-- RPC関数の実行権限をauthenticated roleに付与
GRANT EXECUTE ON FUNCTION get_consent_status() TO authenticated;

-- ============================================================
-- Issue #277: サービスロール用の同意状態取得RPC関数
-- ============================================================
-- Relay Server（サービスロール）からユーザーIDを指定して呼び出す
-- auth.uid()を使わず、明示的にuser_idを受け取る
--
-- 使用例（Relay Server経由）:
-- SELECT * FROM get_consent_status_for_user('user-uuid-here');
-- ============================================================
CREATE OR REPLACE FUNCTION get_consent_status_for_user(p_user_id UUID)
RETURNS TABLE (
    consent_type VARCHAR(50),
    status VARCHAR(20),
    version VARCHAR(20),
    recorded_at TIMESTAMPTZ
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
    -- 入力検証
    IF p_user_id IS NULL THEN
        RAISE EXCEPTION 'user_id is required';
    END IF;

    -- latest_consent_statusビューから取得
    RETURN QUERY
    SELECT
        lcs.consent_type,
        lcs.status,
        lcs.version,
        lcs.recorded_at
    FROM latest_consent_status lcs
    WHERE lcs.user_id = p_user_id;
END;
$$;

-- サービスロール（Relay Server）のみ実行可能
-- authenticated roleには付与しない（直接呼び出し防止）
REVOKE ALL ON FUNCTION get_consent_status_for_user(UUID) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION get_consent_status_for_user(UUID) TO service_role;
