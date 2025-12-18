# Issue #77: ライセンス管理システム基盤 - 要件定義書

## 概要

Baketaアプリケーションにおけるライセンス管理システムの基盤を構築する。
Free、Standard、Pro、Premiaの4プランを管理し、機能制限・クラウドAI翻訳のトークン管理を実現する。

---

## プラン構成

| プラン | 月額 | ローカル翻訳 | クラウドAI翻訳 | トークン上限 | 広告表示 |
|--------|------|-------------|---------------|-------------|---------|
| Free | 0円 | ✅ | ❌ | - | あり |
| Standard | 100円 | ✅ | ❌ | - | なし |
| Pro | 300円 | ✅ | ✅ | 400万/月 | なし |
| Premia | 500円 | ✅ | ✅ | 800万/月 | なし |

### 将来対応
- 年額プラン（割引適用）
- プロモーションコード機能

### 採用しない機能
- トライアル期間
- オフライン猶予期間（有効期限内であればオフラインでも使用可能）
- 招待コード（友達招待で特典）

---

## プロモーションコード（将来対応）

### 概要

プロモーションコードを入力すると、一定期間有料プランを無料で使用できる機能。
本Issueでは実装しないが、データモデル設計に影響するため要件を定義しておく。

### ユースケース

- インフルエンサー配布コード
- キャンペーン用コード
- ベータテスター向け特典

### 設計上の考慮事項

#### 1. subscriptionsテーブルへの影響

プランの取得元（支払い vs プロモーション）を区別できるようにする。

```sql
-- 本Issueで追加するカラム
subscription_source TEXT NOT NULL DEFAULT 'payment'  -- 'payment', 'promotion'
```

#### 2. プロモーションコード管理テーブル（将来実装）

```sql
CREATE TABLE promotion_codes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code TEXT NOT NULL UNIQUE,           -- 'WELCOME2025', 'STREAMER_ABC'
    plan_type TEXT NOT NULL,             -- 付与されるプラン
    duration_days INT NOT NULL,          -- 有効期間（日数）
    token_limit_override BIGINT,         -- トークン上限の上書き（NULL=プラン標準）
    max_uses INT,                        -- 最大使用回数（NULLは無制限）
    current_uses INT DEFAULT 0,
    valid_from TIMESTAMPTZ,              -- コード有効開始日
    valid_until TIMESTAMPTZ,             -- コード有効終了日
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE promotion_code_usages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code_id UUID REFERENCES promotion_codes(id) NOT NULL,
    user_id UUID REFERENCES auth.users(id) NOT NULL,
    used_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(code_id, user_id)             -- 同一コードは1人1回
);
```

#### 3. プロモーション期間終了後の挙動

- 期間終了 → Freeに戻る
- プロモーション中に有料契約した場合 → 有料契約が優先

### 本Issueでの対応

- `subscriptions`テーブルに`subscription_source`カラムを追加（拡張性確保）
- プロモーションコード関連テーブル・UIは将来実装

---

## プラン変更ルール

### 基本方針
プラン変更は即座には反映されず、現在の契約期間満了後に新プランが有効となる。
ただし、Freeからのアップグレードは即座に有効化される。

### 変更パターン

| 変更種別 | 例 | 反映タイミング |
|----------|-----|---------------|
| Free → 有料 | Free → Standard | **即座に有効化** |
| アップグレード | Standard → Pro | 現在の期間満了後 |
| ダウングレード | Pro → Standard | 現在の期間満了後 |
| 解約 | Pro → Free | 現在の期間満了後 |

### 理由
- **Free → 有料**: ユーザーはすぐに有料機能を使いたいため即座に有効化
- **アップグレード**: 残り期間の権利を消化してから新プランが開始
- **ダウングレード**: 支払い済みの期間は現在のプランを維持（ユーザーに有利）

### 実装
- `subscriptions.next_plan_type` カラムで次回プランを管理
- 期間満了時に `plan_type = next_plan_type` に更新

---

## クラウドAI翻訳

### 処理フロー
```
クライアント → 画像キャプチャ送信 → Supabase Edge Function → AI API → 翻訳結果返却
                    (RequestId付与)           ↓
                                       プラン検証 + トークン消費記録
                                       (Idempotency Key で二重課金防止)
```

### 使用モデル

| 用途 | モデル | 入力コスト | 出力コスト |
|------|--------|-----------|-----------|
| メイン | Gemini 2.5 Flash-Lite | $0.10/1M tokens | $0.40/1M tokens |
| フォールバック | GPT-4o-mini | $0.15/1M tokens | $0.60/1M tokens |

### トークン消費見積もり

