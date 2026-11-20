# Security Policy

## Supported Versions

Security fixes go into the latest release of `PgFreshCache.Lite`: upgrade to it to get a fix.

## Reporting a Vulnerability

Please don't open a public issue. Report it privately on GitHub, in the repository's
[Security tab](https://github.com/PgOutput2Json/PgFreshCache/security/advisories/new) ("Report a vulnerability").

Include what's affected (the version), what an attacker can do, and how to reproduce it. You'll get an answer within a
week. Once it's fixed and released, the advisory is published, with credit to you if you want it.

A vulnerability in the replication itself or in how rows are written to SQLite belongs to
[PgOutput2Json](https://github.com/PgOutput2Json/PgOutput2Json/security/advisories/new). If you're not sure which, report
it here and it will be moved.

## What's in Scope

- **The cache is read-only for the application**: a way to write to the cache, or change its schema, through the keyed
  cache `DbContext` (or a connection it opened) is a vulnerability.
- **SQL built by PgFreshCache**: the schema it creates from the `DbContext` model and the statements it runs. A way to
  inject SQL through a cache key, a connection string option or a table or column name is a vulnerability.
- **The cache options**: a cache key's `DbContext` getting another cache's database, or the application's regular
  (PostgreSQL) options, so it reads data it should not.
- **Secrets**: connection strings or passwords written to logs or to the cache.

Not in scope:

- **The cache holds a copy of the published tables.** Everything the replication user can read ends up in the SQLite
  database: publish only what the application may read. A file cache (`UseSqlite`) is readable by whoever can read the
  file, so protect it as you would the data itself; the default in-memory cache lives only in the process.
- **The replication user reads the published tables.** Whoever has its connection string can read every change of
  them, as with PostgreSQL's own logical replication. Give it only the access described in the README.
- The security of PostgreSQL and SQLite themselves.
