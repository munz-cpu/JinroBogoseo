import os
import random
import sqlite3
import tempfile
import unittest
from contextlib import closing
from pathlib import Path
from unittest.mock import patch
from uuid import uuid4

from fastapi.testclient import TestClient

from main import create_app
from metrics import analyze
from models import Group, Participant
from sheets import SheetsMirror
from store import Store

TOKEN = "p" * 43
OTHER_TOKEN = "q" * 43
ADMIN_TOKEN = "a" * 43
NOW = "2026-01-01T00:00:00Z"


def participant():
    return {
        "participant_id": str(uuid4()), "age_group": "18_24", "gaming_experience": "weekly",
        "consented": True, "consented_at": NOW, "consent_version": "v1", "settings_version": "v1",
    }


class StudyTest(unittest.TestCase):
    def setUp(self):
        folder = self.enterContext(tempfile.TemporaryDirectory())
        self.db = Path(folder) / "test.sqlite3"
        self.enterContext(patch.dict(os.environ, {
            "RESEARCH_DB": str(self.db), "RESEARCH_ADMIN_TOKEN": ADMIN_TOKEN,
            "RESEARCH_SPREADSHEET_ID": "", "RESEARCH_GROUPS": '[{"group":"B","target_attempts":7,"weight":1}]',
            "RESEARCH_ASSIGNMENT_VERSION": "test-v1",
        }))
        self.client = self.enterContext(TestClient(create_app()))
        self.p = participant()

    def post(self, route, data, token=TOKEN):
        return self.client.post(route, json=data, headers={"Authorization": f"Bearer {token}"})

    def register(self):
        response = self.post("/participants", self.p)
        self.assertEqual(response.status_code, 200, response.text)
        return response.json()

    def attempt(self):
        return {
            "participant_id": self.p["participant_id"], "attempt_id": str(uuid4()), "attempt_number": 1,
            **self.register(), "settings_version": "v1", "started_at": NOW, "ended_at": "2026-01-01T00:01:00Z",
            "duration_seconds": 60, "damage_taken": 10, "damage_dealt": 500, "boss_hp_remaining": 0,
            "pattern_stats": [{"pattern_id": "dash", "executions": 2, "hit_count": 1, "average_damage": 10, "damage_taken": 10}],
            "difficulty": {"level": .5, "boss_health": 1, "patterns": [
                {"pattern_id": "dash", "speed": 1, "interval": 1, "hitbox": 1, "damage": 1, "selection_weight": 1}
            ]},
            "difficulty_changes": [{"applied_at": NOW, "previous_level": .5, "new_level": .5, "reason": "initial"}],
            "cleared": True, "result": "cleared",
        }

    def survey(self):
        return {
            "participant_id": self.p["participant_id"], "settings_version": "v1", "submitted_at": NOW,
            "cleared": True, "actual_attempts": 1, "skipped": False, "scores": [5, 4, 3, 2, 1, 2],
        }

    def test_consent_auth_and_stable_assignment(self):
        self.assertEqual(self.client.get("/health").json(), {"status": "ok"})
        self.assertEqual(self.client.post("/participants", json=self.p).status_code, 401)
        invalid = self.post("/participants", {**self.p, "consented": False})
        self.assertEqual(invalid.status_code, 422)
        self.assertNotIn(self.p["participant_id"], invalid.text)
        self.assertEqual(self.register(), self.register())
        self.assertEqual(self.post("/participants", self.p, OTHER_TOKEN).status_code, 403)
        self.assertEqual(self.client.get("/admin/sync", headers={"Authorization": "Bearer " + TOKEN}).status_code, 403)
        self.assertEqual(self.client.get("/admin/sync", headers={"Authorization": "Bearer " + ADMIN_TOKEN}).status_code, 200)

    def test_weighted_assignment_and_reopen(self):
        groups = [Group(group=name, target_attempts=target, weight=weight)
                  for name, target, weight in zip("ABC", (3, 7, 12), (1, 2, 5))]
        db = self.db.with_name("assignment.sqlite3")
        store = Store(db, groups, "weighted-v1", random.Random(64))
        store.initialize()
        counts = dict.fromkeys("ABC", 0)
        for _ in range(200):
            p = Participant.model_validate(participant())
            assigned = store.register(p, TOKEN)
            counts[assigned["experiment_group"]] += 1
        self.assertGreater(counts["C"], counts["B"])
        self.assertGreater(counts["B"], counts["A"])
        restarted = Store(db, groups[:1], "weighted-v2")
        restarted.initialize()
        self.assertEqual(restarted.register(p, TOKEN), assigned)

    def test_early_clear_retry_validation_and_survey(self):
        attempt = self.attempt()
        survey = self.survey()
        self.assertEqual(self.post("/surveys", survey).status_code, 409)
        self.assertEqual(self.post("/attempts", attempt, OTHER_TOKEN).status_code, 403)
        self.assertEqual(self.post("/attempts", {**attempt, "cleared": False}).status_code, 422)
        self.assertEqual(self.post("/attempts", {**attempt, "target_attempts": 3}).status_code, 409)
        for _ in range(2):
            self.assertEqual(self.post("/attempts", attempt).status_code, 204)
        self.assertEqual(self.post("/attempts", {**attempt, "attempt_id": str(uuid4())}).status_code, 409)
        self.assertEqual(self.post("/surveys", {**survey, "scores": [6] * 6}).status_code, 422)
        for _ in range(2):
            self.assertEqual(self.post("/surveys", survey).status_code, 204)
        with closing(sqlite3.connect(self.db)) as db:
            self.assertEqual(db.execute("SELECT COUNT(*) FROM attempts").fetchone()[0], 1)
            result = analyze(db)["overall"]
            self.assertEqual(result["mean_absolute_error"], 6)
            self.assertEqual(result["within_one_rate"], 0)

    def test_skipped_survey_is_optional_and_counts_dropout(self):
        self.register()
        survey = {**self.survey(), "skipped": True, "scores": [], "cleared": False, "actual_attempts": 0}
        self.assertEqual(self.post("/surveys", survey).status_code, 204)
        with closing(sqlite3.connect(self.db)) as db:
            result = analyze(db)["overall"]
            self.assertEqual(result["dropout_rate"], 1)
            self.assertIsNone(result["mean_absolute_error"])

    def test_withdrawal_and_sheets_retry(self):
        attempt = self.attempt()
        self.assertEqual(self.post("/attempts", attempt).status_code, 204)
        self.assertEqual(self.post("/surveys", self.survey()).status_code, 204)
        mirror = SheetsMirror(self.client.app.state.store, "fake-sheet", 30)
        session = FakeSheets()
        session.fail_next = True
        with self.assertRaises(OSError):
            mirror.sync(session)
        mirror.sync(session)
        self.assertEqual(session.writes[0], session.writes[1])
        self.assertEqual(session.rows["Surveys"][1][3:9], [5, 4, 2, 2, 1, 3])
        self.assertEqual(len(session.rows["PatternStats"]), 2)

        withdrawal = {"participant_id": self.p["participant_id"]}
        self.assertEqual(self.post("/withdraw", withdrawal, OTHER_TOKEN).status_code, 403)
        for _ in range(2):
            self.assertEqual(self.post("/withdraw", withdrawal).status_code, 204)
        self.assertEqual(self.post("/participants", self.p).status_code, 410)
        self.assertEqual(self.post("/attempts", attempt).status_code, 403)
        mirror.sync(session)
        self.assertTrue(all(len(rows) == 1 for rows in session.rows.values()))
        mirror.sync(session)
        self.assertEqual(len(session.writes), 3)


