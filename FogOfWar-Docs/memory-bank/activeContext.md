---
tags: [memory-bank, active-context]
hacknplan-project: 231460
last-updated: 2026-01-02
---

# Active Context

## Current Focus

**Task #1 Complete - UPM Package Structure Created**

Created testable UPM package at `Packages/com.fogofwar.visibility/` with:
- 4 assembly definitions (Runtime, Editor, Tests/Runtime, Tests/Editor)
- Core ECS components (VisionGroupMembership, UnitVision, Seeable, VisibleToGroups)
- Query API scaffold
- Unit tests verifying component behavior
- Full package metadata (package.json, README, CHANGELOG, LICENSE)

## Review Summary

| Reviewer | Key Findings |
|----------|--------------|
| Architecture Critic | 2 critical issues (linear search, race conditions), good foundation |
| Vulkan Expert | Thread group optimization, shared memory, indirect dispatch needed |
| Test Framework QA | CPU mirror strategy for testing, CI/CD pipeline recommendations |
| UI/UX Engineer | Error feedback needed, artist/designer tooling required |

## Critical Issues (Must Fix First)

1. **C1**: `FindSeeableById` O(n) search → use direct index
2. **C2**: Atomic write bounds checking missing
3. **C3**: Per-frame blob allocation → double-buffer
4. **C4**: Island texture validity checks missing

## Sprint 1 Work Items

| ID | Task | Priority | Status |
|----|------|----------|--------|
| 1 | Create UPM package structure | Normal | **Complete** |
| 6 | Design Review Implementation (User Story) | Urgent | Planned |
| 7 | [C1] Fix FindSeeableById linear search | Urgent | Planned |
| 8 | [C2] Add atomic write bounds checking | Urgent | Planned |
| 11 | [C3] Implement double-buffered blob readback | Urgent | Planned |
| 9 | [C4] Add island texture validity checks | Urgent | Planned |
| 10 | [M1] Implement shared memory unit loading | High | Planned |
| 12 | [M3] Optimize thread group size to 4x4x4 | High | Planned |
| 13 | [M2] Centralize constants with validation | High | Planned |

## Key Decisions Made

1. **Thread Group Size**: Change to 4x4x4 (64 threads) for cross-platform
2. **Readback Strategy**: Triple-buffer (write/readback/ready rotation)
3. **Test Strategy**: CPU mirror for SDF functions, GPU tests on self-hosted CI

## Implementation Order

1. **Phase 1 - Critical Fixes** (C1-C4)
2. **Phase 2 - Core Performance** (M1-M3, indirect dispatch)
3. **Phase 3 - DX/UX** (authoring, error feedback)
4. **Phase 4 - Test Infrastructure**

## Next Steps

1. ~~Start with task #1 (UPM package structure)~~ **DONE**
2. Open Unity Editor to verify package compiles
3. Then critical fix C1 (linear search)
4. Address C2-C4 in parallel if possible

## Vault References

- [[05-Progress/features/design-review-findings]] - Full review details
- [[01-Architecture/Overview]] - System architecture
- [[01-Architecture/GPU-Pipeline]] - GPU compute design
