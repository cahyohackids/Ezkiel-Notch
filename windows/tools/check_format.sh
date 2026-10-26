#!/bin/sh
# Proves the Windows app and the Mac app read each other's data.json:
# Windows writes → the Mac's NotchCore (compiled from Sources/NotchCore) reads and writes → Windows reads.
set -eu
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="${TMPDIR:-/tmp}/ezk-format-check"
rm -rf "$WORK" && mkdir -p "$WORK"
swiftc -O -o "$WORK/format-check" "$ROOT"/Sources/NotchCore/*.swift "$ROOT/windows/tools/format-check/main.swift"
cd "$ROOT/windows"
EZK_FORMAT_CHECK="$WORK" dotnet test EzkielNotch.Core.Tests --filter "FullyQualifiedName~MacRoundTrip" --logger "console;verbosity=normal"
