using System;
using System.Collections.Generic;
using AutoArt.Algorithms;
using AutoArt.Models;
using SkiaSharp;
using Avalonia.Media;

namespace AutoArt.Services;

/// <summary>
/// Service for color quantization and layer extraction.
/// </summary>
public class ColorSplittingService
{
    private SKBitmap? _quantizedBitmap;
    private Dictionary<Color, int>? _colorDictionary;
    
    /// <summary>
    /// Performs color quantization on the input image.
    /// </summary>
    /// <param name="sourceBitmap">The source image to quantize.</param>
    /// <param name="config">The splitting configuration.</param>
    /// <returns>A tuple containing the quantized bitmap and the list of color layers.</returns>
    public (SKBitmap quantizedBitmap, List<ColorLayer> layers) SplitImage(SKBitmap sourceBitmap, SplitConfiguration config)
    {
        // Configure ImageSplitting settings
        ImageSplitting.Colors = (byte)Math.Min(255, Math.Max(1, config.ColorCount));
        ImageSplitting.RemoveStrayPixels = config.RemoveStrayPixels;
        
        // Set color space for LAB-based algorithms
        if (config.UseLab)
        {
            LabHelper.colorSpace = config.ColorSpace;
        }
        
        // Perform quantization
        (_quantizedBitmap, _colorDictionary) = ImageSplitting.ColorQuantize(
            sourceBitmap,
            config.Algorithm,
            config.Iterations,
            config.Initializer,
            config.UseLab
        );
        
        // Extract layers
        var layers = ExtractLayers();
        
        return (_quantizedBitmap.Copy(), layers);
    }
    
    /// <summary>
    /// Extracts individual color layers from the quantized image.
    /// </summary>
    /// <returns>List of ColorLayer objects.</returns>
    private List<ColorLayer> ExtractLayers()
    {
        var layers = new List<ColorLayer>();
        
        if (_colorDictionary == null || _quantizedBitmap == null)
            return layers;
        
        // Get the full-resolution layers
        var layerDict = ImageSplitting.GetLayers(false);
        
        int index = 0;
        foreach (var (bitmap, hexColor) in layerDict)
        {
            layers.Add(new ColorLayer(index, bitmap, hexColor));
            index++;
        }
        
        return layers;
    }
    
    /// <summary>
    /// Gets preview layers (64x64) for UI display.
    /// </summary>
    /// <returns>Dictionary of preview bitmaps and hex colors.</returns>
    public Dictionary<SKBitmap, string> GetPreviewLayers()
    {
        return ImageSplitting.GetLayers(true);
    }
}
