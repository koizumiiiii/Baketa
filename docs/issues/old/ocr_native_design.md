# OCRネイティブ実装 設計書

## 1. 概要

本ドキュメントは、BaketaアプリケーションのOCR処理におけるパフォーマンス向上のため、現在のPython gRPCサーバー経由の実装から、C#から直接C++ライブラリを呼び出すネイティブ実装への移行に関する設計を定義する。

## 2. 背景と課題

- **現状**: C#アプリケーションは、OCR処理のためにPythonで実装されたgRPCサーバーと通信している。
- **課題**:
    - **通信オーバーヘッド**: gRPCによるプロセス間通信には、データのシリアライズ・デシリアライズやネットワーク転送（ローカルホストであっても）に伴う無視できないオーバーヘッドが発生している。
    - **データ転送コスト**: 特に画像データはサイズが大きく、プロセス間でコピーする際のコストが高い。
    - **パフォーマンスボトルネック**: ログ分析の結果、OCR処理全体の時間のうち、純粋な推論時間以外に多くの時間が費やされており、これがリアルタイム性を損なう一因となっている。

## 3. 設計方針

Pythonプロセスを完全に排除し、C#からPaddleOCRのC++推論ライブラリを直接利用するアーキテクチャに変更する。

- **使用ライブラリ**: PaddleOCR C++ Inference Library
- **連携方式**: **P/Invoke (Platform Invocation Services)** を採用する。
- **コンポーネント**:
    1. **C++ラッパーDLL (`PaddleOcrWrapper.dll`)**: PaddleOCRのC++ APIをC言語形式の関数でラップし、P/Invokeから呼び出し可能にするためのネイティブDLL。
    2. **C#サービスクラス (`NativeOcrService.cs`)**: `DllImport`属性を用いてラッパーDLLの関数をインポートし、C#アプリケーションにOCR機能を提供する。

## 4. C++ラッパーDLL (`PaddleOcrWrapper.dll`) の仕様

C言語互換のインターフェースを公開し、C#からの呼び出しを容易にする。

### 4.1. 公開する関数

```cpp
// OCRエンジンの初期化
// model_dir_path: モデルファイルが格納されているディレクトリのパス
// use_gpu: GPUを使用するかどうか
// gpu_id: 使用するGPUのID
// returns: 成功時は0、失敗時は負数
extern "C" __declspec(dllexport) int InitializeOcr(const char* model_dir_path, bool use_gpu, int gpu_id);

// OCRの実行
// image_data: 画像のバイト配列
// width: 画像の幅
// height: 画像の高さ
// channels: 画像のチャンネル数 (例: 3 for BGR)
// returns: 検出されたテキストボックスの数
extern "C" __declspec(dllexport) int ExecuteOcr(unsigned char* image_data, int width, int height, int channels);

// OCR結果の取得
// index: 取得するテキストボックスのインデックス
// buffer: テキストを格納するバッファ
// buffer_size: バッファのサイズ
// out_coords: 座標情報 (x1, y1, x2, y2, ...) を格納する配列 (8要素)
// returns: テキストの文字数
extern "C" __declspec(dllexport) int GetOcrResultText(int index, char* buffer, int buffer_size);
extern "C" __declspec(dllexport) void GetOcrResultCoordinates(int index, int* out_coords);


// リソースの解放
extern "C" __declspec(dllexport) void ReleaseOcr();
```

### 4.2. 責務

- OCRエンジンのライフサイクル管理（初期化、リソース解放）。
- C#から渡された画像データ（`byte[]`）をPaddleOCRが要求する`cv::Mat`形式に変換。
- OCR推論の実行。
- 推論結果（テキスト、座標、信頼度）をC#側で解釈可能なプリミティブ型や構造体で保持。

## 5. C#側の実装 (`NativeOcrService.cs`)

### 5.1. P/Invokeによる関数インポート

```csharp
public class NativeOcrService
{
    private const string DllName = "PaddleOcrWrapper.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int InitializeOcr(string modelDirPath, bool useGpu, int gpuId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ExecuteOcr(IntPtr imageData, int width, int height, int channels);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int GetOcrResultText(int index, StringBuilder buffer, int bufferSize);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void GetOcrResultCoordinates(int index, [Out] int[] outCoords);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ReleaseOcr();

    // ... 以下、サービスクラスの実装
}
```

### 5.2. データフロー

1. **初期化**: アプリケーション起動時に`InitializeOcr`を一度だけ呼び出す。
2. **画像データ変換**: C#側の`Bitmap`オブジェクトを`byte[]`に変換する。
3. **メモリ転送**: `byte[]`を`GCHandle.Alloc`でピン留めし、そのポインタ（`IntPtr`）を`ExecuteOcr`関数に渡す。これにより、プロセス間での大規模なデータコピーを回避する。
4. **OCR実行**: `ExecuteOcr`を呼び出し、結果のテキストボックス数を取得する。
5. **結果取得**: ループ処理で`GetOcrResultText`と`GetOcrResultCoordinates`を呼び出し、各テキストボックスの情報を取得し、C#の`OcrResult`オブジェクトにマッピングする。
6. **リソース解放**: アプリケーション終了時に`ReleaseOcr`を呼び出す。

## 6. 期待される効果

- **レイテンシの大幅な削減**: gRPC通信とPythonプロセスのオーバーヘッドが完全になくなることで、**1000ms以上**の処理時間短縮が期待される。
- **メモリ効率の向上**: 画像データのプロセス間コピーが不要になるため、メモリ使用量が削減される。
- **アーキテクチャの簡素化**: Pythonランタイムや関連ライブラリへの依存がなくなり、デプロイと管理が容易になる。

## 7. ビルドとデプロイ

- `PaddleOcrWrapper.dll`は、PaddleOCRのC++ライブラリと静的/動的にリンクしてビルドする。
- ビルドされた`PaddleOcrWrapper.dll`および依存するPaddleOCRのDLL群は、Baketaの実行ファイルと同じディレクトリに配置する必要がある。
