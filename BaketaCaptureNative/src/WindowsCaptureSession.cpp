#include "pch.h"

WindowsCaptureSession::WindowsCaptureSession(int sessionId, HWND hwnd)
    : m_sessionId(sessionId)
    , m_hwnd(hwnd)
    , m_initialized(false)
    , m_frameReady(false)
    , m_frameWidth(0)
    , m_frameHeight(0)
    , m_frameTimestamp(0)
    , m_lastHResult(S_OK)
{
}

WindowsCaptureSession::~WindowsCaptureSession()
{
    if (m_captureSession)
    {
        m_captureSession.Close();
    }
    
    if (m_framePool)
    {
        m_framePool.Close();
    }
}

bool WindowsCaptureSession::Initialize()
{
    try
    {
        SetLastError("DEBUG: Initialize() started");
        
        // Direct3D デバイスを作成
        if (!CreateD3DDevice())
        {
            SetLastError("DEBUG: CreateD3DDevice() failed - " + m_lastError);
            return false;
        }

        SetLastError("DEBUG: CreateD3DDevice() succeeded");

        // GraphicsCaptureItem を作成
        if (!CreateCaptureItem())
        {
            SetLastError("DEBUG: CreateCaptureItem() failed - " + m_lastError);
            return false;
        }

        SetLastError("DEBUG: CreateCaptureItem() succeeded");

        // フレームプールを作成
        if (!CreateFramePool())
        {
            SetLastError("DEBUG: CreateFramePool() failed - " + m_lastError);
            return false;
        }

        SetLastError("DEBUG: All initialization steps completed successfully");
        m_initialized = true;
        return true;
    }
    catch (const std::exception& e)
    {
        SetLastError(std::string("DEBUG: Initialize exception caught: ") + e.what());
        return false;
    }
    catch (...)
    {
        SetLastError("DEBUG: Initialize unknown exception caught");
        return false;
    }
}

bool WindowsCaptureSession::CreateD3DDevice()
{
    try
    {
        // Direct3D11 デバイスを作成
        D3D_FEATURE_LEVEL featureLevels[] = {
            D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL_11_0,
            D3D_FEATURE_LEVEL_10_1,
            D3D_FEATURE_LEVEL_10_0
        };

        UINT creationFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#ifdef _DEBUG
        // creationFlags |= D3D11_CREATE_DEVICE_DEBUG; // Graphics Tools未対応環境対策で一時的に無効化
#endif

        HRESULT hr = D3D11CreateDevice(
            nullptr,                    // アダプター
            D3D_DRIVER_TYPE_HARDWARE,   // ドライバータイプ
            nullptr,                    // ソフトウェアラスタライザー
            creationFlags,              // フラグ
            featureLevels,              // フィーチャーレベル
            ARRAYSIZE(featureLevels),   // フィーチャーレベル数
            D3D11_SDK_VERSION,          // SDK バージョン
            &m_d3dDevice,               // デバイス
            nullptr,                    // フィーチャーレベル（出力）
            &m_d3dContext               // デバイスコンテキスト
        );

        if (FAILED(hr))
        {
            // HRESULTを保存
            m_lastHResult = hr;
            
            // HRESULTの詳細な値を16進数でログ出力
            char errorBuffer[256];
            sprintf_s(errorBuffer, sizeof(errorBuffer), "D3D11CreateDevice failed with HRESULT: 0x%08X", hr);
            
            // 特定のエラーの詳細説明を追加
            if (hr == DXGI_ERROR_SDK_COMPONENT_MISSING) {
                strcat_s(errorBuffer, sizeof(errorBuffer), " (DXGI_ERROR_SDK_COMPONENT_MISSING - Graphics Tools required for Debug builds)");
            } else if (hr == E_ACCESSDENIED) {
                strcat_s(errorBuffer, sizeof(errorBuffer), " (E_ACCESSDENIED - Access denied)");
            } else if (hr == DXGI_ERROR_UNSUPPORTED) {
                strcat_s(errorBuffer, sizeof(errorBuffer), " (DXGI_ERROR_UNSUPPORTED - Feature not supported)");
            }
            
            SetLastError(std::string(errorBuffer));
            return false;
        }

        // DXGI デバイスを取得
        ComPtr<IDXGIDevice> dxgiDevice;
        hr = m_d3dDevice.As(&dxgiDevice);
        if (FAILED(hr))
        {
            m_lastHResult = hr;
            char errorBuffer[256];
            sprintf_s(errorBuffer, sizeof(errorBuffer), "Failed to get DXGI device with HRESULT: 0x%08X", hr);
            SetLastError(std::string(errorBuffer));
            return false;
        }

        // WinRT Direct3D デバイスを作成

        // WinRT Direct3D デバイスに変換
        hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.Get(), reinterpret_cast<IInspectable**>(winrt::put_abi(m_winrtDevice)));
        if (FAILED(hr))
        {
            m_lastHResult = hr;
            char errorBuffer[256];
            sprintf_s(errorBuffer, sizeof(errorBuffer), "Failed to create WinRT Direct3D device with HRESULT: 0x%08X", hr);
            SetLastError(std::string(errorBuffer));
            return false;
        }

        return true;
    }
    catch (...)
    {
        SetLastError("CreateD3DDevice exception");
        return false;
    }
}

