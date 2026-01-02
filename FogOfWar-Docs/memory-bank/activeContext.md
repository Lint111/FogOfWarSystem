---
tags: [memory-bank, active-context]
hacknplan-project: 231460
last-updated: 2026-01-02
last-commit: d5f5a67
---

# Active Context

## Current Focus

**Sprint 1 Phase 1 Complete - Critical Fixes Implemented**

Session 2026-01-02 completed:
- UPM package structure created
- C1: FindSeeableById O(1) lookup via seeableIndex
- C2: Atomic write bounds checking with rollback
- C4: Island texture validity mask

**Next Priority:** C3 - Double-buffered blob readback

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
| 11 | [C3] Implement double-buffered blob readback | Urgent | NEXT |
| 10 | [M1] Implement shared memory unit loading | High | Planned |
| 12 | [M3] Optimize thread group size to 4x4x4 | High | Planned |
| 13 | [M2] Centralize constants with validation | High | Planned |

## Key Decisions Made

1. GPU Structure Alignment: 16/32/48/64 byte boundaries
2. C1 Fix: seeableIndex field in VisibilityCandidate (O(1) lookup)
3. C2 Fix: Bounds check + counter rollback pattern
4. C4 Fix: _IslandValidityMask uniform in all island sampling
5. Unity MCP: Use code-executor sandbox for script edits

## Next Steps

1. [C3] Double-buffered blob readback - Create VisibilityReadbackSystem.cs
2. PlayerFogVolume.compute - Stage 1 of GPU pipeline
3. ECS Systems - Data collection, compute dispatch, readback processing

## Continuation Checklist

- [ ] Unity Editor open with project
- [ ] Tests pass (Window > Test Runner)
- [ ] Unity MCP server running (if editing via Claude)
- [ ] Read Sessions/2026-01-02-summary.md for full context

## Vault References

- [[Sessions/2026-01-02-summary]] - Detailed session handoff
- [[05-Progress/features/design-review-findings]] - All issues from review
- [[01-Architecture/Overview]] - System architecture
