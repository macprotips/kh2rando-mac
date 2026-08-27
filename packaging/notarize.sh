#!/bin/bash
# Notarizes and staples a signed .app: zips it, submits to Apple, waits for the
# verdict, staples the ticket, and verifies Gatekeeper acceptance.
# Usage: packaging/notarize.sh "<path to .app>" [keychain-profile]
#
# notarytool intermittently crashes mid-upload (Bus error) on some systems; the
# upload usually still reaches Apple. On a crash this script finds the newest
# submission in the account history and resumes waiting on it, retrying the
# submit only if nothing landed.
# -e matters here: a failed staple or spctl must abort before anything is
# uploaded. An unstapled build shipped once because a failure was swallowed.
set -euo pipefail

APP="${1:?usage: notarize.sh <path-to-app> [keychain-profile]}"
PROFILE="${2:-kh2rando-notary}"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
ZIP="$WORK/submit.zip"

echo "==> Zipping for submission..."
ditto -c -k --keepParent "$APP" "$ZIP"

submission_id=""
for attempt in 1 2 3; do
  echo "==> Submitting to Apple notary service (attempt $attempt)..."
  set +e
  OUTPUT="$(xcrun notarytool submit "$ZIP" --keychain-profile "$PROFILE" --wait 2>&1)"
  STATUS=$?
  set -e
  echo "$OUTPUT" | tail -5

  if [ $STATUS -eq 0 ] && echo "$OUTPUT" | grep -q "status: Accepted"; then
    submission_id="done"
    break
  fi
  if echo "$OUTPUT" | grep -q "status: Invalid"; then
    echo "Apple rejected the submission." >&2
    exit 1
  fi

  # Crashed or failed mid-flight: the upload may have landed anyway. Grab the
  # newest submission id and wait on it.
  recovered="$(xcrun notarytool history --keychain-profile "$PROFILE" 2>/dev/null \
    | grep -m1 'id:' | awk '{print $2}')"
  if [ -n "$recovered" ]; then
    echo "==> Recovering: waiting on submission $recovered..."
    set +e
    WAITED="$(xcrun notarytool wait "$recovered" --keychain-profile "$PROFILE" 2>&1)"
    set -e
    echo "$WAITED" | tail -3
    if echo "$WAITED" | grep -q "status: Accepted"; then
      submission_id="done"
      break
    fi
  fi
  sleep 10
done

if [ "$submission_id" != "done" ]; then
  echo "Notarization did not succeed after 3 attempts." >&2
  exit 1
fi

echo "==> Stapling ticket..."
xcrun stapler staple "$APP"

echo "==> Verifying with Gatekeeper..."
spctl -a -vv "$APP"
echo "==> Done: $APP is notarized and stapled."
