#!/usr/bin/env python3
"""Add the web demo to an existing local realm without replacing its client secret or users."""
import argparse
import json
import subprocess
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--project", default="starter-demo")
args = parser.parse_args()
prefix = ["docker", "compose", "-p", args.project, "exec", "-T", "keycloak"]


def command(*arguments, input_text=None):
    return subprocess.run(prefix + list(arguments), input=input_text, text=True,
                          capture_output=True, check=True).stdout


def kc(*arguments):
    return command("/opt/keycloak/bin/kcadm.sh", *arguments)


command("sh", "-lc",
        '/opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 '
        '--realm master --user "$KC_BOOTSTRAP_ADMIN_USERNAME" '
        '--password "$KC_BOOTSTRAP_ADMIN_PASSWORD" >/dev/null')
realm = json.loads(Path("keycloak/realm-export.json").read_text())
web = next(client for client in realm["clients"] if client["clientId"] == "starter-web")
clients = json.loads(kc("get", "clients", "-r", "starter", "-q", "clientId=starter-web"))
if not clients:
    command("sh", "-c", "cat > /tmp/starter-web-import.json", input_text=json.dumps(web))
    kc("create", "clients", "-r", "starter", "-f", "/tmp/starter-web-import.json")
    command("rm", "/tmp/starter-web-import.json")
    clients = json.loads(kc("get", "clients", "-r", "starter", "-q", "clientId=starter-web"))
client = clients[0]
attributes = client.get("attributes", {})
attributes.update(web["attributes"])
kc("update", "clients/" + client["id"], "-r", "starter",
   "-s", "fullScopeAllowed=false", "-s", "publicClient=false",
   "-s", "standardFlowEnabled=true", "-s", "directAccessGrantsEnabled=false",
   "-s", "serviceAccountsEnabled=false", "-s", "implicitFlowEnabled=false",
   "-s", "redirectUris=" + json.dumps(web["redirectUris"]),
   "-s", "attributes=" + json.dumps(attributes))
api = json.loads(kc("get", "clients", "-r", "starter", "-q", "clientId=starter-api"))[0]
roles = json.loads(kc("get", "clients/" + api["id"] + "/roles", "-r", "starter"))
command("sh", "-c", "cat > /tmp/starter-web-roles.json", input_text=json.dumps(roles))
kc("create", "clients/" + client["id"] + "/scope-mappings/clients/" + api["id"],
   "-r", "starter", "-f", "/tmp/starter-web-roles.json")
command("rm", "/tmp/starter-web-roles.json")
for user in realm["users"]:
    if not user["username"].startswith("demo-"):
        continue
    existing = json.loads(kc("get", "users", "-r", "starter", "-q", "username=" + user["username"]))
    if not existing:
        command("sh", "-c", "cat > /tmp/starter-web-user.json", input_text=json.dumps(user))
        kc("create", "users", "-r", "starter", "-f", "/tmp/starter-web-user.json")
        command("rm", "/tmp/starter-web-user.json")
    for role in user.get("clientRoles", {}).get("starter-api", []):
        kc("add-roles", "-r", "starter", "--uusername", user["username"],
           "--cclientid", "starter-api", "--rolename", role)
secret = json.loads(kc("get", "clients/" + client["id"] + "/client-secret", "-r", "starter"))["value"]
subprocess.run(["dotnet", "user-secrets", "set", "--project", "src/Keycloak.Starter.Web"],
               input=json.dumps({"Keycloak:ClientSecret": secret}), text=True,
               stdout=subprocess.DEVNULL, check=True)
path = Path(".env")
lines = path.read_text().splitlines() if path.exists() else []
lines = [line for line in lines if not line.startswith("WEB_CLIENT_SECRET=")]
lines.append("WEB_CLIENT_SECRET=" + json.dumps(secret))
path.write_text("\n".join(lines) + "\n")
path.chmod(0o600)
print("Web client, role scopes, demo users and local secret configuration are ready.")
