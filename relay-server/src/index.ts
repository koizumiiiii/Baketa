/**
 * Baketa Patreon Relay Server
 * Cloudflare Workers上で動作するPatreon OAuth認証プロキシ
 *
 * 機能:
 * - OAuth認証コードをトークンに交換
 * - リフレッシュトークンによるアクセストークン更新
 * - メンバーシップ情報取得（Tier判定）
 */

export interface Env {
  PATREON_CLIENT_ID: string;
  PATREON_CLIENT_SECRET: string;
  ALLOWED_ORIGINS: string;
}

// CORS headers
const corsHeaders = (origin: string, allowedOrigins: string) => {
  const allowed = allowedOrigins.split(',').map(o => o.trim());
  const isAllowed = allowed.includes('*') || allowed.includes(origin);

  return {
    'Access-Control-Allow-Origin': isAllowed ? origin : allowed[0],
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type, Authorization',
    'Access-Control-Max-Age': '86400',
  };
};

// Error response helper
const errorResponse = (message: string, status: number, origin: string, allowedOrigins: string) => {
  return new Response(
    JSON.stringify({ error: message }),
    {
      status,
      headers: {
        'Content-Type': 'application/json',
        ...corsHeaders(origin, allowedOrigins),
      },
    }
  );
};

// Success response helper
const successResponse = (data: object, origin: string, allowedOrigins: string) => {
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
};

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const origin = request.headers.get('Origin') || '*';

    // Handle CORS preflight
    if (request.method === 'OPTIONS') {
      return new Response(null, {
        status: 204,
        headers: corsHeaders(origin, env.ALLOWED_ORIGINS || '*'),
      });
    }

    // Route handling
    switch (url.pathname) {
      case '/oauth/token':
        return handleTokenExchange(request, env, origin);
      case '/oauth/refresh':
        return handleTokenRefresh(request, env, origin);
      case '/api/membership':
        return handleMembershipCheck(request, env, origin);
      case '/api/patreon/exchange':
        return handlePatreonExchange(request, env, origin);
      case '/health':
        return successResponse({ status: 'ok', timestamp: new Date().toISOString() }, origin, env.ALLOWED_ORIGINS || '*');
      default:
        return errorResponse('Not Found', 404, origin, env.ALLOWED_ORIGINS || '*');
    }
  },
};

/**
 * OAuth認証コードをトークンに交換
 * POST /oauth/token
 * Body: { code: string, redirect_uri: string }
 */
async function handleTokenExchange(request: Request, env: Env, origin: string): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, env.ALLOWED_ORIGINS || '*');
  }

  try {
    const body = await request.json() as { code?: string; redirect_uri?: string };
    const { code, redirect_uri } = body;

    if (!code || !redirect_uri) {
      return errorResponse('Missing required fields: code, redirect_uri', 400, origin, env.ALLOWED_ORIGINS || '*');
    }

    // Exchange code for tokens with Patreon
    const tokenResponse = await fetch('https://www.patreon.com/api/oauth2/token', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded',
      },
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
      return errorResponse('Token exchange failed', tokenResponse.status, origin, env.ALLOWED_ORIGINS || '*');
    }

    const tokenData = await tokenResponse.json();
    return successResponse(tokenData, origin, env.ALLOWED_ORIGINS || '*');

  } catch (error) {
    console.error('Token exchange error:', error);
    return errorResponse('Internal server error', 500, origin, env.ALLOWED_ORIGINS || '*');
  }
}

/**
 * リフレッシュトークンでアクセストークンを更新
 * POST /oauth/refresh
 * Body: { refresh_token: string }
 */
