// [Issue #222] DXGI GPU検出実装
// WMI（System.Management）からの移行 - IL Trimming対応、高速、正確なVRAM

#include "pch.h"
#include "DxgiGpuDetector.h"
#include <dxgi1_6.h>
#include <d3d12.h>
#include <d3d11.h>
#include <wrl/client.h>
#include <algorithm>
#include <vector>

#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "d3d11.lib")

using Microsoft::WRL::ComPtr;

namespace {
    // 統合GPUかどうかを判定
    bool IsIntegratedGpu(const DXGI_ADAPTER_DESC1& desc) {
        // DXGI_ADAPTER_FLAG_SOFTWARE: ソフトウェアアダプタ（統合扱い）
        if (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) {
            return true;
        }

        // Intel統合GPU判定（VendorId + 専用VRAM少量）
        if (desc.VendorId == 0x8086) { // Intel
            // Intel統合GPUは通常専用VRAMが128MB以下
            if (desc.DedicatedVideoMemory < 256 * 1024 * 1024) {
                return true;
            }
        }

        // AMD APU判定
        if (desc.VendorId == 0x1002) { // AMD
            // AMD APUは通常専用VRAMが512MB以下
            if (desc.DedicatedVideoMemory < 512 * 1024 * 1024) {
                return true;
            }
        }

        return false;
    }

    // D3D12 Feature Levelを取得
    uint32_t GetD3D12FeatureLevel(IDXGIAdapter1* adapter) {
        static const D3D_FEATURE_LEVEL featureLevels[] = {
            D3D_FEATURE_LEVEL_12_2,
            D3D_FEATURE_LEVEL_12_1,
            D3D_FEATURE_LEVEL_12_0,
            D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL_11_0
        };

        for (auto level : featureLevels) {
            HRESULT hr = D3D12CreateDevice(
                adapter,
                level,
                __uuidof(ID3D12Device),
                nullptr // デバイスを作成せずにサポート確認のみ
            );

            if (SUCCEEDED(hr)) {
                return static_cast<uint32_t>(level);
            }
        }

        return 0; // D3D12非対応
    }

    // D3D11 Feature Levelを取得（D3D12非対応時のフォールバック）
    uint32_t GetD3D11FeatureLevel(IDXGIAdapter1* adapter) {
        D3D_FEATURE_LEVEL featureLevel = D3D_FEATURE_LEVEL_9_1;
        ComPtr<ID3D11Device> device;
        ComPtr<ID3D11DeviceContext> context;

        HRESULT hr = D3D11CreateDevice(
            adapter,
            D3D_DRIVER_TYPE_UNKNOWN, // アダプタ指定時はUNKNOWN
            nullptr,
            0,
            nullptr,
            0,
            D3D11_SDK_VERSION,
            &device,
            &featureLevel,
            &context
        );

        if (SUCCEEDED(hr)) {
            return static_cast<uint32_t>(featureLevel);
        }

        return 0;
    }

    // GPU情報を収集
    bool PopulateGpuInfo(IDXGIAdapter1* adapter, DxgiGpuInfo* outInfo) {
        if (!adapter || !outInfo) return false;

        DXGI_ADAPTER_DESC1 desc = {};
        if (FAILED(adapter->GetDesc1(&desc))) {
            return false;
        }

        // ソフトウェアアダプタ（Microsoft Basic Render Driver）はスキップ
        if (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) {
            return false;
        }

        // GPU情報をコピー
        wcsncpy_s(outInfo->Description, desc.Description, _TRUNCATE);
        outInfo->VendorId = desc.VendorId;
        outInfo->DeviceId = desc.DeviceId;
        outInfo->DedicatedVideoMemory = desc.DedicatedVideoMemory;
        outInfo->DedicatedSystemMemory = desc.DedicatedSystemMemory;
        outInfo->SharedSystemMemory = desc.SharedSystemMemory;
        // [Gemini Review] bool → uint8_t (0=false, 1=true)
        outInfo->IsIntegrated = IsIntegratedGpu(desc) ? 1 : 0;

        // Feature Level取得（D3D12優先）
        outInfo->FeatureLevel = GetD3D12FeatureLevel(adapter);
        if (outInfo->FeatureLevel == 0) {
            outInfo->FeatureLevel = GetD3D11FeatureLevel(adapter);
        }

        // [Gemini Review] bool → uint8_t (0=false, 1=true)
        outInfo->IsValid = 1;
        outInfo->_padding[0] = 0;
        outInfo->_padding[1] = 0;
        return true;
    }
}

