/**
 * Cloud AI翻訳エンドポイント
 * Gemini / OpenAI APIを使用した画像テキスト翻訳
 *
 * 対応プロバイダー:
 * - Gemini (gemini-2.5-flash-lite)
 * - OpenAI (gpt-4.1-nano)
 *
 * セキュリティ:
 * - [Issue #280+#281] Supabase JWT認証（Patreonセッション or Supabase JWT）
 * - プランチェック（Pro/Premium/Ultimate または ボーナストークン所有者）
 * - レートリミット
 */

import { createClient, SupabaseClient } from '@supabase/supabase-js';
import { jwtVerify, JWTPayload } from 'jose';

// ============================================
// 型定義
// ============================================

export interface TranslateEnv {
  SESSIONS: KVNamespace;
  GEMINI_API_KEY?: string;
  OPENAI_API_KEY?: string;
  GEMINI_MODEL?: string;  // モデル名（デフォルト: gemini-2.5-flash-lite）
  OPENAI_MODEL?: string;  // モデル名（デフォルト: gpt-4.1-nano）
  API_KEY: string;
  ALLOWED_ORIGINS: string;
  ENVIRONMENT?: string;
  // [Issue #280+#281] Supabase認証用
  SUPABASE_URL?: string;
  SUPABASE_SERVICE_KEY?: string;
  // [Issue #287] JWT認証用
  JWT_SECRET?: string;
}

/** セッションデータ（index.tsと共有） */
interface SessionData {
  accessToken: string;
  refreshToken: string;
  expiresAt: number;
  userId: string;
}

/** キャッシュ済みメンバーシップ情報 */
interface CachedMembership {
  membership: ParsedMembership;
  cachedAt: number;
}

/** パース済みメンバーシップ情報 */
interface ParsedMembership {
  userId: string;
  email: string;
  fullName: string;
  plan: PlanType;  // Issue #257
  tierId: string;
  patronStatus: string;
  nextChargeDate: string | null;
  entitledAmountCents: number;
}

/** 翻訳リクエスト */
interface TranslateRequest {
  provider: 'gemini' | 'openai';
  image_base64: string;
  mime_type: string;
  source_language: string;
  target_language: string;
  context?: string;
  request_id?: string;
}

/** Gemini API リクエスト */
interface GeminiRequest {
  contents: Array<{
    parts: Array<{
      text?: string;
      inlineData?: {
        mimeType: string;
        data: string;
      };
    }>;
  }>;
  generationConfig?: {
    temperature?: number;
    maxOutputTokens?: number;
  };
}

/** Gemini API レスポンス */
interface GeminiResponse {
  candidates?: Array<{
    content?: {
      parts?: Array<{
        text?: string;
      }>;
    };
    finishReason?: string;
  }>;
  usageMetadata?: {
    promptTokenCount?: number;
    candidatesTokenCount?: number;
    totalTokenCount?: number;
  };
  error?: {
    code: number;
    message: string;
    status: string;
  };
}

/** OpenAI API リクエスト */
interface OpenAIRequest {
  model: string;
  messages: Array<{
    role: 'system' | 'user' | 'assistant';
    content: string | Array<{
      type: 'text' | 'image_url';
      text?: string;
      image_url?: {
        url: string;
        detail?: 'low' | 'high' | 'auto';
      };
    }>;
  }>;
  max_tokens?: number;
  temperature?: number;
}

/** OpenAI API レスポンス */
interface OpenAIResponse {
  id: string;
  object: string;
  created: number;
  model: string;
  choices: Array<{
    index: number;
    message: {
      role: string;
      content: string;
    };
    finish_reason: string;
  }>;
  usage?: {
    prompt_tokens: number;
    completion_tokens: number;
    total_tokens: number;
  };
  error?: {
    message: string;
    type: string;
    code: string;
  };
}

/** 翻訳レスポンス */
interface TranslateResponse {
  success: boolean;
  request_id: string;
  detected_text?: string;
  translated_text?: string;
  detected_language?: string;
  provider_id?: string;
  token_usage?: {
    input_tokens: number;
    output_tokens: number;
    image_tokens: number;
  };
  processing_time_ms?: number;
  error?: {
    code: string;
    message: string;
    is_retryable: boolean;
  };
  /** [Issue #275] 複数テキスト対応 - BoundingBox付きテキスト配列 */
  texts?: TranslatedTextItem[];
  /** [Issue #296] 月間トークン使用状況 */
  monthly_usage?: {
    year_month: string;
    tokens_used: number;
    tokens_limit: number;
  };
}

/** [Issue #275] 翻訳されたテキストアイテム */
interface TranslatedTextItem {
  original: string;
  translation: string;
  /** BoundingBox座標 [y_min, x_min, y_max, x_max] (0-1000スケール) */
  bounding_box?: [number, number, number, number];
}

// ============================================
// 定数
// ============================================

const GEMINI_API_BASE = 'https://generativelanguage.googleapis.com/v1beta';
const DEFAULT_GEMINI_MODEL = 'gemini-2.5-flash-lite';
const OPENAI_API_BASE = 'https://api.openai.com/v1';
const DEFAULT_OPENAI_MODEL = 'gpt-4.1-nano';
/** [Issue #286] メンバーシップキャッシュTTL（1時間）- 手動同期で即時反映可能 */
const IDENTITY_CACHE_TTL_SECONDS = 60 * 60; // 1 hour (was 5 minutes)
const API_TIMEOUT_MS = 30000; // 30秒タイムアウト
/** [Issue #286] 認証結果キャッシュTTL（1時間）- KV Write削減のため延長 */
const AUTH_CACHE_TTL_SECONDS = 60 * 60; // 1 hour (was 60 seconds)
/** [Issue #296] クォータキャッシュTTL（5分）- Geminiフィードバック反映 */
const QUOTA_CACHE_TTL_SECONDS = 5 * 60; // 5 minutes

// [Issue #296] Patreon API定数（メンバーシップKV null時のフォールバック用）
const PATREON_API_BASE = 'https://www.patreon.com/api/oauth2';
const PATREON_IDENTITY_URL = `${PATREON_API_BASE}/v2/identity`;
const PATREON_IDENTITY_PARAMS = 'include=memberships.currently_entitled_tiers,memberships.campaign&fields[user]=email,full_name&fields[member]=patron_status,currently_entitled_amount_cents,next_charge_date,campaign_lifetime_support_cents&fields[tier]=title,amount_cents';

/** Tier金額しきい値（円） - index.tsと同期 */
const TIER_AMOUNTS = {
  ULTIMATE: 900,  // $9相当
  PREMIUM: 500,   // $5相当
  PRO: 300,       // $3相当
} as const;

/** Patreonリソースタイプ定数 */
const PATREON_RESOURCE_TYPES = {
  USER: 'user',
  MEMBER: 'member',
  TIER: 'tier',
} as const;

/** Patron ステータス定数 */
const PATRON_STATUS = {
  ACTIVE: 'active_patron',
  FORMER: 'former_patron',
  DECLINED: 'declined_patron',
} as const;

// [Issue #287] JWT認証設定
const JWT_ISSUER = 'https://baketa-relay.suke009.workers.dev';
const JWT_AUDIENCE = 'baketa-client';

/** [Issue #257] プラン名定義 */
const PLAN = {
  FREE: 'Free',
  PRO: 'Pro',
  PREMIUM: 'Premium',
  ULTIMATE: 'Ultimate',
} as const;

/** プラン型 */
type PlanType = typeof PLAN[keyof typeof PLAN];

/** Cloud AI翻訳が利用可能なプラン */
// Issue #257: Pro/Premium/Ultimate 3段階構成に改定
const ALLOWED_PLANS: readonly PlanType[] = [PLAN.PRO, PLAN.PREMIUM, PLAN.ULTIMATE];

// ============================================
// [Issue #296] Patreon API型定義（メンバーシップKV null時のフォールバック用）
// ============================================

/** ユーザートークン（Patreonアクセストークン保持用） */
interface UserTokenData {
  accessToken: string;
  refreshToken: string;
  expiresAt: number;
  email: string;
  fullName: string;
  sessionTokens: string[];
  updatedAt: number;
}

/** Patreon ユーザー属性 */
interface PatreonUserAttributes {
  email: string;
  full_name: string;
}

/** Patreon ユーザー */
interface PatreonUser {
  id: string;
  type: 'user';
  attributes: PatreonUserAttributes;
}

