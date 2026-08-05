using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Generates the gray-box overworld tile set: a mesh, a prefab, a material and a UV guide
    /// PNG per tile, plus <c>Plans/Tile-UV-Guide.md</c> describing every layout.
    ///
    /// <para>Idempotent, and it preserves GUIDs: meshes are refilled in place, prefabs re-saved
    /// over their own path, materials updated rather than recreated — so scene references and
    /// the future renderer's tile-set asset survive a regenerate.</para>
    ///
    /// <para>Reskinning: each material's base map starts as that tile's UV guide texture. Point
    /// it at a painted texture instead and the tile is skinned; regenerating will NOT overwrite
    /// a texture that lives outside the UVGuides folder. If texturing goes triplanar instead,
    /// swap the shader on the materials — the UVs simply go dormant.</para>
    /// </summary>
    public static class OverworldTileBuilder
    {
        private const string TilesFolder = "Assets/_Prophecy/Art/Tiles";
        private const string GuidesFolder = TilesFolder + "/UVGuides";
        private const int GuideSize = 1024;

        /// <summary>The runtime tile set the overworld host renders from, auto-filled here.</summary>
        public const string TileSetPath = "Assets/_Prophecy/Data/OverworldTileSet.asset";

        private static readonly Dictionary<TileRegion, Color32> RegionColours =
            new Dictionary<TileRegion, Color32>
            {
                { TileRegion.Top, new Color32(107, 166, 97, 255) },
                { TileRegion.Face, new Color32(140, 140, 148, 255) },
                { TileRegion.Rim, new Color32(199, 158, 107, 255) },
                { TileRegion.Skirt, new Color32(89, 71, 56, 255) },
                { TileRegion.Riser, new Color32(168, 163, 153, 255) },
                { TileRegion.Bank, new Color32(212, 189, 133, 255) },
                { TileRegion.Trim, new Color32(64, 64, 72, 255) },
                { TileRegion.Water, new Color32(89, 140, 191, 255) },
            };

        [MenuItem("Prophecy/Build/Generate Overworld Tiles", priority = 45)]
        public static void Generate()
        {
            EnsureFolders();

            List<TileMeshData> tiles = OverworldTileGeometry.BuildAll();
            var guide = new StringBuilder();
            BeginGuide(guide);

            foreach (TileMeshData tile in tiles)
            {
                Mesh mesh = EnsureMesh(tile);
                Texture2D guideTex = WriteGuidePng(tile);
                Material material = EnsureMaterial(tile, guideTex);
                EnsurePrefab(tile, mesh, material);
                AppendGuideEntry(guide, tile);
            }

            FillTileSet();

            AssetDatabase.SaveAssets();
            WriteGuideDoc(guide.ToString());

            Debug.Log($"[Prophecy] Generated {tiles.Count} overworld tiles into {TilesFolder} " +
                      "(mesh + prefab + material + UV guide each), filled the OverworldTileSet, " +
                      "and refreshed Plans/Tile-UV-Guide.md.");
        }

        /// <summary>
        /// Create and fill the runtime <see cref="Rokkan.Prophecy.Overworld.OverworldTileSet"/>
        /// from the generated prefabs — seventeen slots wired by id, never by hand.
        /// </summary>
        private static void FillTileSet()
        {
            var set = AssetDatabase.LoadAssetAtPath<Rokkan.Prophecy.Overworld.OverworldTileSet>(TileSetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<Rokkan.Prophecy.Overworld.OverworldTileSet>();
                AssetDatabase.CreateAsset(set, TileSetPath);
            }

            set.Cap = LoadPrefab("GRD_Cap");
            set.Ramp = LoadPrefab("RMP_Ramp");
            set.Stairs = LoadPrefab("RMP_Stairs");
            set.Water = LoadPrefab("WTR_Fill");
            set.Face1 = LoadPrefab("CLF_Face1");
            set.Face2 = LoadPrefab("CLF_Face2");
            set.Face3 = LoadPrefab("CLF_Face3");
            set.OuterPost1 = LoadPrefab("CLF_OuterPost1");
            set.OuterPost2 = LoadPrefab("CLF_OuterPost2");
            set.OuterPost3 = LoadPrefab("CLF_OuterPost3");
            set.InnerPost1 = LoadPrefab("CLF_InnerPost1");
            set.InnerPost2 = LoadPrefab("CLF_InnerPost2");
            set.InnerPost3 = LoadPrefab("CLF_InnerPost3");
            set.Cheek = LoadPrefab("CLF_Cheek");
            set.CheekMirrored = LoadPrefab("CLF_CheekM");
            set.ShoreEdge = LoadPrefab("SHR_Edge");
            set.ShoreOuterPost = LoadPrefab("SHR_OuterPost");

            EditorUtility.SetDirty(set);

            // The inner shore post was cut (two banks meeting inward already form the valley);
            // sweep its orphaned assets so the folder matches the set.
            AssetDatabase.DeleteAsset($"{TilesFolder}/Tile_SHR_InnerPost.prefab");
            AssetDatabase.DeleteAsset($"{TilesFolder}/Tile_SHR_InnerPost.asset");
            AssetDatabase.DeleteAsset($"{TilesFolder}/Tile_SHR_InnerPost.mat");
            AssetDatabase.DeleteAsset($"{GuidesFolder}/Tile_SHR_InnerPost_UV.png");
        }

        private static GameObject LoadPrefab(string id) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{TilesFolder}/Tile_{id}.prefab");

        // ---------------------------------------------------------------- assets

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Prophecy/Art"))
                AssetDatabase.CreateFolder("Assets/_Prophecy", "Art");
            if (!AssetDatabase.IsValidFolder(TilesFolder))
                AssetDatabase.CreateFolder("Assets/_Prophecy/Art", "Tiles");
            if (!AssetDatabase.IsValidFolder(GuidesFolder))
                AssetDatabase.CreateFolder(TilesFolder, "UVGuides");
        }

        private static Mesh EnsureMesh(TileMeshData tile)
        {
            string path = $"{TilesFolder}/Tile_{tile.Id}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (mesh == null)
            {
                mesh = new Mesh { name = $"Tile_{tile.Id}" };
                tile.CopyInto(mesh);
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                tile.CopyInto(mesh);
                EditorUtility.SetDirty(mesh);
            }

            return mesh;
        }

        private static Material EnsureMaterial(TileMeshData tile, Texture2D guideTex)
        {
            string path = $"{TilesFolder}/Tile_{tile.Id}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            // The guide is the placeholder skin. A real texture (anything outside UVGuides)
            // survives a regenerate — that is the reskin contract.
            Texture current = material.GetTexture("_BaseMap");
            bool holdsRealSkin = current != null &&
                                 !AssetDatabase.GetAssetPath(current).Contains("/UVGuides/");
            if (!holdsRealSkin)
            {
                material.SetTexture("_BaseMap", guideTex);
                material.SetColor("_BaseColor", Color.white);
            }

            // Matte, every regenerate: the URP default smoothness puts a specular glint on every
            // stair riser edge, and glints crawling under a moving camera read exactly like
            // z-fighting. Gray-box rock does not gleam.
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_SpecularHighlights", 0f);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.SetFloat("_EnvironmentReflections", 0f);
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsurePrefab(TileMeshData tile, Mesh mesh, Material material)
        {
            string path = $"{TilesFolder}/Tile_{tile.Id}.prefab";
            var go = new GameObject($"Tile_{tile.Id}");

            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- the UV guide PNG

        private static Texture2D WriteGuidePng(TileMeshData tile)
        {
            var px = new Color32[GuideSize * GuideSize];
            var background = new Color32(245, 245, 242, 255);
            for (int i = 0; i < px.Length; i++) px[i] = background;

            for (int t = 0; t < tile.TriangleRegions.Count; t++)
            {
                Vector2 a = tile.Uvs[tile.Triangles[t * 3]] * GuideSize;
                Vector2 b = tile.Uvs[tile.Triangles[t * 3 + 1]] * GuideSize;
                Vector2 c = tile.Uvs[tile.Triangles[t * 3 + 2]] * GuideSize;
                FillTriangle(px, a, b, c, RegionColours[tile.TriangleRegions[t]]);
            }

            // No wireframe on the water: it is the one piece that stretches (to the whole sea),
            // and its triangulation diagonal became a sixty-metre dark stripe across the map.
            if (tile.Id != "WTR_Fill")
            {
                var edge = new Color32(50, 50, 54, 255);
                for (int t = 0; t < tile.TriangleRegions.Count; t++)
                {
                    Vector2 a = tile.Uvs[tile.Triangles[t * 3]] * GuideSize;
                    Vector2 b = tile.Uvs[tile.Triangles[t * 3 + 1]] * GuideSize;
                    Vector2 c = tile.Uvs[tile.Triangles[t * 3 + 2]] * GuideSize;
                    DrawLine(px, a, b, edge);
                    DrawLine(px, b, c, edge);
                    DrawLine(px, c, a, edge);
                }
            }

            // Dilate island colours into the gutters: under minification the sampler averages
            // across island borders, and a bright background gutter bleeds a crawling white
            // line onto every tread nosing and rim edge. After dilation the borders sample
            // their own colour.
            for (int pass = 0; pass < 8; pass++)
            {
                var src = (Color32[])px.Clone();
                for (int y = 0; y < GuideSize; y++)
                {
                    for (int x = 0; x < GuideSize; x++)
                    {
                        int i = y * GuideSize + x;
                        if (!SameColour(src[i], background)) continue;

                        if (x > 0 && !SameColour(src[i - 1], background)) px[i] = src[i - 1];
                        else if (x < GuideSize - 1 && !SameColour(src[i + 1], background)) px[i] = src[i + 1];
                        else if (y > 0 && !SameColour(src[i - GuideSize], background)) px[i] = src[i - GuideSize];
                        else if (y < GuideSize - 1 && !SameColour(src[i + GuideSize], background)) px[i] = src[i + GuideSize];
                    }
                }
            }

            var tex = new Texture2D(GuideSize, GuideSize, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();

            string relPath = $"{GuidesFolder}/Tile_{tile.Id}_UV.png";
            string absPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relPath));
            File.WriteAllBytes(absPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(relPath);
            if (AssetImporter.GetAtPath(relPath) is TextureImporter importer &&
                importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(relPath);
        }

        private static void FillTriangle(Color32[] px, Vector2 a, Vector2 b, Vector2 c, Color32 colour)
        {
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, GuideSize - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, GuideSize - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, GuideSize - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, GuideSize - 1);

            float area = Cross(a, b, c);
            if (Mathf.Abs(area) < 0.0001f) return;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Cross(a, b, p) / area;
                    float w1 = Cross(b, c, p) / area;
                    float w2 = Cross(c, a, p) / area;
                    if (w0 >= 0f && w1 >= 0f && w2 >= 0f)
                        px[y * GuideSize + x] = colour;
                }
            }
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 p) =>
            (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        private static bool SameColour(Color32 a, Color32 b) =>
            a.r == b.r && a.g == b.g && a.b == b.b;

        private static void DrawLine(Color32[] px, Vector2 a, Vector2 b, Color32 colour)
        {
            int x0 = Mathf.Clamp(Mathf.RoundToInt(a.x), 0, GuideSize - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(a.y), 0, GuideSize - 1);
            int x1 = Mathf.Clamp(Mathf.RoundToInt(b.x), 0, GuideSize - 1);
            int y1 = Mathf.Clamp(Mathf.RoundToInt(b.y), 0, GuideSize - 1);

            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                px[y0 * GuideSize + x0] = colour;
                if (x0 == x1 && y0 == y1) break;
                int e2 = err * 2;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        // ---------------------------------------------------------------- the markdown guide

        private static void BeginGuide(StringBuilder sb)
        {
            sb.AppendLine("# Tile UV guide — GENERATED, do not hand-edit");
            sb.AppendLine();
            sb.AppendLine("Written by `Prophecy > Build > Generate Overworld Tiles` " +
                          "(`OverworldTileBuilder`). Regenerate after any geometry change.");
            sb.AppendLine();
            sb.AppendLine("Each tile's UVs fill its own 0–1 square; its guide PNG " +
                          "(`Assets/_Prophecy/Art/Tiles/UVGuides/Tile_<Id>_UV.png`, 1024²) shows the " +
                          "islands colour-coded by the legend below, and ships as the tile material's " +
                          "base map. **To reskin a tile:** paint (or generate) a texture over its guide " +
                          "and point `Tile_<Id>.mat`'s base map at it — regenerating never overwrites a " +
                          "base map that lives outside the UVGuides folder. If texturing goes triplanar " +
                          "instead, swap the shader on the materials; the UVs go dormant, not wrong.");
            sb.AppendLine();
            sb.AppendLine("UV rects are (u, v) with v UP — image editors put pixel y down, so the pixel " +
                          "row is `1024 − v·1024`. Same colour = same meaning on every tile:");
            sb.AppendLine();
            sb.AppendLine("| Region | Colour | Meaning |");
            sb.AppendLine("|---|---|---|");
            foreach (KeyValuePair<TileRegion, Color32> pair in RegionColours)
                sb.AppendLine($"| {pair.Key} | `#{pair.Value.r:X2}{pair.Value.g:X2}{pair.Value.b:X2}` | {Meaning(pair.Key)} |");
            sb.AppendLine();
            sb.AppendLine("---");
        }

        private static string Meaning(TileRegion region)
        {
            switch (region)
            {
                case TileRegion.Top: return "walkable surface — caps, treads, the ramp grade";
                case TileRegion.Face: return "vertical rock — cliff fronts, wedge sides, stair columns";
                case TileRegion.Rim: return "the ALttP lip along a cliff top edge";
                case TileRegion.Skirt: return "buried / underwater continuation below the footline";
                case TileRegion.Riser: return "stair fronts";
                case TileRegion.Bank: return "shore slope";
                case TileRegion.Trim: return "hidden or mating surfaces — backs, bottoms, flat cut ends";
                case TileRegion.Water: return "the sea plane";
                default: return "";
            }
        }

        private static void AppendGuideEntry(StringBuilder sb, TileMeshData tile)
        {
            sb.AppendLine();
            sb.AppendLine($"## Tile_{tile.Id} — {tile.Display}");
            sb.AppendLine();
            sb.AppendLine(tile.WorldNote);
            sb.AppendLine();
            sb.AppendLine("| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |");
            sb.AppendLine("|---|---|---|---|");
            foreach (RegionNote note in tile.Notes)
            {
                Rect r = note.UvRect;
                sb.AppendLine($"| {note.Region} " +
                              $"| ({r.xMin:0.00}, {r.yMin:0.00} → {r.xMax:0.00}, {r.yMax:0.00}) " +
                              $"| ({Mathf.RoundToInt(r.xMin * GuideSize)}, {Mathf.RoundToInt(r.yMin * GuideSize)}) → " +
                              $"({Mathf.RoundToInt(r.xMax * GuideSize)}, {Mathf.RoundToInt(r.yMax * GuideSize)}) " +
                              $"| {note.What} |");
            }
        }

        private static void WriteGuideDoc(string content)
        {
            string plans = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Plans"));
            if (!Directory.Exists(plans))
            {
                Debug.LogWarning($"[Prophecy] Plans folder not found at {plans}; skipping Tile-UV-Guide.md.");
                return;
            }

            File.WriteAllText(Path.Combine(plans, "Tile-UV-Guide.md"), content);
        }
    }
}
