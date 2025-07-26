# 座標ベース翻訳システム実行問題の分析

## 問題の概要
「Startボタンを押しても座標ベース翻訳が実行されず、翻訳結果（実際にはOCR結果）が画面に表示されない」

## ユーザーからの重要な情報
- **以前の動作**: OCRで読み取ったテキスト（日本語）をそのまま画面に表示していた
- **翻訳機能**: 実際には翻訳されておらず、OCR結果をそのまま表示（認識精度確認には便利だった）
- **現在の問題**: Phase 2-C対応後、OCR結果の表示すらされなくなった

## 現在の状況

### 1. 確認できている動作
- ✅ 画面キャプチャは正常に動作（ログに `CaptureWindowAsync` の実行記録あり）
- ✅ DIコンテナで `CoordinateBasedTranslationService` が正しく注入されるよう修正済み
- ❌ `TranslationOrchestrationService` のログが一切出力されていない
- ❌ `TranslationFlowEventProcessor` のログも出力されていない

### 2. ログから分かること

#### 出力されているログ
```
🔥🔥🔥 [ADAPTER] CaptureWindowAsync呼び出されました！HWND=0x20718
🚀 戦略実行: DirectFullScreen, HWND=0x20718
📸 キャプチャ結果: 成功=True, 戦略=DirectFullScreen, 画像数=1, サイズ=1511x985, エラー=
```

#### 出力されていないログ（期待されるログ）
- `TranslationOrchestrationService` 関連のログ
- `TranslationFlowEventProcessor.HandleAsync` の実行ログ
- `CoordinateBasedTranslationService` の処理ログ
- OCRエンジンの処理ログ

## 問題の詳細分析

### イベントフローの断絶
1. **HomeViewModel** → `StartCaptureRequestedEvent` を発行
2. **期待される処理** → 何かがこのイベントを処理して翻訳を開始
3. **実際** → `StartCaptureRequestedEvent` を処理するイベントプロセッサーが存在しない

### 実際に存在する翻訳フロー
1. **TranslationFlowEventProcessor** は `StartTranslationRequestEvent` を処理
2. **MainOverlayViewModel** は `StartTranslationRequestEvent` を発行
3. **OperationalControlViewModel** は `TranslationOrchestrationService` を直接呼び出し

### 発見された問題
- `StartCaptureRequestedEvent` と `StartTranslationRequestEvent` は別のイベント
- `StartCaptureRequestedEvent` → `StartTranslationRequestEvent` への変換が実装されていない
- つまり、Homeビューのスタートボタンは翻訳フローに接続されていない

## 根本原因
Phase 2-C以前は別の方法でOCR結果が表示されていたが、その実装パスが不明。現在のイベントベースのアーキテクチャでは、HomeViewModelのStartボタンが翻訳システムに接続されていない。

## 解決方法の選択肢

### 選択肢1: イベント変換プロセッサーを追加
`StartCaptureRequestedEvent` を受け取って `StartTranslationRequestEvent` を発行するプロセッサーを作成

### 選択肢2: HomeViewModelを修正
`StartCaptureRequestedEvent` の代わりに `StartTranslationRequestEvent` を発行するよう変更

### 選択肢3: 以前の実装パスを復活
Phase 2-C以前のOCR結果表示方法を調査して復活させる