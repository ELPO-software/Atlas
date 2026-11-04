-- Dates are stored as UTC text in the round-trip format, so sorting the text sorts the dates.

CREATE TABLE workspaces (
    id           TEXT NOT NULL PRIMARY KEY,
    name         TEXT NOT NULL,
    description  TEXT NOT NULL,
    notes        TEXT NOT NULL,
    created_at   TEXT NOT NULL,
    updated_at   TEXT NOT NULL,
    last_used_at TEXT NULL
) STRICT;

CREATE TABLE resources (
    id              TEXT NOT NULL PRIMARY KEY,
    workspace_id    TEXT NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    kind            TEXT NOT NULL,
    display_name    TEXT NOT NULL,
    target          TEXT NOT NULL,
    sort_order      INTEGER NOT NULL,
    requires_review INTEGER NOT NULL CHECK (requires_review IN (0, 1)),
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL
) STRICT;

CREATE INDEX ix_resources_workspace ON resources (workspace_id, sort_order);
