# Elastic API keys

The Elastic kits send an API key to their Elastic deployment:

| Service | Kit | Deployment |
| --- | --- | --- |
| `abox-cape-elastic` | `cape-elastic` | CAPE |
| `abox-mod-elastic-nprod` | `mod-squad-elastic` | MOD nonproduction |
| `abox-mod-elastic-prod` | `mod-squad-elastic` | MOD production |
| `abox-predictopus-elastic` | `predictopus-elastic` | Predictopus |

Create the API key in the Kibana of the deployment and paste its **encoded** form as
issued; do not base64-encode it again. The MOD and Predictopus keys need the
`feature_agentBuilder.read` privilege.