1リクエスト（画像OCR+翻訳）あたり:
- 画像入力: ~1,000トークン
- プロンプト: ~200トークン
- 出力（OCR+翻訳結果）: ~800トークン
- **合計: ~2,000トークン/リクエスト**

### コスト・利益計算

| プラン | 価格 | トークン | API原価 | 粗利 | 粗利率 | 利用時間目安 |
|--------|------|---------|---------|------|--------|-------------|
| Pro | 300円 ($2.00) | 400万 | $0.88 | $1.12 | 56% | 約22時間/月 |
| Premia | 500円 ($3.30) | 800万 | $1.76 | $1.54 | 47% | 約44時間/月 |

### トークンリセットタイミング

**契約開始日基準**でリセット（カレンダー月基準ではない）

- 例: 1月15日に契約開始 → 毎月15日にトークンリセット
- 理由: いつ登録しても公平にトークンを使用できる

### トークン制限超過時の挙動
- クラウドAI翻訳を自動的に無効化
- ローカル翻訳に強制切り替え
- ユーザーに通知表示（「今月のクラウドAI翻訳上限に達しました」）

### 二重課金防止（Idempotency Key）

ネットワーク障害でリトライした場合の二重課金を防止する。

```csharp
public class CloudAiRequest
{
    public Guid RequestId { get; set; }  // クライアント生成のユニークID
    public byte[] ImageData { get; set; }
    public string SourceLanguage { get; set; }
    public string TargetLanguage { get; set; }
}
```

- クライアント側で `Guid.NewGuid()` を生成
- リトライ時は同じ `RequestId` を使用
- サーバー側で `RequestId` の重複チェック → 既存なら前回の結果を返却

---

## 認証・セッション管理

### 基本方針
```
1アカウント = 1アクティブセッション
新しいデバイスでログイン → 旧デバイスは自動ログアウト
```

### メリット
- デバイス管理UIが不要
- PC買い替え時も新PCでログインするだけ
- 不正なアカウント共有を自動防止

### 技術実装

```sql
-- アクティブセッション管理テーブル
CREATE TABLE active_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES auth.users(id) NOT NULL UNIQUE,  -- UNIQUE制約で1セッション保証
    session_token TEXT NOT NULL,
    device_info JSONB,  -- { "os": "Windows 11", "app_version": "1.0.0" }
    last_activity_at TIMESTAMPTZ DEFAULT NOW(),
    created_at TIMESTAMPTZ DEFAULT NOW()
);
```

**ログイン時の処理**:
```sql
BEGIN;
    DELETE FROM active_sessions WHERE user_id = $1;  -- 既存セッション削除
    INSERT INTO active_sessions (user_id, session_token, device_info) VALUES (...);
COMMIT;
```

**API呼び出し時の検証**:
- リクエストの `session_token` と DB の値を比較
- 不一致なら `401 Unauthorized` を返し、クライアント側で再ログイン促す

---

## オフライン時の挙動

### 基本方針
- 有効期限内であればオフラインでも全機能を利用可能（クラウドAI翻訳を除く）
- オフライン猶予期間は設けない（ユーザーは1か月の権利を購入済み）
- クラウドAI翻訳はオンライン必須（当然）

### 詳細

| 状況 | ローカル翻訳 | クラウドAI翻訳 |
|------|-------------|---------------|
| オンライン + 有効期間内 | ✅ | ✅（Pro/Premia） |
| オフライン + 有効期間内 | ✅ | ❌ → ローカル翻訳にフォールバック |
| 期間切れ | Free扱い | ❌ |

### オフライン時のクラウドAI翻訳フォールバック

Pro/Premiaユーザーがオフライン時にクラウドAI翻訳を使用しようとした場合：

```
翻訳実行
    ↓
クラウドAI翻訳を試行
    ↓
[オフライン/サーバー接続失敗]
    ↓
ローカル翻訳にフォールバック
    ↓
ユーザーに通知「クラウドAI翻訳に接続できません。ローカル翻訳を使用します。」
```

### ライセンス検証
- アプリ起動時にオンラインならサーバー検証（HMAC署名付きレスポンス）
- オフライン時はキャッシュの有効期限のみチェック（署名検証はスキップ）
- 有効期限はローカルにキャッシュし、オフラインでも判定可能

**オフライン時に署名検証をスキップする理由**:
- クライアントに秘密鍵を埋め込むとリバースエンジニアリングで漏洩リスク
- 漏洩時は全ユーザーの署名再発行が必要になる
- オンライン時は常にサーバー検証するため、オフライン時は有効期限チェックで十分

---

## セキュリティ

### キャッシュ改ざん対策（HMAC署名）

