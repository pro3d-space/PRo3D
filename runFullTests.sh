#!/usr/bin/env bash
# Runs everything: the whole Expecto suite (runAllTests.sh, including the HERA
# kernel tests) and then the Playwright UI tests (runUiTests.sh). Both run even
# if the first fails; the exit code is non-zero if either did.
cd "$(dirname "$0")"
bash ./runAllTests.sh
dotnetrc=$?
bash ./runUiTests.sh
uirc=$?
echo
echo "Expecto exit code $dotnetrc, UI tests exit code $uirc"
[ "$dotnetrc" -eq 0 ] && [ "$uirc" -eq 0 ]
