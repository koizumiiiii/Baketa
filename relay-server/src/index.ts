/**
 * Baketa Patreon Relay Server
 * Cloudflare Workers上で動作するPatreon OAuth認証プロキシ
 *
 * 機能:
 * - OAuth認証コードをトークンに交換
 * - リフレッシュトークンによるアクセストークン更新
 * - メンバーシップ情報取得（Tier判定）
 * - セキュアなセッション管理
 *
 * セキュリティ:
 * - タイミング攻撃対策（timingSafeCompare）
 * - redirect_uri ホワイトリスト検証
 * - 本番環境でのAPI_KEY必須化
 * - stateパラメータはクライアント側（C#）で検証
 */

// ============================================
// 定数
// ============================================

const PATREON_API_BASE = 'https://www.patreon.com/api/oauth2';
const PATREON_TOKEN_URL = `${PATREON_API_BASE}/token`;
const PATREON_IDENTITY_URL = `${PATREON_API_BASE}/v2/identity`;
const PATREON_IDENTITY_PARAMS = 'include=memberships.currently_entitled_tiers,memberships.campaign&fields[user]=email,full_name&fields[member]=patron_status,currently_entitled_amount_cents,next_charge_date,campaign_lifetime_support_cents&fields[tier]=title,amount_cents';

const SESSION_TTL_SECONDS = 30 * 24 * 60 * 60; // 30 days
const IDENTITY_CACHE_TTL_SECONDS = 5 * 60; // 5 minutes

/** Tier金額しきい値（円） */
const TIER_AMOUNTS = {
  PREMIA: 500,
  PRO: 300,
  STANDARD: 100,
} as const;

type PlanType = 'Free' | 'Standard' | 'Pro' | 'Premia';

// ============================================
// 型定義
// ============================================

export interface Env {
  PATREON_CLIENT_ID: string;
  PATREON_CLIENT_SECRET: string;
  ALLOWED_ORIGINS: string;
  ALLOWED_REDIRECT_URIS?: string;  // カンマ区切りのホワイトリスト
  API_KEY: string;
  ENVIRONMENT?: string;  // 'production' | 'development'
  SESSIONS: KVNamespace;
}

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
 * 文字列の長さに関わらず一定時間で比較を完了する
 */
