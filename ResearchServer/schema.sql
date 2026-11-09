CREATE TABLE IF NOT EXISTS participants (
    participant_id TEXT PRIMARY KEY,
    token_hash TEXT NOT NULL,
    age_group TEXT NOT NULL,
    gaming_experience TEXT NOT NULL,
    experiment_group TEXT NOT NULL,
    target_attempts INTEGER NOT NULL CHECK (target_attempts > 0),
    consent_version TEXT NOT NULL,
    settings_version TEXT NOT NULL,
    assignment_version TEXT NOT NULL,
    consented_at TEXT NOT NULL,
    registered_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS attempts (
    attempt_id TEXT PRIMARY KEY,
    participant_id TEXT NOT NULL REFERENCES participants(participant_id) ON DELETE CASCADE,
    attempt_number INTEGER NOT NULL CHECK (attempt_number > 0),
    payload TEXT NOT NULL CHECK (json_valid(payload)),
    UNIQUE (participant_id, attempt_number)
);

CREATE TABLE IF NOT EXISTS surveys (
    participant_id TEXT PRIMARY KEY REFERENCES participants(participant_id) ON DELETE CASCADE,
    payload TEXT NOT NULL CHECK (json_valid(payload))
);

-- Minimal receipt prevents delayed registration or submissions from recreating excluded data.
CREATE TABLE IF NOT EXISTS withdrawals (
    participant_id TEXT PRIMARY KEY,
    token_hash TEXT NOT NULL,
    requested_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS mirror_state (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    revision INTEGER NOT NULL,
    synced_revision INTEGER NOT NULL,
    destination TEXT NOT NULL
);
INSERT OR IGNORE INTO mirror_state VALUES (1, 0, -1, '');
