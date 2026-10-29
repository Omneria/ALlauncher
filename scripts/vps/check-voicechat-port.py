#!/usr/bin/env python3
"""Vérifie que le port UDP de Simple Voice Chat est joignable depuis l'extérieur.

Simple Voice Chat n'utilise pas le port TCP de Minecraft : la voix passe par un port UDP séparé
(24454 par défaut). Si ce port n'est pas ouvert, le mod affiche « voice chat not connected » et
personne ne s'entend. Le protocole de Simple Voice Chat ignore les paquets non authentifiés : on ne
peut pas le « pinger ». Ce script teste donc le CHEMIN réseau avec un écho UDP temporaire.

Trois commandes :

  local   (sur le VPS)  Lit voicechat-server.properties, regarde si un processus écoute déjà sur le
                        port (Linux : /proc/net/udp*) et si ufw l'autorise. Ne prouve rien sur le
                        pare-feu de l'hébergeur.

  serve   (sur le VPS)  Ouvre un écho UDP temporaire sur le port. Le serveur Minecraft doit être
                        ARRÊTÉ s'il utilise déjà ce port (sinon : « port déjà utilisé »).

  probe   (ailleurs)    Depuis un autre PC (pas le VPS), envoie des paquets UDP à HÔTE:PORT et attend
                        l'écho de `serve`. C'est le seul test qui traverse vraiment le pare-feu.

Déroulé conseillé :
  1. VPS : python3 check-voicechat-port.py local --config <.../config/voicechat/voicechat-server.properties>
  2. Arrêter le serveur de jeu, puis VPS : python3 check-voicechat-port.py serve
  3. Autre PC : python3 check-voicechat-port.py probe astralnexusmc.duckdns.org
  4. Ctrl+C sur le VPS, redémarrer le serveur de jeu.

Bibliothèque standard Python 3 uniquement. Code de sortie 0 = tout est bon, 1 = problème détecté,
2 = résultat indéterminé ou erreur d'usage.
"""

from __future__ import annotations

import argparse
import errno
import os
import re
import secrets
import shutil
import socket
import subprocess
import sys
import time
from pathlib import Path

DEFAULT_PORT = 24454
ECHO_PREFIX = b"OMNERIA-ECHO:"
MAX_DATAGRAM = 512


# --------------------------------------------------------------------------- local


def read_properties(path: Path) -> dict[str, str]:
    """voicechat-server.properties : lignes « clé=valeur », « # » pour les commentaires."""
    values: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        values[key.strip()] = value.strip()
    return values


def udp_listeners(port: int, proc_root: Path = Path("/proc")) -> list[tuple[str, str | None]]:
    """(adresse locale, processus) des sockets UDP liés à `port`. Linux uniquement.

    Le nom du processus n'est connu qu'avec les droits suffisants (root) ; None sinon.
    """
    found: list[tuple[str, int]] = []  # (adresse, inode)
    for table, family in (("net/udp", socket.AF_INET), ("net/udp6", socket.AF_INET6)):
        path = proc_root / table
        if not path.exists():
            continue
        for line in path.read_text().splitlines()[1:]:
            fields = line.split()
            if len(fields) < 10:
                continue
            address_hex, _, port_hex = fields[1].rpartition(":")
            if int(port_hex, 16) != port:
                continue
            found.append((decode_proc_address(address_hex, family), int(fields[9])))

    owners = owners_by_inode({inode for _, inode in found}, proc_root)
    return [(address, owners.get(inode)) for address, inode in found]


def decode_proc_address(address_hex: str, family: int) -> str:
    """/proc/net/udp écrit les adresses en hexadécimal, par mots de 32 bits dans l'ordre de la machine."""
    raw = bytes.fromhex(address_hex)
    words = [raw[i : i + 4][::-1] for i in range(0, len(raw), 4)]
    return socket.inet_ntop(family, b"".join(words))


def owners_by_inode(inodes: set[int], proc_root: Path) -> dict[int, str]:
    owners: dict[int, str] = {}
    if not inodes:
        return owners
    wanted = {f"socket:[{inode}]": inode for inode in inodes}
    for process in proc_root.glob("[0-9]*"):
        try:
            for fd in (process / "fd").iterdir():
                inode = wanted.get(os.readlink(fd))
                if inode is not None and inode not in owners:
                    command = (process / "cmdline").read_bytes().split(b"\0")[0].decode(errors="replace")
                    owners[inode] = f"{Path(command).name or '?'} (pid {process.name})"
        except OSError:
            continue  # processus d'un autre utilisateur ou déjà terminé
    return owners


