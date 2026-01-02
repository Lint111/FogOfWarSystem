---
tags: [architecture, overview]
hacknplan-project: 231460
---

# FogOfWarSystem Architecture Overview

## Purpose

SDF-Based Group Visibility System - a Unity plugin providing real-time fog-of-war using Signed Distance Fields and GPU compute shaders. Designed for distribution via Unity Package Manager.

## Key Goals

- Multi-group visibility (up to 8 factions) with per-group visible entity lists
- SDF-based vision volumes (sphere, cone, dual-sphere types)
- Multi-island environment support with baked SDF volumes
- GPU compute pipeline with async readback (1-2 frame latency)
- Performance targets: <2ms GPU, <0.5ms CPU, <50MB memory

## High-Level Data Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              ECS WORLD                                      │
│  Unit Entities → Seeable Entities → Environment Islands → Vision Groups    │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         DATA COLLECTION (CPU)                               │
│  - Aggregate unit positions per group                                       │
│  - Collect seeable entity positions                                         │
│  - Determine relevant environment islands per group                         │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         GPU COMPUTE PIPELINE                                │
│  Stage 1: Player Fog Volume Generation (128³ voxels)                       │
│  Stage 2: All Groups Visibility Check (SDF evaluation)                     │
│  Stage 3: Ray March Confirmation (occlusion testing)                       │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         RESULTS PROCESSING (CPU)                            │
│  - Async GPU readback (1-2 frame latency)                                  │
│  - Per-group visible entity arrays                                          │
│  - Visibility change events (entered/exited vision)                        │
└─────────────────────────────────────────────────────────────────────────────┘
```

## System Boundaries

| System | Input | Output | Runs On |
|--------|-------|--------|---------|
| Data Collection | ECS Components | GPU Buffers | CPU (Jobs) |
| Fog Volume Gen | Unit SDFs, Islands | 3D Texture | GPU |
| Visibility Check | Units, Seeables, Islands | Candidates | GPU |
| Ray March | Candidates, Islands | Visible Lists | GPU |
| Readback Processing | GPU Buffers | Query Singleton | CPU |
| Event System | Current + Previous Visible | Events | CPU |

## Package Structure

```
Packages/com.yourcompany.fogofwar/
├── package.json
├── Runtime/
│   ├── Scripts/
│   │   ├── Components/    # ECS components
│   │   ├── Systems/       # ECS systems  
│   │   ├── GPU/           # Buffer management
│   │   └── Query/         # Public API
│   ├── Shaders/Visibility/
│   └── Rendering/         # URP integration
├── Editor/                 # SDF baking tools
└── Tests/
```

## Related Documentation

- [[GPU-Pipeline]] - Detailed compute shader architecture
- [[ECS-Components]] - Component definitions
- [[Query-API]] - Public query interface
- [[Islands]] - Environment island system
