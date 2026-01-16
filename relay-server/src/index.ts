/**
 * Baketa Patreon Relay Server
 * Cloudflare Workers上で動作するPatreon OAuth認証プロキシ
 *
 * 機能:
 * - OAuth認証コードをトークンに交換
 * - リフレッシュトークンによるアクセストークン更新
 * - メンバーシップ情報取得（Tier判定）
 * - セキュアなセッション管理
 * - Webhooks（リアルタイムサブスク変更検知）
 * - レートリミット
 * - シングルデバイス強制（他デバイスでログイン時に元デバイスをログアウト）
 * - Cloud AI翻訳（Gemini API経由）
 * - クラッシュレポート受信（Issue #252）
 * - 同意記録（GDPR/CCPA監査ログ）（Issue #261）
 * - ボーナストークン管理（Issue #280+#281）
 *
 * セキュリティ:
 * - タイミング攻撃対策（timingSafeCompare）
 * - redirect_uri ホワイトリスト検証
 * - 本番環境でのAPI_KEY/WEBHOOK_SECRET必須化
 * - Webhook署名検証（HMAC-MD5）
 * - stateパラメータはクライアント側（C#）で検証
 *   ※ デスクトップアプリのため、OAuthフローはクライアントが開始し、
 *     stateの生成・保存・検証もクライアントで完結。
 *     リレーサーバーはstateを透過的に転送するのみ。
 */

import { handleTranslate, handleQuotaStatus, TranslateEnv } from './translate';
import { handleCrashReport, CrashReportEnv } from './crash-report';
import { createClient, SupabaseClient } from '@supabase/supabase-js';
import { SignJWT, jwtVerify, JWTPayload } from 'jose';

// ============================================
// 定数
// ============================================

const PATREON_API_BASE = 'https://www.patreon.com/api/oauth2';
const PATREON_TOKEN_URL = `${PATREON_API_BASE}/token`;
const PATREON_IDENTITY_URL = `${PATREON_API_BASE}/v2/identity`;
const PATREON_IDENTITY_PARAMS = 'include=memberships.currently_entitled_tiers,memberships.campaign&fields[user]=email,full_name&fields[member]=patron_status,currently_entitled_amount_cents,next_charge_date,campaign_lifetime_support_cents&fields[tier]=title,amount_cents';

const SESSION_TTL_SECONDS = 30 * 24 * 60 * 60; // 30 days
/** [Issue #286] メンバーシップキャッシュTTL（1時間）- 手動同期で即時反映可能 */
const IDENTITY_CACHE_TTL_SECONDS = 60 * 60; // 1 hour (was 5 minutes)
const USER_TOKEN_TTL_SECONDS = 31 * 24 * 60 * 60; // 31 days (トークン集約用)

// レートリミット設定
const RATE_LIMIT_WINDOW_SECONDS = 60; // 1分間
const RATE_LIMIT_MAX_REQUESTS = 60; // 1分間に60リクエストまで
const RATE_LIMIT_WEBHOOK_MAX = 100; // Webhookは1分間に100リクエストまで

/** Tier金額しきい値（円） */
// Issue #257: Pro/Premium/Ultimate 3段階構成に改定
const TIER_AMOUNTS = {
  ULTIMATE: 900,  // $9相当
  PREMIUM: 500,   // $5相当
  PRO: 300,       // $3相当
} as const;

type PlanType = 'Free' | 'Pro' | 'Premium' | 'Ultimate';

/** Patreonリソースタイプ定数 */
const PATREON_RESOURCE_TYPES = {
  USER: 'user',
  MEMBER: 'member',
  TIER: 'tier',
  CAMPAIGN: 'campaign',
} as const;

/** Patron ステータス定数 */
const PATRON_STATUS = {
  ACTIVE: 'active_patron',
  FORMER: 'former_patron',
  DECLINED: 'declined_patron',
} as const;

// ============================================
// Issue #287: JWT認証設定
// ============================================

/** JWTアクセストークンTTL（15分） */
const JWT_ACCESS_TOKEN_TTL_SECONDS = 15 * 60;
/** JWTリフレッシュトークンTTL（30日） */
const JWT_REFRESH_TOKEN_TTL_SECONDS = 30 * 24 * 60 * 60;
/** JWT発行者 */
const JWT_ISSUER = 'https://baketa-relay.suke009.workers.dev';
/** JWTオーディエンス */
const JWT_AUDIENCE = 'baketa-client';

// ============================================
// 型定義
// ============================================

export interface Env {
  PATREON_CLIENT_ID: string;
  PATREON_CLIENT_SECRET: string;
  PATREON_WEBHOOK_SECRET?: string;  // Webhook署名検証用
  ALLOWED_ORIGINS: string;
  ALLOWED_REDIRECT_URIS?: string;
  API_KEY: string;
  ENVIRONMENT?: string;
  SESSIONS: KVNamespace;
  GEMINI_API_KEY?: string;   // Cloud AI翻訳用
  GEMINI_MODEL?: string;     // Geminiモデル名（デフォルト: gemini-2.5-flash-lite）
  OPENAI_API_KEY?: string;   // Cloud AI翻訳用
  OPENAI_MODEL?: string;     // OpenAIモデル名（デフォルト: gpt-4.1-nano）
  SUPABASE_URL?: string;     // プロモーションコード検証用
  SUPABASE_SERVICE_KEY?: string;  // プロモーションコード検証用
  ANALYTICS_API_KEY?: string;  // Issue #269: 使用統計収集用APIキー
  JWT_SECRET?: string;       // Issue #287: JWT署名用シークレット
}

interface SessionData {
  accessToken: string;
  refreshToken: string;
  expiresAt: number;
  userId: string;
}

/** ユーザートークン（シングルデバイス強制用） */
interface UserTokenData {
  accessToken: string;
  refreshToken: string;
  expiresAt: number;
  email: string;
  fullName: string;
  sessionTokens: string[];  // 常に1要素のみ（シングルデバイス強制）
  updatedAt: number;
}

/** キャッシュ済みメンバーシップ情報 */
interface CachedMembership {
  membership: ParsedMembership;
  cachedAt: number;
}

/** レートリミットデータ */
interface RateLimitData {
  count: number;
  windowStart: number;
}

/** Issue #269: 使用統計イベント */
interface UsageEvent {
  session_id: string;
  user_id?: string;
  event_type: string;
  event_data?: Record<string, unknown>;
  schema_version: number;
  app_version: string;
  occurred_at: string;  // ISO 8601
}

/** Issue #280+#281: ボーナストークン同期リクエスト */
interface BonusSyncItem {
  id: string;  // UUID
  used_tokens: number;
}

/** Issue #280+#281: ボーナストークン情報 */
interface BonusTokenInfo {
  id: string;
  source_type: string;
  granted_tokens: number;
  used_tokens: number;
  remaining_tokens: number;
  expires_at: string;
  is_expired: boolean;
}

// ============================================
// Issue #287: JWT認証型定義
// ============================================

/** JWTカスタムクレーム */
interface BaketaJwtClaims extends JWTPayload {
  sub: string;           // Patreon user ID
  plan: PlanType;        // 現在のプラン
  hasBonusTokens: boolean; // ボーナストークン有無
  authMethod: 'patreon'; // 認証方法
}

/** JWTトークンペア */
interface JwtTokenPair {
  accessToken: string;
  refreshToken: string;
  expiresAt: number;     // Unix timestamp
}

/** リフレッシュトークンデータ（KV保存用） */
interface RefreshTokenData {
  userId: string;
  sessionToken: string;  // 元のセッショントークン
  createdAt: number;
  expiresAt: number;
  isUsed: boolean;       // 1回使用で無効化
}

/** JWT認証リクエスト */
interface AuthTokenRequest {
  sessionToken?: string; // Bearerヘッダーから取得することも可
}

/** JWT認証レスポンス */
interface AuthTokenResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;     // 秒
  tokenType: 'Bearer';
}

/** JWTリフレッシュリクエスト */
interface AuthRefreshRequest {
  refreshToken: string;
}

