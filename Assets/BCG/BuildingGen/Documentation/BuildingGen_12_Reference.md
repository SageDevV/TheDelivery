# Reference

Lookup material: every setting with its default and range, the components, the deterministic seed
contract, the runtime API, and what ships in the package. If you are learning the tool, start with
[How It Works](BuildingGen_02_HowItWorks.md) instead.

All values on this page were read from the shipping source of **version 2.5.0**.

## Menu items

Everything under `Tools ▸ BoneCracker Games ▸ Building Generator ▸`:

| Menu item | What it opens or does |
|---|---|
| Welcome Window | The onboarding panel |
| Building Generator | The main tool window |
| Replace Greyboxes With Buildings | Converts selected blockout boxes into buildings |
| Generate Light Probes… | Opens the probe density prompt |
| Remove Light Probes | Deletes the generated probe group |
| Optimize City (Combine Meshes) | Merges generated buildings into district chunks |
| De-Combine City | Restores the source buildings |
| Generate Street Furniture | Lamps, benches, shelters and trees along pavements |
| Remove Street Furniture | Clears generated furniture |
| Street Furniture As Separate Props | Toggles one-prefab-per-prop mode |
| Fix Materials (Active Pipeline) | Rebuilds materials for the active render pipeline |
| Regenerate All… | Rebuilds every generated prefab in place |
| Clean Unused… | Finds and deletes orphaned generated assets |
| Select All Generated | Selects every generated building and road in the scene |

The last four also exist inside the window; they are mirrored as menu items so they work with no
window open.

## Window structure

| Stage | Sub-tabs |
|---|---|
| **1 Plan** | City Grid · Zones · Paths |
| **2 Build** | Single · Street · Districts · Greybox |
| **3 Dress** | Mood · Furniture · Probes |
| **4 Ship** | Health · Finalize |

### Primary button per pane

| Pane | Primary button |
|---|---|
| Plan ▸ City Grid | Generate City |
| Plan ▸ Zones | Create Zone Marker |
| Plan ▸ Paths | Create Street Path |
| Build ▸ Single | Generate Building |
| Build ▸ Street | Generate Street Row / Generate Along Path |
| Build ▸ Districts | Populate Selected Zones |
| Build ▸ Greybox | Replace Greyboxes (n) |
| Dress ▸ Mood | Apply Materials |
| Dress ▸ Furniture | Generate Street Furniture |
| Dress ▸ Probes | Generate Light Probes… |
| Ship ▸ Health / Finalize | *(none — Ship is a read-only audit stage)* |

## Settings

Every field in the window, grouped by the pane it appears on. Defaults are what a fresh
install gives you; ranges are the hard limits the controls enforce.

### Build ▸ Single

| Field | Type | Default | Range |
|---|---|---|---|
| Archetype | enum | Tower | Tower / Shop / Apartment / House |
| Texture Variant | enum | A — Light Gray | A / B / C / D |
| Cells X (Width) | int | 7 | — |
| Cells Z (Depth) | int | 5 | — |
| Floors | int | 9 | — |
| Seed | int | 0 | any integer |
| Cell Width (m) | float | 3.0 | 2 – 5 |
| Floor Height (m) | float | 3.2 | 2.4 – 5 |
| Ground Floor (m) | float | 4.0 | 2.8 – 6 |
| Parapet Height (m) | float | 0.9 | 0.2 – 2 |
| Parapet Thick (m) | float | 0.35 | 0.15 – 1 |

Archetype presets (applied when you switch archetype; every field stays editable):

| Archetype | Floors | Cells X × Z | Floor Height | Ground Floor | Parapet Height |
|---|---:|---:|---:|---:|---:|
| Tower | 9 | 7 × 5 | 3.2 | 4.0 | 0.9 |
| Shop | 1 | 5 × 4 | 3.2 | 4.2 | 1.0 |
| Apartment | 5 | 8 × 4 | 3.0 | 4.0 | 0.7 |
| House | 2 | 4 × 3 | 2.8 | 3.0 | *(ignored)* |

