# Python gRPC Server GPU Access Violation - 根本原因調査とGPU活用方針

## 🚨 **問題概要**

**症状**: Python gRPCサーバー起動時に`Windows fatal exception: access violation`が発生し、翻訳機能が使用不能

**エラーログ**:
```
[22:04:13.708] Windows fatal exception: access violation
Current thread 0x000086c0 (most recent call first):
  File "C:\Users\suke0\miniconda3\Lib\site-packages\transformers\modeling_utils.py", line 914 in _load_state_dict_into_meta_model
  File "C:\Users\suke0\miniconda3\Lib\site-packages\transformers\modeling_utils.py", line 4434 in _load_pretrained_model
  File "C:\Users\suke0\miniconda3\Lib\site-packages\transformers\modeling_utils.py", line 3960 in from_pretrained
  File "E:\dev\Baketa\grpc_server\engines\nllb_engine.py", line 78 in load_model
```

**発生箇所**:
```python
# grpc_server/engines/nllb_engine.py Line 76-92
self.model = AutoModelForSeq2SeqLM.from_pretrained(
    self.model_name,  # "facebook/nllb-200-distilled-600M"
    torch_dtype=torch.float16,
    device_map="auto"
)
```

## 🔬 **UltraThink調査結果**

### Phase 1: 既存エラーハンドリング確認

**現状の実装**:
```python
try:
    self.model = AutoModelForSeq2SeqLM.from_pretrained(
        self.model_name,
        torch_dtype=torch.float16,
        device_map="auto"
    )
    self.logger.info("Model loaded successfully (GPU)")
except (torch.cuda.OutOfMemoryError, RuntimeError, OSError) as e:
    self.logger.warning(f"GPU loading failed - Fallback to CPU: {e}")
    # CPUフォールバック
    self.model = AutoModelForSeq2SeqLM.from_pretrained(
        self.model_name,
        torch_dtype=torch.float32,
        device_map="cpu"
    )
    self.device = "cpu"
```

**問題点**: `access violation`はWindowsネイティブエラーのため、Python例外としてキャッチされず、プロセスごとクラッシュ

### Phase 2: 根本原因の候補

| 原因候補 | 可能性 | 詳細 | 診断方法 |
|---------|--------|------|----------|
| **GPU/CUDAメモリ不足** | ⭐⭐⭐⭐⭐ | NLLB-200 (600M) + float16がVRAMに収まらず、メモリアクセス違反 | `torch.cuda.memory_summary()` |
| **CUDA/PyTorchバージョン不整合** | ⭐⭐⭐⭐ | CUDAドライバーとPyTorchの互換性問題で不正メモリアクセス | `torch.version.cuda`, `nvidia-smi` |
| **Transformers並行初期化競合** | ⭐⭐⭐ | `device_map="auto"`の自動デバイス割り当て中にメモリ競合 | `CUDA_LAUNCH_BLOCKING=1`で実行 |
| **モデルファイル破損** | ⭐⭐ | ダウンロード失敗・破損でデシリアライズ時にaccess violation | キャッシュクリア後再ダウンロード |
| **マルチGPU環境の問題** | ⭐ | 複数GPU環境でのデバイス割り当て競合 | `CUDA_VISIBLE_DEVICES=0` |

### Phase 3: 環境情報収集の必要性

以下の情報が必要:
1. **CUDA環境**: `nvidia-smi` 出力（GPU型番、VRAMサイズ、使用状況）
2. **PyTorch/CUDA互換性**: `torch.version.cuda`, `torch.cuda.is_available()`
3. **メモリ使用量**: システムメモリ、VRAM使用量
4. **Transformersバージョン**: `transformers.__version__`

## 🎯 **対応方針（GPU活用前提）**

### 方針A: 段階的GPUメモリ最適化 ⭐⭐⭐⭐⭐ (推奨)

**ステップ1: 診断モード実装**
```python
# grpc_server/engines/nllb_engine.py 修正
def load_model(self):
    # 🔬 [DIAGNOSTIC] GPU環境診断
    if torch.cuda.is_available():
        self.logger.info(f"CUDA Version: {torch.version.cuda}")
        self.logger.info(f"GPU Count: {torch.cuda.device_count()}")
        for i in range(torch.cuda.device_count()):
            props = torch.cuda.get_device_properties(i)
            self.logger.info(f"GPU {i}: {props.name}, VRAM: {props.total_memory / 1024**3:.2f} GB")
            self.logger.info(f"GPU {i} Free Memory: {torch.cuda.mem_get_info(i)[0] / 1024**3:.2f} GB")
    else:
        self.logger.warning("CUDA is not available - will use CPU")

    # 🔥 [MEMORY_OPTIMIZATION] 段階的メモリ最適化戦略
    strategies = [
        # Strategy 1: float16 + auto device map
        {"torch_dtype": torch.float16, "device_map": "auto", "low_cpu_mem_usage": True},
        # Strategy 2: int8量子化（メモリ50%削減）
        {"torch_dtype": torch.int8, "device_map": "auto", "load_in_8bit": True},
        # Strategy 3: 明示的GPU 0割り当て
        {"torch_dtype": torch.float16, "device_map": {"": 0}, "low_cpu_mem_usage": True},
        # Strategy 4: CPU+GPU hybrid
        {"torch_dtype": torch.float16, "device_map": "balanced", "low_cpu_mem_usage": True},
    ]

    last_error = None
    for i, strategy in enumerate(strategies, 1):
        try:
            self.logger.info(f"Strategy {i}/{len(strategies)}: {strategy}")
            self.model = AutoModelForSeq2SeqLM.from_pretrained(
                self.model_name,
                **strategy
            )
            self.logger.info(f"✅ Model loaded successfully with Strategy {i}")

            # 使用デバイス確認
            if hasattr(self.model, 'hf_device_map'):
                self.logger.info(f"Device map: {self.model.hf_device_map}")

            return  # 成功したら終了

        except Exception as e:
            last_error = e
            self.logger.warning(f"Strategy {i} failed: {type(e).__name__}: {e}")

            # GPUメモリをクリア
            if torch.cuda.is_available():
                torch.cuda.empty_cache()

            continue

    # 全戦略失敗 → CPUフォールバック
    self.logger.error(f"All GPU strategies failed. Last error: {last_error}")
    self.logger.info("Falling back to CPU...")
    self.model = AutoModelForSeq2SeqLM.from_pretrained(
        self.model_name,
        torch_dtype=torch.float32,
        device_map="cpu"
    )
    self.device = "cpu"
```

