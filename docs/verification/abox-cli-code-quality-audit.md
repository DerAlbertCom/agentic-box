# Abox CLI code-quality audit

## Scope and method

Audit of the current working tree, anchored to HEAD
`43167ab94bd17665d98f67f2de28f20fbfe0fa4c`, performed on 2026-11-16.
Existing working-tree modifications were reviewed as current code, not reverted or
attributed to this audit. No production or test source was changed.

All production folders were inspected, including C#, embedded Python and shell
receivers, composition, project configuration and the Build slice. Test structure
was inventoried across all folders; representative behavior tests were inspected,
not every assertion in every test. This is a maintainability audit, not a proof of
correctness, security certification or live sbx/registry verification.

Sources of architectural authority: `CODING_STANDARDS.md`, the glossary in
`docs/concept.md`, `docs/implementation.md`, and feature/verification documents.
There is no root glossary or ADR directory in this checkout. The diff-oriented
review workflow was adapted to a whole-current-code audit: there is no originating
feature spec against which to claim implementation completeness.

Clean-code MCP operations used: `repo_score`, `hotspots`, targeted `check_file`
with function metrics, and `coupled_files`. The certificate adapter's history has
only one relevant commit and yielded no coupling partners; absence of history is
not evidence of architectural independence.

## Measured baseline

Scores are LOC-weighted tool measurements, not judgments about correctness.

| Scope scanned by repository scoring | Files | Tool LOC | Score | Critical | Warning | Good |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Repository, including verification probes | 249 | 36,620 | 5.3 | 32 | 88 | 129 |
| CLI source and tests | 242 | 35,172 | 5.3 | 31 | 87 | 124 |
| CLI production | 122 | 11,981 | 5.4 | 17 | 35 | 70 |
| CLI tests | 120 | 23,191 | 5.3 | 14 | 52 | 54 |

**Coverage limitation:** the scanner silently excludes directories named `Build`.
An explicit check of the original BuildService returns “Not an analysable source
file.” All three Build source files were reviewed manually. Unchanged copies in a
scratch directory without that excluded path segment scored BuildCommand 8.0,
BuildFeature 10.0 and BuildService 7.7. Those isolated scores lack repository-wide
duplication context and are not folded into the table. Embedded shell is manually
reviewed, not scored. Repository scoring also includes seven verification files
outside the CLI; its 32 critical files must not be reported as 32 CLI files.

Most frequent repository findings: long test methods 264, boolean arguments 139,
deep nesting 66, duplication 63, cyclomatic complexity 48, large methods 47,
large assertion blocks 44, cognitive complexity 35 and brain methods 19.
These are heuristic counts; several findings can describe the same method.

Top change-frequency hotspots (38 commits analyzed): RunService 81.0,
SquadResolver 63.8, CreateCommand 62.0, PublishService 45.0, FolderConfig 29.2,
UserSecretsCopy.Target 26.0, YamlReader 24.0 and PruneService 23.7.
The short history limits predictive value. CreateCommand and YamlReader illustrate
why hotspot ranking must be followed by source inspection.

## Findings worth acting on

These are prioritized maintainability findings unless explicitly described as
observable robustness problems. References are to this audit snapshot.

### High: language parsing hides recognition and preservation rules

- `cli/src/Abox/UserSecrets/EndpointAdaptation.cs:12-127,248-327` combines format
  dispatch and mutable connection-field scanning. Cognitive complexity is 56 for
  RewriteEndpoint and 60 for ConnectionFields. Explicit internal parse outcomes
  should distinguish unrecognized, recognized/unchanged and unsafe mapped-local
  syntax, retaining original spans and partial recognition.
- `cli/src/Abox/UserSecrets/EndpointAdaptation.Oracle.cs:96-301,318-486` combines
  outer quoting/source mapping, descriptor recursion and Easy Connect/options.
  Easy Connect collection has cognitive complexity 52; descriptor ReadNode 40.
  Separate these responsibilities behind the existing pure adaptation interface.
  Do not normalize credential-bearing strings with generic connection builders.

### High: certificate and port receiver lifecycles are hard to inspect

