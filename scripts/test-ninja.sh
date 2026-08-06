#!/usr/bin/env bash
#
# The NinjaTrader-independent test suite, on Linux with Mono.
#
# Same sources and same assertions as the Windows csc script beside this one.
# It exists because the Windows job is the fragile half of CI: Windows runners
# are scarcer, bill at twice the rate of Linux ones, and when they cannot be
# allocated the job reports as CANCELLED rather than failed - which reads like
# a broken build when nothing is broken at all.
#
# It is also the toolchain the tests are actually written against day to day, so
# a green run here means what it says.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"

# From ninja/, because RuleUpdateTests reads the shipped rule book at the
# relative path Ballast/ballast-rules.txt - the same way the add-on does.
cd "$root/ninja"

out="$(mktemp -d)/ballast-tests.exe"

mcs -nologo -target:exe -out:"$out" \
    -r:System.Xml.Linq.dll -r:System.Drawing.dll -r:System.Net.Http.dll \
    Ballast/*.cs test/*.cs

mono "$out"