ローカルキャッシュの改ざんを防止するため、サーバー側でHMAC-SHA256署名を付与する。

```csharp
public class LicenseState
{
    public PlanType CurrentPlan { get; set; }
    public PlanType? NextPlan { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public long? CloudAiTokensUsed { get; set; }
    public long? CloudAiTokensLimit { get; set; }
    public DateTime? BillingCycleStart { get; set; }
    public DateTime LastVerifiedAt { get; set; }

    // サーバー側で生成した署名
    public string Signature { get; set; }

    // 検証用チャレンジトークン
    public string ChallengeToken { get; set; }

    /// <summary>署名を検証</summary>
    public bool VerifySignature(byte[] secretKey)
    {
        var data = $"{CurrentPlan}|{NextPlan}|{ExpiresAt:O}|{CloudAiTokensUsed}|{ChallengeToken}";
        var computed = ComputeHmacSha256(data, secretKey);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(Signature),
            computed);
    }
}
```

**検証フロー**:
1. キャッシュ読み込み時に署名検証
2. 検証失敗 → 強制的にオンライン再検証
3. オフラインで検証失敗 → Free扱いにフォールバック

### Rate Limiting（DDoS対策）

Edge Function側でリクエスト数を制限する。

```sql
CREATE TABLE rate_limits (
    user_id UUID REFERENCES auth.users(id) NOT NULL,
    minute_key TEXT NOT NULL,  -- '2025-01-15T10:30'
    requests_count INT DEFAULT 0,
    PRIMARY KEY (user_id, minute_key)
);
```

**制限値**:
- クラウドAI翻訳: 60リクエスト/分（1秒1回ペース）
- ライセンス検証: 10リクエスト/分

### 監査ログ

不正利用検知のためトークン消費履歴を記録する。

```sql
CREATE TABLE cloud_ai_audit_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES auth.users(id) NOT NULL,
    request_id UUID NOT NULL UNIQUE,  -- Idempotency Key
    tokens_consumed BIGINT NOT NULL,
    request_metadata JSONB,  -- { "image_size": "1920x1080", "detected_text_length": 150 }
    response_status TEXT,    -- 'success', 'error', 'rate_limited'
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- 異常検知用インデックス
CREATE INDEX idx_audit_log_user_created ON cloud_ai_audit_log(user_id, created_at);
```

**異常検知クエリ例**:
```sql
-- 1時間で20万トークン以上消費したユーザーを検出
SELECT user_id, COUNT(*), SUM(tokens_consumed)
FROM cloud_ai_audit_log
WHERE created_at > NOW() - INTERVAL '1 hour'
GROUP BY user_id
HAVING SUM(tokens_consumed) > 200000;
```

---

## データ分析基盤

### 概要

SaaSビジネスの健全性把握と、ゲーム翻訳アプリ特有のニーズ分析のためのデータ基盤を構築する。

### 分析項目

#### 1. 収益・ビジネス分析（SaaS必須）

| 指標 | 用途 |
|------|------|
| MRR（月次経常収益） | ビジネスの健全性 |
| 解約率（Churn Rate） | プラン改善の指標 |
| プロモーション転換率 | 無料→有料のコンバージョン |
| プラン別ユーザー数 | 価格設定の妥当性検証 |

#### 2. ユーザー行動分析

| 指標 | 用途 |
|------|------|
| DAU/MAU | アクティブ率 |
| プラン変更履歴 | アップグレード/ダウングレード傾向 |
| トークン使用率 | 上限設定の妥当性検証 |

#### 3. ゲーム・地域分析

| 指標 | 用途 |
|------|------|
| ゲームタイトル別利用数 | 人気ゲームの把握 |
| 国別ユーザー分布 | マーケティング対象地域 |
| 言語ペア需要 | 翻訳品質改善の優先順位 |
| ゲーム×国のクロス分析 | 地域別ゲーム人気の把握 |

### 分析用テーブル設計

