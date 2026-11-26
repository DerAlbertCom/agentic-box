# Host IDE launch/control options

Research date: 2026-11-19. Scope: Windows host VS Code, Rider and WebStorm controlling Linux **Docker Sandboxes (`sbx`)**, not ordinary Docker containers. Connectivity is assumed working; the primary question is how a host launcher selects the remote environment and exact project. No IDE was launched, no SSH setup was run, and no sandbox was changed for this research.

**Evidence labels:** **Documented** = official vendor contract linked below; **Observed** = repository inspection or caller-supplied read-only host observations; **Proposal/inference** = architectural recommendation, not implemented or compatibility-tested; **Unverified** = needs a bounded proof of concept.

## Recommendation

Use a **host-side launcher** with two separate inputs: sandbox identity and remote project path. For VS Code, invoke its documented `code --remote` or `--folder-uri` interface. For JetBrains, dispatch the documented Toolbox universal URI (the host already has a `jetbrains://` handler), or dispatch an actual backend-generated Gateway connection link after ensuring its handler exists. Do **not** substitute ordinary `rider <path>` or `webstorm <path>` commands: their documented path interface opens local files/projects, not an SSH remote-development target. [V1][V2][J1][J2][J3][J4][J5]

The universal Toolbox URI is the most promising general JetBrains host-control option here, but its `ideHint`/`projectHint` parameters are hints and its documented flow includes confirmation. Do not promise unattended exact-project opening, guaranteed window reuse, or tested `.sbx` compatibility. Prefer a generated connection link where an explicitly selected backend/project must be carried together. [J1][J2][J3]

## 1. Host commands and URLs

### VS Code: directly documented remote-folder CLI

**Documented:** Microsoft's exact examples are: [V1]

```text
code --remote ssh-remote+remote_server /code/my_project
code --folder-uri vscode-remote://ssh-remote+remote_server/code/folder.with.dot
```

The second form forces folder interpretation: `--remote` otherwise guesses file versus folder using the extension. A trailing slash also forces folder interpretation. Thus, for an existing sandbox named `demo` and a runtime-reported workspace `/workspace/repo`, realistic **adaptations of documented syntax**, to execute on Windows, are: [V1][D1][D2]

```powershell
code --new-window --remote ssh-remote+demo.sbx /workspace/repo/frontend/
code --new-window --folder-uri 'vscode-remote://ssh-remote+demo.sbx/workspace/repo/frontend'
```

Use the actual SSH alias and **Linux remote path**, not `D:\...`; encode URI path characters where necessary. `--new-window` (`-n`) and `--reuse-window` (`-r`) are documented host CLI options; the latter requests the last active window, not a named window/session. `--wait` waits for files to close, not for successful remote readiness. Host launcher exit alone is not proof of backend installation or project load. [V1][V2] (Last sentence: **inference**.)

**Documented URL alternative:** `vscode://vscode-remote/{remoteName}+{authority}/{path}`, e.g. an adaptation `vscode://vscode-remote/ssh-remote+demo.sbx/workspace/repo/frontend`. This is distinct from the `vscode-remote://...` folder URI passed to `--folder-uri`. Prefer the CLI when window policy matters. [V2]

Docker explicitly documents VS Code Remote-SSH against `<name>.sbx`; host pickers may not list the generated `Match` configuration, so supply the hostname explicitly. First connection installs VS Code Server inside the sandbox. [D1][D2]

### Rider/WebStorm: Toolbox universal URI, dispatched on the host

**Documented exact template:** [J3]

```text
jetbrains://gateway/ssh/environment?h=hostname&u=username&p=22&launchIde=true&ideHint=IU-252.23892.409&projectHint=path/to/project
```

The docs define `h` = host, `u` = user, `p` = SSH port, `launchIde` = launch boolean, `ideHint` = product code with optional build, and `projectHint` = project path. Official product codes are **RD for Rider** and **WS for WebStorm**. The browser asks to open Toolbox; Toolbox displays connection confirmation. [J3][J6]

