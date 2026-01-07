# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Passive fog dissipation for smoother visibility transitions
- Temporal blending to reduce flickering artifacts
- `DissipatePlayerFogVolume` kernel for frame-rate independent decay (optional, not used by default)
  - Main `GeneratePlayerFogVolume` kernel applies inline dissipation
  - Separate kernel available via `VisibilitySystemRuntime.DissipatePlayerFog()` for standalone decay
- Simplified configuration with `NumberOfGroups` and `EntitiesPerGroup` parameters
- Auto-calculated buffer capacities to prevent overflow
- `ValidateCapacity()` method with editor validation warnings

### Fixed
- **Critical**: Fixed shared memory stale data bug causing stripe artifacts and ghost sight
  - Nearest unit tracking now happens during SDF evaluation, not after
  - Previously only searched last batch in shared memory
- Temporal blending prevents abrupt visibility changes
- Improved artifact handling with passive dissipation

### Changed
- **BREAKING**: Configuration now uses 2 primary parameters instead of 4 manual capacity settings
  - Old serialized fields (`MaxUnitsPerGroup`, `MaxSeeables`, etc.) are now computed properties
  - Existing config assets will need to set new fields: `NumberOfGroups` and `EntitiesPerGroup`
  - Migration: For existing setups, set `EntitiesPerGroup` to 2× your old `MaxUnitsPerGroup` value
- All buffer sizes (MaxUnitsPerGroup, MaxSeeables, etc.) are now auto-derived
- Added OnValidate() to warn about capacity and performance issues in editor
- Temporal blending rates are now configurable (`VisibilityBlendRate`, `PassiveDissipationRate`)

### Initial Release
- Initial package structure and assembly definitions
- Package manifest with dependencies

## [0.1.0] - TBD

### Added
- Core visibility system architecture
- ECS components (VisionGroupMembership, UnitVision, Seeable, VisibleToGroups)
- GPU compute pipeline (3-stage processing)
- Async readback system
- Query API for visibility checks
- Multi-group support (up to 8 groups)
- Multi-island environment support
