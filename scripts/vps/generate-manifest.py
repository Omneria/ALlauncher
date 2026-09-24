#!/usr/bin/env python3
"""Génère manifest.json (mode incrémental, voir README.md "Mode manifest") à partir du
contenu réel de mods/ et config/ sur le VPS.

À exécuter directement sur le VPS (ou sur une copie locale des mêmes dossiers), pas depuis
ce dépôt. Ne dépend que de la bibliothèque standard Python 3.

Usage :
    python3 generate-manifest.py \
        --root /var/www/html/modpack \
        --base-url https://astralnexusmc.duckdns.org/modpack \
        --output /var/www/html/modpack/manifest.json

--root doit contenir les dossiers mods/ et config/ (les seuls synchronisés par le launcher).
Chaque fichier trouvé est publié à <base-url>/<chemin relatif>, donc les fichiers doivent
être accessibles en HTTP à cette URL (ex: servis statiquement par nginx/Apache depuis --root).
"""

import argparse
import hashlib
import json
import os
import sys
import tempfile
from pathlib import Path

SYNCED_SUBDIRS = ("mods", "config")


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def build_manifest(root: Path, base_url: str) -> dict:
    base_url = base_url.rstrip("/")
    files = {}

    for subdir in SYNCED_SUBDIRS:
        subdir_path = root / subdir
        if not subdir_path.is_dir():
            print(f"Attention : {subdir_path} n'existe pas, ignoré.", file=sys.stderr)
            continue

        for file_path in sorted(subdir_path.rglob("*")):
            if not file_path.is_file():
                continue

            relative_path = file_path.relative_to(root).as_posix()
            files[relative_path] = {
                "url": f"{base_url}/{relative_path}",
                "sha256": sha256_of(file_path),
                "size": file_path.stat().st_size,
            }

    return {"files": files}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", required=True, type=Path, help="Dossier contenant mods/ et config/")
    parser.add_argument("--base-url", required=True, help="URL HTTP de base sous laquelle ces fichiers sont servis")
    parser.add_argument("--output", required=True, type=Path, help="Chemin du manifest.json à écrire")
    args = parser.parse_args()

    if not args.root.is_dir():
        parser.error(f"--root {args.root} n'existe pas ou n'est pas un dossier")

    manifest = build_manifest(args.root, args.base_url)

    # Écriture atomique : fichier temporaire dans le même dossier, puis remplacement d'un coup.
    # Un launcher qui télécharge le manifest pendant sa régénération reçoit soit l'ancien, soit
    # le nouveau, jamais un JSON à moitié écrit (qui ferait échouer sa synchro).
    content = json.dumps(manifest, indent=2, ensure_ascii=False)
    fd, tmp_path = tempfile.mkstemp(dir=args.output.parent, prefix=".manifest-", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as tmp:
            tmp.write(content)
        os.chmod(tmp_path, 0o644)
        os.replace(tmp_path, args.output)
    except BaseException:
        if os.path.exists(tmp_path):
            os.unlink(tmp_path)
        raise

    print(f"{len(manifest['files'])} fichiers indexés -> {args.output}")


if __name__ == "__main__":
    main()
