#include "pch.h"
#include "BaketaCaptureNative.h"

// グローバル状態
static bool g_initialized = false;
static std::mutex g_sessionMutex;
static std::unordered_map<int, std::unique_ptr<WindowsCaptureSession>> g_sessions;
static std::atomic<int> g_nextSessionId(1);
static std::string g_lastError;

/// <summary>
/// エラーメッセージを設定
/// </summary>
static void SetLastError(const std::string& message)
{
    g_lastError = message;
}

/// <summary>
/// ライブラリの初期化
/// </summary>
int BaketaCapture_Initialize()
{
    if (g_initialized)
    {
        return BAKETA_CAPTURE_SUCCESS;
    }

    try
    {
        // WinRT の初期化
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        
        // Windows Graphics Capture API のサポートチェック
        if (!winrt::GraphicsCaptureSession::IsSupported())
        {
            SetLastError("Windows Graphics Capture API is not supported on this system");
            return BAKETA_CAPTURE_ERROR_UNSUPPORTED;
        }

        g_initialized = true;
        SetLastError("");
        return BAKETA_CAPTURE_SUCCESS;
    }
    catch (const std::exception& e)
    {
        SetLastError(std::string("Failed to initialize: ") + e.what());
        return BAKETA_CAPTURE_ERROR_DEVICE;
    }
    catch (...)
    {
        SetLastError("Failed to initialize: Unknown error");
        return BAKETA_CAPTURE_ERROR_DEVICE;
    }
}

/// <summary>
/// ライブラリの終了処理
/// </summary>
void BaketaCapture_Shutdown()
{
    if (!g_initialized)
    {
        return;
    }

    {
        std::lock_guard<std::mutex> lock(g_sessionMutex);
        g_sessions.clear();
    }

    g_initialized = false;
    SetLastError("");
}

/// <summary>
/// ウィンドウキャプチャセッションを作成
/// </summary>
int BaketaCapture_CreateSession(void* hwnd, int* sessionId)
{
    if (!g_initialized)
    {
        SetLastError("Library not initialized");
        return BAKETA_CAPTURE_ERROR_DEVICE;
    }

    if (!hwnd || !sessionId)
    {
        SetLastError("Invalid parameters");
        return BAKETA_CAPTURE_ERROR_INVALID_WINDOW;
    }

    HWND windowHandle = static_cast<HWND>(hwnd);
    
    // ウィンドウの有効性チェック
    if (!IsWindow(windowHandle))
    {
        SetLastError("Invalid window handle");
        return BAKETA_CAPTURE_ERROR_INVALID_WINDOW;
    }

    try
    {
        int newSessionId = g_nextSessionId.fetch_add(1);
        auto session = std::make_unique<WindowsCaptureSession>(newSessionId, windowHandle);
        
        if (!session->Initialize())
        {
            SetLastError("Failed to initialize capture session");
            return BAKETA_CAPTURE_ERROR_DEVICE;
        }

        {
            std::lock_guard<std::mutex> lock(g_sessionMutex);
            g_sessions[newSessionId] = std::move(session);
        }

        *sessionId = newSessionId;
        SetLastError("");
        return BAKETA_CAPTURE_SUCCESS;
    }
    catch (const std::exception& e)
    {
        SetLastError(std::string("Failed to create session: ") + e.what());
        return BAKETA_CAPTURE_ERROR_DEVICE;
    }
    catch (...)
    {
        SetLastError("Failed to create session: Unknown error");
        return BAKETA_CAPTURE_ERROR_DEVICE;
    }
}

/// <summary>
/// フレームをキャプチャ
/// </summary>
int BaketaCapture_CaptureFrame(int sessionId, BaketaCaptureFrame* frame, int timeoutMs)
{
    if (!g_initialized)
    {
        SetLastError("Library not initialized");
        return BAKETA_CAPTURE_ERROR_DEVICE;
    }

    if (!frame)
    {
        SetLastError("Invalid frame parameter");
        return BAKETA_CAPTURE_ERROR_INVALID_WINDOW;
    }

    // フレーム構造体を初期化
    frame->bgraData = nullptr;
    frame->width = 0;
    frame->height = 0;
    frame->stride = 0;
    frame->timestamp = 0;

    WindowsCaptureSession* session = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_sessionMutex);
        auto it = g_sessions.find(sessionId);
        if (it == g_sessions.end())
        {
            SetLastError("Session not found");
            return BAKETA_CAPTURE_ERROR_NOT_FOUND;
        }
        session = it->second.get();
    }

    try
    {
        if (!session->CaptureFrame(&frame->bgraData, &frame->width, &frame->height, &frame->stride, &frame->timestamp, timeoutMs))
        {
            SetLastError("Failed to capture frame");
            return BAKETA_CAPTURE_ERROR_DEVICE;
        }

        SetLastError("");
        return BAKETA_CAPTURE_SUCCESS;
    }
    catch (const std::exception& e)
    {
        SetLastError(std::string("Frame capture failed: ") + e.what());
        return BAKETA_CAPTURE_ERROR_DEVICE;
    }
    catch (...)
    {
        SetLastError("Frame capture failed: Unknown error");
        return BAKETA_CAPTURE_ERROR_DEVICE;
    }
}

/// <summary>
/// フレームデータを解放
/// </summary>
void BaketaCapture_ReleaseFrame(BaketaCaptureFrame* frame)
{
    if (frame && frame->bgraData)
    {
        delete[] frame->bgraData;
        frame->bgraData = nullptr;
        frame->width = 0;
        frame->height = 0;
        frame->stride = 0;
        frame->timestamp = 0;
    }
}

/// <summary>
/// キャプチャセッションを削除
/// </summary>
void BaketaCapture_ReleaseSession(int sessionId)
{
    std::lock_guard<std::mutex> lock(g_sessionMutex);
    g_sessions.erase(sessionId);
}

/// <summary>
/// Windows Graphics Capture API がサポートされているかチェック
/// </summary>
int BaketaCapture_IsSupported()
{
    try
    {
        return winrt::GraphicsCaptureSession::IsSupported() ? 1 : 0;
    }
    catch (...)
    {
        return 0;
    }
}

/// <summary>
/// 最後のエラーメッセージを取得
/// </summary>
int BaketaCapture_GetLastError(char* buffer, int bufferSize)
{
    if (!buffer || bufferSize <= 0)
    {
        return static_cast<int>(g_lastError.length());
    }

    int copyLength = (std::min)(static_cast<int>(g_lastError.length()), bufferSize - 1);
    if (copyLength > 0)
    {
        memcpy(buffer, g_lastError.c_str(), copyLength);
    }
    buffer[copyLength] = '\0';

    return static_cast<int>(g_lastError.length());
}

/// <summary>
/// DLL エントリポイント
/// </summary>
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        break;
    case DLL_THREAD_ATTACH:
        break;
    case DLL_THREAD_DETACH:
        break;
    case DLL_PROCESS_DETACH:
        BaketaCapture_Shutdown();
        break;
    }
    return TRUE;
}