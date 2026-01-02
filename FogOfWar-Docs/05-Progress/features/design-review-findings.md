---
tags: [review, architecture, design]
hacknplan-project: 231460
status: APPROVED WITH CHANGES
---

# Design Document Review Consolidated Findings

## Review Summary

| Reviewer | Focus | Verdict |
|----------|-------|---------|
| Architecture Critic | Feasibility, Scalability | Good foundation, critical fixes needed |
| Vulkan Expert | GPU/Rendering | Solid compute design, optimizations required |
| Test Framework QA | Testability | Mixed - good for queries, challenging for GPU |
| UI/UX Engineer | DX/API | Needs better error feedback and editor tooling |

## Critical Issues (P0 - Must Fix Before Implementation)

### C1. Linear Search in Ray March Shader
**Location**: `RayMarchConfirm.compute`, `FindSeeableById()`
**Problem**: O(n) search per thread with 4096 seeables = 2M+ buffer reads
**Fix**: Pass `seeableIndex` directly in `VisibilityCandidate` struct
**Impact**: +30% ray march performance

### C2. Race Condition in Atomic Writes
**Location**: `VisibilityCheck.compute`, candidate buffer writes
**Problem**: No bounds checking, counter reset race
**Fix**: Add bounds check, explicit memory barriers
**Impact**: Prevents data corruption/crashes

### C3. Per-Frame Blob Allocation
**Location**: `VisibilityReadbackSystem.cs`
**Problem**: Allocates/disposes 128KB blob every frame
**Fix**: Double-buffer pre-allocated blobs
**Impact**: Eliminates GC pressure, meets NF5 requirement

### C4. No GPU-CPU Synchronization for Island Textures
**Location**: `VisibilityComputeDispatchSystem.cs`
**Problem**: Binding mid-upload/destroyed textures
**Fix**: Validity checks, placeholder texture fallback
**Impact**: Prevents crashes during streaming

## Major Issues (P1 - Fix in Sprint 1)

### M1. Non-Coalesced Memory Access in SDF Evaluation
**Fix**: Cooperative loading into shared memory
**Impact**: +40-60% memory latency reduction

### M2. 8-Group Limit Hardcoded Everywhere
**Fix**: Centralize constants with validation
**Impact**: Future scalability

### M3. Thread Group Size Suboptimal
**Current**: 8x8x8 (512 threads)
**Fix**: Use 4x4x4 (64 threads) for cross-platform
**Impact**: +15-25% on AMD GCN

### M4. Missing Island Version Tracking
**Fix**: Add version field, invalidate masks on change
**Impact**: Correct behavior with destructible environments

### M5. Nearest Unit Recalculation in Visibility Check
**Fix**: Precompute in fog volume, store in separate texture
**Impact**: Better player army scaling

### M6. No RenderGraph Support
**Fix**: Migrate to `RecordRenderGraph` API
**Impact**: URP 2023+ compatibility

## Minor Issues (P2 - Backlog)

- N1: VisionGroupData struct alignment wasteful
- N2: Missing SDF formula documentation in enums
- N3: Magic numbers in shaders need constants
- N4: No frustum culling for fog volume
- N5: Unsafe pointer access in Query API

## DX/UX Improvements Needed

### API Improvements
1. Add `VisibilitySystemStatus` singleton with error/warning codes
2. Create `EntityLookup` helper for ID→Entity mapping
3. Add Burst-compatible `NativeSlice` query methods
4. Create `VisibilityValidator` editor tool

### Artist/Designer Experience
1. Add Gizmo drawer for UnitVision preview
2. Create "Bake Island SDF" editor window
3. Add `[Range]` attributes on vision parameters
4. Create `UnitVisionAuthoring` MonoBehaviour

### Documentation Needed
1. "5-Minute Quick Start" tutorial
2. "Hello World" 2-unit setup example
3. "Baking Your First Environment SDF" guide
4. Error message catalog

## Scalability Analysis

| Current | Can Increase To | Limiting Factor |
|---------|-----------------|-----------------|
| 8 groups | 16 groups | Change `byte` masks to `ushort` |
| 2048 units | 4096 units | Memory bandwidth in SDF eval |
| 4096 seeables | 8192 seeables | Spatial partitioning needed |
| 8 islands | 16 islands | Already supported in design |

## Missing Features to Design

1. **Explored Fog** - "previously seen" grayed terrain
2. **Height Advantage** - cliff units see further
3. **Stealth System** - undetectable units
4. **Shared Vision** - allied group merging
5. **Network Sync** - server-authoritative visibility

## Test Strategy Summary

### Unit Testable
- VisibilityQuery static methods
- Bitmask logic
- Island relevance calculation
- SDF primitives (via CPU mirror)

### Requires GPU
- Full pipeline integration
- Performance benchmarks
- Visual regression

### CI/CD Recommendation
- CPU tests: Every commit
- GPU tests: Every PR (self-hosted runner)
- Performance: Main branch only

## Implementation Priority

### Phase 1: Critical Fixes (Before any feature work)
1. Fix `FindSeeableById` linear search
2. Add atomic write bounds checking
3. Implement double-buffered readback
4. Add island texture validity checks

### Phase 2: Core Performance
1. Thread group size optimization (4x4x4)
2. Shared memory unit loading
3. Indirect dispatch

### Phase 3: DX/UX
1. Error feedback system
2. Editor visualization tools
3. Authoring components

### Phase 4: Platform Support
1. RenderGraph migration
2. Mobile configuration path
3. Wave intrinsics (with fallback)
