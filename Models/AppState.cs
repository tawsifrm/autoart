using System.Collections.Generic;

namespace AutoArt.Models;

/// <summary>
/// Represents the current state of the AutoArt application workflow.
/// </summary>
public enum AppStateType
{
    /// <summary>
    /// Initial state - no image loaded.
    /// </summary>
    Idle,
    
    /// <summary>
    /// Image loaded, user is configuring color splitting options.
    /// </summary>
    Configuring,
    
    /// <summary>
    /// Color splitting is in progress.
    /// </summary>
    Splitting,
    
    /// <summary>
    /// Color splitting complete, layers are ready. Waiting for user to start drawing.
    /// </summary>
    Ready,
    
    /// <summary>
    /// Showing current layer, waiting for user to press Shift to draw.
    /// </summary>
    WaitingForUser,
    
    /// <summary>
    /// Currently drawing a layer.
    /// </summary>
    Drawing,
    
    /// <summary>
    /// All layers have been drawn.
    /// </summary>
    Finished
}

/// <summary>
/// Manages the application state and holds all relevant data for the current session.
/// </summary>
public class AppState
{
    public AppStateType CurrentState { get; set; } = AppStateType.Idle;
    
    /// <summary>
    /// Index of the current layer being processed (0-based).
    /// </summary>
    public int CurrentLayerIndex { get; set; } = 0;
    
    /// <summary>
    /// Total number of color layers.
    /// </summary>
    public int TotalLayers { get; set; } = 0;
    
    /// <summary>
    /// The list of color layers to draw.
    /// </summary>
    public List<ColorLayer> Layers { get; set; } = new();
    
    /// <summary>
    /// Gets the current layer being processed, or null if no layers available.
    /// </summary>
    public ColorLayer? CurrentLayer => 
        CurrentLayerIndex >= 0 && CurrentLayerIndex < Layers.Count 
            ? Layers[CurrentLayerIndex] 
            : null;
    
    /// <summary>
    /// Resets the state to idle.
    /// </summary>
    public void Reset()
    {
        CurrentState = AppStateType.Idle;
        CurrentLayerIndex = 0;
        TotalLayers = 0;
        
        // Dispose existing layers
        foreach (var layer in Layers)
        {
            layer.Dispose();
        }
        Layers.Clear();
    }
    
    /// <summary>
    /// Advances to the next layer. Returns true if there is a next layer, false if finished.
    /// </summary>
    public bool AdvanceToNextLayer()
    {
        CurrentLayerIndex++;
        if (CurrentLayerIndex >= TotalLayers)
        {
            CurrentState = AppStateType.Finished;
            return false;
        }
        CurrentState = AppStateType.WaitingForUser;
        return true;
    }
}