- `cli/src/Abox/Sbx/development_certificate.py:292-392`: run has cognitive
  complexity 38 and nesting four; dispatch, locking, staging, activation and
  cleanup are interleaved. `node_bundle:206-257` separately mixes caller-input
  validation, protected source handling and bundle persistence.
- `cli/src/Abox/Sbx/SbxClient.DevelopmentCertificate.cs:243-344` combines payload
  validation/framing, transfer, bounded cleanup and failure reporting (100 tool
  LOC, cognitive complexity 16). Isolate private stages and named protocol codes.
- `cli/src/Abox/Sbx/application_ports.py:145-202,227-275`: listener startup and
  ensure each have cognitive complexity 27. Separate resource acquisition,
  readiness and owned-state planning while retaining single-owner cleanup.
- `cli/src/Abox/Sbx/SbxClient.ApplicationPorts.cs:10-95,98-187` mixes inventory,
  relay request/response validation and publication confirmation. Keep the public
  adapter interface stable while clarifying its private stages.

### High: discovery mixes I/O applicability policy with parsing

- `cli/src/Abox/ApplicationPorts/AppPortDiscovery.cs:16-164`: DiscoverAsync has
  cognitive complexity 34 and 145 tool LOC. Isolate bounded reads, static ASP.NET
  applicability classification and contextual format dispatch. Keep one walk.
- `cli/src/Abox/ApplicationPorts/AngularStartScript.cs:10-101`: separate literal
  tokenization, accepted command recognition and port-argument interpretation.
  Do not broaden the deliberately restricted shell grammar.
- `cli/src/Abox/Https/HostDevelopmentCertificate.cs:241-319`: installed tool
  discovery combines SDK listing, DLL traversal and PE metadata inspection
  (cognitive complexity 37, nesting six). Hide discovery details behind a small
  internal operation; retain metadata-only inspection and exact-identity export.

### High: publication and published metadata loading change for too many reasons

- `cli/src/Abox/Features/Publish/PublishService.cs:168-345,362-539` owns serialized
  descriptor shapes, push arguments/digest-file decoding, recursive OCI inspection
  and argument validation as well as orchestration. Use explicit internal
  serialized shapes and a cohesive verified-publication implementation.
- `cli/src/Abox/Sandboxes/PublishedSquadSource.cs:125-180,185-367` combines platform
  resolution, runtime schema validation and ordered Docker-save archive/layer
  interpretation. Separate archive interpretation and metadata decoding privately.
- Both modules repeat OCI parsing. Share only identical low-level primitives in
  the existing OciRegistry module: publication verifies every runnable platform;
  creation selects one host Linux platform. Those policies must remain distinct.

### High: Herdr protocol handling has concrete robustness weaknesses

`cli/src/Abox/Launchers/HerdrAppLauncher.cs:94-106` treats listing failure as absence
and accepts any output containing the sandbox name. A longer/lookalike name can
satisfy that test. `:137-141` ignores the app-start result. `:158-165` treats missing
or wrong-type errors as healthy and does not catch non-object JSON access failures.
Establish supported response shapes, parse exact identity and reject malformed or
failed discovery with actionable diagnostics. The file scores 10: metrics miss this.
This is targeted behavior hardening, not a behavior-preserving refactor.

### Medium: policy and configuration validation need named stages

- `cli/src/Abox/Configuration/FolderConfig.cs:63-143`: separate I/O/error handling
  from pure strict decoding (cognitive complexity 19).
- `cli/src/Abox/Configuration/UserSecretSelection.cs:97-170`: separate matching
  from validated accumulation and whole-store/key-union merging.
- `cli/src/Abox/Sbx/UserSecretsNetworkPolicy.cs:9-65`: distinguish kit-provisioned
  from persistent local-rule recognition without relaxing either schema.
- `cli/src/Abox/Sbx/SbxClient.Settings.cs:105-124`: replace the twelve-operator
  validation expression with explicit record/array decoding; retain evaluated
  post-write verification, repository segment matching and cross-process locking.

### Medium: orchestration hides smaller cohesive decisions

- `cli/src/Abox/UserSecrets/UserSecretsCopy.Target.cs:14-120`: extract pure
  per-store construction from policy discovery and all-destination validation.
