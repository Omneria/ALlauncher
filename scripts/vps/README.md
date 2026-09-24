# Outils VPS (hors périmètre du launcher lui-même)

Ces scripts ne font pas partie de l'application C# (pas de dépendance depuis
`src/`) : ils servent à produire les fichiers que le launcher va lire côté VPS
pour le mode manifest et la bannière de maintenance. Voir le README principal,
sections "Mode manifest (synchro incrémentale)" et "Bannière de maintenance".

## Manifest (synchro incrémentale)

`generate-manifest.py` parcourt `mods/` et `config/` sous `--root`, calcule le
SHA-256 de chaque fichier et écrit le `manifest.json` attendu par
`ModpackManifest.cs`.

```bash
python3 generate-manifest.py \
    --root /var/www/html/modpack \
    --base-url https://astralnexusmc.duckdns.org/modpack \
    --output /var/www/html/modpack/manifest.json
```

> `--base-url` en `https://` une fois le VPS passé en HTTPS (section ci-dessous), en `http://`
> avant : c'est cette URL, telle qu'écrite dans le manifest, que le launcher télécharge.

**À chaque régénération, les fichiers de `mods/` absents du nouveau manifest sont supprimés chez
chaque joueur à la synchro suivante** (mode manifest, depuis v1.11.0) : c'est ce qui permet de
retirer un mod du pack. En contrepartie, ne jamais publier un manifest généré sur un dossier
`mods/` incomplet — le launcher refuse d'élaguer si le manifest ne contient aucune entrée `mods/`,
mais pas s'il en contient une partie seulement.

- `--root` : dossier contenant `mods/` et `config/` (les seuls sous-dossiers
  pris en compte — le launcher ne synchronise que ceux-là).
- `--base-url` : URL HTTP de base sous laquelle ces fichiers sont déjà servis
  statiquement (nginx/Apache). Chaque fichier est publié à
  `<base-url>/<chemin relatif>` — vérifier que ça correspond à la config du
  serveur web avant de pointer `ModpackManifestUrl` dessus.
- `--output` : où écrire `manifest.json`. Doit être accessible en HTTP à
  l'URL que vous mettrez dans `ModpackManifestUrl` (settings.json du
  launcher).

À relancer à chaque mise à jour du modpack (ajout/suppression/modification
d'un mod ou d'un fichier de config) : le launcher compare les hash au fichier
tel qu'il est au moment de la synchro, un manifest périmé ferait retélécharger
ou, pire, laisserait un fichier obsolète non détecté.

Aucune dépendance externe, Python 3 standard suffit.

## Passage en HTTPS (v1.11.0)

Depuis la v1.11.0, le launcher contacte le VPS en **HTTPS** sur le nom de domaine
(`https://astralnexusmc.duckdns.org/modpack/...`) et non plus en HTTP sur l'IP :
les mods (`.jar` exécutés sur la machine du joueur) et le manifest (leurs
empreintes) transitaient par le même canal en clair, donc l'empreinte ne
protégeait que contre la corruption, pas contre une altération volontaire.

**Le launcher n'a aucun repli en HTTP** : si le VPS ne sert plus HTTPS
(Caddy arrêté, certificat expiré), la synchro du modpack échoue avec un message
d'erreur clair. Surveiller les mails de Let's Encrypt (adresse déclarée dans
le bloc global `email` du Caddyfile) : ils préviennent avant expiration.

Le VPS ne sert plus rien en clair : Caddy redirige tout `http://` vers
`https://` sur le domaine, et ne répond plus sur l'IP brute (les launchers
v1.10.0 et antérieurs, qui l'utilisaient, ne sont plus en service).

Mise en place avec [Caddy](https://caddyserver.com) (certificat Let's Encrypt
obtenu et renouvelé tout seul) :

```bash
# 1. Installer Caddy (Debian/Ubuntu, dépôt officiel)
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update && sudo apt install -y caddy

# 2. Libérer les ports 80/443 (Caddy remplace le serveur web actuel pour ce site)
sudo systemctl disable --now nginx     # ou apache2

# 3. Déployer la config (adapter `root` si le dossier modpack n'est pas sous /var/www/html)
sudo cp Caddyfile /etc/caddy/Caddyfile
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl restart caddy

# 4. Vérifier (depuis n'importe quelle machine)
curl -I https://astralnexusmc.duckdns.org/modpack/manifest.json   # HTTP/2 200 attendu
```

Pare-feu : les ports **80 et 443** doivent être ouverts (80 sert au défi ACME
de Let's Encrypt et redirige vers 443).

Pour **garder nginx/Apache** (autres sites sur le même VPS) : les passer sur un
autre port (ex. 8080) et remplacer `root`/`file_server` dans le Caddyfile par
`reverse_proxy localhost:8080` — Caddy ne fait alors que terminer le TLS.

Une fois en place, `--base-url` du générateur de manifest doit lui aussi être
en `https://astralnexusmc.duckdns.org/modpack` : régénérer le manifest.

## Bannière de maintenance

`maintenance.txt.example` est un modèle prêt à copier :

```bash
cp maintenance.txt.example /var/www/html/modpack/maintenance.txt
# éditer la première ligne (titre), la ligne FIN: (optionnelle) et le sous-titre
```

Format lu par le launcher (`RefreshMaintenanceBannerAsync`) :
- **1ère ligne** → titre en gras dans la bannière.
- **une ligne `FIN: <valeur>`** (n'importe où, optionnelle) → affichée dans le bloc "FIN ESTIMÉE"
  à droite de la carte (`<valeur>` est un texte libre : une heure `23:00`, une date, "indéterminée"...).
  Absente = ce bloc ne s'affiche pas.
- **les autres lignes** (optionnelles) → sous-titre, affiché en dessous du titre.

Publier ce fichier à l'URL configurée dans `MaintenanceMessageUrl`
(settings.json) fait apparaître la bannière sur le dashboard au prochain
rafraîchissement (démarrage du launcher, ou dans la minute qui suit).
**Supprimer ou vider le fichier** fait disparaître la bannière (absence =
ignorée silencieusement, comme le changelog). Si le VPS ne répond plus du tout,
la bannière reste dans l'état où elle était (affichée ou non) : elle ne
disparaît pas pendant la coupure qu'elle annonce.
