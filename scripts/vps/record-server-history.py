#!/usr/bin/env python3
"""Enregistre l'état du serveur Minecraft toutes les minutes dans server-history.json (v2.0.0).

Le launcher affiche, sur sa page SERVEUR, la disponibilité des dernières 24 h et les joueurs vus
récemment : il ne tourne pas en permanence, donc cet historique ne peut venir que du VPS.

Chaque exécution (astralnexus-server-history.timer, une fois par minute) :
  1. interroge le serveur avec le Server List Ping de Minecraft (le même que l'écran multijoueur) ;
  2. ajoute un échantillon {"t", "online", "players", "names"} au fichier ;
  3. retire les échantillons de plus de 24 h et réécrit le fichier de façon atomique.

Configuration par variables d'environnement (voir astralnexus-server-history.service) :
  SERVER_HOST   adresse du serveur (défaut 127.0.0.1)
  SERVER_PORT   port Minecraft (défaut 25565)
  OUTPUT_FILE   fichier JSON publié (défaut /var/www/html/modpack/server-history.json)
  KEEP_HOURS    durée conservée (défaut 24)

Bibliothèque standard Python 3 uniquement.
"""

from __future__ import annotations

import json
import os
import socket
import struct
import sys
import tempfile
from datetime import datetime, timedelta, timezone

HOST = os.environ.get("SERVER_HOST", "127.0.0.1")
PORT = int(os.environ.get("SERVER_PORT", "25565"))
OUTPUT_FILE = os.environ.get("OUTPUT_FILE", "/var/www/html/modpack/server-history.json")
KEEP_HOURS = int(os.environ.get("KEEP_HOURS", "24"))
TIMEOUT_SECONDS = 5


def var_int(value: int) -> bytes:
    out = bytearray()
    while True:
        part = value & 0x7F
        value >>= 7
        out.append(part | (0x80 if value else 0))
        if not value:
            return bytes(out)


def read_var_int(sock: socket.socket) -> int:
    result = 0
    for shift in range(0, 35, 7):
        byte = sock.recv(1)
        if not byte:
            raise ConnectionError("connexion fermée")
        result |= (byte[0] & 0x7F) << shift
        if not byte[0] & 0x80:
            return result
    raise ValueError("VarInt trop long")


def ping() -> tuple[bool, int, list[str]]:
    """Retourne (en ligne, joueurs connectés, pseudos connus). Ne lève jamais."""
    try:
        with socket.create_connection((HOST, PORT), timeout=TIMEOUT_SECONDS) as sock:
            sock.settimeout(TIMEOUT_SECONDS)
            host = HOST.encode()
            handshake = b"\x00" + var_int(47) + var_int(len(host)) + host + struct.pack(">H", PORT) + var_int(1)
            sock.sendall(var_int(len(handshake)) + handshake)
            sock.sendall(var_int(1) + b"\x00")

            read_var_int(sock)  # longueur du paquet
            if read_var_int(sock) != 0:
                return False, 0, []
            length = read_var_int(sock)
            data = b""
            while len(data) < length:
                chunk = sock.recv(length - len(data))
                if not chunk:
                    return False, 0, []
                data += chunk

        players = json.loads(data)["players"]
        names = [p["name"] for p in players.get("sample", []) if p.get("name")]
        return True, int(players.get("online", 0)), names
    except (OSError, ValueError, KeyError):
        return False, 0, []


def load_samples(cutoff: datetime) -> list[dict]:
    try:
        with open(OUTPUT_FILE, encoding="utf-8") as handle:
            samples = json.load(handle).get("samples", [])
    except (OSError, ValueError):
        return []
    kept = []
    for sample in samples:
        try:
            if datetime.fromisoformat(sample["t"]) > cutoff:
                kept.append(sample)
        except (KeyError, ValueError):
            continue
    return kept


def main() -> int:
    now = datetime.now(timezone.utc)
    online, players, names = ping()

    samples = load_samples(now - timedelta(hours=KEEP_HOURS))
    samples.append({"t": now.isoformat(timespec="seconds"), "online": online, "players": players, "names": names})

    document = {"updatedAt": now.isoformat(timespec="seconds"), "samples": samples}
    directory = os.path.dirname(OUTPUT_FILE) or "."
    fd, temp_path = tempfile.mkstemp(dir=directory, prefix=".server-history-", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            json.dump(document, handle, ensure_ascii=False, separators=(",", ":"))
        os.chmod(temp_path, 0o644)
        os.replace(temp_path, OUTPUT_FILE)
    except OSError as error:
        print(f"Écriture impossible : {error}", file=sys.stderr)
        try:
            os.unlink(temp_path)
        except OSError:
            pass
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