### Build ▸ Street

| Field | Type | Default | Range |
|---|---|---|---|
| Scatter Seed | int | 12345 | — |
| Road Length (m) | float | 120 | 30 – 600 |
| Road Width (m) | float | 16 | 6 – 40 |
| Both Sides | bool | on | — |
| Generate road surface | bool | off | — |
| Gap Range (m) | float pair | 4 – 10 | — |
| Tower / Shop / Apartment / House weight | float | 0.35 / 0.30 / 0.35 / 0.25 | relative weights |
| Variant Mix A / B / C / D | bool | all on | — |

### Plan ▸ City Grid

| Field | Type | Default | Range |
|---|---|---|---|
| City Seed | int | 97531 | — |
| Blocks X | int | 4 | 2 – 12 |
| Blocks Z | int | 4 | 2 – 12 |
| Block Width (m) | float | 60 | 40 – 200 |
| Block Depth (m) | float | 50 | 40 – 200 |
| Street Width (m) | float | 12 | 8 – 30 |
| Avenue Every N | int | 3 | 0 – 6 |
| Avenue Width (m) | float | 24 | 12 – 50 |
| Core Preset | asset | none | any `BCG_GenerationPreset` |
| Edge Preset | asset | none | any `BCG_GenerationPreset` |
| Core Radius | float | 0.35 | 0 – 1 |
| Skyline Falloff | bool | on | — |
| Min Height Scale | float | 0.4 | — |
| Create Ground | bool | on | — |
| Create Roads | bool | on | — |
| Sidewalk Width (m) | float | 2.5 | 1 – 4 |
| Road Backend | enum | Built-in (grid roads) | only shown with Road Constructor installed |

### Plan ▸ Zones / Build ▸ Districts

| Field | Type | Default | Range |
|---|---|---|---|
| Zone Seed | int | 24680 | — |
| Edge Margin (m) | float | 1 | 0 – 8 |
| Row Gap (m) | float pair | 6 – 10 | — |
| Markers After | enum | Disable | Disable / Delete |

### Generation Settings ▸ Geometry

| Field | Default | Notes |
|---|---|---|
| Detail | Standard | Simple / Standard / Detailed |
| Rooftop Props | on | Off reproduces pre-props geometry exactly |
| Facade Extras | on | Off reproduces pre-extras geometry exactly |
| Lit Signage | on for new work | The engine default is off, for byte-compatibility |
| Generate LODs | off | Adds an LODGroup and simplified child meshes |

### Generation Settings ▸ Saving

| Field | Default | Notes |
|---|---|---|
| Save As Prefab Assets | on | Off places buildings in the scene with no assets written |
| Bake Lightmap UVs | off | The unwrap is the most expensive part of generating |
| Reuse Existing Assets | on | Loads matching assets instead of rebuilding |
| Mesh Variety | 0 | 0 = unlimited. Caps distinct designs per archetype. Not on Single |

### Where (placement)

| Field | Default | Notes |
|---|---|---|
| Obstacle Layers | Nothing | Physics layers treated as "do not build here" |
| Snap To Ground | off | Places buildings on the ground surface beneath them |
| Ground Layers | Everything | What counts as ground when snapping |

### Dress ▸ Mood

| Field | Default | Notes |
|---|---|---|
| Night Lights Intensity | 0.8 (Dusk) | 0 turns windows off |
| Window Color | warm | The glow colour |
| Fake Interiors | off | Not available on HDRP |

Presets: **Day** = intensity 0 · **Dusk** = 0.8, warm (shipped default) · **Night** = 2.5, warm.

### Dress ▸ Probes

| Field | Default | Range |
|---|---|---|
| Quality | High (12 m) | Low 24 m / Medium 16 m / High 12 m / Ultra 8 m / Custom |
| Custom spacing | — | 1 m minimum |
| Probe Budget | 4096 | up to 65536 |
| Coverage | Whole city | Whole city / Near roads + buildings (15 m band) |

