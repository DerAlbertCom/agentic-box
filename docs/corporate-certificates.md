# Corporate CA certificates

The RWE network inspects HTTPS, so every download and API call from a sandbox is signed
by an RWE CA. abox makes every workload image trust these CAs once, centrally. Kits,
workloads and team scripts do not add certificates or CA variables of their own.

This is about outbound HTTPS. The certificate apps in a sandbox serve is covered in
[Development HTTPS certificates](https-development-certificates.md).

## The CAs

The CAs live in
[`abox-cli/src/Abox.Core/Features/Build/Assets/certificates/`](../abox-cli/src/Abox.Core/Features/Build/Assets/certificates/),
one certificate per `.crt` file, and are embedded in `Abox.Core`:

| File | Certificate |
|---|---|
| `rwe-private-root-ca-r1.crt` | RWE Private Root CA R1 |
| `rwe-private-zica-2020.crt` | RWE Private ZICA 2020, issued by Root CA R1 |
| `rwe-private-root-ca-r23.crt` | RWE Private Root CA R23 |
| `rwe-private-auth-ica-2024.crt` | RWE-Private-Auth-ICA-2024, issued by Root CA R23 |
| `rwe-root-ca-corporate-2016.crt` | RWE Root CA Corporate 2016 |

Only RWE CAs belong here. Public roots already come from the template's
`ca-certificates` package.

## What the workload image does with them

The generated workload Dockerfile installs the CAs as root, before any download, into
`/usr/local/share/ca-certificates/abox/` and runs `update-ca-certificates`. The system
store `/etc/ssl/certs/ca-certificates.crt` then holds the public roots plus the RWE CAs.
apt, curl, git, OpenSSL and .NET read that store directly.

Some tools ship their own CA list and ignore the system store. The Dockerfile points
them at it with `ENV` lines. Image `ENV` applies to every later build step (mise tools
and workload scripts) and stays set in the sandbox at runtime and in kit lifecycle
hooks:

| Variable | Value | For |
|---|---|---|
| `MISE_EXTRA_CA_CERTS` | the system store | mise's own downloads |
| `NODE_EXTRA_CA_CERTS` | the system store | Node, npm, npm-based tools |
| `REQUESTS_CA_BUNDLE` | the system store | Python `requests`, pip, `az` |
| `AWS_CA_BUNDLE` | the system store | `aws` CLI and AWS SDKs |
| `UV_SYSTEM_CERTS` | `true` | uv, uv-backed mise tools such as `pipx:` and `azure-cli` |

The list is `TrustEnvironment` in
[`WorkloadKitGenerator.cs`](../abox-cli/src/Abox.Core/Features/Build/WorkloadKitGenerator.cs).

## Adding a CA

1. Export the CA as PEM, one certificate per file, LF line endings, no private key.
2. Save it in the certificates folder as `<name>.crt`.
3. Add its file name and SHA-256 fingerprint to the pin in
   `TheApprovedCorporateCasAreEmbeddedAndCopiedExactlyIntoTheBuildContext`
   (`WorkloadKitGeneratorTests.cs`). The pin makes every trust-anchor change explicit
   in review.
4. Run `abox build`. The workload image is rebuilt because its sources changed.

Get the fingerprint with
`openssl x509 -in <name>.crt -noout -fingerprint -sha256`, without the colons.

## A tool that still fails with an unknown issuer

An error such as `UnknownIssuer`, `unable to get local issuer certificate` or
`SELF_SIGNED_CERT_IN_CHAIN` means the tool ignores the system store. Find the variable
or setting the tool reads for a CA file, add it to `TrustEnvironment` and to the table
above, and rebuild. Do not set it in a kit, a workload entry or a team script, and do
not turn off TLS verification.