```sql
-- 1. プラン変更履歴（解約率・アップグレード率の計算に必須）
CREATE TABLE subscription_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES auth.users(id) NOT NULL,
    old_plan_type TEXT,                    -- 変更前（NULL=新規）
    new_plan_type TEXT NOT NULL,           -- 変更後
    change_type TEXT NOT NULL,             -- 'new', 'upgrade', 'downgrade', 'cancel', 'promotion'
    subscription_source TEXT,              -- 'payment', 'promotion'
    promotion_code_id UUID,                -- プロモーション経由の場合
    changed_at TIMESTAMPTZ DEFAULT NOW()
);

-- subscription_history自動記録トリガー
-- プラン階層: free(0) < standard(1) < pro(2) < premia(3)
CREATE OR REPLACE FUNCTION record_subscription_change()
RETURNS TRIGGER AS $$
DECLARE
    old_plan_rank INT;
    new_plan_rank INT;
    change_type_value TEXT;
BEGIN
    IF (TG_OP = 'INSERT') THEN
        INSERT INTO subscription_history (user_id, old_plan_type, new_plan_type, change_type, subscription_source)
        VALUES (NEW.user_id, NULL, NEW.plan_type, 'new', NEW.subscription_source);
    ELSIF (TG_OP = 'UPDATE' AND OLD.plan_type != NEW.plan_type) THEN
        -- プラン階層を数値化（比較用）
        old_plan_rank := CASE OLD.plan_type
            WHEN 'free' THEN 0
            WHEN 'standard' THEN 1
            WHEN 'pro' THEN 2
            WHEN 'premia' THEN 3
            ELSE 0
        END;
        new_plan_rank := CASE NEW.plan_type
            WHEN 'free' THEN 0
            WHEN 'standard' THEN 1
            WHEN 'pro' THEN 2
            WHEN 'premia' THEN 3
            ELSE 0
        END;

        -- change_typeを決定
        change_type_value := CASE
            WHEN NEW.plan_type = 'free' THEN 'cancel'        -- 有料→Free: 解約
            WHEN OLD.plan_type = 'free' THEN 'new'           -- Free→有料: 新規
            WHEN new_plan_rank > old_plan_rank THEN 'upgrade'     -- 上位プランへ
            WHEN new_plan_rank < old_plan_rank THEN 'downgrade'   -- 下位プランへ
            ELSE 'downgrade'  -- 同一ランク（通常到達しない）
        END;

        INSERT INTO subscription_history (user_id, old_plan_type, new_plan_type, change_type, subscription_source)
        VALUES (NEW.user_id, OLD.plan_type, NEW.plan_type, change_type_value, NEW.subscription_source);
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_subscription_change
AFTER INSERT OR UPDATE ON subscriptions
FOR EACH ROW EXECUTE FUNCTION record_subscription_change();

-- 2. 翻訳セッション（ゲーム・地域分析用）
CREATE TABLE translation_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES auth.users(id) NOT NULL,
    session_start TIMESTAMPTZ DEFAULT NOW(),
    session_end TIMESTAMPTZ,

    -- ゲーム情報
    game_window_title TEXT,                -- ウィンドウタイトルから取得
    game_process_name TEXT,                -- プロセス名（例: 'game.exe'）

    -- 翻訳情報
    source_language TEXT NOT NULL,         -- 翻訳元言語
    target_language TEXT NOT NULL,         -- 翻訳先言語
    translation_mode TEXT NOT NULL,        -- 'local', 'cloud_ai'

    -- 使用量
    translation_count INT DEFAULT 0,       -- 翻訳回数
    tokens_consumed BIGINT DEFAULT 0,      -- クラウドAIトークン消費量

    -- 地域情報
    country_code TEXT,                     -- ユーザーの国（IP or 設定から）
    timezone TEXT                          -- タイムゾーン
);

-- 3. ユーザープロファイル（地域情報を永続化）
CREATE TABLE user_profiles (
    user_id UUID PRIMARY KEY REFERENCES auth.users(id),
    country_code TEXT,                     -- 登録時のIP or 設定から
    preferred_language TEXT,               -- アプリの表示言語
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- 4. 日次集計テーブル（重いクエリを避けるため）
CREATE TABLE daily_metrics (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    metric_date DATE NOT NULL,

    -- ユーザー指標
    dau INT DEFAULT 0,                     -- Daily Active Users
    new_users INT DEFAULT 0,               -- 新規登録数
    churned_users INT DEFAULT 0,           -- 解約数

    -- プラン別ユーザー数
    free_users INT DEFAULT 0,
    standard_users INT DEFAULT 0,
    pro_users INT DEFAULT 0,
    premia_users INT DEFAULT 0,

    -- 収益（円）
    daily_revenue INT DEFAULT 0,

    -- トークン使用量
    total_tokens_consumed BIGINT DEFAULT 0,
    cloud_ai_requests INT DEFAULT 0,

    UNIQUE(metric_date)
);

-- 5. ゲームタイトル日次集計
CREATE TABLE daily_game_metrics (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    metric_date DATE NOT NULL,
    game_window_title TEXT NOT NULL,       -- 正規化前のタイトル
    game_title_normalized TEXT,            -- 正規化後（将来: マスタ連携）
    country_code TEXT,

    -- 集計値
    unique_users INT DEFAULT 0,
    session_count INT DEFAULT 0,
    translation_count INT DEFAULT 0,

    UNIQUE(metric_date, game_window_title, country_code)
);

-- 日次集計バッチ処理（毎日UTC 1:00に実行）
-- CTEを使用して各テーブルを1パスでスキャン（パフォーマンス最適化）
CREATE OR REPLACE FUNCTION aggregate_daily_metrics(target_date DATE DEFAULT CURRENT_DATE - 1)
RETURNS void AS $$
BEGIN
    -- daily_metricsの集計（CTE最適化版）
    WITH
    -- translation_sessionsを1回だけスキャン
    session_metrics AS (
        SELECT
            COUNT(DISTINCT user_id) as dau,
            COALESCE(SUM(tokens_consumed), 0) as total_tokens,
            COUNT(*) FILTER (WHERE translation_mode = 'cloud_ai') as cloud_ai_requests
        FROM translation_sessions
        WHERE session_start::date = target_date
    ),
    -- subscription_historyを1回だけスキャン
    history_metrics AS (
        SELECT
            COUNT(*) FILTER (WHERE change_type = 'new') as new_users,
            COUNT(*) FILTER (WHERE change_type = 'cancel') as churned_users
        FROM subscription_history
        WHERE changed_at::date = target_date
    ),
    -- subscriptionsを1回だけスキャン
    plan_counts AS (
        SELECT
            COUNT(*) FILTER (WHERE plan_type = 'free') as free_users,
            COUNT(*) FILTER (WHERE plan_type = 'standard') as standard_users,
            COUNT(*) FILTER (WHERE plan_type = 'pro') as pro_users,
            COUNT(*) FILTER (WHERE plan_type = 'premia') as premia_users
        FROM subscriptions
    )
    INSERT INTO daily_metrics (
        metric_date, dau, new_users, churned_users,
        free_users, standard_users, pro_users, premia_users,
        daily_revenue, total_tokens_consumed, cloud_ai_requests
    )
    SELECT
        target_date,
        sm.dau,
        hm.new_users,
        hm.churned_users,
        pc.free_users,
        pc.standard_users,
        pc.pro_users,
        pc.premia_users,
        pc.standard_users * 100 + pc.pro_users * 300 + pc.premia_users * 500,
        sm.total_tokens,
        sm.cloud_ai_requests
    FROM session_metrics sm, history_metrics hm, plan_counts pc
    ON CONFLICT (metric_date) DO UPDATE SET
        dau = EXCLUDED.dau,
        new_users = EXCLUDED.new_users,
        churned_users = EXCLUDED.churned_users,
        free_users = EXCLUDED.free_users,
        standard_users = EXCLUDED.standard_users,
        pro_users = EXCLUDED.pro_users,
        premia_users = EXCLUDED.premia_users,
        daily_revenue = EXCLUDED.daily_revenue,
        total_tokens_consumed = EXCLUDED.total_tokens_consumed,
        cloud_ai_requests = EXCLUDED.cloud_ai_requests;

    -- daily_game_metricsの集計
    INSERT INTO daily_game_metrics (
        metric_date, game_window_title, country_code,
        unique_users, session_count, translation_count
    )
    SELECT
        target_date,
        game_window_title,
        country_code,
        COUNT(DISTINCT user_id),
        COUNT(*),
        SUM(translation_count)
    FROM translation_sessions
    WHERE session_start::date = target_date
      AND game_window_title IS NOT NULL
    GROUP BY game_window_title, country_code
    ON CONFLICT (metric_date, game_window_title, country_code) DO UPDATE SET
        unique_users = EXCLUDED.unique_users,
        session_count = EXCLUDED.session_count,
        translation_count = EXCLUDED.translation_count;
END;
$$ LANGUAGE plpgsql;

-- pg_cronで毎日UTC 1:00に実行（Supabaseで設定）
-- SELECT cron.schedule('daily-metrics', '0 1 * * *', 'SELECT aggregate_daily_metrics()');

-- インデックス
CREATE INDEX idx_subscription_history_user ON subscription_history(user_id);
CREATE INDEX idx_subscription_history_changed ON subscription_history(changed_at);
CREATE INDEX idx_translation_sessions_user ON translation_sessions(user_id);
CREATE INDEX idx_translation_sessions_start ON translation_sessions(session_start);
CREATE INDEX idx_translation_sessions_game ON translation_sessions(game_window_title);
CREATE INDEX idx_daily_game_metrics_date ON daily_game_metrics(metric_date);
```

