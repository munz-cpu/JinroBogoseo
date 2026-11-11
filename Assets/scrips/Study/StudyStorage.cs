using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BossTemplate
{
    internal sealed class StudyStorage
    {
        private readonly string path;

        internal StudyStorage(string directory) => path = Path.Combine(directory, "boss-study-v1.json");

        internal StudySave Load()
        {
            if (!File.Exists(path)) return new StudySave();
            var state = JsonUtility.FromJson<StudySave>(File.ReadAllText(path));
            if (state == null || state.schema != 1 || state.recent == null || state.pending == null)
                throw new InvalidDataException("Unrecognized study save");
            if (string.IsNullOrEmpty(state.participant?.participant_id)) state.participant = null;
            if (string.IsNullOrEmpty(state.active?.attempt_id)) state.active = null;
            if (string.IsNullOrEmpty(state.lastAttempt?.attempt_id)) state.lastAttempt = null;
            if (state.assignment?.target_attempts is null or <= 0) state.assignment = null;
            return state;
        }

        internal void Write(StudySave state)
        {
            Write(path, state);
        }

        internal void ArchiveCompleted(StudySave state)
        {
            if (!Guid.TryParse(state.participant.participant_id, out var participantId))
                throw new InvalidDataException("Invalid completed study participant ID");
            string archive = Path.Combine(Path.GetDirectoryName(path), "completed-studies", $"{participantId:D}.json");
            Write(archive, state);
        }

        private static void Write(string path, StudySave state)
        {
            string temp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(state));
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
