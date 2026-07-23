# Manta Ray

<p align="center">
  <img src="Manta_logo.png" alt="Manta Ray logo" width="460"/>
</p>

<p align="center">
  <img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-blue.svg"/>
  <img alt="Platform: Rhino 8" src="https://img.shields.io/badge/Rhino-8-darkblue.svg"/>
  <img alt=".NET 4.8" src="https://img.shields.io/badge/.NET-4.8-purple.svg"/>
  <img alt="Grasshopper" src="https://img.shields.io/badge/Grasshopper-Plugin-00D2B4.svg"/>
  <img alt="9 components" src="https://img.shields.io/badge/components-9-00D2B4.svg"/>
</p>

> **One Grasshopper plugin for all environmental physics.**  
> Acoustic noise mapping, animated wind streamlines, solar path analysis, and pressure-wave visualisation — live in the Rhino viewport at 60 fps.

---

## Why "Manta Ray"?

Manta rays are masters of fluid dynamics — they read pressure fields, glide on curl vortices, and navigate by sensing wave propagation. Manta Ray does the same: it simulates acoustic noise as ray-cast energy fields, wind as a divergence-free curl-noise velocity field, and solar radiation as a real-time incidence sweep. The name fits the Grasshopper animal-plugin tradition (Weaverbird, Kangaroo, Pufferfish…).

---

## Components

All nine components install to a single **Manta** tab in Grasshopper, split across two panels: **Acoustic** and **Environment**. On the canvas each node carries a short label (*Source*, *Mesh*, *Noise*…); throughout these docs we write **Manta Source**, **Manta Mesh**, etc. for clarity.

### Acoustic — `Manta ▸ Acoustic`

| Icon | Component | What it does |
|------|-----------|--------------|
| ![](MantaSource_24.png)   | **Manta Source**   | Define point and line noise sources (road, rail) |
| ![](MantaMesh_24.png)     | **Manta Mesh**     | Convert any geometry to an analysis mesh |
| ![](MantaNoise_24.png)    | **Manta Noise**    | Acoustic analysis — false-colour heat-map, reflections |
| ![](MantaInterior_24.png) | **Manta Interior** | Interior exposure score for Galapagos optimisation |
| ![](MantaContours_24.png) | **Manta Contours** | Isodecibel contour polylines |
| ![](MantaLegend_24.png)   | **Manta Legend**   | Colour-scale legend in the Rhino viewport |

### Environment — `Manta ▸ Environment`

| Icon | Component | What it does |
|------|-----------|--------------|
| ![](MantaWind_24.png)     | **Manta Wind**     | Animated wind streamlines via curl-noise turbulence |
| ![](MantaSun_24.png)      | **Manta Sun**      | Animated solar path + real-time incidence sweep |
| ![](MantaPressure_24.png) | **Manta Pressure** | Animated acoustic pressure wavefronts |

---

## Typical workflow

Node labels as they appear on the canvas:

```
Source ──► Mesh ──► Noise ──► Interior ──► Galapagos
             │         │
             │         └──► Contours ──► Legend
             │
             ├──► Wind       (wind over same façade mesh)
             └──► Sun        (solar on same mesh)
Source ─────────► Pressure   (animated wavefronts from same sources)
```

---

## Acoustic reference

### Manta Source

Point sources and/or line sources (road/rail centrelines). Line sources subdivide into N equal-power sub-points:

```
L_sub = L_total − 10·log10(N)
```

| Input | Default | Description |
|-------|---------|-------------|
| P – Point Sources | — | Individual noise source points |
| dBP – Point dB | — | Sound power level per point (dB SPL) |
| T – Rail/Road | — | Line-source curve (road or rail centreline) |
| dBT – Line dB | — | Sound power level per line source |
| N – Subdivisions | 20 | Sub-sources per line source |

---

### Manta Mesh

Converts Mesh, Surface, Brep, SubD or Extrusion to a normals-ready analysis mesh. Quality 0–3 maps to Rhino's fast/default/analysis/fine meshing parameters.

---

### Manta Noise

Core acoustic analysis. Computes per-face dB using:

```
L = L_src − 20·log10(d) − 11 + 10·log10(cosθ + 0.01)
L_total = 10·log10(Σ 10^(Li/10))
```

First-order reflections (optional):
```
L_ref = L_src − 20·log10(d1+d2) − 11 + 10·log10(cosθ+0.01) − α_dB − mat_loss
```

| Input | Default | Description |
|-------|---------|-------------|
| M – Mesh | — | From Manta Mesh |
| S – Sources | — | From Manta Source |
| dB – Levels | — | From Manta Source |
| Min / Max | auto | Pin colour-scale bounds |
| R – Reflections | false | Enable first-order reflections |
| α – Absorption | 3 dB | Reflection loss per bounce |
| Mat – Materials | — | Per-face absorption coefficient 0–1 |
| Lim – Limit dB | — | Activates compliance overlay (green/yellow/red) |

| Output | Description |
|--------|-------------|
| M – Mesh | Vertex-coloured façade mesh |
| dB – Face dB | Per-face total → Manta Interior / Manta Contours |
| Min / Max | Colour-scale bounds → Manta Legend |
| ExA – Exceeded m² | Façade area exceeding limit |
| Ex% – % Exceeded | Percentage of façade exceeding limit |
| RM – Reflect Mesh | False-colour reflection hotspot mesh |

---

### Manta Interior

