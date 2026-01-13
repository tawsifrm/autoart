using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AutoArt.Core;

/// <summary>
/// Optimizes action sets (drawing paths) for improved per-layer rendering performance.
/// 
/// PERFORMANCE PROBLEM:
/// During auto-drawing, each layer is converted into "action sets" - contiguous paths that
/// the mouse follows while drawing. For certain layers (especially those with fine details,
/// stippling, or scattered pixels), this can result in thousands of tiny action sets.
/// 
/// Each action set has significant overhead:
/// - Mouse movement interpolation to reach the starting position
/// - Click delay before/after drawing
/// - Context switching between action sets
/// 
/// A layer with 1000 tiny 3-pixel action sets is MUCH slower than one with 100 larger
/// action sets, even if the total pixel count is similar.
/// 
/// SOLUTION:
/// This optimizer identifies clusters of small, spatially-close action sets and merges
/// them into larger composite sets. It uses connected component detection to avoid drawing
/// lines between disconnected pixels, and spatial ordering to minimize mouse travel.
/// 
/// TRADEOFFS:
/// - Merging may slightly change the exact drawing order within a cluster
/// - Very aggressive merging could create visible artifacts (configurable thresholds prevent this)
/// - The optimization pass itself has some CPU cost (but saves much more drawing time)
/// </summary>
public static class ActionSetOptimizer
{
    // =============================================================================================
    // CONFIGURABLE THRESHOLDS
    // These can be adjusted based on performance needs vs. visual fidelity requirements.
    // =============================================================================================

    /// <summary>
    /// Action sets with this many points or fewer are considered "small" and eligible for merging.
    /// Larger values = more aggressive optimization, but potentially more visible changes.
    /// Recommended range: 5-20
    /// </summary>
    public static int SmallActionSetThreshold = 10;

    /// <summary>
    /// Maximum distance (in pixels) between action set centroids for them to be clustered together.
    /// Smaller values = more conservative (only merge very close sets).
    /// Larger values = more aggressive (merge sets that are further apart).
    /// Recommended range: 10-50
    /// </summary>
    public static float ClusterDistanceThreshold = 25f;

    /// <summary>
    /// Minimum number of small action sets in a cluster before merging is applied.
    /// We don't merge isolated small sets - only clusters with significant overhead reduction.
    /// Recommended range: 3-10
    /// </summary>
    public static int MinClusterSizeForMerge = 3;

    /// <summary>
    /// When true, enables optimization. Can be toggled off for debugging or comparison.
    /// </summary>
    public static bool EnableOptimization = true;

    /// <summary>
    /// Maximum distance between consecutive points to be considered "connected" (adjacent).
    /// Points further apart than this will trigger a pen-up/pen-down (separate action set).
    /// Using sqrt(2) ≈ 1.42 allows diagonal adjacency (8-connected pixels).
    /// </summary>
    public static float MaxConnectedDistance = 1.5f;

    /// <summary>
    /// When true, sorts action sets spatially (top-to-bottom, left-to-right) to minimize
    /// mouse travel between action sets. When false, preserves original generation order.
    /// </summary>
    public static bool EnableSpatialOrdering = true;

    // =============================================================================================
    // PUBLIC API
    // =============================================================================================