For an already-working **ordinary loopback SSH endpoint** at port 2222, these are **constructed examples from that documented template**, not executed compatibility proof:

```text
jetbrains://gateway/ssh/environment?h=127.0.0.1&u=agent&p=2222&launchIde=true&ideHint=WS&projectHint=%2Fworkspace%2Frepo%2Ffrontend
jetbrains://gateway/ssh/environment?h=127.0.0.1&u=agent&p=2222&launchIde=true&ideHint=RD&projectHint=%2Fworkspace%2Frepo%2Fbackend%2FProduct.sln
```

A Windows host can dispatch a registered URI with PowerShell `Start-Process '<complete URI>'` (**OS dispatch example**, not a JetBrains CLI). Quote the entire URI so `&` and other characters do not become shell operators; percent-encode parameter values. Actual `.sbx` host/user handling must be validated rather than assuming ordinary `p=22` establishes a network endpoint. [D1][J3]

**Observed host prerequisites, supplied by caller:** `jetbrains://` is registered to `jetbrainsd.exe handleUri "%1"`; `vscode://` is registered to Code's URL opener. No handlers were found for `jetbrains-gateway://`, `jetbrains-client://`, or `vscode-insiders://`. The `code`, Rider, WebStorm and SSH CLIs are on PATH; Remote-SSH 0.128.0 is installed. Registration proves dispatch availability, **not** that the installed JetBrains version accepts this remote URI end to end. Do not assume Gateway is installed because Rider/WebStorm are installed.

**Toolbox CLI limitation:** the official Toolbox CLI page calls it work-in-progress and documents IDE installation/detection, including `java -jar tb-cli.jar install IU 231.8770.65`. It does **not** document a `remote-dev connect` command or project-opening flags. No such syntax is recommended here. The universal URI above is the documented automation entry point. [J7]

### Rider/WebStorm: backend-generated Gateway links

**Documented:** both products support a manually started Linux backend, specifically for custom orchestration. Run the following **inside the remote environment**, using that product's unpacked Linux IDE distribution: [J1][J2]

```text
<IDE>/bin/remote-dev-server.sh run /path_to_project/ --ssh-link-host host_server_address --ssh-link-user remote_side_user --ssh-link-port ssh_connection_port
```

The script defaults to port 22, system username and `hostname` when omitted; those defaults can be wrong for a sandbox reached through a host alias or forwarded port. These flags describe connection information embedded in the links; do not confuse them with a host-side `rider`/`webstorm` command or with SSH provisioning. The documented parameter discovery command is `remote-dev-server.sh --help`. [J1][J2]

On success the backend prints three links: [J1][J2]

* `tcp://127.0.0.1:...#...`: direct join endpoint; the address must actually be reachable, e.g. through forwarding. Do not expose it publicly just to open an IDE.
* `https://code-with-me.jetbrains.com/remoteDev#idePath=...&projectPath=...&host=...&port=22&user=...&type=ssh&deploy=false`: browser handoff to Gateway.
* `jetbrains-gateway://connect#idePath=...&projectPath=...&host=...&port=22&user=...&type=ssh&deploy=false`: direct Gateway dispatch.

The `#` fragment is intentional. Preserve and dispatch the **actual generated link**, rather than replacing it with a guessed query-string URL or inventing flags. Gateway also accepts these links in its connection UI and downloads the matching JetBrains Client. A connection is bound to a specific project; another project needs another connection. [J1][J2]

**Host implication:** install/register the Gateway handler before using direct `jetbrains-gateway://` dispatch on this machine, or use an available documented alternative. The HTTPS handoff may offer Gateway installation; it is not proof the current host can open the project. No reviewed source documents a universal new-window/reuse-window flag for these remote links. Repeated dispatch behavior is **unverified**. [J1][J2]

