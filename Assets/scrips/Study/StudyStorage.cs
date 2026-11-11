using System;
using System.Collections.Generic;
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
            if (state.offlineRegistrationPending && !ValidOfflineRegistration(state))
                throw new InvalidDataException("Invalid pending offline registration");
            if (state.participant != null && state.deaths == 0)
            {
                // Older saves only retained recent attempts and records awaiting upload.
                var defeats = new HashSet<string>();
                void CountDefeat(AttemptRecord attempt)
                {
                    if (attempt?.result == "defeat") defeats.Add(attempt.attempt_id);
                }
                foreach (var attempt in state.recent) CountDefeat(attempt);
                CountDefeat(state.lastAttempt);
                foreach (var submission in state.pending)
                    if (submission.route == "attempts") CountDefeat(JsonUtility.FromJson<AttemptRecord>(submission.json));
                state.deaths = defeats.Count;
            }
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

        internal IEnumerable<StudySave> LoadPendingArchives(string currentParticipantId)
        {
            string directory = Path.Combine(Path.GetDirectoryName(path), "completed-studies");
            if (!Directory.Exists(directory)) yield break;
            foreach (string archive in Directory.EnumerateFiles(directory, "*.json"))
            {
                StudySave saved = null;
                try
                {
                    saved = JsonUtility.FromJson<StudySave>(File.ReadAllText(archive));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    Debug.LogWarning("Could not read an archived study (" + error.GetType().Name + ").");
                }
                if (saved?.schema == 1 && saved.debrief && saved.pending?.Count > 0
                    && Guid.TryParse(saved.participant?.participant_id, out var id)
                    && id.ToString("D") == Path.GetFileNameWithoutExtension(archive)
                    && !string.Equals(id.ToString("D"), currentParticipantId, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(saved.token)
                    && (saved.offlineRegistrationPending ? ValidOfflineRegistration(saved) : saved.protocol?.IsConfigured == true)
                    && saved.pending.TrueForAll(item => item != null && (item.route is "attempts" or "surveys")
                        && !string.IsNullOrEmpty(item.json)))
                    yield return saved;
            }
        }

        private static bool ValidOfflineRegistration(StudySave save) => save.protocol?.HasConsentDetails == true
            && !string.IsNullOrEmpty(save.token) && Guid.TryParse(save.participant?.participant_id, out var participantId)
            && StudyAssignment.ForOfflineParticipant(participantId).Matches(save.assignment);

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
