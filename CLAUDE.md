# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SDF-Based Group Visibility System - a Unity plugin providing real-time fog-of-war using Signed Distance Fields and GPU compute shaders. Designed for distribution via Unity Package Manager.

### Key Goals
- Multi-group visibility (up to 8 factions) with per-group visible entity lists
- SDF-based vision volumes (sphere, cone, dual-sphere types)
- Multi-island environment support with baked SDF volumes
- GPU compute pipeline with async readback (1-2 frame latency)
- Performance targets: <2ms GPU, <0.5ms CPU, <50MB memory

## Package Development

This is a **Unity Package** intended for distribution via Package Manager. Development follows Unity's package layout conventions.

### Package Structure (Target)
```
Packages/
  com.yourcompany.fogofwar/
    package.json
    README.md
    LICENSE.md
    CHANGELOG.md
    Runtime/
      Scripts/
        Components/   # ECS components
        Systems/      # ECS systems
        GPU/          # Buffer management
        Query/        # Public API
      Shaders/
        Visibility/   # Compute shaders
      Rendering/      # URP integration
    Editor/
      SDF Baking tools
      Island configuration
    Documentation~/
    Tests/
      Runtime/
      Editor/
```

### Package Dependencies
```json
{
  "com.unity.entities": "1.0.0",
  "com.unity.render-pipelines.universal": "17.0.0",
  "com.unity.mathematics": "1.0.0"
}
```

## Build Commands

```bash
# Run tests (when implemented)
# Unity Editor: Window > General > Test Runner

# Build sample project
# Unity Editor: File > Build Settings > Build
```

## Architecture

### Data Flow
1. **ECS Data Collection** (CPU) - Aggregate unit positions, collect seeables, determine relevant islands
2. **GPU Compute Pipeline**:
   - Stage 1: Player Fog Volume (128³ voxels)
   - Stage 2: Visibility Check (SDF evaluation)
   - Stage 3: Ray March Confirmation (occlusion)
3. **Async Readback** - GPU results to CPU (1-2 frame latency)
4. **Event System** - Visibility change events

### Key GPU Structures (16-byte aligned)
- `VisionGroupData` (48B) - Group metadata, unit bounds, visibility masks
- `UnitSDFContribution` (40B) - Per-unit vision (position, radius, type)
- `SeeableEntityData` (24B) - Entities that can be seen
- `VisibilityEntry` (16B) - Final visibility output

### Vision Types
- **Sphere** (0) - Radius-based vision
- **SphereWithCone** (1) - Sphere + forward cone
- **DualSphere** (2) - Two smooth-unioned spheres

### Environment Islands
Disconnected regions with separate baked SDF volumes. Cross-island visibility uses ray-AABB intersection.

## Public API

### Query Pattern
```csharp
// Get visible entities for a group
var visible = VisibilityQuery.GetVisibleToGroup(queryData, groupId);

// Check specific entity visibility
bool canSee = VisibilityQuery.IsEntityVisibleToGroup(queryData, entityId, groupId);
```

### ECS Components (Public)
- `VisionGroupMembership` - Assigns entity to vision group
- `UnitVision` - Defines unit's vision contribution
- `Seeable` - Marks entity as visible to other groups
- `VisibleToGroups` - Output: which groups see this entity

### System Update Order
1. VisibilityDataCollectionSystem
2. VisibilityComputeDispatchSystem
3. VisibilityReadbackSystem
4. VisibilityEventSystem
5. VisibleToGroupsUpdateSystem

## Compute Shader Constants
```hlsl
#define MAX_GROUPS 8
#define MAX_ISLANDS 16
#define MAX_RAY_STEPS 48
#define VISIBILITY_THRESHOLD 0.1
#define OCCLUSION_THRESHOLD 0.05
```

## Dependencies

- Unity 6+ (URP 17.0.0+)
- Unity DOTS Entities 1.0+
- Compute Shader Model 5.0+

## Project Management

### HacknPlan
- **Project ID**: 231460
- **Project URL**: https://app.hacknplan.com/p/231460
- **Default Board**: Sprint 1: Infrastructure & Package Setup (651863)

### Obsidian Vault
- **Location**: `FogOfWar-Docs/`
- **Quick Lookup**: `FogOfWar-Docs/00-Index/Quick-Lookup.md`
- **Memory Bank**: `FogOfWar-Docs/memory-bank/`

### Key Design Elements (HacknPlan)
| ID | Name | Type |
|----|------|------|
| 1 | Visibility System Core | System |
| 2 | ECS Data Collection | System |
| 3 | Environment Islands | System |
| 4 | GPU Compute Pipeline | System |
| 5 | Query API | System |

### Documentation Workflow
1. Check `memory-bank/activeContext.md` for current focus
2. Reference `00-Index/Quick-Lookup.md` for navigation
3. Architecture docs in `01-Architecture/`
4. Feature plans in `05-Progress/features/`

## Design Document

Full technical specification: See `sdf_visibility_system_design_doc.md` in Downloads folder.
