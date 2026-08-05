I think this captures the design in a way that will be easy to revisit later:

---

## Damper Installation Engine

### Philosophy

The installer's job is **not** to "install" Damper. Its job is to **converge the environment to the desired state**.

The installer should be **idempotent**, **version-aware**, and **safe**.

### Responsibilities

* Verify Docker prerequisites.
* Verify PostgreSQL.
* Ensure the required database, schema, tables, indexes, and schema version exist.
* Verify RabbitMQ.
* Ensure the required exchanges, queues, bindings, policies, users, and topology version exist.
* Verify configuration files.
* Perform a final verification and produce a clear installation report.

### Core Pattern

Every installable component follows the same pattern:

1. **Verify** – Inspect the current environment; make no changes.
2. **Ensure** – Create or repair only what is missing or safely upgradable.
3. **Verify Again** – Confirm the environment now matches the expected state.

### Repair Philosophy

* Create missing resources automatically.
* Apply known, safe upgrades automatically.
* **Never** delete or replace existing infrastructure automatically if doing so could be destructive (e.g., wrong RabbitMQ exchange type). Instead, fail with a clear diagnostic message.

### Versioning

Track independent versions for:

* Application
* PostgreSQL schema
* RabbitMQ topology

The installer upgrades schema/topology only when a supported migration path exists.

### Architecture

Create a dedicated **Damper.Setup** .NET console application.

PowerShell's role is limited to bootstrapping the environment (Docker, images, prerequisites) and invoking `Damper.Setup`.

`Damper.Setup` owns all Damper-specific installation logic.

### Guiding Principle

> **Verify. Ensure. Verify.**

The runtime processes messages. The installer prepares the environment. The runtime never creates infrastructure—it only validates that the required infrastructure exists.
