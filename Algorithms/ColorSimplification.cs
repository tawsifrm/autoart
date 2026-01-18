using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using SkiaSharp;

namespace AutoArt.Algorithms;

/// <summary>
/// Provides color simplification functionality to merge visually similar colors.
/// This reduces layer count while preserving meaningful color distinctions.
/// 
/// The algorithm uses perceptual color distance (Delta E) in LAB color space
/// to determine visual similarity between colors. Colors that are perceptually
/// similar are merged into a single layer with their centroid color.
/// </summary>
public class ColorSimplification
{
    /// <summary>
    /// Default threshold for merging colors. Colors with perceptual distance below this
    /// value are considered similar enough to merge.
    /// 
    /// Delta E reference (CIELAB):
    /// - 0-1: Not perceptible by human eyes
    /// - 1-2: Perceptible through close observation
    /// - 2-10: Perceptible at a glance
    /// - 11-49: Colors are more similar than opposite
    /// - 100: Colors are exact opposite
    /// 
    /// We use a very conservative threshold (2.5) to only merge colors that are
    /// nearly indistinguishable. This prevents the chaining problem where
    /// transitively similar colors get merged incorrectly.
    /// </summary>
    public const float DefaultMergeThreshold = 2.5f;

    /// <summary>
    /// Minimum ratio of colors to preserve after simplification.
    /// This prevents over-simplification even with high thresholds.
    /// Value of 0.5 means at least 50% of original colors will be kept.
    /// </summary>
    public const float MinColorPreservationRatio = 0.5f;

    /// <summary>
    /// Simplifies a set of colors by merging visually similar ones.
    /// Returns a mapping from original colors to their simplified (merged) colors.
    /// </summary>
    /// <param name="colors">The original set of colors from quantization.</param>
    /// <param name="threshold">The perceptual distance threshold for merging (lower = more conservative).</param>
    /// <param name="colorSpace">The color space to use for distance calculations.</param>
    /// <returns>A dictionary mapping each original color to its simplified color.</returns>
    public static Dictionary<Color, Color> SimplifyColors(
        IEnumerable<Color> colors,
        float threshold = DefaultMergeThreshold,
        LabHelper.ColorSpace colorSpace = LabHelper.ColorSpace.OKLAB)
    {
        var colorList = colors.ToList();
        var mapping = new Dictionary<Color, Color>();

        // If we have 0 or 1 colors, no simplification needed
        if (colorList.Count <= 1)
        {
            foreach (var color in colorList)
                mapping[color] = color;
            return mapping;
        }

        // Build color groups using a greedy clustering approach
        // Each group contains colors that should be merged together
        var colorGroups = BuildColorGroups(colorList, threshold, colorSpace);

        // For each group, compute the centroid color and map all members to it
        foreach (var group in colorGroups)
        {
            var centroid = ComputeCentroidColor(group, colorSpace);
            foreach (var color in group)
            {
                mapping[color] = centroid;
            }
        }

        return mapping;
    }

    /// <summary>
    /// Builds groups of similar colors using hierarchical agglomerative clustering.
    /// Colors within threshold distance are grouped together.
    /// </summary>
    /// <remarks>
    /// This uses a COMPLETE-LINKAGE approach: two clusters are merged only if ALL pairs
    /// of colors (one from each cluster) are within the threshold distance.
    /// This prevents the "chaining" problem where A merges with B, B with C, etc.
    /// resulting in drastically over-simplified output.
    /// 
    /// Additionally, we enforce a minimum color preservation ratio to prevent
    /// over-simplification even with aggressive thresholds.
    /// </remarks>
    private static List<List<Color>> BuildColorGroups(
        List<Color> colors,
        float threshold,
        LabHelper.ColorSpace colorSpace)
    {
        // Start with each color in its own group
        var groups = colors.Select(c => new List<Color> { c }).ToList();

        // Calculate minimum number of groups to preserve
        int minGroups = Math.Max(1, (int)Math.Ceiling(colors.Count * MinColorPreservationRatio));

        // Convert colors to LAB once for efficiency
        var labColors = new Dictionary<Color, float[]>();
        foreach (var color in colors)
        {
            labColors[color] = RgbToLab(color.R, color.G, color.B, colorSpace);
        }

        // Iteratively merge closest groups until no groups are within threshold
        // or we've reached the minimum group count
        bool merged;
        do
        {
            merged = false;

            // Stop if we've reached the minimum number of groups
            if (groups.Count <= minGroups)
                break;

            float minDistance = float.MaxValue;
            int mergeI = -1, mergeJ = -1;

            // Find the closest pair of groups using COMPLETE linkage
            for (int i = 0; i < groups.Count; i++)
            {
                for (int j = i + 1; j < groups.Count; j++)
                {
                    // Compute MAXIMUM distance between any pair of colors in the two groups
                    // (complete linkage - only merge if ALL colors are similar)
                    float distance = ComputeGroupDistanceComplete(groups[i], groups[j], labColors);

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        mergeI = i;
                        mergeJ = j;
                    }
                }
            }

            // If the closest groups are within threshold, merge them
            if (minDistance <= threshold && mergeI >= 0 && mergeJ >= 0)
            {
                groups[mergeI].AddRange(groups[mergeJ]);
                groups.RemoveAt(mergeJ);
                merged = true;
            }

        } while (merged && groups.Count > minGroups);