/** Patreon Tier属性 */
interface PatreonTierAttributes {
  title: string;
  amount_cents: number;
}

/** Patreon Tier */
interface PatreonTier {
  id: string;
  type: 'tier';
  attributes: PatreonTierAttributes;
}

/** Patreon メンバー属性 */
interface PatreonMemberAttributes {
  patron_status: 'active_patron' | 'former_patron' | 'declined_patron' | null;
  currently_entitled_amount_cents: number;
  next_charge_date: string | null;
  campaign_lifetime_support_cents?: number;
}

/** Patreon メンバー */
interface PatreonMember {
  id: string;
  type: 'member';
  attributes: PatreonMemberAttributes;
}

/** Patreon Identity APIレスポンス */
interface PatreonIdentityResponse {
  data: PatreonUser;
  included?: (PatreonMember | PatreonTier)[];
}

// ============================================
// [Issue #280+#281] 認証結果型
// ============================================

/** 認証済みユーザー情報 */
interface AuthenticatedUser {
  userId: string;
  plan: PlanType;
  hasBonusTokens: boolean;
  authMethod: 'patreon' | 'supabase' | 'jwt';  // [Issue #287] JWT認証追加
}

/** [Issue #287] JWTカスタムクレーム */
interface BaketaJwtClaims extends JWTPayload {
  sub: string;           // Patreon user ID
  plan: PlanType;        // 現在のプラン
  hasBonusTokens: boolean; // ボーナストークン有無
  authMethod: 'patreon'; // 元の認証方法
}

// ============================================
// ヘルパー関数
// ============================================

// --------------------------------------------
// [Issue #287] JWT認証ヘルパー
// --------------------------------------------

/**
 * [Issue #287] JWTアクセストークンを検証
 * @param env 環境変数
 * @param token JWTトークン
 * @returns 検証結果（成功時はペイロード、失敗時はnull）
 */
async function validateJwtToken(
  env: TranslateEnv,
  token: string
): Promise<BaketaJwtClaims | null> {
  if (!env.JWT_SECRET) {
    // JWT_SECRETが未設定の場合はJWT認証をスキップ
    return null;
  }

  try {
    const secret = new TextEncoder().encode(env.JWT_SECRET);
    const { payload } = await jwtVerify(token, secret, {
      issuer: JWT_ISSUER,
      audience: JWT_AUDIENCE,
    });

    // 必須クレームの検証
    if (!payload.sub || typeof payload.plan !== 'string') {
      console.warn('[Issue #287] JWT missing required claims');
      return null;
    }

    return payload as BaketaJwtClaims;
  } catch (error) {
    // JWTとして無効な場合は他の認証方式にフォールバック
    // エラーログは出力しない（SessionTokenの可能性があるため）
    return null;
  }
}

// --------------------------------------------
// [Issue #280+#281] Supabase認証ヘルパー
// --------------------------------------------

/** Supabaseクライアント取得 */
function getSupabaseClient(env: TranslateEnv): SupabaseClient | null {
  if (!env.SUPABASE_URL || !env.SUPABASE_SERVICE_KEY) {
    return null;
  }
  return createClient(env.SUPABASE_URL, env.SUPABASE_SERVICE_KEY, {
    auth: { persistSession: false }
  });
}

// ============================================
// [Issue #296] Patreon APIヘルパー関数
// メンバーシップKVがnullの場合にPatreon APIから最新情報を取得
// ============================================

/** ユーザートークンを取得（Patreonアクセストークン用） */
async function getUserToken(env: TranslateEnv, userId: string): Promise<UserTokenData | null> {
  try {
    const key = `usertoken:${userId}`;
    const data = await env.SESSIONS.get<UserTokenData>(key, 'json');
    if (data && typeof data.accessToken === 'string') {
      return data;
    }
    return null;
  } catch {
    return null;
  }
}

/** プラン判定（Tier金額から） */
function determinePlan(amountCents: number): PlanType {
  if (amountCents >= TIER_AMOUNTS.ULTIMATE) return 'Ultimate';
  if (amountCents >= TIER_AMOUNTS.PREMIUM) return 'Premium';
  if (amountCents >= TIER_AMOUNTS.PRO) return 'Pro';
  return 'Free';
}

/** Patreon Identity APIを呼び出し */
async function fetchPatreonIdentity(accessToken: string): Promise<PatreonIdentityResponse | null> {
  try {
    const response = await fetch(`${PATREON_IDENTITY_URL}?${PATREON_IDENTITY_PARAMS}`, {
      headers: { 'Authorization': `Bearer ${accessToken}` },
    });

    if (!response.ok) {
      console.error(`[Issue #296] Patreon identity fetch failed: status=${response.status}`);
      return null;
    }

    return await response.json() as PatreonIdentityResponse;
  } catch (error) {
    console.error('[Issue #296] Patreon identity fetch error:', error);
    return null;
  }
}

/** Patreon Identity APIレスポンスをパース */
function parsePatreonMembership(identityData: PatreonIdentityResponse): ParsedMembership {
  const user = identityData.data;
  const included = identityData.included || [];

  const activeMembership = included.find(
    (item): item is PatreonMember =>
      item.type === PATREON_RESOURCE_TYPES.MEMBER && item.attributes.patron_status === PATRON_STATUS.ACTIVE
  );

  const tiers = included.filter((item): item is PatreonTier => item.type === PATREON_RESOURCE_TYPES.TIER);

  const highestTier = tiers.reduce<PatreonTier | null>((max, tier) => {
    const amount = tier.attributes.amount_cents;
    return amount > (max?.attributes.amount_cents ?? 0) ? tier : max;
  }, null);

  const amountCents = highestTier?.attributes.amount_cents ?? 0;

  return {
    userId: user.id,
    email: user.attributes.email,
    fullName: user.attributes.full_name,
    plan: determinePlan(amountCents),
    tierId: highestTier?.id ?? '',
    patronStatus: activeMembership?.attributes.patron_status ?? 'not_patron',
    nextChargeDate: activeMembership?.attributes.next_charge_date ?? null,
    entitledAmountCents: activeMembership?.attributes.currently_entitled_amount_cents ?? 0,
  };
}

/** メンバーシップをKVにキャッシュ */
async function setCachedMembership(env: TranslateEnv, userId: string, membership: ParsedMembership): Promise<void> {
  const cacheKey = `membership:${userId}`;
  const cached: CachedMembership = { membership, cachedAt: Date.now() };
  await env.SESSIONS.put(cacheKey, JSON.stringify(cached), { expirationTtl: IDENTITY_CACHE_TTL_SECONDS });
}

/**
 * [Issue #296] Patreon APIから最新メンバーシップを取得（KV null時のフォールバック）
 * @param env 環境変数
 * @param patreonUserId Patreon user ID
 * @returns メンバーシップ情報（取得失敗時はnull）
 */
async function fetchAndCachePatreonMembership(
  env: TranslateEnv,
  patreonUserId: string
): Promise<ParsedMembership | null> {
  // 1. UserTokenからPatreonアクセストークンを取得
  const userToken = await getUserToken(env, patreonUserId);
  if (!userToken) {
    console.log(`[Issue #296] No UserToken found for Patreon user: ${patreonUserId}`);
    return null;
  }

  // 2. Patreon APIを呼び出し
  const identityData = await fetchPatreonIdentity(userToken.accessToken);
  if (!identityData) {
    console.log(`[Issue #296] Failed to fetch Patreon identity for user: ${patreonUserId}`);
    return null;
  }

  // 3. メンバーシップをパース
  const membership = parsePatreonMembership(identityData);
  console.log(`[Issue #296] Patreon API fetched: userId=${patreonUserId}, plan=${membership.plan}, patronStatus=${membership.patronStatus}`);

  // 4. KVにキャッシュ
  await setCachedMembership(env, patreonUserId, membership);

  return membership;
}

// ============================================
// [Issue #296] トークン消費記録ヘルパー
// ============================================

/** [Issue #296] トークン消費記録結果 */
interface TokenConsumptionResult {
  year_month: string;
  tokens_used: number;
}

/** プラン別月間トークン上限 */
const PLAN_TOKEN_LIMITS: Record<PlanType, number> = {
  [PLAN.FREE]: 0,
  [PLAN.PRO]: 10_000_000,      // 1,000万トークン
  [PLAN.PREMIUM]: 20_000_000,  // 2,000万トークン
  [PLAN.ULTIMATE]: 50_000_000, // 5,000万トークン
};

