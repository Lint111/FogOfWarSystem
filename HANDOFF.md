# Session Handoff - Visibility POC Setup

## Session Date: 2026-01-02

## Summary
Setting up POC test scene for the visibility pipeline. Scene infrastructure is complete but compute shader kernel errors need resolution.

---

## Completed Work

### 1. VisibilityShaderConfig Asset
- **Created**: `Assets/Configuration/VisibilityShaderConfig.asset`
- **Shaders assigned** (via direct .asset file edit):
  - PlayerFogVolumeShader: `ff6c147ea7bdacc43926a938b69baf23`
  - VisibilityCheckShader: `b799f642d945ced4d9caf84238105ad8`
  - RayMarchConfirmShader: `11ac4607533eb1c44a46376b35ff85b5`

### 2. POC Test Scene
- **Scene**: `Assets/Scenes/VisibilityPOC.unity`
- **Contents**:
  - Main Camera (position: 0,10,-15, rotation: 35,0,0)
    - Camera component
    - AudioListener
    - FogVolumeVisualizer component
  - Directional Light (type set to Directional)
  - Ground (Plane, scale 5x5)
  - VisibilityEntities (parent object) containing:
    - PlayerUnit1 (Capsule @ -5,1,0) with UnitVisionAuthoring (Group 0)
    - PlayerUnit2 (Capsule @ 5,1,0) with UnitVisionAuthoring (Group 0)
    - EnemyTarget1 (Cube @ 0,0.5,8) with SeeableAuthoring (Group 1)
    - EnemyTarget2 (Cube @ 10,0.5,10) with SeeableAuthoring (Group 1)

### 3. Bootstrap Component Created
- **File**: `Packages/com.fogofwar.visibility/Runtime/Scripts/Configuration/VisibilityConfigBootstrap.cs`
- Registers shader config at runtime via `Awake()`
- **NOT YET ADDED TO SCENE** - needs to be added and config assigned

### 4. Improved Error Handling
- Updated `VisibilityComputeDispatchSystem.cs` (lines 122-168)
- Added try-catch around FindKernel calls
- Better error messages for shader compilation failures

---

## Remaining Work

### Fixed: Compute Shader Errors (Session 2)

**Issues Found & Fixed**:

1. **IslandSampling.hlsl** - Mixed line endings (Unix/Windows)
   - Rewrote file with consistent line endings
   - Fixed `AddressW = clamp` → `AddressW = Clamp` (case sensitivity)

2. **PlayerFogVolume.compute** - Thread sync in varying flow control
   - Error: "thread sync operation must be in non-varying flow control"
   - Root cause: Early `return` statements before `GroupMemoryBarrierWithGroupSync()`
   - Fix: Removed early returns, all threads reach barriers, conditional logic guards output

3. **VisibilityCheck.compute** - Shared memory design flaw
   - Each thread processes different seeable, can't cooperate on shared memory
   - Removed shared memory optimization, uses simple per-thread approach
   - No barriers needed since threads work independently

4. **VisibilityComputeDispatchSystem.cs** - Added kernel validation
   - Added try-catch around `FindKernel` calls
   - Better error messages for shader compilation failures

### Required: Convert VisibilityEntities to Subscene
- The `VisibilityEntities` GameObject needs to be converted to a subscene for ECS baking
- Unity MCP doesn't support subscene creation
- **Manual step**: Right-click `VisibilityEntities` in Hierarchy > New Sub Scene From Selection

### Required: Add Bootstrap to Scene
1. Create empty GameObject "VisibilityBootstrap"
2. Add `VisibilityConfigBootstrap` component
3. Assign `Assets/Configuration/VisibilityShaderConfig.asset` to ShaderConfig field

### Testing
Once shaders work:
1. Enter Play mode
2. Press F1 to toggle FogVolumeVisualizer
3. Use PageUp/PageDown to adjust slice height
4. Verify visibility detection between units

---

## File Locations

| Item | Path |
|------|------|
| Shader Config | `Assets/Configuration/VisibilityShaderConfig.asset` |
| POC Scene | `Assets/Scenes/VisibilityPOC.unity` |
| Bootstrap Component | `Packages/.../Configuration/VisibilityConfigBootstrap.cs` |
| Dispatch System | `Packages/.../Systems/VisibilityComputeDispatchSystem.cs` |
| Compute Shaders | `Packages/.../Shaders/Visibility/*.compute` |
| HLSL Includes | `Packages/.../Shaders/Visibility/*.hlsl` |

---

## Console Status at Handoff

**Shader errors FIXED** - The following issues were resolved:
- Thread sync in varying flow control (PlayerFogVolume.compute)
- Mixed line endings (IslandSampling.hlsl)
- Shared memory misuse in 1D kernel (VisibilityCheck.compute)

**Verify after Unity recompiles** - Shaders should compile without errors.

Todos
  ☒ Create VisibilityShaderConfig asset
  ☒ Create POC test scene with camera/light/ground
  ☒ Add UnitVisionAuthoring GameObjects (Group 0)
  ☒ Add SeeableAuthoring GameObjects (Group 1)
  ☒ Add FogVolumeVisualizer to camera
  ☐ Fix compute shader kernel errors
  ☐ User: Convert VisibilityEntities to subscene
  ☐ Test play mode

---

## Tests Status
22/22 passing (unit tests only - no integration tests yet)
