CREATE SCHEMA IF NOT EXISTS damper;

CREATE TABLE IF NOT EXISTS damper.integration (
    id BIGINT GENERATED ALWAYS AS IDENTITY,
    name TEXT NOT NULL,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    api_key_hash BYTEA NOT NULL,
    configuration JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT pk_integration
        PRIMARY KEY (id),

    CONSTRAINT uq_integration_api_key_hash
        UNIQUE (api_key_hash)
);

CREATE TABLE IF NOT EXISTS damper.schema_version (
    version INTEGER NOT NULL,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT pk_schema_version
        PRIMARY KEY (version)
);