/** Patreon OAuth トークンレスポンス */
interface PatreonTokenResponse {
  access_token: string;
  refresh_token: string;
  expires_in: number;
  scope: string;
  token_type: string;
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

/** パース済みメンバーシップ情報 */
interface ParsedMembership {
  userId: string;
  email: string;
  fullName: string;
  plan: PlanType;
  tierId: string;
  patronStatus: string;
  nextChargeDate: string | null;
  entitledAmountCents: number;
}

/** Patreon Webhookペイロード */
interface PatreonWebhookPayload {
  data: {
    id: string;
    type: string;
    attributes: {
      patron_status?: string;
      currently_entitled_amount_cents?: number;
      last_charge_status?: string;
    };
    relationships?: {
      user?: {
        data: { id: string; type: 'user' };
      };
      campaign?: {
        data: { id: string; type: 'campaign' };
      };
    };
  };
  included?: Array<{
    id: string;
    type: string;
    attributes: Record<string, unknown>;
  }>;
}

/** リクエストボディ検証結果 */
interface ValidationResult<T> {
  success: boolean;
  data?: T;
  error?: string;
}

// ============================================
// セキュリティユーティリティ
// ============================================

/**
 * タイミング攻撃対策の文字列比較
 */
function timingSafeCompare(a: string, b: string): boolean {
  if (a.length !== b.length) {
    let result = 0;
    for (let i = 0; i < Math.max(a.length, b.length); i++) {
      result |= (a.charCodeAt(i % a.length) || 0) ^ (b.charCodeAt(i % b.length) || 0);
    }
    return false;
  }
  let result = 0;
  for (let i = 0; i < a.length; i++) {
    result |= a.charCodeAt(i) ^ b.charCodeAt(i);
  }
  return result === 0;
}

/**
 * セッショントークン生成
 */
function generateSessionToken(): string {
  const array = new Uint8Array(32);
  crypto.getRandomValues(array);
  return Array.from(array, b => b.toString(16).padStart(2, '0')).join('');
}

/**
 * redirect_uri のホワイトリスト検証
 */
function validateRedirectUri(redirectUri: string, env: Env): boolean {
  if (!env.ALLOWED_REDIRECT_URIS) {
    if (env.ENVIRONMENT === 'production') {
      console.error('CRITICAL: ALLOWED_REDIRECT_URIS is not set in production');
      return false;
    }
    console.warn('ALLOWED_REDIRECT_URIS is not set - allowing all redirect URIs (development mode)');
    return true;
  }
  const allowedUris = env.ALLOWED_REDIRECT_URIS.split(',').map(uri => uri.trim());
  return allowedUris.includes(redirectUri);
}

/**
 * Webhook署名検証（HMAC-MD5）
 * PatreonはX-Patreon-SignatureヘッダーでHMAC-MD5署名を送信
 */
async function verifyWebhookSignature(
  payload: string,
  signature: string,
  secret: string
): Promise<boolean> {
  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey(
    'raw',
    encoder.encode(secret),
    { name: 'HMAC', hash: 'MD5' },
    false,
    ['sign']
  );
  const signatureBuffer = await crypto.subtle.sign('HMAC', key, encoder.encode(payload));
  const expectedSignature = Array.from(new Uint8Array(signatureBuffer))
    .map(b => b.toString(16).padStart(2, '0'))
    .join('');
  return timingSafeCompare(expectedSignature, signature.toLowerCase());
}

// ============================================
// レートリミット
// ============================================

/**
 * レートリミットをチェック
 * @returns true if rate limited (should reject), false if allowed
 */
async function checkRateLimit(
  env: Env,
  identifier: string,
  maxRequests: number = RATE_LIMIT_MAX_REQUESTS
): Promise<{ limited: boolean; remaining: number; resetAt: number }> {
  const key = `ratelimit:${identifier}`;
  const now = Date.now();
  const windowStart = Math.floor(now / (RATE_LIMIT_WINDOW_SECONDS * 1000)) * (RATE_LIMIT_WINDOW_SECONDS * 1000);
  const resetAt = windowStart + (RATE_LIMIT_WINDOW_SECONDS * 1000);

  try {
    const data = await env.SESSIONS.get<RateLimitData>(key, 'json');

    if (!data || data.windowStart !== windowStart) {
      // 新しいウィンドウを開始
      await env.SESSIONS.put(key, JSON.stringify({ count: 1, windowStart }), {
        expirationTtl: RATE_LIMIT_WINDOW_SECONDS * 2,
      });
      return { limited: false, remaining: maxRequests - 1, resetAt };
    }

    if (data.count >= maxRequests) {
      return { limited: true, remaining: 0, resetAt };
    }

    // カウントをインクリメント
    await env.SESSIONS.put(key, JSON.stringify({ count: data.count + 1, windowStart }), {
      expirationTtl: RATE_LIMIT_WINDOW_SECONDS * 2,
    });
    return { limited: false, remaining: maxRequests - data.count - 1, resetAt };
  } catch {
    // エラー時は許可（フェイルオープン）
    return { limited: false, remaining: maxRequests, resetAt };
  }
}

// ============================================
// リクエストボディ検証
// ============================================

interface TokenExchangeBody {
  code: string;
  redirect_uri: string;
}

interface TokenRefreshBody {
  refresh_token: string;
}

interface PatreonExchangeBody {
  code: string;
  redirect_uri: string;
  state?: string;
  /** [Issue #295] Supabase JWT（アカウント紐づけ用、オプション） */
  supabase_jwt?: string;
}

interface SessionValidateBody {
  session_token: string;
}

function validateTokenExchangeBody(body: unknown): ValidationResult<TokenExchangeBody> {
  if (!body || typeof body !== 'object') {
    return { success: false, error: 'Invalid request body' };
  }
  const obj = body as Record<string, unknown>;
  if (typeof obj.code !== 'string' || !obj.code) {
    return { success: false, error: 'Missing or invalid field: code' };
  }
  if (typeof obj.redirect_uri !== 'string' || !obj.redirect_uri) {
    return { success: false, error: 'Missing or invalid field: redirect_uri' };
  }
  return { success: true, data: { code: obj.code, redirect_uri: obj.redirect_uri } };
}

function validateTokenRefreshBody(body: unknown): ValidationResult<TokenRefreshBody> {
  if (!body || typeof body !== 'object') {
    return { success: false, error: 'Invalid request body' };
  }
  const obj = body as Record<string, unknown>;
  if (typeof obj.refresh_token !== 'string' || !obj.refresh_token) {
    return { success: false, error: 'Missing or invalid field: refresh_token' };
  }
  return { success: true, data: { refresh_token: obj.refresh_token } };
}

function validatePatreonExchangeBody(body: unknown): ValidationResult<PatreonExchangeBody> {
  if (!body || typeof body !== 'object') {
    return { success: false, error: 'Invalid request body' };
  }
  const obj = body as Record<string, unknown>;
  if (typeof obj.code !== 'string' || !obj.code) {
    return { success: false, error: 'Missing or invalid field: code' };
  }
  if (typeof obj.redirect_uri !== 'string' || !obj.redirect_uri) {
    return { success: false, error: 'Missing or invalid field: redirect_uri' };
  }
  const state = typeof obj.state === 'string' ? obj.state : undefined;
  // [Issue #295] Supabase JWT（オプション）
  const supabase_jwt = typeof obj.supabase_jwt === 'string' ? obj.supabase_jwt : undefined;
  return { success: true, data: { code: obj.code, redirect_uri: obj.redirect_uri, state, supabase_jwt } };
}

function validateSessionValidateBody(body: unknown): ValidationResult<SessionValidateBody> {
  if (!body || typeof body !== 'object') {
    return { success: false, error: 'Invalid request body' };
  }
  const obj = body as Record<string, unknown>;
  if (typeof obj.session_token !== 'string' || !obj.session_token) {
    return { success: false, error: 'Missing or invalid field: session_token' };
  }
  return { success: true, data: { session_token: obj.session_token } };
}

// ============================================
// KVヘルパー関数
// ============================================

async function getSession(env: Env, sessionToken: string): Promise<SessionData | null> {
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

async function setSession(env: Env, sessionToken: string, session: SessionData): Promise<void> {
  await env.SESSIONS.put(sessionToken, JSON.stringify(session), { expirationTtl: SESSION_TTL_SECONDS });
}

async function deleteSession(env: Env, sessionToken: string): Promise<void> {
  await env.SESSIONS.delete(sessionToken);
}

/** ユーザートークンを取得（トークン集約） */
async function getUserToken(env: Env, userId: string): Promise<UserTokenData | null> {
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

/** ユーザートークンを保存（トークン集約） */
async function setUserToken(env: Env, userId: string, token: UserTokenData): Promise<void> {
  const key = `usertoken:${userId}`;
  await env.SESSIONS.put(key, JSON.stringify(token), { expirationTtl: USER_TOKEN_TTL_SECONDS });
}

/** ユーザートークンを削除 */
async function deleteUserToken(env: Env, userId: string): Promise<void> {
  const key = `usertoken:${userId}`;
  await env.SESSIONS.delete(key);
}

/** セッションをユーザートークンに紐付け */
async function linkSessionToUser(env: Env, userId: string, sessionToken: string): Promise<void> {
  const userToken = await getUserToken(env, userId);
  if (userToken) {
    if (!userToken.sessionTokens.includes(sessionToken)) {
      userToken.sessionTokens.push(sessionToken);
      userToken.updatedAt = Date.now();
      await setUserToken(env, userId, userToken);
    }
  }
}

/** ユーザーの全セッションを無効化 */
async function revokeAllUserSessions(env: Env, userId: string): Promise<number> {
  const userToken = await getUserToken(env, userId);
  if (!userToken) return 0;

  let revokedCount = 0;
  for (const sessionToken of userToken.sessionTokens) {
    await deleteSession(env, sessionToken);
    revokedCount++;
  }

  await deleteUserToken(env, userId);
  await env.SESSIONS.delete(`membership:${userId}`);

  return revokedCount;
}

/** メンバーシップキャッシュを取得 */
async function getCachedMembership(env: Env, userId: string): Promise<CachedMembership | null> {
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

/** メンバーシップをキャッシュに保存 */
async function setCachedMembership(env: Env, userId: string, membership: ParsedMembership): Promise<void> {
  const cacheKey = `membership:${userId}`;
  const cached: CachedMembership = { membership, cachedAt: Date.now() };
  await env.SESSIONS.put(cacheKey, JSON.stringify(cached), { expirationTtl: IDENTITY_CACHE_TTL_SECONDS });
}

/** メンバーシップキャッシュを無効化 */
async function invalidateMembershipCache(env: Env, userId: string): Promise<void> {
  const cacheKey = `membership:${userId}`;
  await env.SESSIONS.delete(cacheKey);
}

// ============================================
// [Issue #296] 認証キャッシュ無効化
// translate.tsのCache APIと同じ形式でキャッシュを削除
// ============================================

/** トークンをSHA-256でハッシュ（translate.tsと同じ実装） */
async function hashTokenForAuthCache(token: string): Promise<string> {
  const encoder = new TextEncoder();
  const data = encoder.encode(token);
  const hashBuffer = await crypto.subtle.digest('SHA-256', data);
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  return hashArray.slice(0, 16).map(b => b.toString(16).padStart(2, '0')).join('');
}

/** 認証キャッシュ用のURLを生成 */
function getAuthCacheUrl(cacheKey: string): string {
  return `https://baketa-auth-cache.internal/${cacheKey}`;
}

/**
 * [Issue #296] 認証キャッシュを無効化
 * Patreon紐づけ後に古いplan=Freeキャッシュが残らないようにする
 */
async function invalidateAuthCache(token: string): Promise<void> {
  const tokenHash = await hashTokenForAuthCache(token);
  const cacheKey = `auth:v2:${tokenHash}`;
  const cacheUrl = getAuthCacheUrl(cacheKey);

  const cache = caches.default;
  const deleted = await cache.delete(new Request(cacheUrl));

  if (deleted) {
    console.log(`[Issue #296] Auth cache deleted: ${cacheKey.substring(0, 20)}...`);
  } else {
    console.log(`[Issue #296] Auth cache not found (already expired or never cached): ${cacheKey.substring(0, 20)}...`);
  }
}

// ============================================
// レスポンスヘルパー
// ============================================

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
  message: string,
  status: number,
  origin: string,
  allowedOrigins: string,
  errorCode?: string
): Response {
  return new Response(
    JSON.stringify({ error: message, error_code: errorCode }),
    {
      status,
      headers: {
        'Content-Type': 'application/json',
        ...corsHeaders(origin, allowedOrigins),
      },
    }
  );
}

function successResponse(data: object, origin: string, allowedOrigins: string): Response {
  return new Response(
    JSON.stringify(data),
    {
      status: 200,
      headers: {
        'Content-Type': 'application/json',
        ...corsHeaders(origin, allowedOrigins),
      },
    }
  );
}

function rateLimitResponse(
  origin: string,
  allowedOrigins: string,
  resetAt: number
): Response {
  return new Response(
    JSON.stringify({ error: 'Too Many Requests', error_code: 'RATE_LIMITED' }),
    {
      status: 429,
      headers: {
        'Content-Type': 'application/json',
        'Retry-After': String(Math.ceil((resetAt - Date.now()) / 1000)),
        'X-RateLimit-Reset': String(Math.floor(resetAt / 1000)),
        ...corsHeaders(origin, allowedOrigins),
      },
    }
  );
}

// ============================================
// 共通ビジネスロジック
// ============================================

function determinePlan(amountCents: number): PlanType {
  // Issue #257: Pro/Premium/Ultimate 3段階構成に改定
  if (amountCents >= TIER_AMOUNTS.ULTIMATE) return 'Ultimate';
  if (amountCents >= TIER_AMOUNTS.PREMIUM) return 'Premium';
  if (amountCents >= TIER_AMOUNTS.PRO) return 'Pro';
  return 'Free';
}

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

class PatreonApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message);
    this.name = 'PatreonApiError';
  }
}

async function fetchPatreonToken(
  env: Env,
  params: URLSearchParams
): Promise<PatreonTokenResponse> {
  const response = await fetch(PATREON_TOKEN_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: params,
  });

  if (!response.ok) {
    const errorText = await response.text();
    console.error(`Patreon token API failed: status=${response.status}, body=${errorText}`);
    throw new PatreonApiError('Token API failed', response.status);
  }

  return await response.json() as PatreonTokenResponse;
}

async function fetchPatreonIdentity(accessToken: string): Promise<PatreonIdentityResponse> {
  const response = await fetch(`${PATREON_IDENTITY_URL}?${PATREON_IDENTITY_PARAMS}`, {
    headers: { 'Authorization': `Bearer ${accessToken}` },
  });

  if (!response.ok) {
    const errorText = await response.text();
    console.error(`Patreon identity fetch failed: status=${response.status}, body=${errorText}`);
    throw new PatreonApiError('Failed to fetch identity', response.status);
  }

  return await response.json() as PatreonIdentityResponse;
}

