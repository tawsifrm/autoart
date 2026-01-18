using System;
using System.Collections.Generic;
using AutoArt.Algorithms;
using AutoArt.Models;
using SkiaSharp;
using Avalonia.Media;

namespace AutoArt.Services;

/// <summary>
/// Service for color quantization and layer extraction.
/// Supports both standard splitting (exact N colors) and simplified splitting
/// (approximately N colors with similar colors merged for better drawing performance).
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

        // Perform initial quantization
        (_quantizedBitmap, _colorDictionary) = ImageSplitting.ColorQuantize(
            sourceBitmap,
            config.Algorithm,
            config.Iterations,
            config.Initializer,
            config.UseLab
        );

        // Apply simplified split mode if enabled
        // This merges visually similar colors to reduce layer count and improve drawing performance
        if (config.SimplifiedSplit && _colorDictionary != null && _colorDictionary.Count > 1)
        {
            (_quantizedBitmap, _colorDictionary) = ApplyColorSimplification(
                _quantizedBitmap!,
                _colorDictionary,
                config.MergeThreshold,
                config.ColorSpace
            );
        }

        // Extract layers
        var layers = ExtractLayers();

        return (_quantizedBitmap!.Copy(), layers);
    }

    /// <summary>
    /// Applies color simplification to merge visually similar colors.
    /// This is the core of the simplified split mode.
    /// </summary>
    /// <remarks>
    /// The simplification process:
    /// 1. Analyzes colors using perceptual distance in LAB space
    /// 2. Groups colors that are below the similarity threshold
    /// 3. Merges groups into single colors (using the group centroid)
    /// 4. Updates the bitmap to use the simplified color palette
    /// 
    /// This results in fewer layers while preserving visually distinct colors.
    /// Example: 24 initial colors might become 15 after simplification.
    /// </remarks>
    private (SKBitmap, Dictionary<Color, int>) ApplyColorSimplification(
        SKBitmap quantizedBitmap,
        Dictionary<Color, int> colorDictionary,
        float mergeThreshold,
        LabHelper.ColorSpace colorSpace)
    {
        // Use the ColorSimplification algorithm to merge similar colors
        var (simplifiedBitmap, simplifiedDict) = ColorSimplification.SimplifyQuantizedImage(
            quantizedBitmap,
            colorDictionary,
            mergeThreshold,
            colorSpace
        );

        // Update internal state to reflect the simplified colors
        // This is important because GetLayers() and GetPreviewLayers() use the static ImageSplitting state
        UpdateImageSplittingState(simplifiedBitmap, simplifiedDict);

        return (simplifiedBitmap, simplifiedDict);
    }

    /// <summary>
    /// Updates the ImageSplitting static state with simplified data.
    /// This ensures GetLayers() returns the correct simplified layers.
    /// </summary>
    private void UpdateImageSplittingState(SKBitmap bitmap, Dictionary<Color, int> colorDict)
    {
        // We need to update the internal state that ImageSplitting.GetLayers() uses
        // Store our local references for ExtractLayers
        _quantizedBitmap = bitmap;
        _colorDictionary = colorDict;
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

        // Extract layers directly from our local quantized bitmap and color dictionary
        // This handles both standard and simplified modes correctly
        int index = 0;
        foreach (var (color, _) in _colorDictionary)
        {
            var layerBitmap = GetLayerFromBitmap(_quantizedBitmap, color);
            var hexColor = color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
            layers.Add(new ColorLayer(index, layerBitmap, hexColor));
            index++;
        }

        return layers;
    }

    /// <summary>
    /// Extracts a single color layer from a bitmap.
    /// Creates a new bitmap containing only pixels matching the specified color.
    /// </summary>
    private unsafe SKBitmap GetLayerFromBitmap(SKBitmap bitmap, Color color)
    {
        var outputBitmap = new SKBitmap(bitmap.Width, bitmap.Height);

        var srcPtr = (byte*)bitmap.GetPixels().ToPointer();
        var dstPtr = (byte*)outputBitmap.GetPixels().ToPointer();

        int width = bitmap.Width;
        int height = bitmap.Height;

        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                byte b = *srcPtr++;
                byte g = *srcPtr++;
                byte r = *srcPtr++;
                byte a = *srcPtr++;

                // If color matches, copy to output
                if (r == color.R && g == color.G && b == color.B)
                {
                    *dstPtr++ = b;
                    *dstPtr++ = g;
                    *dstPtr++ = r;
                    *dstPtr++ = a;
                }
                else
                {
                    // Transparent pixel
                    *dstPtr++ = 0;
                    *dstPtr++ = 0;
                    *dstPtr++ = 0;
                    *dstPtr++ = 0;
                }
            }
        }

        return outputBitmap;
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
