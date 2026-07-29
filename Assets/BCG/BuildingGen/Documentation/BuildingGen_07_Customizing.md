# Customising Buildings

This page covers everything that changes what a building is *made of* — which kind it is, which
palette it wears, how much geometry it gets, and the optional extras. These settings appear in the
**Generation Settings ▸ Geometry** foldout on every pane that generates buildings, and as
per-district fields on `BCG_BuildingZone`.

Everything here is deterministic. Changing a setting changes what you get, but the same settings
plus the same seed always give the same building.

## The four archetypes

An archetype is a structural preset — the rules the generator follows for that kind of building.

| Archetype | Typical height | Shape | Roof | Reads as |
|---|---|---|---|---|
| **Tower** | 7 – 16 floors | Can break into multiple volumes (setbacks, podiums, L-plans) | Flat, with a concrete parapet and roof clutter | Offices, high-rise |
| **Shop** | 1 – 2 floors | One slab | Flat, with a dark fascia strip | Retail, storefronts |
| **Apartment** | 3 – 8 floors | One slab | Flat, with a parapet | Residential blocks |
| **House** | 1 – 2 floors | One slab | Pitched shingle roof with eaves and a chimney | Suburbs, low-density |

Switching archetype loads a starting point for that kind of building. Every field stays editable
afterwards — these are defaults, not constraints:

| Archetype | Floors | Cells X × Z | Floor Height | Ground Floor | Parapet Height |
|---|---:|---:|---:|---:|---:|
| Tower | 9 | 7 × 5 | 3.2 m | 4.0 m | 0.9 m |
| Shop | 1 | 5 × 4 | 3.2 m | 4.2 m | 1.0 m |
| Apartment | 5 | 8 × 4 | 3.0 m | 4.0 m | 0.7 m |
| House | 2 | 4 × 3 | 2.8 m | 3.0 m | *(ignored)* |

### Multi-volume towers

Towers are the only archetype that can break out of a single box, and only when they are big enough
to justify it: **7 or more floors, at least 6 cells wide and 5 cells deep**. Below that threshold —
and for every other archetype at any size — you get a single slab.

When a tower does qualify, its seed picks between a plain slab, a setback (stepping in as it rises),
a podium (a wide base with a tower on top) or an L-plan. This is the single biggest reason a
downtown district looks varied while a low-rise one looks uniform.

### Houses

House geometry is deliberately special-cased. The roof pitch is a pure function of the seed —
`32 + (seed mod 9)` degrees, so always between **32° and 40°** — with the ridge running along the
building's longer axis. A chimney appears **70% of the time**, its side and position along the ridge
both seeded. Houses have no parapet, no roof clutter and no massing variants, so the parapet fields
are ignored for them.

## Palettes

**Texture Variant** picks which of four palettes a building wears:

| Variant | Look |
|---|---|
| **A** | Light Gray |
| **B** | Brick |
| **C** | Graphite Curtain |
| **D** | White Plaster |

All four share one atlas layout, so a palette swap is genuinely free — same mesh, different
material. On the fills that place many buildings, the **Variant Mix** checkboxes control which
palettes are eligible: tick only C for a glass financial district, drop B for a city with no brick.

## Detail levels

The **Detail** dropdown picks how much geometry represents a building. Crucially, it does **not**
change *which* building you get — the same seed produces the same design at every tier, just with
more or less geometry describing it.

| Level | What it is | Use it for |
|---|---|---|
| **Simple** | Flat shells. Flush facades, no relief, no roof clutter, no props | Distant filler, mobile, LOD tails |
| **Standard** | The default look — recessed windows, roof clutter, full detail | Almost everything |
| **Detailed** | Standard plus heavy facade elaboration | Hero buildings the camera gets close to |

### What Detailed adds

- **Recessed relief on every facade style.** Styles that were flush gain the same recessed-opening
  treatment the others already had, plus window sills and mullion divider bars inside each opening.
- **Real balconies** — an actual slab, railing posts and a top rail per cell, instead of a flat
  painted band.
- **Cornices and trim** — a parapet coping profile and corner pilasters on block edges.
- **Storefront depth** — recessed entry cells and pilasters between shop windows.
- **House details** — window shutters, a door canopy and porch stoop, and a chimney cap.
- **Richer roof clutter** — the same clutter boxes, but each with a lip, legs and a pipe.

None of this consumes any extra randomness. Every Detailed-only feature is either always-present
geometry or a pure function of the seed, which is why switching tiers never changes the design.

### What it costs

These are pinned reference measurements from the package's own regression tests, with rooftop props
and facade extras **off** so the comparison is like-for-like:

| Building | Standard | Detailed | Multiplier |
|---|---:|---:|---:|
| Tower 7×5, 9 floors (seed 4242) | 1,100 | 5,764 | 5.2× |
| Tower 8×6, 12 floors (seed 314159) | 1,364 | 5,096 | 3.7× |
| Shop 5×3, 2 floors (seed 777) | 312 | 1,000 | 3.2× |
| Apartment 6×4, 6 floors (seed 1234) | 1,064 | 9,948 | 9.3× |
| House 4×3, 2 floors (seed 7) | 86 | 1,208 | 14.0× |

*(Vertex counts.)* Apartments and houses show the largest multipliers because they gain the most
per-cell geometry — balconies on one, shutters and porches on the other. A Standard house is close
to a decorated box, which is exactly what makes it cheap.

**Two pieces of guidance:**

