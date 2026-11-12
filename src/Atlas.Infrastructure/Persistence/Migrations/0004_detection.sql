-- Environments Atlas proposed, and what the user decided about them. A saved environment keeps the identifier of
-- the workspace it became, without a foreign key: deleting that workspace does not make Atlas propose it again.
CREATE TABLE detected_environments (
    key           TEXT NOT NULL PRIMARY KEY,
    kind          TEXT NOT NULL,
    title         TEXT NOT NULL,
    confidence    TEXT NOT NULL,
    resources     TEXT NOT NULL,
    status        TEXT NOT NULL,
    first_seen    TEXT NOT NULL,
    last_seen     TEXT NOT NULL,
    seen_days     INTEGER NOT NULL,
    last_seen_day TEXT NOT NULL,
    snoozed_until TEXT NULL,
    notified_at   TEXT NULL,
    workspace_id  TEXT NULL
) STRICT;
