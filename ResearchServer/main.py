import logging
import os
import re
import secrets
import sqlite3
from contextlib import asynccontextmanager
from typing import Annotated

from fastapi import Depends, FastAPI, Header, HTTPException, Request, Response
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import TypeAdapter

from models import Assignment, Attempt, Group, Label, Participant, Survey, Withdrawal
from sheets import SheetsMirror
from store import Store, StoreError

log = logging.getLogger("research.api")
DEFAULT_GROUPS = '[{"group":"A","target_attempts":3,"weight":1},{"group":"B","target_attempts":7,"weight":1},{"group":"C","target_attempts":12,"weight":1},{"group":"D","target_attempts":12,"weight":1}]'


def create_app():
    @asynccontextmanager
    async def lifespan(app):
        groups = TypeAdapter(list[Group]).validate_json(os.getenv("RESEARCH_GROUPS", DEFAULT_GROUPS))
        if not groups or len({g.group for g in groups}) != len(groups):
            raise ValueError("RESEARCH_GROUPS requires unique group names")
        version = TypeAdapter(Label).validate_python(os.getenv("RESEARCH_ASSIGNMENT_VERSION", "assignment-v2"))
        if version == Store.OFFLINE_ASSIGNMENT_VERSION:
            raise ValueError("RESEARCH_ASSIGNMENT_VERSION is reserved for offline registration")
        app.state.store = Store(os.getenv("RESEARCH_DB", "study.sqlite3"), groups, version)
        app.state.store.initialize()
        app.state.admin_token = os.getenv("RESEARCH_ADMIN_TOKEN", "")
        if app.state.admin_token and not valid_token(app.state.admin_token):
            raise ValueError("RESEARCH_ADMIN_TOKEN must contain 32–128 URL-safe characters")
        spreadsheet_id = os.getenv("RESEARCH_SPREADSHEET_ID", "")
        mirror = None
        if spreadsheet_id:
            if not re.fullmatch(r"[A-Za-z0-9_-]+", spreadsheet_id):
                raise ValueError("Invalid RESEARCH_SPREADSHEET_ID")
            interval = max(15, int(os.getenv("RESEARCH_SYNC_SECONDS", "30")))
            mirror = SheetsMirror(app.state.store, spreadsheet_id, interval)
            mirror.thread.start()
        try:
            yield
        finally:
            if mirror:
                mirror.stop.set()
                mirror.thread.join(timeout=50)

    app = FastAPI(lifespan=lifespan, docs_url=None, redoc_url=None, openapi_url=None)

    @app.exception_handler(RequestValidationError)
    async def invalid_payload(request, error):
        log.info("Rejected invalid request on %s", request.url.path)
        return JSONResponse(status_code=422, content={"detail": "Invalid request data"})

    @app.exception_handler(StoreError)
    async def rejected(request, error):
        log.info("Rejected %s (%d)", request.url.path, error.status)
        return JSONResponse(status_code=error.status, content={"detail": str(error)})

    @app.exception_handler(sqlite3.OperationalError)
    async def unavailable(request, error):
        log.error("SQLite operation failed (%s)", getattr(error, "sqlite_errorname", "unknown"))
        return JSONResponse(status_code=503, content={"detail": "Storage temporarily unavailable"})

    @app.get("/health")
    def health(request: Request):
        with request.app.state.store.connection() as db:
            db.execute("SELECT revision FROM mirror_state WHERE id=1").fetchone()
        return {"status": "ok"}

    @app.post("/participants", response_model=Assignment)
    def participants(payload: Participant, request: Request, token: Token):
        return request.app.state.store.register(payload, token)

    @app.post("/participants/offline", response_model=Assignment)
    def offline_participants(payload: Participant, request: Request, token: Token):
        return request.app.state.store.register(payload, token, offline=True)

    @app.post("/attempts", status_code=204)
    def attempts(payload: Attempt, request: Request, token: Token):
        request.app.state.store.submit(payload, token)
        return Response(status_code=204)

    @app.post("/surveys", status_code=204)
    def surveys(payload: Survey, request: Request, token: Token):
        request.app.state.store.submit(payload, token, survey=True)
        return Response(status_code=204)

    @app.post("/withdraw", status_code=204)
    def withdraw(payload: Withdrawal, request: Request, token: Token):
        request.app.state.store.withdraw(payload.participant_id, token)
        return Response(status_code=204)

    @app.get("/admin/sync")
    def sync_status(request: Request, token: Token):
        secret = request.app.state.admin_token
        if not secret or not secrets.compare_digest(token, secret):
            raise HTTPException(403, "Administrator credentials required")
        with request.app.state.store.connection() as db:
            state = db.execute("SELECT revision, synced_revision FROM mirror_state WHERE id=1").fetchone()
        return dict(state)

    return app


def valid_token(value):
    return re.fullmatch(r"[A-Za-z0-9_-]{32,128}", value) is not None


def bearer(authorization: Annotated[str | None, Header()] = None):
    scheme, _, token = (authorization or "").partition(" ")
    if scheme.lower() != "bearer" or not valid_token(token):
        raise HTTPException(401, "Bearer token required", headers={"WWW-Authenticate": "Bearer"})
    return token


Token = Annotated[str, Depends(bearer)]
app = create_app()
