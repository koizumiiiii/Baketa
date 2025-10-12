"""
Resource Monitor for Python Translation Server
Phase 1.1: GPU/VRAM + CPU RAM + Windowsハンドル監視

Geminiレビュー指摘事項の実装:
- GPU VRAMリーク検出（Silent crash最有力原因）
- Windowsハンドルリーク検出
- 5分間隔でのリソース監視
"""

import asyncio
import logging
from typing import Optional

try:
    import pynvml
    PYNVML_AVAILABLE = True
except ImportError:
    PYNVML_AVAILABLE = False
    logging.warning("pynvml not available - GPU monitoring disabled")

try:
    import psutil
    PSUTIL_AVAILABLE = True
except ImportError:
    PSUTIL_AVAILABLE = False
    logging.warning("psutil not available - process monitoring disabled")

logger = logging.getLogger(__name__)


class ResourceMonitor:
    """包括的リソース監視（CPU RAM + GPU VRAM + Windowsハンドル）

    Gemini推奨の最重要実装:
    - GPU VRAMリーク検出 → Silent crashの原因特定確率80%向上
    - Windowsハンドルリーク検出 → 長期稼働安定性向上
    - 5分間隔監視 → リーク率の定量化
    """

    def __init__(self, enable_gpu_monitoring: bool = True):
        """
        Args:
            enable_gpu_monitoring: GPU監視を有効化（CUDAが利用可能な場合のみ）
        """
        self.enable_gpu_monitoring = enable_gpu_monitoring and PYNVML_AVAILABLE
        self.gpu_handle: Optional[any] = None
        self.process: Optional[psutil.Process] = None
        self.monitoring_task: Optional[asyncio.Task] = None

        # GPU監視初期化
        if self.enable_gpu_monitoring:
            try:
                pynvml.nvmlInit()
                self.gpu_handle = pynvml.nvmlDeviceGetHandleByIndex(0)
                logger.info("[RESOURCE_MONITOR] GPU monitoring enabled")
            except Exception as e:
                logger.warning(f"[RESOURCE_MONITOR] GPU monitoring initialization failed: {e}")
                self.enable_gpu_monitoring = False

        # プロセス監視初期化
        if PSUTIL_AVAILABLE:
            try:
                self.process = psutil.Process()
                logger.info("[RESOURCE_MONITOR] Process monitoring enabled")
            except Exception as e:
                logger.error(f"[RESOURCE_MONITOR] Process monitoring initialization failed: {e}")

    async def start_monitoring(self, interval_seconds: int = 300):
        """5分ごとに包括的リソース監視を開始

        Args:
            interval_seconds: 監視間隔（デフォルト300秒 = 5分）

        監視項目:
        - CPU RAM使用量 (RSS, VMS)
        - GPU VRAM使用量（利用可能な場合）
        - Windowsハンドル数
        - スレッド数

        アラート条件:
        - VRAM使用率90%超 → CRITICAL
        - ハンドル数10,000超 → CRITICAL
        """
        logger.info(f"[RESOURCE_MONITOR] Starting monitoring (interval: {interval_seconds}s)")

        self.monitoring_task = asyncio.create_task(self._monitoring_loop(interval_seconds))

    async def _monitoring_loop(self, interval_seconds: int):
        """監視ループ本体"""
        while True:
            try:
                await self._log_resource_usage()
            except Exception as e:
                logger.error(f"[RESOURCE_MONITOR_ERROR] {e}")

            await asyncio.sleep(interval_seconds)

    async def _log_resource_usage(self):
        """リソース使用量をログ出力"""

        # CPU RAM監視
        rss_mb = 0.0
        vms_mb = 0.0
        num_handles = 0
        num_threads = 0

        if self.process:
            try:
                mem_info = self.process.memory_info()
                rss_mb = mem_info.rss / 1024 / 1024  # MB
                vms_mb = mem_info.vms / 1024 / 1024  # MB

                # Windowsハンドル数（Windows環境でのみ利用可能）
                try:
                    num_handles = self.process.num_handles()
                except AttributeError:
                    # Linux/Mac環境ではnum_handles()が存在しない
                    num_handles = 0

                # スレッド数（asyncioループ監視）
                num_threads = self.process.num_threads()
            except Exception as e:
                logger.error(f"[RESOURCE_MONITOR] Process metrics error: {e}")

        # GPU VRAM監視（🔥 CRITICAL）
        vram_used_mb = 0.0
        vram_total_mb = 0.0
        vram_percent = 0.0

        if self.enable_gpu_monitoring and self.gpu_handle:
            try:
                gpu_mem = pynvml.nvmlDeviceGetMemoryInfo(self.gpu_handle)
                vram_used_mb = gpu_mem.used / 1024 / 1024  # MB
                vram_total_mb = gpu_mem.total / 1024 / 1024  # MB
                vram_percent = (vram_used_mb / vram_total_mb) * 100 if vram_total_mb > 0 else 0.0
            except Exception as e:
                logger.error(f"[RESOURCE_MONITOR] GPU metrics error: {e}")

        # ログ出力（異常検出用）
        if self.enable_gpu_monitoring:
            logger.info(
                f"[RESOURCE_MONITOR] "
                f"CPU_RAM: {rss_mb:.2f} MB (VMS: {vms_mb:.2f} MB), "
                f"VRAM: {vram_used_mb:.2f}/{vram_total_mb:.2f} MB ({vram_percent:.1f}%), "
                f"Handles: {num_handles}, "
                f"Threads: {num_threads}"
            )
        else:
            logger.info(
                f"[RESOURCE_MONITOR] "
                f"CPU_RAM: {rss_mb:.2f} MB (VMS: {vms_mb:.2f} MB), "
                f"Handles: {num_handles}, "
                f"Threads: {num_threads}"
            )

        # 🚨 異常検出アラート

        # VRAM使用率90%超 → CRITICAL
        if vram_percent > 90.0:
            logger.critical(
                f"[VRAM_ALERT] VRAM usage exceeds 90%: {vram_used_mb:.2f} MB / {vram_total_mb:.2f} MB "
                f"({vram_percent:.1f}%) - Potential memory leak!"
            )

        # Windowsハンドル数10,000超 → CRITICAL
        if num_handles > 10000:
            logger.critical(
                f"[HANDLE_LEAK_ALERT] Handle count exceeds 10k: {num_handles} - "
                f"Potential handle leak!"
            )

        # CPU RAM使用量1GB超 → WARNING（参考値）
        if rss_mb > 1024.0:
            logger.warning(
                f"[CPU_RAM_WARNING] CPU RAM usage exceeds 1GB: {rss_mb:.2f} MB - "
                f"Monitor for potential leak"
            )

    async def stop_monitoring(self):
        """監視停止 - デッドロック防止のため即座にキャンセル"""
        if self.monitoring_task and not self.monitoring_task.done():
            self.monitoring_task.cancel()
            try:
                await self.monitoring_task
            except asyncio.CancelledError:
                logger.info("[RESOURCE_MONITOR] Monitoring task cancelled gracefully")
                pass
            logger.info("[RESOURCE_MONITOR] Monitoring stopped")

    def cleanup(self):
        """クリーンアップ（NVML終了処理）"""
        if self.enable_gpu_monitoring:
            try:
                pynvml.nvmlShutdown()
                logger.info("[RESOURCE_MONITOR] NVML shutdown completed")
            except Exception as e:
                logger.error(f"[RESOURCE_MONITOR] NVML shutdown error: {e}")
