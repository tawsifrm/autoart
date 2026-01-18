using AutoArt.Algorithms;

namespace AutoArt.Models;

/// <summary>
/// Configuration for color splitting operation.
/// </summary>
public class SplitConfiguration
{
    /// <summary>
    /// Number of colors to split the image into.
    /// </summary>
    public int ColorCount { get; set; } = 12;

    /// <summary>
    /// The algorithm mode to use.
    /// 0 = KMeans OKLab, 1 = KMeans CIELab, 2 = KMeans RGB,
    /// 3 = MedianCut OKLab, 4 = MedianCut CIELab, 5 = MedianCut RGB
    /// </summary>
    public int Mode { get; set; } = 0;

    /// <summary>
    /// Initializer algorithm (for KMeans).
    /// 0 = KMeans++, 1 = MedianCut
    /// </summary>
    public int Initializer { get; set; } = 0;

    /// <summary>
    /// Number of iterations for the algorithm.
    /// </summary>
    public int Iterations { get; set; } = 12;

    /// <summary>
    /// Whether to remove stray pixels after quantization.
    /// </summary>
    public bool RemoveStrayPixels { get; set; } = false;

    /// <summary>
    /// Whether to enable simplified split mode.
    /// When enabled, visually similar colors will be merged to reduce layer count.
    /// The specified ColorCount becomes a target rather than a guarantee.
    /// </summary>
    public bool SimplifiedSplit { get; set; } = false;

    /// <summary>
    /// The perceptual distance threshold for merging similar colors in simplified mode.
    /// Lower values are more conservative (fewer merges), higher values are more aggressive.
    /// Default is 2.5, which only merges colors that are nearly indistinguishable.
    /// </summary>
    public float MergeThreshold { get; set; } = 2.5f;

    /// <summary>
    /// Gets the algorithm type based on Mode.
    /// </summary>
    public ImageSplitting.Algorithm Algorithm => Mode switch
    {
        0 or 1 or 2 => ImageSplitting.Algorithm.KMeans,
        3 or 4 or 5 => ImageSplitting.Algorithm.MedianCut,
        _ => ImageSplitting.Algorithm.KMeans
    };

    /// <summary>
    /// Gets the color space to use based on Mode.
    /// </summary>
    public LabHelper.ColorSpace ColorSpace => Mode switch
    {
        0 or 3 => LabHelper.ColorSpace.OKLAB,
        1 or 4 => LabHelper.ColorSpace.CIELAB,
        _ => LabHelper.ColorSpace.OKLAB
    };

    /// <summary>
    /// Whether to use LAB color space (vs RGB).
    /// </summary>
    public bool UseLab => Mode != 2 && Mode != 5;
}
