# Windows Graphics Capture API - 次のステップ検討

## 現状の問題

調査レポート「windows-graphics-capture-api-investigation.md」で詳述したように、.NET 8 + CsWinRT 2.2.0 環境では `MarshalDirectiveException` により Windows Graphics Capture API の利用が困難であることが判明した。

## 提案された解決アプローチ

### 1. C++/WinRT ネイティブDLL アプローチ ⭐ **推奨**

#### 概要
Windows Graphics Capture API の複雑な COM 相互運用を C++/WinRT で実装し、C# からは単純な P/Invoke で呼び出す。

#### 実装設計

##### C++ DLL 側 (`BaketaCaptureNative.dll`)
```cpp
// エクスポート関数
extern "C" {
    __declspec(dllexport) int CreateCaptureSession(HWND hwnd, int* sessionId);
    __declspec(dllexport) int CaptureFrame(int sessionId, uint8_t** bgraData, int* width, int* height, int* stride);
    __declspec(dllexport) void ReleaseCaptureSession(int sessionId);
    __declspec(dllexport) void ReleaseFrameData(uint8_t* bgraData);
}

// 内部実装
class WindowsCaptureSession {
    GraphicsCaptureItem m_captureItem;
    IDirect3DDevice m_device;
    Direct3D11CaptureFramePool m_framePool;
    // ...
};
```

##### C# 側統合
```csharp
public static class NativeWindowsCapture
{
    [DllImport("BaketaCaptureNative.dll")]
    private static extern int CreateCaptureSession(IntPtr hwnd, out int sessionId);
    
    [DllImport("BaketaCaptureNative.dll")]
    private static extern int CaptureFrame(int sessionId, out IntPtr bgraData, out int width, out int height, out int stride);
    
    [DllImport("BaketaCaptureNative.dll")]
    private static extern void ReleaseCaptureSession(int sessionId);
    
    [DllImport("BaketaCaptureNative.dll")]
    private static extern void ReleaseFrameData(IntPtr bgraData);
}
```

#### 利点
- ✅ **MarshalDirectiveException 完全回避**: COM 相互運用はネイティブ側で完結
- ✅ **高パフォーマンス**: C++/WinRT の最適化された実装
- ✅ **シンプルな統合**: C# 側は単純な P/Invoke のみ
- ✅ **デバッグ容易**: C++ と C# で分離されたデバッグ
- ✅ **将来への対応**: Windows Graphics Capture API の進化に柔軟対応

#### 実装工数
- **C++ DLL 開発**: 3-5日
- **C# 統合**: 1-2日
- **テスト・デバッグ**: 2-3日
- **合計**: 約1-2週間

#### リスク
- 🔸 **追加依存**: ネイティブ DLL の配布が必要
- 🔸 **プラットフォーム固有**: x64/ARM64 別々のビルドが必要
- 🔸 **C++ 開発スキル**: WinRT/Direct3D11 の知識が必要

### 2. .NET 9 プレビュー版での先行検証

#### 概要
.NET 9 Preview での COM 相互運用改善を先行検証する。

#### 検証手順
1. **.NET 9 Preview SDK インストール**
   ```bash
   # .NET 9 Preview のダウンロードとインストール
   ```

2. **Baketa プロジェクトの .NET 9 移行**
   ```xml
   <TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
   ```

3. **CsWinRT 最新版への更新**
   ```xml
   <PackageReference Include="Microsoft.Windows.CsWinRT" Version="2.1.0-preview" />
   ```

4. **MarshalDirectiveException の再現テスト**

#### 利点
- ✅ **先行情報取得**: 問題解決の可能性を早期確認
- ✅ **将来準備**: .NET 9 正式版への準備
- ✅ **コストメリット**: 既存コードの活用

#### リスク
- 🔸 **プレビュー版の不安定性**: 本番環境での使用不可
- 🔸 **問題未解決の可能性**: 期待した改善がない可能性
- 🔸 **追加工数**: 移行作業と検証作業

### 3. Direct3D/OpenGL フックによる低レベルキャプチャ

#### 概要
ゲームプロセスに DLL インジェクションし、描画 API を直接フックする。