extern "C" {

DXGI_GPU_API bool GetPrimaryGpuInfo(DxgiGpuInfo* outInfo) {
    if (!outInfo) return false;

    // 初期化
    memset(outInfo, 0, sizeof(DxgiGpuInfo));

    // DXGI Factory作成（1.6優先）
    ComPtr<IDXGIFactory6> factory6;
    ComPtr<IDXGIFactory1> factory1;

    HRESULT hr = CreateDXGIFactory1(__uuidof(IDXGIFactory6), &factory6);
    if (FAILED(hr)) {
        // Factory6が使えない場合はFactory1にフォールバック
        hr = CreateDXGIFactory1(__uuidof(IDXGIFactory1), &factory1);
        if (FAILED(hr)) {
            return false;
        }
    }

    ComPtr<IDXGIAdapter1> bestAdapter;
    DxgiGpuInfo bestInfo = {};

    if (factory6) {
        // IDXGIFactory6::EnumAdapterByGpuPreferenceで高性能GPU優先
        ComPtr<IDXGIAdapter1> adapter;
        for (UINT i = 0;
             SUCCEEDED(factory6->EnumAdapterByGpuPreference(
                 i,
                 DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE,
                 __uuidof(IDXGIAdapter1),
                 &adapter));
             ++i, adapter.Reset())
        {
            DxgiGpuInfo info = {};
            if (PopulateGpuInfo(adapter.Get(), &info)) {
                // 専用GPUを優先、またはVRAMが大きい方を選択
                if (!bestInfo.IsValid ||
                    (!info.IsIntegrated && bestInfo.IsIntegrated) ||
                    (info.IsIntegrated == bestInfo.IsIntegrated &&
                     info.DedicatedVideoMemory > bestInfo.DedicatedVideoMemory))
                {
                    bestInfo = info;
                    bestAdapter = adapter;
                }
            }
        }
    }
    else if (factory1) {
        // 従来のEnumAdaptersで列挙
        ComPtr<IDXGIAdapter1> adapter;
        for (UINT i = 0;
             factory1->EnumAdapters1(i, &adapter) != DXGI_ERROR_NOT_FOUND;
             ++i, adapter.Reset())
        {
            DxgiGpuInfo info = {};
            if (PopulateGpuInfo(adapter.Get(), &info)) {
                if (!bestInfo.IsValid ||
                    (!info.IsIntegrated && bestInfo.IsIntegrated) ||
                    (info.IsIntegrated == bestInfo.IsIntegrated &&
                     info.DedicatedVideoMemory > bestInfo.DedicatedVideoMemory))
                {
                    bestInfo = info;
                    bestAdapter = adapter;
                }
            }
        }
    }

    if (bestInfo.IsValid) {
        *outInfo = bestInfo;
        return true;
    }

    return false;
}

DXGI_GPU_API int GetAllGpuInfos(DxgiGpuInfo* outInfos, int maxCount) {
    if (!outInfos || maxCount <= 0) return 0;

    // 初期化
    memset(outInfos, 0, sizeof(DxgiGpuInfo) * maxCount);

    ComPtr<IDXGIFactory1> factory;
    if (FAILED(CreateDXGIFactory1(__uuidof(IDXGIFactory1), &factory))) {
        return 0;
    }

    int count = 0;
    ComPtr<IDXGIAdapter1> adapter;
    for (UINT i = 0;
         count < maxCount &&
         factory->EnumAdapters1(i, &adapter) != DXGI_ERROR_NOT_FOUND;
         ++i, adapter.Reset())
    {
        if (PopulateGpuInfo(adapter.Get(), &outInfos[count])) {
            count++;
        }
    }

    return count;
}

DXGI_GPU_API uint32_t GetDirectXFeatureLevelDxgi() {
    ComPtr<IDXGIFactory1> factory;
    if (FAILED(CreateDXGIFactory1(__uuidof(IDXGIFactory1), &factory))) {
        return 0;
    }

    ComPtr<IDXGIAdapter1> adapter;
    if (FAILED(factory->EnumAdapters1(0, &adapter))) {
        return 0;
    }

    // D3D12優先
    uint32_t level = GetD3D12FeatureLevel(adapter.Get());
    if (level > 0) {
        return level;
    }

    // D3D11フォールバック
    return GetD3D11FeatureLevel(adapter.Get());
}

} // extern "C"
