#!/usr/bin/env bash
# Downloads exactly the HERA SPICE kernels the test suite needs into <dest>/kernels,
# laid out like the ESA server so the meta-kernels' relative $KERNELS paths resolve.
#
#   scripts/fetch-spice-kernels.sh [dest]        # default dest: ./spice
#   PRO3D_SPICE_KERNELS=<dest> ./runAllTests.sh  # then point the tests at it
#
# What it fetches is the transitive closure of the meta-kernels pinned in
# scripts/spice-kernels.pins -- ~120 files, ~1.3 GB. The full ESA dataset is ~11 GB
# and its git repo carries every version ever published, so neither is worth cloning
# for a test run; the meta-kernels already name precisely what SPICE will load.
#
# Re-running is cheap and offline: a completed tree records a manifest, and a rerun
# only re-reads that plus local file sizes. Only a missing/short/absent file costs a
# request, which is what makes this safe to run unconditionally after a CI cache
# restore -- a half-populated cache heals itself instead of failing the run later
# inside CSPICE.
#
# Env: PRO3D_SPICE_BASE_URL (mirror to fetch from), PRO3D_SPICE_JOBS (parallel
# downloads, default 8).
set -euo pipefail

BASE_URL="${PRO3D_SPICE_BASE_URL:-https://spiftp.esac.esa.int/data/SPICE/HERA/kernels}"
JOBS="${PRO3D_SPICE_JOBS:-8}"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
pins_file="$script_dir/spice-kernels.pins"

file_size() { wc -c < "$1" | tr -d ' '; }

# --retry-all-errors (curl 7.71+) is what makes a retry cover a mid-transfer reset,
# not just a connection failure. Older curl rejects the unknown option outright, so
# ask before using it rather than failing every download on an old runner image.
retry_opts() {
    local opts="--retry 5 --retry-delay 2 --retry-connrefused --speed-limit 1024 --speed-time 60"
    if curl --help all 2>/dev/null | grep -q -- '--retry-all-errors'; then
        opts="$opts --retry-all-errors"
    fi
    echo "$opts"
}

# Once per process: each worker re-enters this script, so this costs one probe per
# download, not one per curl call.
RETRY_OPTS="$(retry_opts)"

# Size the remote file so a rerun can tell "already have it" from "have half of it".
# Empty output means "unknown" -- a server that refuses HEAD is a reason to download
# and skip the size check, never a reason to fail the run, hence the `|| true`.
remote_size() {
    local headers
    headers="$(curl -fsSIL --max-time 60 $RETRY_OPTS "$1" 2>/dev/null || true)"
    printf '%s\n' "$headers" \
        | tr -d '\r' \
        | awk 'BEGIN { IGNORECASE = 1 } /^content-length:/ { n = $2 } END { if (n != "") print n }'
}

# One download, in its own process so xargs can run several. Re-entering the script
# this way (rather than exporting a function) keeps it working under any /bin/sh-ish
# xargs and on Git Bash.
fetch_one() {
    local kernels_dir="$1" rel="$2"
    local out="$kernels_dir/$rel" url="$BASE_URL/$rel"
    mkdir -p "$(dirname "$out")"

    local remote
    remote="$(remote_size "$url")"

    if [ -f "$out" ] && [ -n "$remote" ]; then
        local have
        have="$(file_size "$out")"
        if [ "$have" = "$remote" ]; then
            return 0
        fi
        # Resuming onto a file that is already longer than the remote one cannot
        # produce the remote file -- it is corrupt, or the pin moved. Start over.
        if [ "$have" -gt "$remote" ]; then
            rm -f "$out"
        fi
    fi

    echo "  fetching $rel"
    curl -fsSL --max-time 3600 $RETRY_OPTS -C - -o "$out" "$url"

    if [ -n "$remote" ] && [ "$(file_size "$out")" != "$remote" ]; then
        echo "error: $rel is $(file_size "$out") bytes, server says $remote" >&2
        return 1
    fi
}

if [ "${1:-}" = "--fetch-one" ]; then
    fetch_one "$2" "$3"
    exit $?
fi

dest="${1:-./spice}"
kernels_dir="$dest/kernels"
manifest="$kernels_dir/.pro3d-kernels-manifest"

