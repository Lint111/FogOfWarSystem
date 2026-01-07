---
tags: [architecture, islands, environment, sdf]
created: 2026-01-07
---

# Island System Architecture

## Overview
The Island System manages disconnected environmental regions in the fog-of-war visibility system. Each island is a self-contained area with a pre-baked Signed Distance Field (SDF) volume that defines its solid geometry. Islands enable efficient visibility queries in multi-region levels while maintaining clear separation between environments.

## Core Concepts

### What is an Island?
An island is a connected region of geometry in the scene with:
- A baked SDF volume (Texture3D) representing solid geometry
- Defined spatial bounds (AABB)
- Unique slot assignment (0-7, max 8 islands per scene)
- Optional hierarchical structure (nested meshes, prefabs)

### Island Types
1. **Monolithic** - Single mesh per island (simple levels)
2. **Composite** - Multiple meshes combined into one SDF (buildings, structures)
3. **Hierarchical** - Nested GameObjects with island root marking boundary

## Architecture

### Data Structures

#### IslandData (GPU Buffer)
```hlsl
struct IslandData
{
    float3 boundsMin;      // World space AABB minimum
    float3 boundsMax;      // World space AABB maximum
    Texture3D sdfVolume;   // Baked SDF texture (64³ or 128³)
    int slot;              // Slot index 0-7
};
```

#### IslandEntry (CPU Runtime)
```csharp
public struct IslandEntry
{
    public int slot;
    public AABB bounds;
    public RenderTexture sdfTexture;
    public IslandSDFContributor contributor;
}
```

### System Components

#### 1. IslandRegistry
Central registry of all islands in the scene.
- Singleton access: `IslandRegistry.Instance`
- Methods:
  - `Register(IslandSDFContributor)` - Add island at runtime
  - `Unregister(int slot)` - Remove island
  - `GetIsland(int slot)` - Retrieve by slot
  - `TryGetIslandAt(Vector3 pos)` - Find island containing position

#### 2. IslandSDFContributor (Authoring)
Runtime component attached to island root GameObject.
- Holds island configuration (slot, bounds override)
- Triggers SDF baking process
- Manages texture upload to GPU
- Lifecycle: Domain load → Register → Ready for queries

#### 3. IslandBounds
Defines the spatial extent of an island.
- Auto-computed from child meshes
- Manual override for performance
- Used for ray-AABB intersection tests
- Defers to parent space for nested islands

### Baking Pipeline

#### 1. SDF Generation
1. Create Texture3D target (64³ default)
2. Render island geometry to RenderTexture via compute shader
3. Ray-march from each voxel to surface
4. Store signed distance (-1 to +1 normalized)

#### 2. Texture Optimization
- Format: RFloat (single channel, 4 bytes per voxel)
- No mipmaps (used as 3D lookup, not filtered)
- Saved as Texture3D asset in `Assets/FogOfWar/BakedSDFs/`

#### 3. Dirty Detection & Auto-Bake
- IslandDirtyTracker monitors mesh/hierarchy changes
- Debounce window: 2 minutes
- Triggers auto-bake or play mode blocking
- See [[05-Progress/features/auto-sdf-island-baking]]

### Visibility Query Integration

#### Cross-Island Ray Marching
When visibility query spans multiple islands:

1. **Ray-AABB Intersection**: Determine which islands ray passes through
2. **Per-Island March**: Step through each island's SDF
3. **Cumulative Distance**: Track minimum distance to all surfaces
4. **Occlusion**: If cumulative < threshold, target occluded

#### Ray-AABB Intersection Test
```csharp
// Pseudo-code
bool RayIntersectsIsland(Ray ray, IslandEntry island, out float tEnter, out float tExit)
{
    return AABBIntersection(ray, island.bounds, out tEnter, out tExit);
}
```

## Data Flow

### Scene Load → Visibility Ready
```
[Scene loaded]
    ↓
[IslandSDFContributor.OnEnable()]
    ↓
[IslandRegistry.Register(contributor)]
    ↓
[VisibilitySystemRuntime.UploadIslandSDFs()]
    ↓
[GPU buffers updated with island data]
    ↓
[Visibility queries ready]
```

### Slot Assignment Strategy

#### Stable Slots
- Slot determined by island transform hash (position + rotation)
- Persists across domain reloads if island doesn't move
- Enables predictable debugging

#### Collision Handling
- Check if requested slot occupied
- Fall back to first free slot (0-7)
- Log warning if 8+ islands attempted

#### Reassignment
- Islands deleted/recreated get new hash
- Old slot available for reuse
- No fragmentation mechanism (simplicity trade-off)

## Performance Considerations

### Memory Per Island
| Component | Size |
|-----------|------|
| Texture3D (64³, RFloat) | ~1 MB |
| CPU IslandEntry | ~32 bytes |
| GPU buffer entry | ~48 bytes |
| **Total per island** | ~1 MB |

### GPU Performance
- **Ray-AABB Test**: ~0.1ms per ray (10,000 rays)
- **Per-Island March**: ~0.5ms per island (1000 voxel steps)
- **Total visibility pass**: Scales with # islands and # rays

### Optimization Strategies
1. **Spatial Partitioning**: Query only islands in relevant region
2. **Slot Recycling**: Reuse freed slots before allocating new
3. **Texture Caching**: Keep frequently-used SDFs in VRAM
4. **LOD System**: Future - lower resolution SDF for distant queries

## Limitations & Future Improvements

### Current
- Max 8 islands per scene (slot array size)
- Fixed resolution (64³) for all islands
- No runtime mesh deformation support
- SDF baking offline only

### Future
- Variable resolution per island based on size
- Runtime SDF updates for dynamic geometry
- Streaming island load/unload
- Hierarchical BVH for ray-island intersection
- Temporal coherence optimization

## Related Systems

### Visibility System
- [[01-Architecture/Overview]] - Visibility query pipeline
- Queries use island SDFs for occlusion checks
- Island bounds inform ray-march termination

### SDF Baking
- [[05-Progress/features/auto-sdf-island-baking]] - Automatic baking system
- Editor tools for manual baking and debugging

### Authoring & Tools
- Editor window for island management
- Visual gizmos for island bounds
- Bake status indicator

## Best Practices

### Creating Islands
1. Group geometry under single root GameObject
2. Add IslandSDFContributor to root
3. Ensure bounds encompass all child meshes
4. Avoid overlapping islands (ray-marching ambiguity)

### Debugging
- Enable island bounds visualization in editor
- Check IslandRegistry for registered slots
- Monitor SDF texture quality in preview window

### Performance
- Keep island count low (4-6 typical)
- Bake islands at reasonable resolution (64-128³)
- Profile visibility queries with profiler

## References
- GPU Compute Pipeline: [[01-Architecture/Overview]]
- Baking System: [[05-Progress/features/auto-sdf-island-baking]]
- Decision Log: [[memory-bank/decisionLog]]
