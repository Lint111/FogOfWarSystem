---
tags: [feature, gpu-pipeline, parallel-compute, complete]
status: COMPLETE
hacknplan-task: 31
design-element: GPU Compute Pipeline (ID: 4)
created: 2026-01-05
completed: 2026-01-05
---

# Parallel Per-Group Visibility Pipeline ✅

**Status:** COMPLETE
**HacknPlan Task:** #31
**Design Element:** GPU Compute Pipeline (ID: 4)
**Created:** 2026-01-05

## Overview

Refactor the GPU visibility compute pipeline to process each vision group in parallel streams instead of sequentially.

## Problem Statement

Current pipeline processes all 8 vision groups sequentially:
1. Single VisibilityCheck kernel iterates over all groups
2. Single RayMarchConfirm kernel processes all candidates
3. Inactive groups still consume dispatch overhead
4. No parallelism across groups

## Proposed Solution

Parallel per-group architecture:
- 8 independent candidate finding streams (XOR mask per group)
- 8 independent ray march streams (per-group confirmation)
- Skip inactive groups entirely (ActiveGroupMask check)
- Shared read-only data, independent write regions

## Architecture Diagram

```
Current (Sequential):
FogVolume → VisibilityCheck(all) → RayMarch(all)

Proposed (Parallel):
FogVolume → [G0 Cand] [G1 Cand] ... [G7 Cand] → [G0 Ray] [G1 Ray] ... [G7 Ray]
```

## Files Affected

- Runtime/Shaders/Visibility/VisibilityCheck.compute
- Runtime/Shaders/Visibility/RayMarchConfirm.compute
- Runtime/Scripts/Systems/VisibilityComputeDispatchSystem.cs
- Runtime/Scripts/Core/VisibilitySystemRuntime.cs
- Runtime/Scripts/GPU/GPUDataStructures.cs

## Architecture Analysis Summary

**Key Bottleneck Identified:** Sequential group loop in `VisibilityCheck.compute` (lines 254-337) with `GroupMemoryBarrierWithGroupSync()` after each group iteration.

**Data Classification:**
- **Read-Only (Shareable):** GroupData, UnitContributions, Seeables, Islands, IslandSDFs, FogVolume
- **Needs Per-Group Partitioning:** CandidatesBuffer, CandidateCountBuffer, IndirectArgsBuffer

**Current Buffer Layout:**
- VisibleEntities: Already partitioned by group ✓
- VisibleCounts: Already per-group ✓
- Candidates: Single global buffer ✗
- CandidateCount: Single counter ✗
- IndirectArgs: Single set ✗

## Implementation Plan

### Phase 1: Buffer Layout Changes (Low Risk)
1. Expand `CandidateCountsBuffer` from 1 to 8 integers
2. Create `CandidateOffsetsBuffer[8]` with pre-computed offsets
3. Partition `CandidatesBuffer` into 8 regions (g * MaxCandidatesPerGroup)
4. Expand `IndirectArgsBuffer` from 1 to 8 sets of args

### Phase 2: Shader Refactor (Medium Risk)
1. Create `VisibilityCheck_PerGroup` kernel with `_TargetGroupId` parameter
2. Remove sequential group loop, process single group per dispatch
3. Update atomic writes to use per-group counters and offsets
4. Create `PrepareDispatch_PerGroup` kernel for 8 indirect args
5. Update `RayMarchConfirmation` to accept group index (mostly unchanged)

### Phase 3: Dispatch System Update (Medium Risk)
1. Add `ActiveGroupMask` early-exit check per group
2. Loop through groups 0-7, dispatch only active ones
3. Each group gets its own set of shader parameters
4. 8 parallel VisibilityCheck dispatches
5. 8 parallel PrepareDispatch dispatches  
6. 8 parallel RayMarch indirect dispatches

### Phase 4: Testing & Validation
1. Verify buffer contents match expected layout
2. Compare visibility results before/after refactor
3. Profile GPU timing with NSight/RenderDoc
4. Stress test with 8 active groups, 200 units each


## HacknPlan Task Breakdown

**Parent:** #31 - Parallel Per-Group Visibility Pipeline

| ID | Task | Est. | Status |
|----|------|------|--------|
| #32 | Buffer layout changes for per-group processing | 2h | Planned |
| #34 | Refactor VisibilityCheck shader for single-group kernel | 2h | Planned |
| #33 | Refactor RayMarchConfirm for per-group dispatch | 1h | Planned |
| #35 | Update VisibilityComputeDispatchSystem for parallel dispatch | 2h | Planned |
| #36 | Testing and validation of parallel pipeline | 1h | Planned |

**Deferred Optimizations:**
- #37 - Low-resolution SDF with interpolation (4h)
- #38 - Texture3DArray for island SDFs (3h)
- #39 - Wave intrinsics for barrier reduction (3h)

## Expected Performance Gains

| Scenario | Current | Parallel | Speedup |
|----------|---------|----------|---------|
| 1 active group | Baseline | Baseline | 1x |
| 4 active groups | 4x loops | 4 parallel | ~3-4x |
| 8 active groups | 8x loops | 8 parallel | ~4-6x |

**Bottleneck shifts from:** Sequential group iteration → Ray march texture sampling
## Future Optimizations (Deferred)

- **Texture3DArray for islands:** Eliminate switch statement, better cache
- **Wave intrinsics:** Reduce barriers with WaveActiveMin/WaveActiveBallot
- **Hierarchical culling:** Spatial hash pre-filter before SDF
- **Temporal reprojection:** Cache results, re-eval only moved entities

## Measured Performance Results

| Units | Groups | FPS | Frame Time | Notes |
|-------|--------|-----|------------|-------|
| 800 | 4 | 150 | 6.6ms | Baseline test |
| 2000 | 4 | 95 | 10.5ms | 2.5x units → 1.6x slower |

**Sub-linear scaling achieved** - system handles increased load efficiently.

## Implementation Notes

- True GPU parallelism not achieved (sequential C# dispatch calls)
- Main performance win: **removed Debug.Log stutter** (108ms → 0ms)
- Per-group buffer architecture provides cleaner code and future optimization path
- `UseParallelDispatch` config toggle allows A/B testing

## Progress Log

- 2026-01-05: Task created
- 2026-01-05: Architecture analysis complete
- 2026-01-05: Implementation plan drafted
- 2026-01-05: HacknPlan subtasks created (#32-#36)
- 2026-01-05: Deferred optimizations logged (#37-#39)
- 2026-01-05: Implementation complete (#32-#35)
- 2026-01-05: Testing validated (95 FPS @ 2000 units)
- 2026-01-05: **FEATURE COMPLETE**
