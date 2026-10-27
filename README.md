# Library Loans API

> [!NOTE]
> This is the real, open-source code of coding-challenge-library. Its home is Forgejo, at
> https://git.techcraftsmen.net/artur-rios/coding-challenge-library; the copy on GitHub is a read-only mirror, for viewing only, and
> its issues and pull requests are not followed. If you want to contribute, email me at
> arturdev@duck.com.

A REST API for a library's catalogue and its loans, built for the technical challenge in
[`docs/`](docs). .NET 10, ASP.NET Core, PostgreSQL, EF Core with Npgsql, Redis, Docker.

> **Where this lives:** the repository is at
> <https://git.techcraftsmen.net/artur-rios/coding-challenge-library>. The GitHub
> repository is a read-only mirror of it.

The scope is deliberately small. What it is trying to demonstrate is not the number of endpoints but
the handful of decisions the challenge actually asks about: that two simultaneous requests cannot lend
the same last copy, that a retried request cannot lend a second one, that nothing is ever erased
silently, and that all of it is observable and testable.

---

## Contents

- [Running it](#running-it)
- [Endpoints](#endpoints)
- [Project layout](#project-layout)
- [Concurrency: how the last copy is protected](#concurrency-how-the-last-copy-is-protected)
- [Idempotency](#idempotency)
- [Auditing](#auditing)
- [Cache](#cache)
- [Health, logs, metrics and traces](#health-logs-metrics-and-traces)
- [Kubernetes, and how this stays correct from 2 to 11 replicas](#kubernetes-and-how-this-stays-correct-from-2-to-11-replicas)
- [Tests](#tests)
- [Configuration](#configuration)
- [Known limitations, and what production would need](#known-limitations-and-what-production-would-need)

---

## Running it

### Prerequisites

- Docker with Compose v2 — that alone is enough to run everything.
- .NET 10 SDK — only to build or test outside a container.

### With Docker Compose

```bash
cp docker/local.env.example docker/local.env
```

Set `DB_PASSWORD` in the copy. It is gitignored; nothing with a real credential is committed.

```bash
docker compose --env-file docker/local.env up -d --build
```

That brings up PostgreSQL, Redis, a one-shot migrator that brings the schema up to date and exits,
and the API. The API answers on <http://localhost:8080>, with Swagger UI at
<http://localhost:8080/swagger>.

If ports 5432, 6379 or 8080 are already taken on your machine, change `DB_PORT`, `REDIS_PORT` or
`API_PORT` in the env file — they are host-side only, and the containers reach one another on their
standard ports regardless.

```bash
docker compose --env-file docker/local.env down -v   # stop, and drop the data volume
```

### A five-command tour

```bash
curl -s localhost:8080/health/ready
```

```bash
curl -s -X POST localhost:8080/books -H 'Content-Type: application/json' -H 'X-Actor: librarian@example.com' -d '{"title":"Refactoring","isbn":"978-0-13-475759-9","author":"Martin Fowler","totalCopies":1}'
```

```bash
curl -s -X POST localhost:8080/users -H 'Content-Type: application/json' -d '{"name":"Ana","email":"ana@example.com"}'
```

Then, with the two identifiers those returned — note that the same `Idempotency-Key` twice produces
one loan and a second response flagged `"replayed": true`:

```bash
curl -s -X POST localhost:8080/loans -H 'Content-Type: application/json' -H 'Idempotency-Key: demo-1' -d '{"bookId":"<book-id>","userId":"<user-id>"}'
```

```bash
curl -s localhost:8080/audit-events
```

### From source

Postgres and Redis still have to be somewhere. The quickest route is to start just those two:

```bash
docker compose --env-file docker/local.env up -d postgres redis
```

Then apply the migrations and run:

```bash
dotnet tool restore
```

```bash
LIBRARY_DATA_CONNECTIONSTRING="Host=localhost;Port=5432;Database=library;Username=library;Password=<yours>" dotnet ef database update --project src/Infrastructure/Library.Data --startup-project src/Infrastructure/Library.Data
```

```bash
dotnet run --project src/Presentation/Library.WebApi
```

The launch profile already points at `localhost` for both dependencies and sets
`LIBRARY_RUN_MIGRATIONS=true`, so `dotnet run` migrates on start-up for convenience. Containers do
not — see [Kubernetes](#kubernetes-and-how-this-stays-correct-from-2-to-11-replicas).

### Migrations

```bash
dotnet ef migrations add <Name> --project src/Infrastructure/Library.Data --startup-project src/Infrastructure/Library.Data
```

```bash
dotnet ef database update --project src/Infrastructure/Library.Data --startup-project src/Infrastructure/Library.Data
```

Both read `LIBRARY_DATA_CONNECTIONSTRING`, falling back to a localhost default so
`migrations add` works with no setup at all. The migrations are versioned in
`src/Infrastructure/Library.Data/Migrations`.

---

## Endpoints

| Method   | Route                       | Notes                                                                 |
|----------|-----------------------------|-----------------------------------------------------------------------|
| `POST`   | `/books`                    | ISBN validated by checksum and stored normalised; duplicates are 409.  |
| `GET`    | `/books`                    | Filter by `title`, `author`, `includeInactive`; paginated. **Cached.** |
| `GET`    | `/books/{id}`               | Deactivated books included.                                            |
| `PATCH`  | `/books/{id}`               | Omitted fields are left alone. Copy count adjusts atomically.          |
| `DELETE` | `/books/{id}`               | **Deactivates.** Never deletes; reports how many loans it preserved.   |
| `GET`    | `/books/{id}/availability`  | **Cached**, and advisory — see [Cache](#cache).                        |
| `GET`    | `/books/{id}/history`       | Every loan ever made, in every state.                                  |
| `POST`   | `/users`                    | E-mail normalised and unique.                                          |
| `GET`    | `/users/{id}/loans`         | The reader's full history, filterable by `status`.                     |
| `POST`   | `/loans`                    | **Requires `Idempotency-Key`.**                                        |
| `POST`   | `/loans/{id}/return`        | Keeps the loan; moves it to `Returned`.                                |
| `POST`   | `/loans/{id}/cancel`        | Keeps the loan; moves it to `Cancelled`. Optional `reason`.            |
| `GET`    | `/audit-events`             | The business trail, filterable by entity, action, actor, correlation, date. |
| `GET`    | `/health/live`              | Liveness. Runs no dependency checks.                                   |
| `GET`    | `/health/ready`             | Readiness. PostgreSQL and Redis.                                       |

### Contract decisions worth flagging

The challenge allows the contract to be adjusted where the decision is documented. Three were:

**`PATCH /books/{id}` rather than a full replace.** The suggested contract already used `PATCH`, and
the implementation takes that literally: every field is nullable, an omitted field is left alone, and
a request that supplies nothing at all is a 400 rather than a no-op success — because that is almost
always a client serializing the wrong object, and answering 200 hides it.

**`DELETE /books/{id}` deactivates.** The requirement is to *"deactivate or refuse the removal of a
book with loans/history"* and that history must not be erased silently. Rather than choosing one, the
endpoint deactivates and says so: the response carries `preservedLoanCount`, and the database refuses
the deletion outright — the loan's foreign key is `RESTRICT`, so no code path present or future can
take the history with it. Deactivating a book that still has copies out is a 409; return or cancel
them first.

**`422` alongside `409`.** A request that collides with another record (duplicate ISBN, no copies
available) is a 409. A request that is well-formed, collides with nothing, and is refused by a rule
about the value itself — lowering a book's total below the copies currently on loan — is a 422.

### Ready-to-send requests

[`api-client/`](api-client) holds the same 15 operations in two formats — a
[Bruno](https://www.usebruno.com) collection and JetBrains `.http` files. Both chain the ids they
create, so nothing has to be copied by hand, and the Bruno collection runs clean end to end:

```bash
npx @usebruno/cli run -r --env Local
```

### Error format

Failures are RFC 9457 Problem Details, as the challenge prefers:

```json
{
  "type": "https://httpstatuses.io/409",
  "title": "No copies of this book are currently available.",
  "status": 409,
  "instance": "/loans",
  "errors": ["No copies of this book are currently available."],
  "correlationId": "93cd224e-bada-4d94-b087-efe917b5dcb8"
}
```

`errors` carries every message rather than only the title — a validator commonly reports several at
once, and a caller fixing input needs all of them rather than one per round trip. `correlationId`
matches the `X-Correlation-Id` response header and the `correlationId` on the audit entries, so a
caller can go from a failed response straight to the trail.

Successes return the payload alongside the messages that produced the status:

```json
{
  "data": { "…": "…" },
  "messages": ["Loan created successfully."],
  "errors": [],
  "timestamp": "2026-08-24T20:07:30.07Z",
  "success": true
}
```

The status itself is never decided at the call site. Each canonical message maps to exactly one
status in a table beside the messages (`BookMessageMap`, `LoanMessageMap`, …), so the same refusal
always leaves the API the same way no matter which controller returned it.

---

## Project layout

```
src/
  Common/          Library.Output, Library.Mediator, Library.Data.Relational, Library.WebApi.Core
  Domain/          Library.Domain              entities and enums, no dependencies on anything above
  Application/     Library.Shared              messages, abstractions, metrics
                   Library.Command             write handlers and their validators
                   Library.Query               read handlers
  Infrastructure/  Library.Data                DbContext, entity maps, migrations, the SQL that matters
                   Library.Cache               Redis
  Presentation/    Library.WebApi              controllers, health checks, telemetry, composition
tests/
  Common/          Library.Testing             fakes, container fixtures, HTTP gateway, test base
  Domain/ Application/ Infrastructure/ Presentation/    one test project per layer
api-client/        bruno/ and http/            ready-to-send requests for every endpoint
```

`src/Common` holds the small building blocks the rest is written against — a result envelope, a
command/query mediator, a generic EF repository and unit of work, and the ASP.NET Core plumbing that
turns an envelope into a response. They are separate projects rather than folders because they know
nothing about libraries or loans, and keeping that boundary compile-enforced is the point.

Dependencies run one way: Presentation → Application → Domain, with Infrastructure plugged in at the
edges. The application layer names what it needs as an interface (`IBookCopyReserver`,
`ICacheStore`, `IIdempotencyStore`, `IAuditEventWriter`) and infrastructure supplies it, which is
what lets the handler tests run with no database at all.

---

## Concurrency: how the last copy is protected

**The rule is in the `WHERE` clause.**

Taking a copy is one statement:

```sql
UPDATE library.book
SET available_copies = available_copies - 1,
    updated_at = now()
WHERE id = @bookId
  AND is_active = TRUE
  AND available_copies > 0
```

If it affects one row, the caller has a copy. If it affects none, somebody else took the last one.
Nothing is read into memory, decided, and written back, so there is no window to lose.

Why this is sufficient: PostgreSQL takes a row-level lock for the duration of each `UPDATE`, and at
`READ COMMITTED` a statement that finds a row locked by a concurrent writer **waits, then
re-evaluates its `WHERE` clause against the committed row**. The loser therefore tests
`available_copies > 0` against the value the winner wrote, not against the value it read on arrival.
That re-evaluation is the whole mechanism.

The same shape guards the other three transitions:

| Operation            | Condition that carries the rule                        |
|----------------------|--------------------------------------------------------|
| Take a copy          | `is_active AND available_copies > 0`                   |
| Return a copy        | `available_copies < total_copies`                      |
| Change the stock     | `available_copies + (@new - total_copies) >= 0`        |
| Return/cancel a loan | `status = @expectedCurrentStatus`                      |

The last one matters as much as the first. Reading a loan, seeing it is `Active` and then writing
`Returned` leaves a window in which a second request does the same — and the copy is then credited to
the shelf twice, so the library believes it owns stock it does not have.

Underneath, three check constraints and a partial unique index hold the invariants even if the code
above them is wrong: `available_copies >= 0`, `available_copies <= total_copies`, a loan's status must
agree with its timestamps, and a reader may hold at most one active copy of a title.

### Alternatives considered

| Approach | Why not |
|---|---|
| `SELECT … FOR UPDATE`, then write | Correct, and one extra round trip per loan for a decision the single `UPDATE` already makes. It also holds the row lock across application time rather than statement time. |
| Optimistic token with a retry loop | Correct, but it turns ordinary contention into a retry loop on the hot path. Under a rush for a popular title the retry rate rises exactly when throughput matters. |
| `SERIALIZABLE` isolation | Correct, and it makes every caller handle `40001` serialization failures for a guarantee this operation does not need — the conflict here is a single row, not a read-write skew across rows. |
| `rowversion` | Not applicable. It is a SQL Server feature; the PostgreSQL analogue is the system column `xmin`. This codebase uses an application-owned `ConcurrencyStamp` where a token is genuinely wanted. |

**Where a concurrency token *is* used:** book *metadata*. Two librarians renaming the same title
concurrently is a genuine lost update and the second one should be told. A librarian adding two copies
while a reader borrows one is not a conflict at all — both changes are correct and both must apply,
which is why the copy counts never travel through the tracked entity.

**EF Core retries are deliberately off.** `EnableRetryOnFailure` installs an execution strategy that
refuses user-initiated transactions, and every write here opens one. Retrying is also not the
provider's decision to make: the caller retries with the same `Idempotency-Key`, which is exactly the
mechanism that makes a retry safe.

**Proof.** `LoanConcurrencyTests` fires 2, 10 and 15 simultaneous requests at a book with 1 or 3
copies, through the full HTTP pipeline against a real PostgreSQL, and asserts the loan count, the
refusal status and the final availability. `BookCopyReserverTests` does the same at the SQL level
across independent connections. A live check against the Compose stack gives one `201` and seven
`409`s for eight concurrent requests on one copy.

---

## Idempotency

`POST /loans` requires an `Idempotency-Key` header.

1. The key is looked up. A hit whose fingerprint matches replays the stored response verbatim, with
   `"replayed": true` added, and touches nothing.
2. A hit whose fingerprint differs is a **409**: the same key with a different body is a client bug,
   and answering it with the first response would quietly do something nobody asked for.
3. A miss opens one transaction that reserves the copy, writes the loan, writes the audit entry and
   records the key — all four, or none.

The record lives in PostgreSQL, not Redis, and in the *same transaction* as the loan. That is what
makes the guarantee hold rather than usually hold: the key becomes visible at the same instant as the
loan it produced, so there is no window in which a retry finds the loan missing and creates a second
one, nor one in which it finds a key with no response to return.

The **unique index on the key** — not the lookup in step 1 — is the actual guarantee. Two replicas
can both miss the lookup and both proceed; only one insert survives. The loser catches the unique
violation, rolls its whole transaction back (so the copy it reserved goes back on the shelf) and
answers 409 asking the caller to retry. A retry a moment later finds the stored response and is
answered with it.

The fingerprint is a SHA-256 of the request's *values*, not of its raw bytes, so whitespace or
property order in the caller's JSON cannot make an identical retry look like a different request. The
key itself is excluded from its own fingerprint. The response body column is `text` rather than
`jsonb` on purpose — `jsonb` normalises what it stores, and this column exists to replay bytes.

Keys are honoured for `LIBRARY_IDEMPOTENCY_RETENTION_HOURS` (24 by default). After that a repeat does
the work again, which is right: a client retrying a day later is making a new request.

---

## Auditing

Every write records a business event in `library.audit_event`: entity type, entity identifier,
action, actor, UTC timestamp, correlation identifier, and a `jsonb` payload with enough of the change
to understand it without finding the row. All seven actions the challenge lists are covered —
`book.created`, `book.updated`, `book.deactivated`, `user.created`, `loan.created`, `loan.returned`,
`loan.cancelled`.

**The entry is written inside the same transaction as the change.** A loan exists if and only if the
trail records it. This is a deliberate departure from a decorator-around-the-handler approach, which
is the right shape when the trail records *attempts* and losing an entry must not fail the request.
Here the trail is a business record of the library's stock: an entry committed while the loan rolls
back, or lost while the loan stands, would leave it lying. A loan that cannot be audited does not
happen.

Refusals are not recorded. The trail says what the library *did*; an attempt that changed no state is
a log line, and it is logged.

The table is append-only — nothing updates or deletes a row, `entityId` is a bare public identifier
rather than a foreign key so an entry outlives whatever it describes, and there is no endpoint that
writes to it.

`X-Actor` is the acting identity. This API has no authentication of its own — the challenge scopes it
out — so the header is taken at face value and recorded as `anonymous` when absent. The trail is
honest about what it knows: *"the caller claimed to be X"*. Behind a gateway that validates tokens,
`HttpAuditContextAccessor` is the single class that changes.

`X-Correlation-Id` is echoed on every response and stamped on every entry, so
`GET /audit-events?correlationId=…` returns everything one request caused across every entity it
touched.

---

## Cache

Redis backs two reads: the catalogue listing (`GET /books`) and per-book availability
(`GET /books/{id}/availability`).

Both are read-through with a short TTL — 60 and 30 seconds. Every write that can move those numbers
drops the affected entries immediately **after** its transaction commits: the book's own availability
key, and the whole `library:catalog:` prefix, because a listing is cached per filter and page and a
writer cannot know which pages a given book appears on. Coarse, and correct; the listing is one
indexed query to rebuild.

**PostgreSQL remains the sole source of truth for whether a loan may happen.** The availability
endpoint says so in its own response: `calculatedAt` stamps when the figure was taken, and
`canBeLoaned` is advisory. Nothing in the loan path consults Redis. A cached figure that says a copy
is free is a statement about a moment that has already passed; the only place the question is asked
and answered atomically is the conditional `UPDATE`.

Cache failures are swallowed. A read that cannot reach Redis is a miss and goes to the database; a
failed invalidation is logged at warning and the entry expires on its TTL. Redis is an accelerator,
and an outage should make the API slower rather than broken — which is also why the readiness check
reports Redis as `Degraded` rather than `Unhealthy`. With no `LIBRARY_REDIS_CONNECTIONSTRING`
configured at all, a no-op store is registered and the API runs uncached.

---

## Health, logs, metrics and traces

**`GET /health/live`** runs no checks. It answers as long as the process is up and the pipeline is
responsive, which is exactly what a liveness probe should ask. Wiring the database into it would mean
a PostgreSQL outage causes Kubernetes to restart every replica in a loop — which cannot help, and
actively hinders recovery by hammering the database with reconnections as each pod starts.

**`GET /health/ready`** checks PostgreSQL (fails readiness) and Redis (degrades only).

**Logs** are structured JSON to stdout via Serilog. Every line a request produces carries its
`CorrelationId`, from a logging scope the correlation middleware opens, along with the business
identifiers of whatever it was doing.

**Metrics** — the four the challenge names, on the `Library.Loans` meter:

| Metric | |
|---|---|
| `library.loans.created` | loans successfully created |
| `library.loans.rejected_unavailable` | attempts refused for want of a copy |
| `library.loans.idempotent_replays` | repeats answered from the store |
| `library.loans.request.duration` | latency of `POST /loans`, tagged `outcome=created\|replayed\|rejected` |

The refusal counter is the one that earns its place: it distinguishes *"the library ran out of
copies"* from *"the API is broken"*, which no HTTP-level metric can — both are 409s. The latency
histogram is split by outcome because a refusal is faster than a creation, and mixing them hides a
regression in the path that matters.

**Traces and metrics** are exported over OTLP when `LIBRARY_OTLP_ENDPOINT` is set, alongside ASP.NET
Core, HttpClient, runtime and Npgsql instrumentation — so the SQL each request issued, including the
conditional updates, appears inside that request's trace. With no endpoint set the instrumentation
still runs and exports nowhere, so there is no code path that only a deployment with a collector
exercises. Health probes are filtered out of tracing: they are the highest-volume and least
interesting requests a cluster makes.

---

## Kubernetes, and how this stays correct from 2 to 11 replicas

Manifests are in [`k8s/`](k8s) — Deployment, Service, ConfigMap, Secret with placeholder values,
migration Job and an HPA — with `kubectl apply -k k8s/`. Plain YAML rather than Helm: four files that
nothing else consumes do not need templating.

**Nothing in the API keeps state in the process.** Availability is decided by a conditional `UPDATE`
against PostgreSQL; idempotency by a unique index; the cache is shared and no decision depends on it;
the audit trail is a table. Adding a replica adds throughput and changes no outcome, so the same
argument holds at 2 and at 11.

Concretely, for each thing that could go wrong with more than one instance:

- **Two replicas lending the last copy.** Serialised by PostgreSQL's row lock; the loser's `WHERE`
  is re-evaluated against the committed row. Replica count is irrelevant.
- **Two replicas answering the same retried request.** The unique index on the idempotency key admits
  one; the other rolls back and is told to retry.
- **A reader borrowing the same title twice through different replicas.** A partial unique index over
  active loans refuses the second.
- **Replicas disagreeing about availability.** They share one Redis, and none of them decides
  anything from it.
- **Every replica migrating at start-up.** They do not. `LIBRARY_RUN_MIGRATIONS` is `false` on the
  Deployment; a `Job` runs first and exits. EF's advisory lock would make the alternative *safe*, but
  it would also serialise every start behind the first and turn a failed migration into a crash loop
  across the whole deployment rather than one failed Job with a readable log. An init container is
  the belt to that Job's braces.

The Deployment sets requests and limits (memory request equals its limit, for the Guaranteed QoS
class), liveness/readiness/startup probes, `runAsNonRoot`, a read-only root filesystem and dropped
capabilities. The HPA scales up quickly and down slowly — each scale-down closes PostgreSQL
connections, and flapping costs more than running warm.

---

## Tests

170 tests, in three suites, run separately because they cost different things.

```bash
dotnet test src/Library.slnx --filter "Category=Unit"          # 106, no I/O, ~1s
dotnet test src/Library.slnx --filter "Category=Integration"   #  24, real PostgreSQL + Redis
dotnet test src/Library.slnx --filter "Category=Functional"    #  40, end to end over HTTP
dotnet test src/Library.slnx                                   # everything
```

The last two need a Docker daemon; Testcontainers starts PostgreSQL 17 and Redis 7 once per suite and
applies the migrations to them.

**Real containers, not an in-memory provider.** Everything this API does about concurrency is
expressed in PostgreSQL — atomic conditional updates, a partial unique index, check constraints, row
locks under `READ COMMITTED`. An in-memory provider implements none of it and would report every one
of those tests as passing while proving nothing.

What the challenge asks for specifically:

| Requirement | Where |
|---|---|
| Unit tests of the business rules | `Library.Command.Tests`, `Library.Query.Tests`, `Library.Domain.Tests` |
| Integration tests against real PostgreSQL | `Library.Data.Tests`, `Library.WebApi.Tests` |
| Concurrent test for the last available copy | `LoanConcurrencyTests`, `BookCopyReserverTests` |
| Idempotency test | `LoanIdempotencyTests`, `IdempotencyStoreTests` |
| History preserved after return/cancellation | `LoanHistoryPreservationTests`, `HistoryQueryHandlerTests` |

Test names follow `GivenSomeCondition_WhenSomeAction_ThenSomeOutcome`, and each body is marked
`// Given` / `// When` / `// Then`.

CI runs all three suites plus a vulnerable-dependency scan (the `test` check, [`ci/test.sh`](ci/test.sh)), and
builds the container image in a separate check (`docker`, [`ci/docker.sh`](ci/docker.sh)) — the Dockerfile
restores from a hand-listed set of project files, so a new project can break the image build while
every test stays green. Jenkins runs both; [`yggdrasil.yml`](yggdrasil.yml) says how.

---

## Configuration

Everything comes from environment variables; `appsettings.*.json` carries only log levels. No secret,
connection string or key is committed.

| Variable | Required | Default | |
|---|---|---|---|
| `LIBRARY_DATA_CONNECTIONSTRING` | yes | — | Npgsql connection string. The API refuses to start without it. |
| `LIBRARY_REDIS_CONNECTIONSTRING` | no | — | Unset, the API runs uncached. |
| `LIBRARY_RUN_MIGRATIONS` | no | `false` | `true` applies pending migrations at start-up. |
| `LIBRARY_IDEMPOTENCY_RETENTION_HOURS` | no | `24` | How long a used key is honoured. |
| `LIBRARY_OTLP_ENDPOINT` | no | — | Unset, telemetry is collected but not exported. |
| `ASPNETCORE_ENVIRONMENT` | no | `Production` | Non-Production serves Swagger UI and turns on EF diagnostics. |

EF's sensitive-data logging is on outside Production and off in it: it writes every parameter value
into the log, which here means reader names and e-mail addresses on every query.

---

## Known limitations, and what production would need

Stated plainly, because most of these are scope decisions rather than oversights.

**No authentication or authorization.** The challenge scopes it out, and nothing here invents one.
The consequence is real: `X-Actor` is self-asserted, so the audit trail records a claim rather than a
verified identity, and every endpoint is open. Production needs a gateway or JWT middleware in front,
after which `HttpAuditContextAccessor` reads the subject claim and nothing else changes.

**No rate limiting.** `POST /loans` is the endpoint that would want it first.

**Idempotency records are never swept.** They expire logically — an expired key is treated as absent
— but nothing deletes the rows. The index for it exists (`ix_idempotency_record_expires_at`); the job
that uses it does not. A `DELETE … WHERE expires_at < now()` on a schedule, or a partitioned table.

**Idempotency is only on loan creation.** Return and cancellation are naturally idempotent in effect
— the second call is refused with a 409 rather than repeating the work — but they do not replay a
stored response, so a client that lost the first response cannot tell "I already did this" from
"somebody else did this".

**Coarse cache invalidation.** Every catalogue write drops the whole listing prefix. Fine at this
size; at a much larger catalogue it would be worth versioning the prefix rather than scanning it.

**The audit payload is unbounded in shape.** It is written by this API only, from fixed anonymous
objects, so it cannot grow arbitrarily today — but nothing enforces a schema, and a trail retained for
years wants one.

**No pagination cursor.** Offset pagination is stable here because every listing has a unique
tiebreaker, but deep pages get slower. Keyset pagination if the history endpoints ever need it.

**No soft delete for readers.** Books deactivate; readers do not. Consistency would suggest they
should, and GDPR-style erasure requests would force the question of what happens to their loan
history — which is exactly the tension the book deactivation design already resolves.

**Single database, no read replicas.** The read endpoints are the ones that would move first, and the
cache already absorbs most of that load.

---

## Notes on provenance

Written for this challenge, against the structure and conventions of an existing service of mine
(a .NET 10 identity API) — layered projects, command/query handlers returning result envelopes,
canonical messages mapped to status codes, the Given/When/Then test style. The building blocks in
`src/Common` are reimplemented here from scratch rather than taken from published packages, so the
repository is self-contained and every line in it is up for discussion.
