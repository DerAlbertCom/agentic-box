#!/bin/sh
# Sourced by shells and sandbox launchers, which may not inherit Docker ENV.
# mise installs the .NET SDKs into one shared root.
: "${HOME:=/home/agent}"
: "${DOTNET_ROOT:=$HOME/.local/share/mise/dotnet-root}"
: "${DOTNET_NOLOGO:=1}"
: "${DOTNET_CLI_TELEMETRY_OPTOUT:=1}"
case ":${PATH:-}:" in
    *":$DOTNET_ROOT:"*) ;;
    *) PATH="$DOTNET_ROOT:${PATH:-/usr/local/bin:/usr/bin:/bin}" ;;
esac
export HOME DOTNET_ROOT DOTNET_NOLOGO DOTNET_CLI_TELEMETRY_OPTOUT PATH
