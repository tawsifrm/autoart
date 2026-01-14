using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AutoArt.Models;

namespace AutoArt.Views;

public partial class DrawingGuideWindow : Window
{
    // Events for layer navigation and session control
    public event EventHandler? PreviousLayerRequested;
    public event EventHandler? NextLayerRequested;
    public event EventHandler? StopRequested;

    // Track current layer position for enabling/disabling navigation buttons
    private int _currentLayerIndex = 0;
    private int _totalLayers = 0;

    // Store current hex code for clipboard copy (without # prefix)
    private string _currentHexCode = "";

    private bool _isDragging = false;
    private Point _dragStartPoint;

    public DrawingGuideWindow()
    {
        InitializeComponent();

        // Button handlers for layer navigation
        PreviousLayerButton.Click += (_, _) => PreviousLayerRequested?.Invoke(this, EventArgs.Empty);
        NextLayerButton.Click += (_, _) => NextLayerRequested?.Invoke(this, EventArgs.Empty);
        StopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        // Clipboard copy handler for hex code
        CopyHexButton.Click += OnCopyHexButtonClick;

        // Enable dragging the window
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    public void UpdateLayer(ColorLayer layer, int currentIndex, int totalLayers)
    {
        // Store layer position for navigation button state management
        _currentLayerIndex = currentIndex;
        _totalLayers = totalLayers;

        // Store hex code without # for clipboard copy
        _currentHexCode = layer.HexColor;

        // Update layer display info
        LayerText.Text = $"Layer {currentIndex} of {totalLayers}";
        ColorHexText.Text = $"#{layer.HexColor}";
        ColorSwatch.Background = new SolidColorBrush(layer.Color);

        InstructionText.Text = "Select this color in your drawing app, then press SHIFT when ready.";
        DrawingProgress.IsVisible = false;

        // Update navigation button enabled states based on current position
        UpdateNavigationButtonStates();
    }

    /// <summary>
    /// Updates the enabled/disabled state of Previous and Next layer buttons
    /// based on the current layer position within the total layers.
    /// </summary>
    private void UpdateNavigationButtonStates()
    {
        // Disable Previous button on first layer (index 1 = first layer)
        PreviousLayerButton.IsEnabled = _currentLayerIndex > 1;

        // Disable Next button on last layer
        NextLayerButton.IsEnabled = _currentLayerIndex < _totalLayers;
    }

    public void SetDrawingState(bool isDrawing)
    {
        if (isDrawing)
        {
            InstructionText.Text = "Drawing in progress... Press ALT to stop.";
            DrawingProgress.IsVisible = true;

            // Disable navigation buttons while drawing is in progress
            PreviousLayerButton.IsEnabled = false;
            NextLayerButton.IsEnabled = false;
        }
        else
        {
            InstructionText.Text = "Layer complete! Preparing next layer...";
            DrawingProgress.IsVisible = false;

            // Re-enable navigation buttons based on current position
            UpdateNavigationButtonStates();
        }
    }

    /// <summary>
    /// Copies the current hex code (without #) to the clipboard.
    /// </summary>
    private async void OnCopyHexButtonClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard != null && !string.IsNullOrEmpty(_currentHexCode))
        {
            await Clipboard.SetTextAsync(_currentHexCode);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(this);
            e.Pointer.Capture(this);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging)
        {
            var currentPoint = e.GetPosition(this);
            var offset = currentPoint - _dragStartPoint;
            Position = new PixelPoint(
                Position.X + (int)offset.X,
                Position.Y + (int)offset.Y
            );
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        e.Pointer.Capture(null);
    }
}
