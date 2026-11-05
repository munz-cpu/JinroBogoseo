using System.IO;
using BossTemplate;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class TerminatorPlayerSetup
{
    [MenuItem("Tools/Boss Arena/Validate Crouching")]
    private static void ValidateCrouching()
    {
        if (!Application.isPlaying) throw new System.InvalidOperationException("Enter Play Mode first.");
        var arena = Object.FindFirstObjectByType<BossArena>(); arena.ResetFight(); arena.Boss.StopAllCoroutines();
        var player = arena.Player; player.enabled = false;
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var move = typeof(SideViewPlayer).GetMethod("Move", flags);
        var half = typeof(SideViewPlayer).GetField("halfSize", flags);
        var visual = player.GetComponent<PlayerSpriteAnimation>();
        var late = typeof(PlayerSpriteAnimation).GetMethod("LateUpdate", flags);
        var body = player.GetComponent<SpriteRenderer>();
        float startY = player.transform.position.y, startX = player.transform.position.x;
        move.Invoke(player, new object[] { 1f, false, true, .02f });
        visual.SetState(0, true, 0, 1, false); late.Invoke(visual, null);
        bool crouch = body.sprite.name == "CHaracter121" && Mathf.Abs(((Vector2)half.GetValue(player)).y - .6f) < .01f;
        bool feet = Mathf.Abs(player.transform.position.y - .6f - arena.GroundY) < .01f;
        bool stationary = Mathf.Abs(player.transform.position.x - startX) < .01f;
        typeof(SideViewPlayer).GetMethod("Shoot", flags).Invoke(player, new object[] { new Vector2(15,0), 7f });
        late.Invoke(visual, null);
        bool fire = body.sprite.name == "CHaracter122";
        move.Invoke(player, new object[] { 0f, false, false, .02f });
        bool stand = Mathf.Abs(player.transform.position.y - startY) < .01f && Mathf.Abs(((Vector2)half.GetValue(player)).y - .9f) < .01f;
        arena.ResetFight();
        var data = new SerializedObject(arena); var array = data.FindProperty("platforms");
        var ground = (ArenaPlatform)data.FindProperty("ground").objectReferenceValue;
        ArenaPlatform platform = null;
        for (int i=0; i<array.arraySize; i++) { var p=(ArenaPlatform)array.GetArrayElementAtIndex(i).objectReferenceValue; if (p && p != ground) { platform=p; break; } }
        player.transform.position = new Vector3(platform.Bounds.center.x, platform.Bounds.max.y + .9f, 0);
        move.Invoke(player, new object[] { 0f, false, true, .02f });
        move.Invoke(player, new object[] { 0f, true, true, .1f });
        bool drop = player.transform.position.y - .9f < platform.Bounds.max.y;
        arena.ResetFight(); player.enabled = true;
        string report=$"crouchSpriteAndHeight={crouch}\nfeetStayOnFloor={feet}\nstationary={stationary}\ncrouchFire={fire}\nstandRestoresHeight={stand}\ndropThroughPreserved={drop}";
        File.WriteAllText("Temp/CrouchValidation.txt",report);
        if (!(crouch && feet && stationary && fire && stand && drop)) throw new System.Exception(report);
    }
    [MenuItem("Tools/Boss Arena/Preview Terminator Shooting")]
    private static void Preview()
    {
        var arena = Object.FindFirstObjectByType<BossArena>();
        arena.ResetFight(); arena.Boss.StopAllCoroutines();
        arena.Player.enabled = false;
        var visual = arena.Player.GetComponent<PlayerSpriteAnimation>();
        visual.SetState(1, true, 0, 1, false); visual.Fire();
        typeof(PlayerSpriteAnimation).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(visual, null);
        EditorApplication.isPaused = true;
        File.WriteAllText("Temp/TerminatorRender.txt", string.Join("\n", System.Array.ConvertAll(arena.Player.GetComponentsInChildren<SpriteRenderer>(), r => r.name + " sprite=" + r.sprite.name + " bounds=" + r.bounds + " scale=" + r.transform.lossyScale)));
    }
    [MenuItem("Tools/Boss Arena/Validate Terminator Player")]
    private static void Validate()
    {
        if (!Application.isPlaying) throw new System.InvalidOperationException("Enter Play Mode first.");
        var arena = Object.FindFirstObjectByType<BossArena>();
        arena.ResetFight();
        var player = arena.Player;
        var visual = player.GetComponent<PlayerSpriteAnimation>();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var late = typeof(PlayerSpriteAnimation).GetMethod("LateUpdate", flags);
        var move = typeof(SideViewPlayer).GetMethod("Move", flags);
        var shoot = typeof(SideViewPlayer).GetMethod("Shoot", flags);
        var body = player.GetComponent<SpriteRenderer>();
        var torso = player.transform.Find("사격 상체").GetComponent<SpriteRenderer>();
        var legs = player.transform.Find("사격 하체").GetComponent<SpriteRenderer>();
        visual.SetState(0, true, 0, 1, false); late.Invoke(visual, null);
        bool idle = body.enabled && body.sprite.name.StartsWith("CHaracter");
        float before = player.transform.position.x;
        move.Invoke(player, new object[] { 1f, false, false, .1f });
        visual.SetState(1, true, 0, 1, false);
        shoot.Invoke(player, new object[] { new Vector2(15, 0), 7f }); late.Invoke(visual, null);
        bool runShoot = player.transform.position.x > before && !body.enabled && torso.enabled && legs.enabled && legs.sprite.name == "CHaracter141" && torso.sprite.name == "CHaracter202";
        bool projectile = Object.FindObjectsByType<ArenaProjectile>(FindObjectsSortMode.None).Length > 0;
        visual.SetState(0, true, 0, -1, false); visual.Fire(); late.Invoke(visual, null);
        bool idleShoot = torso.enabled && legs.sprite.name == "CHaracter131" && !torso.flipX && legs.flipX && torso.sprite.name == "CHaracter202";
        visual.SetState(1, false, 5, 1, false); late.Invoke(visual, null);
        bool airShoot = torso.enabled && legs.sprite.name == "CHaracter172";
        visual.SetState(0, true, 0, 1, true); late.Invoke(visual, null);
        bool death = body.enabled && body.sprite.name == "CHaracter91";
        arena.ResetFight(); late.Invoke(visual, null);
        bool reset = body.enabled && !torso.enabled && !legs.enabled && body.sprite.name == "CHaracter1";
        string report = $"idle={idle}\nrunShoot={runShoot}\nindependentProjectile={projectile}\nidleShootAndFlip={idleShoot}\nairShoot={airShoot}\ndeath={death}\nreset={reset}";
        File.WriteAllText("Temp/TerminatorValidation.txt", report);
        if (!(idle && runShoot && projectile && idleShoot && airShoot && death && reset)) throw new System.Exception(report);
    }

    [MenuItem("Tools/Boss Arena/Apply Terminator Player")]
    internal static void Apply()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
        var player = Object.FindFirstObjectByType<SideViewPlayer>();
        if (!player || player.gameObject.scene.path != "Assets/Scenes/BossFight.unity") throw new System.InvalidOperationException("Open BossFight first.");
        var files = Directory.GetFiles("Assets/Images/Player", "CHaracter1.png", SearchOption.AllDirectories);
        if (files.Length != 1) throw new System.InvalidOperationException("Expected one Terminator character pack.");
        string folder = Path.GetDirectoryName(files[0]).Replace('\\', '/');
        Sprite Load(int number)
        {
            string path = folder + "/CHaracter" + number + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 41;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(65f / 148, (number == 121 || number == 122 ? 39.7f : 52f) / 148);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        var body = player.GetComponent<SpriteRenderer>();
        Undo.RecordObjects(new Object[] { body, player.transform }, "Apply Terminator player");
        body.sprite = Load(1); body.color = Color.white; body.enabled = true;
        player.transform.localScale = Vector3.one;
        var arena = new SerializedObject(Object.FindFirstObjectByType<BossArena>());
        var ground = (ArenaPlatform)arena.FindProperty("ground").objectReferenceValue;
        player.transform.position = new Vector3(player.transform.position.x, ground.Bounds.max.y + .9f, 0);
        var movement = new SerializedObject(player);
        movement.FindProperty("collisionHalfSize").vector2Value = new Vector2(.45f, .9f);
        movement.ApplyModifiedProperties();
        var animation = player.GetComponent<PlayerSpriteAnimation>();
        if (!animation) animation = Undo.AddComponent<PlayerSpriteAnimation>(player.gameObject);
        SpriteRenderer Part(string name, int order)
        {
            var child = player.transform.Find(name);
            if (!child) { var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Create character part"); go.transform.SetParent(player.transform, false); child = go.transform; }
            var renderer = child.GetComponent<SpriteRenderer>();
            if (!renderer) renderer = Undo.AddComponent<SpriteRenderer>(child.gameObject);
            renderer.sharedMaterial = body.sharedMaterial;
            renderer.sortingLayerID = body.sortingLayerID;
            renderer.sortingOrder = body.sortingOrder + order;
            renderer.enabled = false;
            return renderer;
        }
        var serialized = new SerializedObject(animation);
        void Sequence(string field, int first, int count)
        {
            var array = serialized.FindProperty(field); array.arraySize = count;
            for (int i = 0; i < count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = Load(first + i);
        }
        Sequence("idle", 1, 8); Sequence("run", 11, 9); Sequence("jump", 41, 5); Sequence("death", 91, 4);
        Sequence("upperBody", 241, 8); Sequence("idleLegs", 131, 8); Sequence("runLegs", 141, 9); Sequence("jumpLegs", 171, 5);
        serialized.FindProperty("firingUpperBody").objectReferenceValue = Load(202);
        serialized.FindProperty("crouchIdle").objectReferenceValue = Load(121);
        serialized.FindProperty("crouchFire").objectReferenceValue = Load(122);
        serialized.FindProperty("torso").objectReferenceValue = Part("사격 상체", 2);
        serialized.FindProperty("legs").objectReferenceValue = Part("사격 하체", 1);
        serialized.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorSceneManager.SaveScene(player.gameObject.scene);
        File.WriteAllText("Temp/TerminatorSetup.txt", "Terminator player applied; idle/run/jump/death and modular run-and-shoot, independent projectiles.");
    }
}
