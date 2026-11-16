#!/usr/bin/env bash
# Imports the Developer ID certificate into a temporary keychain on a CI runner.
# Needs APPLE_CERTIFICATE_P12 (base64) and APPLE_CERTIFICATE_PASSWORD; does nothing when they are absent.
set -euo pipefail
if [[ -z "${APPLE_CERTIFICATE_P12:-}" || -z "${APPLE_CERTIFICATE_PASSWORD:-}" ]]; then
  echo "No Apple signing certificate configured; the build stays ad-hoc signed."
  exit 0
fi
keychain="$RUNNER_TEMP/ka4alka-signing.keychain-db"
keychain_password="$(uuidgen)"
echo "$APPLE_CERTIFICATE_P12" | base64 --decode > "$RUNNER_TEMP/certificate.p12"
security create-keychain -p "$keychain_password" "$keychain"
security set-keychain-settings -lut 21600 "$keychain"
security unlock-keychain -p "$keychain_password" "$keychain"
security import "$RUNNER_TEMP/certificate.p12" -P "$APPLE_CERTIFICATE_PASSWORD" -A -t cert -f pkcs12 -k "$keychain"
security set-key-partition-list -S apple-tool:,apple: -k "$keychain_password" "$keychain" >/dev/null
security list-keychains -d user -s "$keychain" login.keychain-db
rm -f "$RUNNER_TEMP/certificate.p12"
identity="$(security find-identity -v -p codesigning "$keychain" | sed -n 's/.*"\(Developer ID Application:.*\)"/\1/p' | head -n 1)"
if [[ -z "$identity" ]]; then echo "::error::No Developer ID Application identity found in the imported certificate."; exit 1; fi
echo "APPLE_SIGNING_IDENTITY=$identity" >> "$GITHUB_ENV"
echo "Signing identity ready."
