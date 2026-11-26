# Endpoint rewriting for copied user-secrets

Inside a sandbox, `localhost` is the sandbox, not your machine. A connection string
that points at a database on the host stops working there. When abox
[copies user-secrets](user-secrets.md), it therefore rewrites local endpoints to
`host.docker.internal`, for the host ports the sandbox is allowed to reach.

```text
Server=localhost,8004;Database=dev  ->  Server=host.docker.internal,8004;Database=dev
http://127.0.0.1:8006/path          ->  http://host.docker.internal:8006/path
```

Only the copy in the sandbox changes. Host stores, projects and appsettings files are
never modified. Rewriting is part of every copy; there is no separate command and no
switch.

## Which ports are rewritten

A port is rewritten when the network policy of the sandbox both declares it and
currently allows it. abox reads this from the running sandbox, so it works the same
for local and published squads and needs no kit sources.

```sh
sbx policy ls <sandbox> --type network --json
sbx policy check network --sandbox <sandbox> --protocol tcp --json localhost:<port>
```

A port is declared by a TCP allow rule for exactly `localhost:<port>` that is either

- provisioned by a kit of the squad, or
- added by you to this sandbox:

  ```sh
  sbx policy allow network --sandbox <sandbox> --protocol tcp localhost:1521
  ```

  This grants the sandbox access to that host port. abox never runs it for you. Run
  `abox user-secrets` afterwards to apply the rewrite.

Rules that are wildcards, global, for all ports or for another sandbox declare
nothing, even when they allow the connection. A declared port that a deny rule blocks
is not rewritten. No declared ports means no rewrites. Policy output that abox cannot
interpret fails the copy before anything is written.

The port never changes. A host service published as `8004:5432` is reached at
`localhost:8004`; `localhost:5432` is not rewritten to 8004. An allowed port also
says nothing about whether the service is running.

## What is rewritten

Rewriting looks at whole string values, at any depth of the store. It replaces only
the host. Credentials, ports, database names, paths, queries, options, quoting and
field order stay as they are.

Local hosts are `localhost` in any case, IPv4 loopback such as `127.0.0.1`, and IPv6
loopback `::1`.

