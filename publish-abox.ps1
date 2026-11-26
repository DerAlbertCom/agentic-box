#!/usr/bin/env pwsh
<#
.SYNOPSIS
Packs the abox .NET tool and pushes it to the JFrog NuGet push feed.

.DESCRIPTION
Reads the latest published version of rwest-agentic-box from the feed, asks whether to
bump the patch or the minor version, then packs framework-dependent, ReadyToRun,
RID-specific tool packages plus a portable "any" package and the pointer package that
names them (see abox-cli/src/Abox/Abox.csproj). It pushes the RID packages first and
the pointer package last, so the feed never offers a version whose RID packages are
missing.

Uncommitted changes in abox-cli raise a warning and a question, because the package's
informational version names the commit it was built from.

Credentials come from the environment: ABOX_JFROG_USER and ABOX_JFROG_TOKEN (a JFrog
identity token). They are used to read the feed and to push.

.PARAMETER DryRun
Do everything but push, and print the packages that would be pushed.

.PARAMETER SkipTests
Do not run the abox test suites before packing.

.EXAMPLE
$env:ABOX_JFROG_USER = 'jdoe'; $env:ABOX_JFROG_TOKEN = '<token>'; ./publish-abox.ps1
#>
param(
    [switch] $DryRun,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$source = 'https://rwest.jfrog.io/artifactory/api/nuget/v3/mtiae-agentic-box-local'
$packageId = 'rwest-agentic-box'
$cliRoot = Join-Path $PSScriptRoot 'abox-cli'
$project = Join-Path $cliRoot 'src/Abox/Abox.csproj'

function Get-PublishedVersions([hashtable] $Headers) {
    $index = Invoke-RestMethod $source -Headers $Headers
    $baseAddress = $index.resources |
        Where-Object { $_.'@type' -like 'PackageBaseAddress/3.0.0*' } |
        Select-Object -First 1 -ExpandProperty '@id'
    $versionsUrl = "$($baseAddress.TrimEnd('/'))/$($packageId.ToLowerInvariant())/index.json"
    try {
        return (Invoke-RestMethod $versionsUrl -Headers $Headers).versions
    }
    catch [Microsoft.PowerShell.Commands.HttpResponseException] {
        if ($_.Exception.Response.StatusCode -eq 404) { return @() }
        throw
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

$user = $env:ABOX_JFROG_USER
$token = $env:ABOX_JFROG_TOKEN
if (-not $user -or -not $token) {
    throw 'Set ABOX_JFROG_USER and ABOX_JFROG_TOKEN (a JFrog identity token) for rwest.jfrog.io.'
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

foreach ($package in $packages) {
    dotnet nuget push $package --source $source --api-key "${user}:$token"
}
Write-Host "Published $packageId $version to $source"
