#!/bin/sh
# Build and deploy EOS V2 on the Ubuntu VM from a checkout of this repository.
# Run as a user that may use sudo for: systemctl restart eos, and writing /opt/eos (see deploy/README.md).
# Safe to re-run: publishes to a fresh folder, swaps it in, restarts the service, checks /health, and rolls back if the check fails.
set -eu

ROOT=$(cd "$(dirname "$0")/.." && pwd)
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT

echo "== publish"
dotnet publish "$ROOT/src/EOS.Server" -c Release -o "$STAGE/app" --nologo
dotnet publish "$ROOT/src/EOS.Cli" -c Release -o "$STAGE/cli" --nologo

echo "== install"
sudo mkdir -p /opt/eos /var/lib/eos/backups
if [ -d /opt/eos/app ]; then sudo rm -rf /opt/eos/app.prev && sudo mv /opt/eos/app /opt/eos/app.prev; fi
sudo rm -rf /opt/eos/cli
sudo cp -r "$STAGE/app" /opt/eos/app
sudo cp -r "$STAGE/cli" /opt/eos/cli
sudo cp "$ROOT/deploy/eos" /usr/local/bin/eos
sudo chmod 755 /usr/local/bin/eos
sudo cp "$ROOT/deploy/eos.service" "$ROOT/deploy/eos-backup.service" "$ROOT/deploy/eos-backup.timer" /etc/systemd/system/
sudo chown -R eos:eos /opt/eos /var/lib/eos
sudo systemctl daemon-reload
sudo systemctl enable eos eos-backup.timer >/dev/null 2>&1
sudo systemctl restart eos
sudo systemctl start eos-backup.timer

echo "== health check"
i=0
until curl -fsS http://127.0.0.1:8095/health >/dev/null 2>&1; do
    i=$((i + 1))
    if [ "$i" -ge 30 ]; then
        echo "EOS did not become healthy; rolling back"
        if [ -d /opt/eos/app.prev ]; then
            sudo rm -rf /opt/eos/app && sudo mv /opt/eos/app.prev /opt/eos/app && sudo systemctl restart eos
        fi
        exit 1
    fi
    sleep 1
done
curl -fsS http://127.0.0.1:8095/health
echo
echo "EOS deployed."