        return groups;
    }

    /// <summary>
    /// Computes the MAXIMUM distance between any pair of colors from two groups.
    /// Uses complete-linkage clustering to prevent chaining.
    /// Two groups are only considered similar if ALL their colors are similar.
    /// </summary>
    private static float ComputeGroupDistanceComplete(
        List<Color> group1,
        List<Color> group2,
        Dictionary<Color, float[]> labColors)
    {
        float maxDistance = 0f;

        foreach (var c1 in group1)
        {
            foreach (var c2 in group2)
            {
                float distance = ComputeLabDistance(labColors[c1], labColors[c2]);
                if (distance > maxDistance)
                    maxDistance = distance;
            }
        }

        return maxDistance;
    }

    /// <summary>
    /// Computes the minimum distance between any pair of colors from two groups.
    /// Uses single-linkage clustering (minimum distance).
    /// DEPRECATED: Kept for reference but not used due to chaining issues.
    /// </summary>
    private static float ComputeGroupDistance(
        List<Color> group1,
        List<Color> group2,
        Dictionary<Color, float[]> labColors)
    {
        float minDistance = float.MaxValue;

        foreach (var c1 in group1)
        {
            foreach (var c2 in group2)
            {
                float distance = ComputeLabDistance(labColors[c1], labColors[c2]);
                if (distance < minDistance)
                    minDistance = distance;
            }
        }

        return minDistance;
    }

    /// <summary>
    /// Computes the centroid (average) color for a group of colors.
    /// The averaging is done in LAB space for perceptual accuracy,
    /// then converted back to RGB.
    /// </summary>
    private static Color ComputeCentroidColor(List<Color> colors, LabHelper.ColorSpace colorSpace)
    {
        if (colors.Count == 0)
            return Colors.Black;

        if (colors.Count == 1)
            return colors[0];

        // Average in LAB space for perceptually meaningful centroid
        float sumL = 0, sumA = 0, sumB = 0;

        foreach (var color in colors)
        {
            var lab = RgbToLab(color.R, color.G, color.B, colorSpace);
            sumL += lab[0];
            sumA += lab[1];
            sumB += lab[2];
        }

        float avgL = sumL / colors.Count;
        float avgA = sumA / colors.Count;
        float avgB = sumB / colors.Count;

        // Convert back to RGB
        var rgb = LabToRgb(avgL, avgA, avgB, colorSpace);

        return Color.FromRgb(
            (byte)Math.Clamp(Math.Round(rgb[0]), 0, 255),
            (byte)Math.Clamp(Math.Round(rgb[1]), 0, 255),
            (byte)Math.Clamp(Math.Round(rgb[2]), 0, 255)
        );
    }

    /// <summary>
    /// Computes the perceptual distance (Delta E) between two LAB colors.
    /// Uses CIE76 formula (Euclidean distance in LAB space).
    /// </summary>
    /// <remarks>
    /// CIE76 is sufficient for our use case as we're making broad similarity
    /// decisions, not precise color matching. More advanced formulas (CIE94, CIEDE2000)
    /// could be used for finer control but add complexity.
    /// </remarks>
    private static float ComputeLabDistance(float[] lab1, float[] lab2)
    {
        float dL = lab1[0] - lab2[0];
        float dA = lab1[1] - lab2[1];
        float dB = lab1[2] - lab2[2];

        return MathF.Sqrt(dL * dL + dA * dA + dB * dB);
    }

    /// <summary>
    /// Converts RGB to LAB using the specified color space.
    /// </summary>
    private static float[] RgbToLab(byte r, byte g, byte b, LabHelper.ColorSpace colorSpace)
    {
        // Temporarily set the color space and use LabHelper
        var originalColorSpace = LabHelper.colorSpace;
        LabHelper.colorSpace = colorSpace;

        var result = LabHelper.RgbToLab(r, g, b);

        LabHelper.colorSpace = originalColorSpace;
        return result;
    }

    /// <summary>
    /// Converts LAB to RGB using the specified color space.
    /// </summary>
    private static float[] LabToRgb(float l, float a, float b, LabHelper.ColorSpace colorSpace)
    {
        var originalColorSpace = LabHelper.colorSpace;
        LabHelper.colorSpace = colorSpace;

        var result = LabHelper.LabToRgb(l, a, b);

        LabHelper.colorSpace = originalColorSpace;
        return result;
    }

    /// <summary>
    /// Applies color simplification to a quantized bitmap and color dictionary.
    /// This is the main entry point for the simplified split mode.
    /// </summary>
    /// <param name="quantizedBitmap">The quantized bitmap from the initial split.</param>
    /// <param name="colorDictionary">The color dictionary from the initial split.</param>
    /// <param name="threshold">The merge threshold for perceptual similarity.</param>
    /// <param name="colorSpace">The color space to use for distance calculations.</param>
    /// <returns>
    /// A tuple containing:
    /// - The simplified bitmap with merged colors applied
    /// - The new color dictionary with only the simplified colors
    /// </returns>
    public static unsafe (SKBitmap simplifiedBitmap, Dictionary<Color, int> simplifiedColorDict)
        SimplifyQuantizedImage(
            SKBitmap quantizedBitmap,
            Dictionary<Color, int> colorDictionary,
            float threshold = DefaultMergeThreshold,
            LabHelper.ColorSpace colorSpace = LabHelper.ColorSpace.OKLAB)
    {
        // Get the color mapping (original -> simplified)
        var colorMapping = SimplifyColors(colorDictionary.Keys, threshold, colorSpace);

        // Create the simplified color dictionary
        // Multiple original colors may map to the same simplified color
        var simplifiedDict = new Dictionary<Color, int>();
        foreach (var kvp in colorMapping)
        {
            var simplifiedColor = kvp.Value;
            if (!simplifiedDict.ContainsKey(simplifiedColor))
            {
                // Assign a new index based on order of first appearance
                simplifiedDict[simplifiedColor] = simplifiedDict.Count;
            }
        }

        // If no colors were merged, return original data
        if (simplifiedDict.Count == colorDictionary.Count)
        {
            return (quantizedBitmap.Copy(), new Dictionary<Color, int>(colorDictionary));
        }

        // Create a new bitmap with the simplified colors
        var simplifiedBitmap = new SKBitmap(quantizedBitmap.Width, quantizedBitmap.Height);

        var srcPtr = (byte*)quantizedBitmap.GetPixels().ToPointer();
        var dstPtr = (byte*)simplifiedBitmap.GetPixels().ToPointer();

        int width = quantizedBitmap.Width;
        int height = quantizedBitmap.Height;

        // Build a lookup for fast color replacement
        // Key: original color as packed int (ARGB), Value: simplified color as packed int
        var colorLookup = new Dictionary<uint, uint>();
        foreach (var kvp in colorMapping)
        {
            var original = kvp.Key;
            var simplified = kvp.Value;

            uint originalPacked = ((uint)original.A << 24) | ((uint)original.R << 16) | ((uint)original.G << 8) | original.B;
            uint simplifiedPacked = ((uint)simplified.A << 24) | ((uint)simplified.R << 16) | ((uint)simplified.G << 8) | simplified.B;

            colorLookup[originalPacked] = simplifiedPacked;
        }

        // Process each pixel
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                // Read BGRA from source
                byte b = *srcPtr++;
                byte g = *srcPtr++;
                byte r = *srcPtr++;
                byte a = *srcPtr++;

                // Skip fully transparent pixels
                if (a == 0)
                {
                    *dstPtr++ = 0;
                    *dstPtr++ = 0;
                    *dstPtr++ = 0;
                    *dstPtr++ = 0;
                    continue;
                }

                // Look up the simplified color
                uint originalPacked = ((uint)255 << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

                if (colorLookup.TryGetValue(originalPacked, out uint simplifiedPacked))
                {
                    // Extract simplified color components
                    byte newR = (byte)((simplifiedPacked >> 16) & 0xFF);
                    byte newG = (byte)((simplifiedPacked >> 8) & 0xFF);
                    byte newB = (byte)(simplifiedPacked & 0xFF);

                    *dstPtr++ = newB;
                    *dstPtr++ = newG;
                    *dstPtr++ = newR;
                    *dstPtr++ = a;
                }
                else
                {
                    // Color not in mapping (shouldn't happen, but handle gracefully)
                    *dstPtr++ = b;
                    *dstPtr++ = g;
                    *dstPtr++ = r;
                    *dstPtr++ = a;
                }
            }
        }

        return (simplifiedBitmap, simplifiedDict);
    }
}
