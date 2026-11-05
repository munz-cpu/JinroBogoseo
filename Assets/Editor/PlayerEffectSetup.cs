using System.IO;
using System.Linq;
using BossTemplate;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class PlayerEffectSetup
{
    [MenuItem("Tools/Boss Arena/Apply Player Effects")]
    private static void Apply()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
        var arena = Object.FindFirstObjectByType<BossArena>();
        if (!arena || arena.gameObject.scene.path != "Assets/Scenes/BossFight.unity") throw new System.InvalidOperationException("Open BossFight first.");
        const string folder = "Assets/Art/Effects";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Art", "Effects");
        
        Mesh Shape(string name, bool ring)
        {
            string path = folder + "/" + name + "Mesh.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            var sprite = new Mesh { name = name };
            if (ring)
            {
                const int count = 32;
                var vertices = new Vector2[count * 2];
                var triangles = new ushort[count * 6];
                for (int i = 0; i < count; i++)
                {
                    float a = i * Mathf.PI * 2 / count;
                    Vector2 point = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    vertices[i * 2] = point * .5f; vertices[i * 2 + 1] = point * .39f;
                    int next = (i + 1) % count * 2, at = i * 6;
                    triangles[at] = (ushort)(i * 2); triangles[at + 1] = (ushort)next; triangles[at + 2] = (ushort)(i * 2 + 1);
                    triangles[at + 3] = (ushort)next; triangles[at + 4] = (ushort)(next + 1); triangles[at + 5] = (ushort)(i * 2 + 1);
                }
                sprite.vertices = System.Array.ConvertAll(vertices, v => (Vector3)v); sprite.triangles = System.Array.ConvertAll(triangles, t => (int)t); sprite.RecalculateBounds();
            }
            else
            {
                var vertices = new Vector2[9];
                var triangles = new ushort[24];
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4;
                    vertices[i + 1] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? .5f : .15f);
                    triangles[i * 3] = 0; triangles[i * 3 + 1] = (ushort)(i + 1); triangles[i * 3 + 2] = (ushort)((i + 1) % 8 + 1);
                }
                sprite.vertices = System.Array.ConvertAll(vertices, v => (Vector3)v); sprite.triangles = System.Array.ConvertAll(triangles, t => (int)t); sprite.RecalculateBounds();
            }
            if (existing) { EditorUtility.CopySerialized(sprite, existing); Object.DestroyImmediate(sprite); sprite = existing; EditorUtility.SetDirty(sprite); }
            else AssetDatabase.CreateAsset(sprite, path);
            return sprite;
        }
        var serialized = new SerializedObject(arena);
        serialized.FindProperty("sparkMesh").objectReferenceValue = Shape("발사섬광", false);
        serialized.FindProperty("jumpRingMesh").objectReferenceValue = Shape("더블점프링", true);
        serialized.ApplyModifiedProperties();
        const string materialPath = folder + "/EffectUnlit.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", 5); material.SetFloat("_DstBlend", 10); material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000;
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetFloat("_Cull", 0); EditorUtility.SetDirty(material);
        var pool = serialized.FindProperty("effects");
        for (int i = 0; i < pool.arraySize; i++)
        {
            var effect = (ArenaEffect)pool.GetArrayElementAtIndex(i).objectReferenceValue;
            var child = effect.transform.Find("연출 메시");
            if (!child) { var go = new GameObject("연출 메시"); Undo.RegisterCreatedObjectUndo(go, "Create effect mesh"); go.transform.SetParent(effect.transform, false); child = go.transform; }
            if (!child.GetComponent<MeshFilter>()) Undo.AddComponent<MeshFilter>(child.gameObject);
            var renderer = child.GetComponent<MeshRenderer>();
            if (!renderer) renderer = Undo.AddComponent<MeshRenderer>(child.gameObject);
            renderer.sharedMaterial = material;
            renderer.sortingLayerID = effect.GetComponent<SpriteRenderer>().sortingLayerID;
            renderer.sortingOrder = effect.GetComponent<SpriteRenderer>().sortingOrder + 1;
            renderer.enabled = false;
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
        EditorSceneManager.SaveScene(arena.gameObject.scene);
    }

    [MenuItem("Tools/Boss Arena/Validate Player Effects")]
    private static void Validate()
    {
        if (!Application.isPlaying) throw new System.InvalidOperationException("Enter Play Mode first.");
        EditorApplication.isPaused = false;
        var arena = Object.FindFirstObjectByType<BossArena>(); arena.ResetFight();
        arena.Boss.StopAllCoroutines(); arena.Player.enabled = false;
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var move = typeof(SideViewPlayer).GetMethod("Move", flags);
        var shoot = typeof(SideViewPlayer).GetMethod("Shoot", flags);
        int Count() => Object.FindObjectsByType<ArenaEffect>(FindObjectsSortMode.None).Length;
        move.Invoke(arena.Player, new object[] { 0f, true, false, .02f });
        bool firstJump = Count() == 0;
        move.Invoke(arena.Player, new object[] { 0f, true, false, .02f });
        bool doubleJump = Count() == 4;
        move.Invoke(arena.Player, new object[] { 0f, true, false, .02f });
        bool noThirdJump = Count() == 4;
        shoot.Invoke(arena.Player, new object[] { new Vector2(15,0), 7f });
        bool gun = Count() == 6;
        var effects = Object.FindObjectsByType<ArenaEffect>(FindObjectsSortMode.None);
        var ring = effects.First(e => e.GetComponentInChildren<MeshFilter>(true).sharedMesh != null && e.GetComponentInChildren<MeshFilter>(true).sharedMesh.name == "더블점프링");
        float initial = ring.transform.localScale.x;
        float started = Time.time;
        bool expanded = false;
        EditorApplication.CallbackFunction check = null;
        check = () =>
        {
            if (!Application.isPlaying) { EditorApplication.update -= check; return; }
            if (ring.gameObject.activeSelf && ring.transform.localScale.x > initial) expanded = true;
            if (Time.time - started < .5f) return;
            EditorApplication.update -= check;
            bool cleanup = Count() == 0;
            var reusable = effects[0];
            reusable.Show(Vector2.zero, Vector2.one, Color.red, .1f);
            bool restored = reusable.GetComponent<SpriteRenderer>().sprite.name == "Square" && reusable.transform.rotation == Quaternion.identity;
            arena.ResetFight(); arena.Player.enabled = true;
            bool reset = Count() == 0;
            string report = $"firstJumpNoEffect={firstJump}\ndoubleJumpEffect={doubleJump}\nnoThirdJumpEffect={noThirdJump}\ngunEffect={gun}\nringExpands={expanded}\ncleanup={cleanup}\npoolRestoresSprite={restored}\nreset={reset}";
            File.WriteAllText("Temp/PlayerEffectsValidation.txt", report);
            if (!(firstJump && doubleJump && noThirdJump && gun && expanded && cleanup && restored && reset)) Debug.LogError(report);
        };
        EditorApplication.update += check;
    }
    [MenuItem("Tools/Boss Arena/Preview Player Effects")]
    private static void Preview()
    {
        EditorApplication.isPaused = false;
        var arena = Object.FindFirstObjectByType<BossArena>(); arena.ResetFight();
        arena.Boss.StopAllCoroutines(); arena.Player.enabled = false;
        arena.Player.transform.position += Vector3.up * 2;
        arena.DoubleJumpFlash((Vector2)arena.Player.transform.position - Vector2.up * .9f);
        arena.GunFlash((Vector2)arena.Player.transform.position + new Vector2(1.1f,.62f), 1);
        var visual = arena.Player.GetComponent<PlayerSpriteAnimation>();
        visual.SetState(0, false, 5, 1, false); visual.Fire();
        typeof(PlayerSpriteAnimation).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(visual, null);
        EditorApplication.isPaused = true;
    }
}



