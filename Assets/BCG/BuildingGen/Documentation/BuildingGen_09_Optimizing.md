# Optimising

A generated city is already cheap by construction — one mesh and one material per building, sharing
four materials across the whole city. This page is about the further levers, when each one is worth
pulling, and which ones are traps.

## What is already free

Before you optimise anything, know what you are starting from:

- **One draw call per building** before any batching. Because every building shares one of four
  materials, Unity's static batching and the SRP Batcher then collapse them much further.
- **No runtime cost from the tool.** Generation happens in the Editor. Your shipped city is ordinary
  meshes and prefabs.
- **No lights for night windows.** The night glow is material emission, not light sources.
- **No lightmap unwrap by default**, which is the single most expensive part of generating a
  building.

That means the honest answer for most projects is: generate at Standard detail, turn on LODs, and
stop. The rest of this page is for when you need more.

## LODs

**Generate LODs** (in Generation Settings ▸ Geometry, **off** by default) builds simplified meshes
alongside each building and wires them into a `LODGroup`.

**LOD1 drops** the recessed window relief (facades go flush), the roof clutter, and the House eave
detail and chimney. **Rooftop props are kept**, so nothing visibly pops at the swap — a disappearing
antenna is far more noticeable than flattened window reveals.

The transition points differ by detail tier:

| Detail tier | Chain | Transitions (screen height) |
|---|---|---|
| Simple / Standard | LOD0 → LOD1 | LOD0 down to **0.10**, LOD1 culls below **0.01** |
| Detailed | LOD0 → LOD1 → LOD2 | **0.55** → **0.20** → culls below **0.01** |

For Standard buildings, LOD0 is held all the way down to 10% of screen height on purpose. An earlier
build swapped at 60% and the flush LOD1 was visibly popping in while the building still filled most
of the screen. Culling at 1% keeps a 30 m tower visible to roughly 2.6 km, so distant skylines never
blink out in a driving game.

