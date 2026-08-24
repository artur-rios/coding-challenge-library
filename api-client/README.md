# API client collections

Ready-to-send requests for every endpoint the API exposes, in two formats:

| Directory | Format | Opens in |
| --- | --- | --- |
| [`http/`](http) | JetBrains HTTP Client (`.http`) | Rider, IntelliJ IDEA, and VS Code with the REST Client extension |
| [`bruno/`](bruno) | [Bruno](https://www.usebruno.com) collection | The Bruno app, or `bru run` on the command line |

Both cover the same 15 operations, organised by subject: books, users, loans, audit, health,
withdrawal. For
what each endpoint means, see the [README](../README.md) or a running instance's own Swagger UI at
`/swagger`.

## Before the first request

Start the API — see [Running it](../README.md#running-it):

```bash
docker compose --env-file docker/local.env up -d --build
```

The collections default to `http://localhost:8080`, which is what Compose publishes. If you changed
`API_PORT`, change `host` in `http/http-client.env.json` or `bruno/environments/Local.bru` to match.

There is nothing else to configure. **This API has no authentication**, so there is no token to
fetch and no credentials in either environment file — which is also why neither of them holds
anything secret. `X-Actor` is what the audit trail records, and it is self-asserted: the trail
stores a claim, not a verified identity. That is a stated limitation of the challenge scope, not
something the collections work around.

## Order on an empty database

Requests that create something store the new id, and the rest of the collection reads those ids. On
a database with nothing in it, three requests have to run in this order before any other will find
its subject:

1. **Books › Create a book** — stores `bookId`.
2. **Users › Register a reader** — stores `userId`.
3. **Loans › Lend a copy** — stores `loanId`.

Everything else can then be run in any order.

**The Bruno collection runs clean end to end**, repeatedly, against a fresh database or a used one:

```bash
npx @usebruno/cli run -r --env Local
```

19 requests, 17 of them `2xx` and two `409`s that are the point (see below). It is repeatable
because the three fixture requests mint their own unique values in a pre-request script — a fresh
ISBN, a fresh reader address, a fresh idempotency key — rather than relying on a constant that is
already taken the second time you run it.

The `.http` files have no equivalent hook, so there the ISBN, the reader address and the
idempotency key are variables in `http-client.env.json` that you change between runs. Leaving them
answers `409` for the two creates and *replays* the previous loan rather than making a new one —
each of which is the API working, and confusing if you did not expect it.

One deliberate exception to subject grouping: deactivating a book lives in **Withdrawal** /
`withdrawal.http` rather than at the end of Books. It is refused while copies are out, so running
it in the Books folder would withdraw the title before the Loans folder ever borrowed from it.

## The requests that are supposed to fail

Four of them, and each demonstrates a guarantee rather than a bug:

| Request | Answers | Why |
| --- | --- | --- |
| Loans › Replay the same Idempotency-Key | `201`, `"replayed": true` | Same key, same body — answered from the store, no second loan. |
| Loans › Reuse a key with a different request | `409` | Same key, different body. Replaying would do something nobody asked for. |
| Loans › Cancel a loan, after returning it | `409` | Naming the return specifically, not a generic conflict. |
| Loans › Omit the Idempotency-Key (`http/` only) | `400` | Required by this endpoint, not optional. |
| Audit › An unknown entity type (`http/` only) | `400` | Refused rather than answered with an empty page. |

One more worth knowing about: **Audit › Trace one request end to end** returns nothing if the write
it is tracing was refused. That is not a broken filter — refusals are logged, not audited, and the
trail is correctly recording that nothing happened. The request before it is a `PATCH` rather than a
create for exactly this reason: it can be re-run without turning into a `409`.

## How the chaining works

Neither collection asks you to copy a GUID by hand.

In the `.http` files, a response handler stores the value:

```
> {% client.global.set("bookId", response.body.data.id); %}
```

and in Bruno, a post-response script does the same:

```
script:post-response {
  bru.setVar("bookId", res.body.data.id);
}
```

Both write to a **runtime** variable, which takes precedence over the environment file without
modifying it — so the placeholder GUIDs in `Local.bru` and `http-client.env.json` are only ever what
a variable reads *before* the request that fills it has run, and a collection run never leaves a
diff behind. (Bruno's `bru.setEnvVar` would have written the id back into the tracked template; that
is what `bru.setVar` avoids.)

`idempotencyKey` is chained differently: **Loans › Lend a copy** mints a fresh one per run and the
two requests after it reuse whatever it set, so they demonstrate a replay and a conflict against a
loan that exists *right now* rather than one from a previous run. To retry a specific key by hand,
pin it:

```bash
npx @usebruno/cli run Loans --env Local --env-var pinIdempotencyKey=1 --env-var idempotencyKey=whatever
```

## Seeing the concurrency guarantee

Neither collection can fire two requests at the same instant, so the race is proven by the test
suite rather than here — `LoanConcurrencyTests` sends 2, 10 and 15 simultaneous requests at a book
with 1 or 3 copies and asserts the loan count and the refusal status:

```bash
dotnet test src/Library.slnx --filter "Category=Functional"
```

To watch it by hand against a running instance, seed a book with `totalCopies` 1, register two
readers, and send **Loans › Lend a copy** twice at once with different `userId`s and different
idempotency keys. One answers `201`; the other answers `409` with *"No copies of this book are
currently available."*, and availability sits at zero rather than at minus one.

## Running the Bruno collection from the command line

```bash
npx @usebruno/cli run -r --env Local
```

Individual folders work too, which is usually what you want given the ordering above:

```bash
npx @usebruno/cli run Books --env Local
```

Point it at a different port without editing the environment file:

```bash
npx @usebruno/cli run -r --env Local --env-var host=http://localhost:18080
```

## Keeping secrets out of git

Neither environment file holds a credential today, because the API has none to hold. If you point
these collections at a deployment that sits behind an authenticating gateway, both clients support a
private overlay that is gitignored:

- **JetBrains** reads `http/http-client.private.env.json`, which wins over the tracked file per key.
- **Bruno** reads `bruno/.env`, whose values are available as `{{process.env.NAME}}`.

Both are already in the repository's `.gitignore`.
