---
tags: [memory-bank, context]
hacknplan-project: 231460
last-updated: 2026-01-02
---

# Project Context

## Project Identity

- **Name**: FogOfWarSystem
- **Type**: Unity Package (UPM distribution)
- **Platform**: Unity 6+ with URP
- **Language**: C# (ECS/DOTS) + HLSL (Compute Shaders)

## Core Purpose

Real-time SDF-based fog-of-war/visibility system for strategy games. Computes per-group visibility using GPU compute shaders with signed distance fields.

## Technical Stack

| Layer | Technology |
|-------|------------|
| Game Engine | Unity 6+ |
| Rendering | Universal Render Pipeline (URP 17+) |
| ECS Framework | Unity DOTS Entities 1.0+ |
| GPU Compute | Compute Shaders (SM 5.0+) |
| Math | Unity.Mathematics |

## Project Management

| Tool | ID/Location |
|------|-------------|
| HacknPlan Project | 231460 |
| Obsidian Vault | `FogOfWar-Docs/` |
| Source Code | `C:\GitHub\FogOfWarSystem` |

## Key Constraints

### Performance Budget
- GPU frame time: <2ms
- CPU overhead: <0.5ms
- Memory budget: <50MB
- Readback latency: ≤2 frames

### Scale Limits
- Max groups: 8
- Max vision units: 2048
- Max seeable entities: 4096
- Max environment islands: 16

## Package Dependencies

```json
{
  "com.unity.entities": "1.0.0",
  "com.unity.render-pipelines.universal": "17.0.0",
  "com.unity.mathematics": "1.0.0"
}
```

## Design Document Reference

Full technical specification available in Downloads folder:
`sdf_visibility_system_design_doc.md`

## Tag Mapping (HacknPlan)

| Tag | ID | Purpose |
|-----|-----|---------|
| gpu-compute | 1 | GPU compute shader work |
| ecs-dots | 2 | Unity DOTS/ECS systems |
| urp-rendering | 3 | URP rendering integration |
| sdf | 4 | SDF algorithms and data |
| package-manager | 5 | Package distribution |
| documentation | 6 | Docs and guides |
| visibility | 7 | Visibility system core |
| infrastructure | 8 | Project setup/tooling |
