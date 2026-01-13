using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AutoArt.Core;
using AutoArt.Models;
using AutoArt.Services;
using SharpHook;
using SharpHook.Native;
using SkiaSharp;

namespace AutoArt.Views;

public partial class MainWindow : Window
{
    // Services
    private readonly ColorSplittingService _colorSplittingService = new();
    private readonly DrawingService _drawingService = new();
    
    // State
    private readonly AppState _appState = new();
    private SplitConfiguration _splitConfig = new();
    
    // Image data
    private SKBitmap? _rawBitmap;
    private SKBitmap? _quantizedBitmap;
    private Bitmap? _displayedBitmap;
    
    // Drawing session
    private DrawingGuideWindow? _guideWindow;
    private LayerPreview? _layerPreview;
    private bool _sessionActive = false;
    private int _currentScale = 100;
    private Vector2 _lastPreviewPosition;
    private bool _drawingHalted = false;
    
    // Helpers
    private readonly Regex _numberRegex = new(@"[^0-9]");
    private bool _isUpdatingScale = false;

    public MainWindow()
    {
        InitializeComponent();
        
        if (Design.IsDesignMode) return;
        
        // Initialize drawing service
        _drawingService.Initialize();
        _drawingService.LayerDrawingComplete += OnLayerDrawingComplete;
        
        // Toolbar buttons
        CloseAppButton.Click += (_, _) => Close();
        MinimizeAppButton.Click += (_, _) => WindowState = WindowState.Minimized;
        
        // Import/Split buttons
        ImportButton.Click += OnImportButtonClick;
        SplitButton.Click += OnSplitButtonClick;
        StartDrawingButton.Click += OnStartDrawingClick;
        
        // Session controls
        SkipLayerButton.Click += OnSkipLayerClick;
        StopSessionButton.Click += OnStopSessionClick;
        
        // Input listeners
        ColorsInput.TextChanged += (_, _) => ValidateInputs();
        IterationsInput.TextChanged += (_, _) => ValidateInputs();
        
        // Scale controls
        ScaleSlider.ValueChanged += OnScaleSliderChanged;
        ScaleInput.TextChanging += OnScaleInputChanging;
        
        // Cleanup on close
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _drawingService.Shutdown();
        _appState.Reset();
        _rawBitmap?.Dispose();
        _quantizedBitmap?.Dispose();
        _displayedBitmap?.Dispose();
        _guideWindow?.Close();
        _layerPreview?.Close();
    }

