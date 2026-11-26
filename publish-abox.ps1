#!/usr/bin/env pwsh
<#
.SYNOPSIS
Packs the abox .NET tool and pushes it to the JFrog NuGet push feed.

.DESCRIPTION
Reads the latest published version of RwestAgenticBox.Cli from the feed, asks whether to
bump the patch or the minor version, then packs framework-dependent, ReadyToRun,
RID-specific tool packages plus a portable "any" package and the pointer package that
names them (see abox-cli/src/Abox/Abox.csproj). It pushes the RID packages first and
the pointer package last, so the feed never offers a version whose RID packages are
missing.

Uncommitted changes in abox-cli raise a warning and a question, because the package's
informational version names the commit it was built from.

Credentials come from the environment: JFROG_USER and JFROG_TOKEN (a JFrog
identity token). They are used to read the feed and to push.

.PARAMETER DryRun
Do everything but push, and print the packages that would be pushed.

.PARAMETER SkipTests
Do not run the abox test suites before packing.

.EXAMPLE
$env:JFROG_USER = 'jdoe'; $env:JFROG_TOKEN = '<token>'; ./publish-abox.ps1
#>
param(
    [switch] $DryRun,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$source = 'https://rwest.jfrog.io/artifactory/api/nuget/v3/mtiae-agentic-box-local/index.json'
$packageId = 'RwestAgenticBox.Cli'
$cliRoot = Join-Path $PSScriptRoot 'abox-cli'
$project = Join-Path $cliRoot 'src/Abox/Abox.csproj'

function Get-FeedJson([string] $Url, [hashtable] $Headers) {
    $answer = Invoke-RestMethod $Url -Headers $Headers
    # Artifactory may answer with a non-JSON content type, which leaves a string.
    if ($answer -is [string]) { $answer = $answer | ConvertFrom-Json }
    return $answer
}

# Lists the versions through the registration resource, because Artifactory's service
# index offers no flat container (PackageBaseAddress).
function Get-PublishedVersions([hashtable] $Headers) {
    $index = Get-FeedJson $source $Headers
    $registrationBase = $index.resources |
        Where-Object { $_.'@type' -like 'RegistrationsBaseUrl*' } |
        Select-Object -First 1 -ExpandProperty '@id'
    if (-not $registrationBase) {
        $answer = $index | ConvertTo-Json -Depth 4 -Compress
        throw "$source is not a NuGet v3 service index with a RegistrationsBaseUrl resource. It answered: $($answer.Substring(0, [Math]::Min(500, $answer.Length)))"
    }
    $registrationUrl = "$($registrationBase.TrimEnd('/'))/$($packageId.ToLowerInvariant())/index.json"
    try {
        $registration = Get-FeedJson $registrationUrl $Headers
    }
    catch [Microsoft.PowerShell.Commands.HttpResponseException] {
        if ($_.Exception.Response.StatusCode -eq 404) { return @() }
        throw
    }
    foreach ($page in $registration.items) {
        # A page lists its versions inline or only links to them.
        $leaves = if ($page.items) { $page.items } else { (Get-FeedJson $page.'@id' $Headers).items }
        $leaves | ForEach-Object { $_.catalogEntry.version }
    }
}

function Get-LatestStableVersion([string[]] $Versions) {
    $latest = [semver]'0.0.0'
    foreach ($text in $Versions) {
        $version = $null
        if ([semver]::TryParse($text, [ref] $version) -and -not $version.PreReleaseLabel -and $version -gt $latest) {
            $latest = $version
        }
    }
    return $latest
}

function Confirm-Choice([string] $Title, [string] $Question) {
    $choices = '&No', '&Yes'
    return $Host.UI.PromptForChoice($Title, $Question, $choices, 0) -eq 1
}

$user = $env:JFROG_USER
$token = $env:JFROG_TOKEN
if (-not $user -or -not $token) {
    throw 'Set JFROG_USER and JFROG_TOKEN (a JFrog identity token) for rwest.jfrog.io.'
}
$credential = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("${user}:$token"))
$headers = @{ Authorization = "Basic $credential" }

$changes = git -C $PSScriptRoot status --porcelain -- abox-cli
if ($changes) {
    Write-Warning "abox-cli has uncommitted changes:`n$($changes -join "`n")"
    if (-not (Confirm-Choice 'Uncommitted changes' 'Publish anyway?')) { exit 1 }
}

$published = @(Get-PublishedVersions $headers)
$latest = Get-LatestStableVersion $published
$latestText = if ($published.Count) { "$latest" } else { 'none' }
$patch = [semver]::new($latest.Major, $latest.Minor, $latest.Patch + 1)
$minor = [semver]::new($latest.Major, $latest.Minor + 1, 0)
$bump = $Host.UI.PromptForChoice(
    'Version',
    "Latest published $packageId version: $latestText. Which version should be published?",
    @("&Patch $patch", "&Minor $minor", '&Cancel'),
    0
)
if ($bump -eq 2) { exit 1 }
$version = if ($bump -eq 0) { $patch } else { $minor }

if (-not $SkipTests) {
    dotnet test --solution (Join-Path $cliRoot 'agentic-box.slnx')
}

$output = Join-Path $PSScriptRoot "artifacts/abox/$version"
if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}
dotnet pack $project --configuration Release --output $output "-p:Version=$version"

$pointer = Join-Path $output "$packageId.$version.nupkg"
$ridPackages = Get-ChildItem $output -Filter '*.nupkg' |
    Where-Object FullName -ne $pointer |
    Sort-Object Name
$packages = @($ridPackages.FullName) + $pointer

if ($DryRun) {
    Write-Host "Dry run: would push $packageId $version to $source"
    $packages | ForEach-Object { Write-Host "  $(Split-Path $_ -Leaf)" }
    exit 0
}

# The push reads the service index before it sends the API key, and that read takes
# credentials only from a NuGet config. NuGet expands the %...% references from the
# environment, so the token is never written to disk.
$config = Join-Path $output 'NuGet.Config'
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="push" value="$source" />
  </packageSources>
  <packageSourceCredentials>
    <push>
      <add key="Username" value="%JFROG_USER%" />
      <add key="ClearTextPassword" value="%JFROG_TOKEN%" />
    </push>
  </packageSourceCredentials>
</configuration>
"@ | Set-Content $config

# --skip-duplicate lets a rerun after an interrupted push finish the missing packages:
# the pointer package goes last, so the feed still offers the same version again.
foreach ($package in $packages) {
    dotnet nuget push $package --source push --configfile $config --api-key "${user}:$token" --skip-duplicate
}
Write-Host "Published $packageId $version to $source"