    /// <summary>
    /// Optimizes a list of action sets by merging clusters of small, nearby sets.
    /// This reduces overhead from excessive action set switching during drawing.
    /// 
    /// The optimization performs two key improvements:
    /// 1. CLUSTERING: Groups small nearby action sets and merges them, reducing pen-up/pen-down overhead
    /// 2. SPATIAL ORDERING: Sorts action sets by position to minimize mouse travel between them
    /// 
    /// IMPORTANT: Merged clusters are split into connected components to avoid drawing
    /// lines between disconnected pixels. This preserves visual correctness.
    /// </summary>
    /// <param name="actions">Original list of action sets from GenerateActions</param>
    /// <returns>Optimized list with merged action sets where applicable</returns>
    public static List<List<Vector2>> OptimizeActionSets(List<List<Vector2>> actions)
    {
        if (!EnableOptimization || actions.Count == 0)
            return actions;

        // Step 1: Separate small and large action sets
        var smallSets = new List<(int originalIndex, List<Vector2> points, Vector2 centroid)>();
        var largeSets = new List<(int originalIndex, List<Vector2> points)>();

        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action.Count <= SmallActionSetThreshold)
            {
                smallSets.Add((i, action, CalculateCentroid(action)));
            }
            else
            {
                largeSets.Add((i, action));
            }
        }

        // If not enough small sets to benefit from optimization, return original
        // but still apply spatial ordering if enabled
        if (smallSets.Count < MinClusterSizeForMerge)
        {
            if (EnableSpatialOrdering)
            {
                return SortActionSetsSpatially(actions);
            }
            return actions;
        }

        // Step 2: Cluster small action sets by spatial proximity
        var clusters = ClusterByProximity(smallSets);

        // Step 3: Merge each qualifying cluster into connected sub-paths
        // CRITICAL: We split merged clusters into connected components to avoid
        // drawing lines between disconnected pixels (Issue 1 fix)
        var mergedFromClusters = new List<(float avgOriginalIndex, List<List<Vector2>> subPaths)>();
        var unmergedSmallSets = new List<(int originalIndex, List<Vector2> points)>();

        foreach (var cluster in clusters)
        {
            if (cluster.Count >= MinClusterSizeForMerge)
            {
                // Merge this cluster into connected sub-paths (not one continuous path)
                var subPaths = MergeClusterIntoConnectedSubPaths(cluster);
                float avgIndex = (float)cluster.Average(c => c.originalIndex);
                mergedFromClusters.Add((avgIndex, subPaths));
            }
            else
            {
                // Cluster too small to benefit from merging - keep original sets
                foreach (var item in cluster)
                {
                    unmergedSmallSets.Add((item.originalIndex, item.points));
                }
            }
        }

        // Step 4: Flatten all action sets with their centroids for spatial sorting
        var allSetsWithCentroids = new List<(Vector2 centroid, List<Vector2> points)>();

        foreach (var (_, points) in largeSets)
        {
            allSetsWithCentroids.Add((CalculateCentroid(points), points));
        }

        foreach (var (_, points) in unmergedSmallSets)
        {
            allSetsWithCentroids.Add((CalculateCentroid(points), points));
        }

        // Add all sub-paths from merged clusters
        foreach (var (_, subPaths) in mergedFromClusters)
        {
            foreach (var subPath in subPaths)
            {
                allSetsWithCentroids.Add((CalculateCentroid(subPath), subPath));
            }
        }

        // Step 5: Apply spatial ordering to minimize mouse travel (Issue 2 fix)
        // This sorts action sets so we traverse the canvas efficiently instead of jumping randomly
        List<List<Vector2>> result;
        if (EnableSpatialOrdering)
        {
            result = SortActionSetsSpatiallyWithCentroids(allSetsWithCentroids);
        }
        else
        {
            result = allSetsWithCentroids.Select(s => s.points).ToList();
        }

        // Log optimization results for debugging
        int originalCount = actions.Count;
        int optimizedCount = result.Count;
        int reduction = originalCount - optimizedCount;
        if (reduction > 0)
        {
            Utils.Log($"[ActionSetOptimizer] Reduced action sets from {originalCount} to {optimizedCount} " +
                      $"(-{reduction}, {100.0 * reduction / originalCount:F1}% reduction)");
        }
        else if (reduction < 0)
        {
            // This can happen when clusters are split into connected components
            Utils.Log($"[ActionSetOptimizer] Action sets changed from {originalCount} to {optimizedCount} " +
                      $"(+{-reduction} due to connected component splitting for visual correctness)");
        }

        return result;
    }

    // =============================================================================================
    // CLUSTERING LOGIC
    // =============================================================================================

    /// <summary>
    /// Clusters small action sets by spatial proximity using a greedy approach.
    /// Uses centroid distance to determine which sets belong together.
    /// </summary>
    private static List<List<(int originalIndex, List<Vector2> points, Vector2 centroid)>> ClusterByProximity(
        List<(int originalIndex, List<Vector2> points, Vector2 centroid)> smallSets)
    {
        var clusters = new List<List<(int originalIndex, List<Vector2> points, Vector2 centroid)>>();
        var assigned = new bool[smallSets.Count];

        for (int i = 0; i < smallSets.Count; i++)
        {
            if (assigned[i]) continue;

            // Start a new cluster with this set
            var cluster = new List<(int originalIndex, List<Vector2> points, Vector2 centroid)> { smallSets[i] };
            assigned[i] = true;

            // Find all nearby unassigned sets and add to cluster
            // Use iterative expansion to find transitively connected sets
            bool expanded;
            do
            {
                expanded = false;
                for (int j = 0; j < smallSets.Count; j++)
                {
                    if (assigned[j]) continue;

                    // Check if this set is close to any set already in the cluster
                    foreach (var clusterMember in cluster)
                    {
                        float distance = Vector2.Distance(clusterMember.centroid, smallSets[j].centroid);
                        if (distance <= ClusterDistanceThreshold)
                        {
                            cluster.Add(smallSets[j]);
                            assigned[j] = true;
                            expanded = true;
                            break;
                        }
                    }
                }
            } while (expanded);

            clusters.Add(cluster);
        }

        return clusters;
    }

    // =============================================================================================
    // CONNECTED COMPONENT MERGING (Issue 1 Fix)
    // =============================================================================================
    // 
    // PROBLEM: The original MergeClusterIntoActionSet created one continuous path through
    // all points, causing the pen to draw lines between disconnected pixels.
    //
    // SOLUTION: Instead of one path, we:
    // 1. Collect all points from the cluster
    // 2. Find connected components (groups of adjacent pixels)
    // 3. For each connected component, build a separate sub-path
    // 4. Return multiple sub-paths that can be drawn with pen-up between them
    //
    // This preserves visual correctness while still reducing action set count within
    // each connected region.
    // =============================================================================================

    /// <summary>
    /// Merges a cluster of small action sets into multiple connected sub-paths.
    /// Each sub-path contains only adjacent pixels, preventing lines between disconnected dots.
    /// </summary>
    private static List<List<Vector2>> MergeClusterIntoConnectedSubPaths(
        List<(int originalIndex, List<Vector2> points, Vector2 centroid)> cluster)
    {
        // Collect all unique points from all action sets in the cluster
        var allPoints = new HashSet<Vector2>();
        foreach (var (_, points, _) in cluster)
        {
            foreach (var point in points)
            {
                allPoints.Add(point);
            }
        }

        if (allPoints.Count == 0)
            return new List<List<Vector2>>();

        // Find connected components - groups of pixels that are adjacent to each other
        var components = FindConnectedComponents(allPoints);

        // Build an optimized path through each connected component
        var subPaths = new List<List<Vector2>>();
        foreach (var component in components)
        {
            if (component.Count > 0)
            {
                var path = BuildNearestNeighborPath(component.ToList());
                subPaths.Add(path);
            }
        }

        return subPaths;
    }

    /// <summary>
    /// Finds connected components in a set of points.
    /// Two points are considered connected if they are within MaxConnectedDistance of each other.
    /// Uses flood-fill / union-find approach to group adjacent pixels.
    /// </summary>
    private static List<HashSet<Vector2>> FindConnectedComponents(HashSet<Vector2> points)
    {
        var components = new List<HashSet<Vector2>>();
        var visited = new HashSet<Vector2>();
        var pointList = points.ToList();

        // Build adjacency information for faster lookup
        // For each point, find all points within MaxConnectedDistance
        var adjacency = new Dictionary<Vector2, List<Vector2>>();
        float maxDistSq = MaxConnectedDistance * MaxConnectedDistance;

        foreach (var p in pointList)
        {
            adjacency[p] = new List<Vector2>();
            foreach (var q in pointList)
            {
                if (p != q && Vector2.DistanceSquared(p, q) <= maxDistSq)
                {
                    adjacency[p].Add(q);
                }
            }
        }

        // Flood-fill to find connected components
        foreach (var startPoint in pointList)
        {
            if (visited.Contains(startPoint))
                continue;

            var component = new HashSet<Vector2>();
            var stack = new Stack<Vector2>();
            stack.Push(startPoint);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (visited.Contains(current))
                    continue;

                visited.Add(current);
                component.Add(current);

                // Add all adjacent unvisited points to the stack
                foreach (var neighbor in adjacency[current])
                {
                    if (!visited.Contains(neighbor))
                    {
                        stack.Push(neighbor);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    /// <summary>
    /// [DEPRECATED - kept for reference]
    /// Merges a cluster of small action sets into a single optimized action set.
    /// Uses nearest-neighbor traversal to minimize total travel distance.
    /// WARNING: This creates lines between disconnected points. Use MergeClusterIntoConnectedSubPaths instead.
    /// </summary>
    private static List<Vector2> MergeClusterIntoActionSet(
        List<(int originalIndex, List<Vector2> points, Vector2 centroid)> cluster)
    {
        // Collect all unique points from all action sets in the cluster
        var allPoints = new HashSet<Vector2>();
        foreach (var (_, points, _) in cluster)
        {
            foreach (var point in points)
            {
                allPoints.Add(point);
            }
        }

        if (allPoints.Count == 0)
            return new List<Vector2>();

        // Build an optimized traversal path using greedy nearest-neighbor
        // This creates a connected path through all points with minimal backtracking
        return BuildNearestNeighborPath(allPoints.ToList());
    }

    // =============================================================================================
    // SPATIAL ORDERING (Issue 2 Fix)
    // =============================================================================================
    //
    // PROBLEM: Action sets were executed in generation order (or arbitrary order after merging),
    // causing the mouse to jump randomly across the canvas between action sets.
    //
    // SOLUTION: Sort action sets by spatial position to minimize mouse travel.
    // We use a row-based scanning approach:
    // - Divide the canvas into horizontal bands
    // - Within each band, sort left-to-right
    // - Alternate direction per band (serpentine/boustrophedon pattern) to avoid
    //   jumping back to the left edge after each row
    //
    // This dramatically reduces total mouse travel distance between action sets.
    // =============================================================================================

    /// <summary>
    /// Sorts action sets spatially to minimize mouse travel between them.
    /// Uses a serpentine (boustrophedon) pattern for efficient traversal.
    /// </summary>
    private static List<List<Vector2>> SortActionSetsSpatially(List<List<Vector2>> actions)
    {
        var withCentroids = actions.Select(a => (centroid: CalculateCentroid(a), points: a)).ToList();
        return SortActionSetsSpatiallyWithCentroids(withCentroids);
    }

    /// <summary>
    /// Sorts action sets spatially using pre-computed centroids.
    /// </summary>
    private static List<List<Vector2>> SortActionSetsSpatiallyWithCentroids(
        List<(Vector2 centroid, List<Vector2> points)> setsWithCentroids)
    {
        if (setsWithCentroids.Count <= 1)
            return setsWithCentroids.Select(s => s.points).ToList();

        // Calculate bounding box to determine row height
        float minY = setsWithCentroids.Min(s => s.centroid.Y);
        float maxY = setsWithCentroids.Max(s => s.centroid.Y);
        float height = maxY - minY;

        // Divide into rows based on canvas height
        // Use approximately 20-50 pixel bands for reasonable grouping
        float rowHeight = Math.Max(30f, height / 20f);
        int numRows = Math.Max(1, (int)Math.Ceiling(height / rowHeight));

        // Group action sets into rows based on their Y centroid
        var rows = new List<List<(Vector2 centroid, List<Vector2> points)>>();
        for (int i = 0; i < numRows; i++)
        {
            rows.Add(new List<(Vector2 centroid, List<Vector2> points)>());
        }

        foreach (var set in setsWithCentroids)
        {
            int rowIndex = Math.Min(numRows - 1, (int)((set.centroid.Y - minY) / rowHeight));
            rows[rowIndex].Add(set);
        }

        // Sort each row by X, alternating direction for serpentine pattern
        var result = new List<List<Vector2>>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0) continue;

            // Even rows: left to right, Odd rows: right to left (serpentine)
            if (i % 2 == 0)
            {
                row.Sort((a, b) => a.centroid.X.CompareTo(b.centroid.X));
            }
            else
            {
                row.Sort((a, b) => b.centroid.X.CompareTo(a.centroid.X));
            }

            foreach (var set in row)
            {
                result.Add(set.points);
            }
        }

        Utils.Log($"[ActionSetOptimizer] Applied spatial ordering: {numRows} rows, serpentine pattern");
        return result;
    }

    /// <summary>
    /// Builds an efficient path through a set of points using greedy nearest-neighbor.
    /// Starting from an edge point, always move to the closest unvisited point.
    /// This produces a reasonably short path without expensive TSP solving.
    /// </summary>
    private static List<Vector2> BuildNearestNeighborPath(List<Vector2> points)
    {
        if (points.Count == 0) return new List<Vector2>();
        if (points.Count == 1) return new List<Vector2> { points[0] };

        var path = new List<Vector2>();
        var remaining = new HashSet<Vector2>(points);

        // Start from the topmost-leftmost point (consistent starting position)
        Vector2 current = points.OrderBy(p => p.Y).ThenBy(p => p.X).First();
        path.Add(current);
        remaining.Remove(current);

        // Greedy nearest-neighbor traversal
        while (remaining.Count > 0)
        {
            Vector2 nearest = FindNearest(current, remaining);
            path.Add(nearest);
            remaining.Remove(nearest);
            current = nearest;
        }

        return path;
    }

    /// <summary>
    /// Finds the nearest point to 'from' within the given set.
    /// </summary>
    private static Vector2 FindNearest(Vector2 from, HashSet<Vector2> candidates)
    {
        Vector2 nearest = candidates.First();
        float minDist = float.MaxValue;

        foreach (var candidate in candidates)
        {
            float dist = Vector2.DistanceSquared(from, candidate); // Squared for performance
            if (dist < minDist)
            {
                minDist = dist;
                nearest = candidate;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Calculates the centroid (average position) of an action set.
    /// Used for clustering proximity calculations.
    /// </summary>
    private static Vector2 CalculateCentroid(List<Vector2> points)
    {
        if (points.Count == 0)
            return Vector2.Zero;

        float sumX = 0, sumY = 0;
        foreach (var p in points)
        {
            sumX += p.X;
            sumY += p.Y;
        }

        return new Vector2(sumX / points.Count, sumY / points.Count);
    }

    // =============================================================================================
    // CONFIGURATION HELPERS
    // =============================================================================================

    /// <summary>
    /// Configures the optimizer with custom thresholds.
    /// Call this before drawing to adjust optimization behavior.
    /// </summary>
    /// <param name="smallSetThreshold">Max points for a set to be considered "small"</param>
    /// <param name="clusterDistance">Max distance between set centroids for clustering</param>
    /// <param name="minClusterSize">Min sets in a cluster to trigger merging</param>
    /// <param name="maxConnectedDistance">Max distance between points to be considered connected (default 1.5 for 8-connectivity)</param>
    /// <param name="enableSpatialOrdering">Whether to sort action sets spatially</param>
    public static void Configure(
        int smallSetThreshold = 10,
        float clusterDistance = 25f,
        int minClusterSize = 3,
        float maxConnectedDistance = 1.5f,
        bool enableSpatialOrdering = true)
    {
        SmallActionSetThreshold = smallSetThreshold;
        ClusterDistanceThreshold = clusterDistance;
        MinClusterSizeForMerge = minClusterSize;
        MaxConnectedDistance = maxConnectedDistance;
        EnableSpatialOrdering = enableSpatialOrdering;
    }

    /// <summary>
    /// Provides statistics about action sets without modifying them.
    /// Useful for debugging and understanding layer complexity.
    /// </summary>
    public static ActionSetStatistics AnalyzeActionSets(List<List<Vector2>> actions)
    {
        var stats = new ActionSetStatistics
        {
            TotalActionSets = actions.Count,
            TotalPoints = actions.Sum(a => a.Count),
            SmallActionSets = actions.Count(a => a.Count <= SmallActionSetThreshold),
            LargeActionSets = actions.Count(a => a.Count > SmallActionSetThreshold),
            MinSetSize = actions.Count > 0 ? actions.Min(a => a.Count) : 0,
            MaxSetSize = actions.Count > 0 ? actions.Max(a => a.Count) : 0,
            AverageSetSize = actions.Count > 0 ? actions.Average(a => a.Count) : 0
        };

        return stats;
    }

    /// <summary>
    /// Statistics about action set distribution for a layer.
    /// </summary>
    public struct ActionSetStatistics
    {
        public int TotalActionSets;
        public int TotalPoints;
        public int SmallActionSets;
        public int LargeActionSets;
        public int MinSetSize;
        public int MaxSetSize;
        public double AverageSetSize;

        public override string ToString()
        {
            return $"ActionSets: {TotalActionSets} (Small: {SmallActionSets}, Large: {LargeActionSets}), " +
                   $"Points: {TotalPoints}, Size: min={MinSetSize}, max={MaxSetSize}, avg={AverageSetSize:F1}";
        }
    }
}
