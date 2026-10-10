#!/usr/bin/env python3
"""Uploads a signed Android App Bundle to a Google Play track (default: internal testing).

Used by .github/workflows/android-release.yml. Standard library only; the service account's JWT is signed with the
`openssl` command line tool. Steps (Google Play Developer API v3, "edits"):

  1. service account key -> OAuth access token (scope androidpublisher)
  2. edits.insert -> edits.bundles.upload -> edits.tracks.update -> edits.commit

The service account key comes from the PLAY_SERVICE_ACCOUNT_JSON environment variable and is never printed or written
to the workspace. Endpoints can be overridden for tests (PLAY_TOKEN_URI, PLAY_API_BASE, PLAY_UPLOAD_BASE).
"""

import argparse
import base64
import json
import os
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

SCOPE = "https://www.googleapis.com/auth/androidpublisher"
TOKEN_URI = "https://oauth2.googleapis.com/token"
API_BASE = "https://androidpublisher.googleapis.com/androidpublisher/v3"
UPLOAD_BASE = "https://androidpublisher.googleapis.com/upload/androidpublisher/v3"

HINTS = {
    "Only releases with status draft may be created on draft app":
        "The app has never been released. Upload the first bundle by hand in Play Console (Testing > Internal testing > "
        "Create new release) and roll it out; after that this upload works. See android/docs/PLAY.md.",
    "Package not found":
        "Play Console has no app with this package name yet, or the service account cannot see it. Create the app and "
        "upload the first bundle by hand, then invite the service account in Users and permissions.",
    "The caller does not have permission":
        "The service account has no access to the app: Play Console > Users and permissions > invite its e-mail with "
        "'Release apps to testing tracks' for this app.",
    "APK specifies a version code that has already been used":
        "This version code was already uploaded. Run the workflow again (each run uses a new number).",
    "Version code":
        "Play rejected the version code; each upload needs a higher number than the previous one.",
}


class PlayError(Exception):
    pass


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


def sign_rs256(message: bytes, private_key_pem: str) -> bytes:
    """Signs with openssl so the private key never needs a third-party Python package."""
    fd, key_path = tempfile.mkstemp(suffix=".pem")
    try:
        with os.fdopen(fd, "w") as key_file:
            key_file.write(private_key_pem)
        os.chmod(key_path, 0o600)
        result = subprocess.run(["openssl", "dgst", "-sha256", "-sign", key_path], input=message, capture_output=True,
                                check=False)
        if result.returncode != 0:
            raise PlayError("openssl could not sign the token request (is the service account key valid?)")
        return result.stdout
    finally:
        os.remove(key_path)


def access_token(service_account: dict) -> str:
    for field in ("client_email", "private_key"):
        if not service_account.get(field):
            raise PlayError(f"The service account JSON has no '{field}': download a new JSON key for it.")
    token_uri = os.environ.get("PLAY_TOKEN_URI", TOKEN_URI)
    now = int(time.time())
    header = b64url(json.dumps({"alg": "RS256", "typ": "JWT"}).encode())
    claims = b64url(json.dumps({"iss": service_account["client_email"], "scope": SCOPE, "aud": TOKEN_URI,
                                "iat": now, "exp": now + 3600}).encode())
    unsigned = f"{header}.{claims}".encode("ascii")
    assertion = unsigned.decode("ascii") + "." + b64url(sign_rs256(unsigned, service_account["private_key"]))
    body = urllib.parse.urlencode({"grant_type": "urn:ietf:params:oauth:grant-type:jwt-bearer",
                                   "assertion": assertion}).encode()
    response = request("POST", token_uri, body, "application/x-www-form-urlencoded", token=None)
    if not response.get("access_token"):
        raise PlayError("Google did not return an access token for the service account.")
    return response["access_token"]


def request(method: str, url: str, body: bytes | None, content_type: str | None, token: str | None,
            timeout: int = 60) -> dict:
    headers = {}
    if content_type:
        headers["Content-Type"] = content_type
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(url, data=body, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as response:
            raw = response.read()
            return json.loads(raw) if raw else {}
    except urllib.error.HTTPError as error:
        raw = error.read().decode("utf-8", "replace")
        try:
            message = json.loads(raw).get("error", {})
            text = message.get("message") if isinstance(message, dict) else str(message)
        except json.JSONDecodeError:
            text = raw[:500]
        hint = next((h for key, h in HINTS.items() if text and key.lower() in text.lower()), None)
        raise PlayError(f"{method} {urllib.parse.urlsplit(url).path} failed ({error.code}): {text}"
                        + (f"\nHint: {hint}" if hint else "")) from None


def upload(package: str, bundle_path: str, track: str, status: str, release_name: str, notes: str | None) -> int:
    service_account = json.loads(os.environ["PLAY_SERVICE_ACCOUNT_JSON"])
    api = os.environ.get("PLAY_API_BASE", API_BASE)
    upload_base = os.environ.get("PLAY_UPLOAD_BASE", UPLOAD_BASE)
    token = access_token(service_account)
    app = f"applications/{urllib.parse.quote(package)}"

    edit = request("POST", f"{api}/{app}/edits", b"{}", "application/json", token)
    edit_id = edit["id"]
    print(f"Edit {edit_id} opened for {package}.")

    with open(bundle_path, "rb") as bundle_file:
        bundle = request("POST", f"{upload_base}/{app}/edits/{edit_id}/bundles?uploadType=media", bundle_file.read(),
                         "application/octet-stream", token, timeout=600)
    version_code = int(bundle["versionCode"])
    print(f"Bundle uploaded: version code {version_code}.")

    release = {"name": release_name, "versionCodes": [str(version_code)], "status": status}
    if notes:
        release["releaseNotes"] = [{"language": "es-419", "text": notes}]
    request("PUT", f"{api}/{app}/edits/{edit_id}/tracks/{urllib.parse.quote(track)}",
            json.dumps({"track": track, "releases": [release]}).encode(), "application/json", token)
    print(f"Release '{release_name}' set on track '{track}' ({status}).")

    request("POST", f"{api}/{app}/edits/{edit_id}:commit", None, None, token)
    print("Edit committed: Play is processing the release.")
    return version_code


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--package", required=True)
    parser.add_argument("--bundle", required=True, help="path to the signed .aab")
    parser.add_argument("--track", default="internal")
    parser.add_argument("--status", default="completed", choices=["completed", "draft"])
    parser.add_argument("--release-name", required=True)
    parser.add_argument("--notes", help="release notes (Spanish, es-419)")
    args = parser.parse_args()
    if not os.environ.get("PLAY_SERVICE_ACCOUNT_JSON"):
        print("PLAY_SERVICE_ACCOUNT_JSON is not set.", file=sys.stderr)
        return 2
    try:
        upload(args.package, args.bundle, args.track, args.status, args.release_name, args.notes)
    except (PlayError, json.JSONDecodeError, KeyError, OSError) as error:
        print(f"::error::{error}" if os.environ.get("GITHUB_ACTIONS") else str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