Probe heights: street level **1.5 m**, mid-rise **8 m**, rooftop **tallest roof + 2 m** (only where a
building stands nearby).

## Components

The two components the tool puts in your scene. Both are data-only and safe to ship in a
build — neither contains any generation logic.

### BCG_BuildingZone (district component)

A Runtime `MonoBehaviour` that gives a `BoxCollider` area its own district settings. Requires a
`BoxCollider` on the same GameObject; carries no generation logic, so it is safe in a build.

| Field | Type | Default | Range | Description |
|---|---|---|---|---|
| `towerWeight` | float | 0.35 | 0 – 1 | Relative chance a plot becomes a Tower |
| `shopWeight` | float | 0.30 | 0 – 1 | Relative chance a plot becomes a Shop |
| `apartmentWeight` | float | 0.35 | 0 – 1 | Relative chance a plot becomes an Apartment |
| `houseWeight` | float | 0.25 | 0 – 1 | Relative chance a plot becomes a gabled House |
| `variantA` | bool | true | — | Allow the A — Light Gray palette |
| `variantB` | bool | true | — | Allow the B — Brick palette |
| `variantC` | bool | true | — | Allow the C — Graphite Curtain palette |
| `variantD` | bool | true | — | Allow the D — White Plaster palette |
| `seed` | int | 0 | — | 0 = auto; a stable seed is written on first fill |
| `edgeMargin` | float | 1 | 0 – 8 | Distance (m) buildings keep from the zone bounds |
| `gapMin` | float | 4 | — | Minimum spacing (m) between plots along a row |
| `gapMax` | float | 10 | — | Maximum spacing (m) between plots along a row |
| `rowGapMin` | float | 6 | — | Minimum alley width (m) between rows |
| `rowGapMax` | float | 10 | — | Maximum alley width (m) between rows |
| `obstacleLayers` | LayerMask | Nothing | — | Layers treated as obstacles for this zone |
| `heightFalloff` | AnimationCurve | flat at 1 | — | X = distance from centre (0–1), Y = floor multiplier |
| `snapToGround` | bool | false | — | Snap each building's base to the ground surface |
| `groundLayers` | LayerMask | Everything | — | What counts as ground when snapping |
| `detail` | enum | Full (Standard) | — | Geometry tier for this district |
| `facadeExtras` | bool | true | — | AC units and wall vents on Tower/Shop/Apartment |

`lastPopulated` is hidden in the Inspector and managed by the tool.

**Gizmo colours:** cyan `(0.2, 0.9, 1.0)` while empty, green `(0.3, 1.0, 0.4)` once populated, with
an 8%-alpha solid fill when selected.

### BCG_BuildingMarker (per-building tag)

Stamped on every generated building. Hidden from the Add Component menu and the Inspector; data-only
and safe in a build. It is how the tool finds, audits and cleans up its own output.

| Field | Type | Default | Description |
|---|---|---|---|
| `archetype` | enum | — | What kind of building this is |
| `variant` | int | — | Palette index, 0 = A |
| `seed` | int | — | The seed that produced it |
| `rooftopProps` | bool | false | Whether it was generated with props |
| `detail` | enum | Full | The tier it was generated at |
| `facadeExtras` | bool | false | Whether it was generated with extras |
| `litSigns` | bool | false | Whether it was generated with signage |
| `footprintWidth` | float | — | Local footprint width |
| `footprintDepth` | float | — | Local footprint depth |
| `footprintHeight` | float | — | Local footprint height |

The three option flags default to **false** so buildings made before those features existed
deserialise honestly rather than claiming content they do not have.

## The seed contract

Every building is built from a single `System.Random(seed)` sequence, consumed in a fixed order.
This is the load-bearing invariant behind "same seed = same building", so the order is never
changed — new features append to the end.

1. **Massing plan** — setback / podium / L-plan picks. Tower only, and only when eligible. House
   short-circuits to a slab **without consuming any draws**; its roof pitch is a pure function of the
   seed, not a draw.
