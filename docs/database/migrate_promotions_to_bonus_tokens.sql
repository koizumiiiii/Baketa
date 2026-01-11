-- ============================================================
-- Issue #280+#281 Phase 4: 既存プロモーション → ボーナストークン移行
-- ============================================================
--
-- 実行前の確認事項:
-- 1. bonus_tokens テーブルが作成済みであること
-- 2. DBバックアップを取得済みであること
-- 3. 本番環境では段階的に実行（LIMIT句を使用）
--
-- 移行方針: 満額付与
-- - 既存のプロモーション適用ユーザーに対し、plan_type相当のトークンを付与
-- - 有効期限は redeemed_at + duration_days で計算
-- - 既にbonus_tokensに同一source_idが存在する場合はスキップ
-- ============================================================

-- ============================================================
-- 1. 移行対象の確認（DRY RUN）
-- ============================================================
-- まずはどのユーザーが対象かを確認
SELECT
    r.id AS redemption_id,
    r.user_id,
    r.redeemed_at,
    pc.code,
    pc.plan_type,
    pc.duration_days,
    (r.redeemed_at + (pc.duration_days || ' days')::INTERVAL) AS calculated_expires_at,
    CASE pc.plan_type
        WHEN 1 THEN 10000000   -- Pro: 1000万
        WHEN 2 THEN 20000000   -- Premium: 2000万
        WHEN 3 THEN 50000000   -- Ultimate: 5000万
        ELSE 0
    END AS token_amount,
    CASE
        WHEN (r.redeemed_at + (pc.duration_days || ' days')::INTERVAL) > NOW()
        THEN 'ACTIVE'
        ELSE 'EXPIRED'
    END AS status
FROM promotion_code_redemptions r
JOIN promotion_codes pc ON r.promotion_code_id = pc.id
WHERE r.status = 'success'
  AND r.user_id IS NOT NULL
  AND pc.plan_type IN (1, 2, 3)  -- Free(0)は対象外
ORDER BY r.redeemed_at DESC;

-- ============================================================
-- 2. 既存の移行済みレコード確認
-- ============================================================
-- 既にbonus_tokensに移行済みのsource_idを確認
SELECT
    bt.id,
    bt.user_id,
    bt.source_type,
    bt.source_id,
    bt.granted_tokens,
    bt.expires_at
FROM bonus_tokens bt
WHERE bt.source_type = 'promotion'
ORDER BY bt.created_at DESC;

-- ============================================================
-- 3. 移行実行（本番用）
-- ============================================================
-- 注意: 実行前にDBバックアップを取得すること！

-- トランザクション開始
BEGIN;

-- 移行処理
INSERT INTO bonus_tokens (
    user_id,
    source_type,
    source_id,
    granted_tokens,
    used_tokens,
    expires_at,
    created_at,
    updated_at
)
SELECT
    r.user_id,
    'promotion' AS source_type,
    r.id AS source_id,
    CASE pc.plan_type
        WHEN 1 THEN 10000000   -- Pro: 1000万
        WHEN 2 THEN 20000000   -- Premium: 2000万
        WHEN 3 THEN 50000000   -- Ultimate: 5000万
    END AS granted_tokens,
    0 AS used_tokens,  -- 新規付与なので0
    (r.redeemed_at + (pc.duration_days || ' days')::INTERVAL) AS expires_at,
    NOW() AS created_at,
    NOW() AS updated_at
FROM promotion_code_redemptions r
JOIN promotion_codes pc ON r.promotion_code_id = pc.id
WHERE r.status = 'success'
  AND r.user_id IS NOT NULL
  AND pc.plan_type IN (1, 2, 3)
  -- 既に移行済みのものはスキップ
  AND NOT EXISTS (
      SELECT 1 FROM bonus_tokens bt
      WHERE bt.source_type = 'promotion'
        AND bt.source_id = r.id
  )
  -- アクティブなもののみ（オプション: 期限切れも含める場合はこの条件を削除）
  AND (r.redeemed_at + (pc.duration_days || ' days')::INTERVAL) > NOW();

-- 移行結果確認
SELECT
    'Migration Summary' AS info,
    COUNT(*) AS total_migrated,
    SUM(granted_tokens) AS total_tokens_granted
FROM bonus_tokens
WHERE source_type = 'promotion'
  AND created_at > NOW() - INTERVAL '1 minute';

-- 問題なければコミット
COMMIT;

-- 問題があればロールバック
-- ROLLBACK;

-- ============================================================
-- 4. 移行後の確認
-- ============================================================
-- ユーザーごとのボーナストークン状況
SELECT
    u.email,
    bt.granted_tokens,
    bt.used_tokens,
    (bt.granted_tokens - bt.used_tokens) AS remaining_tokens,
    bt.expires_at,
    CASE WHEN bt.expires_at > NOW() THEN 'ACTIVE' ELSE 'EXPIRED' END AS status
FROM bonus_tokens bt
JOIN auth.users u ON bt.user_id = u.id
WHERE bt.source_type = 'promotion'
ORDER BY bt.expires_at DESC;

-- ============================================================
-- 5. ロールバック用（緊急時のみ）
-- ============================================================
-- 移行したレコードを削除
-- DELETE FROM bonus_tokens
-- WHERE source_type = 'promotion'
--   AND created_at > '2025-01-11 00:00:00+00';  -- 移行日時を指定
