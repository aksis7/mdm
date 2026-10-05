# PostgreSQL integration tests

These tests exercise the composition CTE, object search/card queries, and
concurrent import/version writes against PostgreSQL. Point
`PDM_TEST_POSTGRES_CONNECTION` at a dedicated test database whose schema has
already been migrated. Do not point it at a database containing user data.

Read-query tests run their work in transactions and roll them back. Concurrent
write tests create uniquely identified fixture objects and may commit changes;
their `finally` cleanup removes only fixture objects, their versions and links,
and the import journal row created by that test. Tests do not truncate tables,
drop the schema, or run migrations.

```sh
export PDM_TEST_POSTGRES_CONNECTION='Host=127.0.0.1;Port=55432;Database=pdm_tree;Username=pdm_check;Password=...'
dotnet test tests/MiniPdm.Postgres.Tests/MiniPdm.Postgres.Tests.csproj
```

The connection string is intentionally supplied by the environment and is not
stored in this repository. The regular solution test suite does not require
PostgreSQL; this project is invoked separately. The latest run passed all 11
tests. The temporary verification database has since been stopped and removed.
