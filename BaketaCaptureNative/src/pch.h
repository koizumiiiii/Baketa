#pragma once

// Windows ヘッダー
#include <windows.h>
#include <unknwn.h>
#include <restrictederrorinfo.h>
#include <hstring.h>

// C++ 標準ライブラリ
#include <memory>
#include <vector>
#include <mutex>
#include <unordered_map>
#include <atomic>
#include <chrono>
#include <thread>  // [Issue #324] std::this_thread::sleep_for
#include <condition_variable>
#include <string>
#include <algorithm>
#include <cstdio>

// Windows Runtime
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <winrt/Windows.System.h>

// Windows Runtime ABI
#include <windows.graphics.capture.interop.h>
#include <Windows.Graphics.Capture.h>
#include <Windows.Graphics.DirectX.Direct3D11.interop.h>

// Direct3D
#include <d3d11.h>
#include <dxgi1_2.h>
#include <d3d11_4.h>
#include <d3dcompiler.h>  // 🚀 [Issue #193] GPU Shader Resize

// COM スマートポインタ
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;

// WinRT 名前空間エイリアス
namespace winrt
{
    using namespace Windows::Foundation;
    using namespace Windows::Graphics;
    using namespace Windows::Graphics::Capture;
    using namespace Windows::Graphics::DirectX;
    using namespace Windows::Graphics::DirectX::Direct3D11;
}

// プロジェクト内ヘッダー
#include "WindowsCaptureSession.h"