### クライアント側で収集する情報

翻訳セッション開始時に送信：

```csharp
public class TranslationSessionData
{
    public string GameWindowTitle { get; set; }    // ウィンドウタイトル
    public string GameProcessName { get; set; }    // プロセス名
    public string SourceLanguage { get; set; }
    public string TargetLanguage { get; set; }
    public string TranslationMode { get; set; }    // "local" or "cloud_ai"
}
```

**注意**: ゲームタイトルはウィンドウタイトルから自動取得（既存機能）。正確なゲーム名への正規化は将来対応。

### 分析クエリ例

```sql
-- 人気ゲームTop10（今月）
SELECT game_window_title, COUNT(DISTINCT user_id) as users, SUM(translation_count) as translations
FROM translation_sessions
WHERE session_start >= DATE_TRUNC('month', NOW())
GROUP BY game_window_title
ORDER BY users DESC
LIMIT 10;

-- 国別ユーザー分布
SELECT country_code, COUNT(DISTINCT user_id) as users
FROM translation_sessions
WHERE session_start >= DATE_TRUNC('month', NOW())
GROUP BY country_code
ORDER BY users DESC;

-- 日本で人気のゲーム vs 海外で人気のゲーム
SELECT
    game_window_title,
    SUM(CASE WHEN country_code = 'JP' THEN unique_users ELSE 0 END) as jp_users,
    SUM(CASE WHEN country_code != 'JP' THEN unique_users ELSE 0 END) as other_users
FROM daily_game_metrics
WHERE metric_date >= CURRENT_DATE - 30
GROUP BY game_window_title
ORDER BY (jp_users + other_users) DESC
LIMIT 20;

-- 月次解約率
SELECT
    DATE_TRUNC('month', changed_at) as month,
    COUNT(*) FILTER (WHERE change_type = 'cancel') as cancellations,
    COUNT(*) FILTER (WHERE change_type = 'new') as new_subscriptions
FROM subscription_history
GROUP BY DATE_TRUNC('month', changed_at)
ORDER BY month;
```

