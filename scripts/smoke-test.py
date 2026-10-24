#!/usr/bin/env python3
"""Exercise the disposable development realm without printing tokens or secrets."""
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

API_URL = os.getenv("API_URL", "http://localhost:5150").rstrip("/")
KEYCLOAK_URL = os.getenv("KEYCLOAK_URL", "http://localhost:8080").rstrip("/")


def request_status(path, token=None):
    headers = {"Authorization": f"Bearer {token}"} if token else {}
    request = urllib.request.Request(API_URL + path, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            return response.status
    except urllib.error.HTTPError as error:
        return error.code


def access_token(client_id, secret):
    data = urllib.parse.urlencode({
        "grant_type": "client_credentials",
        "client_id": client_id,
        "client_secret": secret,
    }).encode()
    request = urllib.request.Request(
        KEYCLOAK_URL + "/realms/starter/protocol/openid-connect/token",
        data=data, method="POST",
    )
    with urllib.request.urlopen(request, timeout=10) as response:
        return json.load(response)["access_token"]


def check(path, expected, label, token=None):
    actual = request_status(path, token)
    print(f"{label}: {path} -> {actual} (expected {expected})")
    if actual != expected:
        raise RuntimeError(f"{label} failed: expected {expected}, received {actual}")


def main():
    check("/health", 200, "Public")
    for path in ("/me", "/reader", "/admin"):
        check(path, 401, "Anonymous")

    personas = (
        ("starter-cli", os.getenv("READER_CLIENT_SECRET", "local-demo-reader-secret"), 200, 403),
        ("starter-admin", os.getenv("ADMIN_CLIENT_SECRET", "local-demo-admin-secret"), 403, 200),
        ("starter-no-role", os.getenv("NO_ROLE_CLIENT_SECRET", "local-demo-none-secret"), 403, 403),
    )
    for client_id, secret, reader_status, admin_status in personas:
        token = access_token(client_id, secret)
        check("/me", 200, client_id, token)
        check("/reader", reader_status, client_id, token)
        check("/admin", admin_status, client_id, token)
    print("All 13 development smoke checks passed.")


if __name__ == "__main__":
    try:
        main()
    except (urllib.error.URLError, KeyError, ValueError, RuntimeError) as error:
        print(f"Smoke test failed: {error}", file=sys.stderr)
        sys.exit(1)