/**
 * [Issue #296] トークン消費をサーバーサイドで記録
 * @param env 環境変数
 * @param user 認証済みユーザー
 * @param totalTokens 消費トークン数
 * @returns 更新後の月間使用状況（失敗時はnull）
 */
async function recordTokenConsumption(
  env: TranslateEnv,
  user: AuthenticatedUser,
  totalTokens: number
): Promise<TokenConsumptionResult | null> {
  if (totalTokens <= 0) {
    return null;
  }

  const supabase = getSupabaseClient(env);
  if (!supabase) {
    console.warn('[Issue #296] Supabase not configured, skipping token recording');
    return null;
  }

  try {
    let result: TokenConsumptionResult | null = null;

    if (user.authMethod === 'supabase') {
      // Supabaseユーザー: UUIDで記録
      const { data, error } = await supabase.rpc('record_token_consumption', {
        p_user_id: user.userId,
        p_tokens: totalTokens
      });

      if (error) {
        console.error('[Issue #296] Token recording RPC error:', error);
        return null;
      }

      // カラム名は out_* プレフィックス付き（SQL関数の曖昧さ回避のため）
      if (data && Array.isArray(data) && data.length > 0) {
        result = {
          year_month: data[0].out_year_month,
          tokens_used: Number(data[0].out_tokens_used)
        };
      }
    } else {
      // Patreon/JWTユーザー: Patreon IDで記録
      const { data, error } = await supabase.rpc('record_token_consumption_by_patreon', {
        p_patreon_user_id: user.userId,
        p_tokens: totalTokens
      });

      if (error) {
        console.error('[Issue #296] Token recording (Patreon) RPC error:', error);
        return null;
      }

      // カラム名は out_* プレフィックス付き（SQL関数の曖昧さ回避のため）
      if (data && Array.isArray(data) && data.length > 0) {
        result = {
          year_month: data[0].out_year_month,
          tokens_used: Number(data[0].out_tokens_used)
        };
      }
    }

    if (result) {
      console.log(`[Issue #296] Token recorded: userId=${user.userId.substring(0, 8)}..., month=${result.year_month}, used=${result.tokens_used}, added=${totalTokens}`);
    }

    return result;
  } catch (error) {
    // 記録失敗は翻訳結果に影響させない
    console.error('[Issue #296] Token recording failed:', error);
    return null;
  }
}

// ============================================
// [Issue #296] クォータ超過チェック
// ============================================

/** クォータチェック結果 */
interface QuotaCheckResult {
  exceeded: boolean;
  tokensUsed: number;
  tokensLimit: number;
  yearMonth: string;
}

/** キャッシュされたクォータ状態 */
interface CachedQuotaState {
  tokensUsed: number;
  cachedAt: number;
}

/**
 * [Issue #296] クォータ超過をチェック（Cache API活用）
 * @param env 環境変数
 * @param user 認証済みユーザー
 * @returns クォータチェック結果
 */
async function checkQuotaExceeded(
  env: TranslateEnv,
  user: AuthenticatedUser
): Promise<QuotaCheckResult> {
  const now = new Date();
  const yearMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
  const tokensLimit = PLAN_TOKEN_LIMITS[user.plan] || 0;

  // Freeプランは上限0（ボーナストークンのみで利用）
  if (tokensLimit === 0) {
    return {
      exceeded: true,
      tokensUsed: 0,
      tokensLimit: 0,
      yearMonth,
    };
  }

  // 1. キャッシュを確認（Cache API使用）
  const cacheKey = `quota:${user.userId}:${yearMonth}`;
  const cachedQuota = await getQuotaCache(cacheKey);
  if (cachedQuota !== null) {
    console.log(`[Issue #296] Quota cache hit: userId=${user.userId.substring(0, 8)}..., used=${cachedQuota}`);
    return {
      exceeded: cachedQuota >= tokensLimit,
      tokensUsed: cachedQuota,
      tokensLimit,
      yearMonth,
    };
  }

  // 2. Supabaseから現在の使用量を取得
  const supabase = getSupabaseClient(env);
  if (!supabase) {
    console.warn('[Issue #296] Supabase not configured, allowing request');
    return {
      exceeded: false,
      tokensUsed: 0,
      tokensLimit,
      yearMonth,
    };
  }

  try {
    let tokensUsed = 0;

    if (user.authMethod === 'supabase') {
      // Supabaseユーザー: UUIDで検索
      const { data, error } = await supabase
        .from('token_usage')
        .select('tokens_used')
        .eq('user_id', user.userId)
        .eq('year_month', yearMonth)
        .single();

      if (!error && data) {
        tokensUsed = Number(data.tokens_used);
      }
    } else {
      // Patreon/JWTユーザー: profiles経由でuser_idを取得
      const { data: profile, error: profileError } = await supabase
        .from('profiles')
        .select('id')
        .eq('patreon_user_id', user.userId)
        .single();

      if (!profileError && profile) {
        const { data, error } = await supabase
          .from('token_usage')
          .select('tokens_used')
          .eq('user_id', profile.id)
          .eq('year_month', yearMonth)
          .single();

        if (!error && data) {
          tokensUsed = Number(data.tokens_used);
        }
      }
    }

    // 3. 結果をキャッシュに保存（5分TTL）
    await saveQuotaCache(cacheKey, tokensUsed);

    console.log(`[Issue #296] Quota check: userId=${user.userId.substring(0, 8)}..., used=${tokensUsed}, limit=${tokensLimit}, exceeded=${tokensUsed >= tokensLimit}`);

    return {
      exceeded: tokensUsed >= tokensLimit,
      tokensUsed,
      tokensLimit,
      yearMonth,
    };
  } catch (error) {
    console.error('[Issue #296] Quota check failed:', error);
    // エラー時は翻訳を許可（可用性優先）
    return {
      exceeded: false,
      tokensUsed: 0,
      tokensLimit,
      yearMonth,
    };
  }
}

/** [Issue #296] クォータキャッシュを読み取り（Cache API） */
async function getQuotaCache(cacheKey: string): Promise<number | null> {
  try {
    const cache = caches.default;
    const cacheRequest = new Request(getCacheUrl(cacheKey));
    const cachedResponse = await cache.match(cacheRequest);

    if (cachedResponse) {
      const data = await cachedResponse.json<CachedQuotaState>();
      return data.tokensUsed;
    }
    return null;
  } catch {
    return null;
  }
}

/** [Issue #296] クォータ状態をキャッシュに保存（Cache API） */
async function saveQuotaCache(cacheKey: string, tokensUsed: number): Promise<void> {
  try {
    const cache = caches.default;
    const cacheRequest = new Request(getCacheUrl(cacheKey));
    const state: CachedQuotaState = {
      tokensUsed,
      cachedAt: Date.now(),
    };
    const cacheResponse = new Response(JSON.stringify(state), {
      headers: {
        'Content-Type': 'application/json',
        'Cache-Control': `max-age=${QUOTA_CACHE_TTL_SECONDS}`,
      },
    });

    await cache.put(cacheRequest, cacheResponse);
    console.log(`[Issue #296] Quota cached: key=${cacheKey.substring(0, 16)}..., used=${tokensUsed}`);
  } catch (error) {
    console.warn('[Issue #296] Quota cache save failed:', error);
  }
}

/** [Issue #296] クォータキャッシュを無効化（トークン消費後） */
async function invalidateQuotaCache(userId: string): Promise<void> {
  try {
    const now = new Date();
    const yearMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
    const cacheKey = `quota:${userId}:${yearMonth}`;

    const cache = caches.default;
    const cacheRequest = new Request(getCacheUrl(cacheKey));
    await cache.delete(cacheRequest);

    console.log(`[Issue #296] Quota cache invalidated: userId=${userId.substring(0, 8)}...`);
  } catch {
    // キャッシュ削除失敗は無視
  }
}

/** ユーザーのボーナストークン残高を取得 */
async function getBonusTokensRemaining(supabase: SupabaseClient, userId: string): Promise<number> {
  try {
    const { data, error } = await supabase.rpc('get_bonus_tokens_for_user', {
      p_user_id: userId
    });

    if (error || !Array.isArray(data)) {
      console.error('[Issue #280+#281] Bonus tokens RPC error:', error);
      return 0;
    }

    // 有効なボーナス（未期限切れで残高あり）の合計を計算
    return data
      .filter((b: { is_expired: boolean; remaining_tokens: number }) =>
        !b.is_expired && b.remaining_tokens > 0)
      .reduce((sum: number, b: { remaining_tokens: number }) => sum + b.remaining_tokens, 0);
  } catch (error) {
    console.error('[Issue #280+#281] Bonus tokens check failed:', error);
    return 0;
  }
}