# Every quoted entry in KERNELS_TO_LOAD, as a path below kernels/. The count check
# guards the one way this parse can silently under-deliver: a meta-kernel that wraps
# a long path across two quoted strings would yield fewer paths than quoted lines,
# and the missing kernel would only surface much later as a puzzling SPICE error.
kernel_list() {
    local mk="$1"
    local paths quoted
    paths="$(tr -d '\r' < "$mk" | sed -n "s|.*\$KERNELS/\([^']*\)'.*|\1|p")"
    quoted="$(tr -d '\r' < "$mk" | awk '/KERNELS_TO_LOAD/, /^ *\)/' | grep -c "'" || true)"
    if [ "$(printf '%s\n' "$paths" | grep -c .)" != "$quoted" ]; then
        echo "error: $mk lists $quoted kernels but $(printf '%s\n' "$paths" | grep -c .) parsed" >&2
        echo "       (a wrapped path in KERNELS_TO_LOAD? this parser cannot join those)" >&2
        return 1
    fi
    printf '%s\n' "$paths"
}

# Where a pinned meta-kernel lives below kernels/. ESA keeps the newest release in
# mk/ and moves it to mk/former_versions/ as soon as the next one is published, so a
# pin that is current today is archived in a few weeks - look in both, newest first.
# Local hits first so a half-populated tree costs no requests, and so the answer does
# not change mid-life of a cache entry.
resolve_mk() {
    local name="$1" candidate
    for candidate in "mk/$name" "mk/former_versions/$name"; do
        if [ -f "$kernels_dir/$candidate" ]; then
            echo "$candidate"
            return 0
        fi
    done
    for candidate in "mk/$name" "mk/former_versions/$name"; do
        if curl -fsSIL --max-time 60 $RETRY_OPTS -o /dev/null "$BASE_URL/$candidate" 2>/dev/null; then
            echo "$candidate"
            return 0
        fi
    done
    echo "error: meta-kernel $name is in neither mk/ nor mk/former_versions/ of $BASE_URL" >&2
    return 1
}

# Pins, minus comments and blank lines: "<mk file name>[ -> <alias in mk/>]".
pins="$(sed -e 's/#.*//' -e 's/[[:space:]]*$//' "$pins_file" | grep -v '^$')"
pins_id="$(printf '%s\n' "$pins" | cksum | tr -d ' ')"

echo "SPICE kernels -> $kernels_dir"

# Fast path: this manifest was written for today's pins, and everything it promised is
# still there at the right size. That is the CI-cache-hit case and must not touch the
# network.
#
# The pins check is what makes it safe to restore a cache entry written for older pins
# (CI does, via restore-keys, so that bumping one pin refetches one closure instead of
# all four): every file such a tree lists is present and correct, so a size-only check
# would call it complete and never fetch the newly pinned meta-kernel.
if [ -f "$manifest" ] && [ "$(head -n 1 "$manifest")" = "#pins $pins_id" ]; then
    complete=1
    while IFS=$'\t' read -r rel size; do
        case "${rel:-}" in "" | '#'*) continue ;; esac
        if [ ! -f "$kernels_dir/$rel" ] || [ "$(file_size "$kernels_dir/$rel")" != "$size" ]; then
            complete=0
            break
        fi
    done < "$manifest"
    if [ "$complete" = 1 ]; then
        echo "  $(grep -cv '^#' "$manifest") kernels present and complete (manifest verified, no downloads)"
        exit 0
    fi
    echo "  manifest incomplete -- refetching what is missing"
elif [ -f "$manifest" ]; then
    echo "  manifest was written for different pins -- refetching what changed"
fi

# The meta-kernels first: they are what says which kernels exist.
#
# Aliased pins are additionally written into mk/ under their plain name (hera_ops.tm,
# hera_plan.tm) - the names the tests open, and the names PRo3D ships with, while the
# pin file decides which release they actually are.
mk_paths=()
aliases=()
while IFS= read -r line; do
    mk_path="$(resolve_mk "${line%% -> *}")"
    fetch_one "$kernels_dir" "$mk_path"
    mk_paths+=("$mk_path")

    case "$line" in
        *" -> "*)
            alias_name="${line##* -> }"
            # ESA rewrites PATH_VALUES from '..' to '../..' when it archives a
            # meta-kernel into former_versions/ - the kernel list is untouched, only
            # that one line differs. A copy landing back in mk/ therefore has to have
            # it rewritten, or every $KERNELS path resolves one directory too high
            # (the tests chdir to the meta-kernel's own directory before loading it).
            # A no-op when the pin is still current and sits in mk/.
            sed "s|\(PATH_VALUES *= *( *\)'[^']*'|\1'..'|" "$kernels_dir/$mk_path" \
                > "$kernels_dir/mk/$alias_name"
            aliases+=("mk/$alias_name")
            ;;
    esac
done <<< "$pins"

# Union of the closures. Sorting is what deduplicates: the four meta-kernels overlap
# heavily (~250 entries collapse to ~120 files), and downloading a 338 MB CK twice
# would double the cold-cache cost for nothing.
#
# Accumulated in a file rather than a `$(... | sort)` pipeline on purpose: a failing
# kernel_list inside a pipeline's subshell would be masked by sort's exit status, and
# a truncated kernel list is precisely the failure that must not pass silently.
closure_file="$kernels_dir/.pro3d-kernels-closure"
: > "$closure_file"
for mk in "${mk_paths[@]}"; do
    kernel_list "$kernels_dir/$mk" >> "$closure_file"
done
sort -u -o "$closure_file" "$closure_file"
echo "  $(grep -c . "$closure_file") kernels named by ${#mk_paths[@]} meta-kernels"

xargs -P "$JOBS" -I {} "$script_dir/fetch-spice-kernels.sh" --fetch-one "$kernels_dir" {} \
    < "$closure_file"

# Write the manifest last, and only from files that are actually on disk -- it is the
# "this tree is usable" marker, so a partial write here would be worse than none.
: > "$manifest.tmp"
for rel in "${mk_paths[@]}" "${aliases[@]}" $(cat "$closure_file"); do
    if [ ! -f "$kernels_dir/$rel" ]; then
        echo "error: $rel missing after fetch" >&2
        rm -f "$manifest.tmp"
        exit 1
    fi
    printf '%s\t%s\n' "$rel" "$(file_size "$kernels_dir/$rel")" >> "$manifest.tmp"
done
sort -u "$manifest.tmp" -o "$manifest.tmp"
# The pins line first, so the fast path can reject a manifest from other pins by
# reading one line.
{ echo "#pins $pins_id"; cat "$manifest.tmp"; } > "$manifest.new"
mv "$manifest.new" "$manifest"
rm -f "$manifest.tmp"

total="$(awk -F'\t' '$1 !~ /^#/ { n += $2 } END { printf "%.1f", n / 1073741824 }' "$manifest")"
echo "  done: $(grep -cv '^#' "$manifest") files, ${total} GiB"
echo "  point the tests at it with PRO3D_SPICE_KERNELS=$(cd "$dest" && pwd)"