- `cli/src/Abox/UserSecrets/ProjectSettings.cs:72-175,200-274`: distinguish selected
  project association, bounded traversal and effective appsettings loading.
- `cli/src/Abox/Features/Run/RunService.cs:98-150`: recovery-hint resolution mixes
  provenance/default precedence, optional discovery, diagnostics and rendering.
  Isolate a slice-local operation; keep replacement orchestration explicit.
- `cli/src/Abox/Features/Prune/PruneService.cs:135-225`: precompute group retention
  facts instead of interleaving grouping and per-image classification. Repeated
  scans are structurally apparent; no performance regression was measured.
- `cli/src/Abox/Kits/SquadResolver.cs:154-353,355-534`: improve discovery versus
  composition locality and repeated origin tracking, retaining declaration-specific
  validation rather than inventing a generic merge engine.

### Medium/lower: technical clarity and meaningful test gaps

- `cli/src/Abox/Infrastructure/ProcessOutput.cs:40-141`: separate credential
  collection and raw/decoded URI variants while preserving longest-first redaction.
- `cli/src/Abox/Infrastructure/ProtectedTemporaryDirectory.cs:16-89`: isolate
  platform-specific creation/verification, keeping partial-failure cleanup obvious.
- `cli/src/Abox/LocalState/BuildStore.cs:22-132`: consistently represent internal
  team/squad/tag identity; clarify snapshot and thin-runtime-kit phases locally.
  Existing formats and ownership rules must not change.
- `cli/src/Abox/Health/Preflight.cs:84-93,113-130`: authentication and post-login
  re-diagnosis lack focused tests. Cover interaction, refusal, failure and cancellation
  through the existing adapter and console seams; no new abstraction is needed.

## Folder coverage and disposition

Production scores below are weighted over files actually scored, not minimum-file
scores. Corresponding tests belong with each behavior ticket, not a horizontal
“rewrite all tests” project.

| Production folder | Score | Disposition |
| --- | ---: | --- |
| Root entry point/exception | 10.0 | Retain |
| Cli | 10.0 | Retain composition and deferred configuration; regression coverage |
| Features shared error handling | 10.0 | Retain |
| Features/Build | Excluded | Manually reviewed; cohesive; retain |
| Features/Create | 4.3 | Mostly option boilerplate; retain lifecycle delegation |
| Features/Ports | 7.1 | Retain thin command; improve fixtures with discovery tickets |
| Features/Prune | 3.2 | Simplify pure retention planning |
| Features/Publish | 1.7 | Two bounded publication tickets |
| Features/Run | 3.9 | Isolate recovery-hint logic |
| Features/Sandbox | 8.3 | Retain explicit-name exec/pipe semantics |
| Features/Secrets | 7.7 | Retain cohesive prompting/list/set/remove |
| Features/UserSecrets | 5.2 | Retain command; improve behavior fixtures with shared-feature tickets |
| Configuration | 5.5 | Strict decoding and selection accumulation |
| ApplicationPorts | 3.9 | Discovery and literal Angular parsing |
| Https | 1.7 | Installed-tool discovery and export stages |
| Infrastructure | 7.2 | Redaction and private staging; retain wrappers |
| Kits | 7.1 | Resolver locality; retain small models/generators |
| LocalState | 8.6 | Low-priority local clarity and fixture disposal |
| Naming | 9.8 | Retain pure current/legacy naming and distinct grammars |
| Launchers | 10.0 | Harden Herdr protocol; retain launcher architecture |
| Sandboxes | 5.0 | Published loading; retain resolver and explicit creator ordering |
| Sbx | 4.7 | Four bounded policy/relay/certificate improvements |
| UserSecrets | 4.1 | Four bounded parsing/association/target improvements |

All mirrored test folders and Support were included in structural review. Existing
organization has recently been made intentional; do not reorganize it wholesale.
Focused repairs: command-selection partial currently under shared UserSecrets
belongs with Features/UserSecrets; direct discovery tests under Https belong with
ApplicationPorts. LocalState fixtures owning TempDirectory need local disposal.
Large appsettings/Oracle/split-setting/ports/sandbox tests need scenario-specific
setup helpers, not assertion deletion, a universal builder, or tests of new privates.