    private void UpdateStatus(string message)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            StatusText.Text = message;
        });
    }

    private void ValidateInputs()
    {
        // Validate color count
        if (!int.TryParse(ColorsInput.Text, out int colors) || colors < 1 || colors > 255)
        {
            ColorsInput.Text = Math.Clamp(colors, 1, 255).ToString();
        }
        
        // Validate iterations
        if (!int.TryParse(IterationsInput.Text, out int iterations) || iterations < 1 || iterations > 100)
        {
            IterationsInput.Text = Math.Clamp(iterations, 1, 100).ToString();
        }
    }
    
    #region Scale Control
    
    private void OnScaleSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingScale) return;
        _isUpdatingScale = true;
        
        _currentScale = (int)ScaleSlider.Value;
        ScaleInput.Text = $"{_currentScale}%";
        
        _isUpdatingScale = false;
    }
    
    private void OnScaleInputChanging(object? sender, TextChangingEventArgs e)
    {
        if (_isUpdatingScale) return;
        if (ScaleInput.Text == null) return;
        
        _isUpdatingScale = true;
        
        var numberText = _numberRegex.Replace(ScaleInput.Text, "");
        ScaleInput.Text = numberText + "%";
        e.Handled = true;
        
        if (int.TryParse(numberText, out int value) && value >= 1 && value <= 200)
        {
            _currentScale = value;
            ScaleSlider.Value = value;
        }
        
        _isUpdatingScale = false;
    }
    
    #endregion

    private SplitConfiguration GetCurrentConfig()
    {
        int.TryParse(ColorsInput.Text, out int colors);
        int.TryParse(IterationsInput.Text, out int iterations);
        
        return new SplitConfiguration
        {
            ColorCount = Math.Clamp(colors, 1, 255),
            Mode = ModeSelector.SelectedIndex,
            Initializer = InitializerSelector.SelectedIndex,
            Iterations = Math.Clamp(iterations, 1, 100),
            RemoveStrayPixels = RemoveStrayCheckbox.IsChecked == true
        };
    }

    #region Image Import
    
    private async void OnImportButtonClick(object? sender, RoutedEventArgs e)
    {
        var fileTypes = new FilePickerFileType("Image files")
        {
            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp" },
            MimeTypes = new[] { "image/*" }
        };
        
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select an image",
            FileTypeFilter = new[] { fileTypes },
            AllowMultiple = false
        });
        
        if (files.Count != 1) return;
        
        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;
        
        ImportImage(path);
    }
    
    private void ImportImage(string path)
    {
        try
        {
            // Dispose old bitmaps
            _rawBitmap?.Dispose();
            _quantizedBitmap?.Dispose();
            _displayedBitmap?.Dispose();
            _appState.Reset();
            
            // Load new image
            _rawBitmap = SKBitmap.Decode(path);
            if (_rawBitmap == null)
            {
                UpdateStatus("Failed to load image");
                return;
            }
            
            // Normalize color format
            _rawBitmap = NormalizeColor(_rawBitmap);
            
            // Display the image
            _displayedBitmap = ConvertToAvaloniaBitmap(_rawBitmap);
            ImagePreview.Image = _displayedBitmap;
            
            // Hide empty state overlay
            var emptyStateOverlay = this.FindControl<Border>("EmptyStateOverlay");
            if (emptyStateOverlay != null)
            {
                emptyStateOverlay.IsVisible = false;
            }
            
            // Update state
            _appState.CurrentState = AppStateType.Configuring;
            SplitButton.IsEnabled = true;
            
            UpdateStatus($"Image loaded: {_rawBitmap.Width}x{_rawBitmap.Height}");
            
            // Clear previous layers
            ClearColorLayerPreviews();
            LayerCountText.Text = "(0)";
            StartDrawingButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            UpdateStatus($"Error loading image: {ex.Message}");
        }
    }
    
    #endregion
    
    #region Color Splitting
    
    private async void OnSplitButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_rawBitmap == null) return;
        
        _appState.CurrentState = AppStateType.Splitting;
        SplitButton.IsEnabled = false;
        UpdateStatus("Splitting colors...");
        
        _splitConfig = GetCurrentConfig();
        
        try
        {
            // Run splitting on background thread
            await Task.Run(() =>
            {
                var (quantized, layers) = _colorSplittingService.SplitImage(_rawBitmap, _splitConfig);
                
                Dispatcher.UIThread.Invoke(() =>
                {
                    _quantizedBitmap?.Dispose();
                    _quantizedBitmap = quantized;
                    
                    // Update app state with layers
                    _appState.Layers.Clear();
                    _appState.Layers.AddRange(layers);
                    _appState.TotalLayers = layers.Count;
                    _appState.CurrentLayerIndex = 0;
                    _appState.CurrentState = AppStateType.Ready;
                    
                    // Display quantized image
                    _displayedBitmap?.Dispose();
                    _displayedBitmap = ConvertToAvaloniaBitmap(_quantizedBitmap);
                    ImagePreview.Image = _displayedBitmap;
                    
                    // Update UI
                    UpdateColorLayerPreviews();
                    LayerCountText.Text = $"{layers.Count} layers";
                    SplitButton.IsEnabled = true;
                    StartDrawingButton.IsEnabled = true;
                    
                    UpdateStatus($"Split into {layers.Count} colors");
                });
            });
        }
        catch (Exception ex)
        {
            UpdateStatus($"Error splitting: {ex.Message}");
            SplitButton.IsEnabled = true;
            _appState.CurrentState = AppStateType.Configuring;
        }
    }
    
    private void ClearColorLayerPreviews()
    {
        ColorLayersPanel.Children.Clear();
    }
    
    private void UpdateColorLayerPreviews()
    {
        ClearColorLayerPreviews();
        
        foreach (var layer in _appState.Layers)
        {
            var swatch = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(layer.Color),
                BorderBrush = new SolidColorBrush(Color.Parse("#E5E7EB")),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(4),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            
            // Apply ColorSwatch style class for hover effects
            swatch.Classes.Add("ColorSwatch");
            
            // Show layer preview on click
            swatch.PointerPressed += (_, _) => ShowLayerPreview(layer);
            
            // Tooltip with hex color
            ToolTip.SetTip(swatch, $"#{layer.HexColor}");
            
            ColorLayersPanel.Children.Add(swatch);
        }
    }
    
    private void ShowLayerPreview(ColorLayer layer)
    {
        // Update image preview to show just this layer
        _displayedBitmap?.Dispose();
        _displayedBitmap = ConvertToAvaloniaBitmap(layer.Bitmap);
        ImagePreview.Image = _displayedBitmap;
        
        UpdateStatus($"Previewing layer {layer.Index + 1}: #{layer.HexColor}");
    }
    
    #endregion
    
    #region Drawing Session
    
    private void OnStartDrawingClick(object? sender, RoutedEventArgs e)
    {
        if (_appState.Layers.Count == 0) return;
        
        StartDrawingSession();
    }
    
    private void StartDrawingSession()
    {
        _sessionActive = true;
        _appState.CurrentLayerIndex = 0;
        _appState.CurrentState = AppStateType.WaitingForUser;
        
        // Configure drawing
        _drawingService.ConfigureDrawing(
            interval: 10000,
            clickDelay: 1000,
            algorithm: (byte)AlgorithmSelector.SelectedIndex
        );
        
        // Show current layer panel
        CurrentLayerPanel.IsVisible = true;
        StartDrawingButton.IsEnabled = false;
        SplitButton.IsEnabled = false;
        ImportButton.IsEnabled = false;
        
        // DON'T use the global key hook here - the LayerPreview handles it
        // Update UI to show current layer
        UpdateCurrentLayerDisplay();
        
        // Open the layer preview overlay (repositionable) and guide window
        OpenLayerPreview();
        OpenGuideWindow();
        WindowState = WindowState.Minimized;
    }
    
    private void OpenLayerPreview()
    {
        _layerPreview?.Close();
        _layerPreview = new LayerPreview();
        _layerPreview.DrawRequested += OnLayerPreviewDrawRequested;
        _layerPreview.CancelRequested += OnLayerPreviewCancelRequested;
        
        var layer = _appState.CurrentLayer;
        if (layer != null)
        {
            // Use last position if available, otherwise null for center of screen
            Vector2? initialPos = _lastPreviewPosition != Vector2.Zero ? _lastPreviewPosition : null;
            _layerPreview.ShowLayer(layer, _appState.CurrentLayerIndex + 1, _appState.TotalLayers, _currentScale, _drawingService.Hook, initialPos);
        }
    }
    
    private void UpdateLayerPreview()
    {
        var layer = _appState.CurrentLayer;
        if (layer == null || _layerPreview == null) return;
        
        // Save the current position before updating
        _lastPreviewPosition = _layerPreview.LastPosition;
        
        // Update the preview with the new layer
        _layerPreview.UpdateLayer(layer, _appState.CurrentLayerIndex + 1, _appState.TotalLayers, _currentScale);
    }
    
    private void OnLayerPreviewDrawRequested(object? sender, EventArgs e)
    {
        if (_appState.CurrentState == AppStateType.WaitingForUser)
        {
            // Save position from preview
            if (_layerPreview != null)
            {
                _lastPreviewPosition = _layerPreview.LastPosition;
            }
            DrawCurrentLayer();
        }
    }
    
    private void OnLayerPreviewCancelRequested(object? sender, EventArgs e)
    {
        StopDrawingSession();
    }
    
    private void OpenGuideWindow()
    {
        _guideWindow?.Close();
        _guideWindow = new DrawingGuideWindow();
        _guideWindow.SkipRequested += OnGuideSkipRequested;
        _guideWindow.StopRequested += OnGuideStopRequested;
        
        if (_appState.CurrentLayer != null)
        {
            _guideWindow.UpdateLayer(_appState.CurrentLayer, _appState.CurrentLayerIndex + 1, _appState.TotalLayers);
        }
        
        _guideWindow.Show();
    }
    
    private void UpdateCurrentLayerDisplay()
    {
        var layer = _appState.CurrentLayer;
        if (layer == null) return;
        
        CurrentLayerText.Text = $"Layer {_appState.CurrentLayerIndex + 1} of {_appState.TotalLayers}";
        CurrentColorHex.Text = $"#{layer.HexColor}";
        CurrentColorSwatch.Background = new SolidColorBrush(layer.Color);
        
        InstructionText.Text = _appState.CurrentState == AppStateType.Drawing 
            ? "Drawing in progress..." 
            : "Select this color in your drawing app, then press SHIFT when ready.";
        
        // Update preview
        _displayedBitmap?.Dispose();
        _displayedBitmap = ConvertToAvaloniaBitmap(layer.Bitmap);
        ImagePreview.Image = _displayedBitmap;
        
        // Update guide window if open
        _guideWindow?.UpdateLayer(layer, _appState.CurrentLayerIndex + 1, _appState.TotalLayers);
        
        UpdateStatus($"Waiting for layer {_appState.CurrentLayerIndex + 1}/{_appState.TotalLayers}: #{layer.HexColor}");
    }
    
    private void OnGlobalKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (!_sessionActive) return;
        
        if (e.Data.KeyCode == _drawingService.StartDrawingKey)
        {
            if (_appState.CurrentState == AppStateType.WaitingForUser)
            {
                DrawCurrentLayer();
            }
        }
        else if (e.Data.KeyCode == _drawingService.StopDrawingKey)
        {
            StopDrawingSession();
        }
    }
    
    private async void DrawCurrentLayer()
    {
        var layer = _appState.CurrentLayer;
        if (layer == null) return;
        
        _appState.CurrentState = AppStateType.Drawing;
        _drawingHalted = false;
        
        // Get position and scaled bitmap from the preview before closing it
        var position = _lastPreviewPosition;
        SKBitmap? scaledBitmap = null;
        
        if (_layerPreview != null)
        {
            position = _layerPreview.LastPosition;
            scaledBitmap = _layerPreview.GetScaledBitmap();
            
            // Close the preview overlay during drawing
            _layerPreview.Close();
            _layerPreview = null;
        }
        
        Dispatcher.UIThread.Invoke(() =>
        {
            InstructionText.Text = "Drawing in progress... (Press Alt to stop)";
            _guideWindow?.SetDrawingState(true);
        });
        
        // Disable the built-in popup
        AutoArt.Core.Drawing.ShowPopup = false;
        
        // Set up Alt key listener to stop drawing
        void OnKeyReleasedDuringDraw(object? s, KeyboardHookEventArgs args)
        {
            if (args.Data.KeyCode == Config.Keybind_StopDrawing)
            {
                _drawingHalted = true;
                AutoArt.Core.Drawing.Halt();
            }
        }
        
        if (_drawingService.Hook != null)
        {
            _drawingService.Hook.KeyReleased += OnKeyReleasedDuringDraw;
        }
        
        try
        {
            // Draw the layer using the scaled bitmap if available
            if (scaledBitmap != null)
            {
                // Process the scaled bitmap for drawing
                var tempLayer = new ColorLayer(layer.Index, scaledBitmap, layer.HexColor);
                var processedBitmap = _drawingService.ProcessLayerForDrawing(tempLayer);
                
                await AutoArt.Core.Drawing.Draw(processedBitmap, position);
            }
            else
            {
                // Fallback to the layer's original bitmap
                await _drawingService.DrawLayerAsync(layer, position);
            }
        }
        finally
        {
            // Remove the keyboard hook
            if (_drawingService.Hook != null)
            {
                _drawingService.Hook.KeyReleased -= OnKeyReleasedDuringDraw;
            }
        }
        
        // Check if drawing was halted by Alt key
        if (_drawingHalted)
        {
            // Drawing was cancelled for this layer only - reopen preview for same layer
            Dispatcher.UIThread.Invoke(() =>
            {
                _appState.CurrentState = AppStateType.WaitingForUser;
                _guideWindow?.SetDrawingState(false);
                UpdateCurrentLayerDisplay();
                
                // Reopen the layer preview for the same layer (user can retry or reposition)
                OpenLayerPreview();
                
                UpdateStatus($"Layer {_appState.CurrentLayerIndex + 1} drawing cancelled. Reposition and press SHIFT to retry.");
            });
            return;
        }
        
        // Check if session is still active
        if (!_sessionActive)
        {
            return;
        }
        
        // Drawing completed normally - mark layer and advance
        layer.IsDrawn = true;
        OnLayerDrawingComplete(this, EventArgs.Empty);
    }
    
    private void OnLayerDrawingComplete(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            _guideWindow?.SetDrawingState(false);
            
            // Advance to next layer
            if (_appState.AdvanceToNextLayer())
            {
                UpdateCurrentLayerDisplay();
                
                // Reopen the layer preview for the next layer
                OpenLayerPreview();
            }
            else
            {
                // All done
                FinishDrawingSession();
            }
        });
    }
    
    private void OnSkipLayerClick(object? sender, RoutedEventArgs e)
    {
        SkipCurrentLayer();
    }
    
    private void OnGuideSkipRequested(object? sender, EventArgs e)
    {
        SkipCurrentLayer();
    }
    
    private void SkipCurrentLayer()
    {
        // Save position before advancing
        if (_layerPreview != null)
        {
            _lastPreviewPosition = _layerPreview.LastPosition;
        }
        
        if (_appState.AdvanceToNextLayer())
        {
            UpdateCurrentLayerDisplay();
            UpdateLayerPreview();
        }
        else
        {
            FinishDrawingSession();
        }
    }
    
    private void OnStopSessionClick(object? sender, RoutedEventArgs e)
    {
        StopDrawingSession();
    }
    
    private void OnGuideStopRequested(object? sender, EventArgs e)
    {
        StopDrawingSession();
    }
    
    private void StopDrawingSession()
    {
        _drawingService.StopDrawing();
        
        Dispatcher.UIThread.Invoke(() =>
        {
            _sessionActive = false;
            
            // Close preview overlay
            _layerPreview?.Close();
            _layerPreview = null;
            
            _guideWindow?.Close();
            _guideWindow = null;
            
            CurrentLayerPanel.IsVisible = false;
            StartDrawingButton.IsEnabled = true;
            SplitButton.IsEnabled = true;
            ImportButton.IsEnabled = true;
            
            _appState.CurrentState = AppStateType.Ready;
            
            // Restore main window
            WindowState = WindowState.Normal;
            Activate();
            
            UpdateStatus("Drawing session stopped");
        });
    }
    
    private void FinishDrawingSession()
    {
        _sessionActive = false;
        
        // Close preview overlay
        _layerPreview?.Close();
        _layerPreview = null;
        
        _guideWindow?.Close();
        _guideWindow = null;
        
        CurrentLayerPanel.IsVisible = false;
        StartDrawingButton.IsEnabled = true;
        SplitButton.IsEnabled = true;
        ImportButton.IsEnabled = true;
        
        _appState.CurrentState = AppStateType.Finished;
        
        // Restore main window
        WindowState = WindowState.Normal;
        Activate();
        
        // Update status - no popup needed
        var layersDrawn = _appState.Layers.Count(l => l.IsDrawn);
        UpdateStatus($"Drawing complete! {layersDrawn}/{_appState.TotalLayers} layers drawn.");
    }
    
    #endregion
    
    #region Image Utilities
    
    private static Bitmap ConvertToAvaloniaBitmap(SKBitmap bitmap)
    {
        using var encodedStream = new System.IO.MemoryStream();
        bitmap.Encode(encodedStream, SKEncodedImageFormat.Png, 100);
        encodedStream.Seek(0, System.IO.SeekOrigin.Begin);
        return new Bitmap(encodedStream);
    }
    
    private static unsafe SKBitmap NormalizeColor(SKBitmap sourceBitmap)
    {
        var srcColor = sourceBitmap.ColorType;
        
        if (srcColor == SKColorType.Bgra8888) return sourceBitmap;
        
        var outputBitmap = new SKBitmap(sourceBitmap.Width, sourceBitmap.Height);
        
        var srcPtr = (byte*)sourceBitmap.GetPixels().ToPointer();
        var dstPtr = (byte*)outputBitmap.GetPixels().ToPointer();
        
        var width = outputBitmap.Width;
        var height = outputBitmap.Height;
        
        for (var row = 0; row < height; row++)
        for (var col = 0; col < width; col++)
        {
            if (srcColor == SKColorType.Gray8 || srcColor == SKColorType.Alpha8)
            {
                var b = *srcPtr++;
                *dstPtr++ = b;
                *dstPtr++ = b;
                *dstPtr++ = b;
                *dstPtr++ = 255;
            }
            else if (srcColor == SKColorType.Rgba8888)
            {
                var r = *srcPtr++;
                var g = *srcPtr++;
                var b = *srcPtr++;
                var a = *srcPtr++;
                *dstPtr++ = b;
                *dstPtr++ = g;
                *dstPtr++ = r;
                *dstPtr++ = a;
            }
            else if (srcColor == SKColorType.Argb4444)
            {
                var r = *srcPtr++;
                var g = *srcPtr++;
                var b = *srcPtr++;
                var a = *srcPtr++;
                *dstPtr++ = (byte)(b * 2);
                *dstPtr++ = (byte)(g * 2);
                *dstPtr++ = (byte)(r * 2);
                *dstPtr++ = a;
            }
        }
        
        sourceBitmap.Dispose();
        return outputBitmap;
    }
    
    #endregion
}
