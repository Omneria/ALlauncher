#!/usr/bin/env bash
# Régénère manifest.json après une modification de mods/ ou config/ (appelé par
# modpack-manifest.service, lui-même déclenché par modpack-manifest.path).
#
# 1. Attend que plus aucun fichier n'ait bougé depuis QUIET_SECONDS : un .jar en cours d'upload
#    (SFTP, scp) serait sinon indexé à moitié copié, et son empreinte tronquée distribuée à tous
#    les joueurs.
# 2. Rend les fichiers lisibles par Caddy (sinon 403 et synchro en échec pour tout le monde).
# 3. Régénère le manifest (écriture atomique, voir generate-manifest.py).
set -euo pipefail

MODPACK_ROOT="${MODPACK_ROOT:-/var/www/html/modpack}"
BASE_URL="${BASE_URL:-https://astralnexusmc.duckdns.org/modpack}"
TOOLS_DIR="${TOOLS_DIR:-/opt/modpack-tools}"
QUIET_SECONDS="${QUIET_SECONDS:-15}"

watched=()
for dir in "$MODPACK_ROOT/mods" "$MODPACK_ROOT/config"; do
    [ -d "$dir" ] && watched+=("$dir")
done

while [ -n "$(find "${watched[@]}" -newermt "-${QUIET_SECONDS} seconds" -print -quit)" ]; do
    echo "Fichiers encore en cours de modification, nouvelle vérification dans 5 s..."
    sleep 5
done

chmod -R a+rX "${watched[@]}"

python3 "$TOOLS_DIR/generate-manifest.py" \
    --root "$MODPACK_ROOT" \
    --base-url "$BASE_URL" \
    --output "$MODPACK_ROOT/manifest.json"
