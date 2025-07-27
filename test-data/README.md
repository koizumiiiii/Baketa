# OCR前処理最適化用テストデータセット

## 目的
PaddleOCRとPP-OCRv5の前処理パラメータ最適化のための正解データセット

## ディレクトリ構成
- `bright-scenes/` - 明るいゲーム画面（昼間、明るいUI等）
- `dark-scenes/` - 暗いゲーム画面（夜間、ダンジョン等）
- `menu-ui/` - メニュー画面（インベントリ、設定等）
- `dialog-text/` - 会話テキスト（NPC会話、システムメッセージ等）
- `status-windows/` - ステータス画面（HP/MP、レベル情報等）
- `battle-ui/` - バトル画面（スキル名、ダメージ表示等）

## ファイル命名規則
`{scene-type}_{brightness}_{number}.png`

例:
- `dialog_bright_001.png` - 明るい会話画面
- `status_dark_001.png` - 暗いステータス画面
- `menu_normal_001.png` - 通常の明度のメニュー

## 推奨画像
1. **多様な明度レベル** - 明るい/普通/暗い
2. **様々なフォントサイズ** - 大きなタイトル/小さなステータス文字
3. **異なる背景複雑度** - シンプルUI/複雑なゲーム画面背景
4. **多様なテキスト種類** - ひらがな/カタカナ/漢字/英数字

## データ形式
正解データは `ground-truth-data.json` に保存：
```json
{
  "dataset": [
    {
      "imagePath": "bright-scenes/dialog_bright_001.png",
      "groundTruthText": "勇者よ、この剣を持っていけ！",
      "sceneType": "dialog",
      "brightness": "bright",
      "createdAt": "2025-07-27T10:00:00Z"
    }
  ]
}
```