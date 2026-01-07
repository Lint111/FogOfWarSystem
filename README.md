# SDF Visibility System

GPU-accelerated fog-of-war and visibility system for Unity using Signed Distance Fields (SDF) and ECS.

## Features

- **Multi-Group Visibility** - Up to 8 factions with independent visibility
- **SDF-Based Vision** - Sphere, cone, and dual-sphere vision types
- **GPU Compute Pipeline** - Handles 4000+ units at 60 FPS
- **Multi-Island Support** - Separate SDF volumes for disconnected regions
- **Async Readback** - 1-2 frame latency for CPU queries
- **Event System** - Callbacks when entities enter/exit vision

## Requirements

- Unity 6000.0+ (Unity 6)
- Universal Render Pipeline 17.0.0+
- Entities 1.0.0+
- Compute Shader Model 5.0+

## Installation

### Via Package Manager

1. Open **Window > Package Manager**
2. Click **+ > Add package from git URL...**
3. Enter: `https://github.com/Lint111/FogOfWarSystem.git`

### Via manifest.json

Add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.fogofwar.visibility": "https://github.com/Lint111/FogOfWarSystem.git"
  }
}
```

## Quick Start

### 1. Create Configuration

1. **Assets > Create > FogOfWar > Visibility System Config**
2. Assign the compute shaders from `Packages/com.fogofwar.visibility/Runtime/Shaders/`:
   - `PlayerFogVolume.compute`
   - `VisibilityCheck.compute`
   - `RayMarchConfirm.compute`
3. Set volume bounds to cover your game area
4. Configure capacity with just 2 parameters:
   - **NumberOfGroups** (1-8): How many factions/teams
   - **EntitiesPerGroup** (e.g., 512): Max entities per faction
   - All other capacities are auto-calculated to ensure sufficient buffers

### 2. Add System to Scene

1. Create empty GameObject
2. **Add Component > FogOfWar > Visibility System**
3. Assign your configuration asset

### 3. Add Vision to Units

Add `UnitVisionAuthoring` component to your unit GameObjects:
- Set **Group ID** (0-7) for faction
- Configure **vision radius** and **type**

### 4. Mark Targets as Seeable

Add `SeeableAuthoring` component to entities that can be detected.

### 5. Query Visibility (Optional)

```csharp
// Check if entity is visible to a group
var queryData = SystemAPI.GetSingleton<VisibilityQueryData>();
bool canSee = VisibilityQuery.IsEntityVisibleToGroup(queryData, entityId, groupId);

