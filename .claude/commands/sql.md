---
description: Supabase SQL操作（プロモーションコード発行、データ分析）
---

# Supabase SQL操作

## プロモーションコード発行

### 新規コード生成
```sql
-- 単一コード発行
INSERT INTO promotion_codes (code, plan_type, duration_days, max_uses, created_by)
VALUES (
  'BAKETA-' || upper(substr(md5(random()::text), 1, 4)) || '-' || upper(substr(md5(random()::text), 1, 4)),
  2,  -- 1=Pro, 2=Premium, 3=Ultimate
  30, -- 有効日数
  1,  -- 最大使用回数
  'admin'
);

-- 複数コード一括発行（10個）
INSERT INTO promotion_codes (code, plan_type, duration_days, max_uses, created_by)
SELECT
  'BAKETA-' || upper(substr(md5(random()::text || i::text), 1, 4)) || '-' || upper(substr(md5(random()::text || i::text), 5, 4)),
  2,
  30,
  1,
  'campaign_202501'
FROM generate_series(1, 10) AS i;
```

### コード状態確認
```sql
-- 未使用コード一覧
SELECT code, plan_type, duration_days, created_at
FROM promotion_codes
WHERE used_count < max_uses
  AND (expires_at IS NULL OR expires_at > NOW())
ORDER BY created_at DESC;

-- 使用済みコード確認
SELECT pc.code, pc.plan_type, pcr.user_id, pcr.redeemed_at
FROM promotion_codes pc
JOIN promotion_code_redemptions pcr ON pc.id = pcr.promotion_code_id
ORDER BY pcr.redeemed_at DESC
LIMIT 20;
```

## データ分析

### ユーザー統計
```sql
-- プラン別ユーザー数
SELECT
  plan_type,
  COUNT(*) as user_count
FROM user_licenses
WHERE status = 'active'
GROUP BY plan_type;

-- 月間アクティブユーザー
SELECT
  DATE_TRUNC('month', last_active_at) as month,
  COUNT(DISTINCT user_id) as mau
FROM user_activity
WHERE last_active_at >= NOW() - INTERVAL '6 months'
GROUP BY DATE_TRUNC('month', last_active_at)
ORDER BY month DESC;
```

### トークン使用量分析
```sql
-- ユーザー別月間トークン使用量
SELECT
  user_id,
  SUM(tokens_used) as total_tokens,
  COUNT(*) as request_count
FROM token_usage
WHERE created_at >= DATE_TRUNC('month', NOW())
GROUP BY user_id
ORDER BY total_tokens DESC
LIMIT 20;

-- 日別トークン消費推移
SELECT
  DATE(created_at) as date,
  SUM(tokens_used) as daily_tokens,
  COUNT(DISTINCT user_id) as unique_users
FROM token_usage
WHERE created_at >= NOW() - INTERVAL '30 days'
GROUP BY DATE(created_at)
ORDER BY date DESC;
```

### プロモーション効果分析
```sql
-- キャンペーン別利用状況
SELECT
  created_by as campaign,
  COUNT(*) as total_codes,
  SUM(used_count) as total_uses,
  ROUND(100.0 * SUM(used_count) / COUNT(*), 1) as usage_rate
FROM promotion_codes
WHERE created_by LIKE 'campaign_%'
GROUP BY created_by
ORDER BY created_at DESC;
```

## 注意事項
- 本番DBへの直接INSERT/UPDATE/DELETEは慎重に
- 大量データ取得時はLIMITを付ける
- 機密情報（メールアドレス等）はマスキングして共有
