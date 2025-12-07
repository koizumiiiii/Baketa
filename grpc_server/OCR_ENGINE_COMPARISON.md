# Issue #189: OCRエンジン比較テスト結果

## テスト日時
2025-12-07

## テスト環境
- OS: Windows 11
- GPU: NVIDIA GeForce RTX 4070
- Python: 3.10.9
- 画像解像度: 3839x2146 (4K)

## テスト画像
1. **chrono_trigger_screenshot.png** - レトロゲーム（暗い背景、ピクセルフォント）
2. **stellar_code_screenshot.png** - ビジュアルノベル（明るい背景、半透明ダイアログ）
3. **ihanashi_screenshot.png** - ビジュアルノベル（明るい背景、半透明ダイアログ）

---

## 結果サマリー

| エンジン | chrono_trigger | stellar_code | ihanashi | 推論速度 | VRAM |
|----------|----------------|--------------|----------|----------|------|
| **PP-OCRv5 ONNX** | 部分認識 | ❌ 未検出 | ❌ 未検出 | ~500ms | ~500MB |
| **Surya OCR v0.17.0** | ✅ フル認識 | ✅ フル認識 | ✅ フル認識 | ~2s | ~2GB |

---

## 詳細結果

### 1. chrono_trigger_screenshot.png

**期待テキスト:**
> フリッツ「クロノさん！！！あの時は本当にありがとうございました。」

#### PP-OCRv5 ONNX (既存)
- 結果: **部分認識**
- 検出: 「あの時は本当にありがとう」（ContrastEnhanced前処理時）
- 問題: 名前とセリフ全体は検出できず

#### Surya OCR v0.17.0
- 結果: **フル認識成功**
- 検出:
  ```
  [01] (0.93) フリッツ「クロノさん!!!
  [02] (1.00) あの時は本当にありがとう
  [03] (1.00) ございました。
  ```
- 処理時間: 2.48s

---

### 2. stellar_code_screenshot.png

**期待テキスト:**
> 桃子「なに寝ぼけたこと言ってるのさ。宇宙人だよ、宇宙人！」

#### PP-OCRv5 ONNX (既存)
- 結果: **❌ 日本語ダイアログ未検出**
- 検出されたもの: UIボタン（Save, Load, Auto等）のみ
- 問題: DBNetがダイアログテキストを検出できない

#### Surya OCR v0.17.0
- 結果: **✅ フル認識成功**
- 検出:
  ```
  [02] (1.00) 桃子
  [03] (1.00) 「なに寝ぼけたこと言ってるのさ。宇宙人だよ、宇宙人！
  [04] (0.99) 国連に連絡しなきゃ！」。
  ```
- 処理時間: 1.79s

---

### 3. ihanashi_screenshot.png

**期待テキスト:**
> しかし、当時の基準で言えば、固有名を持つ神女がいる渡夜時島の方が格上となる。

#### PP-OCRv5 ONNX (既存)
- 結果: **❌ 日本語ダイアログ未検出**
- 検出されたもの: UIボタン（MENU, SAVE, LOAD等）のみ
- 問題: DBNetがダイアログテキストを検出できない

#### Surya OCR v0.17.0
- 結果: **✅ フル認識成功**
- 検出:
  ```
  [04] (1.00) しかし、当時の基準で言えば、固有名を持つ神女がいる渡夜
  [05] (0.56) 時島の方が格上となる。・
  ```
- 処理時間: 2.02s

---

## 結論と推奨

### Surya OCR v0.17.0 を推奨

**理由:**
1. **検出精度**: PP-OCRv5で検出できなかったビジュアルノベルのダイアログテキストを100%検出
2. **日本語対応**: 90+言語対応、日本語の認識精度が非常に高い
3. **API安定性**: v0.17.0で安定したAPI（FoundationPredictor, RecognitionPredictor, DetectionPredictor）
4. **CUDA対応**: GPU加速で実用的な処理速度（2秒/画像）

**トレードオフ:**
- PP-OCRv5より処理速度が遅い（500ms → 2秒）
- VRAM使用量が多い（500MB → 2GB）
- モデルサイズが大きい（~1.5GB）

### PaddleOCR-VL について

PaddleOCR-VL-0.9B（109言語対応VLM）もテスト候補でしたが、以下の理由で保留:
- Windows環境での追加インストール手順が複雑
- safetensorsパッケージの特別バージョンが必要
- WSL/Dockerコンテナ推奨

Surya OCRが十分な結果を示しているため、Phase 1ではSurya OCRを採用し、PaddleOCR-VLは将来のオプションとして検討。

---

## 次のステップ

1. ✅ Surya OCR gRPCサーバー実装完了 (`ocr_server_surya.py`)
2. ⏳ C#クライアント実装（`GrpcOcrClient.cs`）
3. ⏳ Baketa.Infrastructureへの統合
4. ⏳ Live翻訳モードでのテスト

---

## 参考リンク

- [Surya OCR GitHub](https://github.com/VikParuchuri/surya)
- [PaddleOCR-VL HuggingFace](https://huggingface.co/PaddlePaddle/PaddleOCR-VL)
- [PaddleOCR-VL Usage Tutorial](https://www.paddleocr.ai/latest/en/version3.x/pipeline_usage/PaddleOCR-VL.html)
