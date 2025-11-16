# Windows Graphics Capture API実装

**最終更新**: 2025-11-17
**Status**: Phase 5.2完了、プロダクション運用中

## 概要

BaketaはWindows Graphics Capture APIをC++/WinRTネイティブDLL経由で使用し、DirectX/OpenGLゲームコンテンツのキャプチャを実現します。.NET 8の`MarshalDirectiveException`問題を回避するため、ネイティブDLLを介したP/Invoke実装を採用しています。

---

## アーキテクチャ概要

```
┌──────────────────────────────────────────────────────────────┐
│        Baketa.Infrastructure.Platform (C#)                   │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │  NativeWindowsCaptureWrapper (High-Level)              │  │
│  │  - IWindowCapturer実装                                 │  │
│  │  - エラーハンドリング                                  │  │
│  │  - リソース管理                                        │  │
│  └─────────────────────┬──────────────────────────────────┘  │
│                        │                                     │
│  ┌─────────────────────▼──────────────────────────────────┐  │
│  │  NativeWindowsCapture (P/Invoke)                       │  │
│  │  - DllImport declarations                              │  │
│  │  - IntPtr marshaling                                   │  │
│  └─────────────────────┬──────────────────────────────────┘  │
└────────────────────────┼──────────────────────────────────────┘
                         │ P/Invoke
                         ▼
┌──────────────────────────────────────────────────────────────┐
│     BaketaCaptureNative.dll (C++/WinRT)                      │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │  BaketaCaptureNative.cpp - DLL Entry Point             │  │
│  │  - CreateCaptureSession()                              │  │
│  │  - DestroyCaptureSession()                             │  │
│  │  - CaptureWindow()                                     │  │
│  └─────────────────────┬──────────────────────────────────┘  │
│                        │                                     │
│  ┌─────────────────────▼──────────────────────────────────┐  │
│  │  WindowsCaptureSession.cpp                             │  │
│  │  - Windows.Graphics.Capture.GraphicsCaptureSession     │  │
│  │  - Direct3D11CaptureFramePool                          │  │
│  │  - BGRA8→RGB24 pixel conversion                        │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

---

## ネイティブDLL実装

### BaketaCaptureNative.cpp

**場所**: `BaketaCaptureNative/src/BaketaCaptureNative.cpp`

**エクスポート関数**:

```cpp
extern "C" {
    __declspec(dllexport) void* CreateCaptureSession(HWND windowHandle);
    __declspec(dllexport) void DestroyCaptureSession(void* sessionHandle);
    __declspec(dllexport) int CaptureWindow(
        void* sessionHandle,
        unsigned char** outBuffer,
        int* outWidth,
        int* outHeight
    );
}
```

### WindowsCaptureSession.cpp

**場所**: `BaketaCaptureNative/src/WindowsCaptureSession.cpp`

**Windows Graphics Capture API使用**:

```cpp
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>

using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;

class WindowsCaptureSession {
private:
    GraphicsCaptureItem m_captureItem;
    Direct3D11CaptureFramePool m_framePool;
    GraphicsCaptureSession m_session;

public:
    WindowsCaptureSession(HWND windowHandle) {
        // GraphicsCaptureItem作成
        m_captureItem = CreateCaptureItemForWindow(windowHandle);

        // Direct3D11デバイス取得
        auto device = CreateDirect3DDevice();

        // フレームプール作成
        m_framePool = Direct3D11CaptureFramePool::Create(
            device,
            DirectXPixelFormat::B8G8R8A8UIntNormalized,
            2,  // バッファサイズ
            m_captureItem.Size()
        );

        // キャプチャセッション開始
        m_session = m_framePool.CreateCaptureSession(m_captureItem);
        m_session.StartCapture();
    }

    std::vector<unsigned char> CaptureFrame() {
        // フレーム取得
        auto frame = m_framePool.TryGetNextFrame();
        if (!frame) return {};

        // Direct3D11テクスチャ取得
        auto surface = frame.Surface();
        auto texture = GetDXGIInterfaceFromObject<ID3D11Texture2D>(surface);

        // CPU読み取り可能テクスチャにコピー
        D3D11_TEXTURE2D_DESC desc{};
        texture->GetDesc(&desc);
        desc.Usage = D3D11_USAGE_STAGING;
        desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        desc.BindFlags = 0;

        ComPtr<ID3D11Texture2D> stagingTexture;
        device->CreateTexture2D(&desc, nullptr, &stagingTexture);
        context->CopyResource(stagingTexture.Get(), texture.Get());

        // ピクセルデータ読み取り
        D3D11_MAPPED_SUBRESOURCE mapped{};
        context->Map(stagingTexture.Get(), 0, D3D11_MAP_READ, 0, &mapped);

        // BGRA8 → RGB24変換
        std::vector<unsigned char> rgb24Buffer;
        ConvertBGRA8ToRGB24(
            static_cast<unsigned char*>(mapped.pData),
            desc.Width,
            desc.Height,
            mapped.RowPitch,
            rgb24Buffer
        );

        context->Unmap(stagingTexture.Get(), 0);

        return rgb24Buffer;
    }

private:
    void ConvertBGRA8ToRGB24(
        const unsigned char* bgra,
        int width,
        int height,
        int rowPitch,
        std::vector<unsigned char>& rgb24
    ) {
        rgb24.resize(width * height * 3);

        for (int y = 0; y < height; ++y) {
            const unsigned char* srcRow = bgra + y * rowPitch;
            unsigned char* dstRow = rgb24.data() + y * width * 3;

            for (int x = 0; x < width; ++x) {
                int srcOffset = x * 4;  // BGRA
                int dstOffset = x * 3;  // RGB

                dstRow[dstOffset + 0] = srcRow[srcOffset + 2];  // R
                dstRow[dstOffset + 1] = srcRow[srcOffset + 1];  // G
                dstRow[dstOffset + 2] = srcRow[srcOffset + 0];  // B
                // Alpha破棄
            }
        }
    }
};
```

---

## C#側P/Invoke実装

### NativeWindowsCapture.cs

**場所**: `Baketa.Infrastructure.Platform/Windows/Capture/NativeWindowsCapture.cs`

```csharp
internal static class NativeWindowsCapture
{
    private const string DllName = "BaketaCaptureNative.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CreateCaptureSession(IntPtr windowHandle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DestroyCaptureSession(IntPtr sessionHandle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CaptureWindow(
        IntPtr sessionHandle,
        out IntPtr outBuffer,
        out int outWidth,
        out int outHeight
    );

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeBuffer(IntPtr buffer);
}
```

### NativeWindowsCaptureWrapper.cs

**場所**: `Baketa.Infrastructure.Platform/Windows/Capture/NativeWindowsCaptureWrapper.cs`

```csharp
public class NativeWindowsCaptureWrapper : IWindowCapturer
{
    private IntPtr _sessionHandle;

