using System;
using SkiaSharp;
using Avalonia.Media;

namespace AutoArt.Models;

/// <summary>
/// Represents a single color layer extracted from the original image.
/// </summary>
public class ColorLayer : IDisposable
{
    /// <summary>
    /// The index of this layer (0-based).
    /// </summary>
    public int Index { get; set; }
    
    /// <summary>
    /// The bitmap containing only pixels of this color.
    /// </summary>
    public SKBitmap Bitmap { get; set; }
    
    /// <summary>
    /// The hex color code (e.g., "FF5500").
    /// </summary>
    public string HexColor { get; set; }
    
    /// <summary>
    /// The Avalonia Color object for UI display.
    /// </summary>
    public Color Color { get; set; }
    
    /// <summary>
    /// Whether this layer has been drawn.
    /// </summary>
    public bool IsDrawn { get; set; } = false;
    
    public ColorLayer(int index, SKBitmap bitmap, string hexColor)
    {
        Index = index;
        Bitmap = bitmap;
        HexColor = hexColor;
        
        // Parse hex color to Avalonia Color
        if (hexColor.Length == 6)
        {
            byte r = Convert.ToByte(hexColor.Substring(0, 2), 16);
            byte g = Convert.ToByte(hexColor.Substring(2, 2), 16);
            byte b = Convert.ToByte(hexColor.Substring(4, 2), 16);
            Color = Color.FromRgb(r, g, b);
        }
        else
        {
            Color = Colors.Black;
        }
    }
    
    public void Dispose()
    {
        Bitmap?.Dispose();
    }
}
