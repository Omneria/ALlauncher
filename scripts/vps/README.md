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

## Réglages du pack : `pack.json` (v1.12.0)

Fichier optionnel à la racine du modpack (`/var/www/html/modpack/pack.json`), lu par
`generate-manifest.py`. Modèle prêt à copier : `pack.json.example`.

```json
{
  "forgeVersion": "47.4.23",
  "recommendedRamMb": 6144,
  "minRamMb": 4096,
  "enforcedConfigs": ["config/simple-custom-early-loading.json", "config/simple-custom-early-loading/*"],
  "clientOnlyMods": ["journeymap-*.jar", "appleskin-*.jar", "Controlling-*.jar", "Searchables-*.jar"],
  "serverOnlyMods": ["Chunky-*.jar", "AI-Improvements-*.jar", "alternate_current-*.jar", "radium-*.jar", "krypton*.jar"]
}
```

- `forgeVersion` : version de Forge installée par le launcher (v1.12.0 et plus). À changer en
  même temps que celle du serveur : plus besoin d'une nouvelle version du launcher.
- `recommendedRamMb`, `minRamMb` : affichés dans les Paramètres du launcher ; sous le minimum,
  le launcher avertit le joueur avant de lancer (une fois par session).
- `enforcedConfigs` : fichiers de `config/` **imposés** à tous les joueurs, réécrits à chaque
  lancement s'ils diffèrent (motifs style shell, chemin complet ou nom de fichier, sans tenir compte des majuscules). Tous les
  autres fichiers de `config/` sont des **réglages par défaut** : installés s'ils manquent, puis
  laissés au joueur (JourneyMap, Quark, JEI...). Pour pousser une nouvelle valeur d'une config
  par défaut à tout le monde, l'ajouter à `enforcedConfigs`.
- `serverOnlyMods` : mods utiles au serveur seul, exclus du téléchargement des joueurs (et
  supprimés chez ceux qui les avaient déjà). Ils restent dans le dossier `mods/` du serveur.
- `clientOnlyMods` (v1.13.0) : mods utiles aux joueurs seuls, jamais recopiés sur le serveur
  par `sync-server-mods.py` (section "Mods du serveur"). Ignoré par le générateur de manifest.
  Attention aux mods qui ouvrent un canal réseau, même embarqué dans leur jar : Forge refuse la
  connexion si le serveur ne l'a pas ("mismatched mod channel list"). Exemple : JEI 15.60+
  embarque MezzConfig, il doit donc être des deux côtés.

Tous les motifs sont de style shell et insensibles aux majuscules.

Les launchers antérieurs à la v1.12.0 ignorent ces réglages : ils continuent d'imposer toutes
les configs, comme avant.

## Changelog du modpack : `changelog.json` (v1.12.0)

À chaque régénération, `generate-manifest.py` compare l'ancien et le nouveau manifest et ajoute
une entrée à `changelog.json` (à côté du manifest, 30 entrées max) : mods ajoutés, mis à jour
(reconnus même si le nom du fichier change avec la version), retirés, nombre de configs
modifiées, changement de Forge. Le launcher l'affiche dans la carte ACTUS avec un badge
MODPACK : rien à annoncer à la main dans `news.txt` pour une simple mise à jour du pack.

## Régénération automatique du manifest

Plutôt que de relancer `generate-manifest.py` à chaque changement, une unité systemd
surveille `mods/`, `config/` et `pack.json` et régénère le manifest toute seule, environ 15 secondes
après la dernière modification (le temps qu'un upload se termine : un `.jar` indexé à
moitié copié serait distribué tronqué à tous les joueurs). Elle rend aussi les fichiers
lisibles par Caddy, ce qui évite l'oubli du `chmod 644`.

Installation, une seule fois (depuis une copie de ce dossier sur le VPS) :

```bash
mkdir -p /opt/modpack-tools
cp generate-manifest.py update-manifest.sh /opt/modpack-tools/
chmod +x /opt/modpack-tools/update-manifest.sh
cp modpack-manifest.path modpack-manifest.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now modpack-manifest.path
```

Ensuite, déposer, remplacer ou supprimer un fichier dans `mods/` suffit. Pour suivre ce
qui se passe :

```bash
journalctl -u modpack-manifest.service -n 20 --no-pager
```

Limite : les sous-dossiers ne sont pas surveillés (ex. `config/create/...`). Après un
changement à cet endroit, lancer la régénération à la main :
`systemctl start modpack-manifest.service`.

Adresse, dossier ou délai différents : variables `MODPACK_ROOT`, `BASE_URL`,
`TOOLS_DIR` et `QUIET_SECONDS` de `update-manifest.sh`, à surcharger avec une ligne
`Environment=` dans `modpack-manifest.service`.

## Mods du serveur : un seul dossier source (v1.13.0)

