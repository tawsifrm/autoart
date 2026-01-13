using System;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AutoArt.Models;
using SharpHook;
using SharpHook.Native;
using SkiaSharp;

namespace AutoArt.Views;

/// <summary>
/// A repositionable preview overlay for a color layer.
/// Shows the layer image as a draggable overlay that the user can position before drawing.
/// </summary>
public partial class LayerPreview : Window
{
    // Events
    public event EventHandler? DrawRequested;
    public event EventHandler? CancelRequested;

    // State
    private ColorLayer? _currentLayer;
    private SKBitmap? _scaledBitmap;
    private Bitmap? _displayBitmap;
    private double _screenScale = 1;
    private bool _isMoving;
    private PointerPoint? _originalPoint;
    private bool _isUpdatingPosition;

    // The hook reference from DrawingService
    private TaskPoolGlobalHook? _hook;

    /// <summary>
    /// The last position where the preview was placed (for drawing).
    /// </summary>
    public Vector2 LastPosition { get; private set; }

    public LayerPreview()
    {
        InitializeComponent();

        if (Design.IsDesignMode) return;

        // Get screen scale
        var screen = Screens.ScreenFromWindow(this);
        if (screen != null)
        {
            _screenScale = screen.Scaling;
        }

        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        UnhookKeyboard();
        _displayBitmap?.Dispose();
        _displayBitmap = null;
    }

    /// <summary>
    /// Sets up the preview with a color layer and shows it.
    /// </summary>
    /// <param name="layer">The color layer to preview.</param>
    /// <param name="layerIndex">1-based index of the layer.</param>
    /// <param name="totalLayers">Total number of layers.</param>
    /// <param name="scale">Scale percentage (1-200).</param>
    /// <param name="hook">The keyboard hook to use for key detection.</param>
    /// <param name="initialPosition">Optional initial position.</param>
    public void ShowLayer(ColorLayer layer, int layerIndex, int totalLayers, int scale, TaskPoolGlobalHook? hook, Vector2? initialPosition = null)
    {
        _currentLayer = layer;
        _hook = hook;

        // Scale the bitmap
        var scaleFactor = scale / 100.0;
        var newWidth = (int)(layer.Bitmap.Width * scaleFactor);
        var newHeight = (int)(layer.Bitmap.Height * scaleFactor);

        if (newWidth < 1) newWidth = 1;
        if (newHeight < 1) newHeight = 1;

        _scaledBitmap?.Dispose();
        // Use nearest-neighbor (None) to avoid anti-aliasing artifacts at transparent edges
        // High quality interpolation creates semi-transparent edge pixels that get drawn as borders
        _scaledBitmap = layer.Bitmap.Resize(new SKSizeI(newWidth, newHeight), SKFilterQuality.None);

        // Convert to Avalonia bitmap
        _displayBitmap?.Dispose();
        _displayBitmap = ConvertToAvaloniaBitmap(_scaledBitmap);
        PreviewImage.Source = _displayBitmap;

        // Set window size
        Width = newWidth / _screenScale;
        Height = newHeight / _screenScale;

        // Set position
        if (initialPosition.HasValue)
        {
            Position = new PixelPoint((int)initialPosition.Value.X, (int)initialPosition.Value.Y);
        }
        else
        {
            // Default to center of primary screen
            var primaryScreen = Screens.Primary;
            if (primaryScreen != null)
            {
                var centerX = (primaryScreen.Bounds.Width - (int)Width) / 2;
                var centerY = (primaryScreen.Bounds.Height - (int)Height) / 2;
                Position = new PixelPoint(centerX, centerY);
            }
        }

        // Update last position
        LastPosition = new Vector2(Position.X, Position.Y);

        // Update UI
        _isUpdatingPosition = true;
        XPos.Text = Position.X.ToString();
        YPos.Text = Position.Y.ToString();
        _isUpdatingPosition = false;

        LayerInfoText.Text = $"Layer {layerIndex}/{totalLayers}";
        ColorHexText.Text = layer.HexColor;
        ColorSwatch.Background = new SolidColorBrush(layer.Color);

        // Hook keyboard
        HookKeyboard();

        Show();
    }

