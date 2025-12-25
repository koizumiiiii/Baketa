/**
 * Baketa Patreon Relay Server
 * Cloudflare Workers上で動作するPatreon OAuth認証プロキシ
 *
 * 機能:
 * - OAuth認証コードをトークンに交換
 * - リフレッシュトークンによるアクセストークン更新
 * - メンバーシップ情報取得（Tier判定）
 * - セキュアなセッション管理
 */

// ============================================
// 定数
// ============================================

const PATREON_API_BASE = 'https://www.patreon.com/api/oauth2';
const PATREON_TOKEN_URL = `${PATREON_API_BASE}/token`;
const PATREON_IDENTITY_URL = `${PATREON_API_BASE}/v2/identity`;
const PATREON_IDENTITY_PARAMS = 'include=memberships.currently_entitled_tiers,memberships.campaign&fields[user]=email,full_name&fields[member]=patron_status,currently_entitled_amount_cents,next_charge_date,campaign_lifetime_support_cents&fields[tier]=title,amount_cents';

const SESSION_TTL_SECONDS = 30 * 24 * 60 * 60; // 30 days

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
  API_KEY: string;
  SESSIONS: KVNamespace;
}

interface SessionData {
  accessToken: string;
  refreshToken: string;
  expiresAt: number;
  userId: string;
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

/** Patreon API エラー */
class PatreonApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message);
    this.name = 'PatreonApiError';
  }
}

// ============================================
// APIキー検証
// ============================================

function validateApiKey(request: Request, env: Env): boolean {
  if (!env.API_KEY) {
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
      return successResponse({ status: 'ok', timestamp: new Date().toISOString() }, origin, allowedOrigins);
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
    const body = await request.json() as { code?: string; redirect_uri?: string };
    const { code, redirect_uri } = body;

    if (!code || !redirect_uri) {
      return errorResponse('Missing required fields: code, redirect_uri', 400, origin, allowedOrigins);
    }

    const tokenResponse = await fetch(PATREON_TOKEN_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        code,
        grant_type: 'authorization_code',
        client_id: env.PATREON_CLIENT_ID,
        client_secret: env.PATREON_CLIENT_SECRET,
        redirect_uri,
      }),
    });

    if (!tokenResponse.ok) {
      const errorText = await tokenResponse.text();
      console.error('Patreon token exchange failed:', errorText);
      return errorResponse('Token exchange failed', tokenResponse.status, origin, allowedOrigins);
    }

    const tokenData = await tokenResponse.json();
    return successResponse(tokenData, origin, allowedOrigins);

  } catch (error) {
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
    const body = await request.json() as { refresh_token?: string };
    const { refresh_token } = body;

    if (!refresh_token) {
      return errorResponse('Missing required field: refresh_token', 400, origin, allowedOrigins);
    }

    const tokenResponse = await fetch(PATREON_TOKEN_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        refresh_token,
        grant_type: 'refresh_token',
        client_id: env.PATREON_CLIENT_ID,
        client_secret: env.PATREON_CLIENT_SECRET,
      }),
    });

    if (!tokenResponse.ok) {
      const errorText = await tokenResponse.text();
      console.error('Patreon token refresh failed:', errorText);
      return errorResponse('Token refresh failed', tokenResponse.status, origin, allowedOrigins);
    }

    const tokenData = await tokenResponse.json();
    return successResponse(tokenData, origin, allowedOrigins);

  } catch (error) {
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
    const body = await request.json() as { code?: string; state?: string; redirect_uri?: string };
    const { code, redirect_uri } = body;

    if (!code || !redirect_uri) {
      return errorResponse('Missing required fields: code, redirect_uri', 400, origin, allowedOrigins, 'MISSING_FIELDS');
    }

    console.log(`Patreon exchange request received: redirect_uri=${redirect_uri}`);

    // Step 1: トークン交換
    const tokenResponse = await fetch(PATREON_TOKEN_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        code,
        grant_type: 'authorization_code',
        client_id: env.PATREON_CLIENT_ID,
        client_secret: env.PATREON_CLIENT_SECRET,
        redirect_uri,
      }),
    });

    if (!tokenResponse.ok) {
      const errorText = await tokenResponse.text();
      console.error(`Patreon token exchange failed: status=${tokenResponse.status}, body=${errorText}`);
      return errorResponse(`Token exchange failed (status: ${tokenResponse.status})`, tokenResponse.status, origin, allowedOrigins, 'TOKEN_EXCHANGE_FAILED');
    }

    const tokenData = await tokenResponse.json() as PatreonTokenResponse;
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
      return errorResponse(`Failed to fetch user identity (status: ${error.status})`, error.status, origin, allowedOrigins, 'IDENTITY_FETCH_FAILED');
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
    const body = await request.json() as { session_token?: string };
    const { session_token } = body;

    if (!session_token) {
      return errorResponse('Missing required field: session_token', 400, origin, allowedOrigins);
    }

    const session = await getSession(env, session_token);
    if (!session) {
      return errorResponse('Invalid or expired session', 401, origin, allowedOrigins, 'SESSION_INVALID');
    }

    if (Date.now() > session.expiresAt) {
      await deleteSession(env, session_token);
      return errorResponse('Session expired', 401, origin, allowedOrigins, 'SESSION_EXPIRED');
    }

    try {
      const identityData = await fetchPatreonIdentity(session.accessToken);
      const membership = parsePatreonMembership(identityData);

      return successResponse({
        SessionValid: true,
        PatreonUserId: membership.userId,
        Email: membership.email,
        FullName: membership.fullName,
        Plan: membership.plan,
        TierId: membership.tierId,
        PatronStatus: membership.patronStatus,
        NextChargeDate: membership.nextChargeDate,
        SessionExpiresAt: session.expiresAt,
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

  const createErrorResponse = (error: string, errorCode: string, status: number) => {
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
      return createErrorResponse('Missing or invalid Authorization header', 'MISSING_AUTH', 401);
    }

    const sessionToken = authHeader.substring(7);

    const session = await getSession(env, sessionToken);
    if (!session) {
      return createErrorResponse('Invalid or expired session', 'SESSION_EXPIRED', 401);
    }

    if (Date.now() > session.expiresAt) {
      await deleteSession(env, sessionToken);
      return createErrorResponse('Session expired', 'SESSION_EXPIRED', 401);
    }

    try {
      const identityData = await fetchPatreonIdentity(session.accessToken);
      const membership = parsePatreonMembership(identityData);

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
        return createErrorResponse('Patreon token expired, re-authentication required', 'PATREON_TOKEN_EXPIRED', 401);
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
      await deleteSession(env, sessionToken);
      console.log(`Revoke: Session deleted for user ${session.userId}`);
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
