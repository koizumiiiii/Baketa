# Baketa プロジェクト - Gemini CLI 設定

このファイルは、GitHub ActionsでGemini CLIを使用する際のプロジェクト固有の指示を提供します。

## プロジェクト概要

**Baketa**は、Windows向けリアルタイム翻訳オーバーレイアプリケーションです。ゲーム画面から日本語テキストを検出し、リアルタイムで英語に翻訳して透明オーバーレイとして表示します。

### 技術スタック
- **言語**: C# 12 / .NET 8 Windows
- **アーキテクチャ**: クリーンアーキテクチャ（5層構造）
- **UI**: Avalonia 11.2.7 + ReactiveUI
- **OCR**: PaddleOCR PP-OCRv5
- **翻訳**: OPUS-MT（ローカル）、Google Gemini（クラウド）
- **キャプチャ**: Windows Graphics Capture API（C++/WinRT ネイティブDLL）
- **画像処理**: OpenCV

### 主要コンポーネント
1. **Baketa.Core**: プラットフォーム非依存のコア機能
2. **Baketa.Infrastructure**: OCR・翻訳エンジンの実装
3. **Baketa.Infrastructure.Platform**: Windows固有の実装
4. **Baketa.Application**: ビジネスロジック
5. **Baketa.UI**: ユーザーインターフェース
6. **BaketaCaptureNative**: C++/WinRT ネイティブDLL

## GitHub Actions レビュー指針

### プルリクエストレビュー時の重点項目

#### 1. アーキテクチャ準拠性
- **依存関係の方向**: 上位層から下位層への依存のみ
- **抽象化の適切な使用**: `Abstractions`名前空間のインターフェース使用
- **レイヤー間の適切な分離**: ビジネスロジックの適切な配置

#### 2. C# 12 / .NET 8 最新機能
- **必須使用機能**:
  - ファイルスコープ名前空間（`namespace Baketa.Core;`）
  - プライマリコンストラクター
  - コレクション式（`[]`構文）
  - パターンマッチング拡張
- **非同期プログラミング**: `ConfigureAwait(false)`の使用
- **Nullable Reference Types**: 適切な null 許容性注釈

#### 3. パフォーマンス要件
- **OCR処理**: リアルタイム性能（100ms以下目標）
- **メモリ使用量**: 画像処理時のメモリリーク防止
- **GPU利用**: PaddleOCR CUDA実行の最適化
- **スレッドセーフティ**: マルチスレッド環境での安全性

#### 4. セキュリティ考慮事項
- **APIキーの保護**: Gemini API キーの適切な管理
- **ファイルアクセス**: OCRモデルファイルの安全な読み込み
- **プロセス間通信**: ネイティブDLLとの安全な連携

### Issue トリアージ指針

#### コンポーネント別ラベル
- `component/ocr`: PaddleOCR、前処理、精度改善
- `component/translation`: OPUS-MT、Gemini、翻訳品質
- `component/ui`: Avalonia、ReactiveUI、オーバーレイ
- `component/capture`: Windows Graphics Capture API、スクリーン取得
- `component/native`: C++/WinRT DLL、P/Invoke連携
- `component/platform`: Windows固有実装、アダプター

#### 優先度分類
- `priority/critical`: アプリケーションクラッシュ、データ損失
- `priority/high`: 主要機能の動作不良、パフォーマンス劣化
- `priority/medium`: 機能改善、新機能追加
- `priority/low`: UI改善、ドキュメント更新

#### 技術的分類
- `type/bug`: 既存機能の不具合
- `type/feature`: 新機能追加
- `type/enhancement`: 既存機能の改善
- `type/performance`: パフォーマンス最適化
- `type/refactoring`: コード構造改善
- `type/documentation`: ドキュメント関連

### コードレビューチェックリスト

#### 必須チェック項目
- [ ] クリーンアーキテクチャの依存関係ルール準拠
- [ ] C# 12機能の適切な使用
- [ ] 非同期メソッドでの`ConfigureAwait(false)`
- [ ] `using`文によるリソース適切な管理
- [ ] 単体テストの追加・更新
- [ ] XMLドキュメントコメント（パブリックAPI）

#### パフォーマンスチェック
- [ ] 画像処理でのメモリ使用量
- [ ] OCR処理時間の最適化
- [ ] UIスレッドブロックの回避
- [ ] 非同期処理の適切な実装

#### セキュリティチェック
- [ ] 外部入力の検証
- [ ] ファイルパスの安全性
- [ ] APIキー・機密情報の適切な管理

## 特殊な考慮事項

### Windows Graphics Capture API
- **制約**: .NET 8のMarshalDirectiveException回避のためC++/WinRT実装
- **ビルド順序**: ネイティブDLL → .NETソリューション
- **配置**: DLLの適切な出力ディレクトリ配置

### OPUS-MT モデル
- **依存関係**: SentencePiece tokenizer、ONNXモデル
- **初期化**: アプリ起動時の事前読み込み
- **パフォーマンス**: CPU/GPU実行の最適化

### ReactiveUI パターン
- **ViewModelBase**: すべてのViewModelの基底クラス
- **Command**: ReactiveCommandの適切な使用
- **Validation**: ReactiveUI.Validationによる検証

## レビュー時の日本語応答例

### 良好なコード例
```
✅ **アーキテクチャ準拠**: クリーンアーキテクチャの依存関係ルールに適合しています
✅ **C# 12対応**: ファイルスコープ名前空間とプライマリコンストラクターを適切に使用
✅ **パフォーマンス**: ConfigureAwait(false)でデッドロックを回避
```

### 改善が必要な例
```
⚠️ **依存関係違反**: Infrastructure層からUI層への直接参照を検出
❌ **レガシー記法**: ファイルスコープ名前空間への移行が必要
🔍 **テスト不足**: 新機能に対する単体テストの追加を推奨
```

### 重要な指摘例
```
🚨 **セキュリティ**: APIキーがハードコードされています
🐛 **メモリリーク**: 画像リソースのDisposeが不適切
⚡ **パフォーマンス**: UIスレッドでの重い処理を検出
```