**Pair LODs with Detailed.** A Detailed building carries three to fourteen times the vertices of a
Standard one (see [what Detailed costs](BuildingGen_07_Customizing.md#what-it-costs)). Without LODs
it carries all of them at every distance. With them, the cost is confined to buildings that are
actually close.

Notes:

- Works for both prefab output (`…_LOD1` / `…_LOD2` mesh assets, GUID-stable) and scene-only output.
- **Preview In Scene** stays full detail — it is an audition view.
- **Regenerate All** preserves each prefab's LOD-ness as authored, whatever the toggle currently
  says.
- Static batching bakes both LOD meshes into its combined buffers, so distant draw cost falls at a
  small memory cost.
- With **Bake Lightmap UVs** on, every LOD gets its own UV set and contributes GI. With it off, the
  LOD children keep the cheaper UV-less layout.

## Choosing a detail tier

| Target | Recommendation |
|---|---|
| Mobile | **Simple** for filler, **Standard** for anything the camera lingers on |
| Desktop background city | **Standard** + LODs |
| Desktop hero street | **Detailed** + LODs, everything else Standard |
| VR | **Standard** or Simple; Detailed only for hand-picked landmarks |

Detail is a per-district field, so mixing tiers across one city is normal — run Detailed only in the
block the player actually walks through.

## Fewer unique meshes

Two settings covered in [Creating Many Buildings](BuildingGen_04_ManyBuildings.md#keeping-variety-under-control)
matter as much for runtime as for disk:

- **Reuse Existing Assets** (on by default) skips rebuilding assets that already match.
- **Mesh Variety** caps how many distinct designs a fill may use per archetype. Buildings sharing a
  footprint then share a mesh, which means fewer assets, faster fills, and identical scene-only
  meshes that static-batch together.

For background filler, a pool of 8–20 per archetype is usually indistinguishable from unlimited once
palettes and row facing are mixed in.

## Optimize City (combining)

**Optimize City (Combine Meshes)** — row 4 of the checklist on **4 Ship ▸ Finalize** — merges every
generated building into a handful of district-scale meshes, bucketed by shared material and split
below **65,000 vertices** per chunk.

**Be honest about what this buys you.** It collapses *GameObject and renderer counts* — the
per-object CPU overhead that hurts mobile most. Draw calls were already being handled by static
batching and the SRP Batcher, so this is a finalise step, not a frame-rate multiplier. On a
demo-scale city it typically takes renderer counts from four figures to double digits.

What you need to know before running it:

- **It is fully reversible.** Source buildings are *disabled*, never destroyed. **De-Combine City**
  restores them exactly, and the button only appears while a combined city exists.
- **Run it last.** While a city is combined, the placement guard cannot see the disabled buildings,
  so anything you generate afterwards will happily overlap them. Generate everything, then combine.
- **Foundation skirts ride along**, and LOD chains contribute their LOD0.
- **Baked lightmap textures do not carry over.** GI-enabled chunks receive a **fresh unwrap over the
  final combined geometry** and are ready for a new bake; you must re-bake lighting afterwards.
  Sources with GI off stay in separate, unwrapped-free chunks.
- Combined renderers use Simple reflection-probe blending, since one probe now covers a
  district-sized mesh.

## Download size

Four import-and-geometry decisions keep the shipped size down. They are automatic, but two of them
only apply to **newly generated** assets, so a library made with an older version needs a
**Regenerate All** to benefit:

| What | Effect | Applies to |
|---|---|---|
| **16-bit mesh indices** where possible | Generated meshes use the narrowest index format their vertex count allows instead of always reserving 32-bit. Meshes past 65,535 vertices switch to 32-bit automatically | New assets — regenerate to apply |
| **Packed vertex format** | Normals and tangents stored as signed bytes, UVs as half-floats. Vertex stride drops from 48 to 24 bytes. Positions stay full 32-bit float, so nothing shifts | New assets — regenerate to apply |
| **Compressed window mask** | The shared window mask texture is pure black and white, so block compression is exact | Immediate |
| **Halved specular atlases on mobile and WebGL** | The specular/smoothness atlases import at half resolution on Android, iOS and WebGL. **Desktop keeps full resolution**, because halving visibly softens railings, window frames and mullions | Immediate |

> **One important exception.** A mesh that carries **lightmap UVs is never packed**. Unity's
> lightmapper rejects any mesh outside the standard float layout and silently drops it from the bake.
> So if you bake GI on your buildings, those meshes keep the wider stride — correct lighting beats a
> smaller download. Meshes without lightmap UVs (the default) get the full saving.

## A mobile checklist

1. **Detail = Simple or Standard.** Never Detailed for filler.
2. **Generate LODs on.**
3. **Mesh Variety** set to something finite — 8 to 16 per archetype.
4. **Fake Interiors off** (the default). It is a per-fragment cost across every facade.
5. **Lit Signage** is free (it rides the existing atlas), so leave it on if you like the look.
6. **Street furniture in combined mode** (the default), not separate props.
7. **Regenerate All** once, so the packed vertex format and 16-bit indices apply.
8. **Optimize City** as the final step, after everything is placed.
9. **Light probes** on a modest budget — 4096 is usually plenty; use *Near roads + buildings*
   coverage rather than raising the budget.

## Common questions

**Combining did not improve my frame rate.** It reduces renderer and GameObject overhead, not draw
calls — those were already batched. If you are GPU-bound on overdraw or shader cost, look at Fake
Interiors and your detail tier instead.

**Should I combine before or after baking lighting?** Combine first, then bake. Combining discards
existing lightmaps and re-unwraps the combined geometry, so baking beforehand wastes the bake.

**My meshes are still the old size after updating.** Run **Regenerate All** — the size wins for
indices and vertex format apply when a mesh is written, and existing assets keep their old format
until they are rebuilt.

**Can I use LODs and combining together?** Yes. Combining takes each building's LOD0. You lose the
LOD chain on combined output, which is the trade — combine district-scale background, keep LODs on
anything the camera approaches.

## Where to go next

- **[Finishing and Shipping](BuildingGen_10_Finishing.md)** — the checklist that puts these steps in
  the right order.
- **[Customising Buildings](BuildingGen_07_Customizing.md)** — the detail tiers and what each costs.
- **[Materials and Lighting](BuildingGen_08_MaterialsAndLighting.md)** — baked lighting and probes.