2. **Facade style pair** — a primary and secondary style rolled from the archetype's pool.
3. **Per block, in plan order** — four per-side U offsets, then one band/style roll per floor.
4. **Rooftop / storefront props** — gated on `rooftopProps`, non-House only. Fixed draw counts
   depending only on archetype and floor count, drawn *before* the tail so a Simple build can
   truncate without desyncing. With props off, this step is skipped entirely.
5. **Tail** — either **5a** per-block roof clutter (non-House), or **5b** the House chimney rolls
   (always 3 draws: presence, side, position — regardless of outcome).
6. **Facade extras** — gated on `facadeExtras`, non-House only. Appended after the tail. Per side
   (0–3), always 3 draws: presence, density, phase — 12 total, fixed regardless of outcome.
7. **Lit signage** — gated on `litSigns`. Shop = 2 draws; Tower with 10+ floors = 7 draws; Apartment,
   shorter Towers and House consume nothing.

**Simple** is a strict *prefix*: it consumes identical draws through step 4 and truncates from there.
It is never a different roll sequence.

**Detailed adds nothing to this list.** Every Detailed-only feature is either always-present geometry
or a pure function of the seed, consuming zero draws. Standard and Detailed are byte-identical
through steps 1–6; only the vertex count differs.

**Road generation consumes nothing** from any seeded sequence, which is why toggling roads never
reshuffles a building.

### Mesh name content tags

Generated mesh assets are name-tagged whenever their geometry departs from the untagged baseline, so
flipping an option can never overwrite a different combination's shared mesh. Tags compose in this
fixed order:

| Tag | Meaning | Applied when |
|---|---|---|
| `_P` | Rooftop / storefront props | `rooftopProps == true`, at every detail tier |
| `_D` | Detailed tier | `detail == Detailed` |
| `_S` | Simple tier | `detail == Simple` |
| `_X` | Facade extras | `facadeExtras == true` **and** tier is not Simple |
| `_G` | Lit signage | `litSigns == true` **and** tier is not Simple |
| `_LOD1` | First LOD child mesh | Generate LODs on |
| `_LOD2` | Second LOD child mesh | Generate LODs on **and** tier is Detailed |

A mesh's full name is `BCG_BuildingMesh_{baseId}{tags}` — for example a Detailed building with props
and extras is `..._P_D_X`. Extras and signage are suppressed at the Simple tier, which is why they
are not tagged there: a Simple mesh with extras on is byte-identical to one with extras off, and
tagging it would create a spurious duplicate.

## LOD transition points

| Detail tier | LOD0 holds to | LOD1 holds to | Culls below |
|---|---:|---:|---:|
| Simple / Standard | 0.10 | — | 0.01 |
| Detailed | 0.55 | 0.20 | 0.01 |

Values are fractions of screen height.

## Runtime API

The geometry engine lives in the Runtime assembly, so buildings can be generated in-game.

```csharp
using BoneCrackerGames.BuildingGen;

public class CitySpawner : MonoBehaviour {

    //  Assign one of the shipped facade materials (Generated/BCG_Building_Facade_A..D.mat)
    //  in the Inspector — runtime code cannot create pipeline-aware materials itself.
    public Material facadeMaterial;

    void Start() {

        BCG_BuildingParams p = new BCG_BuildingParams {
            archetype = BCG_BuildingArchetype.Tower,
            cellsX = 7,
            cellsZ = 5,
            floors = 12,
            seed = 4242
        };

        GameObject building = BCG_RuntimeBuildingFactory.Build(p, facadeMaterial);
        building.transform.position = new Vector3(30f, 0f, 0f);

    }

}
```

### Entry points

```csharp
//  Just the mesh.
Mesh BCG_BuildingMeshCore.BuildMesh(BCG_BuildingParams p);
Mesh BCG_BuildingMeshCore.BuildMesh(BCG_BuildingParams p, BCG_BuildingDetail detail);

//  The whole GameObject: mesh, material, per-block colliders, identity marker.
GameObject BCG_RuntimeBuildingFactory.Build(
    BCG_BuildingParams p,
    Material material,
    bool addColliders = true,
    BCG_BuildingDetail detail = BCG_BuildingDetail.Full);
```