1. **Pair Detailed with Generate LODs.** A Detailed building gets a three-level LOD chain
   (Detailed → Standard → Simple), so the extra triangles only cost anything close up. Without
   LODs, a Detailed building carries its full vertex count at every distance, including when it is
   twelve pixels tall. See [Optimising ▸ LODs](BuildingGen_09_Optimizing.md#lods).
2. **On mobile, stay on Standard or Simple.** Detailed is a desktop, foreground-only tier. It is
   fine for a handful of hero buildings near the camera; it is not what you fill a mobile skyline
   with.

Detail is available from every workflow, and as a per-district field on `BCG_BuildingZone`, so you
can run Detailed in the block the player walks through and Simple everywhere else.

## Rooftop and storefront props

**Rooftop Props** (on by default) adds silhouette props — the things that stop a skyline reading as
a row of boxes:

| Archetype | What it gets |
|---|---|
| Tower, Apartment | An antenna mast with cross-arms and/or a water tank on legs, on the topmost roof block |
| Tower, 10+ floors | Additionally a rooftop billboard on one roof edge |
| Shop | Fabric awnings over every storefront cell (door cells stay clear) and a protruding sign box over the door |
| House | None — its silhouette comes from the gabled roof and chimney |

The rooftop billboard's face reuses the lit-window band of the atlas, so it part-glows at night
along with the windows, at no extra cost.

Turning props **off** reproduces the pre-props geometry exactly. Turning them **on** means a given
seed's roof clutter differs from what that seed produced before props existed — the props draw from
the same random sequence. That is expected and documented; it is not a bug, and it only affects
buildings you generate from now on.

## Facade extras

**Facade Extras** (on by default) adds small seeded set-dressing to walls: air-conditioning units
under some window cells, and wall vents. It applies to Tower, Shop and Apartment; **House is
untouched**.

It is independent of detail level and works the same at Standard and Detailed. Simple truncates it,
along with everything else in the tail.

Turning it off reproduces the extras-free geometry byte-for-byte.

## Lit signage

**Lit Signage** adds vertical corner sign strips to tall towers (10+ floors, up to two of them) and
a lit fascia strip over a shop's storefront. The strips are mapped into the atlas's lit-window band,
so they **glow at night** through the same emission path as the windows — no new textures, no real
lights, still one material and one draw call per building.

The engine default is **off**, for byte-compatibility with scenes made before signage existed. The
window's toggle defaults **on** for new work, so buildings you generate now get signs while nothing
existing changes retroactively.

## Turning a blockout into buildings

If you prefer to design a skyline by hand, block it out with plain boxes — any object with a
BoxCollider or a mesh; scaled cubes are ideal — select them, and run **Replace Greyboxes With
Buildings** from **2 Build ▸ Greybox**.

Each box becomes a generated building matching its **footprint, height, base height and yaw**
(snapped to the nearest 90°).

- **The box defines the intent.** There is no ground snapping (the box's own base *is* the base) and
  no random reseed — the seed is a stable hash of the box's **name**. Re-running a re-blocked scene
  reproduces the same buildings, and renaming one box rerolls just that building.
- **The archetype is inferred from shape**: tall becomes a Tower, mid-rise an Apartment, low and
  wide a Shop, low and small a House. Floors and window cells are fitted to the box's dimensions.
- Placement still routes through the anti-overlap guard. Generated output, road pieces and zone
  markers are never mistaken for greyboxes. One Undo restores the boxes.

Four settings have no effect on greybox replacement either way, and the pane says so: **Snap To
Ground** and **Ground Layers** (the box's base already decides the height), **Mesh Variety** (the
seed is always the box-name hash) and **Reuse Existing Assets** (replacement always reuses a
matching asset when saving prefabs).

> The menu-item version of this command uses quick-blockout defaults instead of the pane's settings:
> scene instances with no assets written, no lightmap UVs, no LODs, Standard detail, props and
> extras on. Run it from the pane when you want your own settings honoured.

## How mesh files stay separate

Because these options change geometry, the tool must never let one option's mesh overwrite
another's. Generated mesh files carry short name tags recording what built them — props, detail
tier, extras and signage each add a tag — so a props-on mesh and a props-off mesh are always
different files at the same size and seed.

You do not need to think about this day to day. It matters when you flip an option and want to know
whether your existing buildings changed: they did not. The tag table is in
[Reference ▸ Mesh name tags](BuildingGen_12_Reference.md#mesh-name-content-tags).

## Common questions

**I switched to Detailed and my building looks the same shape.** That is correct. Detail changes how
much geometry describes the design, not the design. Look at the window reveals, sills and balconies.

**My towers are all plain boxes.** They are below the massing threshold — multi-volume shapes need
7+ floors and at least 6 × 5 cells.

**Can I add my own props or facades?** Not through the tool. You can repaint the texture atlas — see
[Atlas Layout](BuildingGen_AtlasLayout.md) — and you can of course parent your own objects to a
generated building in the scene.

**Why did my old buildings not get signage after I turned it on?** Nothing changes retroactively.
Regenerate them, or refill the district, to pick up new content options.

## Where to go next

- **[Materials and Lighting](BuildingGen_08_MaterialsAndLighting.md)** — palettes, night glow and
  fake interiors.
- **[Optimising](BuildingGen_09_Optimizing.md)** — LODs, and what Detailed actually costs you.
- **[Reference](BuildingGen_12_Reference.md)** — the seed contract and every setting's default.
