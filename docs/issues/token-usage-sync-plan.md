# プロモーションユーザーのトークン消費量サーバー同期

## 背景

Freeプラン + プロモーションコード適用ユーザーがPC買い替え時に、トークン消費量がリセットされる問題。

### 現状の問題

1. **PromotionSettings.MockTokenUsage**: ローカルのみ保存
2. **TokenUsageRepository**: ローカルのみ保存（月間詳細記録）
3. PC移行時にこれらのデータが失われ、消費量が0にリセット

## 提案: ボーナストークンモデルによる同期

> **Note**: Issue #281「プロモーションコードシステムのUX改善」と統合実装

### 設計変更の経緯

当初は `promotion_code_redemptions.tokens_used` に総消費量を保存する設計だったが、
Issue #281 でボーナストークンモデルを導入することになり、以下の理由で設計を変更:

- **複数プロモ対応**: 各ボーナスを個別に管理する必要がある
- **有効期限管理**: ボーナスごとに異なる有効期限を持つ
- **消費順序制御**: 期限が近いボーナスから消費する

### データベース設計

```sql
-- ============================================================
-- Issue #280 + #281: ボーナストークンテーブル
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
CREATE INDEX idx_bonus_tokens_user_expires
ON bonus_tokens(user_id, expires_at ASC);

-- インデックス: 有効なボーナスのみ取得
CREATE INDEX idx_bonus_tokens_active
ON bonus_tokens(user_id, expires_at)
WHERE used_tokens < granted_tokens;

-- RLS有効化
ALTER TABLE bonus_tokens ENABLE ROW LEVEL SECURITY;

-- ユーザーは自分のボーナスのみ参照可能
CREATE POLICY "Users can view own bonus tokens"
    ON bonus_tokens FOR SELECT
    USING (auth.uid() = user_id);

-- ============================================================
-- RPC関数: ボーナストークン状態取得
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

-- ============================================================
-- RPC関数: ボーナストークン同期（複数ボーナス対応）
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

-- ============================================================
-- RPC関数: サービスロール用（Relay Server経由）
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

-- 権限設定
GRANT EXECUTE ON FUNCTION get_bonus_tokens() TO authenticated;
GRANT EXECUTE ON FUNCTION sync_bonus_tokens(JSONB) TO authenticated;

REVOKE ALL ON FUNCTION get_bonus_tokens_for_user(UUID) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION get_bonus_tokens_for_user(UUID) TO service_role;

REVOKE ALL ON FUNCTION sync_bonus_tokens_for_user(UUID, JSONB) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sync_bonus_tokens_for_user(UUID, JSONB) TO service_role;
```

### Relay Server変更

```typescript
// GET /api/bonus-tokens/status - ボーナストークン状態取得
app.get('/api/bonus-tokens/status', authMiddleware, async (c) => {
  const user = c.get('user');

  const { data, error } = await supabase.rpc('get_bonus_tokens_for_user', {
    p_user_id: user.id
  });

  if (error) {
    return c.json({ error: error.message }, 500);
  }

  return c.json({
    bonuses: data,
    total_remaining: data.reduce((sum, b) => sum + b.remaining_tokens, 0)
  });
});

// POST /api/bonus-tokens/sync - ボーナストークン同期
app.post('/api/bonus-tokens/sync', authMiddleware, async (c) => {
  const { bonuses } = await c.req.json();
  const user = c.get('user');

  // 入力検証
  if (!Array.isArray(bonuses)) {
    return c.json({ error: 'bonuses must be an array' }, 400);
  }

  const { data, error } = await supabase.rpc('sync_bonus_tokens_for_user', {
    p_user_id: user.id,
    p_bonuses: bonuses
  });

  if (error) {
    return c.json({ error: error.message }, 500);
  }

  return c.json({ synced_bonuses: data });
});
```

### クライアント側変更

#### 1. インターフェース定義

```csharp
// Baketa.Core/Abstractions/License/IBonusTokenService.cs
public interface IBonusTokenService
{
    /// <summary>ローカルのボーナストークン一覧を取得</summary>
    IReadOnlyList<BonusToken> GetBonusTokens();

    /// <summary>サーバーからボーナストークンを同期</summary>
    Task<SyncResult> SyncFromServerAsync(string accessToken, CancellationToken ct = default);

    /// <summary>ローカルの消費量をサーバーに同期</summary>
    Task<SyncResult> SyncToServerAsync(string accessToken, CancellationToken ct = default);

    /// <summary>トークンを消費（有効期限が近い順）</summary>
    Task<ConsumeResult> ConsumeTokensAsync(long amount, CancellationToken ct = default);

    /// <summary>残りトークン合計</summary>
    long TotalRemainingTokens { get; }
}

public record BonusToken
{
    public required Guid Id { get; init; }
    public required string SourceType { get; init; }
    public required long GrantedTokens { get; init; }
    public required long UsedTokens { get; init; }
    public long RemainingTokens => GrantedTokens - UsedTokens;
    public required DateTime ExpiresAt { get; init; }
    public bool IsExpired => ExpiresAt < DateTime.UtcNow;
    public bool IsValid => !IsExpired && RemainingTokens > 0;
}
```

