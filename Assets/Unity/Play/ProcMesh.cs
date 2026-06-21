using System.Collections.Generic;
using UnityEngine;

namespace Greenkeeper.Unity.Play
{
    /// <summary>
    /// Tiny procedural-mesh helper so the course is built from ORGANIC shapes (curved fairways, kidney
    /// greens, blob bunkers) instead of rectangular planes. All meshes lie in the local XZ plane, face
    /// +Y, carry world-scaled UVs (for tiling textures), and are wound up-facing regardless of input.
    /// </summary>
    public static class ProcMesh
    {
        /// <summary>A filled star-convex blob from per-angle radii (greens, tees, bunkers, water).</summary>
        public static Mesh Blob(float[] radii, float tile)
        {
            int n = radii.Length;
            var verts = new List<Vector3>(n + 1) { Vector3.zero };
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                verts.Add(new Vector3(Mathf.Cos(a) * radii[i], 0f, Mathf.Sin(a) * radii[i]));
            }
            var tris = new List<int>(n * 3);
            for (int i = 0; i < n; i++) { tris.Add(0); tris.Add(1 + i); tris.Add(1 + (i + 1) % n); }
            return Build(verts, tris, tile);
        }

        /// <summary>Per-angle radii for an ellipse with organic noise and an optional kidney dent.</summary>
        public static float[] EllipseRadii(float rx, float rz, int seg, float noise, int seed, float kidney)
        {
            var r = new float[seg];
            var rnd = new System.Random(seed);
            for (int i = 0; i < seg; i++)
            {
                float a = 2f * Mathf.PI * i / seg;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float baseR = (rx * rz) / Mathf.Sqrt(rz * ca * rz * ca + rx * sa * rx * sa);
                float nz = 1f + noise * ((float)rnd.NextDouble() * 2f - 1f);
                float dent = 1f - kidney * Mathf.Max(0f, Mathf.Cos(a - 0.7f)); // soft concave on one side
                r[i] = baseR * nz * dent;
            }
            return r;
        }

        /// <summary>A ribbon (fairway, rough corridor, cart path) along a centreline with per-point half-width.</summary>
        public static Mesh Ribbon(IList<Vector2> center, IList<float> half, float tile)
        {
            int m = center.Count;
            var verts = new List<Vector3>(m * 2);
            for (int i = 0; i < m; i++)
            {
                Vector2 dir = i == 0 ? center[1] - center[0]
                            : i == m - 1 ? center[m - 1] - center[m - 2]
                            : center[i + 1] - center[i - 1];
                dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.up;
                Vector2 nrm = new Vector2(-dir.y, dir.x);
                Vector2 l = center[i] + nrm * half[i];
                Vector2 r = center[i] - nrm * half[i];
                verts.Add(new Vector3(l.x, 0f, l.y));
                verts.Add(new Vector3(r.x, 0f, r.y));
            }
            var tris = new List<int>((m - 1) * 6);
            for (int i = 0; i < m - 1; i++)
            {
                int a = 2 * i, b = 2 * i + 1, c = 2 * i + 2, d = 2 * i + 3;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
            return Build(verts, tris, tile);
        }

        /// <summary>Catmull-Rom through control points, sampled to a smooth polyline.</summary>
        public static List<Vector2> Smooth(IList<Vector2> ctrl, int perSegment)
        {
            var pts = new List<Vector2>();
            int c = ctrl.Count;
            for (int i = 0; i < c - 1; i++)
            {
                Vector2 p0 = ctrl[Mathf.Max(0, i - 1)], p1 = ctrl[i], p2 = ctrl[i + 1], p3 = ctrl[Mathf.Min(c - 1, i + 2)];
                for (int s = 0; s < perSegment; s++)
                {
                    float t = s / (float)perSegment;
                    pts.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }
            pts.Add(ctrl[c - 1]);
            return pts;
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t
                 + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static Mesh Build(List<Vector3> verts, List<int> tris, float tile)
        {
            // Force up-facing winding (so the lit side is the top) regardless of how the caller wound it.
            if (tris.Count >= 3)
            {
                var nrm = Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]);
                if (nrm.y < 0f)
                    for (int i = 0; i < tris.Count; i += 3) { int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }
            }
            var uv = new Vector2[verts.Count];
            float inv = tile > 0f ? 1f / tile : 1f;
            for (int i = 0; i < verts.Count; i++) uv[i] = new Vector2(verts[i].x * inv, verts[i].z * inv);

            var mesh = new Mesh { name = "ProcMesh" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
