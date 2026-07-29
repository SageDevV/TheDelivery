# BCG Building Generator — Facade Strip-Atlas Layout

This document describes the UV layout of the BCG facade strip-atlas so that advanced users
can author their own albedo and emission atlases in any image editor.

---

## Overview

The atlas is a **1024 × 2048 px** vertical strip. One full tile spans all 1024 columns
horizontally and 256 rows vertically (the window-band height for most bands). U advances
**1/8 per window cell** horizontally (`cellsPerTile = 8`): a building 8 cells wide uses
exactly one full tile repetition, a 4-cell building uses half a tile, etc. V selects which
band a floor samples by staying inside that band's row range for the entire floor quad.

The atlas is split into nine horizontal **bands**, stacked from bottom (V = 0) to top
(V = 1). Both the **albedo** and **emission** atlases share the **identical band layout**,
so a custom emission map can be authored directly on top of a custom albedo.

The four shipped palettes — and the `BCG_Facade_Albedo_{A..D}.png` /
`BCG_Facade_Emission_{A..D}.png` file names — use this same layout.

![Band layout overlaid on the Albedo_A atlas — the nine V-bands with their V ranges and pixel rows, the roof band's flat/shingle split at column 512, and the fasciaDark sub-band.](images/atlas_band_diagram.png)

---

## Band table

V coordinates are normalized (0 = bottom of image, 1 = top of image), matching Unity
UV space. Pixel rows count from the **top** of the image downward (row 0 = top).

| Band | Variable | V range (normalized) | Pixel rows (top-origin) | Height | Purpose |
|---|---|---|---|---|---|
| Store | `bandStore` | 0.0000 – 0.1250 | 1792 – 2048 | 256 px | Ground-floor storefront: display windows, entry doors. The door cell is at U [2/8, 3/8] within the tile. |
| Mullion | `bandMullion` | 0.1250 – 0.2500 | 1536 – 1792 | 256 px | Flush mullion curtain wall: vertical metal framing, glass infill. |
| Balcony | `bandBalcony` | 0.2500 – 0.3750 | 1280 – 1536 | 256 px | Balcony band: slab edge + railing detail. Used with geometric relief (0.12 m inset). |
| Ribbon | `bandRibbon` | 0.3750 – 0.5000 | 1024 – 1280 | 256 px | Flush ribbon (strip) glazing: horizontal bands of glass with spandrel. |
| Punched | `bandPunched` | 0.5000 – 0.6250 | 768 – 1024 | 256 px | Punched openings in a masonry/plaster field. Flush (no relief). |
| WinLit | `bandWinLit` | 0.6250 – 0.7500 | 512 – 768 | 256 px | Lit office glass: bright interior illumination behind dark frames. Used with geometric relief. |
| WinDark | `bandWinDark` | 0.7500 – 0.8750 | 256 – 512 | 256 px | Dark office glass: tinted/reflective curtain wall. Used with geometric relief. |
| Concrete | `bandConcrete` | 0.8750 – 0.9375 | 128 – 256 | 128 px | Spandrel / concrete parapet fill. Sub-sampled by the engine for parapet, corner strips, reveals, gable triangles, and House chimney sides. |
| Roof | `bandRoof` | 0.9375 – 1.0000 | 0 – 128 | 128 px | Roof surfaces. **Split horizontally** — see §Roof band below. |

---

## Padding

Each band is inset by **6 px on each vertical edge** before the UV coordinates are handed
to mesh quads. This is computed as:

```
padV = 6 / 2048  ≈ 0.00293 (UV units)
```

The constant in source is `const float padV = 6f / 2048f` (`BCG_BuildingMeshBuilder.cs`, line 172).
The engine calls `Pad(band)` which shrinks the V range inward by `padV` on both sides:
`Pad(band) = (band.x + padV, band.y - padV)`.

This 6 px guard prevents mip-map bleed across band boundaries at standard atlas sizes.

---

## Roof band — horizontal split

The roof band (V 0.9375 – 1.0000, rows 0 – 128) is split **horizontally** into two
independently tileable halves at column 512:

| Half | U range | Constants | Content |
|---|---|---|---|
| Left — flat roof | U 0.0 – 0.5 | `roofFlatU` | Flat-roof gravel / membrane. Padded: `(0 + padU, 0.5 − padU)`. |
| Right — shingles | U 0.5 – 1.0 | `roofShingleU` | Pitched shingle tiles. Padded: `(0.5 + padU, 1.0 − padU)`. |

The horizontal padding is **6 px** at 1024 px width:

```
padU = 6 / 1024  ≈ 0.00586 (UV units)
```

Source constant: `const float padU = 6f / 1024f` (`BCG_BuildingMeshBuilder.cs`, line 191).
This guards against mip bleed across the half seam at U = 0.5.

- **Non-House buildings** (flat roof): parapet cap and roof slab sample `roofFlatU`.
- **House buildings** (pitched roof): the two shingle planes tile into `roofShingleU`,
  with U chunked in `cellWidth × 4` m segments and V stripped in `cellWidth` m bands across
  the slope.

---

## Dark fascia sub-band

Shop parapets sample a narrow strip **inside** the Concrete band rather than the full
parapet concrete sub-range:

| Sub-band | V range | Approximate pixel rows (top-origin) |
|---|---|---|
| `fasciaDark` | 0.9204 – 0.9331 | rows 136 – 164 |

This is stored as a pre-padded constant; no additional `Pad()` is applied at usage.

---

## Tiling and U layout

U advances at one window cell per `1/cellsPerTile` tile fraction. With `cellsPerTile = 8`:

```
U_per_cell = 1/8 = 0.125
```

For a building with `cellsX = 7` cells and a random integer U offset `uOffset` (0–7),
the side maps U = `uOffset/8` to `(uOffset + 7)/8`. The offset shifts lit windows and
door positions per facade for variety, while keeping cell seam alignment.

A per-floor shift of `(floor * 3) & 7` cells breaks the vertical stacking of the same
texel column without extra rng draws, so lit-window columns appear staggered.

---

## Concrete sub-sampling

The Concrete band (128 px) is the shared reservoir for all non-window surfaces. The engine
selects sub-ranges by linear fraction within the **padded** concrete band:

| Usage | `ConcreteSub(f0, f1)` fractions | Surface |
|---|---|---|
| Parapet outer (non-Shop) | 0.25 – 0.70 | Outer parapet wall ring |
| Parapet inner | 0.30 – 0.62 | Inner parapet wall ring |
| Parapet cap | 0.45 – 0.52 | Flat cap strip |
| Gables, chimney sides, corner strips, reveals | 0.25 – 0.70 | Wall fill |
| Chimney cap | 0.45 – 0.52 | Flat box top |
| Eave soffit | 0.30 – 0.40 | Underside of eave overhang |

---

## Four shipped palettes

![The four shipped atlases — top row: albedo palettes A–D (Light Gray / Brick / Graphite Curtain / White Plaster); bottom row: the matching emission maps A–D (dark, with lit window cells).](images/atlas_contact_sheet.png)

| Variant | Letter | Albedo file | Emission file | Visual character |
|---|---|---|---|---|
| 0 | A | `BCG_Facade_Albedo_A.png` | `BCG_Facade_Emission_A.png` | Light gray (generic concrete / glass tower) |
| 1 | B | `BCG_Facade_Albedo_B.png` | `BCG_Facade_Emission_B.png` | Brick (warm red-brown masonry) |
| 2 | C | `BCG_Facade_Albedo_C.png` | `BCG_Facade_Emission_C.png` | Graphite curtain wall (dark glass) |
| 3 | D | `BCG_Facade_Albedo_D.png` | `BCG_Facade_Emission_D.png` | White plaster (bright render / stucco) |

All four atlases share the exact band layout described in this document and are
interchangeable on any generated mesh without UV changes.

---

## Normal-map atlases (v1.2.0)

`BCG_Facade_Normal_{A..D}.png` are tangent-space normal maps sharing the **identical band
layout** as their albedo/emission counterparts — same 1024 × 2048 strip, same nine bands, same
padding. They were generated offline from the albedo/emission features (height-from-features →
tangent-space normal) by the same `Tools~/gen_facade_textures.py` generator, adding relief cues
for window frames, spandrels, and roof gravel/shingles.

- **Always-on when present, no toggle.** `CreateFacadeMaterial` binds them on every pipeline with
  the correct slot/keyword: Built-in `Standard` → `_BumpMap` + `_NORMALMAP`; URP `Lit` →
  `_BumpMap` + its normal-map keyword; HDRP `Lit` → `_NormalMap` via the existing reflection-safe
  path. **Fix Materials** is the one repair entry point that (re)binds them; the footer
  material-health badge flags a missing bind.
- **Requires mesh tangents.** `FinalizeMesh` calls `Mesh.RecalculateTangents()` so normal mapping
  has a valid tangent basis — vertex positions/counts are unaffected, so this does not touch any
  freeze test.
- Day/Night facade variants inherit the normal bind automatically (it's independent of the
  emission dial).
- To author a custom normal atlas: paint it at the same 1024 × 2048 resolution and band layout as
  the albedo atlas (see the band table above), name it `BCG_Facade_Normal_{A..D}.png`, drop it
  into `Assets/BCG/BuildingGen/Textures/`, and run **Fix Materials**.

---

## SpecGloss atlases (v1.2.0)

- `BCG_Facade_Specular_{A,B,C,D}.png` — 1024×2048 SpecGloss atlases (RGB = specular
  color, A = per-texel smoothness), same band layout as the albedo. Bound as
  `_SpecGlossMap` on the fake-interiors facade shader by **Fix Materials**; when
  absent, re-run **Fix Materials** so the materials fall back to the scalar
  wall/glass smoothness.

---

## Shared window mask (v1.2.0)

`BCG_Facade_WindowMask.png` is a single **shared** 1024 × 2048 linear (non-color) mask used by the
[Fake Interiors](BuildingGen_UserGuide.html#12-fake-interiors) shader to tell glass texels from
wall texels:

- **White = glass**, black = wall. It uses the **same band layout and padding** as the albedo
  atlas, so it aligns pixel-for-pixel with every variant's window openings.
- **Shared across all four palette variants** — because A–D all share the same band layout, one
  mask file covers every variant rather than needing four (verified at implementation time; the
  engine would fall back to per-variant masks if a future palette's layout ever diverged).
- The mask also carries **trim detail**: railing silhouettes (Balcony band), door outlines (Store
  band), and mullion bar lines (Mullion band) are painted into the mask alongside the plain glass
  rectangles, so the interior shader's masked-fragment test naturally excludes railings/frames/
  mullions from the "look into the room" treatment — those stay opaque facade.
- Read as **linear** (not sRGB) since it's a boolean-ish mask, not colour data.
- Authored by the same `Tools~` generator as the albedo/normal atlases.

---

## Interior room atlas (v1.2.0)

`BCG_InteriorAtlas.png` is a **2048 × 1024** grid of **4 × 2** pre-projected 512² room captures
used by the Fake Interiors shader as the "room behind the glass":

- **Baked by `BCG_InteriorRoomBaker.BakeAtlas()`** (Editor utility, no MenuItem — an internal dev
  tool, not a shipped workflow). It renders 8 deterministic parametric box-rooms from the window
  plane (camera at the plane, FOV 90°, aspect 1:1) into 512² tiles and composites them into the
  atlas grid. Purely algorithmic — no external or AI-generated imagery — only the baked PNG ships
  as a dependency.
- **`farFrac = 0.5` contract** — each room is baked with its back wall filling exactly the central
  half of the tile (room depth = room width, the classic single-texture parallax-room setup). The
  interior mapping shader (`BCG_InteriorMapping.hlsl`) hard-codes this same `farFrac`, so the atlas
  and the shader's ray-box intersection must agree on it — **re-bake and shader must change
  together** if that ratio is ever tuned.
  Concretely: `farFrac` is the fraction of the tile's linear size the back wall spans as the
  camera's 90° FOV frames a room whose depth equals its width — moving to a different depth/width
  ratio (or FOV) changes `farFrac` and requires updating the constant in the shader to match.
- **One texture sampler instead of N cubemaps** — cheaper on mobile than a per-room cubemap array,
  trivial to atlas into one file, and produces the same "look into a room" visual class as classic
  interior-mapping cubemap techniques.
- **Per-cell room selection** — the shader hashes the window's UV cell index to pick a grid tile
  (room variant), tint, and day/night lit state; this is a pure function of the cell index, not a
  runtime random draw, so the same building always shows the same rooms.
- **Re-baking:** re-run `BCG_InteriorRoomBaker.BakeAtlas()` (via `execute_code` or a temporary
  MenuItem) to change the room look. It overwrites `BCG_InteriorAtlas.png` in place — no further
  wiring needed since the shader already references that path by name.

---

## Road atlas — BCG_Road_Atlas.png

The [Roads](BuildingGen_UserGuide.html#23-roads) feature uses its own **1024 × 2048 px** vertical
strip atlas, laid out with the same top-origin-row convention as the facade atlas but split into
**eight equal 256 px bands** — no per-band sub-splits or sub-sampling reservoir. Both the albedo
atlas and its emission twin (`BCG_Road_Emission.png`) share the **identical** band layout, so a
custom road emission map can be authored directly on top of a custom road albedo, exactly like the
facade atlas's albedo/emission pair.

| Band | Variable | V range (normalized) | Pixel rows (top-origin) | Height | Purpose |
|---|---|---|---|---|---|
| EdgeLine | `bandEdgeLine` | 0.0000 – 0.1250 | 1792 – 2048 | 256 px | Solid lane-edge line, painted just inside each asphalt edge on the shadows-off markings renderer. |
| DashLine | `bandDash` | 0.1250 – 0.2500 | 1536 – 1792 | 256 px | Center dash strip. One atlas tile paints exactly one dash period (`kDashPeriodMeters`); the edge's U is fitted (`FitDashU`) so both trimmed ends land on a whole dash. |
| Crosswalk | `bandCrosswalk` | 0.2500 – 0.3750 | 1280 – 1536 | 256 px | Zebra crosswalk striping, emitted spanning the carriageway at every junction socket. |
| Sidewalk | `bandSidewalk` | 0.3750 – 0.5000 | 1024 – 1280 | 256 px | Pedestrian sidewalk surface — the outer band of the ribbon, outside the curb. |
| CurbTop | `bandCurbTop` | 0.5000 – 0.6250 | 768 – 1024 | 256 px | Flat curb top, between the curb face and the sidewalk. |
| CurbFace | `bandCurbFace` | 0.6250 – 0.7500 | 512 – 768 | 256 px | Beveled curb face (a 0.2 m drivable ramp, `kCurbBevelRun`), junction curb-end faces, and the outer skirt walls (reused for both). |
| Gutter | `bandGutter` | 0.7500 – 0.8750 | 256 – 512 | 256 px | Gutter strip just inside each carriageway edge — a fixed 0.3 m band per side (`kGutterWidth`). |
| Asphalt | `bandAsphalt` | 0.8750 – 1.0000 | 0 – 256 | 256 px | Carriageway surface and the planar-mapped junction pad fill. |

All eight `BCG_RoadMeshCore` band constants are `Vector2` V ranges, exactly like the facade atlas's
`bandStore`/`bandRoof`/etc. — every quad emitted by `SweepEdge`, `EmitJunction`, `EmitEndCap`,
`EmitEdgeMarkings`, and `EmitCrosswalk` samples one of the eight bands verbatim.

**Padding:** every band is shrunk by the same **6 px per edge** mip-bleed guard as the facade atlas
(`const float padV = 6f / 2048f` in `BCG_RoadMeshCore.cs`, applied via the identically-named
`Pad(band)` helper) — same convention, same constant, different source file.

**Tiling:** one atlas U tile spans **8 m** of road (`kMetersPerTile`); the dash band paints one
dash period per tile (`kDashPeriodMeters = 8`). Unlike the facade atlas there is no per-variant
palette — one road atlas covers every generated road, and its bands are horizontally seamless (they
tile cleanly at U = 0/1) rather than mapped to discrete window cells.

Authored by `Tools~/gen_road_textures.py` — the same deterministic-generation approach as
`gen_facade_textures.py`, with a single fixed seed and a fixed paint order per band.

---

## Authoring a custom atlas

1. Create a new 1024 × 2048 px image.
2. Paint each band within its V row range (see band table above), leaving 6 px clear at
   the top and bottom edge of each band as mip-bleed guard.
3. In the Roof band (rows 0 – 128):
   - Fill columns 0 – 511 with a flat-roof surface (gravel, membrane, etc.).
   - Fill columns 512 – 1023 with a shingle tile that repeats cleanly.
   - Leave 6 px clear on each side of the column-512 seam.
4. Name the file to match one of the four expected paths
   (`BCG_Facade_Albedo_{A..D}.png`) and drop it into
   `Assets/BCG/BuildingGen/Textures/`.
5. Run **Fix Materials (Active Pipeline)** from the Building Generator window to rebind
   the materials to the new textures.
6. Optionally author a matching emission atlas at the same resolution and band layout and
   place it as `BCG_Facade_Emission_{A..D}.png`.
