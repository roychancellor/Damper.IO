CREATE SCHEMA IF NOT EXISTS damper;

CREATE TABLE IF NOT EXISTS damper.integration (
    id              BIGINT GENERATED ALWAYS AS IDENTITY,
    name            TEXT NOT NULL,
    enabled         BOOLEAN NOT NULL DEFAULT TRUE,
    api_key_hash    BYTEA NOT NULL,
    configuration   JSONB NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at     TIMESTAMPTZ NOT NULL DEFAULT now(),

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

-- CREATE THE FUNCTIONS DAPPER NEEDS TO OPERATE
CREATE OR REPLACE FUNCTION damper.integration_get_by_id(p_id bigint)
RETURNS TABLE
(
    id bigint,
    name text,
    enabled boolean,
    api_key_hash bytea,
    configuration jsonb,
    created_at timestamptz,
    modified_at timestamptz
)
LANGUAGE sql
STABLE
AS $$
    SELECT
        i.id,
        i.name,
        i.enabled,
        i.api_key_hash,
        i.configuration,
        i.created_at,
        i.modified_at
    FROM damper.integration AS i
    WHERE i.id = p_id;
$$;

CREATE OR REPLACE FUNCTION damper.integration_get_by_api_key_hash(p_api_key_hash bytea)
RETURNS TABLE
(
    id bigint,
    name text,
    enabled boolean,
    api_key_hash bytea,
    configuration jsonb,
    created_at timestamptz,
    modified_at timestamptz
)
LANGUAGE sql
STABLE
AS $$
    SELECT
        i.id,
        i.name,
        i.enabled,
        i.api_key_hash,
        i.configuration,
        i.created_at,
        i.modified_at
    FROM damper.integration AS i
    WHERE i.api_key_hash = p_api_key_hash
      AND i.enabled = true;
$$;

CREATE OR REPLACE FUNCTION damper.integration_get_all()
RETURNS TABLE
(
    id bigint,
    name text,
    enabled boolean,
    api_key_hash bytea,
    configuration jsonb,
    created_at timestamptz,
    modified_at timestamptz
)
LANGUAGE sql
STABLE
AS $$
    SELECT
        i.id,
        i.name,
        i.enabled,
        i.api_key_hash,
        i.configuration,
        i.created_at,
        i.modified_at
    FROM damper.integration AS i
    ORDER BY i.id;
$$;

CREATE OR REPLACE FUNCTION damper.integration_insert
(
    p_name text,
    p_enabled boolean,
    p_api_key_hash bytea,
    p_configuration jsonb
)
RETURNS TABLE
(
    id bigint,
    name text,
    enabled boolean,
    api_key_hash bytea,
    configuration jsonb,
    created_at timestamptz,
    modified_at timestamptz
)
LANGUAGE sql
VOLATILE
AS $$
    INSERT INTO damper.integration AS i
    (
        name,
        enabled,
        api_key_hash,
        configuration
    )
    VALUES
    (
        p_name,
        p_enabled,
        p_api_key_hash,
        p_configuration
    )
    RETURNING
        i.id,
        i.name,
        i.enabled,
        i.api_key_hash,
        i.configuration,
        i.created_at,
        i.modified_at;
$$;

CREATE OR REPLACE FUNCTION damper.integration_update
(
    p_id bigint,
    p_name text,
    p_enabled boolean,
    p_api_key_hash bytea,
    p_configuration jsonb
)
RETURNS TABLE
(
    id bigint,
    name text,
    enabled boolean,
    api_key_hash bytea,
    configuration jsonb,
    created_at timestamptz,
    modified_at timestamptz
)
LANGUAGE sql
VOLATILE
AS $$
    UPDATE damper.integration AS i
    SET
        name = p_name,
        enabled = p_enabled,
        api_key_hash = p_api_key_hash,
        configuration = p_configuration,
        modified_at = now()
    WHERE i.id = p_id
    RETURNING
        i.id,
        i.name,
        i.enabled,
        i.api_key_hash,
        i.configuration,
        i.created_at,
        i.modified_at;
$$;

CREATE OR REPLACE FUNCTION damper.integration_delete(p_id bigint)
RETURNS bigint
LANGUAGE sql
VOLATILE
AS $$
    DELETE FROM damper.integration
    WHERE id = p_id
    RETURNING id;
$$;

-- REVOKE PRIVILEGES FROM PUBLIC
REVOKE ALL ON FUNCTION damper.integration_get_by_id(bigint) FROM PUBLIC;
REVOKE ALL ON FUNCTION damper.integration_get_by_api_key_hash(bytea) FROM PUBLIC;
REVOKE ALL ON FUNCTION damper.integration_get_all() FROM PUBLIC;
REVOKE ALL ON FUNCTION damper.integration_insert(text, boolean, bytea, jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION damper.integration_update(bigint, text, boolean, bytea, jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION damper.integration_delete(bigint) FROM PUBLIC;

-- GRANTS FOR CRUD FUNCTIONS
GRANT EXECUTE ON FUNCTION damper.integration_get_by_id(bigint) TO "damper-runtime";
GRANT EXECUTE ON FUNCTION damper.integration_get_by_api_key_hash(bytea) TO "damper-runtime";
GRANT EXECUTE ON FUNCTION damper.integration_get_all() TO "damper-runtime";

GRANT EXECUTE ON FUNCTION damper.integration_insert(text, boolean, bytea, jsonb) TO "damper-runtime";

GRANT EXECUTE ON FUNCTION damper.integration_update(bigint, text, boolean, bytea, jsonb) TO "damper-runtime";

GRANT EXECUTE ON FUNCTION damper.integration_delete(bigint) TO "damper-runtime";