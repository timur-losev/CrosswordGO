using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// Editor tool to auto-create and assign gold line visuals.
public static class LineDrawerVisualSetup
{
#if UNITY_EDITOR
    private const string MaterialsFolder = "Assets/WordChef/_Materials/Line";

    [MenuItem("Tools/Line Drawer/Setup Gold Line Visuals")]
    public static void SetupGoldLineVisuals()
    {
        EnsureFolder(MaterialsFolder);

        Texture2D goldLineTex = FindTexture("gold_line_tile");
        Texture2D shimmerTex = FindTexture("gold_shimmer_mask");
        Sprite starSprite = FindSprite("gold_star_sprite");

        ApplyTextureSettings(goldLineTex, isSprite: false, repeat: true);
        ApplyTextureSettings(shimmerTex, isSprite: false, repeat: true);
        ApplyTextureSettings(starSprite != null ? starSprite.texture : null, isSprite: true, repeat: false);

        Shader lineShader = Shader.Find("Unlit/Transparent");
        Shader additiveShader = Shader.Find("Particles/Additive");
        if (additiveShader == null)
        {
            additiveShader = Shader.Find("Sprites/Default");
        }

        Material goldMat = CreateOrUpdateMaterial(Path.Combine(MaterialsFolder, "GoldLine.mat"), lineShader, goldLineTex);
        Material shimmerMat = CreateOrUpdateMaterial(Path.Combine(MaterialsFolder, "GoldShimmer.mat"), additiveShader, shimmerTex);
        Material starMat = CreateOrUpdateMaterial(Path.Combine(MaterialsFolder, "GoldStar.mat"), additiveShader, null);

        int assigned = 0;
        foreach (var drawer in Resources.FindObjectsOfTypeAll<LineDrawer>())
        {
            if (drawer == null) continue;

            drawer.goldLineTexture = goldLineTex;
            drawer.shimmerTexture = shimmerTex;
            drawer.starSprite = starSprite;
            drawer.goldLineMaterial = goldMat;
            drawer.shimmerLineMaterial = shimmerMat;
            drawer.starMaterial = starMat;
            drawer.lineShader = lineShader;
            drawer.additiveShader = additiveShader;

            EditorUtility.SetDirty(drawer);
            if (!EditorUtility.IsPersistent(drawer))
            {
                EditorSceneManager.MarkSceneDirty(drawer.gameObject.scene);
            }
            assigned++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"LineDrawer visuals setup complete. Assigned to {assigned} object(s).");
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    private static Texture2D FindTexture(string name)
    {
        string[] guids = AssetDatabase.FindAssets($"{name} t:Texture2D");
        if (guids.Length == 0) return null;
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Sprite FindSprite(string name)
    {
        string[] guids = AssetDatabase.FindAssets($"{name} t:Sprite");
        if (guids.Length == 0) return null;
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void ApplyTextureSettings(Texture2D texture, bool isSprite, bool repeat)
    {
        if (texture == null) return;
        string path = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(path)) return;

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        importer.textureType = isSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        if (isSprite)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
        }

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
    }

    private static Material CreateOrUpdateMaterial(string path, Shader shader, Texture2D texture)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader != null ? shader : Shader.Find("Unlit/Transparent"));
            AssetDatabase.CreateAsset(mat, path);
        }

        if (shader != null && mat.shader != shader)
        {
            mat.shader = shader;
        }

        if (texture != null)
        {
            mat.mainTexture = texture;
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }
#endif
}
