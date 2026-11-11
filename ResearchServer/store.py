import hashlib
import hmac
import json
import random
import sqlite3
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path


class StoreError(Exception):
    def __init__(self, status, message):
        super().__init__(message)
        self.status = status


class Store:
    OFFLINE_ASSIGNMENT_VERSION = "offline-v1"
    OFFLINE_GROUPS = (("A", 3), ("B", 7), ("C", 12), ("D", 12))

    def __init__(self, path, groups, assignment_version, rng=None):
        self.path = path
        self.groups = groups
        self.assignment_version = assignment_version
        self.rng = rng or random.SystemRandom()

    @contextmanager
    def connection(self, *, write=False):
        db = sqlite3.connect(self.path, timeout=10)
        db.row_factory = sqlite3.Row
        try:
            db.execute("PRAGMA foreign_keys = ON")
            db.execute("PRAGMA secure_delete = ON")
            db.execute("BEGIN IMMEDIATE" if write else "BEGIN")
            yield db
            db.commit()
        except BaseException:
            db.rollback()
            raise
        finally:
            db.close()

    def initialize(self):
        with self.connection(write=True) as db:
            db.executescript(Path(__file__).with_name("schema.sql").read_text(encoding="utf-8"))

    @staticmethod
    def token_hash(token):
        return hashlib.sha256(token.encode("ascii")).hexdigest()

    @staticmethod
    def require_token(row, token_hash):
        if row is None or not hmac.compare_digest(row["token_hash"], token_hash):
            raise StoreError(403, "Invalid participant credentials")

    @staticmethod
    def changed(db):
        db.execute("UPDATE mirror_state SET revision = revision + 1 WHERE id = 1")

    def register(self, participant, token, *, offline=False):
        fields = participant.model_dump(mode="json")
        pid = fields["participant_id"]
        token_hash = self.token_hash(token)
        offline_assignment = None
        if offline:
            group, target = self.OFFLINE_GROUPS[participant.participant_id.int % len(self.OFFLINE_GROUPS)]
            offline_assignment = {
                "experiment_group": group, "target_attempts": target,
                "assignment_version": self.OFFLINE_ASSIGNMENT_VERSION,
            }
        with self.connection(write=True) as db:
            withdrawn = db.execute("SELECT token_hash FROM withdrawals WHERE participant_id=?", (pid,)).fetchone()
            if withdrawn:
                self.require_token(withdrawn, token_hash)
                raise StoreError(410, "Participant withdrawn")
            row = db.execute("SELECT * FROM participants WHERE participant_id=?", (pid,)).fetchone()
            if row:
                self.require_token(row, token_hash)
                if any(fields[k] != row[k] for k in ("age_group", "gaming_experience", "settings_version", "consent_version")):
                    raise StoreError(409, "Registration payload changed")
                assignment = {k: row[k] for k in ("experiment_group", "target_attempts", "assignment_version")}
                if offline and assignment != offline_assignment:
                    raise StoreError(409, "Participant already has a different assignment")
                return assignment

            assignment = offline_assignment
            if assignment is None:
                group = self.rng.choices(self.groups, weights=[g.weight for g in self.groups])[0]
                assignment = {
                    "experiment_group": group.group,
                    "target_attempts": group.target_attempts,
                    "assignment_version": self.assignment_version,
                }
            db.execute(
                """INSERT INTO participants VALUES (:participant_id, :token_hash, :age_group,
                :gaming_experience, :experiment_group, :target_attempts, :consent_version,
                :settings_version, :assignment_version, :consented_at, :registered_at)""",
                {**fields, **assignment, "token_hash": token_hash, "registered_at": now()},
            )
            self.changed(db)
            return assignment

    def submit(self, payload, token, *, survey=False):
        data = payload.model_dump(mode="json", exclude_unset=True)
        pid = data["participant_id"]
        encoded = json.dumps(data, ensure_ascii=False, sort_keys=True)
        with self.connection(write=True) as db:
            row = db.execute("SELECT * FROM participants WHERE participant_id=?", (pid,)).fetchone()
            self.require_token(row, self.token_hash(token))
            keys = ("settings_version",) if survey else (
                "settings_version", "experiment_group", "target_attempts", "assignment_version"
            )
            if any(row[k] != data[k] for k in keys):
                raise StoreError(409, "Settings or assignment mismatch")
            if survey:
                old = db.execute("SELECT payload FROM surveys WHERE participant_id=?", (pid,)).fetchone()
            else:
                old = db.execute(
                    "SELECT payload FROM attempts WHERE attempt_id=? OR (participant_id=? AND attempt_number=?)",
                    (data["attempt_id"], pid, data["attempt_number"]),
                ).fetchone()
            if old:
                if old["payload"] != encoded:
                    raise StoreError(409, "Conflicting record")
                return

            if survey:
                count = db.execute("SELECT COUNT(*) FROM attempts WHERE participant_id=?", (pid,)).fetchone()[0]
                last = db.execute(
                    "SELECT payload FROM attempts WHERE participant_id=? ORDER BY attempt_number DESC LIMIT 1", (pid,)
                ).fetchone()
                if payload.actual_attempts != count or payload.cleared != bool(last and json.loads(last[0])["cleared"]):
                    raise StoreError(409, "Survey outcome does not match attempts")
                db.execute("INSERT INTO surveys VALUES (?, ?)", (pid, encoded))
            else:
                if db.execute("SELECT 1 FROM surveys WHERE participant_id=?", (pid,)).fetchone():
                    raise StoreError(409, "Study already closed")
                last = db.execute(
                    "SELECT attempt_number, payload FROM attempts WHERE participant_id=? ORDER BY attempt_number DESC LIMIT 1",
                    (pid,),
                ).fetchone()
                if last and json.loads(last["payload"])["result"] in {"cleared", "stopped"}:
                    raise StoreError(409, "Combat already closed")
                if payload.attempt_number != (last["attempt_number"] + 1 if last else 1):
                    raise StoreError(409, "Attempts must be submitted in order")
                db.execute("INSERT INTO attempts VALUES (?, ?, ?, ?)", (data["attempt_id"], pid, data["attempt_number"], encoded))
            self.changed(db)

    def withdraw(self, participant_id, token):
        pid, token_hash = str(participant_id), self.token_hash(token)
        with self.connection(write=True) as db:
            old = db.execute("SELECT token_hash FROM withdrawals WHERE participant_id=?", (pid,)).fetchone()
            if old:
                self.require_token(old, token_hash)
                return
            row = db.execute("SELECT token_hash FROM participants WHERE participant_id=?", (pid,)).fetchone()
            self.require_token(row, token_hash)
            db.execute("INSERT INTO withdrawals VALUES (?, ?, ?)", (pid, token_hash, now()))
            db.execute("DELETE FROM participants WHERE participant_id=?", (pid,))
            self.changed(db)


def now():
    return datetime.now(timezone.utc).isoformat()
