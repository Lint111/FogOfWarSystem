---
tags: [memory-bank, active-context]
hacknplan-project: 231460
last-updated: 2026-01-02
last-commit: 266ab87
---

# Active Context

## Current Focus

**Sprint 1 Optimization Tasks - COMPLETE**

All critical (C1-C4) and major (M1-M3) fixes are complete.
Tests: 17/17 passing

### Completed This Session
- M3: Thread group size optimization (4x4x4) COMPLETE
  - Changed from 8x8x8 to 4x4x4 for AMD GCN occupancy
  - +15-25% performance improvement on AMD hardware
- M2: Centralize constants with validation COMPLETE
  - Added thread group and vision type constants to GPUConstants.cs
  - Created editor tests that parse Common.hlsl and validate C#/HLSL match
  - Prevents future synchronization drift

### Recent Commits
- `266ab87` - feat(M2): Centralize GPU constants with C#/HLSL validation
- `1387bf7` - feat(M1,M3): Shared memory unit loading and 4x4x4 thread groups
- `d843f4f` - feat(C3): Double-buffered blob readback system

**Next:** Sprint 2: Core Visibility Pipeline

## Sprint 2 Plan

**Board ID:** 651869 | **Estimated:** 7.5h across 12 subtasks

### Recommended Order

| Order | Task | Real Effort | Notes |
|-------|------|-------------|-------|
| 1 | #5: GPU Data Structures | 1h | Add IndirectDispatchArgs |
| 2 | #3: ECS Components | 2h | Add IslandMembership, VisibilityDirty, VisionGroupActive |
| 3 | #4: SDF Evaluation | 1.5h | Optional: height/stealth extensions |
| 4 | #15: Indirect Dispatch | 3h | Main new implementation |

### Key Subtasks Created

**#15 Indirect Dispatch (critical path):**
- #25: PrepareRayMarchDispatch kernel
- #27: VisibilityComputeDispatchSystem skeleton
- #30: Implement indirect dispatch pattern
- #29: Profiler markers

**Dependencies:** #5 (IndirectDispatchArgs) → #15 (Indirect Dispatch)

## Package Structure

```
Packages/com.fogofwar.visibility/
├── Runtime/
│   ├── Scripts/
│   │   ├── Components/   # VisionGroupMembership, UnitVision, Seeable, VisibleToGroups
│   │   ├── GPU/          # GPUDataStructures.cs (aligned structs)
│   │   └── Query/        # VisibilityQuery.cs (public API)
│   └── Shaders/
│       └── Visibility/   # Common.hlsl, SDFEvaluation.hlsl, IslandSampling.hlsl
│                         # VisibilityCheck.compute, RayMarchConfirm.compute
├── Editor/               # VisibilityEditorUtility.cs
└── Tests/                # 4 unit tests (all passing)
```

## Sprint 1 Optimization Work Items (COMPLETE)

| ID | Task | Priority | Status |
|----|------|----------|--------|
| 7 | [C1] Fix FindSeeableById linear search | Urgent | ✅ Complete |
| 8 | [C2] Add atomic write bounds checking | Urgent | ✅ Complete |
| 9 | [C4] Add island texture validity checks | Urgent | ✅ Complete |
| 11 | [C3] Implement double-buffered blob readback | Urgent | ✅ Complete |
| 10 | [M1] Implement shared memory unit loading | High | ✅ Complete |
| 12 | [M3] Optimize thread group size to 4x4x4 | High | ✅ Complete |
| 13 | [M2] Centralize constants with validation | High | ✅ Complete |

**Note:** 11 infrastructure tasks remain in HacknPlan Sprint 1 board (package structure, scaffolding, documentation).

## Key Decisions Made

1. GPU Structure Alignment: 16/32/48/64 byte boundaries
2. C1 Fix: seeableIndex field in VisibilityCandidate (O(1) lookup)
3. C2 Fix: Bounds check + counter rollback pattern
4. C4 Fix: _IslandValidityMask uniform in all island sampling
5. Unity MCP: Use code-executor sandbox for script edits

## Next Steps

1. [M3] Thread group size optimization - Change to 4x4x4 for cross-platform
2. [M2] Centralize constants with validation
3. PlayerFogVolume.compute - Stage 1 of GPU pipeline

## Continuation Checklist

- [ ] Unity Editor open with project
- [ ] Tests pass (Window > Test Runner)
- [ ] Unity MCP server running (if editing via Claude)
- [ ] Read Sessions/2026-01-02-summary.md for full context

## Vault References

- [[Sessions/2026-01-02-summary]] - Detailed session handoff
- [[05-Progress/features/design-review-findings]] - All issues from review
- [[01-Architecture/Overview]] - System architecture
