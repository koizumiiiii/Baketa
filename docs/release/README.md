# Baketa

**Game Translation Overlay Application**

Baketa is a Windows application that translates text on game screens in real-time and displays the results as a transparent overlay.

🇯🇵 [日本語版はこちら](README.ja.md)

---

## System Requirements

### Minimum Requirements
- **OS**: Windows 10/11 (64-bit)
- **CPU**: Intel Core i5 / AMD Ryzen 5 or higher
- **Memory**: 8GB or more (16GB recommended)
- **Storage**: 5GB or more free space (for translation models)
- **Internet**: Required on first launch (to download translation models)

### Recommended (for GPU acceleration)
- **GPU**: NVIDIA GTX 1060 or higher (CUDA compatible)
- **VRAM**: 4GB or more

> **Note**: Baketa works in CPU mode without a GPU, but translation speed will be slower.

---

## Installation

1. Extract the downloaded ZIP file to any folder
2. Double-click `Baketa.exe` in the extracted folder to launch

**Note**: Translation models (~2GB) will be automatically downloaded on first launch.

---

## Translation Modes

Baketa offers two translation modes:

### Live Translation (Automatic)

Continuously monitors the game screen and automatically translates when text changes.

- Click **Start** to begin
- Click **Stop** to end
- Best for text-heavy RPGs and adventure games

### Shot Translation (Manual)

Translates the screen only when you click the button.

- Click **Shot** to translate
- Useful for menus, status screens, and other static content
- Saves resources by translating only when needed

---

## Basic Usage

### 1. Launch Your Game
Start the game you want to translate.

### 2. Launch Baketa
Run `Baketa.exe`.

### 3. Select Game Window
Choose the game window from the main screen.

### 4. Start Translation
- **Automatic**: Click the **Start** button
- **Manual**: Click the **Shot** button

### 5. Stop Translation
Click the **Stop** button to stop translation.

---

## Troubleshooting

### App Won't Start

- **Cause**: .NET Runtime may not be installed
- **Solution**: Install [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

### Model Download Fails

- **Cause**: Network connection issue
- **Solution**:
  1. Check your internet connection
  2. Make sure firewall or security software isn't blocking the download
  3. Wait a moment and try launching again

### Translation Not Showing

- **Cause**: Game window may not be properly selected
- **Solution**:
  1. Re-select the game window
  2. Run the game in Windowed or Borderless Windowed mode

### Overlay Hard to Read

- **Solution**: Adjust font size in the Settings screen

### Translation is Slow

- **Cause**: May be running in CPU mode
- **Solution**: If you have an NVIDIA GPU, verify that CUDA drivers are properly installed

---

## Log File Location

If you encounter issues, check the log files at:

```
%USERPROFILE%\.baketa\settings\logs\
```

Example: `C:\Users\<username>\.baketa\settings\logs\`

---

## Support

- **Bug Reports & Feature Requests**: [GitHub Issues](https://github.com/koizumiiiii/Baketa/issues)
- **Official Website**: [https://koizumiiiii.github.io/Baketa/](https://koizumiiiii.github.io/Baketa/)

---

## License

Copyright (c) 2024-2026 Baketa. All rights reserved.
