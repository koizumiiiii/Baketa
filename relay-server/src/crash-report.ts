/**
 * [Issue #252] クラッシュレポート受信エンドポイント
 * POST /api/crash-report
 *
 * 機能:
 * - クライアントからのクラッシュレポート受信
 * - Supabaseへの保存
 * - レートリミット（IP単位で10件/分）
 *
 * セキュリティ:
 * - APIキー認証不要（クラッシュ時は設定ロード前の可能性があるため）
 *   → index.tsでAPIキー検証前に呼び出される
 * - サイズ制限（ログは最大100KB）
 * - IPベースのレートリミット（DoS対策）
 * - Supabase RLS（service_roleのみ書き込み可能）
 */

import { createClient, SupabaseClient } from '@supabase/supabase-js';

// ============================================
// 定数
// ============================================

const MAX_LOG_SIZE = 100 * 1024; // 100KB
const RATE_LIMIT_WINDOW_SECONDS = 60;
const RATE_LIMIT_MAX_REPORTS = 10; // 1分間に10件まで

// ============================================
// 型定義
// ============================================

export interface CrashReportEnv {
  SESSIONS: KVNamespace;
  SUPABASE_URL?: string;
  SUPABASE_SERVICE_KEY?: string;
  API_KEY: string;
  ALLOWED_ORIGINS: string;
  ENVIRONMENT?: string;
  DISCORD_WEBHOOK_URL?: string;
}

/** クラッシュレポートリクエスト */
interface CrashReportRequest {
  report_id: string;
  crash_timestamp: string;
  error_message: string;
  stack_trace?: string;
  app_version: string;
  os_version: string;
  include_system_info: boolean;
  include_logs: boolean;
  system_info?: SystemInfo;
  logs?: string;
}

/** システム情報 */
interface SystemInfo {
  cpu?: string;
  gpu?: string;
  ram_mb?: number;
  available_ram_mb?: number;
  dotnet_version?: string;
  is_64bit?: boolean;
}

/** レートリミットデータ */
interface RateLimitData {
  count: number;
  windowStart: number;
}

/** バリデーション結果 */
interface ValidationResult<T> {
  success: boolean;
  data?: T;
  error?: string;
}

// ============================================
// ユーティリティ
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
// レートリミット
// ============================================

async function checkCrashReportRateLimit(
  env: CrashReportEnv,
  clientIP: string
): Promise<{ limited: boolean; remaining: number; resetAt: number }> {
  const key = `crashreport:ratelimit:${clientIP}`;
  const now = Date.now();
  const windowStart = Math.floor(now / (RATE_LIMIT_WINDOW_SECONDS * 1000)) * (RATE_LIMIT_WINDOW_SECONDS * 1000);
  const resetAt = windowStart + (RATE_LIMIT_WINDOW_SECONDS * 1000);

  try {
    const data = await env.SESSIONS.get<RateLimitData>(key, 'json');

    if (!data || data.windowStart !== windowStart) {
      await env.SESSIONS.put(key, JSON.stringify({ count: 1, windowStart }), {
        expirationTtl: RATE_LIMIT_WINDOW_SECONDS * 2,
      });
      return { limited: false, remaining: RATE_LIMIT_MAX_REPORTS - 1, resetAt };
    }

    if (data.count >= RATE_LIMIT_MAX_REPORTS) {
      return { limited: true, remaining: 0, resetAt };
    }

    await env.SESSIONS.put(key, JSON.stringify({ count: data.count + 1, windowStart }), {
      expirationTtl: RATE_LIMIT_WINDOW_SECONDS * 2,
    });
    return { limited: false, remaining: RATE_LIMIT_MAX_REPORTS - data.count - 1, resetAt };
  } catch {
    return { limited: false, remaining: RATE_LIMIT_MAX_REPORTS, resetAt };
  }
}

// ============================================
// バリデーション
// ============================================