function timingSafeCompare(a: string, b: string): boolean {
  if (a.length !== b.length) {
    // 長さが異なる場合でも同等の時間をかける
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
 * セッショントークン生成（暗号学的に安全な乱数）
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
  // ホワイトリスト未設定の場合（開発環境）
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
  state?: string;  // Note: stateはクライアント側（C#）で検証される
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
  return { success: true, data: { code: obj.code, redirect_uri: obj.redirect_uri, state } };
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

/** メンバーシップキャッシュを取得 */
async function getCachedMembership(env: Env, userId: string): Promise<CachedMembership | null> {
  try {
    const cacheKey = `membership:${userId}`;
    const data = await env.SESSIONS.get<CachedMembership>(cacheKey, 'json');
    if (data && data.membership && typeof data.cachedAt === 'number') {
      // キャッシュが有効期限内かチェック
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

// ============================================
// 共通ビジネスロジック
// ============================================

/**
 * 金額からプランを判定
 */
function determinePlan(amountCents: number): PlanType {
  if (amountCents >= TIER_AMOUNTS.PREMIA) return 'Premia';
  if (amountCents >= TIER_AMOUNTS.PRO) return 'Pro';
  if (amountCents >= TIER_AMOUNTS.STANDARD) return 'Standard';
  return 'Free';
}

/**
 * Patreon Identity APIレスポンスからメンバーシップ情報を抽出
 */
function parsePatreonMembership(identityData: PatreonIdentityResponse): ParsedMembership {
  const user = identityData.data;
  const included = identityData.included || [];

  // アクティブなメンバーシップを検索
  const activeMembership = included.find(
    (item): item is PatreonMember =>
      item.type === 'member' && item.attributes.patron_status === 'active_patron'
  );

  // Tierを検索
  const tiers = included.filter((item): item is PatreonTier => item.type === 'tier');

  // 最高額のTierを取得
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

/** Patreon API エラー */
class PatreonApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message);
    this.name = 'PatreonApiError';
  }
}

/**
 * Patreon トークンAPIを呼び出す共通関数
 */
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

/**
 * Patreon Identity APIを呼び出してメンバーシップ情報を取得
 */
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

/**
 * セッションからメンバーシップ情報を取得（キャッシュ対応）
 */
async function getMembershipFromSession(
  env: Env,
  session: SessionData,
  useCache: boolean = true
): Promise<ParsedMembership> {
  // キャッシュをチェック
  if (useCache) {
    const cached = await getCachedMembership(env, session.userId);
    if (cached) {
      console.log(`Cache hit for user ${session.userId}`);
      return cached.membership;
    }
  }

  // Patreon APIから取得
  const identityData = await fetchPatreonIdentity(session.accessToken);
  const membership = parsePatreonMembership(identityData);

  // キャッシュに保存
  await setCachedMembership(env, session.userId, membership);

  return membership;
}

/**
 * セッショントークンを検証してセッションを取得
 */
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
  // 本番環境ではAPI_KEY必須
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

  // 本番環境では追加の検証
  if (env.ENVIRONMENT === 'production') {
    if (!env.API_KEY) missingVars.push('API_KEY');
    if (!env.ALLOWED_REDIRECT_URIS) missingVars.push('ALLOWED_REDIRECT_URIS');
  }

  return {
    valid: missingVars.length === 0,
    missingVars,
  };
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

    // ヘルスチェック（認証不要）
    if (url.pathname === '/health') {
      return successResponse({
        status: 'ok',
        timestamp: new Date().toISOString(),
        environment: env.ENVIRONMENT || 'development',
      }, origin, allowedOrigins);
    }

    // 環境変数検証
    const envValidation = validateEnvironment(env);
    if (!envValidation.valid) {
      console.error(`Missing environment variables: ${envValidation.missingVars.join(', ')}`);
      return errorResponse('Server configuration error', 500, origin, allowedOrigins, 'CONFIG_ERROR');
    }

    // APIキー検証
    if (!validateApiKey(request, env)) {
      return errorResponse('Unauthorized: Invalid API Key', 401, origin, allowedOrigins, 'INVALID_API_KEY');
    }

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

    // redirect_uri ホワイトリスト検証
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
      is_active_patron: membership.patronStatus === 'active_patron',
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
 * Note: stateパラメータはクライアント側（C# PatreonOAuthService）で検証されます。
 * このサーバーはstateの生成・検証には関与しません。
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

    const { code, redirect_uri } = validation.data;

    // redirect_uri ホワイトリスト検証
    if (!validateRedirectUri(redirect_uri, env)) {
      console.error(`Invalid redirect_uri: ${redirect_uri}`);
      return errorResponse('Invalid redirect_uri', 400, origin, allowedOrigins, 'INVALID_REDIRECT_URI');
    }

    console.log(`Patreon exchange request received: redirect_uri=${redirect_uri}`);

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

    // Step 4: セッション作成
    const sessionToken = generateSessionToken();

    await setSession(env, sessionToken, {
      accessToken: tokenData.access_token,
      refreshToken: tokenData.refresh_token,
      expiresAt: Date.now() + (tokenData.expires_in * 1000),
      userId: membership.userId,
    });

    // メンバーシップをキャッシュに保存
    await setCachedMembership(env, membership.userId, membership);

    // C#側はsnake_caseのJSONプロパティ名を期待
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

    console.log(`Exchange successful: UserId=${membership.userId}, Plan=${membership.plan}`);

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
      const membership = await getMembershipFromSession(env, sessionResult.session, true);

      // C#のLicenseStatusResponseが期待する形式（snake_case）
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
      // セッションとキャッシュを削除
      await deleteSession(env, sessionToken);
      await env.SESSIONS.delete(`membership:${session.userId}`);
      console.log(`Revoke: Session and cache deleted for user ${session.userId}`);
    } else {
      console.log('Revoke: Session not found, treating as already revoked');
    }

    // Note: Patreon APIにはトークン無効化エンドポイントがないため、
    // サーバー側のセッション削除のみで対応

    return successResponse({
      success: true,
      message: 'Session revoked successfully',
    }, origin, allowedOrigins);

  } catch (error) {
    console.error('Revoke error:', error instanceof Error ? `${error.name}: ${error.message}` : String(error));
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}
