# Projects and dependencies analysis

This document provides a comprehensive overview of the projects and their dependencies in the context of upgrading to .NETCoreApp,Version=v8.0.

## Table of Contents

- [Executive Summary](#executive-Summary)
  - [Highlevel Metrics](#highlevel-metrics)
  - [Projects Compatibility](#projects-compatibility)
  - [Package Compatibility](#package-compatibility)
  - [API Compatibility](#api-compatibility)
- [Aggregate NuGet packages details](#aggregate-nuget-packages-details)
- [Top API Migration Challenges](#top-api-migration-challenges)
  - [Technologies and Features](#technologies-and-features)
  - [Most Frequent API Issues](#most-frequent-api-issues)
- [Projects Relationship Graph](#projects-relationship-graph)
- [Project Details](#project-details)

  - [chaos-cs\Chaos.csproj](#chaos-cschaoscsproj)
  - [SDL2-CS\SDL2-CS.csproj](#sdl2-cssdl2-cscsproj)
  - [usim-cs\Usim.csproj](#usim-csusimcsproj)
  - [usim-cs-tools\UsimTools.csproj](#usim-cs-toolsusimtoolscsproj)


## Executive Summary

### Highlevel Metrics

| Metric | Count | Status |
| :--- | :---: | :--- |
| Total Projects | 4 | 1 require upgrade |
| Total NuGet Packages | 1 | All compatible |
| Total Code Files | 35 |  |
| Total Code Files with Incidents | 1 |  |
| Total Lines of Code | 20264 |  |
| Total Number of Issues | 2 |  |
| Estimated LOC to modify | 0+ | at least 0.0% of codebase |

### Projects Compatibility

| Project | Target Framework | Difficulty | Package Issues | API Issues | Est. LOC Impact | Description |
| :--- | :---: | :---: | :---: | :---: | :---: | :--- |
| [chaos-cs\Chaos.csproj](#chaos-cschaoscsproj) | net8.0 | ✅ None | 0 | 0 |  | ClassLibrary, Sdk Style = True |
| [SDL2-CS\SDL2-CS.csproj](#sdl2-cssdl2-cscsproj) | net40 | 🟢 Low | 0 | 0 |  | ClassicClassLibrary, Sdk Style = False |
| [usim-cs\Usim.csproj](#usim-csusimcsproj) | net8.0 | ✅ None | 0 | 0 |  | DotNetCoreApp, Sdk Style = True |
| [usim-cs-tools\UsimTools.csproj](#usim-cs-toolsusimtoolscsproj) | net8.0 | ✅ None | 0 | 0 |  | DotNetCoreApp, Sdk Style = True |

### Package Compatibility

| Status | Count | Percentage |
| :--- | :---: | :---: |
| ✅ Compatible | 1 | 100.0% |
| ⚠️ Incompatible | 0 | 0.0% |
| 🔄 Upgrade Recommended | 0 | 0.0% |
| ***Total NuGet Packages*** | ***1*** | ***100%*** |

### API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 0 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 6023 |  |
| ***Total APIs Analyzed*** | ***6023*** |  |

## Aggregate NuGet packages details

| Package | Current Version | Suggested Version | Projects | Description |
| :--- | :---: | :---: | :--- | :--- |
| System.CommandLine | 2.0.0-beta4.22272.1 |  | [UsimTools.csproj](#usim-cs-toolsusimtoolscsproj) | ✅Compatible |

## Top API Migration Challenges

### Technologies and Features

| Technology | Issues | Percentage | Migration Path |
| :--- | :---: | :---: | :--- |

### Most Frequent API Issues

| API | Count | Percentage | Category |
| :--- | :---: | :---: | :--- |

## Projects Relationship Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart LR
    P1["<b>📦&nbsp;Usim.csproj</b><br/><small>net8.0</small>"]
    P2["<b>📦&nbsp;Chaos.csproj</b><br/><small>net8.0</small>"]
    P3["<b>📦&nbsp;UsimTools.csproj</b><br/><small>net8.0</small>"]
    P4["<b>⚙️&nbsp;SDL2-CS.csproj</b><br/><small>net40</small>"]
    P1 --> P2
    P1 --> P4
    click P1 "#usim-csusimcsproj"
    click P2 "#chaos-cschaoscsproj"
    click P3 "#usim-cs-toolsusimtoolscsproj"
    click P4 "#sdl2-cssdl2-cscsproj"

```

## Project Details

<a id="chaos-cschaoscsproj"></a>
### chaos-cs\Chaos.csproj

#### Project Info

- **Current Target Framework:** net8.0✅
- **SDK-style**: True
- **Project Kind:** ClassLibrary
- **Dependencies**: 0
- **Dependants**: 1
- **Number of Files**: 5
- **Lines of Code**: 1388
- **Estimated LOC to modify**: 0+ (at least 0.0% of the project)

#### Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph upstream["Dependants (1)"]
        P1["<b>📦&nbsp;Usim.csproj</b><br/><small>net8.0</small>"]
        click P1 "#usim-csusimcsproj"
    end
    subgraph current["Chaos.csproj"]
        MAIN["<b>📦&nbsp;Chaos.csproj</b><br/><small>net8.0</small>"]
        click MAIN "#chaos-cschaoscsproj"
    end
    P1 --> MAIN

```

### API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 0 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 0 |  |
| ***Total APIs Analyzed*** | ***0*** |  |

<a id="sdl2-cssdl2-cscsproj"></a>
### SDL2-CS\SDL2-CS.csproj

#### Project Info

- **Current Target Framework:** net40
- **Proposed Target Framework:** net8.0
- **SDK-style**: False
- **Project Kind:** ClassicClassLibrary
- **Dependencies**: 0
- **Dependants**: 1
- **Number of Files**: 5
- **Number of Files with Incidents**: 1
- **Lines of Code**: 11105
- **Estimated LOC to modify**: 0+ (at least 0.0% of the project)

#### Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph upstream["Dependants (1)"]
        P1["<b>📦&nbsp;Usim.csproj</b><br/><small>net8.0</small>"]
        click P1 "#usim-csusimcsproj"
    end
    subgraph current["SDL2-CS.csproj"]
        MAIN["<b>⚙️&nbsp;SDL2-CS.csproj</b><br/><small>net40</small>"]
        click MAIN "#sdl2-cssdl2-cscsproj"
    end
    P1 --> MAIN

```

### API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 0 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 6023 |  |
| ***Total APIs Analyzed*** | ***6023*** |  |

<a id="usim-csusimcsproj"></a>
### usim-cs\Usim.csproj

#### Project Info

- **Current Target Framework:** net8.0✅
- **SDK-style**: True
- **Project Kind:** DotNetCoreApp
- **Dependencies**: 2
- **Dependants**: 0
- **Number of Files**: 20
- **Lines of Code**: 7383
- **Estimated LOC to modify**: 0+ (at least 0.0% of the project)

#### Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph current["Usim.csproj"]
        MAIN["<b>📦&nbsp;Usim.csproj</b><br/><small>net8.0</small>"]
        click MAIN "#usim-csusimcsproj"
    end
    subgraph downstream["Dependencies (2"]
        P2["<b>📦&nbsp;Chaos.csproj</b><br/><small>net8.0</small>"]
        P4["<b>⚙️&nbsp;SDL2-CS.csproj</b><br/><small>net40</small>"]
        click P2 "#chaos-cschaoscsproj"
        click P4 "#sdl2-cssdl2-cscsproj"
    end
    MAIN --> P2
    MAIN --> P4

```

### API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 0 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 0 |  |
| ***Total APIs Analyzed*** | ***0*** |  |

<a id="usim-cs-toolsusimtoolscsproj"></a>
### usim-cs-tools\UsimTools.csproj

#### Project Info

- **Current Target Framework:** net8.0✅
- **SDK-style**: True
- **Project Kind:** DotNetCoreApp
- **Dependencies**: 0
- **Dependants**: 0
- **Number of Files**: 5
- **Lines of Code**: 388
- **Estimated LOC to modify**: 0+ (at least 0.0% of the project)

#### Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph current["UsimTools.csproj"]
        MAIN["<b>📦&nbsp;UsimTools.csproj</b><br/><small>net8.0</small>"]
        click MAIN "#usim-cs-toolsusimtoolscsproj"
    end

```

### API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 0 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 0 |  |
| ***Total APIs Analyzed*** | ***0*** |  |

