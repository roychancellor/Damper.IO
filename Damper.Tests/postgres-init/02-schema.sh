#!/bin/bash

set -e

echo "Creating Damper database schema..."

psql \
    --username "$POSTGRES_USER" \
    --dbname "$POSTGRES_DB" \
    --set=admin_user="$DAMPER_ADMIN_USER" \
    --set=runtime_user="$DAMPER_RUNTIME_USER" \
    --set=database_name="$POSTGRES_DB" \
    <<'SQL'

CREATE SCHEMA IF NOT EXISTS damper
    AUTHORIZATION :"admin_user";

GRANT CONNECT
ON DATABASE :"database_name"
TO :"runtime_user";

GRANT USAGE
ON SCHEMA damper
TO :"runtime_user";

REVOKE CREATE
ON SCHEMA damper
FROM :"runtime_user";

SET ROLE :"admin_user";

CREATE TABLE IF NOT EXISTS damper.integration
(
    id              BIGINT GENERATED ALWAYS AS IDENTITY,
    name            TEXT NOT NULL,
    enabled         BOOLEAN NOT NULL DEFAULT TRUE,
    api_key_hash    BYTEA NOT NULL,
    configuration   JSONB NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL,
    modified_at     TIMESTAMPTZ NOT NULL,

    CONSTRAINT pk_integration
        PRIMARY KEY (id),

    CONSTRAINT uq_integration_api_key_hash
        UNIQUE (api_key_hash)
);

CREATE TABLE IF NOT EXISTS damper.schema_version
(
    version     INTEGER NOT NULL,
    applied_at  TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT pk_schema_version
        PRIMARY KEY (version)
);

GRANT SELECT, INSERT, UPDATE, DELETE
ON ALL TABLES IN SCHEMA damper
TO :"runtime_user";

GRANT USAGE, SELECT
ON ALL SEQUENCES IN SCHEMA damper
TO :"runtime_user";

ALTER DEFAULT PRIVILEGES
IN SCHEMA damper
GRANT SELECT, INSERT, UPDATE, DELETE
ON TABLES
TO :"runtime_user";

ALTER DEFAULT PRIVILEGES
IN SCHEMA damper
GRANT USAGE, SELECT
ON SEQUENCES
TO :"runtime_user";

RESET ROLE;

SQL

echo "Damper database schema created."