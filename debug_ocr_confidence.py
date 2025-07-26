#!/usr/bin/env python3
"""
OCR信頼度とフィルタリングの確認スクリプト
"""
import os
from PIL import Image, ImageDraw, ImageFont

def create_debug_images():
    """デバッグ用のテスト画像を作成"""
    test_cases = [
        ("戦", "clear_jp_text.png", (255, 255, 255), (0, 0, 0)),     # 白背景、黒文字（高信頼度期待）
        ("戦", "low_contrast.png", (200, 200, 200), (150, 150, 150)), # 低コントラスト（低信頼度期待）
        ("戦闘開始", "multi_jp_text.png", (255, 255, 255), (0, 0, 0)), # 複数文字（正常認識期待）
        ("Battle Start", "clear_en_text.png", (255, 255, 255), (0, 0, 0)), # 英語テキスト
        ("", "noise_only.png", (255, 255, 255), None)                 # ノイズのみ（誤認識テスト）
    ]
    
    os.makedirs('debug_images', exist_ok=True)
    
    for text, filename, bg_color, text_color in test_cases:
        image = Image.new('RGB', (400, 120), bg_color)
        draw = ImageDraw.Draw(image)
        
        if text_color and text:  # テキストがある場合
            try:
                font = ImageFont.load_default()
            except:
                font = None
            
            # テキストを中央に描画
            bbox = draw.textbbox((0, 0), text, font=font)
            text_width = bbox[2] - bbox[0]
            text_height = bbox[3] - bbox[1]
            
            x = (400 - text_width) // 2
            y = (120 - text_height) // 2
            
            draw.text((x, y), text, fill=text_color, font=font)
        elif not text:  # ノイズのみの場合
            # ランダムなノイズを追加
            for _ in range(20):
                x, y = draw.random.randint(10, 390), draw.random.randint(10, 110)
                draw.point((x, y), fill=(100, 100, 100))
        
        image_path = os.path.join('debug_images', filename)
        image.save(image_path)
        expected = text if text else "なし"
        print(f"デバッグ画像生成: {filename} - 期待テキスト: '{expected}'")

def create_confidence_test_report():
    """信頼度テスト用レポートを作成"""
    report_content = """# OCR信頼度フィルタリング検証レポート

## 生成された画像と期待結果

### 1. clear_jp_text.png
- **期待テキスト**: 戦
- **期待信頼度**: 90%以上
- **判定**: 高信頼度で認識されるべき

### 2. low_contrast.png  
- **期待テキスト**: 戦
- **期待信頼度**: 50%以下
- **判定**: 低信頼度で除外されるべき

### 3. multi_jp_text.png
- **期待テキスト**: 戦闘開始
- **期待信頼度**: 80%以上
- **判定**: 複数文字が正常認識されるべき

### 4. clear_en_text.png
- **期待テキスト**: Battle Start
- **期待信頼度**: 80%以上
- **判定**: 英語テキストも認識されるべき

### 5. noise_only.png
- **期待テキスト**: なし
- **期待信頼度**: 10%以下
- **判定**: ノイズは除外されるべき

## 検証手順

1. Baketaアプリを起動
2. debug_images/*.png を画面にして OCR実行
3. ログで信頼度と認識結果を確認
4. ConfidenceThreshold=0.7 が正しく機能しているか確認

## 改善案

### 信頼度フィルタリング強化
```csharp
// 現在: 信頼度70%以下も表示されている
if (region.Confidence < settings.ConfidenceThreshold)
    continue; // 除外処理が必要

// 改善: 段階的フィルタリング
var highConfidenceRegions = regions.Where(r => r.Confidence >= 0.8).ToList();
var mediumConfidenceRegions = regions.Where(r => r.Confidence >= 0.5 && r.Confidence < 0.8).ToList();
```

### 期待テキスト照合システム
```csharp
// 画像ファイル名から期待テキストを推定
public string ExtractExpectedTextFromFilename(string imagePath)
{
    var filename = Path.GetFileNameWithoutExtension(imagePath);
    return filename switch
    {
        "clear_jp_text" => "戦",
        "multi_jp_text" => "戦闘開始", 
        "clear_en_text" => "Battle Start",
        "noise_only" => "",
        _ => null
    };
}
```
"""
    
    with open('debug_images/confidence_test_report.md', 'w', encoding='utf-8') as f:
        f.write(report_content)
    
    print("信頼度テストレポート生成: debug_images/confidence_test_report.md")

if __name__ == "__main__":
    print("🔍 OCR信頼度フィルタリング検証用画像生成中...")
    create_debug_images()
    create_confidence_test_report()
    print("✅ デバッグ画像生成完了")
    print("\n📋 次のステップ:")
    print("1. Baketaアプリを起動")
    print("2. debug_images/*.png をキャプチャ対象として認識")  
    print("3. OCRログで信頼度と認識結果を確認")
    print("4. confidence_test_report.md の期待結果と比較")