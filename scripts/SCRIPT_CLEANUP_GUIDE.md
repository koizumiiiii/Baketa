# Baketa SentencePiece スクリプトファイル整理

## 📋 **保持すべきスクリプト（動作確認済み）**

### ✅ **verify_opus_mt_models_final.ps1**
- **用途**: OPUS-MTモデルファイルの検証
- **状態**: 動作確認済み（5/5モデル成功）
- **保持理由**: 将来のモデル追加時に使用

### ✅ **download_opus_mt_models_correct.ps1** 
- **用途**: 正しいURLでのOPUS-MTモデルダウンロード
- **状態**: 動作確認済み（4/4モデル成功）
- **保持理由**: 新環境でのモデル取得に使用

### ✅ **run_sentencepiece_tests_fixed.ps1**
- **用途**: SentencePiece統合テストの実行
- **状態**: 動作確認済み（178/178テスト成功）
- **保持理由**: 継続的なテスト実行に使用

---

## 🗂️ **アーカイブ対象スクリプト（開発過程の試行錯誤）**

### 📁 **analyze_existing_models.ps1**
- **用途**: 既存モデルファイルの分析（一度のみ使用）
- **状態**: 目的達成済み
- **処理**: アーカイブ

### 📁 **create_dummy_sentencepiece_models.ps1**
- **用途**: テスト用ダミーモデル作成（一度のみ使用）
- **状態**: 実際のモデル取得により不要
- **処理**: アーカイブ

### 📁 **download_opus_mt_models.ps1**
- **用途**: 初期版ダウンロードスクリプト
- **状態**: 動作不良（URLエラー）
- **処理**: アーカイブ

### 📁 **download_opus_mt_models_fixed.ps1**
- **用途**: 中間版ダウンロードスクリプト
- **状態**: 不完全
- **処理**: アーカイブ

### 📁 **run_sentencepiece_tests.ps1**
- **用途**: 初期版テストスクリプト
- **状態**: 動作不良（MSBuildエラー）
- **処理**: アーカイブ

### 📁 **verify_opus_mt_models.ps1**
- **用途**: 初期版検証スクリプト
- **状態**: 動作不良（検証ロジック問題）
- **処理**: アーカイブ

### 📁 **verify_opus_mt_models_fixed.ps1**
- **用途**: 中間版検証スクリプト
- **状態**: バグあり（ファイルパス取得エラー）
- **処理**: アーカイブ

---

## 🚀 **整理アクション**

### **PowerShellコマンド**
```powershell
# アーカイブディレクトリへ移動
Move-Item "scripts/analyze_existing_models.ps1" "scripts/archive/"
Move-Item "scripts/create_dummy_sentencepiece_models.ps1" "scripts/archive/"
Move-Item "scripts/download_opus_mt_models.ps1" "scripts/archive/"
Move-Item "scripts/download_opus_mt_models_fixed.ps1" "scripts/archive/"
Move-Item "scripts/run_sentencepiece_tests.ps1" "scripts/archive/"
Move-Item "scripts/verify_opus_mt_models.ps1" "scripts/archive/"
Move-Item "scripts/verify_opus_mt_models_fixed.ps1" "scripts/archive/"
```

### **Git管理**
```bash
# アーカイブディレクトリをGit管理下に追加
git add scripts/archive/

# メインスクリプトのみをGit管理下に追加
git add scripts/verify_opus_mt_models_final.ps1
git add scripts/download_opus_mt_models_correct.ps1
git add scripts/run_sentencepiece_tests_fixed.ps1
```

---

## 📁 **整理後のscripts/ディレクトリ構造**

```
scripts/
├── verify_opus_mt_models_final.ps1      # ✅ 使用中
├── download_opus_mt_models_correct.ps1  # ✅ 使用中
├── run_sentencepiece_tests_fixed.ps1    # ✅ 使用中
├── create_test_sentencepiece_model.py   # ✅ 保持（テスト用）
└── archive/                             # 📁 アーカイブ
    ├── analyze_existing_models.ps1
    ├── create_dummy_sentencepiece_models.ps1
    ├── download_opus_mt_models.ps1
    ├── download_opus_mt_models_fixed.ps1
    ├── run_sentencepiece_tests.ps1
    ├── verify_opus_mt_models.ps1
    └── verify_opus_mt_models_fixed.ps1
```

---

*作成日: 2025年5月28日*
*目的: 開発過程で作成されたスクリプトファイルの整理とアーカイブ*