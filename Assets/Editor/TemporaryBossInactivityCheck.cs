internal static class TemporaryBossInactivityCheck
{
    [UnityEditor.InitializeOnLoadMethod]
    private static void Run()
    {
        UnityEditor.EditorApplication.delayCall += Check;
    }

    [UnityEditor.MenuItem("Tools/Boss Arena/Research/Temporary Inactivity Check")]
    private static void Check()
    {
        string report = System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Temp", "BossInactivityCheck.result");
        try
        {

var assembly = System.Array.Find(System.AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "Assembly-CSharp");
System.Type Study(string name) => assembly.GetType("BossTemplate." + name, true);
const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
object New(string name) => System.Activator.CreateInstance(Study(name), true);
object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Any).Invoke(owner, args);
object Get(object owner, string field) => owner.GetType().GetField(field, Any).GetValue(owner);
void Set(object owner, string field, object value) => owner.GetType().GetField(field, Any).SetValue(owner, value);
void Check(bool condition, string name) { if (!condition) throw new System.Exception(name); }
var now = System.DateTimeOffset.UtcNow;
bool At(string when) => (bool)Study("StudySession").GetMethod("ReachedInactivityLimit", Any).Invoke(null, new object[] { when, now });
Check(!At(now.UtcDateTime.AddMinutes(-30).AddTicks(1).ToString("O")), "below boundary");
Check(At(now.UtcDateTime.AddMinutes(-30).ToString("O")), "exact boundary");
Check(!At(null) && !At(now.UtcDateTime.AddMinutes(1).ToString("O")), "missing and future times");
var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "boss-inactivity-check-" + System.Guid.NewGuid().ToString("N"));
var go = new UnityEngine.GameObject("Temporary inactivity check") { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
go.SetActive(false);
try {
    var session = go.AddComponent(Study("StudySession"));
    var storage = System.Activator.CreateInstance(Study("StudyStorage"), Any, null, new object[]{path}, null);
    Set(session, "storage", storage);
    Set(session, "quitting", true);
    object SetUp(bool active, bool clear) {
        var save = New("StudySave");
        var p = New("ParticipantRecord");
        Set(p, "participant_id", System.Guid.NewGuid().ToString("D"));
        Set(p, "settings_version", "combat-v15");
        Set(save, "participant", p);
        var assignment = New("StudyAssignment");
        Set(assignment, "experiment_group", "B");
        Set(assignment, "target_attempts", 7);
        Set(assignment, "assignment_version", "test");
        Set(save, "assignment", assignment);
        Set(save, "attempts", 1);
        Set(save, "deaths", active || clear ? 0 : 1);
        Set(save, "cleared", clear);
        Set(save, "surveyDue", clear);
        var attempt = New("AttemptRecord");
        Set(attempt, "attempt_id", System.Guid.NewGuid().ToString("D"));
        Set(attempt, "participant_id", Get(p, "participant_id"));
        Set(attempt, "ended_at", now.UtcDateTime.AddMinutes(-31).ToString("O"));
        Set(attempt, "attempt_number", 1);
        Set(attempt, "result", active ? null : clear ? "cleared" : "defeat");
        Set(save, active ? "active" : "lastAttempt", attempt);
        Set(session, "state", save);
        if (!active) Call(session, "Enqueue", "attempts", attempt);
        return save;
    }
    bool Expire(bool recovering) => (bool)Call(session, "ExpireInactiveStudy", recovering);
    var save = SetUp(false, true);
    Check(!Expire(true), "completed fight awaiting rating");
    save = SetUp(true, false);
    Check(!Expire(false), "live unpaused fight");
    Check(Expire(true), "stale active checkpoint expires on restart");
    Check((string)Get(Get(save, "lastAttempt"), "result") == "stopped", "active attempt is stopped");
    var pending = (System.Collections.IList)Get(save, "pending");
    Check(pending.Count == 2 && (string)Get(pending[0], "route") == "attempts" && (string)Get(pending[1], "route") == "surveys", "upload order");
    save = SetUp(false, false);
    Check(Expire(false) && !Expire(false), "between attempts expires once");
    var loaded = Call(storage, "Load");
    Check((bool)Get(loaded, "inactivityClosed") && (bool)Get(loaded, "debrief"), "closure survives reload");
    Check((string)Get(Get(loaded, "lastAttempt"), "result") == "defeat", "defeat result preserved");
    pending = (System.Collections.IList)Get(loaded, "pending");
    var summary = UnityEngine.JsonUtility.FromJson((string)Get(pending[1], "json"), Study("SurveyRecord"));
    Check(!(bool)Get(summary, "cleared") && (bool)Get(summary, "skipped") && (int)Get(summary, "actual_attempts") == 1, "uncleared closure summary");
    Call(session, "StartNewStudy");
    loaded = Call(storage, "Load");
    Check(Get(loaded, "assignment") == null && (int)Get(loaded, "attempts") == 0 && (int)Get(loaded, "deaths") == 0, "fresh participation has clean assignment and counters");
    var archivedId = (string)Get(Get(save, "participant"), "participant_id");
    Check(System.IO.File.Exists(System.IO.Path.Combine(path, "completed-studies", archivedId + ".json")), "previous session archived");
} finally {
    UnityEngine.Object.DestroyImmediate(go);
    if (System.IO.Directory.Exists(path)) {
        var archive = System.IO.Path.Combine(path, "completed-studies");
        if (System.IO.Directory.Exists(archive)) {
            foreach (var file in System.IO.Directory.GetFiles(archive)) System.IO.File.Delete(file);
            System.IO.Directory.Delete(archive);
        }
        foreach (var file in System.IO.Directory.GetFiles(path)) System.IO.File.Delete(file);
        System.IO.Directory.Delete(path);
    }
}
System.IO.File.WriteAllText(report, "PASS: threshold, unpaused fight, unfinished checkpoint, rating exemption, idempotence, upload order, reload and archive");

        }
        catch (System.Exception ex)
        {
            System.IO.File.WriteAllText(report, ex.ToString());
        }
    }
}