async function getMembershipFromSession(
  env: Env,
  session: SessionData,
  useCache: boolean = true
): Promise<ParsedMembership> {
  if (useCache) {
    const cached = await getCachedMembership(env, session.userId);
    if (cached) {
      console.log(`Cache hit for user ${session.userId}`);
      return cached.membership;
    }
  }

  const identityData = await fetchPatreonIdentity(session.accessToken);
  const membership = parsePatreonMembership(identityData);

  await setCachedMembership(env, session.userId, membership);

  return membership;
}

async function validateAndGetSession(
  env: Env,
  sessionToken: string
): Promise<{ session: SessionData } | { error: string; errorCode: string }> {
  const session = await getSession(env, sessionToken);
  if (!session) {
    return { error: 'Invalid or expired session', errorCode: 'SESSION_INVALID' };
  }

  if (Date.now() > session.expiresAt) {
    await deleteSession(env, sessionToken);
    return { error: 'Session expired', errorCode: 'SESSION_EXPIRED' };
  }

  return { session };
}

// ============================================
// APIキー検証
// ============================================

function validateApiKey(request: Request, env: Env): boolean {
  if (!env.API_KEY) {
    if (env.ENVIRONMENT === 'production') {
      console.error('CRITICAL: API_KEY is not set in a production environment');
      return false;
    }
    console.warn('API_KEY is not set - allowing all requests (development mode)');
    return true;
  }

  const apiKey = request.headers.get('X-API-Key');
  if (!apiKey) return false;

  return timingSafeCompare(apiKey, env.API_KEY);
}

// ============================================
// Issue #287: JWT認証関数
// ============================================

/**
 * JWTアクセストークンを生成
 * @param env 環境変数
 * @param userId Patreonユーザー ID
 * @param plan 現在のプラン
 * @param hasBonusTokens ボーナストークンの有無
 * @returns JWTアクセストークン
 */
async function generateAccessToken(
  env: Env,
  userId: string,
  plan: PlanType,
  hasBonusTokens: boolean
): Promise<string> {
  if (!env.JWT_SECRET) {
    throw new Error('JWT_SECRET is not configured');
  }

  const secret = new TextEncoder().encode(env.JWT_SECRET);
  const now = Math.floor(Date.now() / 1000);

  const jwt = await new SignJWT({
    plan,
    hasBonusTokens,
    authMethod: 'patreon',
  } as BaketaJwtClaims)
    .setProtectedHeader({ alg: 'HS256' })
    .setSubject(userId)
    .setIssuer(JWT_ISSUER)
    .setAudience(JWT_AUDIENCE)
    .setIssuedAt(now)
    .setExpirationTime(now + JWT_ACCESS_TOKEN_TTL_SECONDS)
    .setJti(crypto.randomUUID())
    .sign(secret);

  return jwt;
}

/**
 * リフレッシュトークンを生成し、KVに保存
 * @param env 環境変数
 * @param userId Patreonユーザー ID
 * @param sessionToken 元のセッショントークン
 * @returns リフレッシュトークン
 */
async function generateRefreshToken(
  env: Env,
  userId: string,
  sessionToken: string
): Promise<string> {
  const refreshToken = crypto.randomUUID();
  const now = Date.now();

  const refreshData: RefreshTokenData = {
    userId,
    sessionToken,
    createdAt: now,
    expiresAt: now + JWT_REFRESH_TOKEN_TTL_SECONDS * 1000,
    isUsed: false,
  };

  // KVにリフレッシュトークンを保存
  await env.SESSIONS.put(
    `refresh:${refreshToken}`,
    JSON.stringify(refreshData),
    { expirationTtl: JWT_REFRESH_TOKEN_TTL_SECONDS }
  );

  return refreshToken;
}

/**
 * JWTアクセストークンを検証
 * @param env 環境変数
 * @param token JWTトークン
 * @returns 検証結果（成功時はペイロード、失敗時はnull）
 */
async function validateJwtToken(
  env: Env,
  token: string
): Promise<BaketaJwtClaims | null> {
  if (!env.JWT_SECRET) {
    console.error('JWT_SECRET is not configured');
    return null;
  }

  try {
    const secret = new TextEncoder().encode(env.JWT_SECRET);
    const { payload } = await jwtVerify(token, secret, {
      issuer: JWT_ISSUER,
      audience: JWT_AUDIENCE,
    });

    return payload as BaketaJwtClaims;
  } catch (error) {
    console.warn('JWT validation failed:', error instanceof Error ? error.message : error);
    return null;
  }
}

/**
 * リフレッシュトークンを検証し、使用済みにマーク
 * @param env 環境変数
 * @param refreshToken リフレッシュトークン
 * @returns 検証結果（成功時はリフレッシュトークンデータ、失敗時はnull）
 */
async function validateAndConsumeRefreshToken(
  env: Env,
  refreshToken: string
): Promise<RefreshTokenData | null> {
  const key = `refresh:${refreshToken}`;
  const data = await env.SESSIONS.get(key);

  if (!data) {
    console.warn('Refresh token not found');
    return null;
  }

  const refreshData: RefreshTokenData = JSON.parse(data);

  // 有効期限チェック
  if (Date.now() > refreshData.expiresAt) {
    console.warn('Refresh token expired');
    await env.SESSIONS.delete(key);
    return null;
  }

  // 使用済みチェック（1回使用で無効化）
  if (refreshData.isUsed) {
    console.warn('Refresh token already used - possible token theft');
    // セキュリティ: 使用済みトークンが再利用された場合は削除
    await env.SESSIONS.delete(key);
    return null;
  }

  // 使用済みにマーク
  refreshData.isUsed = true;
  await env.SESSIONS.put(key, JSON.stringify(refreshData), {
    expirationTtl: 60, // 1分後に自動削除（短い猶予）
  });

  return refreshData;
}

/**
 * リクエストからJWTまたはSessionTokenを抽出して認証
 * JWT優先、フォールバックとしてSessionToken
 * @param request リクエスト
 * @param env 環境変数
 * @returns 認証結果（成功時はユーザー情報、失敗時はnull）
 */
async function authenticateRequest(
  request: Request,
  env: Env
): Promise<{ userId: string; plan: PlanType; sessionToken?: string } | null> {
  const authHeader = request.headers.get('Authorization');

  if (authHeader?.startsWith('Bearer ')) {
    const token = authHeader.slice(7);

    // 1. JWTとして検証を試みる
    const jwtClaims = await validateJwtToken(env, token);
    if (jwtClaims && jwtClaims.sub) {
      console.log(`JWT auth successful: userId=${jwtClaims.sub}, plan=${jwtClaims.plan}`);
      return {
        userId: jwtClaims.sub,
        plan: jwtClaims.plan,
      };
    }

    // 2. SessionTokenとして検証（後方互換）
    const sessionData = await env.SESSIONS.get(token);
    if (sessionData) {
      const session: SessionData = JSON.parse(sessionData);
      console.log(`SessionToken auth successful: userId=${session.userId}`);
      // プラン情報はセッションに含まれないため、後で取得が必要
      return {
        userId: session.userId,
        plan: 'Free', // デフォルト、実際のプランは別途取得
        sessionToken: token,
      };
    }
  }

  return null;
}

// ============================================
// 環境変数検証
// ============================================

interface EnvValidationResult {
  valid: boolean;
  missingVars: string[];
}

function validateEnvironment(env: Env): EnvValidationResult {
  const missingVars: string[] = [];

  if (!env.PATREON_CLIENT_ID) missingVars.push('PATREON_CLIENT_ID');
  if (!env.PATREON_CLIENT_SECRET) missingVars.push('PATREON_CLIENT_SECRET');
  if (!env.SESSIONS) missingVars.push('SESSIONS');

  if (env.ENVIRONMENT === 'production') {
    if (!env.API_KEY) missingVars.push('API_KEY');
    if (!env.ALLOWED_REDIRECT_URIS) missingVars.push('ALLOWED_REDIRECT_URIS');
    if (!env.PATREON_WEBHOOK_SECRET) missingVars.push('PATREON_WEBHOOK_SECRET');
  }

  return {
    valid: missingVars.length === 0,
    missingVars,
  };
}

// ============================================
// プロモーションコード関連
// ============================================

/** Supabaseクライアント取得 */
function getSupabaseClient(env: Env): SupabaseClient | null {
  if (!env.SUPABASE_URL || !env.SUPABASE_SERVICE_KEY) {
    return null;
  }
  return createClient(env.SUPABASE_URL, env.SUPABASE_SERVICE_KEY, {
    auth: { persistSession: false }
  });
}

/** プロモーションコードリクエスト */
interface PromotionRedeemBody {
  code: string;
}

/** [Issue #261] 同意記録リクエスト */
interface ConsentRecordBody {
  user_id: string;
  consent_type: string;
  version: string;
  accepted_at: string;
  client_version?: string;
}

/**
 * Supabase JWTからユーザーIDを抽出・検証
 * @returns user_id (UUID) or null if not authenticated
 */
async function extractUserIdFromJwt(
  request: Request,
  supabase: SupabaseClient
): Promise<string | null> {
  const authHeader = request.headers.get('Authorization');
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return null;  // 未ログイン（許容）
  }

  const jwt = authHeader.substring(7);
  if (!jwt) {
    return null;
  }

  try {
    // Supabase Auth でJWTを検証し、ユーザー情報を取得
    const { data: { user }, error } = await supabase.auth.getUser(jwt);

    if (error || !user) {
      console.log(`JWT validation failed: ${error?.message || 'No user found'}`);
      return null;  // 無効なJWTでも未ログイン扱い（エラーにしない）
    }

    return user.id;
  } catch (error) {
    console.error('JWT extraction error:', error);
    return null;
  }
}

// ============================================
// [Issue #295] Patreon-Supabase アカウントリンク
// ============================================

/**
 * [Issue #295] Supabase JWTを検証してユーザーIDを抽出
 * @param supabase Supabaseクライアント
 * @param jwt JWT文字列
 * @returns user_id (UUID) or null if invalid
 */
async function validateSupabaseJwtAndGetUserId(
  supabase: SupabaseClient,
  jwt: string
): Promise<string | null> {
  if (!jwt) {
    return null;
  }

  try {
    const { data: { user }, error } = await supabase.auth.getUser(jwt);

    if (error || !user) {
      console.log(`[Issue #295] Supabase JWT validation failed: ${error?.message || 'No user found'}`);
      return null;
    }

    return user.id;
  } catch (error) {
    console.error('[Issue #295] Supabase JWT validation error:', error);
    return null;
  }
}

/**
 * [Issue #295] PatreonアカウントをSupabaseユーザーに紐づけ
 * @param env 環境変数
 * @param supabaseJwt Supabase JWT
 * @param patreonUserId Patreon ユーザーID
 * @returns 成功時true、失敗時false
 */
