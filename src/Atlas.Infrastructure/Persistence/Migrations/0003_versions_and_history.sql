CREATE TABLE versions (
    id             TEXT NOT NULL PRIMARY KEY,
    workspace_id   TEXT NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    name           TEXT NOT NULL,
    saved_at       TEXT NOT NULL,
    is_automatic   INTEGER NOT NULL CHECK (is_automatic IN (0, 1)),
    resource_count INTEGER NOT NULL,
    content        TEXT NOT NULL
) STRICT;

CREATE INDEX ix_versions_workspace ON versions (workspace_id, saved_at);

-- No foreign key: the history of a workspace stays readable after the workspace is deleted.
CREATE TABLE history (
    id             INTEGER PRIMARY KEY,
    occurred_at    TEXT NOT NULL,
    action         TEXT NOT NULL,
    workspace_id   TEXT NOT NULL,
    workspace_name TEXT NOT NULL,
    detail         TEXT NOT NULL,
    restored       INTEGER NULL,
    missing        INTEGER NULL,
    failed         INTEGER NULL,
    skipped        INTEGER NULL
) STRICT;

CREATE INDEX ix_history_occurred ON history (occurred_at);