### Rider: exact solution, not merely its parent folder

**Documented explicit requirement:** Rider's remote-development FAQ says: “If your backend IDE is Rider, use the full path to the solution file (.sln) during the ‘Project directory’ step/selection.” This overrides an assumption that selecting the repository/backend directory reliably chooses the intended solution. [J8]

Use `/workspace/repo/backend/Product.sln` for the remote project selection. **Inference requiring verification:** applying that same solution-file path as `remote-dev-server.sh run /workspace/repo/backend/Product.sln ...` and as Toolbox `projectHint` is the appropriate candidate, but the reviewed server-script examples themselves show directories, not a Rider `.sln` invocation. Do not present that composition as independently documented or tested script syntax. [J1][J3][J8]

**Documented separately:** Rider supports `.slnx` for C#/VB/F# on Linux. **Unverified:** the remote selection flow, `projectHint`, and `remote-dev-server.sh run` accept an exact `.slnx` with equivalent semantics. General format support does not establish remote launcher compatibility. No `--solution`, `--project`, `--remote`, or other guessed Rider/WebStorm CLI flag is proposed. [J9]

## 2. Execution boundary and repository fit

**Observed:** `LauncherContext` already contains `Location`, `SandboxName`, `WorkspacePath`, and `Argv` in [`ILauncher.cs`](../../cli/src/Abox/Launchers/ILauncher.cs). Current launchers are `shell` and `herdr-app`; [`implementation.md`](../implementation.md#abox-run-launcher---location-path-----argv) describes host launch and runtime workspace discovery.

**Proposal:** thin host IDE launchers can share a connection capability and then select the editor-specific CLI/URI. Keep `--location` as **sandbox identity selection**. It must not be overloaded to select a nested IDE folder or Rider solution: that would resolve another sandbox. A separate project target can be designed later; no new public option or argument contract is established by this research.

Launching `code`/a URI **on Windows** steers Windows apps. Running the same text **inside Linux** does not inherently invoke host apps. If future requirements allow a sandbox process to request host opening, it needs an explicitly authorized narrow host-side handoff (or a human copying a command/link). This is a trust-boundary observation, **not** a recommendation to build a broker now. Do not expose arbitrary host command execution or accept unrestricted protocol URLs from sandbox content. (Architectural **inference** from the separate host/client and remote/backend processes in [V3][J8].)

**Required environment caveat:** the repo's [HTTPS launch contract](../https-development-certificates.md#launch-propagation-and-existing-parents) injects `ASPNETCORE_Kestrel__Certificates__Default__Path`, `ASPNETCORE_Kestrel__Certificates__Default__Password`, and `NODE_EXTRA_CA_CERTS` through `SbxClient.ExecAsync`/`abox pipe`. Native SSH and self-managed SSH sessions bypass that bootstrap. Starting `sshd` through abox does not establish that its child sessions preserve that environment. Docker explicitly ignores client SSH environment requests. A future remote-IDE integration must ensure fresh backend/tool processes cross an equivalent **supported protected bootstrap**, including stale-parent/integrity handling; shell profile guesses and a successful connection do not establish HTTPS readiness. [D1] (**Observed repo contract**, **implementation requirement**, not solved here.)

## 3. Minimal transport context—not the host-control solution

**Documented current preferred candidate:** native SSH uses `sbx setup ssh`, then `ssh demo.sbx`. It writes managed config included by the host SSH config, uses `ProxyCommand "sbx" ssh proxy ...` over the Windows named pipe, requires active Docker login, needs no client key, network listener or sandbox `sshd`, starts stopped sandboxes, and verifies the daemon host key on each connection. Managed config must be refreshed after upgrades; v0.54.0 specifically hardens crafted-host/user handling. The setup command remains **experimental**. Do not hand-copy an older generated block. [D1][D3][D4]

The caller observed client/server **0.54.0**, revision `4bec4a5ce093466e54b034146965b143d065dc09`; the repo minimum is 0.51.0. Native SSH is documented for VS Code, not specifically tested here for JetBrains. Rider/WebStorm deployment documents require SFTP. JetBrains supports `ProxyCommand`, but warns it applies only supported SSH settings even with the OpenSSH parser (`ssh -G`); its legacy list lacks `Match` and `KnownHostsCommand`. Confirm exact installed parser/backend and daemon SFTP compatibility before calling native SSH a common verified endpoint. [D2][J1][J2][J10]

**Fallback proposal:** conventional key-authenticated SSH through a loopback publication can give all clients a plain host/user/port endpoint. The original repo concept uses `sbx ports <sandbox> --publish 127.0.0.1::22`; 0.54 retains it but deprecates it in favor of `sbx port publish`. Caller-observed 0.54 help gives `sbx port publish <sandbox> 127.0.0.1::22/tcp4`. Discover the assigned port; do not assume port 22 on the host. This is a fallback design, **not a tested .51/.54 integration**. [D3][../concept.md](../concept.md#sbx-mapping-internal)

Conventional SSH would require image/kit installation of sshd plus SFTP, per-instance server host keys, root-owned daemon startup/restart/readiness handling, a non-root application login, protected host private key and sandbox `authorized_keys`, and permitted SSH forwarding. Disallow password/root login and agent forwarding unless specifically needed. On replacement, verify the new host key through trusted runtime access before changing only the owned known-hosts entry; never fix mismatches with globally disabled checking. These are **proposed operational requirements**, based on normal SSH deployment/security controls, not evidence that abox already does them. [V3][J1][J2][J10]

**Observed:** [`mti-ca.workload`](../../teams/mti-ca/mti-ca.workload#L17) already lists `openssh-server`, but no sshd launcher/bootstrap was found in `cli/src`. [`SbxApplicationPorts.cs`](../../cli/src/Abox/Sbx/SbxApplicationPorts.cs) applies application-only mapping/offset assumptions and relays; do not reuse it blindly as a generic SSH publisher.

## 4. Alternatives, requirements and security

* **Local shared-file editing:** ordinary `code D:\repo`, `rider D:\repo\Product.sln`, or `webstorm D:\repo\frontend` is a valid different choice. It does not move indexing, language servers, builds or debugging into the sandbox; local SDKs remain relevant. Shared files alone are not remote development. [V2][J4][J5] (**Execution-location conclusion:** inference.)
* **Remote debugging only:** an independently configured debugger can attach remotely while the IDE/project stays local; it does not supply the remote project/backend launch contract. Protocol, source mapping and runtime-specific setup remain separate. [V3]
* **Dev Containers:** VS Code documents Remote-SSH followed by “Reopen in Container” using Docker on the remote host. This can create/select a container *inside* the sandbox; it does not prove that `sbx` microVMs are ordinary host-Docker attach targets. No generic native Dev Containers attach-to-sbx compatibility was established. [V3][D1]
* **Custom `ProxyCommand abox pipe`:** OpenSSH proxy transport needs an SSH-speaking peer, not an arbitrary shell's stdin/stdout. `abox pipe ... -- sh` alone is not an SSH server. An sshd stdio adapter would be another unverified transport design, not required for host steering; native `sbx ssh proxy` already supplies Docker's documented peer. [D1][J10] (**Protocol reasoning:** inference.)
* **WSL is different:** its remote provider opens a WSL distribution, not a Docker Sandbox. It does not select the same backend merely because both run Linux. [V2][J1][J2]
* **Downloads/licensing:** VS Code needs Remote-SSH and installs its server remotely; downloads/extensions may require allowed Microsoft/CDN endpoints. JetBrains needs a Linux backend plus a matching downloaded Client; an existing Windows IDE is not that Linux backend. Its FAQ requires the corresponding IDE license and lists JetBrains download URLs; don't assume Gateway/Client bypass commercial licensing. Check applicable IDE/extension terms and enterprise network policy. [V3][J1][J2][J8]
* **Security:** IDE backends/plugins execute with sandbox-user privileges and can access shared workspace files. Do not forward the host SSH agent or place host private keys in the sandbox. Keep forwarded listeners on loopback; Docker warns non-loopback client bindings can expose services to other machines. Treat generated join links as sensitive connection material, not public log content. [V3][D1] (**Least-privilege/link handling:** recommendation.)

## 5. Bounded proof-of-concept checks

No compatibility proof is claimed. With explicit permission, check only:

1. VS Code: exact nested-folder CLI, dotted/spaced paths, new/reused window, and distinguish command acceptance from actual remote load.
2. Toolbox: registered `jetbrains://` universal URI with WS directory and RD **full `.sln` path**; verify whether hints select the requested project/build and what confirmation remains.
3. If using generated Gateway links: register the required handler, dispatch the actual fragment-preserving link, and verify exact Rider solution selection; separately test `.slnx`. Check repeated-open behavior rather than inventing a reuse flag.
4. Only if native `.sbx` is selected: JetBrains parser/proxy/host-key/SFTP compatibility. A working `ssh` shell is insufficient proof.
5. Before claiming functional development: fresh backend, terminal, build/test and debug children receive the protected abox HTTPS bootstrap environment without leaking certificate passwords or relying on profiles.

## Official sources

* [V1] [Microsoft: Remote Development Tips—connect from terminal](https://code.visualstudio.com/docs/remote/troubleshooting#_connect-to-a-remote-host-from-the-terminal).
* [V2] [Microsoft: VS Code command-line interface](https://code.visualstudio.com/docs/configure/command-line).
* [V3] [Microsoft: Remote Development using SSH](https://code.visualstudio.com/docs/remote/ssh).
* [J1] [JetBrains Rider 2026.3: Connect and work with Gateway](https://www.jetbrains.com/help/rider/Remote_development-a.html).
* [J2] [JetBrains WebStorm 2026.3: Connect and work with Gateway](https://www.jetbrains.com/help/webstorm/remote-development-a.html).
* [J3] [JetBrains Toolbox: Manage remote projects—universal link](https://www.jetbrains.com/help/toolbox-app/gettings-started-with-ssh.html#use_a_universal_link_to_access_a_remote_environment).
* [J4] [Rider: command-line interface](https://www.jetbrains.com/help/rider/Working_with_the_IDE_Features_from_Command_Line.html).
* [J5] [WebStorm: command-line interface](https://www.jetbrains.com/help/webstorm/working-with-the-ide-features-from-command-line.html).
* [J6] [JetBrains Marketplace: product codes](https://plugins.jetbrains.com/docs/marketplace/product-codes.html).
* [J7] [JetBrains Toolbox App CLI](https://www.jetbrains.com/help/toolbox-app/toolbox-app-cli.html).
* [J8] [Rider: FAQ about remote development](https://www.jetbrains.com/help/rider/FAQ_about_remote_development.html).
* [J9] [Rider: supported project formats](https://www.jetbrains.com/help/rider/Supported_project_file_formats.html).
* [J10] [Rider: supported SSH directives and parsers](https://www.jetbrains.com/help/rider/Create_SSH_configurations.html#openssh-directives).
* [D1] [Docker Sandboxes: editor and app integrations](https://docs.docker.com/ai/sandboxes/integrations/).
* [D2] [Docker Sandboxes: VS Code integration](https://docs.docker.com/ai/sandboxes/integrations/vscode/).
* [D3] [Docker Sandboxes: 0.54.0 release notes](https://docs.docker.com/ai/sandboxes/release-notes/#0540).
* [D4] [Docker: sbx setup ssh—experimental command reference](https://docs.docker.com/reference/cli/sbx/setup/ssh/).
