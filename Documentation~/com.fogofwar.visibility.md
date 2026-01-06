# SDF Visibility System Documentation

## Overview

The SDF Visibility System provides GPU-accelerated fog-of-war functionality using Signed Distance Fields. It integrates with Unity's ECS (Entities) framework and Universal Render Pipeline.

## Architecture

### Data Flow

1. **ECS Data Collection** (CPU) - Aggregate unit positions, collect seeables, determine relevant islands
2. **GPU Compute Pipeline**:
   - Stage 1: Player Fog Volume (128³ voxels)
   - Stage 2: Visibility Check (SDF evaluation)
   - Stage 3: Ray March Confirmation (occlusion)
3. **Async Readback** - GPU results to CPU (1-2 frame latency)
4. **Event System** - Visibility change events

### Vision Types

| Type | Description |
|------|-------------|
| Sphere | Radius-based omnidirectional vision |
| SphereWithCone | Sphere + forward-facing cone |
| DualSphere | Two smooth-unioned spheres |

## Components

### VisionGroupMembership
Assigns an entity to a vision group (0-7).

### UnitVision
Defines a unit's vision contribution parameters.

### Seeable
Marks an entity as potentially visible to other groups.

### VisibleToGroups
Output component: bitmask of which groups can see this entity.

## Systems

Systems execute in the following order:

1. VisibilityDataCollectionSystem
2. VisibilityComputeDispatchSystem
3. VisibilityReadbackSystem
4. VisibilityEventSystem
5. VisibleToGroupsUpdateSystem

## Query API

```csharp
// Get all entities visible to a group
var visible = VisibilityQuery.GetVisibleToGroup(queryData, groupId);

// Check if specific entity is visible
bool canSee = VisibilityQuery.IsEntityVisibleToGroup(queryData, entityId, groupId);
```

## Performance Targets

- GPU: <2ms per frame
- CPU: <0.5ms per frame
- Memory: <50MB total

## Multi-Island Support

Disconnected map regions use separate baked SDF volumes. Cross-island visibility uses ray-AABB intersection tests.