async function linkPatreonToSupabaseAccount(
  env: Env,
  supabaseJwt: string,
  patreonUserId: string
): Promise<boolean> {
  const supabase = getSupabaseClient(env);
  if (!supabase) {
    console.warn('[Issue #295] Supabase not configured, skipping account linking');
    return false;
  }

  // Step 1: JWT検証してSupabase user_idを取得
  const supabaseUserId = await validateSupabaseJwtAndGetUserId(supabase, supabaseJwt);
  if (!supabaseUserId) {
    console.log('[Issue #295] Invalid Supabase JWT, skipping account linking');
    return false;
  }

  // Step 2: 既存の紐づけをチェック
  try {
    const { data: existingProfile, error: fetchError } = await supabase
      .from('profiles')
      .select('patreon_user_id')
      .eq('id', supabaseUserId)
      .single();

    if (fetchError && fetchError.code !== 'PGRST116') {
      console.error('[Issue #295] Profile fetch error:', fetchError);
      return false;
    }

    // 既に同じPatreon IDで紐づけ済みならスキップ（冪等性）
    if (existingProfile?.patreon_user_id === patreonUserId) {
      console.log(`[Issue #295] Already linked: supabase=${supabaseUserId.substring(0, 8)}... ↔ patreon=${patreonUserId}`);
      return true;
    }

    // 別のPatreon IDと紐づけ済みの場合は上書き（1 Supabase : 1 Patreon）
    if (existingProfile?.patreon_user_id && existingProfile.patreon_user_id !== patreonUserId) {
      console.log(`[Issue #295] Re-linking: old_patreon=${existingProfile.patreon_user_id} → new_patreon=${patreonUserId}`);
    }

    // Step 3: link_patreon_user RPC呼び出し
    const { error: linkError } = await supabase.rpc('link_patreon_user', {
      p_user_id: supabaseUserId,
      p_patreon_user_id: patreonUserId
    });

    if (linkError) {
      console.error('[Issue #295] link_patreon_user RPC error:', linkError);
      return false;
    }

    // [Issue #296] 紐づけ成功時に認証キャッシュを無効化
    // これにより、次回の翻訳リクエストで新しいプラン情報が取得される
    try {
      await invalidateAuthCache(supabaseJwt);
      console.log(`[Issue #296] Auth cache invalidated for supabase user`);
    } catch (cacheError) {
      // キャッシュ削除失敗は紐づけ成功に影響させない
      console.warn('[Issue #296] Failed to invalidate auth cache:', cacheError);
    }

    console.log(`[Issue #295] Account linked successfully: supabase=${supabaseUserId.substring(0, 8)}... ↔ patreon=${patreonUserId}`);
    return true;
  } catch (error) {
    console.error('[Issue #295] Account linking failed:', error);
    return false;
  }
}

/** プロモーションコードバリデーション */
function validatePromotionRedeemBody(body: unknown): ValidationResult<PromotionRedeemBody> {
  if (!body || typeof body !== 'object') {
    return { success: false, error: 'Invalid request body' };
  }
  const obj = body as Record<string, unknown>;
  if (typeof obj.code !== 'string' || !obj.code) {
    return { success: false, error: 'Missing or invalid field: code' };
  }
  return { success: true, data: { code: obj.code } };
}

/** [Issue #261] 同意記録バリデーション */
function validateConsentRecordBody(body: unknown): ValidationResult<ConsentRecordBody> {
  if (!body || typeof body !== 'object') {
    return { success: false, error: 'Invalid request body' };
  }
  const obj = body as Record<string, unknown>;
  if (typeof obj.user_id !== 'string' || !obj.user_id) {
    return { success: false, error: 'Missing or invalid field: user_id' };
  }
  if (typeof obj.consent_type !== 'string' || !obj.consent_type) {
    return { success: false, error: 'Missing or invalid field: consent_type' };
  }
  // consent_type は privacy_policy または terms_of_service のみ
  const validTypes = ['privacy_policy', 'terms_of_service'];
  if (!validTypes.includes(obj.consent_type)) {
    return { success: false, error: `Invalid consent_type: must be one of ${validTypes.join(', ')}` };
  }
  if (typeof obj.version !== 'string' || !obj.version) {
    return { success: false, error: 'Missing or invalid field: version' };
  }
  if (typeof obj.accepted_at !== 'string' || !obj.accepted_at) {
    return { success: false, error: 'Missing or invalid field: accepted_at' };
  }
  const clientVersion = typeof obj.client_version === 'string' ? obj.client_version : undefined;
  return {
    success: true,
    data: {
      user_id: obj.user_id,
      consent_type: obj.consent_type,
      version: obj.version,
      accepted_at: obj.accepted_at,
      client_version: clientVersion
    }
  };
}

/** プロモーションコード形式検証（Base32 Crockford: O/I/L/U除外） */
const PROMOTION_CODE_PATTERN = /^BAKETA-[0-9A-HJKMNP-TV-Z]{8}$/;

function isValidPromotionCodeFormat(code: string): boolean {
  return PROMOTION_CODE_PATTERN.test(code);
}

/** プロモーションコードRPC結果 */
interface PromotionRpcResult {
  success: boolean;
  plan_type?: number;
  duration_days?: number;
  expires_at?: string;
  redemption_id?: string;
  error_code?: string;
  message?: string;
}

/**
 * プロモーションコード適用
 * POST /api/promotion/redeem
 */
