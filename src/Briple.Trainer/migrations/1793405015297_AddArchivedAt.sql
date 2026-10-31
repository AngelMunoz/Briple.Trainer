-- MIGRONDI:NAME=1793405015297_AddArchivedAt.sql
-- MIGRONDI:TIMESTAMP=1793405015297
-- ---------- MIGRONDI:UP ----------
-- Add your SQL migration code below. You can delete this line but do not delete the comments above.

ALTER TABLE athletes ADD COLUMN archived_at TEXT NULL;
ALTER TABLE plans ADD COLUMN archived_at TEXT NULL;
ALTER TABLE memories ADD COLUMN archived_at TEXT NULL;

CREATE INDEX IF NOT EXISTS idx_plans_athlete_created
  ON plans (athlete_id, created_at);

CREATE INDEX IF NOT EXISTS idx_memories_athlete_created
  ON memories (athlete_id, created_at);

CREATE INDEX IF NOT EXISTS idx_memories_plan_id
  ON memories (plan_id);

-- ---------- MIGRONDI:DOWN ----------
-- Add your SQL rollback code below. You can delete this line but do not delete comment above.

DROP INDEX IF EXISTS idx_memories_plan_id;
DROP INDEX IF EXISTS idx_memories_athlete_created;
DROP INDEX IF EXISTS idx_plans_athlete_created;

ALTER TABLE memories DROP COLUMN archived_at;
ALTER TABLE plans DROP COLUMN archived_at;
ALTER TABLE athletes DROP COLUMN archived_at;