`sync-server-mods.py` recopie `modpack/mods/` vers le dossier `mods/` du serveur Minecraft, sans
les `clientOnlyMods` de `pack.json`. On dépose donc un mod **une seule fois**, dans le modpack :
les joueurs le reçoivent par le manifest, le serveur par ce script.

- Copie seulement ce qui a changé (empreinte SHA-256), en prenant le propriétaire du dossier
  `mods/` du serveur (inutile de refaire le `chown`).
- Retire du serveur les mods qu'il y a lui-même installés puis qui ont quitté le modpack, ainsi
  que les mods client seul qui s'y trouvent sous le même nom que dans le modpack. **Un mod
  ajouté à la main sur le serveur, absent du modpack, n'est jamais touché.**
- Ne fait rien si `modpack/mods/` est vide, pour ne pas vider le serveur par erreur.
- Ne redémarre pas le serveur : les changements s'appliquent à son prochain redémarrage.

Installation, une seule fois :

```bash
cp sync-server-mods.py update-manifest.sh /opt/modpack-tools/
chmod +x /opt/modpack-tools/update-manifest.sh

# Aperçu de ce qui changerait, sans rien modifier
python3 /opt/modpack-tools/sync-server-mods.py --dry-run \
    --modpack-root /var/www/html/modpack \
    --server-mods /var/opt/minecraft/crafty/crafty-4/servers/<id-du-serveur>/mods

# Automatique à chaque changement du modpack : décommenter la ligne
# Environment=SERVER_MODS_DIR=... de modpack-manifest.service (avec le bon chemin), puis
cp modpack-manifest.service /etc/systemd/system/
systemctl daemon-reload
systemctl start modpack-manifest.service
journalctl -u modpack-manifest.service -n 30 --no-pager
```

## Mise à jour automatique du mod Astral Nexus

Le mod client Astral Nexus (dépôt privé `Astral-Nexus-MC/Astral-launcher`) publie une release
GitHub à chaque tag `vX.Y.Z` (workflow `release.yml` de ce dépôt : jar + `.sha256`).
`sync-astralnexus-mod.py`, lancé toutes les 5 minutes par un timer systemd, installe la
dernière release (`astral-nexus-launcher-<version>.jar`) dans `modpack/mods/` et supprime
l'ancienne version (seuls les `astral-nexus-launcher-*.jar` sont touchés). Le manifest est ensuite
régénéré par `modpack-manifest.path` (section précédente) : les joueurs reçoivent la nouvelle
version à leur prochain clic sur JOUER, sans rien déposer à la main.

Publier une nouvelle version du mod :

1. passer `mod_version` à la nouvelle version dans `gradle.properties`, committer, pousser ;
2. créer le tag correspondant (`v1.0.1` pour `mod_version=1.0.1`) : le workflow refuse un tag
   qui ne correspond pas.

Le mod ne s'exécute que côté client (écrans de titre et de déconnexion) et n'ouvre aucun canal
réseau : le serveur Minecraft n'a pas besoin de l'avoir. S'il doit quand même l'avoir, ajouter
son dossier `mods/` à `TARGET_DIRS` dans `astralnexus-mod-sync.service`, séparé par `:`. Le
serveur ne charge le nouveau jar qu'au redémarrage suivant, et le script ne redémarre rien.
Avec `sync-server-mods.py` (section précédente), `TARGET_DIRS` n'a besoin que du dossier du
modpack : c'est lui qui décide ensuite si le serveur l'a (`clientOnlyMods`).

Installation, une seule fois :

```bash
# Token GitHub en lecture seule (le dépôt est privé) : github.com > Settings > Developer settings
# > Fine-grained tokens, dépôt Astral-Nexus-MC/Astral-launcher uniquement, permission
# "Contents: Read-only".
mkdir -p /etc/modpack-tools
nano /etc/modpack-tools/github-token      # coller le token, enregistrer
chmod 600 /etc/modpack-tools/github-token

cp sync-astralnexus-mod.py /opt/modpack-tools/
chmod +x /opt/modpack-tools/sync-astralnexus-mod.py
cp astralnexus-mod-sync.service astralnexus-mod-sync.timer /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now astralnexus-mod-sync.timer

# Premier passage tout de suite, puis vérification
systemctl start astralnexus-mod-sync.service
journalctl -u astralnexus-mod-sync.service -n 20 --no-pager
```

Tant qu'aucune release n'existe, le journal affiche "Aucune release trouvée" : c'est normal.
Même message avec une release publiée : le token est absent, expiré ou n'a pas accès au dépôt.
Un jar dont l'empreinte ne correspond pas au `.sha256` de la release n'est jamais installé.

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

Depuis la v1.13.0, le Caddyfile ne sert sous `/modpack/` que ce que lit le launcher (`mods/`,
`config/`, `manifest.json`, `changelog.json`, `changelog.txt`, `news.txt`, `maintenance.txt`) :
tout autre fichier déposé là (script, `pack.json`, ancienne archive, sauvegarde) répond 404. Les
fichiers cachés (`.part`, temporaires) ne sont jamais servis.

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