/**
 * [Issue #280+#281] 統合認証
 * [Issue #287] JWT認証を最優先に追加
 * 1. Baketa JWT認証を試行（最優先）
 * 2. キャッシュを確認
 * 3. Patreonセッション（KV）を試行
 * 4. 失敗したらSupabase JWT認証を試行
 * 5. ボーナストークンの有無も確認
 * 6. 結果をキャッシュに保存
 */
async function authenticateUser(
  env: TranslateEnv,
  sessionToken: string
): Promise<AuthenticatedUser | null> {
  // [Issue #287] 1. Baketa JWT認証を試行（最優先）
  const jwtClaims = await validateJwtToken(env, sessionToken);
  if (jwtClaims && jwtClaims.sub) {
    console.log(`[Issue #287] JWT auth success: userId=${jwtClaims.sub.substring(0, 8)}..., plan=${jwtClaims.plan}`);
    return {
      userId: jwtClaims.sub,
      plan: jwtClaims.plan,
      hasBonusTokens: jwtClaims.hasBonusTokens,
      authMethod: 'jwt',
    };
  }

  // トークンのハッシュをキーに使用（セキュリティ対策）
  // [Issue #295] v2: Patreonプラン紐づけ対応によりキャッシュ形式変更
  const tokenHash = await hashToken(sessionToken);
  const cacheKey = `auth:v2:${tokenHash}`;

  // 2. キャッシュを確認（[Issue #286] Cache APIを使用 - KV制限回避）
  const cachedAuth = await getAuthCache(cacheKey);
  if (cachedAuth) {
    // [Issue #296] Patreonユーザーの場合、メンバーシップKVから最新プランを確認
    // handleLicenseStatusで更新されたプランがAuth Cacheに反映されていない場合がある
    if (cachedAuth.authMethod === 'patreon') {
      const latestMembership = await getCachedMembership(env, cachedAuth.userId);
      // メンバーシップKVが無効化されている場合（Webhook後）、Auth Cacheを削除して再認証
      if (!latestMembership) {
        console.log(`[Issue #296] Membership KV invalidated, re-authenticating: userId=${cachedAuth.userId.substring(0, 8)}...`);
        await deleteAuthCache(cacheKey);
        // 再認証へフォールスルー
      } else if (latestMembership.membership.plan !== cachedAuth.plan) {
        console.log(`[Issue #296] Plan mismatch detected: cached=${cachedAuth.plan}, latest=${latestMembership.membership.plan}`);
        const updatedAuth: AuthenticatedUser = {
          ...cachedAuth,
          plan: latestMembership.membership.plan,
        };
        await saveAuthCache(cacheKey, updatedAuth);
        return updatedAuth;
      } else {
        // プランが一致 → キャッシュを使用
        console.log(`[Issue #286] Auth cache hit (Cache API): userId=${cachedAuth.userId.substring(0, 8)}..., plan=${cachedAuth.plan}`);
        return cachedAuth;
      }
    }
    // [Issue #296] Freeプランのキャッシュはスキップして再認証
    // Patreon連携後にキャッシュが古いままの場合があるため
    if (cachedAuth.plan === PLAN.FREE) {
      console.log(`[Issue #296] Skipping Free plan cache, re-authenticating: userId=${cachedAuth.userId.substring(0, 8)}...`);
      // キャッシュを削除して再認証へ
      await deleteAuthCache(cacheKey);
    } else {
      console.log(`[Issue #286] Auth cache hit (Cache API): userId=${cachedAuth.userId.substring(0, 8)}..., plan=${cachedAuth.plan}`);
      return cachedAuth;
    }
  }

  // 3. Patreonセッション（KV）を試行
  const patreonSession = await getSession(env, sessionToken);
  if (patreonSession && Date.now() <= patreonSession.expiresAt) {
    let cachedMembership = await getCachedMembership(env, patreonSession.userId);

    // [Issue #296] メンバーシップKVがnullの場合、Patreon APIから最新情報を取得
    if (!cachedMembership) {
      console.log(`[Issue #296] Patreon session: Membership KV is null, fetching from Patreon API: userId=${patreonSession.userId}`);
      const freshMembership = await fetchAndCachePatreonMembership(env, patreonSession.userId);
      if (freshMembership) {
        cachedMembership = { membership: freshMembership, cachedAt: Date.now() };
      }
    }

    if (cachedMembership) {
      const result: AuthenticatedUser = {
        userId: patreonSession.userId,
        plan: cachedMembership.membership.plan,
        hasBonusTokens: false, // Patreonユーザーはボーナス不要（プランで判定）
        authMethod: 'patreon'
      };
      // キャッシュに保存（[Issue #286] Cache API使用）
      await saveAuthCache(cacheKey, result);
      return result;
    }
  }

  // 4. Supabase JWT認証を試行
  const supabase = getSupabaseClient(env);
  if (!supabase) {
    console.log('[Issue #280+#281] Supabase not configured, skipping JWT auth');
    return null;
  }

  try {
    const { data: { user }, error } = await supabase.auth.getUser(sessionToken);
    if (error || !user) {
      console.log('[Issue #280+#281] Supabase JWT validation failed:', error?.message);
      return null;
    }

    // 4a. ボーナストークン残高を確認
    const bonusRemaining = await getBonusTokensRemaining(supabase, user.id);
    const hasBonusTokens = bonusRemaining > 0;

    // [Issue #295] 4b. profiles.patreon_user_id から紐づけられたPatreonプランを取得
    let plan: PlanType = PLAN.FREE;
    let resolvedUserId = user.id;
    let authMethod: 'supabase' | 'patreon' = 'supabase';

    const { data: profile, error: profileError } = await supabase
      .from('profiles')
      .select('patreon_user_id')
      .eq('id', user.id)
      .single();

    if (!profileError && profile?.patreon_user_id) {
      // Patreon紐づけあり → KVからメンバーシップ情報を取得
      const patreonUserId = profile.patreon_user_id;
      console.log(`[Issue #295] Supabase user linked to Patreon: supabaseId=${user.id.substring(0, 8)}..., patreonId=${patreonUserId}`);

      let cachedMembership = await getCachedMembership(env, patreonUserId);

      // [Issue #296] メンバーシップKVがnullの場合、Patreon APIから最新情報を取得
      if (!cachedMembership) {
        console.log(`[Issue #296] Membership KV is null, fetching from Patreon API: patreonId=${patreonUserId}`);
        const freshMembership = await fetchAndCachePatreonMembership(env, patreonUserId);
        if (freshMembership) {
          // 取得成功 → キャッシュ済みなのでCachedMembership形式に変換
          cachedMembership = { membership: freshMembership, cachedAt: Date.now() };
        }
      }

      if (cachedMembership) {
        plan = cachedMembership.membership.plan;
        resolvedUserId = patreonUserId; // トークン記録用にPatreon IDを使用
        authMethod = 'patreon'; // 認証方法をpatreonに変更（トークン記録のため）
        console.log(`[Issue #295] Patreon plan resolved: userId=${patreonUserId}, plan=${plan}`);
      } else {
        console.log(`[Issue #296] Patreon membership unavailable, falling back to Free: patreonId=${patreonUserId}`);
      }
    }

    console.log(`[Issue #280+#281] Supabase auth success: userId=${user.id.substring(0, 8)}..., plan=${plan}, bonusTokens=${bonusRemaining}`);

    const result: AuthenticatedUser = {
      userId: resolvedUserId,
      plan,
      hasBonusTokens,
      authMethod
    };

    // 5. キャッシュに保存（[Issue #286] Cache API使用）
    await saveAuthCache(cacheKey, result);
    return result;
  } catch (error) {
    console.error('[Issue #280+#281] Supabase auth error:', error);
    return null;
  }
}

/** [Issue #280+#281] トークンをハッシュ化（キャッシュキー用） */
async function hashToken(token: string): Promise<string> {
  const encoder = new TextEncoder();
  const data = encoder.encode(token);
  const hashBuffer = await crypto.subtle.digest('SHA-256', data);
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  return hashArray.slice(0, 16).map(b => b.toString(16).padStart(2, '0')).join('');
}

// ============================================
// [Issue #286] Cache API ヘルパー関数
// KV制限を回避するためCache APIを使用（無制限）
// ============================================

