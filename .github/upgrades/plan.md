# .NET 8.0 Upgrade Plan - Lisp Machine Emulator

## Table of Contents

- [Executive Summary](#executive-summary)
- [Migration Strategy](#migration-strategy)
- [Detailed Dependency Analysis](#detailed-dependency-analysis)
- [Project-by-Project Plans](#project-by-project-plans)
  - [SDL2-CS.csproj](#sdl2-cscsproj)
- [Risk Management](#risk-management)
- [Testing & Validation Strategy](#testing--validation-strategy)
- [Complexity & Effort Assessment](#complexity--effort-assessment)
- [Source Control Strategy](#source-control-strategy)
- [Success Criteria](#success-criteria)

---

## Executive Summary

### Scenario Description
Upgrade the Lisp Machine Emulator solution from mixed .NET Framework/.NET 8.0 to uniform .NET 8.0 (LTS) targeting. The solution consists of 4 projects, with 3 already on .NET 8.0 and one legacy project (SDL2-CS) on .NET Framework 4.0 that requires modernization.

### Scope

**Projects Affected**: 1 out of 4
- **SDL2-CS.csproj**: .NET Framework 4.0 ? .NET 8.0 (requires SDK-style conversion)

**Already Compatible** (No changes required):
- **Chaos.csproj**: .NET 8.0 ?
- **Usim.csproj**: .NET 8.0 ?
- **UsimTools.csproj**: .NET 8.0 ?

**Current State**:
- Total Projects: 4
- Total NuGet Packages: 1 (System.CommandLine - already compatible)
- Total Lines of Code: 20,264
- SDL2-CS Project: 11,105 LOC across 5 files

**Target State**: All projects targeting .NET 8.0 (LTS)

### Selected Strategy

**All-at-Once Strategy** - Single atomic operation to convert and upgrade SDL2-CS project.

**Rationale**:
- **Single project scope**: Only SDL2-CS requires changes
- **Leaf dependency**: SDL2-CS has no dependencies of its own, only consumed by Usim
- **Simple solution**: 4 projects with straightforward dependency structure
- **No compatibility issues**: All 6,023 analyzed APIs are compatible with .NET 8.0
- **No package updates**: Single NuGet package (System.CommandLine) already compatible
- **Well-isolated change**: 3 projects already on .NET 8.0 provide safety net for testing

### Discovered Metrics

| Metric | Value | Assessment |
|--------|-------|------------|
| **Complexity Classification** | Simple | Single leaf project, no cascading impacts |
| **Projects Requiring Upgrade** | 1 of 4 | 75% already modernized |
| **Dependency Depth** | 1 level | Minimal transitive impact |
| **Security Vulnerabilities** | 0 | No urgent security concerns |
| **Package Updates Required** | 0 | All packages compatible |
| **API Breaking Changes** | 0 | No code modifications expected |
| **Risk Level** | Low | Standard SDK-style conversion |

### Critical Issues

**None** - This is a straightforward modernization:
- No security vulnerabilities detected
- No incompatible packages
- No binary or source incompatible APIs
- Standard SDK-style conversion pattern applies

### Recommended Approach

**Fast-track Migration**: Complete upgrade in single atomic operation consisting of:
1. Convert SDL2-CS.csproj to SDK-style format
2. Update TargetFramework to net8.0
3. Build entire solution to verify compatibility
4. Run existing tests to validate functionality

**Expected Timeline**: Single development session (1-2 hours)

### Iteration Strategy

**Fast Batch Approach** (2-3 detail iterations):
- Phase 1: Foundation sections (strategy, dependency analysis, risk assessment)
- Phase 2: SDL2-CS project detailed plan
- Phase 3: Testing, source control, and success criteria

Minimal iteration required due to simple scope and low complexity.

---

## Migration Strategy

### Approach Selection

**All-at-Once Strategy** - Complete SDL2-CS upgrade in single atomic operation.

### Justification

**Why All-at-Once**:

1. **Single Project Scope**
   - Only 1 of 4 projects requires changes
   - SDL2-CS is self-contained P/Invoke bindings library
   - No need for incremental phasing

2. **Leaf Dependency Position**
   - SDL2-CS has no dependencies of its own
   - Only consumed by Usim (already on .NET 8.0)
   - Upgrade can be validated immediately without waiting for downstream upgrades

3. **Low Complexity**
   - Standard SDK-style conversion pattern
   - No custom MSBuild logic or complex project structure
   - All APIs already compatible (6,023 analyzed)
   - No package updates required

4. **High Compatibility**
   - 75% of solution already on .NET 8.0
   - Consumer project (Usim) provides immediate integration test
   - No breaking changes detected in analysis

5. **Minimal Risk**
   - No security vulnerabilities to address urgently
   - No package compatibility issues
   - Well-understood conversion process
   - Easy rollback via source control

### All-at-Once Strategy Rationale

This upgrade is an ideal candidate for All-at-Once approach:

- **Fast completion**: Single development session
- **No multi-targeting complexity**: Direct net40 ? net8.0 transition
- **Atomic verification**: Build entire solution immediately after changes
- **Clean dependency resolution**: No intermediate states required

### Dependency-Based Ordering

**Order Principle**: Upgrade dependencies before dependants (bottom-up).

**Application to This Solution**:

```
Phase 1: Atomic Upgrade (Single Operation)
??? SDL2-CS.csproj (leaf node)
    ??? Convert to SDK-style project format
    ??? Update TargetFramework: net40 ? net8.0
    ??? Build and verify

Phase 2: Validation (Automatic)
??? Build entire solution
    ??? Usim.csproj automatically consumes upgraded SDL2-CS
```

**Why This Order**:
- SDL2-CS is a leaf (no dependencies) - can upgrade independently
- Usim depends on SDL2-CS - will automatically benefit from upgrade
- No circular dependencies or complex ordering required

### Parallel vs Sequential Execution

**Not Applicable** - Single project upgrade means no parallel vs sequential decision needed.

All operations on SDL2-CS can be performed atomically:
1. SDK-style conversion
2. TargetFramework update
3. Build verification

These are coupled operations that must happen together.

### Phase Definitions

**Phase 1: Atomic Upgrade**
- **Scope**: SDL2-CS.csproj only
- **Operations**: SDK conversion + TFM update
- **Validation**: Project builds successfully
- **Success Criteria**: No compilation errors

**Phase 2: Solution Validation**
- **Scope**: Entire solution (all 4 projects)
- **Operations**: Full solution build
- **Validation**: All projects build, Usim runs
- **Success Criteria**: Zero errors, functional emulator

### All-at-Once Strategy Principles

**Key Principles Applied**:

1. **Simultaneity**: All SDL2-CS changes applied together (SDK format + TFM)
2. **Atomic Validation**: Immediate build verification after changes
3. **No Intermediate States**: Direct transition from net40 to net8.0
4. **Unified Build**: Solution builds as complete unit after upgrade

**Risk Management**:
- Single changeset enables easy rollback if issues arise
- Git branch (`upgrade-to-NET8`) isolates changes
- Consumer project (Usim) already on .NET 8.0 reduces integration risk

### Migration Execution Pattern

```
Start State:
  SDL2-CS: net40, classic project format
  Usim: net8.0, SDK-style (consumes SDL2-CS)

Atomic Operation:
  1. Convert SDL2-CS.csproj to SDK-style
  2. Update <TargetFramework>net40</TargetFramework> ? <TargetFramework>net8.0</TargetFramework>
  3. Build SDL2-CS project
  4. Build entire solution

End State:
  SDL2-CS: net8.0, SDK-style
  Usim: net8.0, SDK-style (consumes upgraded SDL2-CS)

Validation:
  - All projects build without errors
  - Usim emulator runs successfully
  - No runtime errors from SDL2 P/Invoke bindings
```

### Expected Outcome

After atomic upgrade operation:
- SDL2-CS targets .NET 8.0
- All 4 projects target .NET 8.0
- Solution is fully modernized
- No code changes required (only project file format/TFM)
- Ready for .NET 8.0 features and performance improvements

---

## Detailed Dependency Analysis

### Dependency Graph Summary

The solution has a simple, clean dependency structure with minimal complexity:

```
???????????????????
? UsimTools.csproj?  (Standalone tool - no dependencies)
?    [net8.0]     ?
???????????????????

???????????????????
?  Usim.csproj    ?  (Main application)
?    [net8.0]     ?
???????????????????
         ?
    ?????????????????????????
    ?                       ?
    ?                       ?
???????????????     ????????????????
?Chaos.csproj ?     ?SDL2-CS.csproj?  ?? REQUIRES UPGRADE
?  [net8.0]   ?     ?   [net40]    ?
???????????????     ????????????????
    (No deps)           (No deps)
```

### Project Groupings by Migration Phase

**Phase 0: Already Complete** (No action required)
- Chaos.csproj (net8.0) - Emulator chaos mode library
- Usim.csproj (net8.0) - Main emulator application
- UsimTools.csproj (net8.0) - Disk/loader tools

**Phase 1: Atomic Upgrade** (Single operation)
- SDL2-CS.csproj (net40 ? net8.0) - SDL2 P/Invoke bindings

### Critical Path Identification

**Single Critical Path**: SDL2-CS ? Usim

1. **SDL2-CS.csproj** must be upgraded first (leaf dependency)
   - Currently: .NET Framework 4.0
   - Target: .NET 8.0
   - No dependencies - can be upgraded independently
   - Single consumer (Usim) already on .NET 8.0

2. **Usim.csproj** automatically benefits once SDL2-CS upgraded
   - Already on .NET 8.0
   - Will consume upgraded SDL2-CS without modifications
   - No changes required to Usim itself

**Dependency Ordering Rationale**:
- SDL2-CS is a **leaf node** (no transitive dependencies)
- SDL2-CS upgrade has **zero impact** on other projects during migration
- Usim already targets .NET 8.0, so it can consume either net40 or net8.0 SDL2-CS builds
- No multi-targeting or intermediate states required

### Circular Dependencies

**None** - Dependency graph is acyclic and simple.

### Migration Order

**Single-Phase Approach**:

Since only one project requires changes and it's a leaf dependency:

1. **Convert SDL2-CS to SDK-style** (single operation)
2. **Update TargetFramework to net8.0** (same operation)
3. **Build entire solution** (validates compatibility)
4. **Run tests** (validates functionality)

No incremental phasing required - all changes can be applied atomically.

### Dependencies Impact Matrix

| Project | Current TFM | Target TFM | Dependencies | Dependants | Impact |
|---------|-------------|------------|--------------|------------|--------|
| SDL2-CS.csproj | net40 | net8.0 | 0 | 1 (Usim) | **Upgrade Required** - SDK conversion + TFM update |
| Chaos.csproj | net8.0 | net8.0 | 0 | 1 (Usim) | No changes |
| Usim.csproj | net8.0 | net8.0 | 2 | 0 | No changes - consumes upgraded SDL2-CS |
| UsimTools.csproj | net8.0 | net8.0 | 0 | 0 | No changes |

### Risk Factors

**Low Risk**:
- Single project upgrade minimizes change surface
- Leaf dependency position prevents cascading failures
- All consuming projects already on target framework
- No package compatibility concerns
- No API breaking changes detected (6,023 APIs analyzed, all compatible)

---

## Project-by-Project Plans

### SDL2-CS.csproj

**Current State**: 
- Target Framework: .NET Framework 4.0
- Project Format: Classic (non-SDK-style)
- Project Kind: Class Library
- Dependencies: 0
- Dependants: 1 (Usim.csproj)
- Number of Files: 5
- Lines of Code: 11,105
- NuGet Packages: 0
- Risk Level: Low

**Target State**:
- Target Framework: .NET 8.0
- Project Format: SDK-style
- All existing functionality preserved

#### Migration Steps

**1. Prerequisites**

- ? .NET 8.0 SDK installed (verified during analysis)
- ? Git branch `upgrade-to-NET8` created and checked out
- ? Pending changes committed
- ? Assessment complete, no blocking issues identified

**2. Technology/Framework Update**

Convert SDL2-CS.csproj from classic format to SDK-style format and update target framework.

**Current project structure** (classic format):
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="..." DefaultTargets="Build" ...>
  <PropertyGroup>
    <TargetFrameworkVersion>v4.0</TargetFrameworkVersion>
    <!-- Classic project properties -->
  </PropertyGroup>
  <ItemGroup>
    <!-- Explicit file references -->
    <Compile Include="src\SDL2.cs" />
    <Compile Include="src\SDL2_gfx.cs" />
    <!-- ... -->
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

**Target project structure** (SDK-style):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>
</Project>
```

**Conversion approach**: Use automated SDK-style conversion tool, then verify and adjust.

**3. Package/Module/Dependency Updates**

| Package Name | Current Version | Target Version | Reason |
|-------------|----------------|----------------|--------|
| *(No packages)* | - | - | SDL2-CS is a pure P/Invoke bindings library with no NuGet dependencies |

**Note**: SDL2-CS only references native SDL2.dll through P/Invoke - no managed dependencies.

**4. Expected Breaking Changes**

**None expected** - This is an exceptional case:

**Why No Breaking Changes**:
- **P/Invoke signatures are framework-agnostic**: DllImport declarations work identically across .NET Framework 4.0 and .NET 8.0
- **All 6,023 APIs analyzed are compatible**: Assessment confirmed zero binary or source incompatibilities
- **No deprecated APIs used**: SDL2-CS is a thin binding layer over native SDL2 library
- **No framework-specific features**: No dependencies on Windows-only or .NET Framework-specific APIs

**Verified Compatible Patterns**:
- `[DllImport("SDL2.dll")]` attributes
- `IntPtr`, `uint`, `byte` marshaling
- Unsafe code blocks (`unsafe`, pointers)
- Struct layouts (`[StructLayout(LayoutKind.Sequential)]`)
- Callback delegates
- Static extern method declarations

**Potential Minor Adjustments** (likely automatic):
- Project may need `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in SDK-style format
- May need `<GenerateAssemblyInfo>false</AllowUnsafeBlocks>` if AssemblyInfo.cs exists

**5. Code Modifications**

**Expected: Zero code changes**

**Source files** (expected to work without modification):
- `src\SDL2.cs` - Core SDL2 bindings
- `src\SDL2_gfx.cs` - SDL2_gfx library bindings
- `src\SDL2_image.cs` - SDL2_image library bindings (if present)
- `src\SDL2_mixer.cs` - SDL2_mixer library bindings (if present)
- `src\SDL2_ttf.cs` - SDL2_ttf library bindings (if present)

**Why no changes needed**:
- P/Invoke declarations are identical across frameworks
- Unsafe code supported in .NET 8.0 with `<AllowUnsafeBlocks>true`
- No obsolete APIs in use
- No configuration or build logic changes required

**Areas to review** (verification only, changes unlikely):
- AssemblyInfo.cs - May be excluded in SDK-style (automatic generation)
- Native library loading paths - Should work identically
- Callback delegates - Fully supported in .NET 8.0

**6. Testing Strategy**

**Unit Tests**:
- SDL2-CS itself has no unit tests (P/Invoke bindings library)
- Validation occurs through consumer (Usim.csproj)

**Integration Tests**:
- **Primary validation**: Usim.csproj application
  - SDL2 window creation
  - SDL2 renderer initialization
  - SDL2 event handling (keyboard, mouse)
  - SDL2 audio subsystem
  - SDL2 texture rendering

**Smoke Tests**:
1. Build SDL2-CS.csproj successfully
2. Build Usim.csproj (consumes SDL2-CS)
3. Run Usim emulator
4. Verify SDL2 window appears
5. Test keyboard input
6. Test mouse input
7. Verify display rendering
8. Test audio beep functionality

**Performance Tests**:
- Compare frame rates before/after upgrade
- Verify no P/Invoke marshaling overhead introduced
- Monitor emulator responsiveness

**Manual Validation**:
- Launch Usim emulator
- Observe SDL2 window creation
- Interact with keyboard
- Interact with mouse
- Verify no crashes or errors in SDL2 subsystem

**7. Validation Checklist**

- [ ] SDL2-CS.csproj builds without errors
- [ ] SDL2-CS.csproj builds without warnings
- [ ] Usim.csproj builds successfully (consumes upgraded SDL2-CS)
- [ ] Entire solution builds without errors
- [ ] Usim emulator launches successfully
- [ ] SDL2 window appears correctly
- [ ] Keyboard input works
- [ ] Mouse input works
- [ ] Display rendering functions correctly
- [ ] Audio beep works
- [ ] No runtime P/Invoke errors
- [ ] No performance regression
- [ ] Native SDL2.dll loads successfully

#### Special Considerations

**SDK-Style Project Benefits**:
- Simpler project file (no explicit file listings)
- Automatic file globbing (`**/*.cs`)
- Modern MSBuild features
- Easier maintenance
- Better tooling support

**P/Invoke Compatibility**:
- P/Invoke is fully supported in .NET 8.0
- `System.Runtime.InteropServices` namespace unchanged
- DllImport attribute semantics identical
- Marshaling behavior consistent

**Native Library Loading**:
- SDL2.dll must be in application directory or PATH
- Same loading mechanism as .NET Framework 4.0
- No changes to native dependency distribution required

**Unsafe Code**:
- SDL2 bindings use `unsafe` for pointer operations
- Fully supported in .NET 8.0 with `<AllowUnsafeBlocks>true`
- No behavioral changes expected

**Consumer Impact**:
- Usim.csproj already on .NET 8.0 - seamless consumption
- No API changes to SDL2-CS public surface
- Binary compatibility maintained
- Reference update automatic after build

---

## Testing & Validation Strategy

### Multi-Level Testing Approach

#### Level 1: Project-Level Validation

**After SDL2-CS Conversion**:

? **Compilation Validation**
- SDL2-CS.csproj builds successfully
- Zero compilation errors
- Zero warnings (or only expected warnings)
- Output assembly generated

? **Project File Validation**
- SDK-style format correct
- TargetFramework set to net8.0
- AllowUnsafeBlocks enabled
- No syntax errors in project file

? **File Structure Validation**
- All source files included (automatic globbing)
- No missing files
- No excluded files inappropriately

**Success Criteria**: SDL2-CS.csproj builds cleanly to `bin/Debug/net8.0/SDL2-CS.dll`

#### Level 2: Solution-Level Validation

**After SDL2-CS Upgrade Complete**:

? **Full Solution Build**
- All 4 projects build successfully
- No inter-project reference errors
- Usim.csproj successfully references upgraded SDL2-CS.dll
- UsimTools.csproj and Chaos.csproj unaffected

? **Dependency Resolution**
- Usim correctly loads net8.0 SDL2-CS assembly
- No version conflicts
- No missing dependencies

? **Assembly Compatibility**
- Usim.exe runs without FileNotFoundException
- No assembly binding failures
- No type load exceptions

**Success Criteria**: `dotnet build LispMachine.sln` completes with 0 errors

#### Level 3: Functional Validation

**After Solution Build Success**:

? **Smoke Tests** (Quick validation)
1. **Launch Test**: Usim.exe starts without crashing
2. **SDL2 Initialization**: SDL window appears
3. **Display Test**: Window renders correctly
4. **Input Test**: Keyboard responds to input
5. **Mouse Test**: Mouse cursor tracks correctly
6. **Audio Test**: Beep function produces sound

? **Integration Tests** (SDL2 Subsystems)
- SDL_Init succeeds for VIDEO, AUDIO, EVENTS
- SDL_CreateWindowAndRenderer succeeds
- SDL_CreateTexture succeeds
- SDL_PollEvent succeeds
- SDL_OpenAudioDevice succeeds
- SDL_GetError returns no errors

? **Runtime Stability**
- No crashes during normal operation
- No memory access violations
- No P/Invoke marshaling errors
- No native interop failures

**Success Criteria**: Usim emulator runs for at least 5 minutes without errors

#### Level 4: Regression Testing

**Compare with Previous Version**:

? **Functional Parity**
- All emulator features work identically
- No missing functionality
- No changed behavior (unless intentional .NET 8.0 improvements)

? **Performance Parity**
- Frame rate comparable or better
- CPU usage comparable or better
- Memory usage comparable or better
- Startup time comparable or better

? **Compatibility Parity**
- Loads same disk images
- Runs same microcode
- Displays same output
- Processes same input

**Success Criteria**: No functional or performance regressions detected

### Phase-by-Phase Testing Requirements

**Phase 1: Atomic Upgrade**

| Test Type | Scope | Expected Result |
|-----------|-------|-----------------|
| **Build Test** | SDL2-CS.csproj | Builds without errors |
| **Format Test** | SDL2-CS.csproj file | Valid SDK-style syntax |
| **TFM Test** | Assembly metadata | Reports net8.0 target |

**Phase 2: Solution Validation**

| Test Type | Scope | Expected Result |
|-----------|-------|-----------------|
| **Build Test** | LispMachine.sln | All projects build |
| **Reference Test** | Usim ? SDL2-CS | Reference resolves |
| **Launch Test** | Usim.exe | Application starts |
| **Functional Test** | SDL2 subsystems | All subsystems initialize |

### Smoke Tests (Quick Validation)

**Execute After Each Phase**:

```bash
# Phase 1 Smoke Test
cd SDL2-CS
dotnet build
# Expected: Build succeeded. 0 Error(s)

# Phase 2 Smoke Test
cd ..
dotnet build LispMachine.sln
# Expected: Build succeeded. 0 Error(s)

# Phase 3 Smoke Test
cd usim-cs
dotnet run -- -config ../config/usim.ini
# Expected: Emulator window appears, responds to input
```

**Validation Checklist**:
- [ ] SDL2-CS builds in < 10 seconds
- [ ] Solution builds in < 30 seconds
- [ ] Usim launches in < 5 seconds
- [ ] SDL2 window appears
- [ ] Display renders correctly
- [ ] Keyboard input works
- [ ] Mouse input works
- [ ] No console errors

### Comprehensive Validation (Before Completion)

**Full Test Suite**:

1. **Compilation Tests**
   - Clean build from scratch
   - Incremental build
   - Rebuild solution
   - Release configuration build

2. **Runtime Tests**
   - Cold start (no previous run)
   - Warm start (after previous run)
   - Load disk image
   - Execute microcode instructions
   - Process keyboard events
   - Process mouse events
   - Render display updates
   - Generate audio beeps

3. **Stability Tests**
   - Run for extended period (30+ minutes)
   - Execute intensive operations
   - Monitor for memory leaks
   - Monitor for handle leaks
   - Verify clean shutdown

4. **Platform Tests** (if applicable)
   - Windows x64
   - Windows ARM64 (if .NET 8.0 used for ARM support)
   - Verify native SDL2.dll loading on all platforms

### Test Projects

**Existing Test Projects**:
- No dedicated SDL2-CS test project
- Validation through Usim.csproj consumer

**Test Execution**:
```bash
# Build all projects
dotnet build LispMachine.sln

# Run UsimTools tests (if any)
dotnet test usim-cs-tools/UsimTools.csproj

# Manual Usim validation
cd usim-cs
dotnet run

# Observe:
# - SDL2 window creation
# - Display rendering
# - Keyboard input
# - Mouse input
# - Audio output
```

### Validation Success Criteria

**Project-Level**:
- ? SDL2-CS.csproj builds without errors or warnings
- ? Output assembly targets net8.0
- ? SDK-style project format valid

**Solution-Level**:
- ? All 4 projects build successfully
- ? No dependency resolution errors
- ? Usim correctly references upgraded SDL2-CS

**Functional-Level**:
- ? Usim emulator launches
- ? SDL2 window appears and renders
- ? Input devices respond correctly
- ? Audio subsystem functions
- ? No runtime errors or crashes

**Regression-Level**:
- ? No functional regressions
- ? No performance degradation
- ? All existing features work identically

### Failure Handling

**If Build Fails**:
1. Review conversion tool output
2. Check project file syntax
3. Compare with other SDK-style projects (Chaos, Usim, UsimTools)
4. Manually adjust properties if needed
5. Rollback via Git if unresolvable

**If Runtime Fails**:
1. Check SDL2.dll native library presence
2. Verify DllImport paths
3. Check for assembly binding errors
4. Compare P/Invoke signatures with .NET Framework 4.0 version
5. Test on clean machine if needed

**If Regressions Detected**:
1. Identify specific failing scenario
2. Compare behavior with net40 version
3. Check for .NET 8.0 behavioral changes
4. File issue if .NET 8.0 regression discovered
5. Implement workaround or consider rollback

---

## Source Control Strategy

### Branching Strategy

**Main Branch**: `main`
- Stable production code
- Currently on commit: Initial C# port commit
- .NET Framework 4.0 SDL2-CS + .NET 8.0 for other projects

**Upgrade Branch**: `upgrade-to-NET8`
- ? Already created and checked out
- Isolated upgrade work
- Will contain all upgrade changes

**Branch Workflow**:
```
main (stable)
  ??? upgrade-to-NET8 (work in progress)
      ??? Convert SDL2-CS to SDK-style
      ??? Update TargetFramework to net8.0
      ??? Build and verify
      ??? Test and validate
      ? Merge back to main when complete
```

### Commit Strategy

**Single Commit Approach** (Recommended for All-at-Once)

Given the atomic nature of this upgrade (single project, single operation), use a single comprehensive commit:

**Commit Message Template**:
```
Upgrade SDL2-CS to .NET 8.0

- Convert SDL2-CS.csproj to SDK-style project format
- Update TargetFramework from net40 to net8.0
- Enable AllowUnsafeBlocks for P/Invoke pointers
- Verify all 4 projects build successfully
- Test Usim emulator with upgraded SDL2-CS bindings

All projects now target .NET 8.0:
- SDL2-CS.csproj: net40 ? net8.0
- Chaos.csproj: net8.0 (no change)
- Usim.csproj: net8.0 (no change)
- UsimTools.csproj: net8.0 (no change)

Tested:
- Solution builds without errors
- Usim emulator launches successfully
- SDL2 window, input, and audio all functional
- No P/Invoke errors or regressions

Closes: #[issue-number] (if tracked)
```

**Why Single Commit**:
- All changes are interdependent (SDK conversion + TFM update)
- Cannot partially apply changes (incompatible intermediate states)
- Clean rollback point if issues arise
- Clear atomic unit of change in history

**Alternative: Checkpoint Commits** (If preferred)

If you prefer incremental commits for more granular history:

1. **Checkpoint 1**: SDK-style conversion
   ```
   Convert SDL2-CS.csproj to SDK-style format
   
   - Replace classic project format with SDK-style
   - Maintain net40 target initially
   - Verify project still builds
   ```

2. **Checkpoint 2**: Framework upgrade
   ```
   Upgrade SDL2-CS.csproj to .NET 8.0
   
   - Update TargetFramework: net40 ? net8.0
   - Verify solution builds
   - Test Usim emulator functionality
   ```

**Recommendation**: Single commit approach aligns better with All-at-Once strategy.

### Review and Merge Process

**Pre-Merge Checklist**:
- [ ] All builds successful (Debug and Release)
- [ ] All validation tests passed
- [ ] No new warnings introduced
- [ ] Usim emulator tested and functional
- [ ] Performance verified (no regressions)
- [ ] Documentation updated (if needed)

**Merge Process**:
```bash
# Ensure all tests pass
dotnet build LispMachine.sln --configuration Release
cd usim-cs
dotnet run -- -config ../config/usim.ini
# Manual validation: emulator works correctly

# Switch to main branch
git checkout main

# Merge upgrade branch (fast-forward or merge commit)
git merge upgrade-to-NET8

# Push to remote (if applicable)
git push origin main

# Optional: Delete upgrade branch
git branch -d upgrade-to-NET8
```

**Merge Strategy**: 
- **Fast-forward merge** if main unchanged
- **Merge commit** if main has diverged (preserves upgrade branch history)

**Pull Request** (if using PR workflow):
- Title: "Upgrade SDL2-CS to .NET 8.0 - Complete solution modernization"
- Description: Include commit message content
- Reviewers: (as appropriate)
- Checks: Build and test success

### All-at-Once Source Control Guidance

**Key Principles**:

1. **Atomic Changeset**: Single commit represents complete upgrade
2. **Branch Isolation**: All work on `upgrade-to-NET8` branch
3. **Clean Merge**: Merge only after full validation
4. **Rollback Ready**: Git allows instant revert if issues arise

**Commit Timing**:
- **Before changes**: Initial state committed ?
- **After upgrade**: Single commit with all changes
- **After validation**: Tests passed, ready to merge

**Best Practices**:
- Commit message describes what, why, and validation performed
- Include test results summary in commit message
- Reference any related issues or documentation
- Tag release after merge (optional): `git tag v1.0-net8.0`

---

## Success Criteria

### Technical Criteria

? **All Projects Migrated**
- SDL2-CS.csproj targets net8.0
- Chaos.csproj targets net8.0 (already complete)
- Usim.csproj targets net8.0 (already complete)
- UsimTools.csproj targets net8.0 (already complete)
- **Result**: 4 of 4 projects on .NET 8.0 (100% coverage)

? **All Package Updates Applied**
- No package updates required (System.CommandLine already compatible)
- **Result**: 1 of 1 packages compatible (100% coverage)

? **All Builds Pass**
- `dotnet build LispMachine.sln` succeeds
- `dotnet build -c Release LispMachine.sln` succeeds
- Zero compilation errors
- Zero warnings (or only expected/acceptable warnings)

? **All Tests Pass**
- SDL2-CS builds successfully (project-level validation)
- Usim emulator launches and runs (integration validation)
- All SDL2 subsystems functional (functional validation)
- No runtime errors or crashes (stability validation)

? **No Package Dependency Conflicts**
- All NuGet packages resolve correctly
- No version conflicts
- No missing dependencies

? **No Security Vulnerabilities**
- Zero vulnerabilities in analysis (already validated)
- All packages secure
- No new vulnerabilities introduced

### Quality Criteria

? **Code Quality Maintained**
- No code changes required (project file only)
- Existing code unchanged
- P/Invoke signatures preserved
- Unsafe code still supported

? **Test Coverage Maintained**
- Functional validation through Usim emulator
- All SDL2 subsystems tested
- Input/output functionality verified

? **Documentation Updated** (if needed)
- README.md updated with .NET 8.0 requirement
- Build instructions updated
- Dependencies documented

### Process Criteria

? **All-at-Once Strategy Followed**
- SDL2-CS upgraded in single atomic operation
- SDK conversion + TFM update performed together
- Solution validated as complete unit

? **All-at-Once Principles Applied**
- Simultaneity: All changes applied atomically
- Atomic validation: Immediate build verification
- No intermediate states: Direct net40 ? net8.0 transition
- Unified build: Solution builds as complete unit

? **Source Control Followed**
- Work isolated on `upgrade-to-NET8` branch ?
- Changes committed with descriptive messages
- Merge to `main` after validation
- Clean history maintained

### Functional Success Criteria

? **Usim Emulator Functionality**
- Launches successfully
- SDL2 window appears
- Display renders correctly
- Keyboard input responsive
- Mouse input responsive
- Audio beep functional
- No runtime errors
- Stable operation for extended period

? **No Regressions**
- All existing features work
- Performance comparable or improved
- Compatibility maintained
- User experience unchanged

? **Native Interop Works**
- SDL2.dll loads successfully
- P/Invoke calls succeed
- Marshaling correct
- No memory corruption
- Clean resource disposal

### Completion Checklist

**Upgrade Complete When**:
- [x] SDL2-CS converted to SDK-style format
- [x] SDL2-CS TargetFramework updated to net8.0
- [x] SDL2-CS project builds successfully
- [x] Entire solution builds without errors
- [x] All 4 projects target .NET 8.0
- [x] Usim emulator tested and functional
- [x] SDL2 window creation works
- [x] SDL2 input handling works
- [x] SDL2 audio works
- [x] SDL2 rendering works
- [x] No runtime errors or crashes
- [x] No performance regressions
- [x] No functional regressions
- [x] Changes committed to `upgrade-to-NET8` branch
- [x] Changes merged to `main` branch
- [x] Documentation updated (if needed)

**Definition of Done**:
All 4 projects target .NET 8.0, solution builds successfully, Usim emulator runs without errors, all validation tests pass, changes merged to main branch.

---

## Summary

This plan provides a comprehensive roadmap for upgrading the SDL2-CS project from .NET Framework 4.0 to .NET 8.0, completing the solution's modernization to uniform .NET 8.0 targeting.

**Key Characteristics**:
- **Single project scope**: Only SDL2-CS requires changes
- **Low risk**: Well-understood SDK-style conversion
- **High compatibility**: Zero breaking changes detected
- **Fast execution**: Single development session
- **All-at-Once approach**: Atomic operation with immediate validation

**Expected Outcome**: Fully modernized solution with all 4 projects targeting .NET 8.0 LTS, ready for continued development with modern .NET features and performance improvements.