#### 技術アプローチ
- **DLL インジェクション**: SetWindowsHookEx または CreateRemoteThread
- **API フック**: Detours ライブラリまたは手動 IAT パッチ
- **対象 API**: 
  - Direct3D: `Present`, `Present1`
  - OpenGL: `SwapBuffers`, `wglSwapBuffers`

#### 利点
- ✅ **最高品質**: オーバーレイ前の純粋なゲーム画面
- ✅ **隠れウィンドウ対応**: 最小化・隠蔽状態でもキャプチャ可能
- ✅ **フレーム同期**: ゲームの描画タイミングと完全同期

#### リスク
- 🔴 **実装複雑性**: 非常に高度な技術が必要
- 🔴 **アンチチート誤検知**: ゲームから不正プログラムと判定される危険
- 🔴 **安定性問題**: プロセスクラッシュやシステム不安定化
- 🔴 **メンテナンス負荷**: ゲームアップデートごとの対応が必要

## 推奨実装計画

### フェーズ 1: C++/WinRT ネイティブ DLL (最優先)

#### 1.1 プロトタイプ開発 (1週間)
- 基本的な C++/WinRT 実装
- 単一ウィンドウのキャプチャ機能
- C# からの呼び出しテスト

#### 1.2 本格実装 (1週間)
- セッション管理の実装
- エラーハンドリングの強化
- メモリ管理の最適化

#### 1.3 統合・テスト (3-5日)
- Baketa への統合
- パフォーマンステスト
- 各種ゲームでの動作確認

### フェーズ 2: .NET 9 検証 (並行実施)

#### 2.1 環境構築 (1-2日)
- .NET 9 Preview 環境のセットアップ
- 依存パッケージの互換性確認

#### 2.2 問題再現テスト (1-2日)
- MarshalDirectiveException の確認
- 既存実装での動作テスト

#### 2.3 結果評価 (1日)
- 改善状況の評価
- フェーズ3計画への反映

### フェーズ 3: 最終実装選択

#### シナリオ A: C++/WinRT DLL 成功
- ネイティブ DLL を正式採用
- 配布パッケージに含める
- ドキュメント・サポート体制整備

#### シナリオ B: .NET 9 で解決
- .NET 9 への移行計画策定
- 既存 C# 実装の活用
- ネイティブ DLL は開発環境用に保持

#### シナリオ C: 両方とも困難
- PrintWindow による安定動作を継続
- フック方式の詳細調査
- 将来の技術動向を継続監視

## 実装優先度マトリクス

| アプローチ | 実装難易度 | 成功確率 | 保守性 | パフォーマンス | 推奨度 |
|------------|------------|----------|--------|----------------|--------|
| C++/WinRT DLL | 中 | 高 | 良 | 最高 | ⭐⭐⭐⭐⭐ |
| .NET 9 検証 | 低 | 中 | 最良 | 高 | ⭐⭐⭐⭐ |
| Direct3D フック | 高 | 中 | 悪 | 最高 | ⭐⭐ |

## 技術要件

### C++/WinRT DLL 開発環境
- **Visual Studio 2022** with C++/WinRT workload
- **Windows 11 SDK** (10.0.22621.0 以降)
- **CMake** or MSBuild
- **vcpkg** for dependency management

### 必要スキルセット
- **C++/WinRT**: Windows Runtime API の C++ 実装
- **Direct3D11**: テクスチャとフレームバッファ操作
- **COM プログラミング**: Windows COM の理解
- **P/Invoke**: C# からネイティブ DLL 呼び出し

## まとめ

**C++/WinRT ネイティブ DLL アプローチ**が最も実用的で確実な解決策として推奨される。並行して **.NET 9 プレビュー検証**を実施し、将来的な選択肢を確保する。

Direct3D フック方式は技術的興味深いが、リスクが高すぎるため、他の手法で解決できない場合の最後の手段として位置づける。

---

**次のアクション**:
1. C++/WinRT DLL プロトタイプの開発開始
2. .NET 9 Preview 環境の並行セットアップ
3. 1週間後の進捗評価と方針確定