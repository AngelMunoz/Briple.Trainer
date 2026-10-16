-- MIGRONDI:NAME=1792099710731_CreateBripleSchema.sql
-- MIGRONDI:TIMESTAMP=1792099710731
-- ---------- MIGRONDI:UP ----------
-- Add your SQL migration code below. You can delete this line but do not delete the comments above.

create table if not exists "athletes"(
    "id" text primary key,
    "name" text not null,
    "gender" text not null,
    "variants" text not null,
    "goals" text not null,
    "focus" text not null,
    "created_at" text not null
);

create table if not exists "memories"(
    "id" text primary key,
    "athlete_id" text not null,
    "summary" text not null,
    "status" text not null,
    "origin" text not null,
    "deltas" text not null,
    "created_at" text not null,
    foreign key("athlete_id") references "athletes"("id") on delete cascade
);

create table if not exists "plans"(
    "id" text primary key,
    "athlete_id" text not null,
    "title" text not null,
    "kind" text not null,
    "weeks" integer not null,
    "counts" text not null,
    "raw" text not null,
    "created_at" text not null,
    foreign key("athlete_id") references "athletes"("id") on delete cascade
);

create table if not exists "requests"(
    "id" text primary key,
    "athlete_id" text not null,
    "kind" text not null,
    "payload" text not null,
    "created_at" text not null,
    foreign key("athlete_id") references "athletes"("id") on delete cascade
);


-- ---------- MIGRONDI:DOWN ----------
-- Add your SQL rollback code below. You can delete this line but do not delete comment above.

drop table if exists "requests";
drop table if exists "plans";
drop table if exists "memories";
drop table if exists "athletes";


