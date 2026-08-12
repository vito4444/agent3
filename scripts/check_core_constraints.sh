#!/usr/bin/env bash
# CI gate 1 static checks for the pure-C# sim core (docs/plan/08):
#   1. Game.Core must not reference UnityEngine/UnityEditor.
#   2. Game.Core.asmdef must pin noEngineReferences: true.
#   3. Game.Core must not use ambient nondeterminism (System.Random, wall clock, ...).
#   4. Numeric literal whitelist: multi-digit/decimal literals only on `const` lines,
#      in whitelisted PRNG/hash files, or in comments.
# All checks strip // comments first so documentation may mention banned APIs.
set -uo pipefail

CORE_DIR="Assets/_Game/Core"
ASMDEF="$CORE_DIR/Game.Core.asmdef"
# Bit-twiddling internals are inherently numeric.
LITERAL_WHITELIST_REGEX='(Rng\.cs|Hashing\.cs)'
FAIL=0

# scan_stripped <regex> — greps all Core sources with comments removed,
# prefixing matches with file:line.
scan_stripped() {
    local regex="$1" file lines
    while IFS= read -r file; do
        lines=$(sed 's|//.*||' "$file" | grep -nE "$regex" || true)
        if [ -n "$lines" ]; then
            while IFS= read -r line; do
                echo "$file:$line"
            done <<< "$lines"
        fi
    done < <(find "$CORE_DIR" -name '*.cs' | sort)
}

echo "== check 1: no UnityEngine/UnityEditor in Game.Core =="
HITS=$(scan_stripped '\bUnityEngine\b|\bUnityEditor\b')
if [ -n "$HITS" ]; then
    echo "$HITS"
    echo "FAIL: Game.Core references Unity APIs"
    FAIL=1
else
    echo "PASS"
fi

echo "== check 2: asmdef noEngineReferences =="
if grep -q '"noEngineReferences": true' "$ASMDEF"; then
    echo "PASS"
else
    echo "FAIL: $ASMDEF must set \"noEngineReferences\": true"
    FAIL=1
fi

echo "== check 3: no ambient nondeterminism in Game.Core =="
HITS=$(scan_stripped 'System\.Random|DateTime\.Now|DateTime\.UtcNow|Environment\.TickCount|Guid\.NewGuid|Stopwatch')
if [ -n "$HITS" ]; then
    echo "$HITS"
    echo "FAIL: nondeterministic API used in Game.Core"
    FAIL=1
else
    echo "PASS"
fi

echo "== check 4: numeric literal whitelist =="
# Literal = digits not preceded by an identifier character (so v00 / Fnv1a64 don't count):
# decimals, or integers with 2+ digits. Lines declaring consts are allowed.
VIOLATIONS=""
while IFS= read -r file; do
    if [[ "$file" =~ $LITERAL_WHITELIST_REGEX ]]; then
        continue
    fi
    LINES=$(sed 's|//.*||' "$file" \
        | grep -nE '(^|[^A-Za-z0-9_.])([0-9]+\.[0-9]+|[0-9]{2,})' \
        | grep -v 'const ' || true)
    if [ -n "$LINES" ]; then
        while IFS= read -r line; do
            VIOLATIONS+="$file:$line"$'\n'
        done <<< "$LINES"
    fi
done < <(find "$CORE_DIR" -name '*.cs' | sort)
if [ -n "$VIOLATIONS" ]; then
    printf '%s' "$VIOLATIONS"
    echo "FAIL: loose numeric literals in Game.Core (move them to consts, see docs/plan/08)"
    FAIL=1
else
    echo "PASS"
fi

if [ "$FAIL" -ne 0 ]; then
    echo "core constraint checks FAILED"
    exit 1
fi
echo "core constraint checks passed"
