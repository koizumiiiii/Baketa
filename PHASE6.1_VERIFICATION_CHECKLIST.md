# Phase 6.1 動作検証チェックリスト

## ✅ 実装完了項目

1. ✅ `StopTranslationRequestEvent` をCore層に移動
2. ✅ `StopTranslationRequestEventHandler` 作成・登録
3. ✅ Clean Architecture違反修正完了
4. ✅ ビルド成功（0エラー）

## 🔍 動作検証手順

### Step 1: アプリケーション再ビルド & 起動

```cmd
cd E:\dev\Baketa
dotnet build --configuration Debug
dotnet run --project Baketa.UI
```

### Step 2: ログファイル監視準備

```cmd
# PowerShellで別ターミナルを開いて監視
Get-Content "E:\dev\Baketa\Baketa.UI\bin\Debug\net8.0-windows10.0.19041.0\debug_app_logs.txt" -Tail 10 -Wait
```

### Step 3: 翻訳開始

1. ゲームウィンドウを選択
2. Startボタンをクリック
3. 翻訳が開始されることを確認

**期待されるログ**:
```
🟢 StartTranslationAsync呼び出し
```

### Step 4: 翻訳停止（重要！）

1. **Stopボタンをクリック**
2. ログを確認

**期待されるログ**:
```
🔴 StopTranslationAsync呼び出し
🚀 [RACE_CONDITION_FIX] StopTranslationRequestEvent最優先発行開始
✅ [RACE_CONDITION_FIX] StopTranslationRequestEvent最優先発行成功
🛑 [STOP_HANDLER] Stop translation request received - EventId: <GUID>
✅ [STOP_HANDLER] Translation stopped successfully - EventId: <GUID>
```

### Step 5: 処理停止確認

翻訳が実際に停止されることを確認:
- ✅ 画像キャプチャが停止
- ✅ OCR処理が停止
- ✅ 翻訳処理が停止
- ✅ オーバーレイ表示が消える

## ⚠️ 重要な注意点

### 現在のログ分析結果

**ログファイル**: `debug_app_logs.txt` (2025-11-07 14:13:28 ～ 14:14:11)

**判明した事実**:
1. ✅ `StopTranslationRequestHandler`は正常に登録されている
   ```
   [14:13:28.058][T01] [INFO] 🛑 StopTranslationRequestHandlerを登録しました
   ```

2. ❌ **Stopボタン押下の記録が全く存在しない**
   - `🔴 StopTranslationAsync呼び出し` ログなし
   - `RACE_CONDITION_FIX` ログなし
   - `STOP_HANDLER` ログなし

**結論**: この実行では**Stopボタンが一度も押されていない**

## 📋 トラブルシューティング

### 問題: Stopボタンを押しても処理が止まらない

**確認項目**:

1. **最新ビルドで実行しているか?**
   ```cmd
   # ビルド日時確認
   dir "E:\dev\Baketa\Baketa.UI\bin\Debug\net8.0-windows10.0.19041.0\Baketa.UI.dll"
   ```

2. **ログに`STOP_HANDLER`が出力されているか?**
   ```cmd
   grep "STOP_HANDLER" debug_app_logs.txt
   ```

3. **TranslationOrchestrationService.StopAsync()が呼ばれているか?**
   - `STOP_HANDLER`ログの後に翻訳処理が停止するはず

### 問題: Stopボタン押下時にエラーが発生

**確認項目**:

1. **例外ログを確認**
   ```cmd
   grep "❌\|Exception\|Error" debug_app_logs.txt | tail -20
   ```

2. **EventAggregatorの動作確認**
   ```cmd
   grep "EventAggregator\|Subscribe\|Publish" debug_app_logs.txt | grep "StopTranslation"
   ```

## ✅ 成功判定基準

以下のログが全て出力されれば**Phase 6.1実装成功**:

1. ✅ ハンドラー登録ログ
2. ✅ Stopボタン押下ログ
3. ✅ StopTranslationRequestEvent発行ログ
4. ✅ STOP_HANDLERイベント受信ログ
5. ✅ TranslationOrchestrationService停止成功ログ
6. ✅ 実際に翻訳処理が停止

## 🔜 次のステップ

Phase 6.1動作検証が成功したら、**Phase 6.2: オーバーレイ座標ズレ調査**に進む。
