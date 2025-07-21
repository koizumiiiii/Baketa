cmake_minimum_required(VERSION 3.20)
project(BaketaCaptureNative)

# C++20 標準を使用
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Windows SDK の最小バージョン (Windows 10 1903)
set(CMAKE_SYSTEM_VERSION 10.0.19041.0)

# DLL として出力
add_library(BaketaCaptureNative SHARED
    src/BaketaCaptureNative.cpp
    src/WindowsCaptureSession.cpp
    src/pch.cpp
)

# プリコンパイル済みヘッダー
target_precompile_headers(BaketaCaptureNative PRIVATE src/pch.h)

# Windows Runtime サポート
set_target_properties(BaketaCaptureNative PROPERTIES
    VS_WINRT_COMPONENT TRUE
    CXX_STANDARD 20
    CXX_STANDARD_REQUIRED ON
)

# リンクライブラリ
target_link_libraries(BaketaCaptureNative PRIVATE
    windowsapp
    d3d11
    dxgi
    dwmapi
)

# インクルードディレクトリ
target_include_directories(BaketaCaptureNative PRIVATE
    src
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

# コンパイラオプション
target_compile_options(BaketaCaptureNative PRIVATE
    /await
    /bigobj
)

# リンカーオプション
target_link_options(BaketaCaptureNative PRIVATE
    /SUBSYSTEM:WINDOWS
)

# 出力ディレクトリ設定
set_target_properties(BaketaCaptureNative PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_CURRENT_SOURCE_DIR}/bin/Debug"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_CURRENT_SOURCE_DIR}/bin/Release"
    LIBRARY_OUTPUT_DIRECTORY_DEBUG "${CMAKE_CURRENT_SOURCE_DIR}/bin/Debug"
    LIBRARY_OUTPUT_DIRECTORY_RELEASE "${CMAKE_CURRENT_SOURCE_DIR}/bin/Release"
)