def ufw_verdict(port: int, output: str) -> str:
    """'autorisé', 'bloqué' ou 'inactif' d'après la sortie de `ufw status`."""
    if re.search(r"^Status:\s+inactive", output, re.M | re.I):
        return "inactif"
    for line in output.splitlines():
        match = re.match(r"^\s*(\d+)(?::(\d+))?(?:/(tcp|udp))?\s+(ALLOW|DENY|REJECT|LIMIT)", line, re.I)
        if not match or not (int(match.group(1)) <= port <= int(match.group(2) or match.group(1))):
            continue
        if match.group(3) in (None, "udp", "UDP"):
            return "autorisé" if match.group(4).upper() in ("ALLOW", "LIMIT") else "bloqué"
    return "non mentionné"


def command_local(args: argparse.Namespace) -> int:
    problems = 0
    port = args.port

    if args.config:
        path = Path(args.config)
        if not path.is_file():
            print(f"[!] Fichier de configuration introuvable : {path}")
            return 2
        properties = read_properties(path)
        port = int(properties.get("port", port))
        print(f"[i] Configuration : {path}")
        print(f"    port={properties.get('port', '(absent, défaut 24454)')}  voice_host={properties.get('voice_host') or '(vide)'}  "
              f"bind_address={properties.get('bind_address') or '(vide)'}")
        if not properties.get("voice_host"):
            print("    voice_host vide : le mod annonce l'adresse par laquelle le joueur s'est connecté, "
                  "ce qui convient tant que c'est le nom de domaine du VPS.")
    else:
        print(f"[i] Pas de --config : port {port} supposé.")

    if not Path("/proc/net/udp").exists():
        print("[?] Écoute du port : vérification possible seulement sous Linux.")
    else:
        listeners = udp_listeners(port)
        if listeners:
            for address, owner in listeners:
                print(f"[ok] Un processus écoute en UDP sur {address}:{port}" + (f" — {owner}" if owner else " (processus non visible sans root)"))
            if all(address.startswith("127.") or address == "::1" for address, _ in listeners):
                print("[!] Il n'écoute que sur la boucle locale : personne d'extérieur ne pourra l'atteindre "
                      "(vérifier bind_address).")
                problems += 1
        else:
            print(f"[!] Rien n'écoute en UDP sur le port {port} : le serveur de jeu est arrêté, le mod "
                  "n'est pas chargé, ou le port configuré est différent.")
            problems += 1

    if shutil.which("ufw"):
        try:
            result = subprocess.run(["ufw", "status"], capture_output=True, text=True, timeout=10, check=False)
        except (OSError, subprocess.SubprocessError) as error:
            print(f"[?] ufw : lecture impossible ({error}).")
        else:
            if result.returncode != 0:
                print("[?] ufw : lecture impossible (relancer avec sudo).")
            else:
                verdict = ufw_verdict(port, result.stdout)
                print(f"[{'ok' if verdict in ('autorisé', 'inactif') else '!'}] ufw : port {port}/udp {verdict}.")
                if verdict in ("bloqué", "non mentionné"):
                    print(f"    Pour l'ouvrir : sudo ufw allow {port}/udp")
                    problems += 1
    else:
        print("[i] ufw absent : si un autre pare-feu (nftables, iptables, firewalld) est actif, vérifier la règle à la main.")

    print("[i] Cette commande ne voit pas le pare-feu de l'hébergeur : terminer par `serve` puis `probe`.")
    return 1 if problems else 0


# --------------------------------------------------------------------------- serve


