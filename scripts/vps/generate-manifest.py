#!/usr/bin/env python3
"""Génère manifest.json (mode incrémental, voir README.md "Mode manifest") à partir du
contenu réel de mods/ et config/ sur le VPS, et tient à jour changelog.json (historique des
changements du pack, affiché dans la carte ACTUS du launcher).

À exécuter directement sur le VPS (ou sur une copie locale des mêmes dossiers), pas depuis
ce dépôt. Ne dépend que de la bibliothèque standard Python 3.

Usage :
    python3 generate-manifest.py \
        --root /var/www/html/modpack \
        --base-url https://astralnexusmc.duckdns.org/modpack \
        --output /var/www/html/modpack/manifest.json

--root doit contenir les dossiers mods/ et config/ (les seuls synchronisés par le launcher).
Chaque fichier trouvé est publié à <base-url>/<chemin relatif>, donc les fichiers doivent
être accessibles en HTTP à cette URL (ex: servis statiquement par Caddy depuis --root).

Réglages du pack (optionnel) : <root>/pack.json, recopié dans la section "pack" du manifest.
    {
      "forgeVersion": "47.4.23",       version de Forge installée par le launcher
      "recommendedRamMb": 6144,        RAM conseillée aux joueurs
      "minRamMb": 4096,                en dessous, le launcher avertit avant de lancer
      "enforcedConfigs": ["config/simple-custom-early-loading.json"],
      "serverOnlyMods": ["Chunky-*.jar"]
    }
- enforcedConfigs : motifs (style shell, insensibles à la casse) des fichiers de config/ imposés à tous les joueurs,
  réécrits à chaque lancement. Les autres fichiers de config/ sont des réglages "par défaut" :
  installés seulement s'ils manquent, un joueur garde ce qu'il y a changé.
- serverOnlyMods : motifs des fichiers de mods/ utiles au serveur seul, jamais envoyés aux joueurs.
"""

import argparse
import fnmatch
import hashlib
import json
import os
import re
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path

SYNCED_SUBDIRS = ("mods", "config")
PACK_KEYS = ("forgeVersion", "recommendedRamMb", "minRamMb")
CHANGELOG_MAX_ENTRIES = 30


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def load_pack_settings(root: Path) -> dict:
    path = root / "pack.json"
    if not path.is_file():
        return {}
    try:
        settings = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        # Un pack.json cassé ne doit pas bloquer la publication des mods : on l'ignore, bruyamment.
        print(f"Attention : {path} illisible ({error}), ignoré.", file=sys.stderr)
        return {}
    return settings if isinstance(settings, dict) else {}


def matches_any(relative_path: str, patterns) -> bool:
    # Insensible à la casse : les noms de jars varient d'un auteur à l'autre ("Krypton..." dans la
    # doc, "krypton-reno-..." sur le disque), et fnmatch respecte la casse sous Linux.
    path = relative_path.lower()
    name = path.rsplit("/", 1)[-1]
    return any(fnmatch.fnmatchcase(path, p.lower()) or fnmatch.fnmatchcase(name, p.lower()) for p in patterns)


def build_manifest(root: Path, base_url: str, pack_settings: dict) -> dict:
    base_url = base_url.rstrip("/")
    enforced_configs = pack_settings.get("enforcedConfigs") or []
    server_only_mods = pack_settings.get("serverOnlyMods") or []
    files = {}

    for subdir in SYNCED_SUBDIRS:
        subdir_path = root / subdir
        if not subdir_path.is_dir():
            print(f"Attention : {subdir_path} n'existe pas, ignoré.", file=sys.stderr)
            continue

        for file_path in sorted(subdir_path.rglob("*")):
            # Fichiers cachés : temporaires d'upload ou d'écriture atomique (.part, .mod-sync-*),
            # jamais du contenu du pack.
            if not file_path.is_file() or file_path.name.startswith("."):
                continue

            relative_path = file_path.relative_to(root).as_posix()
            if subdir == "mods" and matches_any(relative_path, server_only_mods):
                continue

            entry = {
                "url": f"{base_url}/{relative_path}",
                "sha256": sha256_of(file_path),
                "size": file_path.stat().st_size,
            }
            # Absent = imposé (comportement des launchers antérieurs à la v1.12.0, qui ignorent ce
            # champ) : seuls les réglages "par défaut" le portent.
            if subdir == "config" and not matches_any(relative_path, enforced_configs):
                entry["mode"] = "default"
            files[relative_path] = entry

    manifest = {"files": files}
    pack = {key: pack_settings[key] for key in PACK_KEYS if key in pack_settings}
    if pack:
        manifest["pack"] = pack
    return manifest


