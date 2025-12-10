#pragma once

class WindowsCaptureSession
{
public:
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="sessionId">セッションID</param>
    /// <param name="hwnd">対象ウィンドウハンドル</param>
    WindowsCaptureSession(int sessionId, HWND hwnd);
    
    /// <summary>
    /// デストラクタ
    /// </summary>
    ~WindowsCaptureSession();

    /// <summary>
    /// キャプチャセッションを初期化
    /// </summary>
    /// <returns>成功時は true</returns>
    bool Initialize();

    /// <summary>
    /// 最後の初期化エラーのHRESULTを取得
    /// </summary>
    /// <returns>HRESULT値（成功時は S_OK）</returns>
    HRESULT GetLastHResult() const { return m_lastHResult; }

    /// <summary>
    /// フレームをキャプチャ
    /// </summary>
    /// <param name="bgraData">BGRAピクセルデータ（出力）</param>
    /// <param name="width">幅（出力）</param>
    /// <param name="height">高さ（出力）</param>
    /// <param name="stride">行バイト数（出力）</param>
    /// <param name="timestamp">タイムスタンプ（出力）</param>
    /// <param name="timeoutMs">タイムアウト時間</param>
    /// <returns>成功時は true</returns>
    bool CaptureFrame(unsigned char** bgraData, int* width, int* height, int* stride, long long* timestamp, int timeoutMs);

    /// <summary>
    /// フレームをキャプチャしてGPU側でリサイズ (Issue #193 パフォーマンス最適化)
    /// </summary>
    /// <param name="bgraData">BGRAピクセルデータ（出力）</param>
    /// <param name="width">幅（出力）- リサイズ後のサイズ</param>
    /// <param name="height">高さ（出力）- リサイズ後のサイズ</param>
    /// <param name="stride">行バイト数（出力）</param>
    /// <param name="timestamp">タイムスタンプ（出力）</param>
    /// <param name="targetWidth">ターゲット幅</param>
    /// <param name="targetHeight">ターゲット高さ</param>
    /// <param name="timeoutMs">タイムアウト時間</param>
    /// <returns>成功時は true</returns>
    bool CaptureFrameResized(unsigned char** bgraData, int* width, int* height, int* stride, long long* timestamp, int targetWidth, int targetHeight, int timeoutMs);

    /// <summary>
    /// セッションIDを取得
    /// </summary>
    /// <returns>セッションID</returns>
    int GetSessionId() const { return m_sessionId; }

    /// <summary>
    /// ウィンドウハンドルを取得
    /// </summary>
    /// <returns>ウィンドウハンドル</returns>
    HWND GetWindowHandle() const { return m_hwnd; }

    /// <summary>
    /// 初期化済みかチェック
    /// </summary>
    /// <returns>初期化済みの場合は true</returns>
    bool IsInitialized() const { return m_initialized; }

    /// <summary>
    /// 最後のエラーメッセージを取得
    /// </summary>
    /// <returns>エラーメッセージ</returns>
    const std::string& GetLastError() const { return m_lastError; }

    /// <summary>
    /// ウィンドウ情報とスクリーン座標を取得（デバッグ用）
    /// </summary>
    /// <param name="windowInfo">ウィンドウ情報（出力）</param>
    /// <param name="screenRect">スクリーン座標（出力）</param>
    /// <returns>成功時は true</returns>
    bool GetWindowDebugInfo(std::string& windowInfo, std::string& screenRect) const;

private:
    /// <summary>
    /// Direct3D デバイスを作成
    /// </summary>
    /// <returns>成功時は true</returns>
    bool CreateD3DDevice();

    /// <summary>
    /// ウィンドウ状態をキャプチャ用に検証 (Phase 0 WGC修復)
    /// </summary>
    /// <returns>キャプチャ可能時は true</returns>
    bool ValidateWindowStateForCapture();

    /// <summary>
    /// GraphicsCaptureItem を作成
    /// </summary>
    /// <returns>成功時は true</returns>
    bool CreateCaptureItem();

    /// <summary>
    /// フレームプールを作成
    /// </summary>
    /// <returns>成功時は true</returns>
    bool CreateFramePool();

    /// <summary>
    /// テクスチャを BGRA データに変換
    /// </summary>
    /// <param name="texture">ソーステクスチャ</param>
    /// <param name="bgraData">BGRAデータ（出力）</param>
    /// <param name="stride">行バイト数（出力）</param>
    /// <returns>成功時は true</returns>
    bool ConvertTextureToBGRA(ID3D11Texture2D* texture, unsigned char** bgraData, int* stride);

    /// <summary>
    /// テクスチャをGPU上でリサイズしてBGRAデータに変換 (Issue #193)
    /// </summary>
    /// <param name="texture">ソーステクスチャ</param>
    /// <param name="bgraData">BGRAデータ（出力）</param>
    /// <param name="outputWidth">出力幅（出力）</param>
    /// <param name="outputHeight">出力高さ（出力）</param>
    /// <param name="stride">行バイト数（出力）</param>
    /// <param name="targetWidth">ターゲット幅</param>
    /// <param name="targetHeight">ターゲット高さ</param>
    /// <returns>成功時は true</returns>
    bool ResizeAndConvertTextureToBGRA(ID3D11Texture2D* texture, unsigned char** bgraData, int* outputWidth, int* outputHeight, int* stride, int targetWidth, int targetHeight);

    /// <summary>
    /// エラーメッセージを設定
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    void SetLastError(const std::string& message);

    /// <summary>
    /// フレーム到着イベントハンドラー
    /// </summary>
    /// <param name="sender">送信者</param>
    /// <param name="args">イベント引数</param>
    void OnFrameArrived(winrt::Direct3D11CaptureFramePool const& sender, winrt::IInspectable const& args);

private:
    int m_sessionId;
    HWND m_hwnd;
    bool m_initialized;

    // Direct3D オブジェクト
    ComPtr<ID3D11Device> m_d3dDevice;
    ComPtr<ID3D11DeviceContext> m_d3dContext;
    winrt::IDirect3DDevice m_winrtDevice;

    // WinRT キャプチャオブジェクト
    winrt::GraphicsCaptureItem m_captureItem{ nullptr };
    winrt::Direct3D11CaptureFramePool m_framePool{ nullptr };
    winrt::GraphicsCaptureSession m_captureSession{ nullptr };

    // 同期オブジェクト
    mutable std::mutex m_frameMutex;
    mutable std::condition_variable m_frameCondition;
    bool m_frameReady;
    
    // 最新フレーム
    ComPtr<ID3D11Texture2D> m_latestFrame;
    int m_frameWidth;
    int m_frameHeight;
    long long m_frameTimestamp;

    // エラー情報
    std::string m_lastError;
    HRESULT m_lastHResult;
};