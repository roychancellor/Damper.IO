#!/bin/bash

set -e

echo "Creating Damper PostgreSQL roles..."

psql \
    --username "$POSTGRES_USER" \
    --dbname "$POSTGRES_DB" \
    --set=admin_user="$DAMPER_ADMIN_USER" \
    --set=admin_password="$DAMPER_ADMIN_PASSWORD" \
    --set=runtime_user="$DAMPER_RUNTIME_USER" \
    --set=runtime_password="$DAMPER_RUNTIME_PASSWORD" \
    --set=database_name="$POSTGRES_DB" \
    <<'SQL'

CREATE ROLE :"admin_user"
    LOGIN
    NOSUPERUSER
    NOCREATEDB
    NOCREATEROLE
    NOREPLICATION
    NOBYPASSRLS
    PASSWORD :'admin_password';

CREATE ROLE :"runtime_user"
    LOGIN
    NOSUPERUSER
    NOCREATEDB
    NOCREATEROLE
    NOREPLICATION
    NOBYPASSRLS
    PASSWORD :'runtime_password';

ALTER DATABASE :"database_name"
    OWNER TO :"admin_user";

SQL

echo "Damper PostgreSQL roles created."