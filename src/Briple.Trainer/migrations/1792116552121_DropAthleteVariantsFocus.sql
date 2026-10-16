-- MIGRONDI:NAME=1792116552121_DropAthleteVariantsFocus.sql
-- MIGRONDI:TIMESTAMP=1792116552121
-- ---------- MIGRONDI:UP ----------
-- Add your SQL migration code below. You can delete this line but do not delete the comments above.

-- The training frequency moved from the athlete profile to each plan's
-- brief; the free-text focus became the typed emphasis of the brief.
ALTER TABLE athletes DROP COLUMN variants;
ALTER TABLE athletes DROP COLUMN focus;

-- ---------- MIGRONDI:DOWN ----------
-- Add your SQL rollback code below. You can delete this line but do not delete comment above.

ALTER TABLE athletes ADD COLUMN variants TEXT NOT NULL DEFAULT '[]';
ALTER TABLE athletes ADD COLUMN focus TEXT NOT NULL DEFAULT '[]';