async function handleTokenRefresh(request: Request, env: Env, origin: string): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, env.ALLOWED_ORIGINS || '*');
  }

  try {
    const body = await request.json() as { refresh_token?: string };
    const { refresh_token } = body;

    if (!refresh_token) {
      return errorResponse('Missing required field: refresh_token', 400, origin, env.ALLOWED_ORIGINS || '*');
    }

    // Refresh tokens with Patreon
    const tokenResponse = await fetch('https://www.patreon.com/api/oauth2/token', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded',
      },
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
      return errorResponse('Token refresh failed', tokenResponse.status, origin, env.ALLOWED_ORIGINS || '*');
    }

    const tokenData = await tokenResponse.json();
    return successResponse(tokenData, origin, env.ALLOWED_ORIGINS || '*');

  } catch (error) {
    console.error('Token refresh error:', error);
    return errorResponse('Internal server error', 500, origin, env.ALLOWED_ORIGINS || '*');
  }
}

/**
 * メンバーシップ情報を取得
 * GET /api/membership
 * Header: Authorization: Bearer <access_token>
 */
async function handleMembershipCheck(request: Request, env: Env, origin: string): Promise<Response> {
  if (request.method !== 'GET') {
    return errorResponse('Method not allowed', 405, origin, env.ALLOWED_ORIGINS || '*');
  }

  try {
    const authHeader = request.headers.get('Authorization');
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      return errorResponse('Missing or invalid Authorization header', 401, origin, env.ALLOWED_ORIGINS || '*');
    }

    const accessToken = authHeader.substring(7);

    // Get user identity and memberships from Patreon
    const identityResponse = await fetch(
      'https://www.patreon.com/api/oauth2/v2/identity?include=memberships.currently_entitled_tiers&fields[user]=email,full_name&fields[member]=patron_status,currently_entitled_amount_cents&fields[tier]=title,amount_cents',
      {
        headers: {
          'Authorization': `Bearer ${accessToken}`,
        },
      }
    );

    if (!identityResponse.ok) {
      const errorText = await identityResponse.text();
      console.error('Patreon identity fetch failed:', errorText);
      return errorResponse('Failed to fetch membership', identityResponse.status, origin, env.ALLOWED_ORIGINS || '*');
    }

    const identityData = await identityResponse.json();

    // Parse the response to extract relevant membership info
    const membership = parseMembershipData(identityData);

    return successResponse(membership, origin, env.ALLOWED_ORIGINS || '*');

  } catch (error) {
    console.error('Membership check error:', error);
    return errorResponse('Internal server error', 500, origin, env.ALLOWED_ORIGINS || '*');
  }
}

/**
 * Patreon認証コード交換とユーザー情報取得（統合エンドポイント）
 * POST /api/patreon/exchange
 * Body: { code: string, state: string, redirect_uri: string }
 *
 * PatreonOAuthService.ExchangeCodeForSessionAsync が期待する形式でレスポンスを返す
 */