/** Cache APIのキーとなるURLを生成 */
function getCacheUrl(cacheKey: string): string {
  return `https://baketa-auth-cache.internal/${cacheKey}`;
}

/** [Issue #286] 認証キャッシュを読み取り（Cache API） */
async function getAuthCache(cacheKey: string): Promise<AuthenticatedUser | null> {
  try {
    const cache = caches.default;
    const cacheRequest = new Request(getCacheUrl(cacheKey));
    const cachedResponse = await cache.match(cacheRequest);

    if (cachedResponse) {
      const data = await cachedResponse.json<AuthenticatedUser>();
      return data;
    }
    return null;
  } catch {
    return null;
  }
}

/** [Issue #286] 認証結果をキャッシュに保存（Cache API） */
async function saveAuthCache(cacheKey: string, auth: AuthenticatedUser): Promise<void> {
  try {
    const cache = caches.default;
    const cacheRequest = new Request(getCacheUrl(cacheKey));
    const cacheResponse = new Response(JSON.stringify(auth), {
      headers: {
        'Content-Type': 'application/json',
        'Cache-Control': `max-age=${AUTH_CACHE_TTL_SECONDS}`,
      },
    });

    await cache.put(cacheRequest, cacheResponse);
    console.log(`[Issue #286] Auth cached (Cache API): key=${cacheKey.substring(0, 12)}...`);
  } catch (error) {
    // キャッシュ保存エラーは無視（翻訳処理を続行）
    console.warn('[Issue #286] Auth cache save failed:', error);
  }
}

/** [Issue #296] 認証キャッシュを削除（Cache API） */
async function deleteAuthCache(cacheKey: string): Promise<void> {
  try {
    const cache = caches.default;
    const cacheRequest = new Request(getCacheUrl(cacheKey));
    await cache.delete(cacheRequest);
    console.log(`[Issue #296] Auth cache deleted: key=${cacheKey.substring(0, 12)}...`);
  } catch (error) {
    console.warn('[Issue #296] Auth cache delete failed:', error);
  }
}

// --------------------------------------------
// 既存ヘルパー関数
// --------------------------------------------

function corsHeaders(origin: string, allowedOrigins: string): Record<string, string> {
  const allowed = allowedOrigins.split(',').map(o => o.trim());
  const isAllowed = allowed.includes('*') || allowed.includes(origin);

  return {
    'Access-Control-Allow-Origin': isAllowed ? origin : (allowed[0] || ''),
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type, Authorization, X-API-Key',
    'Access-Control-Max-Age': '86400',
  };
}

function errorResponse(
  requestId: string,
  code: string,
  message: string,
  isRetryable: boolean,
  status: number,
  origin: string,
  allowedOrigins: string,
  processingTimeMs?: number
): Response {
  const response: TranslateResponse = {
    success: false,
    request_id: requestId,
    processing_time_ms: processingTimeMs,
    error: { code, message, is_retryable: isRetryable },
  };
  return new Response(JSON.stringify(response), {
    status,
    headers: { 'Content-Type': 'application/json', ...corsHeaders(origin, allowedOrigins) },
  });
}

function successResponse(
  data: TranslateResponse,
  origin: string,
  allowedOrigins: string,
  rateLimitHeaders?: Record<string, string>
): Response {
  return new Response(JSON.stringify(data), {
    status: 200,
    headers: {
      'Content-Type': 'application/json',
      ...corsHeaders(origin, allowedOrigins),
      ...rateLimitHeaders,
    },
  });
}

/**
 * [Issue #296] クォータ超過エラーレスポンス
 * Geminiフィードバック: 既存のerrorResponseと同じJSON構造を使用
 */
function quotaExceededResponse(
  requestId: string,
  quotaCheck: QuotaCheckResult,
  origin: string,
  allowedOrigins: string
): Response {
  const response: TranslateResponse = {
    success: false,
    request_id: requestId,
    error: {
      code: 'QUOTA_EXCEEDED',
      message: `Monthly token limit exceeded: ${quotaCheck.tokensUsed.toLocaleString()} / ${quotaCheck.tokensLimit.toLocaleString()} tokens used`,
      is_retryable: false,
    },
    // [Issue #296] クォータ情報も含める
    monthly_usage: {
      year_month: quotaCheck.yearMonth,
      tokens_used: quotaCheck.tokensUsed,
      tokens_limit: quotaCheck.tokensLimit,
    },
  };

  // 月末リセット日時を計算（翌月1日 00:00:00 UTC）
  const now = new Date();
  const nextMonth = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1, 0, 0, 0));
  const resetTimestamp = Math.floor(nextMonth.getTime() / 1000);

  return new Response(JSON.stringify(response), {
    status: 429, // Too Many Requests（クォータ超過に適切）
    headers: {
      'Content-Type': 'application/json',
      ...corsHeaders(origin, allowedOrigins),
      ...rateLimitHeaders(quotaCheck, resetTimestamp),
    },
  });
}

/**
 * [Issue #296] X-RateLimit-* ヘッダーを生成
 * Geminiフィードバック: クライアント側でクォータ状況を明示
 */
function rateLimitHeaders(
  quotaCheck: QuotaCheckResult,
  resetTimestamp?: number
): Record<string, string> {
  const headers: Record<string, string> = {
    'X-RateLimit-Limit': quotaCheck.tokensLimit.toString(),
    'X-RateLimit-Remaining': Math.max(0, quotaCheck.tokensLimit - quotaCheck.tokensUsed).toString(),
  };

  if (resetTimestamp) {
    headers['X-RateLimit-Reset'] = resetTimestamp.toString();
  }

  return headers;
}

async function getSession(env: TranslateEnv, sessionToken: string): Promise<SessionData | null> {
  try {
    const data = await env.SESSIONS.get<SessionData>(sessionToken, 'json');
    if (data && typeof data.accessToken === 'string' && typeof data.expiresAt === 'number') {
      return data;
    }
    return null;
  } catch {
    return null;
  }
}

async function getCachedMembership(env: TranslateEnv, userId: string): Promise<CachedMembership | null> {
  try {
    const cacheKey = `membership:${userId}`;
    const data = await env.SESSIONS.get<CachedMembership>(cacheKey, 'json');
    if (data && data.membership && typeof data.cachedAt === 'number') {
      if (Date.now() - data.cachedAt < IDENTITY_CACHE_TTL_SECONDS * 1000) {
        return data;
      }
    }
    return null;
  } catch {
    return null;
  }
}

function validateTranslateRequest(body: unknown): { valid: true; data: TranslateRequest } | { valid: false; error: string } {
  if (!body || typeof body !== 'object') {
    return { valid: false, error: 'Invalid request body' };
  }

  const obj = body as Record<string, unknown>;

  if (typeof obj.provider !== 'string' || !['gemini', 'openai'].includes(obj.provider)) {
    return { valid: false, error: 'Invalid or missing provider (must be "gemini" or "openai")' };
  }

  if (typeof obj.image_base64 !== 'string' || !obj.image_base64) {
    return { valid: false, error: 'Missing or invalid image_base64' };
  }

  if (typeof obj.mime_type !== 'string' || !obj.mime_type) {
    return { valid: false, error: 'Missing or invalid mime_type' };
  }

  if (typeof obj.target_language !== 'string' || !obj.target_language) {
    return { valid: false, error: 'Missing or invalid target_language' };
  }

  return {
    valid: true,
    data: {
      provider: obj.provider as 'gemini' | 'openai',
      image_base64: obj.image_base64,
      mime_type: obj.mime_type,
      source_language: typeof obj.source_language === 'string' ? obj.source_language : 'auto',
      target_language: obj.target_language,
      context: typeof obj.context === 'string' ? obj.context : undefined,
      request_id: typeof obj.request_id === 'string' ? obj.request_id : crypto.randomUUID(),
    },
  };
}

/**
 * 画像サイズからトークン数を推定（概算値）
 *
 * 注意: Gemini Visionのトークン数は実際にはピクセル数に基づいて計算されますが、
 * クライアント側では画像のピクセル情報がBase64から直接取得できないため、
 * ファイルサイズからの概算値を使用しています。
 *
 * Gemini Vision: 258トークン（~768px）～ 1032トークン（~3072px）
 */
function estimateImageTokens(base64Length: number): number {
  // Base64は約1.33倍のサイズなので、元のバイト数を推定
  const estimatedBytes = Math.floor(base64Length * 0.75);
  // 1MB = 約500トークンと推定（概算値）
  return Math.max(258, Math.min(1032, Math.floor(estimatedBytes / 2000)));
}

