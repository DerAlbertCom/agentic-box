# CAPE host service access

`host-access-cape` lets a sandbox reach the services that the CAPE container stack
(`Carmen.Shared/scripts/containers/docker-compose.yaml`) publishes on the host, plus
Rider MCP. It does not start services and carries no credentials. CAPE squads inherit
it from `cape.abox`; MOD, Predictopus and BAU do not.

| Service | Host port | Container port |
| --- | ---: | ---: |
| postgres | 8004 | 5432 |
| oracle | 1521 | 1521 |
| database-mcp | 8005 | 8080 |
| localstack | 8006 | 4566 |
| s3manager | 8007 | 8080 |
| jaeger | 13133, 16686, 4317, 4318 | same |
| Rider MCP | 65520–65523 | — |

Keep this table and `host-access-cape.yaml` in line with the compose file.

## Connection addresses

Inside the sandbox, connect to `host.docker.internal:<host port>`:

- LocalStack: `http://host.docker.internal:8006`
- PostgreSQL: `host.docker.internal:8004`
- Oracle: `host.docker.internal:1521`

Do not use `localhost`, Compose service names or the container ports.

## How it works

The sandbox proxy translates `host.docker.internal` to `localhost` before it checks
the network policy (see Docker's [host-service workflow][host-services]). The kit
therefore allows individual `localhost:<port>` entries with `network-policy@1`. The
host's own network policy still applies, and a deny wins.

abox also uses these entries to decide which host ports it may rewrite endpoints to
when it copies [user-secrets](../../docs/user-secrets.md).

The team's base squad allows open egress, so the entries are not an egress
restriction.

## Applying and checking

The kit takes effect when the sandbox is created. For an existing sandbox, rebuild
the squad and recreate the sandbox.

The services must be running on the host. A quick check from the sandbox:

```sh
curl --fail --silent --show-error --max-time 5 \
  http://host.docker.internal:8006/_localstack/health --output /dev/null
```

[host-services]: https://docs.docker.com/ai/sandboxes/workflows/development/#accessing-host-services-from-a-sandbox