    public async Task<IImage> CaptureWindowAsync(IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        // セッション作成（初回のみ）
        if (_sessionHandle == IntPtr.Zero)
        {
            _sessionHandle = NativeWindowsCapture.CreateCaptureSession(windowHandle);
            if (_sessionHandle == IntPtr.Zero)
            {
                throw new CaptureException("Failed to create capture session");
            }
        }

        // キャプチャ実行
        int result = NativeWindowsCapture.CaptureWindow(
            _sessionHandle,
            out IntPtr buffer,
            out int width,
            out int height
        );

        if (result != 0)
        {
            throw new CaptureException($"Capture failed with code: {result}");
        }

        try
        {
            // RGB24バイト配列にコピー
            int bufferSize = width * height * 3;
            byte[] imageData = new byte[bufferSize];
            Marshal.Copy(buffer, imageData, 0, bufferSize);

            return new WindowsImage(imageData, width, height, ImageFormat.Rgb24);
        }
        finally
        {
            // ネイティブバッファ解放
            NativeWindowsCapture.FreeBuffer(buffer);
        }
    }

    public void Dispose()
    {
        if (_sessionHandle != IntPtr.Zero)
        {
            NativeWindowsCapture.DestroyCaptureSession(_sessionHandle);
            _sessionHandle = IntPtr.Zero;
        }
    }
}
```

---

## ビルド要件

### Visual Studio 2022

- **C++ Desktop Development**ワークロード必須
- **Windows SDK 10.0.19041.0**以上

### ビルドコマンド

```cmd
# 1. Visual Studio Developer Command Prompt起動
call "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"

# 2. ネイティブDLLビルド
msbuild BaketaCaptureNative\BaketaCaptureNative.sln /p:Configuration=Debug /p:Platform=x64

# 3. DLLを.NET出力ディレクトリにコピー
Copy-Item 'BaketaCaptureNative\bin\Debug\BaketaCaptureNative.dll' 'Baketa.UI\bin\x64\Debug\net8.0-windows10.0.19041.0\'
```

---

## 技術的メリット

### 1. MarshalDirectiveException回避

**問題**: .NET 8でWindows.Graphics.Capture APIを直接使用すると`MarshalDirectiveException`が発生

**解決**: C++/WinRTネイティブDLLを介することで回避

### 2. DirectX/OpenGL対応

- **DirectX**: フルスクリーンゲームのキャプチャ可能
- **OpenGL**: 一部ゲームエンジン対応

### 3. メモリ効率

- ネイティブ側でBGRA→RGB24変換
- 不要なアルファチャネル破棄（25%メモリ削減）

---

## フォールバック戦略

Windows Graphics Capture APIが利用できない場合、PrintWindow APIにフォールバック：

```csharp
public class AdaptiveCaptureService : ICaptureService
{
    private readonly NativeWindowsCaptureWrapper _nativeCapture;
    private readonly PrintWindowCapturer _printWindowCapture;

    public async Task<IImage> CaptureAsync(IntPtr windowHandle, CancellationToken cancellationToken)
    {
        try
        {
            return await _nativeCapture.CaptureWindowAsync(windowHandle, cancellationToken);
        }
        catch (CaptureException ex)
        {
            _logger.LogWarning(ex, "Native capture failed, falling back to PrintWindow");
            return await _printWindowCapture.CaptureWindowAsync(windowHandle, cancellationToken);
        }
    }
}
```

---

## 関連ドキュメント

- `E:\dev\Baketa\CLAUDE.md` - Windows Graphics Capture概要
- `E:\dev\Baketa\BaketaCaptureNative\README.md` - ネイティブDLLビルド方法
- `E:\dev\Baketa\docs\3-architecture\clean-architecture.md` - Clean Architecture設計

---

**Last Updated**: 2025-11-17
**Status**: Phase 5.2完了、プロダクション運用中