/** [Issue #275] AIレスポンスのパース結果型 */
interface ParsedAiResponse {
  detected_text?: string;
  translated_text?: string;
  detected_language?: string;
  /** [Issue #275] 複数テキスト対応 */
  texts?: Array<{
    original: string;
    translation: string;
    bounding_box?: [number, number, number, number];
  }>;
}

/**
 * AIレスポンスからJSONをパース
 * マークダウンコードブロック（```json...```）を除去してパース
 * [Issue #275] 複数テキスト形式にも対応
 */
function parseAiJsonResponse(textContent: string): ParsedAiResponse | null {
  let jsonText = textContent.trim();

  // マークダウンコードブロックを除去
  if (jsonText.startsWith('```json')) {
    jsonText = jsonText.slice(7);
  } else if (jsonText.startsWith('```')) {
    jsonText = jsonText.slice(3);
  }
  if (jsonText.endsWith('```')) {
    jsonText = jsonText.slice(0, -3);
  }
  jsonText = jsonText.trim();

  try {
    const parsed = JSON.parse(jsonText);

    // [Issue #275] 新形式（texts配列）の場合、後方互換性のためdetected_text/translated_textも設定
    if (parsed.texts && Array.isArray(parsed.texts) && parsed.texts.length > 0) {
      const firstText = parsed.texts[0];
      return {
        detected_text: firstText.original || '',
        translated_text: firstText.translation || '',
        detected_language: parsed.detected_language,
        texts: parsed.texts,
      };
    }

    // 旧形式の場合はそのまま返す
    return parsed;
  } catch {
    return null;
  }
}

// ============================================
// Gemini API 呼び出し
// ============================================

async function translateWithGemini(
  request: TranslateRequest,
  apiKey: string,
  modelName: string = DEFAULT_GEMINI_MODEL
): Promise<{
  success: boolean;
  detectedText?: string;
  translatedText?: string;
  detectedLanguage?: string;
  /** [Issue #275] 複数テキスト対応 */
  texts?: Array<{ original: string; translation: string; bounding_box?: [number, number, number, number] }>;
  tokenUsage?: { input: number; output: number; image: number };
  error?: { code: string; message: string; isRetryable: boolean };
}> {
  const contextHint = request.context ? `\nContext: This is from a ${request.context}.` : '';
  const sourceHint = request.source_language !== 'auto'
    ? `The source language is ${request.source_language}.`
    : 'Detect the source language.';

  // [Issue #275] 複数テキスト+BoundingBox形式のプロンプト（DirectGeminiImageTranslatorと同等）
  const prompt = `You are a game localization expert. Detect ALL visible text in this image and translate it to ${request.target_language}.${contextHint}

## Translation Guidelines
- Use natural, fluent expressions in ${request.target_language}, not literal translations
- Maintain appropriate tone for game UI and dialog
- Keep proper nouns (character names, place names) as-is or use standard transliteration
- Choose appropriate formality level based on context
- ${sourceHint}

## Output Format
Include ALL detected text items with their bounding box coordinates.
Bounding boxes use normalized 0-1000 scale coordinates in [y_min, x_min, y_max, x_max] order.

Response format (JSON only, no markdown):
{
  "texts": [
    {
      "original": "original text 1",
      "translation": "translated text 1",
      "bounding_box": [y_min, x_min, y_max, x_max]
    },
    {
      "original": "original text 2",
      "translation": "translated text 2",
      "bounding_box": [y_min, x_min, y_max, x_max]
    }
  ],
  "detected_language": "ISO 639-1 code (e.g., ja, en, ko)"
}

If no text is visible, respond with:
{
  "texts": [],
  "detected_language": ""
}`;

  const geminiRequest: GeminiRequest = {
    contents: [{
      parts: [
        { text: prompt },
        {
          inlineData: {
            mimeType: request.mime_type,
            data: request.image_base64,
          },
        },
      ],
    }],
    generationConfig: {
      temperature: 0.1,
      maxOutputTokens: 2048, // [Issue #275] 複数テキスト対応のため増加
    },
  };

  const url = `${GEMINI_API_BASE}/models/${modelName}:generateContent?key=${apiKey}`;

  try {
    const response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(geminiRequest),
      signal: AbortSignal.timeout(API_TIMEOUT_MS),
    });

    if (!response.ok) {
      const errorBody = await response.text();
      console.error(`Gemini API error: status=${response.status}, body=${errorBody}`);

      if (response.status === 429) {
        return {
          success: false,
          error: { code: 'RATE_LIMITED', message: 'Gemini API rate limit exceeded', isRetryable: true },
        };
      }

      if (response.status === 401 || response.status === 403) {
        return {
          success: false,
          error: { code: 'API_ERROR', message: 'Gemini API authentication failed', isRetryable: false },
        };
      }

      return {
        success: false,
        error: { code: 'API_ERROR', message: `Gemini API error: ${response.status}`, isRetryable: response.status >= 500 },
      };
    }

    const geminiResponse = await response.json() as GeminiResponse;

    if (geminiResponse.error) {
      return {
        success: false,
        error: { code: 'API_ERROR', message: geminiResponse.error.message, isRetryable: false },
      };
    }

    const textContent = geminiResponse.candidates?.[0]?.content?.parts?.[0]?.text;
    if (!textContent) {
      return {
        success: false,
        error: { code: 'API_ERROR', message: 'No content in Gemini response', isRetryable: false },
      };
    }

    // JSONをパース
    const parsed = parseAiJsonResponse(textContent);
    if (!parsed) {
      console.error(`Failed to parse Gemini response as JSON: ${textContent}`);
      return {
        success: false,
        error: { code: 'API_ERROR', message: 'Invalid JSON response from Gemini', isRetryable: false },
      };
    }

    const usage = geminiResponse.usageMetadata;

    return {
      success: true,
      detectedText: parsed.detected_text || '',
      translatedText: parsed.translated_text || '',
      detectedLanguage: parsed.detected_language,
      // [Issue #275] 複数テキスト対応
      texts: parsed.texts,
      tokenUsage: {
        input: usage?.promptTokenCount || 0,
        output: usage?.candidatesTokenCount || 0,
        image: estimateImageTokens(request.image_base64.length),
      },
    };
  } catch (error) {
    console.error('Gemini API call failed:', error);

    // タイムアウトエラーの判定
    if (error instanceof Error && error.name === 'TimeoutError') {
      return {
        success: false,
        error: { code: 'TIMEOUT', message: `Gemini API request timed out after ${API_TIMEOUT_MS}ms`, isRetryable: true },
      };
    }

    return {
      success: false,
      error: { code: 'NETWORK_ERROR', message: 'Failed to connect to Gemini API', isRetryable: true },
    };
  }
}

// ============================================
// OpenAI API 呼び出し
// ============================================

