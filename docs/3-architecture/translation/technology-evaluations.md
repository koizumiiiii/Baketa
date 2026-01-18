# 翻訳技術評価ログ

このドキュメントは、Baketaの翻訳システムに関する技術評価の記録を保存する。
将来的に技術が改善された際の再検討材料として活用する。

---

## TranslateGemma（2026-01-18 評価）

**結論: 採用見送り**

### 概要

| 項目 | 内容 |
|------|------|
| 開発元 | Google DeepMind |
| リリース | 2026年1月 |
| ベース | Gemma 3 |
| モデルサイズ | 4B / 12B / 27B |
| 対応言語 | 55言語 |
| 特徴 | マルチモーダル対応（画像+テキスト→翻訳） |

### 検証方法

[translategemma.org](https://translategemma.org/) のオンラインデモで、ゲームスクリーンショット（ビジュアルノベル）を使用してテスト。

### 検証結果

#### マルチモーダル画像翻訳（OCR+翻訳統合）

| モデル | 日本語OCR精度 | UIテキスト検出 | 座標情報 |
|--------|--------------|----------------|----------|
| 4B | ❌ 「ナニカ」→「オフィス」 | 一部 | なし |
| 12B | ❌ 「ナニカ」→「チニカ」 | 全部 | なし |
| 27B | ✅ 正確 | メインのみ | なし |

#### 不採用理由

1. **座標情報なし**: 翻訳テキストのみ返却。オーバーレイ配置不可
2. **OCR精度がSurya OCRより低い**: 4B/12Bで日本語認識に問題
3. **27B必須**: 精度確保には27Bが必要だが、一般ユーザーのGPUでは動作不可

#### テキスト翻訳エンジンとしての評価

| 観点 | TranslateGemma 4B | NLLB-200 (CTranslate2) |
|------|-------------------|------------------------|
| VRAM | ~3GB（Q4量子化時） | ~500MB |
| 安定性 | 未検証 | 実績あり |
| 置き換えメリット | 不明確 | - |

**現行のNLLB-200で実用上問題なく、置き換えによる明確なメリットが見出せない。**

### 再検討条件

- 軽量モデル（4B以下）でOCR精度が改善された場合
- 座標情報を返すAPIが追加された場合
- テキスト翻訳品質がNLLB-200を大幅に上回ることが確認された場合

### 参考リンク

- [TranslateGemma公式ブログ](https://blog.google/innovation-and-ai/technology/developers-tools/translategemma/)
- [TranslateGemma Technical Report (arXiv)](https://arxiv.org/abs/2601.09012)
- [Hugging Face - google/translategemma-4b-it](https://huggingface.co/google/translategemma-4b-it)
- [npaka note解説](https://note.com/npaka/n/ne22141b07a0f)
- [GitHub Issue #304](https://github.com/koizumiiiii/Baketa/issues/304)

---

## 今後の評価候補

将来的に評価を検討する技術：

- [ ] MADLAD-400（Google、400言語対応）
- [ ] SeamlessM4T v2（Meta、音声+テキスト翻訳）
- [ ] その他の軽量翻訳モデル