Scores interior noise exposure as a single dB fitness scalar — wire to Galapagos and set to **Minimise**:

```
Score = 10·log10(Σ [10^(dBi/10) × area_i / dist_i²])
```

---

### Manta Contours

Marching-triangles isodecibel contour extraction. Connect `{50,55,60,65,70,75}` to Levels for a full contour map. Output is a GH tree — one branch per level.

---

### Manta Legend

Draws a gradient colour-scale bar in the Rhino viewport. Position with Origin, size with Height/Width. Optional limit-line overlay.

---

## Environment reference

### Manta Wind

Particles advect through a curl-noise velocity field at ~60 fps. Curl noise is divergence-free — trajectories are organic, not bunched.

```
v(x,t) = V_wind + curl( N(x/scale + t·0.1, y/scale, z/scale) ) × turbulence
```

Integrated with **RK2 (midpoint method)**. Golden-ratio phase offsets spread particles evenly.

| Input | Default | Description |
|-------|---------|-------------|
| M – Mesh | — | Analysis mesh |
| V – Wind Dir | (1,0,0) | Wind direction (normalised) |
| Sp – Speed | 5.0 | Animation rate |
| Tu – Turbulence | 1.5 | Curl-noise intensity (0 = laminar) |
| Sc – Scale | 10.0 | Noise scale relative to geometry |
| N – Particles | 80 | Streamline count |
| Tr – Trail | 20 | Trail length (steps) |
| S – Seed | 0 | Random seed |
| On | true | Animate live in the viewport (off = static outputs only, no redraw loop) |

---

### Manta Sun

NOAA SPA algorithm — accurate to ±0.01° for 2000–2050. Animates the sun across the sky, colouring each mesh face by **solar incidence** (surface orientation relative to the sun) in real time.

> **Note:** face lighting is incidence-based self-shading — `max(cos θ, 0)` between the face normal and the sun vector. It does **not** compute cast-shadow occlusion between separate elements.

| Input | Default | Description |
|-------|---------|-------------|
| M – Mesh | — | Analysis mesh |
| Lat | 51.5 | Latitude (°N) |
| Lon | −0.1 | Longitude (°E) |
| Yr / Mo / Dy | 2026-06-21 | Date |
| H0 / H1 | 6 / 20 | Analysis window (UTC hours) |
| As – Anim Spd | 1.0 | Speed multiplier |
| On | true | Animate live in the viewport (off = static outputs only, no redraw loop) |

Outputs: sun-path arc, current direction/elevation/azimuth, per-face solar incidence, peak sun hours per face.

---

### Manta Pressure

Spherical pressure wavefronts from noise sources. Each source emits concentric rings across three planes. Colour shifts warm (high dB) → cool (low dB).

| Input | Default | Description |
|-------|---------|-------------|
| S – Sources | — | From Manta Source |
| dB – Levels | — | From Manta Source |
| c – Wave Speed | 343 | Speed of sound (m/s) |
| Sc – Scale | 0.05 | Visual scale |
| R – Rings | 5 | Wavefront rings per source |
| On | true | Animate live in the viewport (off = static outputs only, no redraw loop) |

---

## Installation

### Build from source

Requirements: .NET SDK, Rhino 8

```bat
git clone https://github.com/LCS3002/Manta-Ray-for-Grasshopper
cd Manta-Ray-for-Grasshopper
build.bat
```

`build.bat` generates icons, compiles, and installs `Manta.gha`. Close Rhino before running.

### Manual install

Copy `Manta.gha` to `%APPDATA%\Grasshopper\Libraries\` and restart Rhino.

---

## Requirements

- Rhino 8 (RhinoCommon 8.x, Grasshopper 1.x)
- .NET Framework 4.8
- No external plugin dependencies

---

## Colour palette

| Colour | Hex | Used for |
|--------|-----|---------|
| Navy | `#080C1C` | Background |
| Teal | `#00D2B4` | Environment icons, wind trails |
| Cyan | `#3CDCFF` | Particle heads, solar glow |
| Amber | `#F5A623` | Acoustic icons, source indicators |

The scientific dB heat-map (blue → cyan → yellow → orange → red) is a fixed perceptual scale and is independent of the brand palette.

---

## Project structure

```
Manta-Grasshopper/
├── MantaInfo.cs                 # Assembly info, icon loader, brand colours
├── MantaAcoustics.cs            # Acoustic math — direct, reflections, energy sum
├── MantaContourAlgo.cs          # Marching-triangles isodecibel contour extraction
├── MantaSourceComponent.cs      # Manta Source
├── MantaMeshComponent.cs        # Manta Mesh
├── MantaNoiseComponent.cs       # Manta Noise
├── MantaInteriorComponent.cs    # Manta Interior
├── MantaContoursComponent.cs    # Manta Contours
├── MantaLegendComponent.cs      # Manta Legend
├── MantaMath.cs                 # Curl noise, RK2 advection, NOAA SPA, incidence
├── MantaWindComponent.cs        # Manta Wind
├── MantaSunComponent.cs         # Manta Sun
├── MantaPressureComponent.cs    # Manta Pressure
├── Manta.csproj                 # SDK-style .NET 4.8 project
├── build.bat                    # One-click build + install
├── GenerateIcon/                # Programmatic icon generator (System.Drawing)
└── MathTest/                    # Standalone acoustic-math test suite (65 tests)
```

---

## License

MIT — see [LICENSE](LICENSE)