#### 2. 消費ロジック

```csharp
// 有効期限が近い順に消費
public async Task<ConsumeResult> ConsumeTokensAsync(long amount, CancellationToken ct)
{
    var remaining = amount;
    var consumed = new List<(Guid BonusId, long Amount)>();

    // 有効期限が近い順にソート
    var validBonuses = _bonusTokens
        .Where(b => b.IsValid)
        .OrderBy(b => b.ExpiresAt)
        .ToList();

    foreach (var bonus in validBonuses)
    {
        if (remaining <= 0) break;

        var toConsume = Math.Min(remaining, bonus.RemainingTokens);
        bonus.UsedTokens += toConsume;
        remaining -= toConsume;
        consumed.Add((bonus.Id, toConsume));
    }

    // ボーナスで足りない場合はプラン枠から消費
    if (remaining > 0)
    {
        await _licenseManager.ConsumeFromPlanQuotaAsync(remaining, ct);
    }

    // 非同期で同期キューに追加（デバウンス付き）
    _syncQueue.Enqueue(consumed);

    return new ConsumeResult { Success = true, ConsumedFromBonus = amount - remaining };
}
```

#### 3. 同期タイミング

- **アプリ起動時**: サーバーから最新状態を取得
- **トークン消費時**: 動的デバウンス付きでサーバーに同期
  ```csharp
  // 上限接近時は頻繁に同期
  var debounceInterval = TotalRemainingTokens < (totalLimit * 0.1)
      ? TimeSpan.FromMinutes(1)  // 残り10%未満は1分
      : TimeSpan.FromMinutes(5); // 通常は5分
  ```
- **アプリ終了時**: タイムアウト付きベストエフォート（5秒）

#### 4. 競合解決

- **CRDT G-Counterパターン**: 各ボーナスの `used_tokens` で大きい方を採用
- トークン消費は単調増加のため、この方式が最適
- 複数PC同時使用時も正しく動作

#### 5. ローカル永続化

```csharp
// BonusTokenSettings.cs
public class BonusTokenSettings
{
    public List<LocalBonusToken> Bonuses { get; set; } = new();
    public bool HasPendingSync { get; set; }
    public DateTime LastSyncedAt { get; set; }
}

public class LocalBonusToken
{
    public Guid Id { get; set; }
    public string SourceType { get; set; }
    public long GrantedTokens { get; set; }
    public long UsedTokens { get; set; }
    public DateTime ExpiresAt { get; set; }
    public long LastSyncedUsedTokens { get; set; }  // 差分計算用
}
```

### UI表示

```
トークン使用状況
├── プラン枠: 350,000 / 500,000
├── ボーナス: + 150,000
└── 詳細:
    ├── プロモA: 50,000 (1/31まで)
    └── プロモB: 100,000 (2/28まで)
```

## 実装フェーズ

### Phase 1: DB & Relay Server
- [ ] `bonus_tokens` テーブル作成
- [ ] RPC関数作成（`get_bonus_tokens`, `sync_bonus_tokens` + `_for_user` 版）
- [ ] RLS ポリシー設定
- [ ] Relay Server エンドポイント追加（`/api/bonus-tokens/status`, `/api/bonus-tokens/sync`）

### Phase 2: クライアント実装
- [ ] `IBonusTokenService` インターフェース定義
- [ ] `BonusTokenService` 実装
- [ ] `BonusTokenSettings` ローカル永続化
- [ ] `LicenseManager` 統合（消費ロジック変更）
- [ ] 起動時/終了時の同期処理

### Phase 3: プロモーション適用時のボーナス作成
- [ ] `PromotionCodeService.ApplyCodeAsync` でボーナス作成
- [ ] Patreon購入時のボーナス変換ロジック

### Phase 4: UI実装
- [ ] ライセンス情報画面にボーナストークン表示追加
- [ ] トークン内訳の詳細表示

### Phase 5: テスト
- [ ] 新規PCでのログイン後、ボーナス状態が復元されることを確認
- [ ] 複数PC同時使用時の競合解決テスト
- [ ] 有効期限切れボーナスの処理確認
- [ ] 消費順序（期限が近い順）の確認

## Gemini Review結果サマリー

### ✅ 評価良好
- **競合解決ポリシー**: CRDT G-Counterパターンは各ボーナスに適用可能
- **複数ボーナス対応**: 将来の拡張性を確保

### 🔴 実装上の注意点
- トランザクション管理の徹底（データ整合性）
- UIでの透明性確保（内訳と期限を明確表示）
- 有効期限が近い順の消費順序

### 🆕 追加で検討すべきエッジケース
- プランのダウングレード時のボーナス扱い
- 月末プロモ適用 → 翌日月次リセット
- 同日複数回プラン変更

## 関連

- Issue #281: プロモーションコードシステムのUX改善（ボーナストークンモデル導入）
- Issue #276: プロモーション状態のDB同期
- Issue #277: 同意設定のDB同期