async function handlePatreonExchange(request: Request, env: Env, origin: string): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, env.ALLOWED_ORIGINS || '*');
  }

  try {
    const body = await request.json() as { code?: string; state?: string; redirect_uri?: string };
    const { code, state, redirect_uri } = body;

    if (!code || !redirect_uri) {
      return errorResponse('Missing required fields: code, redirect_uri', 400, origin, env.ALLOWED_ORIGINS || '*');
    }

    console.log('Patreon exchange request received');

    // Step 1: Exchange code for tokens with Patreon
    const tokenResponse = await fetch('https://www.patreon.com/api/oauth2/token', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded',
      },
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
      return errorResponse(`Token exchange failed: ${errorText}`, tokenResponse.status, origin, env.ALLOWED_ORIGINS || '*');
    }

    const tokenData = await tokenResponse.json() as {
      access_token: string;
      refresh_token: string;
      expires_in: number;
      scope: string;
      token_type: string;
    };

    console.log('Token exchange successful, fetching identity...');

    // Step 2: Fetch user identity and memberships
    const identityResponse = await fetch(
      'https://www.patreon.com/api/oauth2/v2/identity?include=memberships.currently_entitled_tiers,memberships.campaign&fields[user]=email,full_name&fields[member]=patron_status,currently_entitled_amount_cents,next_charge_date,campaign_lifetime_support_cents&fields[tier]=title,amount_cents',
      {
        headers: {
          'Authorization': `Bearer ${tokenData.access_token}`,
        },
      }
    );

    if (!identityResponse.ok) {
      const errorText = await identityResponse.text();
      console.error('Patreon identity fetch failed:', errorText);
      return errorResponse(`Failed to fetch user identity: ${errorText}`, identityResponse.status, origin, env.ALLOWED_ORIGINS || '*');
    }

    const identityData = await identityResponse.json() as any;
    console.log('Identity fetched successfully');

    // Step 3: Parse membership data and determine plan
    const user = identityData.data;
    const included = identityData.included || [];

    // Find active membership
    const membership = included.find((item: any) =>
      item.type === 'member' && item.attributes?.patron_status === 'active_patron'
    );

    // Find entitled tiers
    const tiers = included.filter((item: any) => item.type === 'tier');

    // Get the highest tier (by amount)
    const highestTier = tiers.reduce((max: any, tier: any) => {
      const amount = tier.attributes?.amount_cents || 0;
      return amount > (max?.attributes?.amount_cents || 0) ? tier : max;
    }, null);

    // Determine plan based on tier amount
    let plan = 'Free';
    const amountCents = highestTier?.attributes?.amount_cents || 0;
    if (amountCents >= 500) {
      plan = 'Premia';
    } else if (amountCents >= 300) {
      plan = 'Pro';
    } else if (amountCents >= 100) {
      plan = 'Standard';
    }

    // Build response in format expected by PatreonOAuthService.SessionTokenResponse
    const sessionResponse = {
      PatreonUserId: user?.id || '',
      Email: user?.attributes?.email || '',
      FullName: user?.attributes?.full_name || '',
      SessionToken: tokenData.access_token,  // Using access_token as session token
      RefreshToken: tokenData.refresh_token,
      ExpiresIn: tokenData.expires_in,
      Plan: plan,
      TierId: highestTier?.id || '',
      PatronStatus: membership?.attributes?.patron_status || 'not_patron',
      NextChargeDate: membership?.attributes?.next_charge_date || null,
      EntitledAmountCents: membership?.attributes?.currently_entitled_amount_cents || 0,
    };

    console.log(`Exchange successful: UserId=${sessionResponse.PatreonUserId}, Plan=${plan}`);

    return successResponse(sessionResponse, origin, env.ALLOWED_ORIGINS || '*');

  } catch (error) {
    console.error('Patreon exchange error:', error);
    return errorResponse(`Internal server error: ${error}`, 500, origin, env.ALLOWED_ORIGINS || '*');
  }
}

/**
 * Patreon APIレスポンスからメンバーシップ情報を抽出
 */
function parseMembershipData(data: any): object {
  const user = data.data;
  const included = data.included || [];

  // Find active membership
  const membership = included.find((item: any) =>
    item.type === 'member' && item.attributes?.patron_status === 'active_patron'
  );

  // Find entitled tiers
  const tiers = included.filter((item: any) => item.type === 'tier');

  // Get the highest tier (by amount)
  const highestTier = tiers.reduce((max: any, tier: any) => {
    const amount = tier.attributes?.amount_cents || 0;
    return amount > (max?.attributes?.amount_cents || 0) ? tier : max;
  }, null);

  return {
    user_id: user?.id,
    email: user?.attributes?.email,
    full_name: user?.attributes?.full_name,
    is_active_patron: !!membership,
    patron_status: membership?.attributes?.patron_status || 'not_patron',
    entitled_amount_cents: membership?.attributes?.currently_entitled_amount_cents || 0,
    tier: highestTier ? {
      id: highestTier.id,
      title: highestTier.attributes?.title,
      amount_cents: highestTier.attributes?.amount_cents,
    } : null,
    raw_tiers: tiers.map((t: any) => ({
      id: t.id,
      title: t.attributes?.title,
      amount_cents: t.attributes?.amount_cents,
    })),
  };
}
