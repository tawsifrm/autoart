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
    public event EventHandler? SkipRequested;
    public event EventHandler? StopRequested;
    
    private bool _isDragging = false;
    private Point _dragStartPoint;
    
    public DrawingGuideWindow()
    {
        InitializeComponent();
        
        // Button handlers
        SkipButton.Click += (_, _) => SkipRequested?.Invoke(this, EventArgs.Empty);
        StopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        
        // Enable dragging the window
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }
    
    public void UpdateLayer(ColorLayer layer, int currentIndex, int totalLayers)
    {
        LayerText.Text = $"Layer {currentIndex} of {totalLayers}";
        ColorHexText.Text = $"#{layer.HexColor}";
        ColorSwatch.Background = new SolidColorBrush(layer.Color);
        
        InstructionText.Text = "Select this color in your drawing app, then press SHIFT when ready.";
        DrawingProgress.IsVisible = false;
        
        SkipButton.IsEnabled = true;
    }
    
    public void SetDrawingState(bool isDrawing)
    {
        if (isDrawing)
        {
            InstructionText.Text = "Drawing in progress... Press ALT to stop.";
            DrawingProgress.IsVisible = true;
            SkipButton.IsEnabled = false;
        }
        else
        {
            InstructionText.Text = "Layer complete! Preparing next layer...";
            DrawingProgress.IsVisible = false;
            SkipButton.IsEnabled = true;
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