### BCG_BuildingParams

| Field | Default |
|---|---|
| `archetype` | Tower |
| `variant` | 0 (A) |
| `cellsX` | 7 |
| `cellsZ` | 5 |
| `floors` | 9 |
| `seed` | 0 |
| `cellWidth` | 3 |
| `floorHeight` | 3.2 |
| `groundFloorHeight` | 4 |
| `parapetHeight` | 0.9 |
| `parapetThickness` | 0.35 |
| `rooftopProps` | true |
| `detail` | Full |
| `facadeExtras` | true |
| `litSigns` | false |

### Notes

- **The same seed produces the same building everywhere.** The Editor and the runtime share one
  engine.
- **Runtime output has no static flags, lightmap UVs or LODGroup.** Those are editor-time concerns;
  runtime output is meant to be dynamic.
- **Generation is main-thread** (it uses Unity's mesh API). A large tower costs a few milliseconds,
  so spread bulk generation across frames.

## Enumerations

```csharp
enum BCG_BuildingArchetype { Tower, Shop, Apartment, House }
enum BCG_BuildingDetail    { Full, Simple, Detailed }        //  UI: Standard, Simple, Detailed
enum BCG_FacadeStyle       { OfficeDark, OfficeLit, Punched, Ribbon, Balcony, Mullion }
```

## Stored preferences

Settings are stored per user in `EditorPrefs` under the `BCG.BuildingGen.` prefix — for example
`BCG.BuildingGen.Stage`, `BCG.BuildingGen.DetailLevel`, `BCG.BuildingGen.GenerateLODs`,
`BCG.BuildingGen.SaveAsPrefab`, `BCG.BuildingGen.FakeInteriors`, `BCG.BuildingGen.ProbeBudget`.

Three of them are stored **per project** (the key carries the project's GUID) because they reference
project-specific things: the output root, the obstacle layer mask and the ground layer mask.

Nothing about your city depends on these — they are UI state. A teammate opening your scene sees the
same city with their own tool preferences.

## Package contents

| Folder | Contents |
|---|---|
| `Editor/` | The generator window and every tool: mesh builder, zone populator, placement guard, road builder, greybox replacer, probe placer, city optimizer, furniture builder, scene inventory, asset cleanup, onboarding, UI theme |
| `Editor/RC/` | The optional Road Constructor bridge. Only compiles when Road Constructor is present |
| `Runtime/` | The geometry core and runtime factory, plus data-only components: `BCG_BuildingZone`, `BCG_BuildingMarker`, `BCG_RoadNetwork`, `BCG_RoadMarker`, the enums and the version stamp |
| `Shaders/` | `BCG_FacadeInterior.shader` and its include (Built-in + URP SubShaders) |
| `Textures/` | The facade atlases (albedo, emission, normal, specular × A–D), the window mask, the interior room atlas, and the road atlas and emission maps |
| `Presets/` | The four shipped district presets |
| `Demo/` | The demo scene and its playable rig scripts |
| `Documentation/` | This documentation set and its HTML mirrors |
| `Generated/` | Materials, plus the meshes and prefabs you generate |
| `Addons/` | The optional City Demo package, imported on demand |

**Not shipped:** the Edit Mode tests, the internal Python texture-authoring scripts, and the
development documents outside `Assets/`.

## Version

The authoritative version is `BuildingGen_Version.Version` in
`Runtime/BuildingGen_Version.cs` — currently **2.5.0**. The generator window shows it in its title
row. See the [Changelog](BuildingGen_Changelog.md) for what changed in each release.

## See also

- **[How It Works](BuildingGen_02_HowItWorks.md)** — the concepts behind this reference.
- **[Atlas Layout](BuildingGen_AtlasLayout.md)** — the texture atlas band layout.
- **[Troubleshooting](BuildingGen_11_Troubleshooting.md)** — every Console message explained.
