# Phase 3 DPI Awareness テスト手順書

## 目的
Per-Monitor DPI V2実装の動作確認と高DPI環境での座標精度検証

## テストレベル

### ✅ レベル1: 最小限テスト（5分） - 必須

#### 目的
DPI補正ロジックが実行されているか確認

#### 手順
1. アプリケーション起動
   ```bash
   dotnet run --project Baketa.UI
   ```

2. ゲームウィンドウ選択 → Start → 翻訳実行（1回）

3. ログファイル確認
   ```powershell
   Get-Content "Baketa.UI\bin\Debug\net8.0-windows10.0.19041.0\baketa_debug.log" | Select-String "PHASE3_DPI" | Select-Object -Last 10
   ```

#### 合格基準
以下のいずれかのログが出力されること：

**100% DPI環境**:
```
✅ 📐 [PHASE3_DPI] DPI補正後 - DPI=96, Scale=1.00, Corrected=(X,Y)
```

**125% DPI環境**:
```
✅ 📐 [PHASE3_DPI] DPI補正後 - DPI=120, Scale=1.25, Corrected=(X,Y)
```

**150% DPI環境**:
```
✅ 📐 [PHASE3_DPI] DPI補正後 - DPI=144, Scale=1.50, Corrected=(X,Y)
```

**200% DPI環境**:
```
✅ 📐 [PHASE3_DPI] DPI補正後 - DPI=192, Scale=2.00, Corrected=(X,Y)
```

#### NGパターンと対応

| ログ出力 | 原因 | 対応 |
|---------|------|------|
| ログが出ない | DPI補正が実行されていない | CoordinateTransformationService実装確認 |
| `DPI=0` | GetDpiForWindow失敗 | Windows 10 1607以前の場合は正常（フォールバック動作） |
| `無効なウィンドウハンドル` | ウィンドウ選択の問題 | 正しいウィンドウを選択 |
| 例外ログ | GetDpiForWindow API失敗 | try-catchで捕捉されているか確認 |

---

### ✅ レベル2: 視覚的確認（10分） - 推奨

#### 目的
翻訳オーバーレイの座標精度を視覚的に確認

#### 手順
1. ゲーム起動（Chrono Trigger推奨）
2. Baketa起動 → ウィンドウ選択 → Start
3. 日本語テキストが表示される場面で翻訳実行
4. **翻訳オーバーレイが元のテキストの真上に表示されるか目視確認**

#### 合格基準
- ✅ 翻訳オーバーレイが元の日本語テキスト位置に**正確に重なる**
- ✅ 上下左右どの方向にも座標ズレがない
- ✅ テキストの開始位置（左端）が一致
- ✅ テキストの高さ（Y座標）が一致

#### NGパターンと原因推定

| 現象 | 推定原因 | 確認方法 |
|------|---------|---------|
| **右にズレる** | DPI補正が過剰適用 | ログで `dpiScale > 1` が不要に適用されていないか |
| **左にズレる** | DPI補正不足 | ログで `dpiScale` が正しく計算されているか |
| **下にズレる** | Y座標のDPI補正問題 | ClientToScreen後のY座標確認 |
| **ランダムな位置** | Phase 2以前の問題 | GetWindowPlacement, MonitorFromWindow確認 |

#### デバッグ用ログ確認
```powershell
# 座標変換の全過程を確認
Get-Content "Baketa.UI\bin\Debug\net8.0-windows10.0.19041.0\baketa_debug.log" | Select-String "ROI_SCALE|PHASE3_DPI|SCREEN_COORD" | Select-Object -Last 30
```

期待されるログの流れ：
```
1. [ROI_SCALE] ROIスケーリング後: (X,Y,W,H)
2. [PHASE3_DPI] DPI補正後 - DPI=96, Scale=1.00, Corrected=(X,Y)
3. [SCREEN_COORD] ClientToScreen後: (X,Y)
4. [MONITOR_OFFSET] モニターオフセット補正: (X,Y)
```

---

### ✅ レベル3: 高DPI環境テスト（30分） - オプション

#### 目的
複数のDPI設定（125%, 150%, 200%）での動作確認

#### 前提条件
- **Windows 10 1703以降** (Per-Monitor DPI V2対応)
- **ディスプレイ設定変更権限**
- **アプリケーション再起動が必要**

#### 手順

##### 3.1: 現在のDPI設定確認
```powershell
# PowerShellで現在のDPI設定確認
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class DisplayDPI {
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    public static int GetCurrentDPI() {
        IntPtr hdc = GetDC(IntPtr.Zero);
        int dpi = GetDeviceCaps(hdc, 88); // LOGPIXELSX
        return dpi;
    }
}
"@
[DisplayDPI]::GetCurrentDPI()
```

出力例: `96` (100%), `120` (125%), `144` (150%), `192` (200%)

