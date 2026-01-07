---
tags: [feature, sdf-baking, islands, tooling]
status: in-progress
created: 2026-01-07
---

# Automatic SDF Island Baking System

## Status: In Progress

## Overview
Behind-the-scenes tool for automatic island SDF baking with dirty detection, debounced change monitoring, and play mode triggers. This system eliminates manual baking workflows by automatically detecting island changes and pre-baking SDF volumes before play mode entry.

## Approved Configuration
- **Default Resolution**: 64³ (262,144 voxels per island)
- **Play Mode Behavior**: Block entry and bake dirty islands before entering
- **Slot Assignment**: Auto-assign slots 0-7
- **Output Path**: `Assets/FogOfWar/BakedSDFs/`
- **Debounce Window**: 2 minutes after last change
- **Memory Limit**: 50MB per island

## Architecture

### Components

#### 1. SDFBakeConfig
ScriptableObject configuration asset for global baking settings.
- Resolution preset selection
- Output directory configuration
- Play mode blocking toggle
- Debounce timeout settings

#### 2. IslandSDFContributor
Runtime component that registers islands for SDF baking.
- Attached to island GameObject root
- Provides mesh reference and bounds
- Handles domain reload registration
- Updates VisibilitySystemRuntime with baked textures

#### 3. IslandDirtyTracker
Editor singleton for change detection.
- Monitors hierarchy changes (Add/Remove GameObject)
- Tracks mesh asset modifications
- Tracks material changes on island meshes
- Debounce mechanism prevents thrashing
- Signals SDFBakeQueue when dirty

#### 4. SDFBakeQueue
Background baking pipeline manager.
- Processes island queue sequentially
- Handles texture generation (RenderTexture → Texture3D)
- Manages async GPU operations
- Saves results to disk
- Updates runtime registry

#### 5. SDFIslandManagerWindow
Editor window UI for manual control and monitoring.
- Lists all registered islands
- Shows dirty status and last bake timestamp
- Manual bake trigger per island
- Clear all baked SDF files
- Bake progress indicator

### Data Flow

```
[Island Created/Modified]
              ↓
[IslandDirtyTracker monitors changes]
              ↓
[Debounce timer (2 min)]
              ↓
[Enter Play Mode OR timeout expires]
              ↓
[SDFBakeQueue processes island]
              ↓
[Unity MeshToSDFBaker GPU bake (JFA algorithm)]
              ↓
[Copy to Texture3D asset]
              ↓
[Write to Assets/FogOfWar/BakedSDFs/]
              ↓
[VisibilitySystemRuntime registers texture]
              ↓
[Island ready for visibility queries]
```

## Implementation Details

### Dirty Detection Strategy
- **Hierarchy Changes**: EditorApplication.hierarchyChanged event
- **Asset Changes**: AssetModificationProcessor for mesh/material changes
- **Timing**: IslandDirtyTracker marks island dirty, queues for processing

### Play Mode Entry Flow
1. Editor detects EditorApplication.playModeStateChanged → ExitingEditMode
2. IslandDirtyTracker triggers bake for all dirty islands
3. SDFBakeQueue blocks play mode entry while baking (IslandSDFContributor.WaitForBakes())
4. Once complete, play mode entry proceeds

### Slot Assignment
- Islands assigned slots 0-7 automatically
- Stable assignment: based on island transform hash
- Reassignment only if island deleted and new one created

## Files

### Core Implementation
- `Editor/SDFBaking/SDFBakeConfig.cs` - Configuration asset
- `Editor/SDFBaking/IslandDirtyTracker.cs` - Change detection singleton
- `Editor/SDFBaking/SDFBakeQueue.cs` - Baking pipeline (uses Unity's MeshToSDFBaker)
- `Editor/SDFBaking/SDFIslandManagerWindow.cs` - Editor UI window
- `Runtime/Scripts/Authoring/IslandSDFContributor.cs` - Island registration

### Dependencies
- `com.unity.visualeffectgraph` - Provides `UnityEngine.VFX.SDF.MeshToSDFBaker` API

## Key Features

### Automatic Tracking
- No manual registration required
- Island changes detected automatically
- Debounce prevents repeated bakes

### Non-Blocking
- Baking happens in background after debounce
- Play mode entry blocks only if islands dirty
- Progress UI shows baking status

### Slot Management
- Up to 8 islands per scene
- Auto-assignment prevents conflicts
- Efficient slot reuse on deletion

### Texture Management
- Texture3D saved per island
- VRAM optimized (RFloat, mipless)
- Async upload to GPU

## Performance Targets
- **Bake Time**: ~50-100ms per 64³ island (GPU)
- **Memory**: ~1MB per island (Texture3D) + temp RenderTexture (4MB)
- **CPU**: <5ms per frame for change detection

## Related References
- [[01-Architecture/Islands]] - Island system architecture
- [[memory-bank/decisionLog#DECISION-003]] - Decision on auto-baking approach
- [[01-Architecture/Overview]] - Visibility system overview

## Implementation Status

### Completed
1. ~~Implement IslandDirtyTracker with hierarchy monitoring~~
2. ~~Create SDFBakeQueue with Unity MeshToSDFBaker API~~
3. ~~Build SDFIslandManagerWindow UI~~
4. ~~SDFBakeConfig ScriptableObject~~
5. ~~IslandSDFContributor runtime component~~

### Remaining
1. Integrate with VisibilitySystemRuntime
2. Test play mode entry blocking behavior
3. Add VFX Graph package dependency to package.json
