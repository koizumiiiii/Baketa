#pragma once

#include <cstdint>

// [Issue #222] DXGI GPU検出 - WMIからの移行
// DXGIを使用したGPU情報取得（IL Trimming対応、高速、正確なVRAM）

#ifdef BAKETACAPTURENATIVE_EXPORTS
#define DXGI_GPU_API __declspec(dllexport)
#else
#define DXGI_GPU_API __declspec(dllimport)
#endif

extern "C" {
    // GPU情報構造体
    // [Gemini Review] bool → uint8_t に変更（マーシャリングの堅牢性向上）
    // パディング2バイトを明示的に追加（構造体サイズ296バイト）
    struct DxgiGpuInfo {
        wchar_t Description[128];       // GPU名
        uint32_t VendorId;              // ベンダーID (NVIDIA=0x10DE, AMD=0x1002, Intel=0x8086)
        uint32_t DeviceId;              // デバイスID
        uint64_t DedicatedVideoMemory;  // 専用VRAM (bytes)
        uint64_t DedicatedSystemMemory; // 専用システムメモリ (bytes)
        uint64_t SharedSystemMemory;    // 共有システムメモリ (bytes)
        uint32_t FeatureLevel;          // D3D Feature Level (0xc000=12.0, 0xc100=12.1, etc.)
        uint8_t IsIntegrated;           // 統合GPU判定 (0=false, 1=true)
        uint8_t IsValid;                // 情報が有効か (0=false, 1=true)
        uint8_t _padding[2];            // 明示的パディング（4バイト境界）
    };

    // プライマリGPU情報を取得
    // 専用GPUがあれば専用GPU、なければ統合GPUの情報を返す
    DXGI_GPU_API bool GetPrimaryGpuInfo(DxgiGpuInfo* outInfo);

    // 全GPU情報を取得
    // outInfos: GPU情報配列（呼び出し側で確保）
    // maxCount: 配列の最大要素数
    // 戻り値: 実際に取得したGPU数
    DXGI_GPU_API int GetAllGpuInfos(DxgiGpuInfo* outInfos, int maxCount);

    // DirectX Feature Levelを取得（D3D12優先、D3D11フォールバック）
    // 戻り値: Feature Level値（例: 0xc100 = D3D_FEATURE_LEVEL_12_1）
    DXGI_GPU_API uint32_t GetDirectXFeatureLevelDxgi();
}
