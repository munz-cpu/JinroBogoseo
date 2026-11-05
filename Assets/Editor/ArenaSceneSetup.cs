using System.IO;
using System.Linq;
using BossTemplate;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class ArenaSceneSetup
{
    [MenuItem("Tools/Boss Arena/Apply Player and Camera")]
    private static void Apply()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
        var arena = Object.FindFirstObjectByType<BossArena>();
        if (!arena || arena.gameObject.scene.path != "Assets/Scenes/BossFight.unity")
            throw new System.InvalidOperationException("Open BossFight first.");
        var data = new SerializedObject(arena);
        var ground = (ArenaPlatform)data.FindProperty("ground").objectReferenceValue;
        var player = (SideViewPlayer)data.FindProperty("player").objectReferenceValue;
        var camera = (Camera)data.FindProperty("arenaCamera").objectReferenceValue;
        Undo.RecordObject(ground.transform, "Expand arena");
        ground.transform.localScale = new Vector3(44, ground.transform.localScale.y, 1);
        var platforms = data.FindProperty("platforms");
        int index = 0;
        for (int i = 0; i < platforms.arraySize; i++)
        {
            var platform = (ArenaPlatform)platforms.GetArrayElementAtIndex(i).objectReferenceValue;
            if (!platform || platform == ground) continue;
            Undo.RecordObject(platform.transform, "Expand platforms");
            platform.transform.position = new Vector3((index - 1) * 10, index == 1 ? .2f : -2.2f, 0);
            platform.transform.localScale = new Vector3(7, .3f, 1);
            index++;
        }

        TerminatorPlayerSetup.Apply();

        if (!camera.GetComponent<CinemachineBrain>()) Undo.AddComponent<CinemachineBrain>(camera.gameObject);
        var vcam = Object.FindFirstObjectByType<CinemachineCamera>();
        if (!vcam)
        {
            var go = new GameObject("플레이어 추적 카메라");
            Undo.RegisterCreatedObjectUndo(go, "Create Cinemachine camera");
            vcam = Undo.AddComponent<CinemachineCamera>(go);
        }
        Undo.RecordObject(vcam, "Configure Cinemachine");
        vcam.Follow = player.transform;
        vcam.Lens = LensSettings.FromCamera(camera);
        vcam.Lens.OrthographicSize = 6.6f;
        var composer = vcam.GetComponent<CinemachinePositionComposer>();
        if (!composer) composer = Undo.AddComponent<CinemachinePositionComposer>(vcam.gameObject);
        Undo.RecordObject(composer, "Configure follow");
        composer.CameraDistance = 10;
        composer.TargetOffset = new Vector3(0, 1.5f, 0);
        composer.Damping = new Vector3(.35f, .5f, 0);
        composer.Composition = ScreenComposerSettings.Default;
        composer.Composition.DeadZone.Enabled = true;
        composer.Composition.DeadZone.Size = new Vector2(.12f, .15f);
        var boundsObject = GameObject.Find("카메라 이동 경계");
        if (!boundsObject)
        {
            boundsObject = new GameObject("카메라 이동 경계");
            Undo.RegisterCreatedObjectUndo(boundsObject, "Create camera bounds");
        }
        var bounds = boundsObject.GetComponent<PolygonCollider2D>();
        if (!bounds) bounds = Undo.AddComponent<PolygonCollider2D>(boundsObject);
        bounds.isTrigger = true;
        bounds.points = new[] { new Vector2(-22,-6), new Vector2(-22,12), new Vector2(22,12), new Vector2(22,-6) };
        var confiner = vcam.GetComponent<CinemachineConfiner2D>();
        if (!confiner) confiner = Undo.AddComponent<CinemachineConfiner2D>(vcam.gameObject);
        confiner.BoundingShape2D = bounds;
        confiner.Damping = .2f;
        confiner.InvalidateBoundingShapeCache();
        EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
        EditorSceneManager.SaveScene(arena.gameObject.scene);
        File.WriteAllText("Temp/ArenaSetup.txt", "Applied Terminator, 44-unit ground, expanded platforms, Cinemachine follow and confiner.");
    }

    [MenuItem("Tools/Boss Arena/Report")]
    private static void Report()
    {
        var player = Object.FindFirstObjectByType<SideViewPlayer>();
        var vcam = Object.FindFirstObjectByType<CinemachineCamera>();
        var camera = Object.FindFirstObjectByType<Camera>();
        File.WriteAllText("Temp/ArenaReport.txt", "playing=" + Application.isPlaying
            + "\nplayer=" + player.transform.position + " sprite=" + player.GetComponent<SpriteRenderer>().sprite.name
            + " size=" + player.GetComponent<SpriteRenderer>().bounds.size
            + "\ncamera=" + camera.transform.position + " ortho=" + camera.orthographic
            + "\nfollow=" + (vcam.Follow == player.transform) + " valid=" + vcam.GetComponent<CinemachinePositionComposer>().IsValid
            + "\nbrain=" + camera.GetComponent<CinemachineBrain>().isActiveAndEnabled);
    }

    [MenuItem("Tools/Boss Arena/Validate Play Mode")]
    private static void ValidatePlayMode()
    {
        if (!Application.isPlaying) throw new System.InvalidOperationException("Enter Play Mode first.");
        var arena = Object.FindFirstObjectByType<BossArena>();
        var player = arena.Player;
        var move = typeof(SideViewPlayer).GetMethod("Move", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var body = player.GetComponent<SpriteRenderer>();
        var collision = new SerializedObject(player).FindProperty("collisionHalfSize").vector2Value;
        if (collision.x <= 0 || collision.y <= 0) collision = body.bounds.extents;
        for (int i = 0; i < 500; i++) move.Invoke(player, new object[] { 1f, false, false, .02f });
        bool right = Mathf.Abs(player.transform.position.x + collision.x - arena.GroundBounds.max.x) < .02f;
        for (int i = 0; i < 500; i++) move.Invoke(player, new object[] { -1f, false, false, .02f });
        bool left = Mathf.Abs(player.transform.position.x - collision.x - arena.GroundBounds.min.x) < .02f && body.flipX;
        bool floor = Mathf.Abs(player.transform.position.y - collision.y - arena.GroundY) < .02f;
        move.Invoke(player, new object[] { 0f, true, false, .1f });
        bool jump = player.transform.position.y - collision.y > arena.GroundY + .1f;
        var camera = Object.FindFirstObjectByType<CinemachineBrain>();
        var vcam = Object.FindFirstObjectByType<CinemachineCamera>();
        player.enabled = false;
        arena.Boss.StopAllCoroutines();
        player.transform.position = new Vector3(-12, -3.1f, 0);
        vcam.PreviousStateIsValid = false;
        double due = EditorApplication.timeSinceStartup + 1;
        float cameraLeft = 0;
        int stage = 0;
        EditorApplication.CallbackFunction check = null;
        check = () =>
        {
            if (!Application.isPlaying) { EditorApplication.update -= check; return; }
            if (EditorApplication.timeSinceStartup < due) return;
            if (stage++ == 0)
            {
                cameraLeft = camera.transform.position.x;
                player.transform.position = new Vector3(12, -3.1f, 0);
                due = EditorApplication.timeSinceStartup + 1;
                return;
            }
            EditorApplication.update -= check;
            bool follow = camera.transform.position.x > cameraLeft + 5;
            bool confined = Mathf.Abs(camera.transform.position.x) < 22;
            string report = $"right={right}\nleft={left}\nfloor={floor}\njump={jump}\nfollow={follow}\nconfined={confined}";
            File.WriteAllText("Temp/ArenaValidation.txt", report);
            player.enabled = true;
            arena.ResetFight();
            vcam.PreviousStateIsValid = false;
            if (!(right && left && floor && jump && follow && confined)) Debug.LogError(report);
        };
        EditorApplication.update += check;
    }
}