function validateCrashReportRequest(body: unknown): ValidationResult<CrashReportRequest> {
  if (!body || typeof body !== 'object') {
    return { success: false, error: 'Invalid request body' };
  }

  const obj = body as Record<string, unknown>;

  // 必須フィールド
  if (typeof obj.report_id !== 'string' || !obj.report_id) {
    return { success: false, error: 'Missing or invalid field: report_id' };
  }
  if (typeof obj.crash_timestamp !== 'string' || !obj.crash_timestamp) {
    return { success: false, error: 'Missing or invalid field: crash_timestamp' };
  }
  if (typeof obj.error_message !== 'string') {
    return { success: false, error: 'Missing or invalid field: error_message' };
  }
  if (typeof obj.app_version !== 'string' || !obj.app_version) {
    return { success: false, error: 'Missing or invalid field: app_version' };
  }
  if (typeof obj.os_version !== 'string' || !obj.os_version) {
    return { success: false, error: 'Missing or invalid field: os_version' };
  }

  // UUID形式チェック
  const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!uuidPattern.test(obj.report_id)) {
    return { success: false, error: 'Invalid report_id format (expected UUID)' };
  }

  // オプションフィールド
  const stackTrace = typeof obj.stack_trace === 'string' ? obj.stack_trace : undefined;
  const includeSystemInfo = typeof obj.include_system_info === 'boolean' ? obj.include_system_info : false;
  const includeLogs = typeof obj.include_logs === 'boolean' ? obj.include_logs : false;

  // システム情報のバリデーション
  let systemInfo: SystemInfo | undefined;
  if (includeSystemInfo && obj.system_info && typeof obj.system_info === 'object') {
    const si = obj.system_info as Record<string, unknown>;
    systemInfo = {
      cpu: typeof si.cpu === 'string' ? si.cpu : undefined,
      gpu: typeof si.gpu === 'string' ? si.gpu : undefined,
      ram_mb: typeof si.ram_mb === 'number' ? si.ram_mb : undefined,
      available_ram_mb: typeof si.available_ram_mb === 'number' ? si.available_ram_mb : undefined,
      dotnet_version: typeof si.dotnet_version === 'string' ? si.dotnet_version : undefined,
      is_64bit: typeof si.is_64bit === 'boolean' ? si.is_64bit : undefined,
    };
  }

  // ログのバリデーション（サイズ制限）
  let logs: string | undefined;
  if (includeLogs && typeof obj.logs === 'string') {
    logs = obj.logs.length > MAX_LOG_SIZE
      ? obj.logs.substring(0, MAX_LOG_SIZE) + '\n... [truncated]'
      : obj.logs;
  }

  return {
    success: true,
    data: {
      report_id: obj.report_id,
      crash_timestamp: obj.crash_timestamp,
      error_message: obj.error_message,
      stack_trace: stackTrace,
      app_version: obj.app_version,
      os_version: obj.os_version,
      include_system_info: includeSystemInfo,
      include_logs: includeLogs,
      system_info: systemInfo,
      logs,
    },
  };
}

// ============================================
// Supabaseクライアント
// ============================================

function getSupabaseClient(env: CrashReportEnv): SupabaseClient | null {
  if (!env.SUPABASE_URL || !env.SUPABASE_SERVICE_KEY) {
    return null;
  }
  return createClient(env.SUPABASE_URL, env.SUPABASE_SERVICE_KEY, {
    auth: { persistSession: false }
  });
}

// ============================================
// 通知機能
// ============================================

/**
 * Discordに通知を送信
 */
async function sendDiscordNotification(
  env: CrashReportEnv,
  report: CrashReportRequest
): Promise<void> {
  if (!env.DISCORD_WEBHOOK_URL) return;

  try {
    const embed = {
      embeds: [{
        title: '🚨 クラッシュレポート受信',
        color: 0xFF0000,
        fields: [
          { name: 'Version', value: report.app_version, inline: true },
          { name: 'OS', value: report.os_version, inline: true },
          { name: 'Error', value: `\`\`\`${report.error_message.substring(0, 500)}\`\`\`` },
        ],
        footer: { text: `Report ID: ${report.report_id}` },
        timestamp: report.crash_timestamp,
      }]
    };

    await fetch(env.DISCORD_WEBHOOK_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(embed),
    });
  } catch (error) {
    console.error('Discord notification failed:', error);
  }
}

