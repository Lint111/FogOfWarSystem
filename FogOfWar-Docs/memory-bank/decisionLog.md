---
tags: [memory-bank, decisions]
hacknplan-project: 231460
---

# Decision Log

Architectural and design decisions with rationale.

## Template

```markdown
### [DECISION-XXX] Title
**Date**: YYYY-MM-DD
**Status**: Proposed | Accepted | Deprecated
**Context**: Why this decision was needed
**Decision**: What was decided
**Rationale**: Why this option was chosen
**Alternatives Considered**: Other options evaluated
**Consequences**: Impact of this decision
```

---

## Decisions

### [DECISION-001] Unity Package Distribution
**Date**: 2026-01-02
**Status**: Accepted
**Context**: Need to determine how the visibility system will be distributed to users.
**Decision**: Distribute as Unity Package Manager (UPM) package.
**Rationale**: 
- Standard Unity distribution method
- Easy version management
- Can be hosted on Git or custom registry
- Familiar to Unity developers
**Alternatives Considered**:
- Asset Store package (more friction, revenue share)
- Source include (no version management)
**Consequences**: Must follow UPM package structure conventions.

### [DECISION-002] GPU Compute Pipeline Architecture
**Date**: 2026-01-02
**Status**: Accepted
**Context**: Need efficient visibility computation for many entities across multiple groups.
**Decision**: 3-stage GPU compute pipeline with async readback.
**Rationale**:
- Stage 1 (Fog Volume): Enables fast player visibility sampling
- Stage 2 (Visibility Check): Broad phase reduces ray march candidates
- Stage 3 (Ray March): Accurate occlusion only for candidates
- Async readback: Hides latency, doesn't stall CPU
**Alternatives Considered**:
- CPU-only raycasting (too slow for scale)
- Single-pass GPU (too complex, less efficient)
**Consequences**: 1-2 frame latency acceptable for gameplay systems.

### [DECISION-003] Multi-Island Environment Support
**Date**: 2026-01-02
**Status**: Accepted
**Context**: Game worlds may have disconnected areas (islands, floors, zones).
**Decision**: Support up to 16 separate baked SDF volumes with cross-island visibility.
**Rationale**:
- Avoids single massive SDF for entire world
- Enables streaming/LOD per island
- Cross-island visibility handled via ray-AABB intersection
**Alternatives Considered**:
- Single world SDF (memory prohibitive)
- No cross-island visibility (limiting for gameplay)
**Consequences**: Slightly more complex ray marching, but better scalability.
