using System;
using System.Numerics;
using System.Threading.Tasks;
using AutoArt.Core;
using AutoArt.Models;
using SharpHook;
using SharpHook.Native;
using SkiaSharp;

namespace AutoArt.Services;

/// <summary>
/// Service that wraps drawing functionality for drawing images.
/// </summary>
public class DrawingService
{
    private bool _isInitialized = false;
    private TaskPoolGlobalHook? _hook;

    /// <summary>
    /// The global keyboard/mouse hook for this service.
    /// </summary>
    public TaskPoolGlobalHook? Hook => _hook;

    /// <summary>
    /// Current mouse position.
    /// </summary>
    public Vector2 MousePosition { get; private set; }

    /// <summary>
    /// Event fired when drawing of a layer is complete.
    /// </summary>
    public event EventHandler? LayerDrawingComplete;

    /// <summary>
    /// Event fired when all layers are drawn.
    /// </summary>
    public event EventHandler? AllLayersComplete;

    /// <summary>
    /// Event fired when a key is pressed.
    /// </summary>
    public event EventHandler<KeyboardHookEventArgs>? KeyPressed;

    /// <summary>
    /// Initializes the input system. Should be called once at app startup.
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;

        // Initialize our own hook
        _hook = new TaskPoolGlobalHook();

        _hook.MouseMoved += (sender, e) =>
        {
            MousePosition = new Vector2(e.Data.X, e.Data.Y);
        };

        _hook.KeyPressed += (sender, e) =>
        {
            KeyPressed?.Invoke(this, e);
        };

        _hook.RunAsync();

        // Initialize config
        Config.Init();
        _isInitialized = true;
    }

    /// <summary>
    /// Stops the input system. Should be called when the app is closing.
    /// </summary>
    public void Shutdown()
    {
        if (!_isInitialized) return;

        Drawing.Halt();
        _hook?.Dispose();
        _hook = null;
        _isInitialized = false;
    }

    /// <summary>
    /// Sets up the drawing parameters.
    /// </summary>
    /// <param name="interval">Drawing interval in ticks.</param>
    /// <param name="clickDelay">Click delay in milliseconds.</param>
    /// <param name="algorithm">Algorithm: 0 = DFS, 1 = Edge-Following.</param>
    public void ConfigureDrawing(int interval = 10000, int clickDelay = 1000, byte algorithm = 0)
    {
        Drawing.Interval = interval;
        Drawing.ClickDelay = clickDelay;
        Drawing.ChosenAlgorithm = algorithm;
    }

    /// <summary>
    /// Processes a layer bitmap for drawing.
    /// For color layers, we convert based on alpha only - any opaque pixel should be drawn.
    /// </summary>
    /// <param name="layer">The color layer to process.</param>
    /// <returns>The processed bitmap ready for drawing.</returns>
    public SKBitmap ProcessLayerForDrawing(ColorLayer layer)
    {
        // For color layers, we need to convert based on alpha only.
        // Colored pixels (any opaque pixel) should become black (drawable).
        // Transparent pixels should become white (non-drawable).
        // We don't use luminosity thresholding because layer colors can be light (yellow, cyan, etc.)
        return ConvertLayerToDrawable(layer.Bitmap, alphaThreshold: 128);
    }

    /// <summary>
    /// Converts a color layer bitmap to a black/white drawable format.
    /// Opaque pixels (alpha >= threshold) become black (drawable).
    /// Transparent pixels become white (non-drawable).
    /// </summary>
    private unsafe SKBitmap ConvertLayerToDrawable(SKBitmap sourceBitmap, byte alphaThreshold = 128)
    {
        int width = sourceBitmap.Width;
        int height = sourceBitmap.Height;

        SKBitmap outputBitmap = new SKBitmap(width, height);

        byte* srcPtr = (byte*)sourceBitmap.GetPixels().ToPointer();
        byte* dstPtr = (byte*)outputBitmap.GetPixels().ToPointer();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Read BGRA
                byte b = *srcPtr++;
                byte g = *srcPtr++;
                byte r = *srcPtr++;
                byte a = *srcPtr++;

                // If pixel is opaque (alpha >= threshold), make it black (drawable)
                // Otherwise, make it white (non-drawable)
                byte value = a >= alphaThreshold ? (byte)0 : (byte)255;

                // Write BGRA (black or white with full opacity)
                *dstPtr++ = value; // B
                *dstPtr++ = value; // G
                *dstPtr++ = value; // R
                *dstPtr++ = 255;   // A (always fully opaque in output)
            }
        }

        return outputBitmap;
    }

    /// <summary>
    /// Draws a single layer at the specified position.
    /// </summary>
    /// <param name="layer">The layer to draw.</param>
    /// <param name="position">The screen position to start drawing.</param>
    public async Task DrawLayerAsync(ColorLayer layer, Vector2 position)
    {
        if (Drawing.IsDrawing) return;

        var processedBitmap = ProcessLayerForDrawing(layer);

        await Drawing.Draw(processedBitmap, position);

        layer.IsDrawn = true;
        LayerDrawingComplete?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Halts any ongoing drawing operation.
    /// </summary>
    public void StopDrawing()
    {
        Drawing.Halt();
    }

    /// <summary>
    /// Gets whether drawing is currently in progress.
    /// </summary>
    public bool IsDrawing => Drawing.IsDrawing;

    /// <summary>
    /// Gets or sets the last drawing position.
    /// </summary>
    public Vector2 LastPosition
    {
        get => Drawing.LastPos;
        set => Drawing.LastPos = value;
    }

    /// <summary>
    /// Gets the start drawing keybind.
    /// </summary>
    public SharpHook.Native.KeyCode StartDrawingKey => Config.Keybind_StartDrawing;

    /// <summary>
    /// Gets the stop drawing keybind.
    /// </summary>
    public SharpHook.Native.KeyCode StopDrawingKey => Config.Keybind_StopDrawing;
}