bool WindowsCaptureSession::CreateCaptureItem()
{
    try
    {
        // C++/WinRT でのGraphicsCaptureItem作成（MarshalDirectiveException回避）
        auto interopFactory = winrt::get_activation_factory<winrt::GraphicsCaptureItem>();
        auto interop = interopFactory.as<::IGraphicsCaptureItemInterop>();
        
        if (!interop)
        {
            SetLastError("Failed to get IGraphicsCaptureItemInterop");
            return false;
        }

        // GraphicsCaptureItemを作成
        winrt::com_ptr<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem> captureItem;
        HRESULT hr = interop->CreateForWindow(
            m_hwnd,
            winrt::guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(),
            captureItem.put_void()
        );

        if (FAILED(hr))
        {
            SetLastError("CreateForWindow failed with HRESULT: 0x" + std::to_string(hr));
            return false;
        }

        // C++/WinRT オブジェクトに変換
        m_captureItem = captureItem.as<winrt::GraphicsCaptureItem>();
        
        if (!m_captureItem)
        {
            SetLastError("Failed to convert to GraphicsCaptureItem");
            return false;
        }

        return true;
    }
    catch (const winrt::hresult_error& ex)
    {
        m_lastHResult = ex.code();
        SetLastError("CreateCaptureItem winrt error: 0x" + std::to_string(ex.code()));
        return false;
    }
    catch (const std::exception& ex)
    {
        SetLastError(std::string("CreateCaptureItem exception: ") + ex.what());
        return false;
    }
    catch (...)
    {
        SetLastError("CreateCaptureItem unknown exception");
        return false;
    }
}

bool WindowsCaptureSession::CreateFramePool()
{
    try
    {
        if (!m_captureItem || !m_winrtDevice)
        {
            SetLastError("CaptureItem or WinRT device not initialized");
            return false;
        }

        // キャプチャアイテムのサイズを取得
        auto itemSize = m_captureItem.Size();
        
        // フレームプールを作成
        m_framePool = winrt::Direct3D11CaptureFramePool::CreateFreeThreaded(
            m_winrtDevice,
            winrt::DirectXPixelFormat::B8G8R8A8UIntNormalized,
            1, // フレーム数
            itemSize
        );

        if (!m_framePool)
        {
            SetLastError("Failed to create frame pool");
            return false;
        }

        // フレーム到着イベントを設定
        m_framePool.FrameArrived({ this, &WindowsCaptureSession::OnFrameArrived });

        // キャプチャセッションを作成
        m_captureSession = m_framePool.CreateCaptureSession(m_captureItem);
        
        if (!m_captureSession)
        {
            SetLastError("Failed to create capture session");
            return false;
        }

        return true;
    }
    catch (const winrt::hresult_error& ex)
    {
        m_lastHResult = ex.code();
        SetLastError("CreateFramePool winrt error: 0x" + std::to_string(ex.code()));
        return false;
    }
    catch (const std::exception& ex)
    {
        SetLastError(std::string("CreateFramePool exception: ") + ex.what());
        return false;
    }
    catch (...)
    {
        SetLastError("CreateFramePool unknown exception");
        return false;
    }
}

void WindowsCaptureSession::OnFrameArrived(winrt::Direct3D11CaptureFramePool const& sender, winrt::IInspectable const& args)
{
    try
    {
        auto frame = sender.TryGetNextFrame();
        if (!frame)
        {
            return;
        }

        // フレームからDirect3D11Surface を取得
        auto surface = frame.Surface();
        auto access = surface.as<Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
        
        ComPtr<ID3D11Texture2D> texture;
        HRESULT hr = access->GetInterface(IID_PPV_ARGS(&texture));
        
        if (SUCCEEDED(hr) && texture)
        {
            std::lock_guard<std::mutex> lock(m_frameMutex);
            
            // 最新フレームを保存
            m_latestFrame = texture;
            
            // フレーム情報を更新
            D3D11_TEXTURE2D_DESC desc;
            texture->GetDesc(&desc);
            m_frameWidth = static_cast<int>(desc.Width);
            m_frameHeight = static_cast<int>(desc.Height);
            m_frameTimestamp = std::chrono::duration_cast<std::chrono::nanoseconds>(
                std::chrono::steady_clock::now().time_since_epoch()).count() / 100; // 100ns単位
            
            m_frameReady = true;
            m_frameCondition.notify_one();
        }
    }
    catch (...)
    {
        // エラーは無視（フレーム取得は継続）
    }
}

