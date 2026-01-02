---
tags: [index, navigation]
hacknplan-project: 231460
---

# Quick Lookup Index

Master navigation for FogOfWarSystem documentation.

## Project Links

| Resource | Location |
|----------|----------|
| HacknPlan Project | [FogOfWarSystem #231460](https://app.hacknplan.com/p/231460) |
| GitHub Repo | `C:\GitHub\FogOfWarSystem` |
| Design Document | [[01-Architecture/Technical-Design]] |

## Architecture

| Topic | Primary Doc | Code Reference |
|-------|-------------|----------------|
| System Overview | [[01-Architecture/Overview]] | - |
| GPU Compute Pipeline | [[01-Architecture/GPU-Pipeline]] | `Shaders/Visibility/` |
| ECS Components | [[01-Architecture/ECS-Components]] | `Runtime/Scripts/Components/` |
| SDF Evaluation | [[01-Architecture/SDF-System]] | `SDFEvaluation.hlsl` |
| Environment Islands | [[01-Architecture/Islands]] | `IslandSampling.hlsl` |
| Query API | [[01-Architecture/Query-API]] | `Runtime/Scripts/Query/` |

## Implementation

| Topic | Doc |
|-------|-----|
| Package Setup | [[02-Implementation/Package-Structure]] |
| Compute Shaders | [[02-Implementation/Compute-Shaders]] |
| URP Integration | [[02-Implementation/URP-Integration]] |
| SDF Baking | [[02-Implementation/SDF-Baking]] |

## Memory Bank

| Context | File |
|---------|------|
| Project Context | [[memory-bank/projectContext]] |
| Active Context | [[memory-bank/activeContext]] |
| Decision Log | [[memory-bank/decisionLog]] |
| Progress | [[memory-bank/progress]] |

## Tags Reference

| Tag | HacknPlan ID | Usage |
|-----|--------------|-------|
| gpu-compute | 1 | GPU compute shader work |
| ecs-dots | 2 | Unity DOTS/ECS systems |
| urp-rendering | 3 | URP rendering integration |
| sdf | 4 | SDF algorithms and data |
| package-manager | 5 | Package distribution |
| documentation | 6 | Docs and guides |
| visibility | 7 | Visibility system core |
| infrastructure | 8 | Project setup/tooling |
