-- MIGRONDI:NAME=1792119522752_AddMemoryScopeAndPlan.sql
-- MIGRONDI:TIMESTAMP=1792119522752
-- ---------- MIGRONDI:UP ----------
-- Add your SQL migration code below. You can delete this line but do not delete the comments above.

-- A memory lives for the plan it was observed on or for the athlete; the
-- plan_id carries which plan a plan-scoped memory belongs to.
ALTER TABLE memories ADD COLUMN scope TEXT NOT NULL DEFAULT 'plan';
ALTER TABLE memories ADD COLUMN plan_id TEXT NULL;

-- ---------- MIGRONDI:DOWN ----------
-- Add your SQL rollback code below. You can delete this line but do not delete comment above.

ALTER TABLE memories DROP COLUMN scope;
ALTER TABLE memories DROP COLUMN plan_id;