### プライバシー考慮事項

- **IPアドレスは保存しない**（国コード推定後に即座に破棄）
- ゲームタイトル・国情報は統計目的のみに使用
- 個人を特定できる情報は収集しない
- プライバシーポリシーに分析目的でのデータ収集を明記
- 将来対応: 分析データ収集のオプトアウト機能

### プライバシーポリシーへの追記事項

以下の内容をプライバシーポリシーに追記する：

```
【サービス改善のための統計データ収集】

本サービスでは、サービス改善を目的として以下の情報を収集します：

■ 収集する情報
- ゲームウィンドウタイトル（翻訳対象のゲーム名）
- 翻訳言語ペア（翻訳元言語・翻訳先言語）
- 利用国（IPアドレスから推定、IPアドレス自体は保存しません）
- 翻訳モード（ローカル翻訳/クラウドAI翻訳）
- セッション情報（利用開始時刻、翻訳回数）

■ 利用目的
- 人気ゲームの把握によるサービス改善
- 翻訳品質改善の優先順位決定
- マーケティング戦略の策定

■ データの保持期間
- 個別セッションデータ: 90日間
- 日次集計データ: 無期限（個人を特定できない形式）

■ 注意事項
- 収集したデータは統計目的のみに使用し、個人を特定する目的では使用しません
- IPアドレスは国コード推定後に即座に破棄し、保存しません
```

---

## エラーハンドリング

### リトライ戦略

Pollyライブラリを使用した堅牢なエラーハンドリングを実装する。

```csharp
public class LicenseManagerOptions
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}
```

**リトライ対象**:
- `HttpRequestException`（ネットワークエラー）
- `TaskCanceledException`（タイムアウト）
- HTTP 5xx（サーバーエラー）

**リトライ非対象**:
- HTTP 401（認証エラー）→ 再ログイン促す
- HTTP 403（権限エラー）→ プラン不足を通知
- HTTP 429（Rate Limit）→ 待機後リトライ

### トークン消費失敗時の挙動

```csharp
public async Task<CloudAiTranslationResult> TranslateWithCloudAiAsync(...)
{
    // トークン消費APIが失敗した場合
    if (!await ConsumeCloudAiTokensAsync(estimatedTokens, requestId, ct))
    {
        _logger.LogWarning("Token consumption failed, falling back to local translation");

        // ローカル翻訳にフォールバック
        return new CloudAiTranslationResult
        {
            Success = false,
            FallbackToLocal = true,
            ErrorMessage = "クラウドAI翻訳に接続できません。ローカル翻訳を使用します。"
        };
    }

    // クラウドAI翻訳を実行
    ...
}
```

---

## UI要件

### プラン表示場所
- 設定画面 > アカウントタブ

