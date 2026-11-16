"""
Decimação dos modelos pesados do Apartment (plano de otimização, fase 2).

Uso (Blender em background):
  blender --background --factory-startup --python decimate_models.py -- <jobs.json>

jobs.json = [{"src": "<fbx original>", "out": "<fbx de saída>", "target": <triângulos alvo>}, ...]
Para cada job: importa o FBX, aplica Decimate (Collapse) em todos os objetos de malha com a
mesma razão (alvo / total), e exporta para "out". O original não é tocado. Orientação e escala
são conferidas e corrigidas depois, no Unity (comparando os limites com a malha original).
"""
import bpy, json, sys, os

argv = sys.argv[sys.argv.index("--") + 1:]
jobs = json.load(open(argv[0], encoding="utf-8"))


def tris_of(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


report = []
for job in jobs:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=job["src"])
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    total = sum(tris_of(o) for o in meshes)
    ratio = min(1.0, job["target"] / max(1, total))

    for o in meshes:
        bpy.context.view_layer.objects.active = o
        m = o.modifiers.new("Decimate", "DECIMATE")
        m.decimate_type = "COLLAPSE"
        m.ratio = ratio
        m.use_collapse_triangulate = True
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.ops.object.modifier_apply(modifier=m.name)
        # Normais suaves com corte por ângulo: a decimação desfaz as normais custom.
        try:
            bpy.ops.object.shade_auto_smooth(angle=0.6)
        except Exception:
            try:
                bpy.ops.object.shade_smooth_by_angle(angle=0.6)
            except Exception:
                pass

    after = sum(tris_of(o) for o in meshes)
    os.makedirs(os.path.dirname(job["out"]), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=job["out"],
        use_selection=False,
        object_types={"MESH", "EMPTY"},
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        bake_anim=False,
        path_mode="STRIP",
        embed_textures=False,
        axis_forward="-Z",
        axis_up="Y",
        apply_scale_options="FBX_SCALE_UNITS",
    )
    report.append(f"{os.path.basename(job['src'])}: {total} -> {after} tris (razão {ratio:.4f})")
    print("DECIMATED", report[-1], flush=True)

print("DONE", len(report), flush=True)