async function translateWithOpenAI(
  request: TranslateRequest,
  apiKey: string,
  modelName: string = DEFAULT_OPENAI_MODEL
): Promise<{
  success: boolean;
  detectedText?: string;
  translatedText?: string;
  detectedLanguage?: string;
  /** [Issue #275] 複数テキスト対応 */
  texts?: Array<{ original: string; translation: string; bounding_box?: [number, number, number, number] }>;
  tokenUsage?: { input: number; output: number; image: number };
  error?: { code: string; message: string; isRetryable: boolean };
}> {
  const contextHint = request.context ? `\nContext: This is from a ${request.context}.` : '';
  const sourceHint = request.source_language !== 'auto'
    ? `The source language is ${request.source_language}.`
    : 'Detect the source language.';

  // [Issue #275] 複数テキスト+BoundingBox形式のプロンプト
  const systemPrompt = `You are a game localization expert. Always respond with valid JSON only, no markdown formatting.`;

  const userPrompt = `${contextHint}

Task: Detect ALL visible text in this image and translate it to ${request.target_language}.
${sourceHint}

## Translation Guidelines
- Use natural, fluent expressions in ${request.target_language}, not literal translations
- Maintain appropriate tone for game UI and dialog
- Keep proper nouns (character names, place names) as-is or use standard transliteration
- Choose appropriate formality level based on context

## Output Format
Include ALL detected text items with their bounding box coordinates.
Bounding boxes use normalized 0-1000 scale coordinates in [y_min, x_min, y_max, x_max] order.

Response format (JSON only, no markdown):
{
  "texts": [
    {
      "original": "original text 1",
      "translation": "translated text 1",
      "bounding_box": [y_min, x_min, y_max, x_max]
    }
  ],
  "detected_language": "ISO 639-1 code (e.g., ja, en, ko)"
}

If no text is visible, respond with:
{
  "texts": [],
  "detected_language": ""
}`;

  const openaiRequest: OpenAIRequest = {
    model: modelName,
    messages: [
      { role: 'system', content: systemPrompt },
      {
        role: 'user',
        content: [
          { type: 'text', text: userPrompt },
          {
            type: 'image_url',
            image_url: {
              url: `data:${request.mime_type};base64,${request.image_base64}`,
              detail: 'high',
            },
          },
        ],
      },
    ],
    max_tokens: 2048, // [Issue #275] 複数テキスト対応のため増加
    temperature: 0.1,
  };

  const url = `${OPENAI_API_BASE}/chat/completions`;

  try {
    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${apiKey}`,
      },
      body: JSON.stringify(openaiRequest),
      signal: AbortSignal.timeout(API_TIMEOUT_MS),
    });

    if (!response.ok) {
      const errorBody = await response.text();
      console.error(`OpenAI API error: status=${response.status}, body=${errorBody}`);

      if (response.status === 429) {
        return {
          success: false,
          error: { code: 'RATE_LIMITED', message: 'OpenAI API rate limit exceeded', isRetryable: true },
        };
      }

      if (response.status === 401 || response.status === 403) {
        return {
          success: false,
          error: { code: 'API_ERROR', message: 'OpenAI API authentication failed', isRetryable: false },
        };
      }

      if (response.status === 402) {
        return {
          success: false,
          error: { code: 'PAYMENT_REQUIRED', message: 'OpenAI API credit exhausted', isRetryable: false },
        };
      }

      return {
        success: false,
        error: { code: 'API_ERROR', message: `OpenAI API error: ${response.status}`, isRetryable: response.status >= 500 },
      };
    }

    const openaiResponse = await response.json() as OpenAIResponse;

    if (openaiResponse.error) {
      return {
        success: false,
        error: { code: 'API_ERROR', message: openaiResponse.error.message, isRetryable: false },
      };
    }

    const textContent = openaiResponse.choices?.[0]?.message?.content;
    if (!textContent) {
      return {
        success: false,
        error: { code: 'API_ERROR', message: 'No content in OpenAI response', isRetryable: false },
      };
    }

    // JSONをパース
    const parsed = parseAiJsonResponse(textContent);
    if (!parsed) {
      console.error(`Failed to parse OpenAI response as JSON: ${textContent}`);
      return {
        success: false,
        error: { code: 'API_ERROR', message: 'Invalid JSON response from OpenAI', isRetryable: false },
      };
    }

    const usage = openaiResponse.usage;

    return {
      success: true,
      detectedText: parsed.detected_text || '',
      translatedText: parsed.translated_text || '',
      detectedLanguage: parsed.detected_language,
      // [Issue #275] 複数テキスト対応
      texts: parsed.texts,
      tokenUsage: {
        input: usage?.prompt_tokens || 0,
        output: usage?.completion_tokens || 0,
        image: estimateImageTokens(request.image_base64.length),
      },
    };
  } catch (error) {
    console.error('OpenAI API call failed:', error);

    // タイムアウトエラーの判定
    if (error instanceof Error && error.name === 'TimeoutError') {
      return {
        success: false,
        error: { code: 'TIMEOUT', message: `OpenAI API request timed out after ${API_TIMEOUT_MS}ms`, isRetryable: true },
      };
    }

    return {
      success: false,
      error: { code: 'NETWORK_ERROR', message: 'Failed to connect to OpenAI API', isRetryable: true },
    };
  }
}

// ============================================
// メインハンドラ
// ============================================

export async function handleTranslate(
  request: Request,
  env: TranslateEnv,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  const startTime = Date.now();
  const defaultRequestId = crypto.randomUUID();

  if (request.method !== 'POST') {
    return errorResponse(defaultRequestId, 'METHOD_NOT_ALLOWED', 'Method not allowed', false, 405, origin, allowedOrigins);
  }

  // セッショントークン取得
  const authHeader = request.headers.get('Authorization');
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return errorResponse(defaultRequestId, 'SESSION_INVALID', 'Missing or invalid Authorization header', false, 401, origin, allowedOrigins);
  }

  const sessionToken = authHeader.substring(7);

  // [Issue #280+#281] 統合認証（Patreonセッション or Supabase JWT）
  const authenticatedUser = await authenticateUser(env, sessionToken);
  if (!authenticatedUser) {
    return errorResponse(defaultRequestId, 'SESSION_INVALID', 'Invalid or expired session', false, 401, origin, allowedOrigins);
  }

  // [Issue #280+#281] 利用権限チェック: 有料プラン or ボーナストークン所有者
  const isPaidPlan = ALLOWED_PLANS.includes(authenticatedUser.plan);
  if (!isPaidPlan && !authenticatedUser.hasBonusTokens) {
    return errorResponse(
      defaultRequestId,
      'PLAN_NOT_SUPPORTED',
      `Cloud AI translation requires a paid plan (Pro/Premium/Ultimate) or bonus tokens. Current plan: ${authenticatedUser.plan}`,
      false,
      403,
      origin,
      allowedOrigins
    );
  }

  // [Issue #296] クォータ超過チェック（翻訳前に実行）
  const quotaCheck = await checkQuotaExceeded(env, authenticatedUser);
  if (quotaCheck.exceeded && !authenticatedUser.hasBonusTokens) {
    console.log(`[Issue #296] Quota exceeded: userId=${authenticatedUser.userId.substring(0, 8)}..., used=${quotaCheck.tokensUsed}, limit=${quotaCheck.tokensLimit}`);
    return quotaExceededResponse(
      defaultRequestId,
      quotaCheck,
      origin,
      allowedOrigins
    );
  }

  const userId = authenticatedUser.userId;
  const plan = authenticatedUser.plan;

  // リクエストボディ検証
  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return errorResponse(defaultRequestId, 'VALIDATION_ERROR', 'Invalid JSON body', false, 400, origin, allowedOrigins);
  }

  const validation = validateTranslateRequest(body);
  if (!validation.valid) {
    return errorResponse(defaultRequestId, 'VALIDATION_ERROR', validation.error, false, 400, origin, allowedOrigins);
  }

  const translateRequest = validation.data;
  const requestId = translateRequest.request_id || defaultRequestId;

  // プロバイダー別処理
  if (translateRequest.provider === 'gemini') {
    if (!env.GEMINI_API_KEY) {
      return errorResponse(requestId, 'NOT_IMPLEMENTED', 'Gemini API is not configured', false, 503, origin, allowedOrigins);
    }

    const modelName = env.GEMINI_MODEL || DEFAULT_GEMINI_MODEL;
    const result = await translateWithGemini(translateRequest, env.GEMINI_API_KEY, modelName);
    const processingTimeMs = Date.now() - startTime;

    if (!result.success) {
      return errorResponse(
        requestId,
        result.error!.code,
        result.error!.message,
        result.error!.isRetryable,
        result.error!.code === 'RATE_LIMITED' ? 429 : 500,
        origin,
        allowedOrigins,
        processingTimeMs
      );
    }

    // [Issue #296] トークン消費を記録（翻訳結果に影響させない）
    const totalTokens = (result.tokenUsage?.input || 0) + (result.tokenUsage?.output || 0);
    const tokenRecord = await recordTokenConsumption(env, authenticatedUser, totalTokens);

    // [Issue #296] トークン消費後にクォータキャッシュを無効化
    if (tokenRecord) {
      await invalidateQuotaCache(authenticatedUser.userId);
    }

    // [Issue #296] 更新後のクォータ情報でX-RateLimitヘッダーを生成
    const updatedQuota: QuotaCheckResult = {
      exceeded: tokenRecord ? tokenRecord.tokens_used >= quotaCheck.tokensLimit : quotaCheck.exceeded,
      tokensUsed: tokenRecord?.tokens_used ?? quotaCheck.tokensUsed,
      tokensLimit: quotaCheck.tokensLimit,
      yearMonth: tokenRecord?.year_month ?? quotaCheck.yearMonth,
    };

    const response: TranslateResponse = {
      success: true,
      request_id: requestId,
      detected_text: result.detectedText,
      translated_text: result.translatedText,
      detected_language: result.detectedLanguage,
      provider_id: 'gemini',
      token_usage: result.tokenUsage ? {
        input_tokens: result.tokenUsage.input,
        output_tokens: result.tokenUsage.output,
        image_tokens: result.tokenUsage.image,
      } : undefined,
      processing_time_ms: processingTimeMs,
      // [Issue #275] 複数テキスト対応
      texts: result.texts,
      // [Issue #296] 月間使用状況
      monthly_usage: tokenRecord ? {
        year_month: tokenRecord.year_month,
        tokens_used: tokenRecord.tokens_used,
        tokens_limit: PLAN_TOKEN_LIMITS[authenticatedUser.plan] || 0,
      } : undefined,
    };

    console.log(`Translate success: requestId=${requestId}, userId=${userId}, plan=${plan}, authMethod=${authenticatedUser.authMethod}, provider=gemini, texts=${result.texts?.length || 0}, tokens=${totalTokens}, monthly=${tokenRecord?.tokens_used || 'N/A'}`);

    return successResponse(response, origin, allowedOrigins, rateLimitHeaders(updatedQuota));
  }

  if (translateRequest.provider === 'openai') {
    if (!env.OPENAI_API_KEY) {
      return errorResponse(requestId, 'NOT_IMPLEMENTED', 'OpenAI API is not configured', false, 503, origin, allowedOrigins);
    }

    const modelName = env.OPENAI_MODEL || DEFAULT_OPENAI_MODEL;
    const result = await translateWithOpenAI(translateRequest, env.OPENAI_API_KEY, modelName);
    const processingTimeMs = Date.now() - startTime;

    if (!result.success) {
      return errorResponse(
        requestId,
        result.error!.code,
        result.error!.message,
        result.error!.isRetryable,
        result.error!.code === 'RATE_LIMITED' ? 429 : (result.error!.code === 'PAYMENT_REQUIRED' ? 402 : 500),
        origin,
        allowedOrigins,
        processingTimeMs
      );
    }

    // [Issue #296] トークン消費を記録（翻訳結果に影響させない）
    const totalTokens = (result.tokenUsage?.input || 0) + (result.tokenUsage?.output || 0);
    const tokenRecord = await recordTokenConsumption(env, authenticatedUser, totalTokens);

    // [Issue #296] トークン消費後にクォータキャッシュを無効化
    if (tokenRecord) {
      await invalidateQuotaCache(authenticatedUser.userId);
    }

    // [Issue #296] 更新後のクォータ情報でX-RateLimitヘッダーを生成
    const updatedQuota: QuotaCheckResult = {
      exceeded: tokenRecord ? tokenRecord.tokens_used >= quotaCheck.tokensLimit : quotaCheck.exceeded,
      tokensUsed: tokenRecord?.tokens_used ?? quotaCheck.tokensUsed,
      tokensLimit: quotaCheck.tokensLimit,
      yearMonth: tokenRecord?.year_month ?? quotaCheck.yearMonth,
    };

    const response: TranslateResponse = {
      success: true,
      request_id: requestId,
      detected_text: result.detectedText,
      translated_text: result.translatedText,
      detected_language: result.detectedLanguage,
      provider_id: 'openai',
      token_usage: result.tokenUsage ? {
        input_tokens: result.tokenUsage.input,
        output_tokens: result.tokenUsage.output,
        image_tokens: result.tokenUsage.image,
      } : undefined,
      processing_time_ms: processingTimeMs,
      // [Issue #275] 複数テキスト対応
      texts: result.texts,
      // [Issue #296] 月間使用状況
      monthly_usage: tokenRecord ? {
        year_month: tokenRecord.year_month,
        tokens_used: tokenRecord.tokens_used,
        tokens_limit: PLAN_TOKEN_LIMITS[authenticatedUser.plan] || 0,
      } : undefined,
    };

    console.log(`Translate success: requestId=${requestId}, userId=${userId}, plan=${plan}, authMethod=${authenticatedUser.authMethod}, provider=openai, texts=${result.texts?.length || 0}, tokens=${totalTokens}, monthly=${tokenRecord?.tokens_used || 'N/A'}`);

    return successResponse(response, origin, allowedOrigins, rateLimitHeaders(updatedQuota));
  }

  return errorResponse(requestId, 'VALIDATION_ERROR', 'Invalid provider', false, 400, origin, allowedOrigins);
}

