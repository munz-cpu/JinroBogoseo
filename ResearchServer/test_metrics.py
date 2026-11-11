import json
import sqlite3
import unittest
from contextlib import closing
from pathlib import Path

from metrics import analyze


class AchievementMetricsTest(unittest.TestCase):
    def test_achievement_uses_answered_stars_and_keeps_groups_and_versions_distinct(self):
        with closing(sqlite3.connect(":memory:")) as db:
            db.executescript(Path(__file__).with_name("schema.sql").read_text(encoding="utf-8"))

            def add(pid, group, attempts, scores, *, settings="v1", assignment="v1", outcome="cleared"):
                db.execute(
                    "INSERT INTO participants VALUES (?, '', '', '', ?, 12, '', ?, ?, '', '')",
                    (pid, group, settings, assignment),
                )
                db.executemany(
                    "INSERT INTO attempts VALUES (?, ?, ?, ?)",
                    [(f"{pid}-{number}", pid, number, json.dumps({"result": result}))
                     for number, result in enumerate(["defeat"] * (attempts - 1) + [outcome], 1)],
                )
                if scores is not None:
                    db.execute("INSERT INTO surveys VALUES (?, ?)", (pid, json.dumps({
                        "scores": scores, "skipped": not scores, "cleared": outcome == "cleared",
                    })))

            add("early", "C", 1, [5])
            add("later", "C", 2, [3])
            add("skipped", "C", 2, [])
            add("unanswered", "C", 2, None)
            add("legacy", "C", 2, [1, 1, 1, 1, 1, 1])
            add("fixed-hard", "D", 2, [1])
            add("new-settings", "C", 2, [2], settings="v2")
            add("new-assignment", "C", 2, None, assignment="v2")
            add("stopped", "C", 2, [1], outcome="stopped")
            result = analyze(db)

        def summarize(key):
            return {
                (row["group"], row["assignment_version"], row["settings_version"], row["attempts"]):
                    (row["cleared"], row["rated"], row["mean_achievement_score"])
                for row in result[key]
            }

        self.assertEqual(result["overall"]["stopped"], 1)
        self.assertEqual(summarize("achievement_by_target_attempts"), {
            ("C", "v1", "v1", 12): (5, 2, 4),
            ("C", "v1", "v2", 12): (1, 1, 2),
            ("C", "v2", "v1", 12): (1, 0, None),
            ("D", "v1", "v1", 12): (1, 1, 1),
        })
        self.assertEqual(summarize("achievement_by_actual_attempts"), {
            ("C", "v1", "v1", 1): (1, 1, 5),
            ("C", "v1", "v1", 2): (4, 1, 3),
            ("C", "v1", "v2", 2): (1, 1, 2),
            ("C", "v2", "v1", 2): (1, 0, None),
            ("D", "v1", "v1", 2): (1, 1, 1),
        })
