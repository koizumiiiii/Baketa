-- ============================================
-- Issue #296: トークン使用量追跡テーブル
-- ============================================

-- token_usage テーブル
-- 月別のトークン使用量を記録
CREATE TABLE IF NOT EXISTS token_usage (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    year_month TEXT NOT NULL,  -- 'YYYY-MM' 形式
    tokens_used BIGINT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),

    -- ユニーク制約: ユーザーごとに月1レコード
    CONSTRAINT token_usage_user_month_unique UNIQUE (user_id, year_month)
);

-- インデックス
CREATE INDEX IF NOT EXISTS idx_token_usage_user_id ON token_usage(user_id);
CREATE INDEX IF NOT EXISTS idx_token_usage_year_month ON token_usage(year_month);

-- RLS (Row Level Security) 有効化
ALTER TABLE token_usage ENABLE ROW LEVEL SECURITY;

-- RLSポリシー: ユーザーは自分のデータのみ参照可能
CREATE POLICY "Users can view own token usage"
    ON token_usage FOR SELECT
    USING (auth.uid() = user_id);

-- サービスロールは全データにアクセス可能（Relay Server用）
CREATE POLICY "Service role has full access"
    ON token_usage FOR ALL
    USING (auth.role() = 'service_role');

-- ============================================
-- RPC関数: record_token_consumption
-- Supabaseユーザー（UUID）用のトークン消費記録
-- ============================================

-- 重複関数を削除（BIGINT版とINTEGER版の両方）
DROP FUNCTION IF EXISTS record_token_consumption(UUID, BIGINT);
DROP FUNCTION IF EXISTS record_token_consumption(UUID, INTEGER);

CREATE OR REPLACE FUNCTION record_token_consumption(
    p_user_id UUID,
    p_tokens INTEGER
)
RETURNS TABLE(out_year_month TEXT, out_tokens_used BIGINT)
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_year_month TEXT;
    v_tokens_used BIGINT;
BEGIN
    -- 現在の年月を取得
    v_year_month := to_char(now(), 'YYYY-MM');

    -- UPSERT: 存在すれば加算、なければ新規作成
    INSERT INTO token_usage AS tu (user_id, year_month, tokens_used, updated_at)
    VALUES (p_user_id, v_year_month, p_tokens, now())
    ON CONFLICT (user_id, year_month)
    DO UPDATE SET
        tokens_used = tu.tokens_used + p_tokens,
        updated_at = now()
    RETURNING tu.year_month, tu.tokens_used
    INTO v_year_month, v_tokens_used;

    RETURN QUERY SELECT v_year_month, v_tokens_used;
END;
$$;

-- ============================================
-- RPC関数: record_token_consumption_by_patreon
-- Patreonユーザー（patreon_user_id）用のトークン消費記録
-- ============================================

-- 重複関数を削除（BIGINT版とINTEGER版の両方）
DROP FUNCTION IF EXISTS record_token_consumption_by_patreon(TEXT, BIGINT);
DROP FUNCTION IF EXISTS record_token_consumption_by_patreon(TEXT, INTEGER);

CREATE OR REPLACE FUNCTION record_token_consumption_by_patreon(
    p_patreon_user_id TEXT,
    p_tokens INTEGER
)
RETURNS TABLE(out_year_month TEXT, out_tokens_used BIGINT)
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id UUID;
    v_year_month TEXT;
    v_tokens_used BIGINT;
BEGIN
    -- profiles テーブルから user_id を取得
    SELECT p.id INTO v_user_id
    FROM profiles p
    WHERE p.patreon_user_id = p_patreon_user_id;

    -- ユーザーが見つからない場合はNULLを返す
    IF v_user_id IS NULL THEN
        RETURN;
    END IF;

    -- 現在の年月を取得
    v_year_month := to_char(now(), 'YYYY-MM');

    -- UPSERT: 存在すれば加算、なければ新規作成
    INSERT INTO token_usage AS tu (user_id, year_month, tokens_used, updated_at)
    VALUES (v_user_id, v_year_month, p_tokens, now())
    ON CONFLICT (user_id, year_month)
    DO UPDATE SET
        tokens_used = tu.tokens_used + p_tokens,
        updated_at = now()
    RETURNING tu.year_month, tu.tokens_used
    INTO v_year_month, v_tokens_used;

    RETURN QUERY SELECT v_year_month, v_tokens_used;
END;
$$;

-- ============================================
-- 権限設定
-- ============================================
-- サービスロールにRPC実行権限を付与
GRANT EXECUTE ON FUNCTION record_token_consumption(UUID, INTEGER) TO service_role;
GRANT EXECUTE ON FUNCTION record_token_consumption_by_patreon(TEXT, INTEGER) TO service_role;