### 表示内容
- 現在のプラン名
- 次回プラン（変更予約がある場合）
- 有効期限（有料プランの場合）
- クラウドAI翻訳の残りトークン（Pro/Premiaの場合）
- 次回トークンリセット日
- アップグレードボタン（Free/Standardの場合）

### アラート・通知機能

| トリガー | 通知内容 | 表示方法 |
|----------|----------|----------|
| トークン80%到達 | 「クラウドAI翻訳の残りが20%です」 | トースト通知 |
| トークン100%到達 | 「今月のクラウドAI翻訳上限に達しました」 | モーダルダイアログ |
| 有効期限7日前 | 「プランの有効期限が近づいています」 | トースト通知 |
| 有効期限切れ | 「プランの有効期限が切れました」 | モーダルダイアログ |
| セッション無効化 | 「別のデバイスでログインされました」 | モーダルダイアログ |

### アップグレード導線
- アプリ内からWebブラウザで決済ページへ遷移
- 決済完了後、アプリ側でライセンス状態を再取得

---

## データモデル

### ライセンス状態（クライアント側キャッシュ）

```csharp
public enum PlanType
{
    Free,
    Standard,
    Pro,
    Premia
}

public class LicenseState
{
    public PlanType CurrentPlan { get; set; }
    public PlanType? NextPlan { get; set; }           // 次回更新時のプラン
    public DateTime? ExpiresAt { get; set; }
    public long? CloudAiTokensUsed { get; set; }
    public long? CloudAiTokensLimit { get; set; }
    public DateTime? BillingCycleStart { get; set; }  // 現在の課金サイクル開始日
    public DateTime? BillingCycleEnd { get; set; }    // 現在の課金サイクル終了日（トークンリセット日）
    public DateTime LastVerifiedAt { get; set; }

    // 署名（改ざん検出用）
    public string Signature { get; set; }
    public string ChallengeToken { get; set; }
}
```

### サーバー側（Supabase Database）

```sql
-- ユーザーのサブスクリプション情報
CREATE TABLE subscriptions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES auth.users(id) NOT NULL UNIQUE,
    plan_type TEXT NOT NULL DEFAULT 'free',
    next_plan_type TEXT,                    -- 次回更新時のプラン（NULL=変更なし）
    subscription_source TEXT NOT NULL DEFAULT 'payment',  -- 'payment', 'promotion'（将来拡張用）
    billing_day INT NOT NULL DEFAULT 1,     -- 契約日の日（1-28、29-31は28に丸める）
    expires_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),

    CHECK (billing_day >= 1 AND billing_day <= 28)
);

-- アクティブセッション管理
CREATE TABLE active_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES auth.users(id) NOT NULL UNIQUE,
    session_token TEXT NOT NULL,
    device_info JSONB,
    last_activity_at TIMESTAMPTZ DEFAULT NOW(),
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- クラウドAIトークン使用量（契約開始日基準でリセット）
CREATE TABLE cloud_ai_usage (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES auth.users(id) NOT NULL,
    billing_cycle_start DATE NOT NULL,      -- この課金サイクルの開始日
    tokens_used BIGINT DEFAULT 0,
    updated_at TIMESTAMPTZ DEFAULT NOW(),

    UNIQUE(user_id, billing_cycle_start)
);

-- クラウドAIリクエスト記録（Idempotency Key + 監査ログ）
CREATE TABLE cloud_ai_requests (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES auth.users(id) NOT NULL,
    request_id UUID NOT NULL UNIQUE,        -- クライアント生成のIdempotency Key
    tokens_consumed BIGINT NOT NULL,
    request_metadata JSONB,
    response_data JSONB,                    -- キャッシュ用（リトライ時に返却）
    response_status TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Rate Limiting
CREATE TABLE rate_limits (
    user_id UUID REFERENCES auth.users(id) NOT NULL,
    minute_key TEXT NOT NULL,
    requests_count INT DEFAULT 0,
    PRIMARY KEY (user_id, minute_key)
);

-- インデックス
CREATE INDEX idx_subscriptions_user ON subscriptions(user_id);
CREATE INDEX idx_cloud_ai_usage_user_cycle ON cloud_ai_usage(user_id, billing_cycle_start);
CREATE INDEX idx_cloud_ai_requests_user_created ON cloud_ai_requests(user_id, created_at);
CREATE INDEX idx_active_sessions_token ON active_sessions(session_token);
```

---

## インターフェース設計

### ILicenseManager

