# SDF Visibility System

GPU-accelerated fog-of-war and visibility system using Signed Distance Fields (SDF) for Unity.

## Features

- **Multi-Group Visibility**: Support for up to 8 independent vision groups (factions)
- **SDF-Based Vision**: Accurate vision volumes using sphere, cone, and dual-sphere types
- **Multi-Island Support**: Handle disconnected map regions with separate baked SDF volumes
- **GPU Compute Pipeline**: 3-stage GPU processing with async readback (1-2 frame latency)
- **ECS Integration**: Built on Unity DOTS for high-performance entity processing

## Requirements

- Unity 6000.0 or later
- Universal Render Pipeline (URP) 17.0.0+
- Unity Entities 1.0.0+
- Compute Shader Model 5.0+ capable GPU

## Installation

### Via Package Manager (Git URL)

1. Open Window > Package Manager
2. Click + > Add package from git URL
3. Enter: `https://github.com/yourusername/FogOfWarSystem.git?path=Packages/com.fogofwar.visibility`

### Via manifest.json

Add to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.fogofwar.visibility": "https://github.com/yourusername/FogOfWarSystem.git?path=Packages/com.fogofwar.visibility"
  }
}
```

## Quick Start

```csharp
// Add vision to a unit
entityManager.AddComponent<VisionGroupMembership>(entity);
entityManager.SetComponentData(entity, new VisionGroupMembership { GroupId = 0 });
entityManager.AddComponent<UnitVision>(entity);
entityManager.SetComponentData(entity, new UnitVision
{
    Type = VisionType.Sphere,
    Radius = 10f
});

// Mark an entity as visible
entityManager.AddComponent<Seeable>(entity);

// Query visibility
var visible = VisibilityQuery.GetVisibleToGroup(queryData, groupId);
```

## Documentation

See the [Documentation](Documentation~/com.fogofwar.visibility.md) folder for detailed guides.

## License

MIT License - See LICENSE.md for details.