| Form | Example | Notes |
| --- | --- | --- |
| Absolute URL | `http://localhost:8006/path`, `postgresql://localhost:8004/db` | Without a port, the default of the scheme must be declared. |
| SQL Server | `Server=localhost,8004` | Fields `Server`, `Data Source`, `Address`, `Addr`, `Network Address`; optional `tcp:` prefix. |
| Host and port fields | `Host=localhost;Port=8004`, `Server=localhost;Port=3306` | PostgreSQL `Host` without `Port` uses 5432, when 5432 is declared. |
| PostgreSQL inline port | `Host=localhost:8004` | |
| Oracle | see [Oracle](#oracle) | |
| Split setting | `Database_Config:Host` and `Database_Config:Port` | see [Split host and port](#split-host-and-port) |

Left unchanged:

| Value | Why |
| --- | --- |
| `https://sql.internal:8004` | A named host keeps its name, also for TLS. |
| `Server=host.docker.internal,8004` | Already rewritten. |
| `Server=localhost,5432` when 5432 is not declared | The port is not declared. |
| `http://127.0.0.1:8006/path?q=localhost:8004` | Only the host of the URL changes, not text in the query. |
| `the db runs on localhost:8004` | Prose is not an endpoint. |

Not supported: host aliases, other spellings of loopback, SQL Server named instances,
relative URLs and endpoints inside other text or embedded JSON.

### Ambiguous endpoints fail the copy

When a value clearly is a local endpoint at a declared port, but abox cannot rewrite
it safely, the copy of all stores fails before anything is written. The message names
the store, the key and the project, never the value. `--force` does not get past it.
Examples:

- a malformed URL;
- several hosts or ports in one connection string, or the same field twice;
- unfinished quotes;
- a PostgreSQL URL with a host list, or with `host`, `hostaddr` or `port` in its
  query.

Fix the value, or leave the key out with a `keys` selection. A value that only
mentions `localhost`, or uses a port that is not declared, is never an error.

### Oracle

Oracle endpoints are recognized as whole values and inside the `Data Source` field of
an Oracle connection string.

- **Easy Connect:** `[//]host[:port][/service[:server][/instance]][?options]`, with
  an optional `tcp://` or `tcps://`. Without a port, 1521 must be declared. A bare
  name without prefix, port or service is a TNS alias and stays unchanged.
- **Easy Connect Plus:** lists of hosts with shared or own ports, such as
  `localhost,remote.test:1521/service`. Only the local hosts at declared ports change.
- **Descriptors:** `(DESCRIPTION=...)`, `(DESCRIPTION_LIST=...)`, `(ADDRESS_LIST=...)`
  and `(ADDRESS=...)`. Only the `HOST` of a TCP or TCPS `ADDRESS` with an explicit,
  declared `PORT` changes.

A local descriptor `ADDRESS` without `PORT` fails the copy when 1521 is declared: add
the `PORT`. Other Oracle endpoints that fail are unbalanced descriptors, a duplicate
host, port or protocol in one address, an unsupported protocol, IPv6 without brackets
in Easy Connect, and more than 64 nested descriptor levels.

`tnsnames.ora` aliases, JDBC URLs and SQL*Plus logins are not interpreted.

### Split host and port

One pair of separate settings is recognized: `Database_Config:Host` with
`Database_Config:Port`, in any case. Other pairs, such as `Other:Host` and
`Other:Port`, are not.

```json
{ "Database_Config": { "Host": "localhost", "Port": 8004, "Database": "development" } }
```

With port 8004 declared, the sandbox gets `"Database_Config:Host":
"host.docker.internal"`. The port is not touched. It can be a JSON number or a string
of digits. Without a usable port nothing is rewritten; no default is assumed.

Only selected user-secret keys count. A port that your `keys` selection leaves out is
ignored; the port from appsettings is used instead, when there is one. Leaving out the
host key turns the rewrite off.

A local pair at a declared port with a host abox cannot use, such as a host list or a
host with an inline port, fails the copy.

## Overrides from appsettings

Endpoints often live in `appsettings.json`, not in user-secrets. abox does not change
project files. It adds an override to the sandbox's copy of the store instead, because
user-secrets take precedence over appsettings in .NET.

With `userSecrets: [api-development]`, a project that declares
`<UserSecretsId>api-development</UserSecretsId>` and this `appsettings.development.json`:

```json
{
  "Telemetry": { "Endpoint": "http://localhost:4317" },
  "WorkerApi": "http://localhost:5000/api"
}
```

and a sandbox where port 4317 is declared but 5000 is not, the copied store gets:

```json
{ "Telemetry:Endpoint": "http://host.docker.internal:4317" }
```

Only values that need a rewrite are added, as flat keys. The rest of appsettings is
not copied.

### Which projects count

abox searches the sandbox location for project files that declare a selected store
ID. It reads the files as text and never runs MSBuild.

- The project must hold exactly one `<UserSecretsId>` with a literal value, without a
  condition, in a top-level `PropertyGroup`. A project that names a selected ID in
  another way, such as conditionally or twice, fails the copy.
- IDs that are computed, or come from an import such as `Directory.Build.props`, are
  not seen. Such projects are skipped without a message. Put the literal ID into the
  project file to include them.
- Folders named `.git`, `bin`, `obj` and `node_modules` are skipped, as are nCrunch
  copies (`nCrunchTemp_*.csproj`) and directory links that leave the workspace.
- Projects never select a store. Store prefixes such as `Cape.*` match host store
  folders only; for a store that exists only as a project ID, use the exact ID.

### Which values win

1. `appsettings.json` of the project.
2. `appsettings.development.json` in exactly this lowercase spelling.
   `appsettings.Development.json` is not read.
3. The selected keys of the host store.

A selected key of the host store always wins and blocks an override for that key,
whatever its value. It is still rewritten when it is a local endpoint itself. A `keys`
selection applies to overrides too: a key it leaves out is never added.

Appsettings files must be valid JSON objects without duplicate keys, as for stores.
Values that lead to an override are checked against the deny list.

Environment variables, command-line arguments, launch profiles, other environments
and custom configuration providers are not considered.

### Projects that share a store

A store has one value per key. When several projects use the same store, an override
must suit all of them.

For each key that needs an override, every project that defines the key must end up
with the same value. If one project has `http://localhost:8004/path` and another has
a remote URL, or a local one at a port that is not declared, the copy of all stores
fails. The message names the store, the key and the projects. A project that does not
define the key does not conflict.

To resolve a conflict:

- put the intended value into the host store and select it, or
- leave the key out with a `keys` selection, or
- give the projects separate stores.

## When overrides are refreshed

Every copy builds the destination again from the current host store and the current
appsettings and policy. Overrides that no longer apply disappear. After changing
appsettings or the sandbox's network policy, run:

```sh
abox user-secrets --location "/path/to/project"
```

During `abox create`, overrides need the policy of the new sandbox, so a conflict or
an ambiguous endpoint can stop the copy after the sandbox was created. See
[failure and recovery](user-secrets.md#failure-and-recovery).
