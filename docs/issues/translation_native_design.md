# 翻訳ネイティブ実装 設計書

## 1. 概要

本ドキュメントは、Baketaアプリケーションの翻訳処理におけるパフォーマンス向上のため、現在のPython gRPCサーバー経由の実装から、C#から直接C++ライブラリを呼び出すネイティブ実装への移行に関する設計を定義する。

## 2. 背景と課題

- **現状**: C#アプリケーションは、翻訳処理（NLLB-200モデル）のためにPythonで実装されたgRPCサーバーと通信している。このサーバーは内部でCTranslate2ライブラリを使用している。
- **課題**:
    - **通信オーバーヘッド**: OCRと同様に、gRPCによるプロセス間通信には無視できないオーバーヘッドが存在する。
    - **処理の冗長性**: テキストデータという比較的小さなデータを扱うにもかかわらず、プロセス間通信の枠組みを経由するのは冗長である。
    - **パフォーマンス**: ログ分析の結果、翻訳処理全体で数百ミリ秒のオーバーヘッドが確認されており、リアルタイム翻訳体験の足かせとなっている。

## 3. 設計方針

Pythonプロセスを完全に排除し、C#からCTranslate2のC++ライブラリを直接利用するアーキテクチャに変更する。

- **使用ライブラリ**: CTranslate2 C++ Library
- **連携方式**: **P/Invoke (Platform Invocation Services)** を採用する。
- **コンポーネント**:
    1. **C++ラッパーDLL (`CTranslate2Wrapper.dll`)**: CTranslate2のC++ APIをC言語形式の関数でラップし、P/Invokeから呼び出し可能にするためのネイティブDLL。
    2. **C#サービスクラス (`NativeTranslationService.cs`)**: `DllImport`属性を用いてラッパーDLLの関数をインポートし、C#アプリケーションに翻訳機能を提供する。

## 4. C++ラッパーDLL (`CTranslate2Wrapper.dll`) の仕様

C言語互換のインターフェースを公開し、C#からの呼び出しを容易にする。

### 4.1. 公開する関数

```cpp
// 翻訳エンジンの初期化
// model_path: CTranslate2モデルが格納されているディレクトリのパス
// device: "cpu" または "cuda"
// device_indices: 使用するデバイスのインデックス配列
// num_devices: device_indicesの要素数
// returns: 成功時は0、失敗時は負数
extern "C" __declspec(dllexport) int InitializeTranslator(const char* model_path, const char* device, int* device_indices, int num_devices);

// バッチ翻訳の実行
// input_tokens: トークン化された入力文字列の配列 (例: "<s> a b c </s>")
// num_inputs: 入力文字列の数
// target_prefix: ターゲット言語のプレフィックス (例: "jpn_Jpan")
// max_batch_size: 最大バッチサイズ
// returns: 翻訳結果の数
extern "C" __declspec(dllexport) int TranslateBatch(const char** input_tokens, int num_inputs, const char* target_prefix, int max_batch_size);

// 翻訳結果の取得
// index: 取得する結果のインデックス
// buffer: 翻訳結果テキストを格納するバッファ
// buffer_size: バッファのサイズ
// returns: テキストの文字数
extern "C" __declspec(dllexport) int GetTranslationResult(int index, char* buffer, int buffer_size);

// リソースの解放
extern "C" __declspec(dllexport) void ReleaseTranslator();
```

### 4.2. 責務

- 翻訳エンジン（`ctranslate2::Translator`）のライフサイクル管理。
- C#から渡された`string[]`を、CTranslate2が要求する`std::vector<std::vector<std::string>>`形式（トークン化された形式）に変換。
- 翻訳推論の実行。
- 推論結果をC#側で解釈可能な`char*`として保持。

## 5. C#側の実装 (`NativeTranslationService.cs`)

### 5.1. P/Invokeによる関数インポート

```csharp
public class NativeTranslationService
{
    private const string DllName = "CTranslate2Wrapper.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int InitializeTranslator(string modelPath, string device, int[] deviceIndices, int numDevices);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int TranslateBatch(string[] inputTokens, int numInputs, string targetPrefix, int maxBatchSize);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int GetTranslationResult(int index, StringBuilder buffer, int bufferSize);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ReleaseTranslator();

    // ... 以下、サービスクラスの実装
}
```

### 5.2. データフロー

1. **初期化**: アプリケーション起動時に`InitializeTranslator`を一度だけ呼び出す。
2. **前処理**: C#側で、翻訳対象の`string[]`に対してSentencePieceによるトークン化と、ソース言語・ターゲット言語のプレフィックストークンの付与を行う。
3. **翻訳実行**: 前処理済みの`string[]`を`TranslateBatch`関数に渡して翻訳を実行する。
4. **結果取得**: ループ処理で`GetTranslationResult`を呼び出し、各翻訳結果を取得してC#の`string`に変換する。
5. **後処理**: C#側で、翻訳結果のデトークン化を行う。
6. **リソース解放**: アプリケーション終了時に`ReleaseTranslator`を呼び出す。

## 6. 期待される効果

- **レイテンシの削減**: gRPC通信のオーバーヘッドがなくなることで、**数百ms**の処理時間短縮が期待される。
- **アーキテクチャの簡素化**: Pythonランタイムへの依存がなくなる。
- **リソース効率**: C#プロセス内で完結するため、リソース管理がより効率的になる。

## 7. ビルドとデプロイ

- `CTranslate2Wrapper.dll`は、CTranslate2のC++ライブラリと静的/動的にリンクしてビルドする。
- ビルドされた`CTranslate2Wrapper.dll`および依存するCTranslate2のDLL群（例: `oneDNN.dll`, `OpenBLAS.dll`等）は、Baketaの実行ファイルと同じディレクトリに配置する必要がある。
- SentencePieceのトークナイザモデルは、引き続きC#側からアクセス可能な場所に配置する。