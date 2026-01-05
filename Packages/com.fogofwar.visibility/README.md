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
3. Enter: `https://github.com/yourusername/FogOfWarSystem.git?path=Packages/com.fogofwar.visibility`

### Via manifest.json

Add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.fogofwar.visibility": "https://github.com/yourusername/FogOfWarSystem.git?path=Packages/com.fogofwar.visibility"
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
| `EnvironmentIslandAuthoring` | Defines SDF occlusion volume |

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

### SDF Baking

Use Unity's **VFX Graph SDF Bake Tool**:

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
- Reduce `FogResolution` for larger volumes
- Limit `MaxCandidatesPerGroup` based on expected density

## Samples

Import via Package Manager:
- **Basic Visibility Demo** - Minimal setup example
- **Stress Test** - 4000+ unit performance test

## Troubleshooting

### No visibility data
1. Check `VisibilitySystemBehaviour` exists in scene
2. Verify config has all shaders assigned
3. Ensure entities are within volume bounds

### Poor performance
1. Check unit count with `VisibilityPerformanceMonitor`
2. Reduce `FogResolution` in config
3. Enable `UseParallelDispatch`

### Visibility incorrect
1. Add `VisibilityDebugOverlay` to visualize
2. Check island SDF textures are valid
3. Verify group IDs are correct (0-7)

## License

MIT License - See LICENSE.md