def mod_key(file_name: str) -> str:
    """Nom d'un mod sans sa version : "create-1.20.1-6.0.8.jar" -> "create". Sert à reconnaître
    une mise à jour (ancien et nouveau fichier portent des noms différents)."""
    stem = file_name[:-4] if file_name.lower().endswith(".jar") else file_name
    return re.split(r"[-_ ]+(?:mc)?v?\d", stem, maxsplit=1)[0].lower()


def display_name(file_name: str) -> str:
    return file_name[:-4] if file_name.lower().endswith(".jar") else file_name


def diff_manifests(old: dict, new: dict) -> list:
    """Lignes lisibles par un joueur décrivant ce qui change entre deux manifests (mods seulement,
    les configs sont résumées en une ligne)."""
    old_files = old.get("files", {})
    new_files = new.get("files", {})

    def mods(files):
        return {p.split("/", 1)[1]: f["sha256"] for p, f in files.items() if p.startswith("mods/")}

    old_mods, new_mods = mods(old_files), mods(new_files)
    added = sorted(set(new_mods) - set(old_mods))
    removed = sorted(set(old_mods) - set(new_mods))
    changed_in_place = sorted(n for n in set(new_mods) & set(old_mods) if new_mods[n] != old_mods[n])

    removed_by_key = {}
    for name in removed:
        removed_by_key.setdefault(mod_key(name), []).append(name)

    lines = []
    for name in added:
        previous = removed_by_key.get(mod_key(name))
        if previous:
            old_name = previous.pop(0)
            removed.remove(old_name)
            lines.append(f"Mis à jour : {display_name(old_name)} → {display_name(name)}")
        else:
            lines.append(f"Ajouté : {display_name(name)}")
    lines.extend(f"Mis à jour : {display_name(name)}" for name in changed_in_place)
    lines.extend(f"Retiré : {display_name(name)}" for name in removed)

    config_changes = sum(
        1 for p in set(old_files) | set(new_files)
        if p.startswith("config/") and old_files.get(p, {}).get("sha256") != new_files.get(p, {}).get("sha256"))
    if config_changes:
        lines.append(f"Configuration : {config_changes} fichier(s) modifié(s)")

    old_pack, new_pack = old.get("pack", {}), new.get("pack", {})
    if old_pack.get("forgeVersion") != new_pack.get("forgeVersion") and new_pack.get("forgeVersion"):
        lines.append(f"Forge : {new_pack['forgeVersion']}")
    return lines


def write_json_atomically(path: Path, data) -> None:
    # Écriture atomique : fichier temporaire dans le même dossier, puis remplacement d'un coup.
    # Un launcher qui télécharge le fichier pendant sa régénération reçoit soit l'ancien, soit
    # le nouveau, jamais un JSON à moitié écrit (qui ferait échouer sa synchro).
    content = json.dumps(data, indent=2, ensure_ascii=False)
    fd, tmp_path = tempfile.mkstemp(dir=path.parent, prefix=f".{path.stem}-", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as tmp:
            tmp.write(content)
        os.chmod(tmp_path, 0o644)
        os.replace(tmp_path, path)
    except BaseException:
        if os.path.exists(tmp_path):
            os.unlink(tmp_path)
        raise


def load_json(path: Path, default):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return default


def update_changelog(changelog_path: Path, old_manifest: dict, new_manifest: dict) -> list:
    lines = diff_manifests(old_manifest, new_manifest)
    # Premier passage (aucun ancien manifest) : tout le pack apparaîtrait "ajouté", ce qui
    # n'apprend rien à personne.
    if not lines or not old_manifest.get("files"):
        return []

    entries = load_json(changelog_path, [])
    if not isinstance(entries, list):
        entries = []
    entries.insert(0, {"date": datetime.now(timezone.utc).isoformat(timespec="seconds"), "lines": lines})
    write_json_atomically(changelog_path, entries[:CHANGELOG_MAX_ENTRIES])
    return lines


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", required=True, type=Path, help="Dossier contenant mods/ et config/")
    parser.add_argument("--base-url", required=True, help="URL HTTP de base sous laquelle ces fichiers sont servis")
    parser.add_argument("--output", required=True, type=Path, help="Chemin du manifest.json à écrire")
    parser.add_argument("--changelog", type=Path, help="Chemin du changelog.json (défaut : à côté du manifest)")
    args = parser.parse_args()

    if not args.root.is_dir():
        parser.error(f"--root {args.root} n'existe pas ou n'est pas un dossier")

    old_manifest = load_json(args.output, {})
    manifest = build_manifest(args.root, args.base_url, load_pack_settings(args.root))
    write_json_atomically(args.output, manifest)

    changelog_path = args.changelog or args.output.with_name("changelog.json")
    changes = update_changelog(changelog_path, old_manifest if isinstance(old_manifest, dict) else {}, manifest)

    print(f"{len(manifest['files'])} fichiers indexés -> {args.output}")
    for line in changes:
        print(f"  {line}")


if __name__ == "__main__":
    main()
