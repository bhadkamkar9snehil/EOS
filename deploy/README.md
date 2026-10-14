# Deploying EOS V2 to the Ubuntu VM

EOS runs as the systemd service `eos` on port 8095, data in `/var/lib/eos` (SQLite, WAL), nightly backup at 02:30 into `/var/lib/eos/backups` (14 daily + 12 monthly kept).

## One-time setup (needs sudo on the VM)

```sh
sudo useradd --system --home /var/lib/eos --shell /usr/sbin/nologin eos
sudo mkdir -p /opt/eos /var/lib/eos/backups
sudo chown -R eos:eos /opt/eos /var/lib/eos
```

The .NET 10 runtime/SDK must be installed (`dotnet --version`). The deploy script needs `sudo` for the file copies and `systemctl`; to let the GitLab runner do it unattended, add a sudoers rule that allows only those commands for the runner user.

Reachability: bind is `0.0.0.0:8095`. Restrict it to the office LAN and Tailscale with the firewall, for example:

```sh
sudo ufw allow from 10.2.6.0/24 to any port 8095 proto tcp
sudo ufw allow in on tailscale0 to any port 8095 proto tcp
```

## Deploy

From a checkout on the VM:

```sh
sh deploy/deploy.sh
```

It publishes the server and the CLI, swaps them in, restarts, checks `/health`, and rolls back to the previous build if the check fails. The same script runs from the manual `deploy` job in `.gitlab-ci.yml`.

## First sign-in

No token exists on a fresh database. Create one on the VM, then paste it on the login page:

```sh
eos token create snehil
```

Create one token per person or tool (CLI, MCP, a team leader). The token name is recorded as the actor on everything it changes.

## Moving data in

Use the Imports page (or `eos import ... --commit`) for the agility workbook, training tracker, ERP master, task tracker and VM inventory. Imports are repeatable.

## Operations

| Task | Command |
|---|---|
| Status / logs | `systemctl status eos`, `journalctl -u eos -f` |
| Backup now | `eos backup create` |
| Restore | stop `eos`, copy a file from `/var/lib/eos/backups` over `/var/lib/eos/eos.db`, start `eos` |
| Health | `curl http://127.0.0.1:8095/health` |