    /// <summary>
    /// Updates the preview for a new layer without closing the window.
    /// </summary>
    public void UpdateLayer(ColorLayer layer, int layerIndex, int totalLayers, int scale)
    {
        _currentLayer = layer;

        // Scale the bitmap
        var scaleFactor = scale / 100.0;
        var newWidth = (int)(layer.Bitmap.Width * scaleFactor);
        var newHeight = (int)(layer.Bitmap.Height * scaleFactor);

        if (newWidth < 1) newWidth = 1;
        if (newHeight < 1) newHeight = 1;

        _scaledBitmap?.Dispose();
        // Use nearest-neighbor (None) to avoid anti-aliasing artifacts at transparent edges
        // High quality interpolation creates semi-transparent edge pixels that get drawn as borders
        _scaledBitmap = layer.Bitmap.Resize(new SKSizeI(newWidth, newHeight), SKFilterQuality.None);

        // Convert to Avalonia bitmap
        _displayBitmap?.Dispose();
        _displayBitmap = ConvertToAvaloniaBitmap(_scaledBitmap);
        PreviewImage.Source = _displayBitmap;

        // Update window size but keep position
        Width = newWidth / _screenScale;
        Height = newHeight / _screenScale;

        // Update UI
        LayerInfoText.Text = $"Layer {layerIndex}/{totalLayers}";
        ColorHexText.Text = layer.HexColor;
        ColorSwatch.Background = new SolidColorBrush(layer.Color);
    }

    /// <summary>
    /// Gets a copy of the scaled bitmap for drawing.
    /// The caller is responsible for disposing the returned bitmap.
    /// </summary>
    public SKBitmap? GetScaledBitmap() => _scaledBitmap?.Copy();

    private void HookKeyboard()
    {
        if (_hook != null)
        {
            _hook.KeyReleased += OnKeyReleased;
        }
    }

    private void UnhookKeyboard()
    {
        if (_hook != null)
        {
            _hook.KeyReleased -= OnKeyReleased;
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == KeyCode.VcLeftShift || e.Data.KeyCode == KeyCode.VcRightShift)
        {
            // SHIFT pressed - start drawing
            LastPosition = new Vector2(Position.X, Position.Y);
            Dispatcher.UIThread.Post(() =>
            {
                DrawRequested?.Invoke(this, EventArgs.Empty);
            });
        }
        else if (e.Data.KeyCode == KeyCode.VcEscape)
        {
            // ESC pressed - cancel
            Dispatcher.UIThread.Post(() =>
            {
                CancelRequested?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    // Pointer handling for dragging
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isMoving = true;
        _originalPoint = e.GetCurrentPoint(this);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isMoving = false;
        LastPosition = new Vector2(Position.X, Position.Y);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isMoving || _originalPoint == null) return;

        var currentPoint = e.GetCurrentPoint(this);
        var deltaX = XLock.IsChecked == true ? 0 : (int)(currentPoint.Position.X - _originalPoint.Value.Position.X);
        var deltaY = YLock.IsChecked == true ? 0 : (int)(currentPoint.Position.Y - _originalPoint.Value.Position.Y);

        Position = new PixelPoint(Position.X + deltaX, Position.Y + deltaY);
        LastPosition = new Vector2(Position.X, Position.Y);

        _isUpdatingPosition = true;
        XPos.Text = Position.X.ToString();
        YPos.Text = Position.Y.ToString();
        _isUpdatingPosition = false;
    }

    // Position textbox handlers
    private void OnXPosChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingPosition) return;
        _isUpdatingPosition = true;

        if (int.TryParse(new string(XPos.Text?.Where(char.IsDigit).ToArray()), out int x))
        {
            Position = new PixelPoint(x, Position.Y);
            LastPosition = new Vector2(Position.X, Position.Y);
        }

        _isUpdatingPosition = false;
    }

    private void OnYPosChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingPosition) return;
        _isUpdatingPosition = true;

        if (int.TryParse(new string(YPos.Text?.Where(char.IsDigit).ToArray()), out int y))
        {
            Position = new PixelPoint(Position.X, y);
            LastPosition = new Vector2(Position.X, Position.Y);
        }

        _isUpdatingPosition = false;
    }

    // Lock checkbox handlers
    private void OnXLockChanged(object? sender, RoutedEventArgs e)
    {
        XPos.IsEnabled = XLock.IsChecked != true;
    }

    private void OnYLockChanged(object? sender, RoutedEventArgs e)
    {
        YPos.IsEnabled = YLock.IsChecked != true;
    }

    // Toggle panel
    private void OnTogglePanelClick(object? sender, RoutedEventArgs e)
    {
        EditPanel.IsVisible = !EditPanel.IsVisible;
        TogglePanelButton.Content = EditPanel.IsVisible ? "▲" : "▼";
    }

    // Helper to convert SKBitmap to Avalonia Bitmap
    private static Bitmap ConvertToAvaloniaBitmap(SKBitmap skBitmap)
    {
        using var data = skBitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new System.IO.MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, System.IO.SeekOrigin.Begin);
        return new Bitmap(stream);
    }
}
