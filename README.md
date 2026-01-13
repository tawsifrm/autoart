# AutoArt

A multi-color automated drawing application for Windows that splits images into color layers and draws them automatically using simulated mouse input.

![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)
![Avalonia UI](https://img.shields.io/badge/Avalonia-11.2-blue)
![Windows](https://img.shields.io/badge/Platform-Windows-lightgrey)

## Features

- **Color Quantization**: Automatically reduces images to a specified number of colors using advanced algorithms (MedianCut, K-Means)
- **Layer-Based Drawing**: Splits images into separate color layers that can be drawn individually
- **Multiple Drawing Algorithms**: Choose between DFS (Depth-First Search) or Edge-Following algorithms for optimal drawing paths
- **Live Preview**: Draggable overlay preview to precisely position your drawing before starting
- **Adjustable Scale**: Scale your image up or down before drawing
- **Session Management**: Draw multiple layers in sequence with automatic layer tracking
- **Configurable Speed**: Adjust drawing interval and click delay to match your target application

## How It Works

1. **Load an Image**: Import any image file (PNG, JPG, BMP, etc.)
2. **Configure Colors**: Set the number of colors to quantize the image to
3. **Split Image**: The application analyzes the image and creates separate layers for each color
4. **Position Preview**: Drag the transparent overlay to position where you want to draw
5. **Draw Layers**: Press Shift to start drawing each layer - the application simulates mouse movements to recreate the image

The drawing engine converts each color layer into a series of mouse movements and clicks, effectively "drawing" the image in any application that accepts mouse input (paint programs, drawing games, etc.).

## Requirements

- Windows 10/11
- .NET 8.0 Runtime

## Building from Source

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 or VS Code (optional)

### Build Steps

```bash
# Clone the repository
git clone https://github.com/YOUR_USERNAME/AutoArt.git
cd AutoArt

# Restore dependencies
dotnet restore

# Build in Debug mode
dotnet build

# Or build in Release mode
dotnet build -c Release
```

### Running

```bash
# Run the application
dotnet run

# Or run the built executable directly
./bin/Debug/net8.0-windows/AutoArt.exe
```

### Publishing

To create a standalone executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

The published files will be in `bin/Release/net8.0-windows/win-x64/publish/`

## Usage

### Basic Workflow

1. Launch AutoArt
2. Click **Load Image** to select your source image
3. Adjust the **Number of Colors** slider (default: 8)
4. Click **Split Image** to generate color layers
5. Select a layer from the list on the left
6. Click **Preview Layer** to show the positioning overlay
7. Drag the preview to your desired location
8. Press **F6** to start drawing (or click the Draw button)
9. Press **Alt** to stop drawing at any time

### Keyboard Shortcuts

| Key | Action                            |
| --- | --------------------------------- |
| F6  | Start drawing                     |
| Alt | Stop drawing (current layer only) |

### Configuration Options

- **Drawing Interval**: Controls the speed between draw operations (lower = faster)
- **Click Delay**: Time between mouse clicks
- **Algorithm**: Choose between DFS or Edge-Following drawing patterns
- **Scale**: Resize the image before drawing (10% - 500%)

## Project Structure

```
AutoArt/
├── Algorithms/          # Color quantization algorithms
│   ├── ImageSplitting.cs
│   ├── KMeans.cs
│   ├── LabHelper.cs
│   ├── MedianCut.cs
│   └── SeedsSuperpixels.cs
├── Core/                # Drawing engine
│   ├── Config.cs
│   ├── Drawing.cs
│   ├── DrawDataDisplay.axaml
│   └── Input.cs
├── Models/              # Data models
│   ├── AppState.cs
│   ├── ColorLayer.cs
│   └── SplitConfiguration.cs
├── Services/            # Business logic
│   ├── ColorSplittingService.cs
│   └── DrawingService.cs
├── Styles/              # UI themes
│   ├── dark.axaml
│   ├── light.axaml
│   └── universal.axaml
├── Views/               # UI windows
│   ├── MainWindow.axaml
│   ├── LayerPreview.axaml
│   └── DrawingGuideWindow.axaml
├── App.axaml
├── Program.cs
└── AutoArt.csproj
```

## Technologies

- **[Avalonia UI](https://avaloniaui.net/)** - Cross-platform UI framework
- **[SkiaSharp](https://github.com/mono/SkiaSharp)** - 2D graphics library for image processing
- **[SharpHook](https://github.com/TolikPyl662/SharpHook)** - Global keyboard/mouse hooks
- **[SimWinMouse](https://github.com/Wikipedia/SimWinMouse)** - Windows mouse simulation

## License

This project is open source. See the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request