## False positives and safeguards

- A long command constructor containing declarative options is not automatically
  a god object. Build manifest projection and generated Dockerfile text are likewise
  not reasons for new abstraction hierarchies.
- Independent accessors/static factories do not make AboxConfig or YamlReader
  incohesive. Boolean command flags and explicit platform-test switches are useful.
- FileExistsError handling for idempotent mkdir, socket fallback/cleanup handlers,
  and sensitive diagnostic suppression are deliberate. Never add raw logging just
  to remove a swallowed-exception finding.
- Root/public-certificate trust and non-root/private activation are distinct trust
  contexts. Do not share privileged and unprivileged receiver implementations merely
  to remove duplicate mount checks.
- Preserve host-input validation before runtime/destructive work, exact identities,
  immutable digest pinning, original-value deny checks, all-store validation before
  writes, per-file atomicity, protected filesystem checks and cancellation semantics.
- Scores should improve where real complexity is reduced. Do not target a universal
  9/10 by moving complexity into unscanned paths or deleting meaningful tests.

## Verification and publication status

The ambient SDK is 11.0.100. Initial no-restore build encountered host-generated
Windows NuGet fallback paths. An isolated restore/build using the public NuGet
source and an artifacts directory under `/tmp/opencode` succeeded with zero warnings
and errors, without changing project files. The ordinary `dotnet test` invocation
found no test projects; the documented direct xUnit executable was used instead.

The first isolated executable run discovered 2,078 cases: 77 failed, 37 skipped.
All failures were fixture checkout-root assumptions (73 explicit checkout errors,
four null-reference errors), not established product failures. The isolated output
was then given fixture-discovery links to the checkout marker, verification fixtures
and kit sources, without changing those sources or the test implementation.
The final Debug executable run discovered **2,078 cases: 2,041 passed, zero failed,
37 skipped, zero errors** (45.101 seconds). No Release run was performed.
The links fix this audit's execution layout, not the underlying fixture portability
issue: certificate, metadata and kit lookup each assume output beneath a checkout.

Reproduction commands used for the successful isolated build and execution:

```sh
dotnet restore cli/agentbox.slnx --verbosity minimal \
  --source https://api.nuget.org/v3/index.json \
  -p:UseArtifactsOutput=true -p:ArtifactsPath=/tmp/opencode/abox-quality-artifacts
dotnet build cli/agentbox.slnx --no-restore --verbosity minimal \
  -p:UseArtifactsOutput=true -p:ArtifactsPath=/tmp/opencode/abox-quality-artifacts
dotnet /tmp/opencode/abox-quality-artifacts/bin/Abox.Tests/debug/Abox.Tests.dll \
  -noLogo -maxThreads 2 -xml /tmp/opencode/abox-quality-debug-tests-final.xml
```

For this unchanged harness, the artifacts directory additionally contains symlinks
named `CODING_STANDARDS.md`, `docs` and `kits` pointing to their checkout equivalents.
The successful run's XML and log are under `/tmp/opencode`, with prefix
`abox-quality-debug-tests-final`. No repository SDK or NuGet settings were changed.

No live sandbox, host certificate export, real user-secret reads or publication was
performed. Platform skips are not passing evidence.

GitHub is the configured tracker. Initial authentication failed, but the user
restored access and approved the spec, testing seams, 24 tickets and dependency
edges. The spec was published as
[issue #49](https://github.com/DerAlbertCom/agentbox/issues/49), with
`ready-for-agent`. All 24 children were published as #50–73 with the same label
and verified as native sub-issues of #49. Native blocking edges are #63 → #64 and
#68 → #69; the other 22 tickets are initially startable. The local publication index
is `.scratch/abox-cli-quality/published.md`. Existing issue bodies and statuses
were not changed. No implementation was started.

Before publication, the full bodies/comments of existing #3, #47, #21, #22 and #23
were read. The new program builds on the completed prior audit/OCI/endpoint work,
not a reopening of it. #47 still records an unresolved retry-command shell-quoting
defect, a strict fresh-create preflight policy concern and deferred live evidence.
Those existing gates are not fixed or superseded by these maintenance tickets;
the new spec explicitly preserves that distinction.