def command_serve(args: argparse.Namespace) -> int:
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.bind((args.bind, args.port))
    except OSError as error:
        if error.errno == errno.EADDRINUSE:
            print(f"[!] Le port UDP {args.port} est déjà utilisé (probablement par le serveur Minecraft). "
                  "Arrêter le serveur de jeu, ou tester un autre port avec --port.")
        else:
            print(f"[!] Impossible d'écouter sur {args.bind}:{args.port} : {error}")
        return 1

    deadline = time.monotonic() + args.timeout
    answered = 0
    print(f"[i] Écho UDP actif sur {args.bind}:{args.port} pendant {args.timeout} s (Ctrl+C pour arrêter). "
          "Lancer maintenant `probe` depuis un autre PC.")
    try:
        while (remaining := deadline - time.monotonic()) > 0:
            sock.settimeout(min(remaining, 1.0))
            try:
                data, source = sock.recvfrom(MAX_DATAGRAM)
            except socket.timeout:
                continue
            sock.sendto(ECHO_PREFIX + data, source)
            answered += 1
            print(f"    paquet de {source[0]}:{source[1]} ({len(data)} octets) → écho envoyé")
    except KeyboardInterrupt:
        print()
    finally:
        sock.close()

    print(f"[i] Terminé : {answered} paquet(s) reçu(s).")
    print("[ok] Le port est joignable depuis l'extérieur." if answered else
          "[!] Aucun paquet reçu : le pare-feu (machine ou hébergeur) bloque probablement le port, "
          "ou `probe` visait une autre adresse.")
    return 0 if answered else 1


# --------------------------------------------------------------------------- probe


def command_probe(args: argparse.Namespace) -> int:
    try:
        address = socket.getaddrinfo(args.host, args.port, socket.AF_INET, socket.SOCK_DGRAM)[0][4]
    except socket.gaierror as error:
        print(f"[!] Adresse introuvable : {args.host} ({error})")
        return 2

    print(f"[i] Test UDP vers {address[0]}:{address[1]} ({args.tries} essai(s), {args.timeout} s chacun).")
    replies = refused = 0
    for attempt in range(1, args.tries + 1):
        nonce = secrets.token_hex(8).encode()
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.settimeout(args.timeout)
        try:
            # connect() : permet de recevoir l'erreur ICMP « port inaccessible » (ConnectionRefused/Reset).
            sock.connect(address)
            sock.send(nonce)
            data = sock.recv(MAX_DATAGRAM)
        except socket.timeout:
            print(f"    essai {attempt} : pas de réponse")
        except (ConnectionRefusedError, ConnectionResetError):
            refused += 1
            print(f"    essai {attempt} : le port répond « fermé » (rien n'y écoute, mais le paquet est arrivé)")
        except OSError as error:
            print(f"    essai {attempt} : erreur réseau ({error})")
        else:
            if data == ECHO_PREFIX + nonce:
                replies += 1
                print(f"    essai {attempt} : écho reçu")
            else:
                print(f"    essai {attempt} : réponse inattendue (autre chose écoute sur ce port ?)")
        finally:
            sock.close()

    if replies:
        print(f"[ok] {replies}/{args.tries} écho(s) : le chemin réseau est ouvert jusqu'au port {args.port}.")
        return 0
    if refused:
        print(f"[ok?] Le paquet atteint la machine, mais personne n'écoute : le pare-feu laisse passer. "
              "Lancer `serve` sur le VPS pour confirmer (un pare-feu qui REJETTE donne le même résultat).")
        return 2
    print("[!] Aucune réponse. Causes possibles : `serve` n'est pas lancé, le port est bloqué par le pare-feu de "
          "la machine ou de l'hébergeur, ou le NAT ne redirige pas l'UDP.")
    return 1


# --------------------------------------------------------------------------- main


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)

    local = commands.add_parser("local", help="vérifications sur le VPS (configuration, écoute, ufw)")
    local.add_argument("--config", help="chemin de voicechat-server.properties")
    local.add_argument("--port", type=int, default=DEFAULT_PORT)
    local.set_defaults(handler=command_local)

    serve = commands.add_parser("serve", help="écho UDP temporaire sur le VPS")
    serve.add_argument("--port", type=int, default=DEFAULT_PORT)
    serve.add_argument("--bind", default="0.0.0.0")
    serve.add_argument("--timeout", type=int, default=120, help="durée d'écoute en secondes (défaut 120)")
    serve.set_defaults(handler=command_serve)

    probe = commands.add_parser("probe", help="test depuis un autre PC")
    probe.add_argument("host")
    probe.add_argument("--port", type=int, default=DEFAULT_PORT)
    probe.add_argument("--tries", type=int, default=5)
    probe.add_argument("--timeout", type=float, default=2.0)
    probe.set_defaults(handler=command_probe)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if not 1 <= args.port <= 65535:
        print("Port invalide (1-65535).")
        return 2
    return args.handler(args)


if __name__ == "__main__":
    sys.exit(main())
