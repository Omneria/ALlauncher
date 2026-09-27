#!/usr/bin/env python3
"""Recopie les mods du modpack vers le dossier mods/ du serveur Minecraft, sans les mods
"client seul" (clientOnlyMods dans pack.json), qui peuvent empêcher un serveur dédié de démarrer.

Le dossier du modpack devient ainsi la seule source : on y dépose un mod une fois, les joueurs le
reçoivent (manifest.json) et le serveur aussi (ce script). Lancé par update-manifest.sh après
chaque régénération du manifest si SERVER_MODS_DIR est défini (voir modpack-manifest.service),
ou à la main :

    python3 sync-server-mods.py \\
        --modpack-root /var/www/html/modpack \\
        --server-mods /var/opt/minecraft/crafty/crafty-4/servers/<id>/mods

Ne supprime jamais un mod ajouté à la main sur le serveur : seuls les fichiers que ce script a
lui-même installés (mémorisés dans --state-file) sont retirés quand ils quittent le modpack. Un mod
client seul présent sur le serveur sous le même nom que dans le modpack est aussi retiré.

Les fichiers copiés prennent le propriétaire du dossier mods/ du serveur (Crafty tourne sous son
propre utilisateur). Le serveur ne voit les changements qu'à son prochain redémarrage.

Réglages lus dans <modpack-root>/pack.json :
    "clientOnlyMods": ["jei-*.jar", "journeymap-*.jar"]     jamais copiés sur le serveur
Les serverOnlyMods (exclus du téléchargement joueur) sont bien copiés : c'est leur place.

Bibliothèque standard Python 3 uniquement.
"""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path

DEFAULT_STATE_FILE = "/var/lib/modpack-tools/server-mods.json"


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def matches_any(name: str, patterns) -> bool:
    # Insensible à la casse, comme generate-manifest.py.
    name = name.lower()
    return any(fnmatch.fnmatchcase(name, p.lower().rsplit("/", 1)[-1]) for p in patterns)


def load_json(path: Path, default):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return default
    except (OSError, json.JSONDecodeError) as error:
        print(f"Attention : {path} illisible ({error}), ignoré.", file=sys.stderr)
        return default


def jar_files(directory: Path) -> dict[str, Path]:
    # Premier niveau seulement, fichiers cachés exclus (temporaires d'upload, .part).
    return {
        p.name: p
        for p in directory.iterdir()
        if p.is_file() and p.suffix.lower() == ".jar" and not p.name.startswith(".")
    }


def copy_into(source: Path, target_dir: Path, owner: os.stat_result) -> None:
    # Copie dans un temporaire du même dossier puis remplacement d'un coup : jamais de jar à moitié
    # copié sous son nom définitif si le serveur redémarre pendant la copie.
    fd, tmp_name = tempfile.mkstemp(dir=target_dir, prefix=".server-mods-", suffix=".tmp")
    try:
        with os.fdopen(fd, "wb") as tmp, source.open("rb") as src:
            shutil.copyfileobj(src, tmp)
        os.chmod(tmp_name, 0o644)
        try:
            os.chown(tmp_name, owner.st_uid, owner.st_gid)
        except PermissionError:
            pass  # Lancé sans root : le fichier garde l'utilisateur courant.
        os.replace(tmp_name, target_dir / source.name)
    except BaseException:
        Path(tmp_name).unlink(missing_ok=True)
        raise


def sync(modpack_root: Path, server_mods: Path, state_file: Path, dry_run: bool) -> int:
    source_dir = modpack_root / "mods"
    if not source_dir.is_dir():
        print(f"Erreur : {source_dir} introuvable.", file=sys.stderr)
        return 1
    if not server_mods.is_dir():
        print(f"Erreur : {server_mods} introuvable.", file=sys.stderr)
        return 1

    pack = load_json(modpack_root / "pack.json", {})
    client_only = (pack.get("clientOnlyMods") or []) if isinstance(pack, dict) else []
    state = load_json(state_file, {})
    managed = set(state.get("managed", [])) if isinstance(state, dict) else set()

    source = jar_files(source_dir)
    if not source:
        # Dossier du modpack vide (déplacement en cours, montage absent...) : ne pas vider le
        # serveur de tous ses mods pour autant.
        print(f"Erreur : aucun mod dans {source_dir}, rien n'est modifié sur le serveur.", file=sys.stderr)
        return 1
    target = jar_files(server_mods)
    wanted = {name: path for name, path in source.items() if not matches_any(name, client_only)}
    owner = server_mods.stat()
    changes = []

    for name, path in sorted(wanted.items()):
        existing = target.get(name)
        if existing is not None and sha256_of(existing) == sha256_of(path):
            continue
        changes.append(f"{'Mis à jour' if existing else 'Ajouté'} : {name}")
        if not dry_run:
            copy_into(path, server_mods, owner)

    for name, path in sorted(target.items()):
        if name in wanted:
            continue
        # Installé par ce script puis retiré du modpack (ou passé client seul), ou mod client seul
        # arrivé sur le serveur depuis le modpack. Tout autre jar a été ajouté à la main : on n'y
        # touche pas.
        if name in managed or (name in source and name not in wanted):
            changes.append(f"Retiré : {name}")
            if not dry_run:
                path.unlink()

    if not dry_run:
        state_file.parent.mkdir(parents=True, exist_ok=True)
        state_file.write_text(json.dumps({"managed": sorted(wanted)}, indent=2), encoding="utf-8")

    prefix = "[simulation] " if dry_run else ""
    if changes:
        for line in changes:
            print(f"{prefix}{line}")
        print(f"{prefix}{len(changes)} changement(s) dans {server_mods} : redémarre le serveur pour les appliquer.")
    else:
        print(f"{prefix}Mods du serveur déjà à jour ({len(wanted)} mods).")
    return 0


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--modpack-root", required=True, type=Path, help="Dossier du modpack (contient mods/ et pack.json)")
    parser.add_argument("--server-mods", required=True, type=Path, help="Dossier mods/ du serveur Minecraft")
    parser.add_argument("--state-file", type=Path, default=Path(DEFAULT_STATE_FILE),
                        help=f"Mémorise les mods installés par ce script (défaut {DEFAULT_STATE_FILE})")
    parser.add_argument("--dry-run", action="store_true", help="Affiche ce qui changerait, sans rien modifier")
    args = parser.parse_args()
    sys.exit(sync(args.modpack_root, args.server_mods, args.state_file, args.dry_run))


if __name__ == "__main__":
    main()
