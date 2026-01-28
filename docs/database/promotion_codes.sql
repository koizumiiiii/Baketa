-- ===========================================
-- Baketa プロモーションコードシステム
-- Supabase テーブル・関数定義
-- ===========================================
--
-- [Issue #280+#281] 変更点:
-- プロモーションコード適用は「プラン昇格」から「ボーナストークン付与」に変更されました。
-- - promotion_codes.expires_at: コード入力の有効期限（変更なし）
-- - duration_days: レガシー（ボーナストークンは永続のため使用されない）
-- - get_promotion_status(): レガシー関数（プラン昇格の有効期限確認用）
--
-- [Issue #347] ボーナストークンの有効期限は削除されました。
-- ボーナストークンは永続的に利用可能です。
--
-- 使用方法:
-- 1. Supabase Console → SQL Editor でこのSQLを実行
-- 2. wrangler secret で SUPABASE_URL と SUPABASE_SERVICE_KEY を設定
-- 3. Relay Server をデプロイ
--
-- 関連ドキュメント:
-- - docs/3-architecture/auth/promotion-code-system.md
-- ===========================================

-- ===========================================
-- 1. プロモーションコードテーブル
-- ===========================================
CREATE TABLE IF NOT EXISTS promotion_codes (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  code TEXT UNIQUE NOT NULL,                    -- "BAKETA-XXXXXXXX" (英数字のみ、+=/等の特殊文字不可)
  plan_type INT NOT NULL,                       -- 0=Free, 1=Pro, 2=Premium, 3=Ultimate (Issue #257)
  expires_at TIMESTAMPTZ NOT NULL,              -- コード自体の有効期限
  duration_days INT NOT NULL DEFAULT 30,        -- 適用後のプラン有効期間（日数）
  usage_type TEXT NOT NULL DEFAULT 'single_use', -- single_use, multi_use, limited
  max_uses INT DEFAULT 1,                       -- 最大使用回数（single_use=1）
  current_uses INT DEFAULT 0,                   -- 現在の使用回数
  created_at TIMESTAMPTZ DEFAULT NOW(),
  is_active BOOLEAN DEFAULT true,               -- 無効化フラグ
  description TEXT                              -- 管理用メモ
);

-- コード検索用インデックス
CREATE INDEX IF NOT EXISTS idx_promotion_codes_code
  ON promotion_codes(code);

-- アクティブコード部分インデックス
CREATE INDEX IF NOT EXISTS idx_promotion_codes_active
  ON promotion_codes(is_active)
  WHERE is_active = true;

-- RLS有効化
ALTER TABLE promotion_codes ENABLE ROW LEVEL SECURITY;

-- Service roleのみがアクセス可能
DROP POLICY IF EXISTS "Service role can manage promotion_codes" ON promotion_codes;
CREATE POLICY "Service role can manage promotion_codes" ON promotion_codes
  FOR ALL TO service_role USING (true);

-- ===========================================
-- 2. 監査ログテーブル（利用履歴追跡）
-- ===========================================
CREATE TABLE IF NOT EXISTS promotion_code_redemptions (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  promotion_code_id UUID REFERENCES promotion_codes(id),  -- NULLable for not_found cases
  user_id UUID REFERENCES auth.users(id) ON DELETE SET NULL,  -- Supabase Auth ユーザーID（未ログインはNULL）
  device_id TEXT,                               -- デバイス識別子（将来用）
  redeemed_at TIMESTAMPTZ DEFAULT NOW(),
  status TEXT NOT NULL,                         -- 'success', 'failed_not_found', 'failed_expired', 'failed_limit'
  error_message TEXT,
  client_ip TEXT
);

-- コードID検索用インデックス
CREATE INDEX IF NOT EXISTS idx_redemptions_code
  ON promotion_code_redemptions(promotion_code_id);

-- ユーザーID検索用インデックス
CREATE INDEX IF NOT EXISTS idx_redemptions_user
  ON promotion_code_redemptions(user_id);

-- 時系列検索用インデックス
CREATE INDEX IF NOT EXISTS idx_redemptions_time
  ON promotion_code_redemptions(redeemed_at);

-- RLS有効化
ALTER TABLE promotion_code_redemptions ENABLE ROW LEVEL SECURITY;

-- Service roleのみがアクセス可能
DROP POLICY IF EXISTS "Service role can manage redemptions" ON promotion_code_redemptions;
CREATE POLICY "Service role can manage redemptions" ON promotion_code_redemptions
  FOR ALL TO service_role USING (true);

-- ===========================================
-- 3. アトミック処理用DB関数（レースコンディション対策）
-- ===========================================
--
-- この関数は以下を1つのトランザクションで実行:
-- 1. 行ロック（FOR UPDATE）で競合防止
-- 2. 有効性チェック（存在、有効期限、使用回数）
-- 3. 使用回数インクリメント
-- 4. 監査ログ記録
--
-- 使用例:
-- SELECT * FROM redeem_promotion_code('BAKETA-XXXXXXXX', '192.168.1.1');
-- ===========================================
CREATE OR REPLACE FUNCTION redeem_promotion_code(
  code_to_redeem TEXT,
  client_ip_address TEXT DEFAULT NULL,
  redeeming_user_id UUID DEFAULT NULL  -- JWT検証済みのユーザーID（未ログインはNULL）
)
RETURNS json
LANGUAGE plpgsql
SECURITY DEFINER  -- service_role権限で実行
AS $$
DECLARE
  rec RECORD;
  redemption_id UUID;
  result_expires_at TIMESTAMPTZ;
BEGIN
  -- 行をロックして競合を防ぎつつ、コードの有効性をチェック
  SELECT id, plan_type, duration_days, usage_type, max_uses, current_uses, expires_at
  INTO rec
  FROM promotion_codes
  WHERE code = code_to_redeem AND is_active = true
  FOR UPDATE;

  -- レコードが見つからない場合
  IF rec IS NULL THEN
    INSERT INTO promotion_code_redemptions (promotion_code_id, user_id, status, error_message, client_ip)
    VALUES (NULL, redeeming_user_id, 'failed_not_found', 'Code not found or inactive', client_ip_address);
    RETURN json_build_object(
      'success', false,
      'error_code', 'CODE_NOT_FOUND',
      'message', '無効なプロモーションコードです'
    );
  END IF;

  -- 有効期限チェック
  IF rec.expires_at < NOW() THEN
    INSERT INTO promotion_code_redemptions (promotion_code_id, user_id, status, error_message, client_ip)
    VALUES (rec.id, redeeming_user_id, 'failed_expired', 'Code expired', client_ip_address);
    RETURN json_build_object(
      'success', false,
      'error_code', 'CODE_EXPIRED',
      'message', 'このコードは有効期限が切れています'
    );
  END IF;

  -- 使用回数上限チェック
  IF rec.current_uses >= rec.max_uses THEN
    INSERT INTO promotion_code_redemptions (promotion_code_id, user_id, status, error_message, client_ip)
    VALUES (rec.id, redeeming_user_id, 'failed_limit', 'Usage limit reached', client_ip_address);
    RETURN json_build_object(
      'success', false,
      'error_code', 'CODE_ALREADY_REDEEMED',
      'message', 'このコードは既に使用されています'
    );
  END IF;

  -- 使用回数をインクリメント
  UPDATE promotion_codes
  SET current_uses = current_uses + 1
  WHERE id = rec.id;

  -- 監査ログに記録（user_id含む）
  INSERT INTO promotion_code_redemptions (promotion_code_id, user_id, status, client_ip)
  VALUES (rec.id, redeeming_user_id, 'success', client_ip_address)
  RETURNING id INTO redemption_id;

  -- 適用後の有効期限を計算（現在時刻 + duration_days）
  result_expires_at := NOW() + (rec.duration_days || ' days')::INTERVAL;

  RETURN json_build_object(
    'success', true,
    'plan_type', rec.plan_type,
    'duration_days', rec.duration_days,
    'expires_at', result_expires_at,
    'redemption_id', redemption_id,
    'user_id', redeeming_user_id  -- 適用したユーザーID（確認用）
  );
EXCEPTION
  WHEN OTHERS THEN
    -- 予期せぬエラーをログに記録
    RAISE LOG 'redeem_promotion_code error: % %', SQLERRM, SQLSTATE;
    RETURN json_build_object(
      'success', false,
      'error_code', 'SERVER_ERROR',
      'message', '予期せぬエラーが発生しました'
    );
END;
$$;

-- ===========================================
-- 4. 管理用クエリ例
-- ===========================================

-- ■ コード形式ルール
-- - プレフィックス: "BAKETA-"
-- - サフィックス: 英数字のみ (A-Z, 0-9)
-- - 禁止文字: +, =, /, その他特殊文字（Base64由来の文字は使用不可）
-- - 例: BAKETA-BETA2025, BAKETA-A1B2C3D4, BAKETA-ULT10YRS

-- ■ コード発行例（固定コード）
-- INSERT INTO promotion_codes (code, plan_type, expires_at, duration_days, usage_type, max_uses, description)
-- VALUES
--   ('BAKETA-BETA2025', 2, '2025-12-31 23:59:59+00', 30, 'multi_use', 1000, 'ベータテスター向け'),
--   ('BAKETA-PREMVIP1', 3, '2025-06-30 23:59:59+00', 90, 'single_use', 1, 'VIPユーザー向け');

-- ■ コード発行例（ランダム生成 - 英数字のみ）
-- INSERT INTO promotion_codes (code, plan_type, expires_at, duration_days, usage_type, max_uses, description)
-- VALUES (
--   'BAKETA-' || upper(substr(md5(random()::text), 1, 8)),  -- 8文字の英数字ランダムコード
--   3,
--   '2026-01-31 23:59:59+00',
--   30,
--   'single_use',
--   1,
--   'ランダム生成コード'
-- )
-- RETURNING code, plan_type, expires_at, duration_days;

-- 使用状況確認
-- SELECT
--   code,
--   CASE plan_type WHEN 0 THEN 'Free' WHEN 1 THEN 'Pro' WHEN 2 THEN 'Premium' WHEN 3 THEN 'Ultimate' END as plan,
--   current_uses || '/' || max_uses as usage,
--   expires_at,
--   is_active
-- FROM promotion_codes
-- ORDER BY created_at DESC;

-- 利用履歴確認
-- SELECT
--   r.redeemed_at,
--   p.code,
--   r.status,
--   r.client_ip
-- FROM promotion_code_redemptions r
-- LEFT JOIN promotion_codes p ON r.promotion_code_id = p.id
-- ORDER BY r.redeemed_at DESC
-- LIMIT 100;

-- コード無効化
-- UPDATE promotion_codes SET is_active = false WHERE code = 'BAKETA-XXXXXXXX';

-- ===========================================
-- 5. マイグレーション用SQL（既存テーブル更新）
-- ===========================================
-- 既存のpromotion_code_redemptionsテーブルにuser_id列を追加する場合:
--
-- ALTER TABLE promotion_code_redemptions
-- ADD COLUMN IF NOT EXISTS user_id UUID REFERENCES auth.users(id) ON DELETE SET NULL;
--
-- CREATE INDEX IF NOT EXISTS idx_redemptions_user
--   ON promotion_code_redemptions(user_id);

-- ===========================================
-- 6. ユーザー追跡付き利用履歴確認クエリ
-- ===========================================
-- SELECT
--   r.redeemed_at,
--   p.code,
--   r.status,
--   u.email as user_email,
--   r.user_id,
--   r.client_ip
-- FROM promotion_code_redemptions r
-- LEFT JOIN promotion_codes p ON r.promotion_code_id = p.id
-- LEFT JOIN auth.users u ON r.user_id = u.id
-- ORDER BY r.redeemed_at DESC
-- LIMIT 100;

-- ===========================================
-- 7. Issue #276: プロモーション状態取得RPC関数
-- ===========================================
-- [レガシー] ユーザーの現在のプロモーション状態を取得
-- 日付計算をDB側で行い、タイムゾーン問題を回避
--
-- ⚠️ 注意: Issue #280+#281 以降、プロモーションコードは「プラン昇格」ではなく
-- 「ボーナストークン付与」に変更されました。この関数は後方互換性のために残されています。
-- ボーナストークンの状態は get_bonus_tokens_for_user() を使用してください。
--
-- 使用例:
-- SELECT * FROM get_promotion_status();
-- ===========================================
CREATE OR REPLACE FUNCTION get_promotion_status()
RETURNS TABLE (
    has_promotion BOOLEAN,
    code_masked TEXT,
    plan_type INT,
    plan_name TEXT,
    applied_at TIMESTAMPTZ,
    expires_at TIMESTAMPTZ,
    is_expired BOOLEAN
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_user_id UUID;
    v_redemption RECORD;
    v_expires_at TIMESTAMPTZ;
BEGIN
    -- 現在のユーザーIDを取得
    v_user_id := auth.uid();

    IF v_user_id IS NULL THEN
        -- 未認証ユーザー
        RETURN QUERY SELECT
            FALSE::BOOLEAN,
            NULL::TEXT,
            NULL::INT,
            NULL::TEXT,
            NULL::TIMESTAMPTZ,
            NULL::TIMESTAMPTZ,
            NULL::BOOLEAN;
        RETURN;
    END IF;

    -- 最新の成功したredemptionを取得
    SELECT
        r.redeemed_at,
        pc.code,
        pc.plan_type,
        pc.duration_days
    INTO v_redemption
    FROM promotion_code_redemptions r
    JOIN promotion_codes pc ON r.promotion_code_id = pc.id
    WHERE r.user_id = v_user_id
      AND r.status = 'success'
    ORDER BY r.redeemed_at DESC
    LIMIT 1;

    -- プロモーションなし
    IF v_redemption IS NULL THEN
        RETURN QUERY SELECT
            FALSE::BOOLEAN,
            NULL::TEXT,
            NULL::INT,
            NULL::TEXT,
            NULL::TIMESTAMPTZ,
            NULL::TIMESTAMPTZ,
            NULL::BOOLEAN;
        RETURN;
    END IF;

    -- 有効期限をDB側で計算（日の終わり 23:59:59 UTC）
    v_expires_at := (v_redemption.redeemed_at::DATE + v_redemption.duration_days * INTERVAL '1 day')::TIMESTAMPTZ
                    + INTERVAL '23 hours 59 minutes 59 seconds';

    -- 結果を返す
    RETURN QUERY SELECT
        TRUE::BOOLEAN,
        LEFT(v_redemption.code, 10) || '****',  -- マスク処理
        v_redemption.plan_type,
        CASE v_redemption.plan_type
            WHEN 0 THEN 'Free'
            WHEN 1 THEN 'Pro'
            WHEN 2 THEN 'Premium'
            WHEN 3 THEN 'Ultimate'
            ELSE 'Unknown'
        END,
        v_redemption.redeemed_at,
        v_expires_at,
        (v_expires_at < NOW())::BOOLEAN;  -- 有効期限切れかどうか
END;
$$;

-- RPC関数の実行権限をauthenticated roleに付与
GRANT EXECUTE ON FUNCTION get_promotion_status() TO authenticated;

-- ============================================================
-- [レガシー] Issue #276: サービスロール用のプロモーション状態取得RPC関数
-- ============================================================
-- Relay Server（サービスロール）からユーザーIDを指定して呼び出す
-- auth.uid()を使わず、明示的にp_user_idを受け取る
--
-- ⚠️ 注意: Issue #280+#281 以降、プロモーションコードは「ボーナストークン付与」に
-- 変更されました。この関数は後方互換性のために残されています。
-- ボーナストークンの状態は get_bonus_tokens_for_user() を使用してください。
--
-- 使用例（Relay Server経由）:
-- SELECT * FROM get_promotion_status_for_user('user-uuid-here');
-- ============================================================
CREATE OR REPLACE FUNCTION get_promotion_status_for_user(p_user_id UUID)
RETURNS TABLE (
    has_promotion BOOLEAN,
    promotion_code TEXT,
    plan_type INT,
    plan_name TEXT,
    redeemed_at TIMESTAMPTZ,
    expires_at TIMESTAMPTZ,
    is_expired BOOLEAN
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_redemption RECORD;
    v_expires_at TIMESTAMPTZ;
BEGIN
    -- 入力検証
    IF p_user_id IS NULL THEN
        RAISE EXCEPTION 'user_id is required';
    END IF;

    -- 最新の成功したredemptionを取得
    SELECT
        r.redeemed_at,
        pc.code,
        pc.plan_type,
        pc.duration_days
    INTO v_redemption
    FROM promotion_code_redemptions r
    JOIN promotion_codes pc ON r.promotion_code_id = pc.id
    WHERE r.user_id = p_user_id
      AND r.status = 'success'
    ORDER BY r.redeemed_at DESC
    LIMIT 1;

    -- プロモーションなし
    IF v_redemption IS NULL THEN
        RETURN QUERY SELECT
            FALSE::BOOLEAN,
            NULL::TEXT,
            NULL::INT,
            NULL::TEXT,
            NULL::TIMESTAMPTZ,
            NULL::TIMESTAMPTZ,
            NULL::BOOLEAN;
        RETURN;
    END IF;

    -- 有効期限をDB側で計算（日の終わり 23:59:59 UTC）
    v_expires_at := (v_redemption.redeemed_at::DATE + v_redemption.duration_days * INTERVAL '1 day')::TIMESTAMPTZ
                    + INTERVAL '23 hours 59 minutes 59 seconds';

    -- 結果を返す
    RETURN QUERY SELECT
        TRUE::BOOLEAN,
        LEFT(v_redemption.code, 10) || '****',  -- マスク処理
        v_redemption.plan_type,
        CASE v_redemption.plan_type
            WHEN 0 THEN 'Free'
            WHEN 1 THEN 'Pro'
            WHEN 2 THEN 'Premium'
            WHEN 3 THEN 'Ultimate'
            ELSE 'Unknown'
        END,
        v_redemption.redeemed_at,
        v_expires_at,
        (v_expires_at < NOW())::BOOLEAN;  -- 有効期限切れかどうか
END;
$$;

-- サービスロール（Relay Server）のみ実行可能
REVOKE ALL ON FUNCTION get_promotion_status_for_user(UUID) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION get_promotion_status_for_user(UUID) TO service_role;