// ============================================
// [Issue #296] クォータ状態取得エンドポイント
// ============================================

/**
 * [Issue #296] クォータ状態を取得
 * GET /api/quota/status
 *
 * 認証済みユーザーの現在の月間トークン使用状況を取得
 * 起動時にクライアントがサーバーと同期するために使用
 */
export async function handleQuotaStatus(
  request: Request,
  env: TranslateEnv,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'GET') {
    return new Response(JSON.stringify({
      success: false,
      error: { code: 'METHOD_NOT_ALLOWED', message: 'Method not allowed', is_retryable: false }
    }), {
      status: 405,
      headers: { 'Content-Type': 'application/json', ...corsHeaders(origin, allowedOrigins) }
    });
  }

  // セッショントークン取得
  const authHeader = request.headers.get('Authorization');
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return new Response(JSON.stringify({
      success: false,
      error: { code: 'SESSION_INVALID', message: 'Missing or invalid Authorization header', is_retryable: false }
    }), {
      status: 401,
      headers: { 'Content-Type': 'application/json', ...corsHeaders(origin, allowedOrigins) }
    });
  }

  const sessionToken = authHeader.substring(7);

  // 統合認証
  const authenticatedUser = await authenticateUser(env, sessionToken);
  if (!authenticatedUser) {
    return new Response(JSON.stringify({
      success: false,
      error: { code: 'SESSION_INVALID', message: 'Invalid or expired session', is_retryable: false }
    }), {
      status: 401,
      headers: { 'Content-Type': 'application/json', ...corsHeaders(origin, allowedOrigins) }
    });
  }

  // クォータ状態を取得（キャッシュをスキップして最新状態を取得）
  // 起動時の同期はキャッシュを使わない方が良い
  const supabase = getSupabaseClient(env);
  const now = new Date();
  const yearMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
  const tokensLimit = PLAN_TOKEN_LIMITS[authenticatedUser.plan] || 0;

  let tokensUsed = 0;

  if (supabase) {
    try {
      if (authenticatedUser.authMethod === 'supabase') {
        // Supabaseユーザー: UUIDで検索
        const { data, error } = await supabase
          .from('token_usage')
          .select('tokens_used')
          .eq('user_id', authenticatedUser.userId)
          .eq('year_month', yearMonth)
          .single();

        if (!error && data) {
          tokensUsed = Number(data.tokens_used);
        }
      } else {
        // Patreon/JWTユーザー: profiles経由でuser_idを取得
        const { data: profile, error: profileError } = await supabase
          .from('profiles')
          .select('id')
          .eq('patreon_user_id', authenticatedUser.userId)
          .single();

        if (!profileError && profile) {
          const { data, error } = await supabase
            .from('token_usage')
            .select('tokens_used')
            .eq('user_id', profile.id)
            .eq('year_month', yearMonth)
            .single();

          if (!error && data) {
            tokensUsed = Number(data.tokens_used);
          }
        }
      }
    } catch (error) {
      console.error('[Issue #296] Quota status query failed:', error);
    }
  }

  const isExceeded = tokensLimit > 0 && tokensUsed >= tokensLimit;

  console.log(`[Issue #296] Quota status: userId=${authenticatedUser.userId.substring(0, 8)}..., plan=${authenticatedUser.plan}, used=${tokensUsed}, limit=${tokensLimit}, exceeded=${isExceeded}`);

  // 月末リセット日時を計算
  const nextMonth = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1, 0, 0, 0));
  const resetTimestamp = Math.floor(nextMonth.getTime() / 1000);

  return new Response(JSON.stringify({
    success: true,
    monthly_usage: {
      year_month: yearMonth,
      tokens_used: tokensUsed,
      tokens_limit: tokensLimit,
      is_exceeded: isExceeded,
    },
    plan: authenticatedUser.plan,
    has_bonus_tokens: authenticatedUser.hasBonusTokens,
  }), {
    status: 200,
    headers: {
      'Content-Type': 'application/json',
      ...corsHeaders(origin, allowedOrigins),
      'X-RateLimit-Limit': tokensLimit.toString(),
      'X-RateLimit-Remaining': Math.max(0, tokensLimit - tokensUsed).toString(),
      'X-RateLimit-Reset': resetTimestamp.toString(),
    }
  });
}