class FakeSheets:
    """Test the outgoing REST request shape, repeated writes and range clearing."""
    def __init__(self):
        self.properties = {}
        self.rows = {}
        self.writes = []
        self.fail_next = False

    def get(self, url, **kwargs):
        return Reply({"sheets": [{"properties": p} for p in self.properties.values()]})

    def post(self, url, json, **kwargs):
        self.writes.append(json)
        if self.fail_next:
            self.fail_next = False
            return Reply(None)
        for request in json["requests"]:
            if "addSheet" in request:
                props = request["addSheet"]["properties"]
                self.properties[props["sheetId"]] = props
            elif "appendDimension" in request:
                grow = request["appendDimension"]
                field = "rowCount" if grow["dimension"] == "ROWS" else "columnCount"
                self.properties[grow["sheetId"]]["gridProperties"][field] += grow["length"]
            else:
                update = request["updateCells"]
                assert "endRowIndex" not in update["range"]
                assert update["fields"] == "userEnteredValue"
                title = self.properties[update["range"]["sheetId"]]["title"]
                self.rows[title] = [
                    [next(iter(c.get("userEnteredValue", {}).values()), None) for c in r["values"]]
                    for r in update["rows"]
                ]
        return Reply({})


class Reply:
    def __init__(self, data):
        self.data = data

    def json(self):
        return self.data

    def raise_for_status(self):
        if self.data is None:
            raise OSError("simulated connection failure")


if __name__ == "__main__":
    unittest.main()
