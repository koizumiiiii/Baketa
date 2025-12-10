#pragma once

#ifdef __cplusplus
extern "C" {
#endif

// エラーコード定義
#define BAKETA_CAPTURE_SUCCESS 0
#define BAKETA_CAPTURE_ERROR_INVALID_WINDOW -1
#define BAKETA_CAPTURE_ERROR_UNSUPPORTED -2
#define BAKETA_CAPTURE_ERROR_ALREADY_EXISTS -3
#define BAKETA_CAPTURE_ERROR_NOT_FOUND -4
#define BAKETA_CAPTURE_ERROR_MEMORY -5
#define BAKETA_CAPTURE_ERROR_DEVICE -6

// フレームデータ構造体
typedef struct {
    unsigned char* bgraData;    // BGRA ピクセルデータ
    int width;                  // 幅
    int height;                 // 高さ
    int stride;                 // 行バイト数
    long long timestamp;        // キャプチャ時刻 (100ns 単位)
} BaketaCaptureFrame;

/// <summary>
/// ライブラリの初期化
/// </summary>
/// <returns>成功時は BAKETA_CAPTURE_SUCCESS</returns>
__declspec(dllexport) int BaketaCapture_Initialize();

/// <summary>
/// ライブラリの終了処理
/// </summary>
__declspec(dllexport) void BaketaCapture_Shutdown();

/// <summary>
/// ウィンドウキャプチャセッションを作成
/// </summary>
/// <param name="hwnd">対象ウィンドウハンドル</param>
/// <param name="sessionId">作成されたセッションID（出力）</param>
/// <returns>成功時は BAKETA_CAPTURE_SUCCESS</returns>
__declspec(dllexport) int BaketaCapture_CreateSession(void* hwnd, int* sessionId);

/// <summary>
/// フレームをキャプチャ
/// </summary>
/// <param name="sessionId">セッションID</param>
/// <param name="frame">キャプチャフレーム（出力）</param>
/// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
/// <returns>成功時は BAKETA_CAPTURE_SUCCESS</returns>
__declspec(dllexport) int BaketaCapture_CaptureFrame(int sessionId, BaketaCaptureFrame* frame, int timeoutMs);

/// <summary>
/// フレームをキャプチャしてGPU側でリサイズ (Issue #193 パフォーマンス最適化)
/// </summary>
/// <param name="sessionId">セッションID</param>
/// <param name="frame">キャプチャフレーム（出力）</param>
/// <param name="targetWidth">ターゲット幅（0の場合はリサイズなし）</param>
/// <param name="targetHeight">ターゲット高さ（0の場合はリサイズなし）</param>
/// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
/// <returns>成功時は BAKETA_CAPTURE_SUCCESS</returns>
__declspec(dllexport) int BaketaCapture_CaptureFrameResized(int sessionId, BaketaCaptureFrame* frame, int targetWidth, int targetHeight, int timeoutMs);

/// <summary>
/// フレームデータを解放
/// </summary>
/// <param name="frame">解放するフレーム</param>
__declspec(dllexport) void BaketaCapture_ReleaseFrame(BaketaCaptureFrame* frame);

/// <summary>
/// キャプチャセッションを削除
/// </summary>
/// <param name="sessionId">セッションID</param>
__declspec(dllexport) void BaketaCapture_ReleaseSession(int sessionId);

/// <summary>
/// Windows Graphics Capture API がサポートされているかチェック
/// </summary>
/// <returns>サポートされている場合は 1、それ以外は 0</returns>
__declspec(dllexport) int BaketaCapture_IsSupported();

/// <summary>
/// 最後のエラーメッセージを取得
/// </summary>
/// <param name="buffer">メッセージバッファ</param>
/// <param name="bufferSize">バッファサイズ</param>
/// <returns>実際のメッセージ長</returns>
__declspec(dllexport) int BaketaCapture_GetLastError(char* buffer, int bufferSize);

/// <summary>
/// セッションのウィンドウデバッグ情報を取得
/// </summary>
/// <param name="sessionId">セッションID</param>
/// <param name="windowInfoBuffer">ウィンドウ情報バッファ</param>
/// <param name="windowInfoSize">ウィンドウ情報バッファサイズ</param>
/// <param name="screenRectBuffer">スクリーン座標バッファ</param>
/// <param name="screenRectSize">スクリーン座標バッファサイズ</param>
/// <returns>成功時は 1、失敗時は 0</returns>
__declspec(dllexport) int BaketaCapture_GetWindowDebugInfo(int sessionId, char* windowInfoBuffer, int windowInfoSize, char* screenRectBuffer, int screenRectSize);

#ifdef __cplusplus
}
#endif