async function handlePromotionRedeem(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    // 1. リクエスト検証
    const body = await request.json();
    const validation = validatePromotionRedeemBody(body);
    if (!validation.success || !validation.data) {
      return errorResponse(validation.error || 'Invalid request', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
    }

    const { code } = validation.data;
    const normalizedCode = code.toUpperCase().trim();

    // 2. コード形式検証（Base32 Crockford）- 早期リジェクト
    if (!isValidPromotionCodeFormat(normalizedCode)) {
      return successResponse({
        success: false,
        error_code: 'INVALID_CODE',
        message: '無効なプロモーションコードの形式です'
      }, origin, allowedOrigins);
    }

    // 3. Supabaseクライアント確認
    const supabase = getSupabaseClient(env);
    if (!supabase) {
      console.error('Supabase not configured for promotion codes');
      return errorResponse('Promotion service not available', 503, origin, allowedOrigins, 'SERVICE_UNAVAILABLE');
    }

    // 4. クライアントIP取得（監査用）
    const clientIP = request.headers.get('CF-Connecting-IP') || 'unknown';

    // 5. JWT検証してユーザーID抽出（未ログインはnull）
    const userId = await extractUserIdFromJwt(request, supabase);
    if (userId) {
      console.log(`Promotion redeem: authenticated user ${userId.substring(0, 8)}...`);
    } else {
      console.log('Promotion redeem: anonymous user');
    }

    // 6. Supabase RPC呼び出し（アトミック処理）
    const { data, error } = await supabase.rpc('redeem_promotion_code', {
      code_to_redeem: normalizedCode,
      client_ip_address: clientIP,
      redeeming_user_id: userId  // JWT検証済みユーザーID（nullable）
    });

    if (error) {
      console.error('Supabase RPC error:', error);
      return errorResponse('Server error', 500, origin, allowedOrigins, 'SERVER_ERROR');
    }

    // 7. RPC結果を解析してレスポンス生成
    const result = data as PromotionRpcResult;

    if (!result.success) {
      console.log(`Promotion code rejected: code=${normalizedCode.substring(0, 10)}****, error=${result.error_code}`);
      return successResponse({
        success: false,
        error_code: result.error_code || 'UNKNOWN_ERROR',
        message: result.message || '予期せぬエラーが発生しました'
      }, origin, allowedOrigins);
    }

    // 8. 成功レスポンス
    // PlanType型と一致させるため大文字で定義（C#側はToLowerInvariant()で正規化）
    // Issue #257: Pro/Premium/Ultimate 3段階構成に改定 (Free=0, Pro=1, Premium=2, Ultimate=3)
    const planTypeMap: Record<number, PlanType> = {
      0: 'Free', 1: 'Pro', 2: 'Premium', 3: 'Ultimate'
    };

    // Issue #280+#281: プラン相当のボーナストークンを付与（プラン変更なし）
    const PLAN_TOKEN_AMOUNTS: Record<number, number> = {
      1: 10_000_000,   // Pro: 1000万トークン
      2: 20_000_000,   // Premium: 2000万トークン
      3: 50_000_000    // Ultimate: 5000万トークン
    };

    const tokenAmount = PLAN_TOKEN_AMOUNTS[result.plan_type ?? 0] ?? 0;
    let bonusTokenId: string | null = null;

    // ユーザーがログイン済みで、トークン付与対象の場合のみボーナス付与
    if (userId && tokenAmount > 0) {
      console.log(`Granting bonus tokens: user=${userId.substring(0, 8)}..., tokens=${tokenAmount.toLocaleString()}`);

      const { data: bonusData, error: bonusError } = await supabase.rpc('grant_bonus_tokens', {
        p_user_id: userId,
        p_source_type: 'promotion',
        p_source_id: result.redemption_id,
        p_granted_tokens: tokenAmount,
        p_expires_at: result.expires_at
      });

      if (bonusError) {
        // [Gemini Review] ボーナス付与失敗 → redemptionをfailed状態に更新（監査ログ保持）
        console.error('Failed to grant bonus tokens:', bonusError);

        // 補償処理: redemptionのstatusをfailed_bonusに更新
        // これにより監査ログは保持しつつ、管理者が手動で対応可能
        const { error: updateError } = await supabase
          .from('promotion_code_redemptions')
          .update({
            status: 'failed_bonus',
            error_message: `Bonus grant failed: ${bonusError.message || 'Unknown error'}`
          })
          .eq('id', result.redemption_id);

        if (updateError) {
          console.error('Failed to update redemption status:', updateError);
        }

        return errorResponse(
          'ボーナストークンの付与に失敗しました。サポートにお問い合わせください。',
          500,
          origin,
          allowedOrigins,
          'BONUS_GRANT_FAILED'
        );
      }
      bonusTokenId = bonusData as string;
      console.log(`Bonus tokens granted: id=${bonusTokenId}`);
    }

    console.log(`Promotion code redeemed: code=${normalizedCode.substring(0, 10)}****, plan=${result.plan_type}, tokens=${tokenAmount.toLocaleString()}, user=${userId?.substring(0, 8) || 'anonymous'}`);

    // DBスキーマ上 plan_type は NOT NULL だが、万が一のフォールバックとして 'Pro' を使用
    const plan = planTypeMap[result.plan_type ?? 2] ?? 'Pro';

    return successResponse({
      success: true,
      plan_type: plan,
      expires_at: result.expires_at,
      bonus_tokens_granted: tokenAmount,  // Issue #280+#281: 付与トークン数
      message: tokenAmount > 0
        ? `プロモーションコードが適用されました。${tokenAmount.toLocaleString()}トークンが付与されました。`
        : 'プロモーションコードが適用されました。'
    }, origin, allowedOrigins);

  } catch (error) {
    console.error('Promotion redeem error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}

/**
 * [Issue #276] プロモーション状態取得
 * GET /api/promotion/status
 *
 * 認証済みユーザーのプロモーション適用状態をDBから取得
 * ローカルファイル依存からの脱却
 */
async function handlePromotionStatus(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'GET') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    // 1. Supabaseクライアント確認
    const supabase = getSupabaseClient(env);
    if (!supabase) {
      console.error('Supabase not configured for promotion status');
      return errorResponse('Promotion service not available', 503, origin, allowedOrigins, 'SERVICE_UNAVAILABLE');
    }

    // 2. JWT認証（必須）- サーバーサイド検証
    const authHeader = request.headers.get('Authorization');
    if (!authHeader?.startsWith('Bearer ')) {
      return errorResponse('Authentication required', 401, origin, allowedOrigins, 'PROMO_AUTH_REQUIRED');
    }

    const token = authHeader.substring(7);
    const { data: { user }, error: authError } = await supabase.auth.getUser(token);
    if (authError || !user) {
      return errorResponse('Invalid token', 401, origin, allowedOrigins, 'PROMO_INVALID_TOKEN');
    }

    // 3. レート制限チェック（10回/分）
    const rateLimit = await checkRateLimit(env, `promotion-status:${user.id}`, 10);
    if (rateLimit.limited) {
      return rateLimitResponse(origin, allowedOrigins, rateLimit.resetAt);
    }

    // 4. RPC関数を使用してプロモーション状態を取得
    // [Issue #276] サービスキーでは auth.uid() が機能しないため、
    // user_idをパラメータとして渡す専用関数を使用
    const { data, error } = await supabase.rpc('get_promotion_status_for_user', {
      p_user_id: user.id
    });

    if (error) {
      console.error('[Promotion] RPC error:', error);
      return errorResponse('Database error', 500, origin, allowedOrigins, 'PROMO_DB_ERROR');
    }

    // RPC関数は常に1行を返す（プロモーションなしの場合はhas_promotion=false）
    const result = Array.isArray(data) ? data[0] : data;

    if (!result || !result.has_promotion) {
      // プロモーションなし（正常系）
      console.log(JSON.stringify({
        event: 'promotion_status_check',
        user_id: user.id.substring(0, 8) + '...',
        has_promotion: false,
        expired: result?.is_expired ?? false,
        timestamp: new Date().toISOString()
      }));

      return successResponse({
        has_promotion: false,
        promotion: null,
        expired: result?.is_expired ?? false
      }, origin, allowedOrigins);
    }

    // 5. 有効期限チェック（DB側で計算済み）
    if (result.is_expired) {
      console.log(JSON.stringify({
        event: 'promotion_status_check',
        user_id: user.id.substring(0, 8) + '...',
        has_promotion: false,
        expired: true,
        timestamp: new Date().toISOString()
      }));

      return successResponse({
        has_promotion: false,
        promotion: null,
        expired: true
      }, origin, allowedOrigins);
    }

    // 6. 成功レスポンス
    console.log(JSON.stringify({
      event: 'promotion_status_check',
      user_id: user.id.substring(0, 8) + '...',
      has_promotion: true,
      plan_type: result.plan_name,
      timestamp: new Date().toISOString()
    }));

    return successResponse({
      has_promotion: true,
      promotion: {
        code_masked: result.code_masked,
        plan_type: result.plan_name,
        applied_at: result.applied_at,
        expires_at: result.expires_at
      }
    }, origin, allowedOrigins);

  } catch (error) {
    console.error('Promotion status error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}

/**
 * [Issue #261] 同意記録をSupabaseに保存
 * POST /api/consent/record
 *
 * GDPR/CCPA準拠の監査ログとして同意記録をDBに保存
 * - INSERT only（監査の整合性のため更新・削除は不可）
 * - RLSでユーザー自身の記録のみ参照可能
 */
async function handleConsentRecord(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    // 1. リクエスト検証
    const body = await request.json();
    const validation = validateConsentRecordBody(body);
    if (!validation.success || !validation.data) {
      return errorResponse(validation.error || 'Invalid request', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
    }

    const { user_id, consent_type, version, accepted_at, client_version } = validation.data;

    // 2. Supabaseクライアント確認
    const supabase = getSupabaseClient(env);
    if (!supabase) {
      console.error('[Issue #261] Supabase not configured for consent records');
      return errorResponse('Consent service not available', 503, origin, allowedOrigins, 'SERVICE_UNAVAILABLE');
    }

    // 3. JWT検証でユーザーID確認（セキュリティ: なりすまし防止）
    // [Gemini Review] 監査ログの信頼性確保のため、認証を必須化
    const authenticatedUserId = await extractUserIdFromJwt(request, supabase);
    if (!authenticatedUserId) {
      // 未認証は拒否（GDPR監査ログの整合性確保）
      console.warn('[Issue #261] Consent record rejected: no authentication');
      return errorResponse('Authentication required', 401, origin, allowedOrigins, 'AUTHENTICATION_REQUIRED');
    }

    // 認証済みユーザーIDとリクエストのuser_idが一致するか検証
    if (authenticatedUserId !== user_id) {
      console.warn(`[Issue #261] User ID mismatch: jwt=${authenticatedUserId.substring(0, 8)}, body=${user_id.substring(0, 8)}`);
      return errorResponse('User ID mismatch', 403, origin, allowedOrigins, 'USER_ID_MISMATCH');
    }

    // 4. consent_records テーブルにINSERT
    const { error } = await supabase
      .from('consent_records')
      .insert({
        user_id,
        consent_type,
        version,
        status: 'granted',
        recorded_at: accepted_at,
        client_version: client_version || null,
        metadata: {}
      });

    if (error) {
      // 外部キー制約エラー（user_idが存在しない）の場合は特別なエラーコード
      if (error.code === '23503') {
        console.warn(`[Issue #261] User not found: user_id=${user_id.substring(0, 8)}...`);
        return errorResponse('User not found', 404, origin, allowedOrigins, 'USER_NOT_FOUND');
      }
      console.error('[Issue #261] Supabase insert error:', error);
      return errorResponse('Failed to record consent', 500, origin, allowedOrigins, 'DATABASE_ERROR');
    }

    console.log(`[Issue #261] Consent recorded: user=${user_id.substring(0, 8)}..., type=${consent_type}, version=${version}`);

    return successResponse({
      success: true,
      message: 'Consent recorded successfully'
    }, origin, allowedOrigins);

  } catch (error) {
    console.error('[Issue #261] Consent record error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}

/**
 * [Issue #277] 同意状態取得
 * GET /api/consent/status
 *
 * 認証済みユーザーの同意状態をDBから取得
 * ローカルファイル依存からの脱却
 */
async function handleConsentStatus(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'GET') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    // 1. Supabaseクライアント確認
    const supabase = getSupabaseClient(env);
    if (!supabase) {
      console.error('Supabase not configured for consent status');
      return errorResponse('Consent service not available', 503, origin, allowedOrigins, 'SERVICE_UNAVAILABLE');
    }

    // 2. JWT認証（必須）- サーバーサイド検証
    const authHeader = request.headers.get('Authorization');
    if (!authHeader?.startsWith('Bearer ')) {
      return errorResponse('Authentication required', 401, origin, allowedOrigins, 'CONSENT_AUTH_REQUIRED');
    }

    const token = authHeader.substring(7);
    const { data: { user }, error: authError } = await supabase.auth.getUser(token);
    if (authError || !user) {
      return errorResponse('Invalid token', 401, origin, allowedOrigins, 'CONSENT_INVALID_TOKEN');
    }

    // 3. レート制限チェック（10回/分）
    const rateLimit = await checkRateLimit(env, `consent-status:${user.id}`, 10);
    if (rateLimit.limited) {
      return rateLimitResponse(origin, allowedOrigins, rateLimit.resetAt);
    }

    // 4. RPC関数を使用して同意状態を取得
    // [Issue #277] サービスキーでは auth.uid() が機能しないため、
    // user_idをパラメータとして渡す専用関数を使用
    const { data, error } = await supabase.rpc('get_consent_status_for_user', {
      p_user_id: user.id
    });

    if (error) {
      console.error('[Consent] RPC error:', error);
      return errorResponse('Database error', 500, origin, allowedOrigins, 'CONSENT_DB_ERROR');
    }

    // 5. レスポンス構築
    const results = Array.isArray(data) ? data : [];
    const privacyPolicy = results.find((r: { consent_type: string }) => r.consent_type === 'privacy_policy');
    const termsOfService = results.find((r: { consent_type: string }) => r.consent_type === 'terms_of_service');

    // 6. 監査ログ記録
    console.log(JSON.stringify({
      event: 'consent_status_retrieved',
      user_id: user.id.substring(0, 8) + '...',
      has_privacy_policy: !!privacyPolicy,
      has_terms: !!termsOfService,
      timestamp: new Date().toISOString()
    }));

    return successResponse({
      privacy_policy: privacyPolicy ? {
        status: privacyPolicy.status,
        version: privacyPolicy.version,
        recorded_at: privacyPolicy.recorded_at
      } : null,
      terms_of_service: termsOfService ? {
        status: termsOfService.status,
        version: termsOfService.version,
        recorded_at: termsOfService.recorded_at
      } : null
    }, origin, allowedOrigins);

  } catch (error) {
    console.error('[Issue #277] Consent status error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}

/**
 * Issue #269: 使用統計イベント収集
 * POST /api/analytics/events
 *
 * クライアントから送信された使用統計イベントをSupabaseに保存。
 * - 独自のAPIキー認証（ANALYTICS_API_KEY）
 * - CloudflareのCF-IPCountryヘッダーから国コードを取得
 * - バッチ送信対応（最大1000件）
 */
async function handleAnalyticsEvents(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  // 1. APIキー認証
  const apiKey = request.headers.get('X-Analytics-Key');
  if (!env.ANALYTICS_API_KEY) {
    console.error('[Issue #269] ANALYTICS_API_KEY not configured');
    return errorResponse('Analytics service not available', 503, origin, allowedOrigins, 'SERVICE_UNAVAILABLE');
  }
  if (!apiKey || apiKey !== env.ANALYTICS_API_KEY) {
    return errorResponse('Unauthorized', 401, origin, allowedOrigins, 'INVALID_API_KEY');
  }

  // 2. レートリミット（IP単位）
  const clientIP = request.headers.get('CF-Connecting-IP') || 'unknown';
  const rateLimit = await checkRateLimit(env, `analytics:${clientIP}`, 120);  // 1分間に120リクエストまで
  if (rateLimit.limited) {
    return rateLimitResponse(origin, allowedOrigins, rateLimit.resetAt);
  }

  try {
    // 3. リクエストボディのパース
    const events: UsageEvent[] = await request.json();

    // 4. バリデーション
    if (!Array.isArray(events)) {
      return errorResponse('Request body must be an array', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
    }
    if (events.length === 0) {
      return errorResponse('No events provided', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
    }
    if (events.length > 1000) {
      return errorResponse('Too many events (max 1000)', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
    }

    // 各イベントの必須フィールドチェック
    for (const event of events) {
      if (!event.session_id || !event.event_type || !event.app_version || !event.occurred_at) {
        return errorResponse('Missing required fields in event', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
      }
    }

    // 5. Supabaseクライアント確認
    const supabase = getSupabaseClient(env);
    if (!supabase) {
      console.error('[Issue #269] Supabase not configured for analytics');
      return errorResponse('Analytics service not available', 503, origin, allowedOrigins, 'SERVICE_UNAVAILABLE');
    }

    // 6. 国コード取得（Cloudflare自動付与）
    const countryCode = (request as Request & { cf?: { country?: string } }).cf?.country || 'UNKNOWN';

    // 7. イベントにcountry_codeを付与してSupabaseに挿入
    const eventsWithCountry = events.map(event => ({
      session_id: event.session_id,
      user_id: event.user_id || null,
      event_type: event.event_type,
      event_data: event.event_data || null,
      schema_version: event.schema_version || 1,
      app_version: event.app_version,
      country_code: countryCode,
      occurred_at: event.occurred_at,
    }));

    const { error } = await supabase
      .from('usage_events')
      .insert(eventsWithCountry);

    if (error) {
      console.error('[Issue #269] Supabase insert error:', error);
      return errorResponse('Failed to store events', 500, origin, allowedOrigins, 'DATABASE_ERROR');
    }

    console.log(`[Issue #269] Analytics events stored: count=${events.length}, country=${countryCode}, ip=${clientIP.substring(0, 8)}...`);

    return successResponse({
      success: true,
      stored_count: events.length,
    }, origin, allowedOrigins);

  } catch (error) {
    console.error('[Issue #269] Analytics error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}

// ============================================
// Issue #280+#281: ボーナストークンAPI
// ============================================

/**
 * [Issue #280+#281] ボーナストークン状態取得
 * GET /api/bonus-tokens/status
 *
 * 認証済みユーザーのボーナストークン一覧を取得
 * - 有効期限順にソート
 * - 残高と有効期限情報を含む
 */
async function handleBonusTokensStatus(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'GET') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    // 1. Supabaseクライアント確認
    const supabase = getSupabaseClient(env);
    if (!supabase) {
      console.error('[Issue #280] Supabase not configured for bonus tokens');
      return errorResponse('Bonus token service not available', 503, origin, allowedOrigins, 'SERVICE_UNAVAILABLE');
    }

    // 2. JWT認証（必須）
    const authHeader = request.headers.get('Authorization');
    if (!authHeader?.startsWith('Bearer ')) {
      return errorResponse('Authentication required', 401, origin, allowedOrigins, 'BONUS_AUTH_REQUIRED');
    }

    const token = authHeader.substring(7);
    const { data: { user }, error: authError } = await supabase.auth.getUser(token);
    if (authError || !user) {
      return errorResponse('Invalid token', 401, origin, allowedOrigins, 'BONUS_INVALID_TOKEN');
    }

    // 3. レート制限チェック（10回/分）
    const rateLimit = await checkRateLimit(env, `bonus-status:${user.id}`, 10);
    if (rateLimit.limited) {
      return rateLimitResponse(origin, allowedOrigins, rateLimit.resetAt);
    }

    // 4. RPC関数を使用してボーナストークン状態を取得
    // サービスキーでは auth.uid() が機能しないため、専用関数を使用
    const { data, error } = await supabase.rpc('get_bonus_tokens_for_user', {
      p_user_id: user.id
    });

    if (error) {
      console.error('[Issue #280] RPC error:', error);
      return errorResponse('Database error', 500, origin, allowedOrigins, 'BONUS_DB_ERROR');
    }

    // 5. レスポンス構築
    const bonuses: BonusTokenInfo[] = Array.isArray(data) ? data : [];

    // 有効なボーナス（未期限切れで残高あり）の合計を計算
    const totalRemaining = bonuses
      .filter(b => !b.is_expired && b.remaining_tokens > 0)
      .reduce((sum, b) => sum + b.remaining_tokens, 0);

    console.log(JSON.stringify({
      event: 'bonus_tokens_status',
      user_id: user.id.substring(0, 8) + '...',
      bonus_count: bonuses.length,
      total_remaining: totalRemaining,
      timestamp: new Date().toISOString()
    }));

    return successResponse({
      bonuses,
      total_remaining: totalRemaining,
      active_count: bonuses.filter(b => !b.is_expired && b.remaining_tokens > 0).length
    }, origin, allowedOrigins);

  } catch (error) {
    console.error('[Issue #280] Bonus tokens status error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}

/**
 * [Issue #280+#281] ボーナストークン使用量同期
 * POST /api/bonus-tokens/sync
 *
 * CRDT G-Counterパターンで使用量を同期
 * - 各ボーナスIDごとに大きい方を採用
 * - オフライン時のローカル消費もサーバーに反映可能
 */
async function handleBonusTokensSync(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    // 1. Supabaseクライアント確認
    const supabase = getSupabaseClient(env);
    if (!supabase) {
      console.error('[Issue #281] Supabase not configured for bonus tokens sync');
      return errorResponse('Bonus token service not available', 503, origin, allowedOrigins, 'SERVICE_UNAVAILABLE');
    }

    // 2. JWT認証（必須）
    const authHeader = request.headers.get('Authorization');
    if (!authHeader?.startsWith('Bearer ')) {
      return errorResponse('Authentication required', 401, origin, allowedOrigins, 'BONUS_AUTH_REQUIRED');
    }

    const token = authHeader.substring(7);
    const { data: { user }, error: authError } = await supabase.auth.getUser(token);
    if (authError || !user) {
      return errorResponse('Invalid token', 401, origin, allowedOrigins, 'BONUS_INVALID_TOKEN');
    }

    // 3. レート制限チェック（30回/分 - 同期は頻繁に呼ばれる可能性あり）
    const rateLimit = await checkRateLimit(env, `bonus-sync:${user.id}`, 30);
    if (rateLimit.limited) {
      return rateLimitResponse(origin, allowedOrigins, rateLimit.resetAt);
    }

    // 4. リクエストボディ取得
    const body = await request.json() as { bonuses?: BonusSyncItem[] };

    if (!body.bonuses || !Array.isArray(body.bonuses)) {
      return errorResponse('Missing or invalid bonuses array', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
    }

    // 空配列の場合は同期不要
    if (body.bonuses.length === 0) {
      return successResponse({
        synced: [],
        synced_count: 0
      }, origin, allowedOrigins);
    }

    // 各項目のバリデーション
    for (const item of body.bonuses) {
      if (!item.id || typeof item.used_tokens !== 'number') {
        return errorResponse('Invalid bonus sync item', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
      }
      if (item.used_tokens < 0) {
        return errorResponse('used_tokens must be non-negative', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
      }
    }

    // 5. RPC関数を使用してボーナストークンを同期
    const { data, error } = await supabase.rpc('sync_bonus_tokens_for_user', {
      p_user_id: user.id,
      p_bonuses: body.bonuses
    });

    if (error) {
      console.error('[Issue #281] RPC error:', error);
      return errorResponse('Sync failed', 500, origin, allowedOrigins, 'BONUS_SYNC_ERROR');
    }

    // 6. 同期結果をレスポンス
    const synced = Array.isArray(data) ? data : [];

    console.log(JSON.stringify({
      event: 'bonus_tokens_sync',
      user_id: user.id.substring(0, 8) + '...',
      requested_count: body.bonuses.length,
      synced_count: synced.length,
      timestamp: new Date().toISOString()
    }));

    return successResponse({
      synced,
      synced_count: synced.length
    }, origin, allowedOrigins);

  } catch (error) {
    console.error('[Issue #281] Bonus tokens sync error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}

// ============================================
// メインハンドラ
// ============================================

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const origin = request.headers.get('Origin') || '*';
    const allowedOrigins = env.ALLOWED_ORIGINS || '*';

    // CORS preflight
    if (request.method === 'OPTIONS') {
      return new Response(null, {
        status: 204,
        headers: corsHeaders(origin, allowedOrigins),
      });
    }

    // ヘルスチェック（認証不要・レートリミットなし）
    if (url.pathname === '/health') {
      return successResponse({
        status: 'ok',
        timestamp: new Date().toISOString(),
        environment: env.ENVIRONMENT || 'development',
      }, origin, allowedOrigins);
    }

    // Webhook（別の認証方式）
    if (url.pathname === '/webhook') {
      return handleWebhook(request, env, origin, allowedOrigins);
    }

    // クラッシュレポート（認証不要 - クラッシュ時は設定ロード前の可能性があるため）
    // 独自のレートリミットを持つ
    if (url.pathname === '/api/crash-report') {
      return handleCrashReport(request, env as CrashReportEnv, origin, allowedOrigins);
    }

    // Issue #269: 使用統計収集（独自のAPIキー認証）
    if (url.pathname === '/api/analytics/events') {
      return handleAnalyticsEvents(request, env, origin, allowedOrigins);
    }

    // 環境変数検証
    const envValidation = validateEnvironment(env);
    if (!envValidation.valid) {
      console.error(`Missing environment variables: ${envValidation.missingVars.join(', ')}`);
      return errorResponse('Server configuration error', 500, origin, allowedOrigins, 'CONFIG_ERROR');
    }

    // [Issue #287] 認証検証（JWT → SessionToken → API Key の順で検証）
    // 各エンドポイントで個別に認証を行うため、ここではグローバルチェックを削除
    // 認証が必要なエンドポイントは authenticateUser() を使用

    // [Issue #286] グローバルIPレートリミットを削除（KV Write削減）
    // グローバルなDoS対策はCloudflare WAF Rate Limiting Rulesで設定推奨:
    // - Dashboard → Security → WAF → Rate limiting rules
    // - Rule: If IP address equals, then Block for 1 minute (60 requests/minute)
    // エンドポイント単位のレートリミットは各ハンドラ内で継続
    const clientIP = request.headers.get('CF-Connecting-IP') || 'unknown';

    // ルーティング
    try {
      switch (url.pathname) {
        case '/oauth/token':
          return handleTokenExchange(request, env, origin, allowedOrigins);
        case '/oauth/refresh':
          return handleTokenRefresh(request, env, origin, allowedOrigins);
        case '/api/membership':
          return handleMembershipCheck(request, env, origin, allowedOrigins);
        case '/api/patreon/exchange':
          return handlePatreonExchange(request, env, origin, allowedOrigins);
        case '/api/session/validate':
          return handleSessionValidate(request, env, origin, allowedOrigins);
        case '/api/patreon/license-status':
          return handleLicenseStatus(request, env, origin, allowedOrigins);
        case '/api/patreon/revoke':
          return handlePatreonRevoke(request, env, origin, allowedOrigins);
        case '/api/patreon/revoke-all':
          return handlePatreonRevokeAll(request, env, origin, allowedOrigins);
        case '/api/translate':
          return handleTranslate(request, env as TranslateEnv, origin, allowedOrigins);
        case '/api/promotion/redeem':
          return handlePromotionRedeem(request, env, origin, allowedOrigins);
        case '/api/promotion/status':
          return handlePromotionStatus(request, env, origin, allowedOrigins);
        case '/api/consent/record':
          return handleConsentRecord(request, env, origin, allowedOrigins);
        case '/api/consent/status':
          return handleConsentStatus(request, env, origin, allowedOrigins);
        case '/api/bonus-tokens/status':
          return handleBonusTokensStatus(request, env, origin, allowedOrigins);
        case '/api/bonus-tokens/sync':
          return handleBonusTokensSync(request, env, origin, allowedOrigins);
        // Issue #296: クォータ状態取得
        case '/api/quota/status':
          return handleQuotaStatus(request, env as TranslateEnv, origin, allowedOrigins);
        // Issue #287: JWT認証エンドポイント
        case '/api/auth/token':
          return handleAuthToken(request, env, origin, allowedOrigins);
        case '/api/auth/refresh':
          return handleAuthRefresh(request, env, origin, allowedOrigins);
        // Note: /api/crash-report is handled earlier (before API key validation)
        default:
          return errorResponse('Not Found', 404, origin, allowedOrigins, 'NOT_FOUND');
      }
    } catch (error) {
      console.error(`Unhandled error at ${url.pathname}:`, error);
      return errorResponse('Internal Server Error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
    }
  },
};

// ============================================
// エンドポイントハンドラ
// ============================================

/**
 * Patreon Webhook受信
 * POST /webhook
 *
 * Patreonからのリアルタイム通知を処理:
 * - members:pledge:create - 新規支援
 * - members:pledge:update - Tier変更
 * - members:pledge:delete - 支援停止
 */
async function handleWebhook(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  // Webhookレートリミット
  const clientIP = request.headers.get('CF-Connecting-IP') || 'webhook';
  const rateLimit = await checkRateLimit(env, `webhook:${clientIP}`, RATE_LIMIT_WEBHOOK_MAX);
  if (rateLimit.limited) {
    return rateLimitResponse(origin, allowedOrigins, rateLimit.resetAt);
  }

  try {
    const signature = request.headers.get('X-Patreon-Signature');
    const eventType = request.headers.get('X-Patreon-Event');
    const body = await request.text();

    // 署名検証（設定されている場合）
    if (env.PATREON_WEBHOOK_SECRET) {
      if (!signature) {
        console.error('Webhook: Missing signature');
        return errorResponse('Missing signature', 401, origin, allowedOrigins, 'MISSING_SIGNATURE');
      }

      const isValid = await verifyWebhookSignature(body, signature, env.PATREON_WEBHOOK_SECRET);
      if (!isValid) {
        console.error('Webhook: Invalid signature');
        return errorResponse('Invalid signature', 401, origin, allowedOrigins, 'INVALID_SIGNATURE');
      }
    } else {
      console.warn('Webhook: PATREON_WEBHOOK_SECRET not set, skipping signature verification');
    }

    const payload = JSON.parse(body) as PatreonWebhookPayload;
    const userId = payload.data.relationships?.user?.data?.id;
    const pledgeId = payload.data.id;
    const amountCents = payload.data.attributes?.currently_entitled_amount_cents ?? 0;
    const patronStatus = payload.data.attributes?.patron_status;

    console.log(`Webhook received: event=${eventType}, userId=${userId}, pledgeId=${pledgeId}, amountCents=${amountCents}, patronStatus=${patronStatus}`);

    if (userId) {
      // [Issue #271] プラン変更を履歴に記録
      const supabase = getSupabaseClient(env);
      const newPlan = eventType === 'members:pledge:delete' ? 'Free' : determinePlan(amountCents);

      if (supabase) {
        // 前回のプランを取得
        const { data: oldPlan } = await supabase.rpc('get_latest_plan_by_patreon', {
          p_patreon_user_id: userId
        });

        // プラン変更があれば記録
        if (oldPlan !== newPlan) {
          const { data: recordId, error: recordError } = await supabase.rpc('record_plan_change_by_patreon', {
            p_patreon_user_id: userId,
            p_old_plan: oldPlan || null,
            p_new_plan: newPlan,
            p_source: 'patreon_webhook',
            p_patreon_pledge_id: pledgeId,
            p_metadata: { event: eventType, amount_cents: amountCents }
          });

          if (recordError) {
            console.error(`Webhook: Failed to record plan change: ${recordError.message}`);
          } else {
            console.log(`Webhook: Recorded plan change ${oldPlan || 'NULL'} -> ${newPlan} (recordId=${recordId})`);
          }
        } else {
          console.log(`Webhook: No plan change detected (current=${newPlan})`);
        }
      } else {
        console.warn('Webhook: Supabase client not available, skipping plan change recording');
      }

      // 全イベントでメンバーシップキャッシュを無効化
      // 次回API呼び出し時にPatreon APIから最新状態を取得する
      // Patreon APIが「真実の情報源」- 課金期間中はまだ有料プラン、期間終了後はFreeを返す
      await invalidateMembershipCache(env, userId);
      console.log(`Webhook: Invalidated membership cache for user ${userId} (${eventType})`);

      // イベントタイプに応じた追加処理
      switch (eventType) {
        case 'members:pledge:delete':
          // [Issue #296] セッションは無効化しない
          // Patreon APIがアクセス状態を正確に返すため、セッション無効化は不要
          // - 課金期間中: Patreon APIはまだ有料プランを返す
          // - 支払い失敗等で即時停止: Patreon APIはFreeを返す
          console.log(`Webhook: Pledge delete processed for user ${userId}`);
          break;

        case 'members:pledge:create':
          console.log(`Webhook: Processed ${eventType} for user ${userId}`);
          break;

        case 'members:pledge:update':
          // [Issue #296] patron_statusによる即時アクセス停止判定
          // - declined_patron: 支払い失敗・カード拒否
          // - former_patron: メンバーシップ自体をキャンセル済み
          if (patronStatus === 'declined_patron' || patronStatus === 'former_patron') {
            console.log(`Webhook: Immediate revocation triggered for user ${userId} (patronStatus=${patronStatus})`);
            const revokedCount = await revokeAllUserSessions(env, userId);
            console.log(`Webhook: Revoked ${revokedCount} sessions for user ${userId}`);
          } else {
            console.log(`Webhook: Processed ${eventType} for user ${userId} (patronStatus=${patronStatus})`);
          }
          break;

        default:
          console.log(`Webhook: Unknown event type ${eventType}`);
      }
    }

    return successResponse({
      success: true,
      event: eventType,
      processed: true,
      planChangeRecorded: !!userId,
    }, origin, allowedOrigins);

  } catch (error) {
    console.error('Webhook error:', error instanceof Error ? error.message : String(error));
    return errorResponse('Webhook processing failed', 500, origin, allowedOrigins, 'WEBHOOK_ERROR');
  }
}

/**
 * OAuth認証コードをトークンに交換
 * POST /oauth/token
 */
async function handleTokenExchange(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    const body = await request.json();
    const validation = validateTokenExchangeBody(body);
    if (!validation.success || !validation.data) {
      return errorResponse(validation.error || 'Invalid request', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
    }

    const { code, redirect_uri } = validation.data;

    if (!validateRedirectUri(redirect_uri, env)) {
      console.error(`Invalid redirect_uri: ${redirect_uri}`);
      return errorResponse('Invalid redirect_uri', 400, origin, allowedOrigins, 'INVALID_REDIRECT_URI');
    }

    const tokenData = await fetchPatreonToken(env, new URLSearchParams({
      code,
      grant_type: 'authorization_code',
      client_id: env.PATREON_CLIENT_ID,
      client_secret: env.PATREON_CLIENT_SECRET,
      redirect_uri,
    }));

    return successResponse(tokenData, origin, allowedOrigins);

  } catch (error) {
    if (error instanceof PatreonApiError) {
      return errorResponse('Token exchange failed', error.status, origin, allowedOrigins, 'TOKEN_EXCHANGE_FAILED');
    }
    console.error('Token exchange error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins);
  }
}

/**
 * リフレッシュトークンでアクセストークンを更新
 * POST /oauth/refresh
 */
async function handleTokenRefresh(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    const body = await request.json();
    const validation = validateTokenRefreshBody(body);
    if (!validation.success || !validation.data) {
      return errorResponse(validation.error || 'Invalid request', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
    }

    const { refresh_token } = validation.data;

    const tokenData = await fetchPatreonToken(env, new URLSearchParams({
      refresh_token,
      grant_type: 'refresh_token',
      client_id: env.PATREON_CLIENT_ID,
      client_secret: env.PATREON_CLIENT_SECRET,
    }));

    return successResponse(tokenData, origin, allowedOrigins);

  } catch (error) {
    if (error instanceof PatreonApiError) {
      return errorResponse('Token refresh failed', error.status, origin, allowedOrigins, 'TOKEN_REFRESH_FAILED');
    }
    console.error('Token refresh error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins);
  }
}

/**
 * メンバーシップ情報を取得
 * GET /api/membership
 */
async function handleMembershipCheck(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'GET') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    const authHeader = request.headers.get('Authorization');
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      return errorResponse('Missing or invalid Authorization header', 401, origin, allowedOrigins);
    }

    const accessToken = authHeader.substring(7);
    const identityData = await fetchPatreonIdentity(accessToken);
    const membership = parsePatreonMembership(identityData);

    return successResponse({
      user_id: membership.userId,
      email: membership.email,
      full_name: membership.fullName,
      is_active_patron: membership.patronStatus === PATRON_STATUS.ACTIVE,
      patron_status: membership.patronStatus,
      entitled_amount_cents: membership.entitledAmountCents,
      tier: membership.tierId ? { id: membership.tierId } : null,
    }, origin, allowedOrigins);

  } catch (error) {
    if (error instanceof PatreonApiError) {
      return errorResponse('Failed to fetch membership', error.status, origin, allowedOrigins);
    }
    console.error('Membership check error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins);
  }
}

/**
 * Patreon認証コード交換とユーザー情報取得（統合エンドポイント）
 * POST /api/patreon/exchange
 *
 * シングルデバイス強制:
 * - 新規ログイン時に既存の全セッションを無効化
 * - 1ユーザー1セッションのみ有効（他デバイスでログインすると元デバイスはログアウト）
 * - ユーザーIDベースでトークンを保存
 */
async function handlePatreonExchange(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    const body = await request.json();
    const validation = validatePatreonExchangeBody(body);
    if (!validation.success || !validation.data) {
      return errorResponse(validation.error || 'Invalid request', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
    }

    const { code, redirect_uri, supabase_jwt } = validation.data;

    if (!validateRedirectUri(redirect_uri, env)) {
      console.error(`Invalid redirect_uri: ${redirect_uri}`);
      return errorResponse('Invalid redirect_uri', 400, origin, allowedOrigins, 'INVALID_REDIRECT_URI');
    }

    console.log(`Patreon exchange request received: redirect_uri=${redirect_uri}, hasSupabaseJwt=${!!supabase_jwt}`);

    // Step 1: トークン交換
    const tokenData = await fetchPatreonToken(env, new URLSearchParams({
      code,
      grant_type: 'authorization_code',
      client_id: env.PATREON_CLIENT_ID,
      client_secret: env.PATREON_CLIENT_SECRET,
      redirect_uri,
    }));

    console.log('Token exchange successful, fetching identity...');

    // Step 2: ユーザー情報取得
    const identityData = await fetchPatreonIdentity(tokenData.access_token);
    console.log('Identity fetched successfully');

    // Step 3: メンバーシップ解析
    const membership = parsePatreonMembership(identityData);

    // Step 4: シングルデバイス強制 - 既存セッションをすべて無効化
    const existingUserToken = await getUserToken(env, membership.userId);
    let revokedCount = 0;
    if (existingUserToken && existingUserToken.sessionTokens.length > 0) {
      // 既存の全セッションを削除（他デバイスをログアウト）
      for (const oldSessionToken of existingUserToken.sessionTokens) {
        await deleteSession(env, oldSessionToken);
        revokedCount++;
      }
      console.log(`Single-device enforcement: Revoked ${revokedCount} existing sessions for user ${membership.userId}`);
    }

    // Step 5: 新しいセッション作成（唯一の有効セッション）
    const sessionToken = generateSessionToken();

    await setSession(env, sessionToken, {
      accessToken: tokenData.access_token,
      refreshToken: tokenData.refresh_token,
      expiresAt: Date.now() + (tokenData.expires_in * 1000),
      userId: membership.userId,
    });

    // Step 6: ユーザートークン保存（単一セッションのみ）
    await setUserToken(env, membership.userId, {
      accessToken: tokenData.access_token,
      refreshToken: tokenData.refresh_token,
      expiresAt: Date.now() + (tokenData.expires_in * 1000),
      email: membership.email,
      fullName: membership.fullName,
      sessionTokens: [sessionToken],  // 常に1セッションのみ
      updatedAt: Date.now(),
    });

    // メンバーシップをキャッシュに保存
    await setCachedMembership(env, membership.userId, membership);

    // [Issue #295] Supabase JWTがあればアカウントを紐づけ
    let accountLinked = false;
    if (supabase_jwt) {
      accountLinked = await linkPatreonToSupabaseAccount(env, supabase_jwt, membership.userId);
    }

    const sessionResponse = {
      session_token: sessionToken,
      patreon_user_id: membership.userId,
      email: membership.email,
      full_name: membership.fullName,
      expires_in: tokenData.expires_in,
      plan: membership.plan,
      tier_id: membership.tierId,
      patron_status: membership.patronStatus,
      next_charge_date: membership.nextChargeDate,
      entitled_amount_cents: membership.entitledAmountCents,
    };

    console.log(`Exchange successful: UserId=${membership.userId}, Plan=${membership.plan}, RevokedSessions=${revokedCount}, AccountLinked=${accountLinked}`);

    return successResponse(sessionResponse, origin, allowedOrigins);

  } catch (error) {
    if (error instanceof PatreonApiError) {
      const errorCode = error.message.includes('Token') ? 'TOKEN_EXCHANGE_FAILED' : 'IDENTITY_FETCH_FAILED';
      return errorResponse(`Operation failed (status: ${error.status})`, error.status, origin, allowedOrigins, errorCode);
    }
    console.error('Patreon exchange error:', error instanceof Error ? `${error.name}: ${error.message}` : String(error));
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}

/**
 * セッショントークン検証とライセンス状態取得
 * POST /api/session/validate
 */
async function handleSessionValidate(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    const body = await request.json();
    const validation = validateSessionValidateBody(body);
    if (!validation.success || !validation.data) {
      return errorResponse(validation.error || 'Invalid request', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
    }

    const { session_token } = validation.data;

    const sessionResult = await validateAndGetSession(env, session_token);
    if ('error' in sessionResult) {
      if (sessionResult.errorCode === 'SESSION_EXPIRED') {
        await deleteSession(env, session_token);
      }
      return errorResponse(sessionResult.error, 401, origin, allowedOrigins, sessionResult.errorCode);
    }

    try {
      const membership = await getMembershipFromSession(env, sessionResult.session, true);

      return successResponse({
        SessionValid: true,
        PatreonUserId: membership.userId,
        Email: membership.email,
        FullName: membership.fullName,
        Plan: membership.plan,
        TierId: membership.tierId,
        PatronStatus: membership.patronStatus,
        NextChargeDate: membership.nextChargeDate,
        SessionExpiresAt: sessionResult.session.expiresAt,
      }, origin, allowedOrigins);

    } catch (error) {
      if (error instanceof PatreonApiError && error.status === 401) {
        await deleteSession(env, session_token);
        return errorResponse('Session invalid, re-authentication required', 401, origin, allowedOrigins, 'PATREON_TOKEN_EXPIRED');
      }
      throw error;
    }

  } catch (error) {
    console.error('Session validate error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins);
  }
}

/**
 * ライセンス状態を取得
 * GET /api/patreon/license-status
 */
async function handleLicenseStatus(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'GET') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  const createLicenseErrorResponse = (error: string, errorCode: string, status: number) => {
    return new Response(
      JSON.stringify({
        success: false,
        session_valid: false,
        session_expired: true,
        error,
        error_code: errorCode,
      }),
      {
        status,
        headers: { 'Content-Type': 'application/json', ...corsHeaders(origin, allowedOrigins) },
      }
    );
  };

  try {
    const authHeader = request.headers.get('Authorization');
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      return createLicenseErrorResponse('Missing or invalid Authorization header', 'MISSING_AUTH', 401);
    }

    const sessionToken = authHeader.substring(7);

    const sessionResult = await validateAndGetSession(env, sessionToken);
    if ('error' in sessionResult) {
      return createLicenseErrorResponse(sessionResult.error, sessionResult.errorCode, 401);
    }

    try {
      // [Issue #296] 手動同期時はキャッシュをバイパスしてPatreon APIから最新情報を取得
      // KVの結果整合性により、Webhook後のキャッシュ無効化が即座に反映されない場合があるため
      const membership = await getMembershipFromSession(env, sessionResult.session, false);

      return successResponse({
        success: true,
        session_valid: true,
        session_expired: false,
        plan: membership.plan,
        tier_id: membership.tierId,
        patron_status: membership.patronStatus,
        next_charge_date: membership.nextChargeDate,
        error: null,
        error_code: null,
      }, origin, allowedOrigins);

    } catch (error) {
      if (error instanceof PatreonApiError && error.status === 401) {
        await deleteSession(env, sessionToken);
        return createLicenseErrorResponse('Patreon token expired, re-authentication required', 'PATREON_TOKEN_EXPIRED', 401);
      }
      throw error;
    }

  } catch (error) {
    console.error('License status error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins);
  }
}

/**
 * Patreonセッションを無効化（ログアウト）
 * POST /api/patreon/revoke
 */
async function handlePatreonRevoke(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    const authHeader = request.headers.get('Authorization');
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      return errorResponse('Missing or invalid Authorization header', 401, origin, allowedOrigins, 'MISSING_AUTH');
    }

    const sessionToken = authHeader.substring(7);

    const session = await getSession(env, sessionToken);
    if (session) {
      // セッションを削除
      await deleteSession(env, sessionToken);

      // ユーザートークンからこのセッションを削除
      const userToken = await getUserToken(env, session.userId);
      if (userToken) {
        userToken.sessionTokens = userToken.sessionTokens.filter(t => t !== sessionToken);
        if (userToken.sessionTokens.length > 0) {
          await setUserToken(env, session.userId, userToken);
        } else {
          // セッションがなくなったらユーザートークンも削除
          await deleteUserToken(env, session.userId);
        }
      }

      // メンバーシップキャッシュも削除
      await env.SESSIONS.delete(`membership:${session.userId}`);
      console.log(`Revoke: Session deleted for user ${session.userId}`);
    } else {
      console.log('Revoke: Session not found, treating as already revoked');
    }

    return successResponse({
      success: true,
      message: 'Session revoked successfully',
    }, origin, allowedOrigins);

  } catch (error) {
    console.error('Revoke error:', error instanceof Error ? `${error.name}: ${error.message}` : String(error));
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}

/**
 * ユーザーの全セッションを無効化（緊急用）
 * POST /api/patreon/revoke-all
 *
 * シングルデバイス強制下では通常1セッションのみだが、
 * 不正アクセスが疑われる場合などに全セッションを強制無効化
 */
async function handlePatreonRevokeAll(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    const authHeader = request.headers.get('Authorization');
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      return errorResponse('Missing or invalid Authorization header', 401, origin, allowedOrigins, 'MISSING_AUTH');
    }

    const sessionToken = authHeader.substring(7);

    const session = await getSession(env, sessionToken);
    if (!session) {
      return errorResponse('Invalid session', 401, origin, allowedOrigins, 'SESSION_INVALID');
    }

    // ユーザーの全セッションを無効化
    const revokedCount = await revokeAllUserSessions(env, session.userId);

    console.log(`Revoke-all: ${revokedCount} sessions revoked for user ${session.userId}`);

    return successResponse({
      success: true,
      message: 'All sessions revoked successfully',
      revoked_count: revokedCount,
    }, origin, allowedOrigins);

  } catch (error) {
    console.error('Revoke-all error:', error instanceof Error ? `${error.name}: ${error.message}` : String(error));
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}

// ============================================
// Issue #287: JWT認証エンドポイント
// ============================================

/**
 * SessionTokenをJWTアクセストークンに交換
 * POST /api/auth/token
 *
 * リクエスト:
 * - Authorization: Bearer {sessionToken}
 *
 * レスポンス:
 * - accessToken: JWTアクセストークン（15分TTL）
 * - refreshToken: リフレッシュトークン（30日TTL、1回使用で無効化）
 * - expiresIn: アクセストークン有効期限（秒）
 * - tokenType: "Bearer"
 */
async function handleAuthToken(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  // JWT_SECRET設定チェック
  if (!env.JWT_SECRET) {
    console.error('JWT_SECRET is not configured');
    return errorResponse('JWT authentication not available', 503, origin, allowedOrigins, 'JWT_NOT_CONFIGURED');
  }

  try {
    // SessionTokenを取得
    const authHeader = request.headers.get('Authorization');
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      return errorResponse('Missing or invalid Authorization header', 401, origin, allowedOrigins, 'MISSING_AUTH');
    }

    const sessionToken = authHeader.substring(7);

    // SessionToken検証
    const session = await getSession(env, sessionToken);
    if (!session) {
      return errorResponse('Invalid or expired session', 401, origin, allowedOrigins, 'SESSION_INVALID');
    }

    // ユーザーのプラン情報を取得
    const membership = await getMembershipFromSession(env, session);
    const plan = membership?.plan || 'Free';

    // ボーナストークン有無をチェック
    const supabase = getSupabaseClient(env);
    let hasBonusTokens = false;
    if (supabase) {
      const { data: bonusStatus } = await supabase.rpc('get_bonus_status_by_patreon', {
        p_patreon_user_id: session.userId,
      });
      hasBonusTokens = bonusStatus?.has_active_bonus || false;
    }

    // JWTアクセストークン生成
    const accessToken = await generateAccessToken(env, session.userId, plan, hasBonusTokens);

    // リフレッシュトークン生成
    const refreshToken = await generateRefreshToken(env, session.userId, sessionToken);

    console.log(`JWT issued: userId=${session.userId}, plan=${plan}, hasBonusTokens=${hasBonusTokens}`);

    const response: AuthTokenResponse = {
      accessToken,
      refreshToken,
      expiresIn: JWT_ACCESS_TOKEN_TTL_SECONDS,
      tokenType: 'Bearer',
    };

    return successResponse(response, origin, allowedOrigins);

  } catch (error) {
    console.error('Auth token error:', error instanceof Error ? `${error.name}: ${error.message}` : String(error));
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}

/**
 * リフレッシュトークンを使用して新しいJWTを発行
 * POST /api/auth/refresh
 *
 * リクエスト（JSON body）:
 * - refreshToken: リフレッシュトークン
 *
 * レスポンス:
 * - accessToken: 新しいJWTアクセストークン
 * - refreshToken: 新しいリフレッシュトークン
 * - expiresIn: アクセストークン有効期限（秒）
 * - tokenType: "Bearer"
 */
async function handleAuthRefresh(
  request: Request,
  env: Env,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  // JWT_SECRET設定チェック
  if (!env.JWT_SECRET) {
    console.error('JWT_SECRET is not configured');
    return errorResponse('JWT authentication not available', 503, origin, allowedOrigins, 'JWT_NOT_CONFIGURED');
  }

  try {
    // リクエストボディからリフレッシュトークンを取得
    const body = await request.json() as AuthRefreshRequest;
    if (!body.refreshToken) {
      return errorResponse('Missing refreshToken', 400, origin, allowedOrigins, 'MISSING_REFRESH_TOKEN');
    }

    // リフレッシュトークン検証（1回使用で無効化）
    const refreshData = await validateAndConsumeRefreshToken(env, body.refreshToken);
    if (!refreshData) {
      return errorResponse('Invalid or expired refresh token', 401, origin, allowedOrigins, 'INVALID_REFRESH_TOKEN');
    }

    // 元のSessionTokenがまだ有効か確認
    const session = await getSession(env, refreshData.sessionToken);
    if (!session) {
      console.warn(`Session no longer valid for refresh: userId=${refreshData.userId}`);
      return errorResponse('Session expired, please login again', 401, origin, allowedOrigins, 'SESSION_EXPIRED');
    }

    // ユーザーのプラン情報を取得（最新の状態を反映）
    const membership = await getMembershipFromSession(env, session);
    const plan = membership?.plan || 'Free';

    // ボーナストークン有無をチェック
    const supabase = getSupabaseClient(env);
    let hasBonusTokens = false;
    if (supabase) {
      const { data: bonusStatus } = await supabase.rpc('get_bonus_status_by_patreon', {
        p_patreon_user_id: session.userId,
      });
      hasBonusTokens = bonusStatus?.has_active_bonus || false;
    }

    // 新しいJWTアクセストークン生成
    const newAccessToken = await generateAccessToken(env, session.userId, plan, hasBonusTokens);

    // 新しいリフレッシュトークン生成
    const newRefreshToken = await generateRefreshToken(env, session.userId, refreshData.sessionToken);

    console.log(`JWT refreshed: userId=${session.userId}, plan=${plan}`);

    const response: AuthTokenResponse = {
      accessToken: newAccessToken,
      refreshToken: newRefreshToken,
      expiresIn: JWT_ACCESS_TOKEN_TTL_SECONDS,
      tokenType: 'Bearer',
    };

    return successResponse(response, origin, allowedOrigins);

  } catch (error) {
    console.error('Auth refresh error:', error instanceof Error ? `${error.name}: ${error.message}` : String(error));
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}