**期待効果**:
- 環境に応じた最適なGPU活用戦略を自動選択
- int8量子化でVRAM使用量50%削減可能
- access violation発生前に代替戦略を試行
- 診断ログでGPU環境の詳細把握

### 方針B: 事前環境検証スクリプト ⭐⭐⭐⭐

**新規ファイル**: `grpc_server/diagnose_gpu.py`
```python
#!/usr/bin/env python
"""GPU環境診断スクリプト - NLLB-200モデル実行可能性チェック"""
import torch
from transformers import AutoModelForSeq2SeqLM, AutoTokenizer

def diagnose_gpu_environment():
    print("=" * 60)
    print("GPU Environment Diagnostic Report")
    print("=" * 60)

    # CUDA availability
    print(f"\n[1] CUDA Available: {torch.cuda.is_available()}")
    if not torch.cuda.is_available():
        print("⚠️ CUDA is not available. Only CPU mode is possible.")
        return

    print(f"[2] CUDA Version: {torch.version.cuda}")
    print(f"[3] PyTorch Version: {torch.__version__}")

    # GPU details
    gpu_count = torch.cuda.device_count()
    print(f"\n[4] GPU Count: {gpu_count}")

    for i in range(gpu_count):
        props = torch.cuda.get_device_properties(i)
        free_mem, total_mem = torch.cuda.mem_get_info(i)
        print(f"\n--- GPU {i} ---")
        print(f"  Name: {props.name}")
        print(f"  Total VRAM: {total_mem / 1024**3:.2f} GB")
        print(f"  Free VRAM: {free_mem / 1024**3:.2f} GB")
        print(f"  Compute Capability: {props.major}.{props.minor}")

    # NLLB-200 memory requirements
    print("\n" + "=" * 60)
    print("NLLB-200 Model Memory Requirements")
    print("=" * 60)
    print("facebook/nllb-200-distilled-600M:")
    print("  - float32 (CPU): ~2.4 GB")
    print("  - float16 (GPU): ~1.2 GB VRAM")
    print("  - int8 (GPU):    ~0.6 GB VRAM")

    # Recommendation
    print("\n" + "=" * 60)
    print("Recommendation")
    print("=" * 60)

    if gpu_count > 0:
        free_mem, _ = torch.cuda.mem_get_info(0)
        free_gb = free_mem / 1024**3

        if free_gb >= 1.5:
            print("✅ float16 mode is recommended (sufficient VRAM)")
        elif free_gb >= 0.8:
            print("⚠️ int8 mode is recommended (limited VRAM)")
        else:
            print("❌ CPU mode is recommended (insufficient VRAM)")

    print("=" * 60)

if __name__ == "__main__":
    diagnose_gpu_environment()
```

**実行**: `py grpc_server/diagnose_gpu.py`

### 方針C: CTranslate2統合（最終手段） ⭐⭐⭐⭐⭐

**背景**: CLAUDE.local.mdに既にCTranslate2実装完了の記録あり（Phase 2.2.1）

**利点**:
- メモリ使用量80%削減達成実績あり（2.4GB → 500MB）
- int8量子化による高速推論
- GPU/CPUハイブリッド実行可能

**実装済みファイル**: `grpc_server/engines/ctranslate2_engine.py`

## 📋 **質問事項**

1. **診断優先か実装優先か**:
   - Option A: まず`diagnose_gpu.py`実行で環境診断
   - Option B: 方針A実装で段階的最適化を試行

2. **CTranslate2への完全移行**:
   - NLLB-200のaccess violation問題が根深い場合、CTranslate2に統一すべきか？

3. **int8量子化の精度影響**:
   - NLLB-200のint8量子化で翻訳品質がどの程度低下するか？
   - 実用上許容できる範囲か？

4. **PyTorch/CUDA更新の必要性**:
   - バージョン不整合の可能性がある場合、PyTorch/CUDA再インストールすべきか？

## 🎯 **推奨実装順序**

**Geminiレビュー後の実装**:
1. Phase 1: `diagnose_gpu.py` 実行でGPU環境詳細把握
2. Phase 2: 診断結果に基づいて方針A実装
3. Phase 3: 動作確認（GPUメモリ使用量・翻訳速度測定）
4. Phase 4: 必要に応じてCTranslate2完全移行検討

---

**技術的判断を求めるポイント**:
- access violationの真の原因特定方法
- GPU活用を保ちつつ安定性を確保する最適戦略
- int8量子化 vs CTranslate2 の選択基準
