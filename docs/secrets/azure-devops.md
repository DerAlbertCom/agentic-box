# Azure DevOps (`abox-azure-devops`)

The `azure-devops` kit sends this value to `dev.azure.com`, `pkgs.dev.azure.com` and
`vssps.dev.azure.com`, for git, the NuGet feeds and the Azure DevOps CLI, in the install
phase and at runtime.

The value is a personal access token (PAT) of Azure DevOps. Create it in Azure DevOps
under **User settings → Personal access tokens** for the organization the squad uses,
with the scopes the squad needs, such as **Code (Read & write)** and **Packaging
(Read)**.

The guided setup asks only for the PAT. The user name is fixed to `pat`, and abox stores
`base64("pat:<PAT>")`, the Basic authentication the proxy sends. `AZURE_DEVOPS_EXT_PAT`
inside the sandbox is a placeholder, not the stored credential.