// Get all visible entities for a group
var visible = VisibilityQuery.GetVisibleToGroup(queryData, groupId);
```

## Components

### Authoring (Add to GameObjects)

| Component | Purpose |
|-----------|---------|
| `UnitVisionAuthoring` | Gives entity vision capability |
| `SeeableAuthoring` | Marks entity as detectable |
| `EnvironmentIslandAuthoring` | Defines SDF occlusion volume (runtime) |
| `IslandSDFContributor` | Marks island for auto-baking (editor) |

### Editor Tools

| Window | Purpose |
|--------|---------|
| `SDF Island Manager` | Monitor and bake island SDFs (Window > FogOfWar) |

### Debug Tools

| Component | Purpose |
|-----------|---------|
| `FogVolumeVisualizer` | Shows fog volume slice in scene |
| `VisibilityDebugOverlay` | Shows units, targets, visibility lines |
| `VisibilityPerformanceMonitor` | On-screen FPS and entity stats |
| `VisibilityEventListener` | Subscribe to visibility events via UnityEvents |

## Vision Types

```csharp
public enum VisionType
{
    Sphere,           // Basic radius-based vision
    SphereWithCone,   // Sphere + forward cone (peripheral + focused)
    DualSphere        // Two blended spheres (e.g., head + turret)
}
```

## Environment Islands (SDF Occlusion)

Islands are disconnected regions with baked SDF volumes for occlusion.

### Creating an Island

1. Create a 3D texture with SDF data (see SDF Baking below)
2. Add `EnvironmentIslandAuthoring` to a GameObject
3. Assign the SDF texture and set texture slot (0-7)
4. The system supports up to 8 islands

### SDF Baking (Automatic)

The system includes **automatic SDF baking** with change detection:

1. Add `IslandSDFContributor` component to your island GameObject (auto-adds `EnvironmentIslandAuthoring`)
2. The system automatically detects mesh changes and queues rebakes
3. Open **Window > FogOfWar > SDF Island Manager** to monitor status

#### SDF Island Manager Window

Access via **Window > FogOfWar > SDF Island Manager**:

- **Config Asset** - Drag-drop or search for `SDFBakeConfig` asset
- **Status Bar** - Shows tracked islands, dirty count, queue status
- **Island List** - View all islands with bake status
- **Actions** - Bake All Dirty, Stop Baking, Refresh Islands

#### SDF Bake Config

Create via **Assets > Create > FogOfWar > SDF Bake Config**:

| Setting | Description |
|---------|-------------|
| Bake Before Play | Auto-bake dirty islands before entering play mode |
| Auto-Bake Debounced | Queue bake after changes (with debounce timer) |
| Debounce Duration | Wait time after last change before auto-bake (30-300s) |
| Resolution | Default SDF texture resolution (32-256) |
| Output Path | Folder for baked SDF textures |
| Bounds Padding | Extra padding around mesh bounds (0-0.5) |
| Sign Passes | Accuracy vs speed tradeoff (1-4) |

#### Manual Baking (Alternative)

You can also use Unity's **VFX Graph SDF Bake Tool**:

1. **Window > Visual Effects > Utilities > SDF Bake Tool**
2. Assign your mesh
3. Set resolution (64-128 recommended)
4. Bake and save as 3D texture
5. Assign to `EnvironmentIslandAuthoring`

## Events

Subscribe to visibility changes:

```csharp
void Start()
{
    var system = VisibilitySystemBehaviour.Instance;
    system.OnVisibilityChanged += OnVisibilityChanged;
}

void OnVisibilityChanged(VisibilityChangeInfo info)
{
    if (info.EventType == VisibilityEventType.Entered)
        Debug.Log($"Entity {info.EntityId} spotted by group {info.ViewerGroupId}");
}
```

Or use the `VisibilityEventListener` component with UnityEvents.

## Performance

Tested performance (RTX 3080):

| Units | Groups | FPS |
|-------|--------|-----|
| 2000 | 4 | 95 |
| 4096 | 4 | 55-60 |

### Optimization Tips

- Enable `UseParallelDispatch` in config for multi-group scenarios
- Reduce `FogResolution` for larger volumes (64 for large worlds, 128 for medium, 256 for small precise areas)
- Adjust `EntitiesPerGroup` based on expected entity counts:
  - Small games: 256 entities per group
  - Medium games: 512 entities per group (default)
  - Large games: 1024 entities per group
- All buffer capacities are automatically derived to prevent overflow

## Samples

Import via Package Manager:
- **Basic Visibility Demo** - Minimal setup example
- **Stress Test** - 4000+ unit performance test

## Troubleshooting

### No visibility data
1. Check `VisibilitySystemBehaviour` exists in scene
2. Verify config has all shaders assigned
3. Ensure entities are within volume bounds
4. Check that `NumberOfGroups` includes your entity's group ID

### Poor performance
1. Check unit count with `VisibilityPerformanceMonitor`
2. Reduce `FogResolution` in config
3. Enable `UseParallelDispatch`
4. Reduce `EntitiesPerGroup` if you have fewer entities than configured

### Visibility incorrect / artifacts
1. Add `VisibilityDebugOverlay` to visualize
2. Check island SDF textures are valid
3. Verify group IDs are correct (0-7 and within `NumberOfGroups`)
4. Recent fixes:
   - Fixed stripe artifacts from shared memory bug
   - Added passive fog dissipation for smoother transitions
   - Temporal blending reduces flickering

### Buffer overflow errors
1. Increase `EntitiesPerGroup` to match your entity count
2. The system auto-calculates all buffer sizes from this value
3. Check console for validation warnings from config's `OnValidate()`

## License

MIT License - See LICENSE.md