```csharp
public interface ILicenseManager
{
    /// <summary>現在のライセンス状態を取得（キャッシュ優先）</summary>
    Task<LicenseState> GetCurrentStateAsync(CancellationToken ct = default);

    /// <summary>サーバーからライセンス状態を再取得</summary>
    Task<LicenseState> RefreshStateAsync(CancellationToken ct = default);

    /// <summary>プラン変更を即座に反映（強制再検証）</summary>
    Task<LicenseState> ForceRefreshAsync(CancellationToken ct = default);

    /// <summary>指定機能が利用可能か判定</summary>
    bool IsFeatureAvailable(FeatureType feature);

    /// <summary>クラウドAIトークンを消費（サーバーに記録）</summary>
    /// <param name="tokens">消費トークン数</param>
    /// <param name="requestId">Idempotency Key</param>
    Task<TokenConsumptionResult> ConsumeCloudAiTokensAsync(
        long tokens,
        Guid requestId,
        CancellationToken ct = default);

    /// <summary>ライセンス状態変更イベント</summary>
    event EventHandler<LicenseStateChangedEventArgs>? StateChanged;

    /// <summary>プラン変更イベント</summary>
    event EventHandler<PlanChangedEventArgs>? PlanChanged;

    /// <summary>トークン使用量警告イベント（80%到達時）</summary>
    event EventHandler<TokenUsageWarningEventArgs>? TokenUsageWarning;

    /// <summary>有効期限警告イベント（7日前）</summary>
    event EventHandler<PlanExpirationWarningEventArgs>? PlanExpirationWarning;

    /// <summary>セッション無効化イベント</summary>
    event EventHandler<SessionInvalidatedEventArgs>? SessionInvalidated;
}

public enum FeatureType
{
    LocalTranslation,
    CloudAiTranslation,
    AdFree
}

public class TokenConsumptionResult
{
    public bool Success { get; set; }
    public long RemainingTokens { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsRateLimited { get; set; }
    public bool IsQuotaExceeded { get; set; }
}
```

---

## 実装フェーズ

### Phase 1: 基盤実装
- [ ] LicenseState モデル定義（署名フィールド含む）
- [ ] ILicenseManager インターフェース定義
- [ ] ローカルキャッシュ機能（署名検証付き）
- [ ] Supabase テーブル作成（全6テーブル）

### Phase 2: サーバー連携
- [ ] ライセンス検証 Edge Function（署名生成含む）
- [ ] トークン消費記録 Edge Function（Idempotency Key対応）
- [ ] セッション検証 Edge Function
- [ ] Rate Limiting実装
- [ ] クライアント側 API クライアント実装（Pollyリトライ）

### Phase 3: UI実装
- [ ] 設定画面 > アカウントタブにプラン情報表示
- [ ] トークン残量表示（プログレスバー）
- [ ] 次回プラン表示
- [ ] アラート・通知機能
- [ ] アップグレード/ダウングレードボタン

### Phase 4: 機能制限統合
- [ ] 広告表示制御との連携
- [ ] クラウドAI翻訳の有効/無効切り替え
- [ ] トークン超過時の自動切り替え
- [ ] セッション無効化時の再ログイン誘導

---

## 依存関係

### このIssueが前提となるもの
- #110 統合トライアル・決済・分析（決済部分）
- #78 クラウドAI翻訳連携
- #76 有料・無料プラン機能差別化

### このIssueが依存するもの
- Supabase Auth（実装済み）
- Supabase Database（実装済み）
- Supabase Edge Functions（実装済み）

---

## 備考

- 年額プランは将来対応とし、本Issueでは月額のみ実装
- 決済処理（FastSpring連携）は #110 で実装
- クラウドAI翻訳の実際のAPI呼び出しは #78 で実装
- 本Issueはライセンス状態の管理・検証・UI表示に集中
- HMAC署名の秘密鍵はサーバー側環境変数で管理

---

## 更新履歴

| 日付 | 内容 |
|------|------|
| 2025-12-18 | 初版作成 |
| 2025-12-18 | Geminiレビュー反映: セキュリティ強化、プラン変更ルール、契約開始日基準トークンリセット、エラーハンドリング追加 |
| 2025-12-18 | オフライン時フォールバック動作を明確化、プロモーションコード将来対応の設計考慮事項を追加 |
| 2025-12-18 | データ分析基盤セクションを追加（収益分析、ゲーム・地域分析用テーブル設計） |
| 2025-12-18 | プライバシー考慮事項を強化（IP非保持、プライバシーポリシー追記事項） |
| 2025-12-18 | データ整合性強化: subscription_history自動記録トリガー、日次集計バッチ処理、オフライン時署名検証スキップ方針を追加 |
| 2025-12-18 | Geminiレビュー反映: change_typeロジック修正（プラン階層の数値化）、日次集計バッチ処理のCTE最適化 |