bool WindowsCaptureSession::CaptureFrame(unsigned char** bgraData, int* width, int* height, int* stride, long long* timestamp, int timeoutMs)
{
    if (!m_initialized)
    {
        SetLastError("Session not initialized");
        return false;
    }

    if (!m_captureSession)
    {
        SetLastError("Capture session not created");
        return false;
    }

    try
    {
        // キャプチャを開始
        m_captureSession.StartCapture();

        // フレーム待機
        std::unique_lock<std::mutex> lock(m_frameMutex);
        bool frameReceived = m_frameCondition.wait_for(
            lock, 
            std::chrono::milliseconds(timeoutMs),
            [this] { return m_frameReady; }
        );

        if (!frameReceived)
        {
            SetLastError("Frame capture timeout");
            return false;
        }

        // フレーム情報を設定
        *width = m_frameWidth;
        *height = m_frameHeight;
        *timestamp = m_frameTimestamp;

        // テクスチャをBGRAデータに変換
        if (!ConvertTextureToBGRA(m_latestFrame.Get(), bgraData, stride))
        {
            SetLastError("Failed to convert texture to BGRA");
            return false;
        }

        // フレーム状態をリセット
        m_frameReady = false;
        
        return true;
    }
    catch (const winrt::hresult_error& ex)
    {
        SetLastError("CaptureFrame winrt error: 0x" + std::to_string(ex.code()));
        return false;
    }
    catch (const std::exception& ex)
    {
        SetLastError(std::string("CaptureFrame exception: ") + ex.what());
        return false;
    }
    catch (...)
    {
        SetLastError("CaptureFrame unknown exception");
        return false;
    }
}

bool WindowsCaptureSession::ConvertTextureToBGRA(ID3D11Texture2D* texture, unsigned char** bgraData, int* stride)
{
    try
    {
        if (!texture || !bgraData || !stride)
        {
            SetLastError("Invalid parameters for texture conversion");
            return false;
        }

        // テクスチャの詳細を取得
        D3D11_TEXTURE2D_DESC desc;
        texture->GetDesc(&desc);

        // ステージングテクスチャを作成（CPU読み取り可能）
        D3D11_TEXTURE2D_DESC stagingDesc = {};
        stagingDesc.Width = desc.Width;
        stagingDesc.Height = desc.Height;
        stagingDesc.MipLevels = 1;
        stagingDesc.ArraySize = 1;
        stagingDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        stagingDesc.Usage = D3D11_USAGE_STAGING;
        stagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        stagingDesc.SampleDesc.Count = 1;
        stagingDesc.SampleDesc.Quality = 0;

        ComPtr<ID3D11Texture2D> stagingTexture;
        HRESULT hr = m_d3dDevice->CreateTexture2D(&stagingDesc, nullptr, &stagingTexture);
        if (FAILED(hr))
        {
            SetLastError("Failed to create staging texture");
            return false;
        }

        // GPU テクスチャを CPU アクセス可能なステージングテクスチャにコピー
        m_d3dContext->CopyResource(stagingTexture.Get(), texture);

        // ステージングテクスチャをマップしてCPUから読み取り
        D3D11_MAPPED_SUBRESOURCE mappedResource;
        hr = m_d3dContext->Map(stagingTexture.Get(), 0, D3D11_MAP_READ, 0, &mappedResource);
        if (FAILED(hr))
        {
            SetLastError("Failed to map staging texture");
            return false;
        }

        // BGRAデータ用のメモリを確保
        *stride = static_cast<int>(mappedResource.RowPitch);
        size_t dataSize = desc.Height * (*stride);
        *bgraData = new unsigned char[dataSize];

        if (!(*bgraData))
        {
            m_d3dContext->Unmap(stagingTexture.Get(), 0);
            SetLastError("Failed to allocate BGRA data memory");
            return false;
        }

        // ピクセルデータをコピー
        const unsigned char* srcData = static_cast<const unsigned char*>(mappedResource.pData);
        unsigned char* dstData = *bgraData;

        for (UINT y = 0; y < desc.Height; ++y)
        {
            memcpy(
                dstData + y * (*stride),
                srcData + y * mappedResource.RowPitch,
                desc.Width * 4 // BGRA = 4 bytes per pixel
            );
        }

        // テクスチャのマップを解除
        m_d3dContext->Unmap(stagingTexture.Get(), 0);

        return true;
    }
    catch (const std::exception& ex)
    {
        SetLastError(std::string("ConvertTextureToBGRA exception: ") + ex.what());
        return false;
    }
    catch (...)
    {
        SetLastError("ConvertTextureToBGRA unknown exception");
        return false;
    }
}

void WindowsCaptureSession::SetLastError(const std::string& message)
{
    m_lastError = message;
}