/**
 * Discordに通知を送信
 */
async function sendCrashNotifications(
  env: CrashReportEnv,
  report: CrashReportRequest
): Promise<void> {
  await sendDiscordNotification(env, report);
}

// ============================================
// メインハンドラ
// ============================================

/**
 * クラッシュレポート送信
 * POST /api/crash-report
 */
export async function handleCrashReport(
  request: Request,
  env: CrashReportEnv,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  if (request.method !== 'POST') {
    return errorResponse('Method not allowed', 405, origin, allowedOrigins);
  }

  try {
    // 1. レートリミットチェック
    const clientIP = request.headers.get('CF-Connecting-IP') || 'unknown';
    const rateLimit = await checkCrashReportRateLimit(env, clientIP);
    if (rateLimit.limited) {
      console.warn(`Crash report rate limited: IP=${clientIP}`);
      return new Response(
        JSON.stringify({ error: 'Too Many Requests', error_code: 'RATE_LIMITED' }),
        {
          status: 429,
          headers: {
            'Content-Type': 'application/json',
            'Retry-After': String(Math.ceil((rateLimit.resetAt - Date.now()) / 1000)),
            'X-RateLimit-Reset': String(Math.floor(rateLimit.resetAt / 1000)),
            ...corsHeaders(origin, allowedOrigins),
          },
        }
      );
    }

    // 2. リクエストバリデーション
    const body = await request.json();
    const validation = validateCrashReportRequest(body);
    if (!validation.success || !validation.data) {
      return errorResponse(validation.error || 'Invalid request', 400, origin, allowedOrigins, 'VALIDATION_ERROR');
    }

    const report = validation.data;

    // 3. Supabaseクライアント確認
    const supabase = getSupabaseClient(env);
    if (!supabase) {
      console.error('Supabase not configured for crash reports');
      return errorResponse('Crash report service not available', 503, origin, allowedOrigins, 'SERVICE_UNAVAILABLE');
    }

    // 4. Supabaseに保存
    const { error: insertError } = await supabase
      .from('crash_reports')
      .insert({
        id: report.report_id,
        crash_timestamp: report.crash_timestamp,
        error_message: report.error_message,
        stack_trace: report.stack_trace,
        app_version: report.app_version,
        os_version: report.os_version,
        include_system_info: report.include_system_info,
        include_logs: report.include_logs,
        system_info: report.system_info ? JSON.stringify(report.system_info) : null,
        logs: report.logs,
        client_ip: clientIP,
        created_at: new Date().toISOString(),
      });

    if (insertError) {
      // 重複レポートの場合は成功扱い
      if (insertError.code === '23505') { // unique_violation
        console.log(`Crash report already exists: ${report.report_id}`);
        return successResponse({
          success: true,
          report_id: report.report_id,
          message: 'Crash report already submitted',
        }, origin, allowedOrigins);
      }

      console.error('Supabase insert error:', insertError);
      return errorResponse('Failed to save crash report', 500, origin, allowedOrigins, 'DATABASE_ERROR');
    }

    console.log(`Crash report saved: id=${report.report_id}, version=${report.app_version}`);

    // 5. Discord通知（非同期、失敗しても処理は継続）
    await sendCrashNotifications(env, report);

    return successResponse({
      success: true,
      report_id: report.report_id,
      message: 'Crash report submitted successfully',
    }, origin, allowedOrigins);

  } catch (error) {
    console.error('Crash report error:', error);
    return errorResponse('Internal server error', 500, origin, allowedOrigins, 'INTERNAL_ERROR');
  }
}
