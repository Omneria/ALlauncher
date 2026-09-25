#!/usr/bin/env python3
"""Installe sur le VPS la dernière release GitHub du mod Astral Nexus (dépôt
Astral-Nexus-MC/Astral-launcher, publiée par son workflow release.yml à chaque tag vX.Y.Z).

Lancé toutes les 5 minutes par astralnexus-mod-sync.timer. Quand une nouvelle release existe :
  1. télécharge le jar et son .sha256, vérifie l'empreinte (jar refusé si elle ne correspond pas) ;
  2. le dépose dans chaque dossier cible (modpack, et serveur Minecraft si configuré) ;
  3. supprime les anciennes versions du mod de ces dossiers (MOD_PREFIX et OLD_PREFIXES).
Côté modpack, modpack-manifest.path voit le changement et régénère le manifest tout seul.

Configuration par variables d'environnement (voir astralnexus-mod-sync.service) :
  GITHUB_REPO      dépôt du mod (défaut Astral-Nexus-MC/Astral-launcher)
  GITHUB_TOKEN_FILE fichier contenant un token GitHub en lecture seule (dépôt privé)
  MOD_PREFIX       préfixe du jar publié dans la release (défaut astral-nexus-launcher-)
  OLD_PREFIXES     anciens préfixes du mod, séparés par ":" : leurs jars sont aussi retirés
                   (défaut astralnexus-, nom du jar avant le renommage)
  TARGET_DIRS      dossiers mods/ où installer le jar, séparés par ":"
  STATE_FILE       mémorise la dernière release vue (ETag : les réponses 304 ne comptent pas
                   dans le quota d'appels à l'API GitHub)

Bibliothèque standard Python 3 uniquement.
"""

from __future__ import annotations

import hashlib
import json
import os
import sys
import tempfile
import urllib.error
import urllib.request
from pathlib import Path

GITHUB_REPO = os.environ.get("GITHUB_REPO", "Astral-Nexus-MC/Astral-launcher")
TOKEN_FILE = os.environ.get("GITHUB_TOKEN_FILE", "/etc/modpack-tools/github-token")
MOD_PREFIX = os.environ.get("MOD_PREFIX", "astral-nexus-launcher-")
OLD_PREFIXES = [p for p in os.environ.get("OLD_PREFIXES", "astralnexus-").split(":") if p]
TARGET_DIRS = [Path(d) for d in os.environ.get("TARGET_DIRS", "/var/www/html/modpack/mods").split(":") if d]
STATE_FILE = Path(os.environ.get("STATE_FILE", "/var/lib/modpack-tools/astralnexus-mod.json"))
USER_AGENT = "astralnexus-mod-sync"


class NoRedirect(urllib.request.HTTPRedirectHandler):
    # Les assets d'un dépôt privé redirigent vers une URL S3 pré-signée qui refuse l'en-tête
    # Authorization : la redirection est suivie à la main, sans lui.
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def read_token() -> str | None:
    try:
        return Path(TOKEN_FILE).read_text(encoding="utf-8").strip() or None
    except FileNotFoundError:
        return None


def api_request(url: str, token: str | None, accept: str, etag: str | None = None) -> urllib.request.Request:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": accept})
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    if etag:
        request.add_header("If-None-Match", etag)
    return request


