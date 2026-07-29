//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Terminal vertex-buffer compressor for generated meshes. Halves the vertex stride —
    /// 48 B -> 24 B — by storing normals and tangents as SNorm8x4 and the UV set as Float16x2.
    /// POSITIONS DELIBERATELY STAY Float32: the L-block anti-z-fight shrink is 0.02 m
    /// (BCG_BuildingMeshCore) while a half-float ulp at the 42 m corpus extent is 0.031 m, so half
    /// positions would collapse it.
    ///
    /// A MESH THAT CARRIES LIGHTMAP UVs IS NEVER PACKED — <see cref="Pack"/> returns it untouched at
    /// the full 56 B stride. Unity's lightmapper rejects any mesh whose vertex channels are not the
    /// standard Float32 layout ("Invalid Mesh was removed from light baking input due to failure
    /// code: 'Invalid format for channel(s) in Mesh'") and bakes that building to nothing, which is
    /// what made "Bake Lightmap UVs" appear to do nothing at all: the unwrap succeeded and this
    /// class made the result unusable one line later. Correctness beats 24 bytes, and the default
    /// path (lightmap UVs off — how city filler is generated) keeps the whole win.
    /// See <see cref="HasUsableLightmapUVs"/> for the migration predicate.
    ///
    /// MUST run LAST, immediately before a mesh is written. Every simple Mesh setter (vertices /
    /// normals / tangents / SetUVs) re-emits its channel as Float32, and
    /// Unwrapping.GenerateSecondaryUVSet rewrites the whole vertex buffer — so any such call AFTER
    /// a pack silently un-packs the mesh. Call <see cref="Unpack"/> first whenever an already-packed
    /// mesh has to be handed back to Unwrapping.
    /// </summary>
    public static class BCG_MeshPacker {

        /// <summary>Master switch. Setting this false disables every pack site in one edit — the
        /// rollback lever. Public so tests can pin the unpacked baseline.</summary>
        public static bool Enabled = true;

        //  SNorm8 scale. 127 (not 128) so +-1 encodes EXACTLY: 127/127 == 1.0f. Tangent handedness
        //  rides on that — every tangent.w the generator emits is exactly +-1.
        const float kSNormScale = 127f;

        /// <summary>Packed vertex WITHOUT lightmap UVs — 24 B. Field order MUST be Unity's canonical
        /// attribute order: Unity canonicalizes the descriptor array it is handed, so the struct —
        /// not the descriptor array — is what has to be right.</summary>
        [StructLayout(LayoutKind.Sequential)]
        struct PackedVertex {

            public Vector3 position;            //  Float32x3 @ 0
            public sbyte nx, ny, nz, nw;        //  SNorm8x4  @ 12   (nw unused — 4-byte alignment)
            public sbyte tx, ty, tz, tw;        //  SNorm8x4  @ 16   (tw = handedness, exact)
            public ushort u0x, u0y;             //  Float16x2 @ 20

        }

        /// <summary>True when the mesh already carries the packed layout — the idempotence guard.
        /// Normal's format is the discriminator (Float32x3 when unpacked, SNorm8x4 when packed).</summary>
        public static bool IsPacked(Mesh mesh) {

            return mesh != null
                && mesh.HasVertexAttribute(VertexAttribute.Normal)
                && mesh.GetVertexAttributeFormat(VertexAttribute.Normal) == VertexAttributeFormat.SNorm8;

        }

        /// <summary>True when the mesh carries a lightmap UV set Unity's lightmapper can actually
        /// consume. PRESENCE IS NOT ENOUGH: 2.3.0 packed such meshes, and the lightmapper throws out
        /// every packed mesh ("Invalid format for channel(s) in Mesh") — so it looks unwrapped to any
        /// plain HasVertexAttribute check while baking to nothing. The whole lightmapper-facing
        /// layout is checked, not only TexCoord1: promoting UV2 while leaving packed normals,
        /// tangents, or UV0 still fails the bake. Treating those meshes as UNBAKED is what lets a
        /// 2.3.0 library repair itself through the normal "Bake Missing" path instead of needing a
        /// full regenerate.</summary>
        public static bool HasUsableLightmapUVs(Mesh mesh) {

            return mesh != null
                && !IsPacked(mesh)
                && HasFloat32Channel(mesh, VertexAttribute.Position, 3, true)
                && HasFloat32Channel(mesh, VertexAttribute.Normal, 3, false)
                && HasFloat32Channel(mesh, VertexAttribute.Tangent, 4, false)
                && HasFloat32Channel(mesh, VertexAttribute.TexCoord0, 2, false)
                && HasFloat32Channel(mesh, VertexAttribute.TexCoord1, 2, true);

        }

        static bool HasFloat32Channel(Mesh mesh, VertexAttribute attribute, int dimension, bool required) {

            if (!mesh.HasVertexAttribute(attribute))
                return !required;

            return mesh.GetVertexAttributeFormat(attribute) == VertexAttributeFormat.Float32
                && mesh.GetVertexAttributeDimension(attribute) == dimension;

        }

        /// <summary>Only the exact generator layout is packed: Position (Float32x3) + Normal +
        /// Tangent + TexCoord0, optionally TexCoord1, and NOTHING else. Anything richer (vertex
        /// colours, extra UV sets) or poorer (street-furniture meshes carry no tangents — a
        /// synthesised all-zero tangent would hand the facade shader a degenerate TBN and break
        /// _BumpMap) is returned untouched rather than guessed at.</summary>
        static bool IsEligible(Mesh mesh) {

            if (mesh == null || mesh.vertexCount == 0)
                return false;

            if (!mesh.HasVertexAttribute(VertexAttribute.Position)
                || !mesh.HasVertexAttribute(VertexAttribute.Normal)
                || !mesh.HasVertexAttribute(VertexAttribute.Tangent)
                || !mesh.HasVertexAttribute(VertexAttribute.TexCoord0))
                return false;

            if (mesh.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32
                || mesh.GetVertexAttributeDimension(VertexAttribute.Position) != 3)
                return false;

            VertexAttributeDescriptor[] attrs = mesh.GetVertexAttributes();

            for (int i = 0; i < attrs.Length; i++) {

                VertexAttribute a = attrs[i].attribute;

                if (a != VertexAttribute.Position && a != VertexAttribute.Normal
                    && a != VertexAttribute.Tangent && a != VertexAttribute.TexCoord0
                    && a != VertexAttribute.TexCoord1)
                    return false;

            }

            return true;

        }

        //  +-1 encodes exactly (127/127 == 1.0f). Mathf.RoundToInt is deterministic ties-to-even.
        static sbyte ToSNorm8(float v) {

            return (sbyte)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * kSNormScale);

        }

        /// <summary>Rewrites the mesh's vertex buffer IN PLACE to the packed layout and returns the
        /// same instance (so call sites can chain). Idempotent, deterministic, and lossless on the
        /// index buffer, submeshes, index format and bounds. A no-op on a mesh that is already
        /// packed, disabled, or does not carry the generator layout.</summary>
        public static Mesh Pack(Mesh mesh) {

            if (!Enabled || mesh == null || IsPacked(mesh) || !IsEligible(mesh))
                return mesh;

            //  LIGHTMAP UVs => DO NOT PACK AT ALL. A mesh carrying TexCoord1 is bound for Unity's
            //  lightmapper, and the lightmapper REJECTS any mesh whose vertex channels are not the
            //  standard Float32 layout: it logs "Invalid Mesh was removed from light baking input
            //  due to failure code: 'Invalid format for channel(s) in Mesh'" and bakes that building
            //  to nothing — which is what made "Bake Lightmap UVs" look like it did nothing at all.
            //  Promoting only TexCoord1 back to Float32 is NOT enough; that was measured and the
            //  meshes were still rejected, so the packed normals / tangents / TexCoord0 are refused
            //  too. Exactly which channels it tolerates is undocumented and free to change between
            //  Unity versions, so this deliberately does NOT reverse-engineer a minimal subset:
            //  a mesh with lightmap UVs keeps the full 56 B stride and bakes correctly. The default
            //  path — lightmap UVs OFF, how city filler is generated — keeps the whole 48 -> 24 B win.
            if (mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                return mesh;

            int count = mesh.vertexCount;

            //  The convenience accessors decode whatever layout is currently in place and always
            //  hand back Vector3/Vector4/Vector2, so this works on a fresh mesh and on one that
            //  has already been through GenerateSecondaryUVSet.
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;

            var uv0 = new List<Vector2>(count);
            mesh.GetUVs(0, uv0);

            //  Ragged channels would silently corrupt the interleave — leave such a mesh alone.
            if (normals.Length != count || tangents.Length != count || uv0.Count != count)
                return mesh;

            //  SetVertexBufferData does NOT recompute bounds, and a zero-extent AABB frustum-culls
            //  the mesh everywhere (and would mis-classify greybox replacements). Positions are
            //  untouched by packing, so RESTORE the AABB instead of paying an O(n) recalculation
            //  on every mesh in a library.
            Bounds bounds = mesh.bounds;

            //  Index state is captured defensively. SetVertexBufferParams is documented to touch
            //  the vertex buffer only, and that was measured to hold, but a silent index wipe
            //  would be catastrophic and the restore costs nothing when it never fires.
            IndexFormat indexFormat = mesh.indexFormat;
            int subMeshCount = mesh.subMeshCount;
            var indices = new int[subMeshCount][];
            var topologies = new MeshTopology[subMeshCount];

            for (int s = 0; s < subMeshCount; s++) {

                topologies[s] = mesh.GetTopology(s);
                indices[s] = mesh.GetIndices(s);

            }

            var descriptors = new[] {
                new VertexAttributeDescriptor(VertexAttribute.Position,  VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal,    VertexAttributeFormat.SNorm8,  4),
                new VertexAttributeDescriptor(VertexAttribute.Tangent,   VertexAttributeFormat.SNorm8,  4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2)
            };

            var data = new PackedVertex[count];

            for (int i = 0; i < count; i++) {

                data[i].position = positions[i];
                data[i].nx = ToSNorm8(normals[i].x);
                data[i].ny = ToSNorm8(normals[i].y);
                data[i].nz = ToSNorm8(normals[i].z);
                data[i].nw = 0;
                data[i].tx = ToSNorm8(tangents[i].x);
                data[i].ty = ToSNorm8(tangents[i].y);
                data[i].tz = ToSNorm8(tangents[i].z);
                data[i].tw = ToSNorm8(tangents[i].w);
                data[i].u0x = Mathf.FloatToHalf(uv0[i].x);
                data[i].u0y = Mathf.FloatToHalf(uv0[i].y);

            }

            mesh.SetVertexBufferParams(count, descriptors);
            mesh.SetVertexBufferData(data, 0, 0, count);

            //  Defensive index restore — a no-op on every measured path.
            if (mesh.subMeshCount != subMeshCount
                || (subMeshCount > 0 && mesh.GetIndexCount(0) != (uint)indices[0].Length)) {

                mesh.indexFormat = indexFormat;
                mesh.subMeshCount = subMeshCount;

                for (int s = 0; s < subMeshCount; s++)
                    mesh.SetIndices(indices[s], topologies[s], s, false);

            }

            mesh.bounds = bounds;

            return mesh;

        }

        /// <summary>Rebuilds the mesh with Unity's default all-Float32 layout. REQUIRED before an
        /// already-packed mesh is handed to Unwrapping.GenerateSecondaryUVSet — that call rewrites
        /// the vertex buffer (and per Unity's docs may ADD vertices when it splits charts), so it
        /// must never be pointed at a packed buffer. Returns true when the mesh actually was
        /// packed.</summary>
        public static bool Unpack(Mesh mesh) {

            if (mesh == null || !IsPacked(mesh))
                return false;

            int count = mesh.vertexCount;
            bool hasUV1 = mesh.HasVertexAttribute(VertexAttribute.TexCoord1);

            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;

            var uv0 = new List<Vector2>(count);
            mesh.GetUVs(0, uv0);

            var uv1 = new List<Vector2>();

            if (hasUV1)
                mesh.GetUVs(1, uv1);

            IndexFormat indexFormat = mesh.indexFormat;
            int subMeshCount = mesh.subMeshCount;
            var indices = new int[subMeshCount][];
            var topologies = new MeshTopology[subMeshCount];

            for (int s = 0; s < subMeshCount; s++) {

                topologies[s] = mesh.GetTopology(s);
                indices[s] = mesh.GetIndices(s);

            }

            Bounds bounds = mesh.bounds;

            //  Clear(false) DROPS the vertex layout. The parameterless Clear() KEEPS it
            //  (keepVertexLayout defaults to true), which would leave the packed descriptors in
            //  place and defeat this method entirely.
            mesh.Clear(false);

            mesh.indexFormat = indexFormat;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv0);

            if (hasUV1)
                mesh.SetUVs(1, uv1);

            mesh.subMeshCount = subMeshCount;

            for (int s = 0; s < subMeshCount; s++)
                mesh.SetIndices(indices[s], topologies[s], s, false);

            mesh.bounds = bounds;

            return true;

        }

    }

}
