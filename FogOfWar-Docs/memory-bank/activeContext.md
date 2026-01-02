---
tags: [memory-bank, active-context]
hacknplan-project: 231460
last-updated: 2026-01-02
last-commit: pending
---

# Active Context

## Current Focus

**Sprint 1 Phase 2 - Critical + Major Fixes Complete**

Session 2026-01-02 (continued):
- C3: Double-buffered blob readback COMPLETE
  - Created `VisibilityReadbackSystem.cs` with async GPU readback
  - Created `VisibilityQueryData.cs` with blob-based singleton components
  - Added `VisibilityReadbackBuffers` managed class for double-buffering
  - Enhanced `VisibilityQuery.cs` with high-performance blob query methods
  - Added 11 new unit tests for readback and query functionality
- M1: Shared memory unit loading COMPLETE
  - Added `groupshared` memory for cooperative unit loading
  - Added `FindNearestUnitShared()` for player group path
  - Added `EvaluateGroupVisionWithSharedMemory()` for AI groups
  - Expected +40-60% memory latency reduction

**Next Priority:** M3 - Thread group size optimization (4x4x4)

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

## Sprint 1 Work Items

| ID | Task | Priority | Status |
|----|------|----------|--------|
| 1 | Create UPM package structure | Normal | Complete |
| 7 | [C1] Fix FindSeeableById linear search | Urgent | Complete |
| 8 | [C2] Add atomic write bounds checking | Urgent | Complete |
| 9 | [C4] Add island texture validity checks | Urgent | Complete |
| 11 | [C3] Implement double-buffered blob readback | Urgent | Complete |
| 10 | [M1] Implement shared memory unit loading | High | Complete |
| 12 | [M3] Optimize thread group size to 4x4x4 | High | NEXT |
| 13 | [M2] Centralize constants with validation | High | Planned |

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