def download_asset(asset: dict, token: str | None) -> bytes:
    opener = urllib.request.build_opener(NoRedirect)
    request = api_request(asset["url"], token, "application/octet-stream")
    try:
        with opener.open(request, timeout=60) as response:
            return response.read()
    except urllib.error.HTTPError as error:
        if error.code not in (301, 302, 303, 307, 308):
            raise
        location = error.headers["Location"]
    plain = urllib.request.Request(location, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(plain, timeout=120) as response:
        return response.read()


def load_state() -> dict:
    try:
        return json.loads(STATE_FILE.read_text(encoding="utf-8"))
    except (FileNotFoundError, json.JSONDecodeError):
        return {}


def save_state(state: dict) -> None:
    STATE_FILE.parent.mkdir(parents=True, exist_ok=True)
    STATE_FILE.write_text(json.dumps(state, indent=2), encoding="utf-8")


def sha256_of(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def install(target_dir: Path, jar_name: str, content: bytes, expected_hash: str) -> bool:
    """Dépose le jar dans target_dir et retire les anciennes versions. True si quelque chose a changé."""
    if not target_dir.is_dir():
        print(f"Attention : {target_dir} n'existe pas, ignoré.", file=sys.stderr)
        return False

    changed = False
    destination = target_dir / jar_name
    if not destination.is_file() or sha256_of(destination) != expected_hash:
        # Fichier caché puis renommage atomique : le serveur Minecraft ou le générateur de
        # manifest ne voient jamais un jar à moitié écrit.
        fd, tmp_path = tempfile.mkstemp(dir=target_dir, prefix=".mod-sync-", suffix=".tmp")
        try:
            with os.fdopen(fd, "wb") as tmp:
                tmp.write(content)
            os.chmod(tmp_path, 0o644)
            os.replace(tmp_path, destination)
        except BaseException:
            if os.path.exists(tmp_path):
                os.unlink(tmp_path)
            raise
        print(f"{destination} installé.")
        changed = True

    old_jars = {old for prefix in [MOD_PREFIX, *OLD_PREFIXES] for old in target_dir.glob(f"{prefix}*.jar")}
    for old in sorted(old_jars):
        if old.name != jar_name:
            old.unlink()
            print(f"{old} supprimé (ancienne version).")
            changed = True

    return changed


def main() -> int:
    token = read_token()
    state = load_state()
    url = f"https://api.github.com/repos/{GITHUB_REPO}/releases/latest"

    try:
        with urllib.request.urlopen(api_request(url, token, "application/vnd.github+json", state.get("etag")), timeout=30) as response:
            release = json.load(response)
            etag = response.headers.get("ETag")
    except urllib.error.HTTPError as error:
        if error.code == 304 and all((d / state.get("jar", "")).is_file() for d in TARGET_DIRS if d.is_dir()):
            return 0  # Aucune nouvelle release, et le jar courant est bien en place.
        if error.code == 304:
            state.pop("etag", None)  # Jar absent d'un dossier : on refait un appel complet.
            save_state(state)
            return main()
        if error.code == 404:
            print(f"Aucune release trouvée sur {GITHUB_REPO} (ou dépôt privé sans token valide dans {TOKEN_FILE}).", file=sys.stderr)
            return 1
        raise

    assets = {asset["name"]: asset for asset in release.get("assets", [])}
    jars = [name for name in assets if name.startswith(MOD_PREFIX) and name.endswith(".jar")]
    if len(jars) != 1 or f"{jars[0]}.sha256" not in assets:
        print(f"Release {release.get('tag_name')} : il faut exactement un {MOD_PREFIX}*.jar et son .sha256 (trouvé : {sorted(assets)}).", file=sys.stderr)
        return 1

    jar_name = jars[0]
    expected_hash = download_asset(assets[f"{jar_name}.sha256"], token).decode("utf-8").split()[0].lower()
    content = download_asset(assets[jar_name], token)
    actual_hash = hashlib.sha256(content).hexdigest()
    if actual_hash != expected_hash:
        print(f"Empreinte invalide pour {jar_name} ({actual_hash} au lieu de {expected_hash}) : rien n'est installé.", file=sys.stderr)
        return 1

    changed = False
    for target_dir in TARGET_DIRS:
        changed |= install(target_dir, jar_name, content, expected_hash)

    save_state({"tag": release.get("tag_name"), "jar": jar_name, "etag": etag})
    print(f"{release.get('tag_name')} ({jar_name}) : {'mis à jour' if changed else 'déjà à jour'}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
