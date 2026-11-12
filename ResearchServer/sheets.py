import json
import logging
from threading import Event, Thread

log = logging.getLogger("research.sheets")

HEADERS = {
    "Participants": (
        "participant_id age_group gaming_experience target_attempts consent_version experiment_group "
        "settings_version assignment_version progress star_rating actual_attempts total_play_time_seconds "
        "total_damage_taken last_result"
    ),
    "Attempts": (
        "participant_id attempt_id attempt_number target_attempts duration_seconds damage_taken damage_dealt "
        "boss_hp_remaining difficulty_level result boss_phase phase_progress_percent progress"
    ),
    "PatternStats": "participant_id attempt_id pattern_id hit_count average_damage difficulty_parameters executions damage_taken",
    "Surveys": (
        "participant_id cleared actual_attempts achievement_score immersion_score fairness_score "
        "frustration_score retry_intention_score difficulty_score skipped early_exit_rating"
    ),
}


def attempt_progress(attempt):
    if attempt["cleared"]:
        return "완료"
    if attempt.get("boss_phase"):
        return f'페이즈{attempt["boss_phase"]} {attempt["phase_progress_percent"]:.1f}%'
    return None


def snapshot(store):
    rows = {name: [header.split()] for name, header in HEADERS.items()}
    participants = {}
    with store.connection() as db:
        revision = db.execute("SELECT revision FROM mirror_state WHERE id=1").fetchone()[0]
        for p in db.execute("SELECT * FROM participants ORDER BY participant_id"):
            participants[p["participant_id"]] = dict(
                p, progress="페이즈1 0.0%", star_rating=None, actual_attempts=0,
                total_play_time_seconds=0, total_damage_taken=0, last_result=None,
            )
        for row in db.execute("SELECT payload FROM attempts ORDER BY participant_id, attempt_number"):
            a = json.loads(row[0])
            a["difficulty_level"] = a["difficulty"]["level"]
            a["progress"] = attempt_progress(a)
            if not a.get("boss_phase"):
                a["boss_phase"] = a["phase_progress_percent"] = None
            rows["Attempts"].append([a[k] for k in rows["Attempts"][0]])
            summary = participants[a["participant_id"]]
            summary["progress"] = a["progress"]
            summary["actual_attempts"] += 1
            summary["total_play_time_seconds"] += a["duration_seconds"]
            summary["total_damage_taken"] += a["damage_taken"]
            summary["last_result"] = a["result"]
            parameters = {p["pattern_id"]: p for p in a["difficulty"]["patterns"]}
            for p in a["pattern_stats"]:
                rows["PatternStats"].append([
                    a["participant_id"], a["attempt_id"], p["pattern_id"], p["hit_count"],
                    p["average_damage"], json.dumps(parameters[p["pattern_id"]], sort_keys=True),
                    p["executions"], p["damage_taken"],
                ])
        for row in db.execute("SELECT payload FROM surveys ORDER BY participant_id"):
            s = json.loads(row[0])
            scores = s["scores"]
            early_exit_rating = None
            if len(scores) == 1:
                if s["cleared"]:
                    participants[s["participant_id"]]["star_rating"] = scores[0]
                else:
                    early_exit_rating = scores[0]
                    scores = []
            a, i, d, f, r, x = scores + [None] * (6 - len(scores))
            rows["Surveys"].append([
                s["participant_id"], s["cleared"], s["actual_attempts"], a, i, f, x, r, d, s["skipped"],
                early_exit_rating,
            ])
    rows["Participants"].extend([p[k] for k in rows["Participants"][0]] for p in participants.values())
    return revision, rows


def cell(value):
    if value is None:
        return {}
    kind = "boolValue" if isinstance(value, bool) else "numberValue" if isinstance(value, (int, float)) else "stringValue"
    return {"userEnteredValue": {kind: value}}


def batch(rows, sheets):
    existing = {p["title"]: p for p in sheets}
    used_ids = {p["sheetId"] for p in sheets}
    next_id = 1
    requests = []
    for title, table in rows.items():
        height, width = len(table), len(table[0])
        if title not in existing:
            while next_id in used_ids:
                next_id += 1
            sheet_id = next_id
            used_ids.add(sheet_id)
            requests.append({"addSheet": {"properties": {
                "title": title, "sheetId": sheet_id,
                "gridProperties": {"rowCount": max(100, height), "columnCount": width},
            }}})
        else:
            props = existing[title]
            sheet_id = props["sheetId"]
            for dim, key, size in (("ROWS", "rowCount", height), ("COLUMNS", "columnCount", width)):
                length = size - props["gridProperties"][key]
                if length > 0:
                    requests.append({"appendDimension": {"sheetId": sheet_id, "dimension": dim, "length": length}})

        # A bounded column range with no end row also clears stale rows after a withdrawal.
        requests.append({"updateCells": {
            "range": {"sheetId": sheet_id, "startRowIndex": 0, "startColumnIndex": 0, "endColumnIndex": width},
            "rows": [{"values": [cell(v) for v in row]} for row in table],
            "fields": "userEnteredValue",
        }})
    return {"requests": requests}


class SheetsMirror:
    def __init__(self, store, spreadsheet_id, interval):
        self.store = store
        self.spreadsheet_id = spreadsheet_id
        self.interval = interval
        self.synced = False
        self.stop = Event()
        self.thread = Thread(target=self.run, name="sheets-mirror", daemon=True)

    def sync(self, session):
        with self.store.connection() as db:
            state = db.execute("SELECT * FROM mirror_state WHERE id=1").fetchone()
        if self.synced and state["destination"] == self.spreadsheet_id and state["revision"] == state["synced_revision"]:
            return

        revision, rows = snapshot(self.store)
        url = f"https://sheets.googleapis.com/v4/spreadsheets/{self.spreadsheet_id}"
        response = session.get(url, params={"fields": "sheets(properties(sheetId,title,gridProperties))"}, timeout=15)
        response.raise_for_status()
        payload = batch(rows, [s["properties"] for s in response.json().get("sheets", [])])
        response = session.post(url + ":batchUpdate", json=payload, timeout=30)
        response.raise_for_status()
        with self.store.connection(write=True) as db:
            db.execute("UPDATE mirror_state SET synced_revision=?, destination=? WHERE id=1", (revision, self.spreadsheet_id))
        self.synced = True
        log.info("Sheets synced revision %d", revision)

    def run(self):
        from google.auth import default
        from google.auth.transport.requests import AuthorizedSession

        session = None
        try:
            while not self.stop.is_set():
                try:
                    if session is None:
                        credentials, _ = default(scopes=["https://www.googleapis.com/auth/spreadsheets"])
                        session = AuthorizedSession(credentials, refresh_timeout=10)
                    self.sync(session)
                except Exception as error:
                    status = getattr(getattr(error, "response", None), "status_code", None)
                    log.warning("Sheets sync pending (%s, HTTP %s)", type(error).__name__, status)
                self.stop.wait(self.interval)
        finally:
            if session is not None:
                session.close()