##### 3.2: DPI設定変更
1. Windows設定を開く: `Win + I`
2. **システム** → **ディスプレイ**
3. **拡大縮小とレイアウト** → **テキスト、アプリ、その他の項目のサイズを変更する**
4. **125%**, **150%**, **200%** から選択
5. **サインアウトして設定を適用** ← 重要

##### 3.3: 各DPI設定でのテスト
| DPI設定 | 期待DPI値 | 期待dpiScale | テスト内容 |
|---------|----------|-------------|-----------|
| **100%** | 96 | 1.00 | ベースライン確認 |
| **125%** | 120 | 1.25 | 1.25倍補正確認 |
| **150%** | 144 | 1.50 | 1.50倍補正確認 |
| **200%** | 192 | 2.00 | 2.00倍補正確認 |

各設定で以下を確認：
1. ✅ アプリ起動 → 翻訳実行
2. ✅ ログで `DPI={期待DPI値}, Scale={期待dpiScale}` 確認
3. ✅ オーバーレイ座標が正確に一致するか視覚確認
4. ✅ スクリーンショット撮影（証拠記録）

##### 3.4: マルチモニター環境テスト（オプション）
**前提**: 異なるDPI設定の複数モニター

1. モニター1: 100% DPI
2. モニター2: 125% DPI

**テスト**:
- ゲームウィンドウをモニター1に配置 → 翻訳実行 → DPI=96確認
- ゲームウィンドウをモニター2に移動 → 翻訳実行 → DPI=120確認
- **Per-Monitor DPI V2により、自動的にDPIが切り替わるか確認**

---

## 最終合格基準

### 必須条件（レベル1+2）
- ✅ DPI補正ログが正しく出力される
- ✅ DPI値が96の倍数（100%=96, 125%=120, 150%=144, 200%=192）
- ✅ dpiScaleが正しく計算される（1.0, 1.25, 1.5, 2.0）
- ✅ 翻訳オーバーレイが正しい位置に表示される
- ✅ エラーログが出ない（`dpi==0`チェックが動作）

### オプション条件（レベル3）
- ✅ 複数のDPI設定（125%, 150%, 200%）で動作確認
- ✅ マルチモニター環境での動作確認

---

## トラブルシューティング

### Q1: DPI補正ログが出ない
**原因**: CoordinateTransformationServiceでDPI補正ロジックが実行されていない

**確認**:
```powershell
# コード確認
Get-Content "Baketa.Infrastructure.Platform\Windows\Services\CoordinateTransformationService.cs" | Select-String "PHASE3_DPI" -Context 2
```

**対応**: Phase 3実装を再確認

### Q2: `DPI=0` が出力される
**原因**: GetDpiForWindow APIが失敗（Windows 10 1607以前、無効なハンドル）

**確認**: Gemini P0修正が適用されているか
```csharp
if (dpi == 0)
{
    _logger.LogWarning("⚠️ [PHASE3_DPI] GetDpiForWindow返り値が0");
}
```

**対応**: フォールバック処理が正常動作していれば問題なし

### Q3: オーバーレイがズレる（DPI補正後も）
**原因**: Phase 2以前の問題、またはDPI補正の計算ミス

**確認**:
1. Phase 2実装（ClientToScreen, GetWindowPlacement）が正常か
2. dpiScaleの計算式が正しいか（`dpi / 96.0f`）

**対応**: 座標変換の全過程ログ確認
```powershell
Get-Content "baketa_debug.log" | Select-String "ConvertRoiToScreenCoordinates" -Context 10
```

---

## テスト記録テンプレート

### 環境情報
- OS: Windows 10/11 バージョン _____
- DPI設定: _____% (DPI値: _____)
- モニター: _____ (解像度: _____)
- テスト対象: Baketa Phase 3 DPI Awareness

### レベル1: 最小限テスト
- [ ] DPI補正ログ出力: ✅ / ❌
- [ ] DPI値: _____
- [ ] dpiScale: _____
- [ ] エラーログ: なし / あり（内容: _____）

### レベル2: 視覚的確認
- [ ] オーバーレイ座標: 正確 / ズレあり（方向: _____）
- [ ] スクリーンショット: 添付 / なし

### レベル3: 高DPI環境テスト（オプション）
| DPI設定 | DPI値 | dpiScale | オーバーレイ座標 | 結果 |
|---------|-------|---------|----------------|------|
| 100% | 96 | 1.00 | 正確 / ズレ | ✅ / ❌ |
| 125% | 120 | 1.25 | 正確 / ズレ | ✅ / ❌ |
| 150% | 144 | 1.50 | 正確 / ズレ | ✅ / ❌ |
| 200% | 192 | 2.00 | 正確 / ズレ | ✅ / ❌ |

### 総合評価
- [ ] Phase 3実装: 合格 / 不合格
- [ ] 備考: _____
