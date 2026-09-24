# Launcher Minecraft personnalisé

Launcher WPF (.NET / C#) pour un serveur Minecraft privé (8 joueurs max), basé sur
[CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) et
[CmlLib.Core.Installer.Forge](https://github.com/CmlLib/CmlLib.Core.Installer.Forge).

## Contexte serveur

- Minecraft **1.20.1**, Forge **47.3.0** (migré depuis 1.16.5/Forge 36.2.34, voir `MinecraftVersion`/`ForgeVersion` dans `LauncherSettings.cs`)
- Liste de mods gérée côté VPS (voir manifeste ci-dessous) — plus de liste figée dans ce README depuis la migration
- **Java 17 obligatoire** (Minecraft ne démarre plus sur un JRE antérieur à 17 depuis la 1.18, et Forge 1.20.1 l'exige explicitement)
- Mods/config hébergés sur un VPS perso, accessibles en HTTP direct (voir manifeste ci-dessous)

## Ce que fait le launcher

1. Vérifie/installe Java 17 (build Temurin/Adoptium si absent) — **implémenté**
2. Installe Forge 1.20.1-47.3.0 via CmlLib.Core.Installer.Forge — **implémenté**
3. Synchronise `mods/` et `config/` depuis le VPS (par hash, pas à chaque lancement), affiche un
   changelog optionnel quand une mise à jour est détectée — **implémenté**
4. Authentifie via OAuth Microsoft direct (navigateur système, sans dépendre du launcher officiel)
   puis la chaîne Xbox Live → XSTS → Minecraft — **implémenté et approuvé par Microsoft**
5. Lance le jeu avec le bon classpath Forge, la RAM/résolution configurées, en masquant la console
   Java — **implémenté**
6. Vérifie au démarrage si une nouvelle version du launcher est disponible (GitHub Releases) et
   propose de l'installer — **implémenté**
7. Affiche le statut du serveur (en ligne/hors ligne, joueurs connectés) avant de jouer —
   **implémenté**

Pas de gestion multi-comptes : usage privé entre amis, un seul compte par machine.

> **Statut (2026-10) :** l'authentification OAuth Microsoft directe (MSAL.NET, navigateur système +
> Xbox Live/XSTS, sans dépendre du launcher officiel) a été testée en conditions réelles et
> fonctionne : l'application Azure AD du groupe est approuvée par Microsoft. Si l'app venait à
> perdre son approbation ou à devoir être recréée, l'ancien mode de secours (lecture de la session
> du launcher officiel via
> `launcher_accounts.json`) reste disponible dans l'historique git (commit `b97b6fa` et avant) et
> peut être restauré temporairement.

> **Migration 1.20.1 :** le serveur et le modpack sont passés de 1.16.5/Forge 36.2.34 à
> 1.20.1/Forge 47.3.0 (`MinecraftVersion`/`ForgeVersion` dans `LauncherSettings.cs`) — Java 8 n'est
> plus utilisable, `JavaManager` installe désormais Java 17 (Minecraft ne démarre plus sur un JRE
> antérieur à 17 depuis la 1.18). Un joueur passant d'une ancienne version du launcher verra son
> Java 8 portable (`runtime/java8`) ignoré au profit d'un Java 17 fraîchement téléchargé
> (`runtime/java17`) — l'ancien dossier n'est pas supprimé automatiquement, il peut être nettoyé à
> la main si besoin. `SettingsManager` migre aussi automatiquement `MinecraftVersion`/`ForgeVersion`
> dans un `settings.json` existant qui contenait encore les anciennes valeurs par défaut exactes
> (`1.16.5`/`36.2.34`) — un joueur ayant délibérément configuré une autre version garde son choix.

## Structure du repo

```
├── MinecraftLauncherPerso.sln
├── src/
│   └── MinecraftLauncherPerso/
│       ├── MinecraftLauncherPerso.csproj   # net8.0-windows, WPF, CmlLib.Core + Installer.Forge
│       │                                   # AssemblyName "AL Launcher", pas de .pdb, icône embarquée
│       ├── App.xaml(.cs)                   # bootstrap : ouvre SplashWindow au démarrage
│       ├── SplashWindow.xaml(.cs)          # écran de démarrage (logo animé), puis ouvre MainWindow
│       ├── MainWindow.xaml(.cs)            # UI + orchestration Java → Forge → Sync → Auth → Lancement
│       ├── SettingsWindow.xaml(.cs)        # fenêtre de paramètres (RAM, résolution, dossier de jeu, maintenance)
│       ├── LegalWindow.xaml(.cs)           # mentions légales + dépendances open source
│       ├── WelcomeWindow.xaml(.cs)         # assistant de premier lancement (v1.8.0)
│       ├── LogViewerWindow.xaml(.cs)       # visualiseur de logs intégré (v1.8.0, Paramètres)
│       ├── AppTheme.xaml                   # charte graphique (couleurs, polices, styles de contrôles)
│       ├── Assets/
│       │   ├── Fonts/                      # Chakra Petch / Inter / JetBrains Mono (OFL), embarquées
│       │   └── Images/                     # logo Omnéria, crest Astral Nexus, icône .ico du launcher
│       ├── Models/
│       │   ├── LauncherSettings.cs         # préférences persistées (RAM, dossier de jeu, URL modpack...)
│       │   ├── JavaVersionInfo.cs
│       │   └── JavaSetupProgress.cs
│       └── Services/
│           ├── Java/                       # détection + installation Java 17
│           │   ├── IJavaManager.cs
│           │   ├── JavaManager.cs
│           │   └── AdoptiumApiClient.cs
│           ├── Forge/                      # installation Forge (CmlLib.Core.Installer.Forge)
│           │   ├── IForgeManager.cs
│           │   └── ForgeManager.cs
│           ├── ModSync/                    # synchro du modpack : zip unique (ETag) ou manifest par fichier (v1.9.0)
│           │   ├── IModSyncService.cs
│           │   ├── ModSyncService.cs
│           │   └── ModpackManifest.cs      # format du manifest incrémental (chemin -> URL + sha256)
│           ├── Auth/                       # OAuth Microsoft direct (MSAL.NET) -> Xbox Live -> XSTS -> Minecraft
│           │   ├── IAuthService.cs
│           │   └── MicrosoftAuthService.cs
│           ├── Launch/                     # construction + démarrage du process Forge/Minecraft
│           │   ├── IGameLauncher.cs
│           │   ├── GameLauncher.cs
│           │   └── ServerListWriter.cs     # verrouille servers.dat sur le serveur configuré
│           ├── Status/                     # ping du serveur (en ligne/hors ligne, joueurs)
│           │   ├── IServerStatusService.cs
│           │   └── ServerStatusService.cs
│           ├── Update/                     # vérification/installation des mises à jour du launcher
│           │   ├── IUpdateService.cs
│           │   └── GitHubUpdateService.cs
│           ├── News/                       # actus optionnelles (news.txt à côté du modpack)
│           │   ├── INewsService.cs
│           │   ├── NewsService.cs
│           │   └── NewsHistoryStore.cs     # historique local des actus (news.txt lui-même n'en garde aucun)
│           ├── Changelog/                  # changelog du launcher (releases GitHub, pas un fichier VPS)
│           │   ├── IReleaseChangelogService.cs
│           │   ├── ReleaseChangelogService.cs
│           │   └── ReleaseChangelogEntry.cs
│           ├── Notifications/
│           │   └── DesktopNotificationService.cs  # bulle Windows native (NotifyIcon), ex. serveur de retour en ligne
│           ├── Maintenance/                # bannière de maintenance optionnelle (v1.9.0)
│           │   ├── IMaintenanceService.cs
│           │   └── MaintenanceService.cs
│           ├── Hardware/                    # RAM totale de la machine (P/Invoke GlobalMemoryStatusEx)
│           │   └── SystemInfo.cs
│           ├── Http/
│           │   └── SharedHttpClient.cs     # HttpClient unique partagé par tous les services HTTP (évite l'épuisement des sockets)
│           ├── Configuration/
│           │   └── SettingsManager.cs      # charge/sauvegarde settings.json (écriture atomique, validation)
│           └── Diagnostics/
│               ├── Logger.cs               # journal fichier (launcher.log) pour les échecs "avalés"
│               ├── CrashDiagnosisService.cs # diagnostic best-effort d'un crash du jeu (v1.8.0)
│               └── DiskSpaceChecker.cs     # vérification d'espace disque avant un téléchargement
├── tests/
│   └── MinecraftLauncherPerso.Tests/       # xUnit : VarInt, NBT servers.dat, parsing versions, SettingsManager
├── RELEASE_NOTES.md                        # notes de la prochaine release, en langage clair pour les joueurs
├── README.md
└── .gitignore
```

Chaque responsabilité (Java, Forge, sync mods, auth, lancement) est isolée dans son propre
service derrière une interface (`IJavaManager`, `IForgeManager`, `IModSyncService`,
`IAuthService`, `IGameLauncher`), injectées dans `MainWindow` qui orchestre l'enchaînement complet
au clic sur "Jouer".

## Logique de vérification/installation de Java 17

Fichier : `src/MinecraftLauncherPerso/Services/Java/JavaManager.cs`

Ordre de résolution dans `EnsureJavaAsync` :

1. **Java 17 portable déjà installé par ce launcher** : recherche récursive d'un exécutable
   `java(.exe)` sous `%AppData%/MinecraftLauncherPerso/runtime/java17`, validé en exécutant
   `java -version` et en vérifiant que la version majeure vaut bien 17.
2. **Java 17 déjà présent sur la machine** : `JAVA_HOME`, `java` sur le `PATH`, les dossiers
   d'installation courants sous Windows (`Program Files\Java`, `...\Eclipse Adoptium`,
   `...\AdoptOpenJDK`), puis le **registre Windows** (`HKLM\SOFTWARE\JavaSoft\...`,
   `...\Eclipse Adoptium\...`, `...\Eclipse Foundation\...`, vues 64 et 32 bits) — tout installeur
   Java officiel s'y enregistre, ce qui rattrape une installation faite dans un dossier non
   standard que le scan de dossiers seul manquerait.
3. **Téléchargement automatique** : si aucun Java 17 valide n'est trouvé, interrogation de l'API
   [Adoptium](https://api.adoptium.net) (`/v3/assets/latest/17/hotspot`) pour récupérer la dernière
   build Temurin 17 (JRE) correspondant à l'OS/architecture de la machine, téléchargement avec
   suivi de progression, puis extraction dans le dossier `runtime/java17` ci-dessus.
   `AdoptiumApiClient.GetLatestJreAsync` prend la version majeure en paramètre (17 ici) plutôt que
   d'être câblé en dur sur Java 8, pour rester réutilisable si une future version de Minecraft exige
   encore un autre Java.

La détection de version parse la sortie de `java -version` (`version "1.8.0_392"` →
version majeure 8 ; `version "17.0.9"` → version majeure 17), ce qui permet de rejeter tout
Java déjà installé qui ne serait pas une version 17, même si un JDK plus ancien ou plus récent est présent.

## Installation de Forge

Fichier : `src/MinecraftLauncherPerso/Services/Forge/ForgeManager.cs`

Utilise le package `CmlLib.Core.Installer.Forge` :
`ForgeInstaller.Install(minecraftVersion, forgeVersion, options)` installe/mappe le profil de
version composé (vanilla + Forge) et retourne son identifiant (ex. `1.20.1-forge-47.3.0`).
Ce mapping seul ne télécharge pas les fichiers de la version : `MinecraftLauncher.InstallAsync`
est appelé juste après pour installer réellement le jar, les libs et les assets vanilla dont
Forge dépend.

## Synchronisation mods/config (VPS)

Fichier : `src/MinecraftLauncherPerso/Services/ModSync/ModSyncService.cs`

**Deux modes**, selon que `ModpackManifestUrl` est configuré ou non dans `settings.json` :

- **Mode manifest (incrémental, recommandé, v1.9.0)** : voir la section dédiée ci-dessous. Activé
  **par défaut** depuis le modpack 1.20.1 (`ModpackManifestUrl` pointe vers le
  `manifest.json` publié sur le VPS, généré par `scripts/vps/generate-manifest.py`).
- **Mode zip (historique)** : décrit dans le reste de cette section, utilisé uniquement si
  `ModpackManifestUrl` est explicitement vidé dans `settings.json`.

Le launcher pointe sur une archive `.zip` unique du pack complet, hébergée sur le VPS
(`ModpackZipUrl`), qui doit
contenir `mods/` et `config/` **à sa racine** (mêmes noms de dossiers qu'un `.minecraft`
classique) : à chaque mise à jour du pack, remplacez ce zip sur le VPS.

Avant de (re)télécharger, le launcher envoie une requête HTTP `HEAD` sur cette URL et compare
`ETag`/`Last-Modified`/`Content-Length` à la dernière synchro réussie (mise en cache localement
dans `{GameDirectory}/launcher-modpack-cache.json`) : si rien n'a changé côté serveur, il ne
retélécharge pas à chaque lancement. Si le serveur ne renvoie pas ces en-têtes (ou ne supporte pas
`HEAD`), le launcher retélécharge par prudence plutôt que d'échouer. Le zip est ensuite extrait
directement dans `GameDirectory`, en écrasant les fichiers existants.

**Changelog optionnel :** quand une mise à jour du modpack est détectée (avant de télécharger le
nouveau zip), le launcher tente de récupérer `changelog.txt` dans le même dossier que le zip sur
le VPS (ex. si `ModpackZipUrl` est `.../modpack/Omneria-modded.zip`, il cherche
`.../modpack/changelog.txt`) et en affiche le contenu ligne par ligne dans le journal de statut.
Fichier entièrement optionnel : absent (404) ou VPS injoignable, le launcher l'ignore
silencieusement et continue la synchro normalement.

**Vérification d'intégrité optionnelle :** même quand le cache ETag dit "à jour" (pas de
retéléchargement prévu), le launcher vérifie les fichiers déjà extraits contre un éventuel
`manifest.json` hébergé au même endroit (mapping `"chemin relatif": "sha256 hexadécimal"`). Un
fichier manquant ou dont le hash ne correspond plus (corruption disque, modification manuelle
accidentelle d'un mod) force un retéléchargement complet du zip, même si l'ETag n'a pas changé.
Comme pour le changelog, absence du manifest = aucune vérification, pas d'erreur.

**Vraie barre de progression, pas un indicateur indéterminé :** `SyncAsync` accepte désormais un
second `IProgress<double>` (fraction 0.0-1.0, en plus du message texte existant) rapporté pendant
le téléchargement effectif du zip — `MainWindow` l'utilise pour piloter `ProgressBar.Value`
directement plutôt que de basculer sur une barre indéterminée le temps de toute l'étape (même
principe pour Forge, dont CmlLib exposait déjà cette progression en octets, jusqu'ici seulement
transformée en texte).

**Indicateur de dernière synchro :** `GetLastSyncedAt` lit l'horodatage `syncedAt` du cache local
(mis à jour à chaque `SyncAsync`, que le modpack ait été retéléchargé ou juste confirmé à jour)
sans requête réseau — affiché en bas de la barre latérale ("SYNCHRO : IL Y A X MIN"), rafraîchi au
démarrage et après chaque tentative de lancement.

**Téléchargement reprenable :** le zip du modpack peut faire plusieurs centaines de Mo ; une
coupure réseau (VPS instable, fermeture du launcher en pleine synchro) forçait auparavant à tout
retélécharger depuis le début. Le fichier en cours de téléchargement est maintenant écrit dans un
`.part` temporaire à un chemin stable (dérivé d'un hash SHA-256 de l'URL, retrouvable même après
redémarrage du launcher) plutôt que dans le fichier final directement. À la tentative suivante, si
ce `.part` existe déjà, le launcher reprend avec un en-tête HTTP `Range: bytes={taille}-`, protégé
par `If-Range` sur l'ETag connu : si le serveur ignore `Range` (renvoie `200` complet au lieu de
`206 Partial Content`) ou si le contenu a changé côté serveur entretemps (ETag différent), le
`.part` est abandonné et le téléchargement repart intégralement de zéro plutôt que de produire un
zip corrompu par concaténation de deux versions différentes.

**Préchargement en arrière-plan (v1.8.0) :** `IModSyncService.PrefetchAsync`, appelé dès
`MainWindow_Loaded` (best-effort, silencieux), vérifie si une mise à jour du modpack est disponible
et, si oui, commence à la télécharger tout de suite plutôt que d'attendre le clic sur "JOUER" —
directement dans le même fichier `.part` que `SyncAsync` (chemin partagé, dérivé du hash de l'URL,
voir ci-dessus), pour qu'un clic sur "JOUER" trouve le téléchargement déjà fait (ou bien avancé) au
lieu de repartir de zéro. Si "JOUER" est cliqué pendant que ce préchargement tourne encore,
`MainWindow` l'annule et attend sa fin avant de démarrer la vraie synchro, pour ne jamais avoir deux
écritures simultanées sur le même fichier `.part`.

**Réparation rapide (v1.8.0, Paramètres → Maintenance) :** `IModSyncService.RepairAsync` supprime le
cache ETag local puis relance une synchro complète, forçant un retéléchargement même si le contenu
distant n'a pas changé. Utile quand c'est un fichier *local* qui a été corrompu ou supprimé par
erreur (le cache ETag dirait alors "à jour" à tort, `SyncAsync` seul ne retéléchargerait rien).
Accessible manuellement via un bouton dans les Paramètres, ou proposé directement après un crash du
jeu si le diagnostic (voir "Lancement du jeu" ci-dessus) l'identifie comme cause probable. En mode
manifest, "Réparer" et une synchro normale font exactement la même chose (voir ci-dessous).

### Mode manifest (synchro incrémentale, v1.9.0)

Fichier : `Services/ModSync/ModpackManifest.cs`

Configurer `ModpackManifestUrl` (settings.json) bascule `SyncAsync` sur un mode par fichier plutôt
que par zip unique : au lieu de retélécharger tout le pack dès qu'un seul mod change, seuls les
fichiers dont le hash diffère du manifest sont retéléchargés.

**Format du manifest** (JSON, hébergé sur le VPS à l'URL configurée) :

```json
{
  "files": {
    "mods/create-0.5.1.jar": {
      "url": "https://vps/modpack/files/mods/create-0.5.1.jar",
      "sha256": "3a7bd3e2360a...",
      "size": 8421376
    },
    "config/create.toml": {
      "url": "https://vps/modpack/files/config/create.toml",
      "sha256": "1b2c3d4e5f6a..."
    }
  }
}
```

`url` est une URL absolue par fichier (peut pointer n'importe où, un CDN par exemple, pas
nécessairement à côté du manifest) ; `sha256` est comparé au hash local pour décider si le fichier
doit être retéléchargé ; `size` est optionnel, utilisé uniquement pour dimensionner la barre de
progression globale (le total des tailles des fichiers à mettre à jour).

À chaque synchro, le launcher télécharge un `GET` sur `ModpackManifestUrl`, calcule le SHA-256 de
chaque fichier local correspondant à une entrée du manifest et ne retélécharge que ceux qui
manquent ou dont le hash diffère — **auto-réparateur par construction** : un fichier corrompu ou
supprimé par erreur est détecté et retéléchargé à la prochaine synchro, sans action de l'utilisateur
ni notion de cache "à jour" à invalider (contrairement au mode zip, où le cache ETag doit être
explicitement supprimé par `RepairAsync` pour forcer une revérification).

**Générer ce manifest côté VPS** est hors du périmètre de ce dépôt (script à écrire côté serveur :
parcourir `mods/`/`config/`, calculer un SHA-256 par fichier, publier le JSON à une URL stable) —
ce launcher se contente de le consommer.

### Retour en arrière d'un cran (v1.9.0, Paramètres → Maintenance)

Avant d'écraser `mods/`/`config/` (mode zip ou manifest, `BackupCurrentModpackAsync`), le contenu
courant est copié dans `.rollback-mods`/`.rollback-config` à la racine du dossier de jeu, écrasant
la sauvegarde précédente — **un seul cran de recul**, pas un historique complet. "REVENIR EN
ARRIÈRE" (désactivé tant qu'aucune sauvegarde n'existe) restaure ce contenu.

**Effet volontairement temporaire, pas un vrai pin de version** : le cache de synchro n'est pas
touché par ce rollback. En mode zip, si le contenu distant n'a pas changé depuis, une synchro
normale ultérieure considère toujours "à jour" via le cache ETag et laisse le rollback en place ;
en mode manifest en revanche, une synchro normale revérifie systématiquement chaque fichier par
hash et retélécharge/annule le rollback tant que le VPS sert toujours la version problématique —
une vraie protection durable demanderait que le VPS lui-même serve une version antérieure. Une
sauvegarde d'`options.txt` (réglages perso du joueur) encadre aussi "RÉPARER LE MODPACK", en filet
de sécurité même si ni le mode zip ni le mode manifest ne sont censés y toucher.

### Vérification d'espace disque

Fichier : `Services/Diagnostics/DiskSpaceChecker.cs`

Avant tout téléchargement volumineux (modpack, JRE Temurin), le launcher vérifie l'espace disque
disponible et échoue avec un message clair (`Espace disque insuffisant pour...`) plutôt que de
laisser un `IOException` générique survenir en pleine extraction. Marge large par prudence (x3 en
mode zip pour le zip temporaire + l'extraction + la sauvegarde de rollback qui coexistent
brièvement, x2 en mode manifest, 500 Mo fixes pour Java faute de connaître la taille exacte de
l'archive à l'avance) : mieux vaut refuser une mise à jour qui aurait en réalité tenu de justesse
que laisser un disque presque plein dans un état corrompu.

## Actus (news)

Fichiers : `Services/News/NewsService.cs`, `Services/News/NewsHistoryStore.cs`, `MainWindow.xaml.cs`

Au démarrage puis toutes les 5 minutes (`DispatcherTimer`), le launcher tente de récupérer
`news.txt` (même convention que `changelog.txt` : même dossier que le zip du modpack sur le VPS) —
pratique pour annoncer un event, une maintenance prévue, etc. sans passer par Discord. Optionnel,
silencieux si absent : la carte ACTUS (colonne gauche du tableau de bord, au-dessus de la carte
CHANGELOG — voir section suivante) affiche un texte par défaut ("Aucune actualité pour le moment.")
tant qu'aucun `news.txt` n'est disponible, plutôt que de disparaître entièrement (la mise en page
suppose sa présence).

**Historique, pas juste la dernière actu :** `news.txt` côté VPS est un simple fichier "à plat" —
il ne garde lui-même aucun historique, chaque requête ne renvoie que son contenu actuel. Pour
afficher un historique malgré tout, `NewsHistoryStore` horodate et conserve localement
(`%AppData%/MinecraftLauncherPerso/news-history.json`, plafonné à 20 entrées) chaque contenu
distinct observé, dédupliqué sur les rafraîchissements consécutifs identiques : la carte affiche
la liste complète, la plus récente en tête, plutôt que de se contenter d'écraser le texte affiché.

## Changelog (releases GitHub du launcher)

Fichier : `Services/Changelog/ReleaseChangelogService.cs`

Carte CHANGELOG (colonne gauche du tableau de bord, sous ACTUS — jusqu'ici les deux étaient
fusionnées dans une seule carte "ACTUS & CHANGELOG" qui n'affichait en réalité que les actus).
Chargée une fois au démarrage : `GET
https://api.github.com/repos/Omneria/ALlauncher/releases` (4 dernières releases non-draft/pré-
release), **même source et mêmes règles de nettoyage des notes que la carte "Changelog" de la
landing page** (`Omneria-landing/index.html`) — pas un fichier `changelog.txt` séparé à maintenir
côté VPS, contrairement au changelog *du modpack* (voir "Synchronisation mods/config" ci-dessus,
sans rapport avec celui-ci qui concerne le launcher lui-même).

**Notes nettoyées à partir du corps de la release** (texte généré par `gh release create
--generate-notes`) : seules les lignes `- ...`/`* ...` sont gardées, liens Markdown/gras/backticks
retirés, mentions `by @user in <url>` et URLs brutes supprimées, 4 notes max par release — un
message par défaut ("Voir les notes de version sur GitHub.") si le nettoyage ne laisse rien. Les
deux implémentations (launcher en C#, landing page en JS) doivent rester alignées si l'une des deux
règles de nettoyage change.

Best-effort comme les actus : une requête échouée (GitHub indisponible, rate-limit non authentifié)
laisse la carte sur son dernier contenu affiché plutôt que de la vider, avec un texte par défaut
uniquement au tout premier chargement.

## Authentification (OAuth Microsoft direct)

Fichier : `src/MinecraftLauncherPerso/Services/Auth/MicrosoftAuthService.cs`

Flux MSAL.NET interactif, navigateur système (pas de WebView2, pas de code à recopier — même
expérience que CurseForge/Paladium) :

1. `PublicClientApplicationBuilder` (autorité `consumers`, redirection `http://localhost`) tente
   d'abord un `AcquireTokenSilent` sur un compte déjà en cache (`msal-cache-v2.bin` dans
   `%AppData%/MinecraftLauncherPerso/`, persisté via `Microsoft.Identity.Client.Extensions.Msal`
   — package officiel MSAL, chiffrement DPAPI natif sur Windows — plutôt qu'une sérialisation
   manuelle) ; en cas d'échec/expiration, ouvre le navigateur par défaut pour une connexion
   interactive (`AcquireTokenInteractive`, scopes `XboxLive.signin` + `offline_access`). Une fois
   connecté une première fois, les lancements suivants renouvellent la session en silence tant que
   le refresh token Microsoft reste valide (habituellement des mois).

   **Reconnexion automatique à l'écran :** `IAuthService.TryGetCachedSessionAsync` (appelée au
   chargement de `MainWindow`) tente cette même reconnexion silencieuse — jamais de navigateur
   ouvert, retourne `null` sans erreur si aucun compte n'est en cache ou si le token a
   expiré/été révoqué — pour réafficher automatiquement le profil connecté (avatar + pseudo) au
   redémarrage du launcher, au lieu de faire réapparaître le bouton `SE CONNECTER` alors que la
   session Microsoft elle-même était toujours valide en arrière-plan.
2. Échange le token Microsoft contre un token Xbox Live (`user.auth.xboxlive.com/user/authenticate`).
3. Autorise ce token via XSTS (`xsts.auth.xboxlive.com/xsts/authorize`, `RelyingParty` Minecraft
   Services) — les erreurs `XErr` connues (pas de compte Xbox, région non supportée, vérification
   d'âge requise, compte enfant non rattaché à une famille) sont traduites en messages explicites.
4. Échange le token XSTS contre un access token Minecraft (`login_with_xbox`), puis récupère le
   profil (`pseudo`/`UUID`) via `api.minecraftservices.com/minecraft/profile`.

Chaque étape rapporte sa progression (`IProgress<string>`, affiché dans le journal de statut de
l'UI), et un échec HTTP à n'importe quelle étape lève une erreur nommant l'étape et incluant le
corps de la réponse (au lieu d'un `403 (Forbidden)` générique) — utile pour diagnostiquer un
`login_with_xbox` bloqué par l'approbation Azure AD en attente (voir note en tête de README).

Le `MicrosoftClientId` utilisé (`LauncherSettings.MicrosoftClientId`) identifie l'application
Azure AD partagée par tout le groupe — un seul ID pour tous, chaque joueur se connecte ensuite
avec son propre compte Microsoft. Pour référence, si ce Client ID doit un jour être recréé
(compte Azure changé, app supprimée...) :

1. [portal.azure.com](https://portal.azure.com) → **Azure Active Directory** (ou **Microsoft
   Entra ID**) → **App registrations** → **New registration**.
2. Type de compte : "Comptes dans n'importe quel annuaire d'organisation et comptes Microsoft
   personnels" (nécessaire pour les comptes Xbox/Minecraft grand public).
3. **Authentication** → **Add a platform** → **Mobile and desktop applications** → cocher
   `http://localhost` (redirect URI utilisé par MSAL en boucle locale).
4. Copier l'**Application (client) ID** dans `LauncherSettings.MicrosoftClientId`.
5. Si Minecraft renvoie `403 "Invalid app registration"` sur `login_with_xbox` : soumettre le
   formulaire d'approbation Microsoft (https://aka.ms/mce-reviewappid) et attendre la validation.

## Lancement du jeu

Fichier : `src/MinecraftLauncherPerso/Services/Launch/GameLauncher.cs`

Construit une `MSession` (CmlLib.Core.Auth) à partir de la session lue ci-dessus, un
`MLaunchOption` à partir de `LauncherSettings` (RAM, serveur, résolution — voir ci-dessous), puis
appelle `MinecraftLauncher.BuildProcessAsync(versionId, options)`. Le process est ensuite démarré
manuellement (au lieu du `ProcessWrapper.StartWithEvents()` fourni par CmlLib.Core, qui force
`CreateNoWindow=false`) avec `CreateNoWindow=true` : sans ça, une fenêtre de console Windows
s'ouvrait pour `java.exe` (application console sans parent console attaché) en plus de la fenêtre
du launcher. Le launcher ne bloque pas en attendant la fermeture du jeu : le bouton "Jouer"
redevient disponible dès que le process a démarré, et les logs du jeu remontent dans le journal de
statut tant que la fenêtre reste ouverte.

**Crash du jeu :** `GameLauncher` expose un événement `GameExited(exitCode)` (déclenché par
`Process.Exited`, sur un thread d'arrière-plan). Si le code de sortie est non nul, `MainWindow`
affiche un bouton "VOIR LES LOGS" qui ouvre `{GameDirectory}/logs/latest.log` (ou le dossier
`logs/` si le fichier n'existe pas encore) avec l'application associée par défaut — évite de devoir
chercher soi-même où sont les logs pour diagnostiquer un crash.

**Diagnostic automatique (v1.8.0) :** `Services/Diagnostics/CrashDiagnosisService.cs` cherche, dans
la sortie console du jeu déjà capturée (tampon borné aux 400 dernières lignes, `MainWindow`), un
motif connu et fréquent sur ce modpack précis : RAM insuffisante (`OutOfMemoryError`), version Java
incompatible, mod ou fichier `.jar` corrompu/manquant. Volontairement limité à ces quelques cas
(pas un moteur générique de diagnostic multi-modpack) : quand un motif est reconnu, une boîte de
dialogue explique la cause probable et, si elle suggère un fichier corrompu, propose d'ouvrir
directement les Paramètres pour lancer une réparation (voir ci-dessous). Aucun motif reconnu = pas
de diagnostic affiché, plutôt que d'inventer une explication non fiable.

### Serveur unique (Astral Nexus)

Fichiers : `MainWindow.xaml.cs` (orchestration), `Services/Launch/ServerListWriter.cs`

`LauncherSettings.ServerHost` est préconfiguré par défaut (adresse du serveur Astral Nexus) : deux
mécanismes combinés limitent le joueur à ce serveur.

1. **Rejoint automatiquement au démarrage** : `ServerIp`/`ServerPort` sur `MLaunchOption` ajoutent
   les arguments `--server`/`--port` (fonctionnalité vanilla, gérée par CmlLib.Core en interne) —
   le jeu se connecte directement au serveur configuré dès le lancement, sans passer par l'écran
   multijoueur.
2. **Liste multijoueur réinitialisée** : `ServerListWriter.WriteSingleServer` écrit `servers.dat`
   (NBT non compressé, format vanilla, écrit à la main — pas de dépendance NBT nécessaire pour une
   structure aussi simple) avec ce seul serveur, à chaque lancement, avant de démarrer le jeu.

**Limite connue :** ceci n'empêche pas un joueur d'ajouter manuellement un autre serveur *pendant*
une session déjà lancée (l'écran multijoueur vanilla le permet nativement, et ni CmlLib.Core ni ce
launcher n'implémentent de restriction côté client type mod/whitelist) — seule la liste au
prochain lancement est remise à zéro. Suffisant pour un usage privé entre amis, pas une vraie
sandbox contre un joueur déterminé à contourner.

## Bannière de maintenance (v1.9.0)

Fichiers : `Services/Maintenance/MaintenanceService.cs`, `MainWindow.xaml(.cs)`

`MaintenanceMessageUrl` (settings.json) pointe **par défaut** vers `maintenance.txt` à côté du
modpack sur le VPS (généré via `cp scripts/vps/maintenance.txt.example` — voir
`scripts/vps/README.md`) : s'il existe et n'est pas vide, son contenu s'affiche en carte sur le
dashboard (filet magenta, icône d'alerte — même esprit que les autres cartes mais volontairement
plus marquée), pour prévenir les joueurs d'une coupure programmée sans dépendre de Discord.
Première ligne du fichier affichée en titre ; une éventuelle ligne `FIN: <valeur>` (n'importe où
dans le fichier, `<valeur>` libre — heure, date, "indéterminée"...) s'affiche à part, dans un bloc
"FIN ESTIMÉE" à droite de la carte, et n'apparaît pas dans le sous-titre ; le reste (s'il y en a)
forme le sous-titre atténué — un message court tient sur une ligne, un message détaillé peut
s'étaler sur plusieurs, sans format imposé côté VPS au-delà de cette ligne `FIN:` optionnelle.
Absence de fichier (404), fichier vide, ou VPS injoignable = pas de carte, même logique que le
changelog optionnel du modpack — **publier ou supprimer `maintenance.txt` sur le VPS suffit donc à
afficher/masquer la bannière chez tous les joueurs**, sans jamais toucher au launcher. Revérifiée
toutes les 5 minutes (même cadence que les actus), pas seulement au démarrage.

## Statut du serveur

Fichier : `src/MinecraftLauncherPerso/Services/Status/ServerStatusService.cs`

Implémente le protocole *Server List Ping* de Minecraft (le même que l'écran multijoueur du jeu
utilise pour afficher joueurs connectés/latence à côté de chaque serveur) : handshake puis requête
status sur une connexion TCP brute vers `ServerHost:ServerPort`, sans authentification, réponse
JSON parsée pour en extraire `players.online`/`players.max`. `MainWindow` l'interroge au démarrage
puis toutes les 30 secondes (`DispatcherTimer`) et affiche le statut dans la carte serveur de la
colonne droite (v1.5.0) : pastille + libellé "EN LIGNE"/"HORS LIGNE", et le nombre de joueurs en
gros chiffres mono (`X/8`) à côté du crest du serveur. N'importe quel échec (timeout, port fermé,
DNS invalide) est traité comme "hors ligne" plutôt que de propager une erreur.

**Avertissement avant de jouer :** si le dernier statut connu est "hors ligne" au moment de
cliquer sur "Jouer", une boîte de dialogue demande confirmation avant de continuer (le joueur peut
choisir de lancer quand même — utile si le ping échoue à tort, ex. pare-feu bloquant juste le port
de status tout en laissant passer le jeu).

**Notification desktop au retour en ligne :** `Services/Notifications/DesktopNotificationService.cs`
affiche une bulle native Windows (`System.Windows.Forms.NotifyIcon`, pas de dépendance toast dédiée
— celles-ci supposent généralement une identité de paquet MSIX que cet exe autonome n'a pas) sur
une vraie transition hors ligne → en ligne détectée par le polling ci-dessus, pour le remarquer
même si le launcher est réduit ou en arrière-plan. Ne se déclenche jamais au tout premier check
(statut "inconnu" au démarrage, pas "hors ligne"). Peut être désactivée dans les Paramètres
(`DesktopNotificationsEnabled`, activée par défaut).

## Lancement unique (anti double-instance)

Fichier : `App.xaml.cs`

Un [Mutex](https://learn.microsoft.com/dotnet/api/system.threading.mutex) nommé globalement
(`Global\AL_Launcher_SingleInstance`) est créé au démarrage : si un autre processus du launcher
tourne déjà (même utilisateur ou non), une boîte de dialogue prévient et le nouveau process se
ferme immédiatement, plutôt que de risquer deux instances qui écrivent en même temps dans le même
`GameDirectory`/`settings.json`.

## Mise à jour automatique du launcher

Fichiers : `Services/Update/GitHubUpdateService.cs`, workflow `.github/workflows/build-windows.yml`

Au démarrage puis toutes les **60 secondes** (`DispatcherTimer` dans `MainWindow`, même principe que
le ping du statut serveur), le launcher interroge `GET /repos/Omneria/ALlauncher/releases/latest`
(API GitHub publique, pas d'authentification nécessaire) et compare le tag de la dernière release
(`vX.Y.Z`) à `MinecraftLauncherPerso.csproj` → `<Version>` — inutile de fermer/rouvrir le launcher
pour savoir si une mise à jour est sortie entre-temps. Dès qu'une mise à jour est détectée, les
vérifications suivantes ne font plus rien (pas de nouvel appel GitHub, pas de son/notification
répétés) tant qu'elle n'a pas été appliquée. Si une version plus récente existe, un bouton
"MISE À JOUR X.Y.Z DISPONIBLE" apparaît à côté du statut serveur ; un clic télécharge l'exe joint à
la release, puis :

1. Écrit un script `.cmd` temporaire qui attend (boucle sur `tasklist`/PID) que le process courant
   se termine — impossible de remplacer son propre `.exe` pendant qu'il tourne (verrou Windows) —,
   remplace le fichier, puis relance le launcher.
2. Lance ce script en détaché et appelle `Environment.Exit(0)` : le launcher se ferme, le script
   termine le remplacement, le nouveau launcher redémarre automatiquement.

**Côté publication :** le workflow CI construit toujours l'exe sur chaque push (comme avant), mais
publie en plus une **GitHub Release** (avec l'exe self-contained en pièce jointe) uniquement quand
un tag `v*.*.*` est poussé sur le dépôt (`git tag v1.2.0 && git push origin v1.2.0`, ou via "Draft a
new release" sur github.com — le job `release` complète l'exe automatiquement dans les deux cas).
Penser à incrémenter `<Version>` dans le `.csproj` **au même commit** que le tag, sinon l'auto-update
ne détectera rien de nouveau (voir l'incident documenté dans l'historique git autour de `v1.2.5` :
`<Version>` était monté à `1.3.2` sans qu'aucune release `v1.3.x` n'ait jamais été taguée, ce qui
rendait toute mise à jour indétectable).

**Notes de version en langage clair (`RELEASE_NOTES.md`) :** le corps de chaque GitHub Release
provient de `RELEASE_NOTES.md` (racine du dépôt, `--notes-file` dans le job `release`), pas des
notes auto-générées par GitHub à partir des titres de PR/commits — illisibles pour un joueur (ex.
"Merge dev dans main : correctif urgent..."). C'est aussi ce fichier qui alimente la carte
CHANGELOG du launcher et celle de la landing page (voir "Changelog (releases GitHub du launcher)"
ci-dessus) : une ligne `- ...` par changement **visible pour un joueur**, pas un résumé technique.
À mettre à jour **au même commit** que `<Version>` avant de tagger — un check CI dédié
("Vérifie que RELEASE_NOTES.md a été mis à jour") **fait échouer le build** si ce fichier n'a pas
changé depuis le tag précédent, pour ne jamais laisser passer une release avec les notes de la
version d'avant.

**Règle de versionnage (semver `X.Y.Z`)** — à appliquer à chaque tag/release :

| Nature du changement | Exemple | Champ incrémenté | Exemple de tag |
|---|---|---|---|
| Petit correctif (bugfix, pas de nouveau comportement) | fix crash, typo, ajustement mineur | `Z` (patch) | `v1.2.6` → `v1.2.7` |
| Ajout non structurel (nouvelle fonctionnalité, sans casser l'existant) | nouvel écran, nouveau réglage | `Y` (minor), `Z` remis à `0` | `v1.2.6` → `v1.3.0` |
| Gros ajout structurel (changement cassant, refonte majeure) | changement de format de settings, refonte de l'auth | `X` (major), `Y`/`Z` remis à `0` | `v1.2.6` → `v2.0.0` |

Dans les deux derniers cas, les champs à droite de celui incrémenté repartent à `0` (semver
classique) : `v1.2.6` → `v1.3.0` (pas `v1.3.6`), `v1.2.6` → `v2.0.0` (pas `v2.2.6`).

**À chaque modification** (commit, PR), préciser explicitement le tag qui en résulterait d'après ce
tableau (ex. « → prochain tag : `v1.2.7` ») — que le tag soit posé tout de suite ou plus tard, ça
évite la dérive qui a cassé l'auto-update sur `v1.2.5` (`<Version>` du csproj en avance sur les
releases réellement publiées, sans que personne ne s'en aperçoive avant que l'auto-update cesse de
fonctionner).

## Fiabilité, sécurité & tests

Fichiers : `Services/Configuration/SettingsManager.cs`, `Services/Diagnostics/Logger.cs`,
`Services/Java/AdoptiumApiClient.cs`, `Services/Update/GitHubUpdateService.cs`,
`.github/workflows/build-windows.yml`, `tests/MinecraftLauncherPerso.Tests/`

**settings.json robuste face aux coupures/corruptions :** `SettingsManager.Save` écrit sur un
fichier temporaire puis le remplace de façon atomique (`File.Move(..., overwrite: true)`) plutôt
que d'écraser directement le fichier final — une coupure en pleine écriture ne peut plus laisser un
`settings.json` tronqué. Si un `settings.json` corrompu existe déjà (ancienne version du launcher,
crash antérieur à ce correctif...), `Load()` ne plante plus : il repart des valeurs par défaut et
renomme le fichier fautif à côté (`settings.json.corrupt-<horodatage>`) plutôt que de l'écraser en
silence. `Load()` valide aussi les champs numériques (RAM min/max positives et dans le bon ordre,
port serveur dans `[1, 65535]`) et corrige toute valeur absurde — utile si `settings.json` est édité
à la main, en plus de la validation déjà faite dans `SettingsWindow`.

**`HttpClient` partagé :** `Services/Http/SharedHttpClient.cs` fournit une instance unique
(`SharedHttpClient.Instance`) réutilisée par tous les services HTTP (Java, ModSync, Auth, Update,
News) au lieu qu'un `new HttpClient()` distinct soit créé par service — évite l'épuisement des
sockets disponibles (chaque `HttpClient` non partagé garde ses connexions TCP ouvertes jusqu'à sa
propre finalisation par le GC) sur un launcher qui enchaîne beaucoup de requêtes courtes au même
moment (démarrage : vérif Java, statut serveur, sync modpack, mise à jour, actus, auth).

**Vérification d'intégrité des téléchargements :** le JRE Temurin (API Adoptium, qui fournit une
empreinte SHA-256 par build) et l'exe de mise à jour du launcher (empreinte publiée par le workflow
CI en pièce jointe séparée `<exe>.sha256` à côté de l'exe sur chaque release) sont tous les deux
vérifiés après téléchargement, avant extraction/exécution — un contenu altéré en transit est rejeté
plutôt qu'exécuté avec les mêmes droits que le launcher. Une release publiée avant l'ajout de ce
mécanisme (sans fichier `.sha256` joint) applique la mise à jour sans ce contrôle plutôt que de
casser l'auto-update rétroactivement.

**Cohérence `<Version>` / tag vérifiée en CI :** un job échoue désormais le build si le tag `v*`
poussé ne correspond pas à `<Version>` dans le `.csproj`, pour empêcher à la racine l'incident
documenté plus haut (`v1.2.5`, auto-update cassé en silence par une dérive entre les deux).

**Journal fichier (`launcher.log`) :** plusieurs échecs volontairement "avalés" pour ne jamais
bloquer l'utilisateur (reconnexion Microsoft silencieuse en cache, vérification de mise à jour,
sondage d'un exécutable `java` candidat) étaient jusqu'ici totalement invisibles. Ils sont
maintenant tracés dans `%AppData%/MinecraftLauncherPerso/launcher.log` (rotation simple au-delà de
5 Mo) via `Services/Diagnostics/Logger.cs` — best-effort : une erreur d'écriture du journal
lui-même est ignorée, pour ne jamais devenir une nouvelle source de plantage.

**Tests unitaires :** `tests/MinecraftLauncherPerso.Tests` (xUnit) couvre la logique la plus
risquée à la main : encodage/décodage VarInt du ping serveur, écriture NBT de `servers.dat`,
parsing des tags `vX.Y.Z` et de la sortie `java -version`, la récupération de `SettingsManager`
face à un fichier corrompu ou des valeurs invalides, et la reconnaissance de motifs de
`CrashDiagnosisService`. Quelques membres
normalement `private` sont exposés en `internal` (voir `[InternalsVisibleTo]` dans
`AssemblyInfo.cs`) uniquement pour rester testables sans passer par le réseau ou le disque. Lancé
en CI (`dotnet test`) avant la publication des artefacts.

## Paramètres

Fichiers : `SettingsWindow.xaml(.cs)`

Fenêtre ouverte via l'item "PARAMÈTRES" de la barre latérale : RAM minimum/maximum,
résolution de la fenêtre du jeu (`ScreenWidth`/`ScreenHeight` sur `MLaunchOption` — `0` laisse
Minecraft décider, pas d'argument `--width`/`--height` passé), dossier de jeu (`GameDirectory`,
sélection via `Microsoft.Win32.OpenFolderDialog`, natif WPF depuis .NET 8, pas de dépendance
WinForms). "Enregistrer" persiste dans `settings.json` et ferme la fenêtre ; fermer sans enregistrer
(✕) n'écrit rien.

**RAM en un slider à deux poignées, pas en champs texte :** `RamMinThumb`/`RamMaxThumb` (deux
`Thumb` sur un `Canvas`, `RamRangeCanvas`) remplacent les anciens `TextBox` — et depuis v1.8.0,
les deux sliders min/max séparés de la v1.7.0, fusionnés en un seul contrôle à deux poignées.
Bornées à `[512, RAM physique totale de la machine]` (`SystemInfo.GetTotalPhysicalMemoryMb`),
calées sur des paliers de 256 Mo. RAM min > RAM max est rendu impossible par construction (chaque
poignée ne peut pas dépasser l'autre, `RamMinThumb_DragDelta`/`RamMaxThumb_DragDelta` dans
`SettingsWindow.xaml.cs`), au lieu de devoir détecter/rejeter la valeur invalide comme avec des
champs texte libres.

**RAM par défaut adaptée à la machine :** `LauncherSettings.MaxRamMb` n'est plus une valeur fixe
identique pour tout le monde — `Services/Hardware/SystemInfo.cs` interroge la RAM physique totale
de la machine (P/Invoke `GlobalMemoryStatusEx`, API Win32) et calcule une recommandation adaptée
(3G en dessous de 8 Go de RAM système, 4G en dessous de 12 Go, 6G en dessous de 16 Go, 8G au-delà —
jamais plus de la moitié de la RAM totale). Uniquement au premier lancement (settings.json pas
encore créé) : une fois modifiée, la valeur choisie par le joueur reste celle utilisée.

**Notifications desktop :** case à cocher "NOTIFICATIONS DESKTOP (SERVEUR EN LIGNE)"
(`DesktopNotificationsEnabled`, activée par défaut) contrôle si `DesktopNotificationService` (voir
section "Statut du serveur") a le droit de s'afficher au retour en ligne du serveur.

**Maintenance (v1.8.0/v1.9.0) :** quatre boutons dans cette fenêtre.
- "RÉPARER LE MODPACK" déclenche `IModSyncService.RepairAsync` (voir "Synchronisation mods/config"
  ci-dessus) avec le statut de progression affiché directement sous les boutons, encadré d'une
  sauvegarde/restauration d'`options.txt` en filet de sécurité.
- "VOIR LES LOGS" ouvre `LogViewerWindow.xaml(.cs)`, un visualiseur intégré qui bascule entre
  `launcher.log` (`Logger.LogFilePath`) et `{GameDirectory}/logs/latest.log` (jeu), avec une
  recherche texte simple (filtre par ligne, insensible à la casse) et un bouton "COPIER" qui met le
  contenu affiché dans le presse-papier — pratique pour coller un extrait sur Discord quand un
  joueur demande de l'aide, sans avoir à aller fouiller `%AppData%` à la main.
- "REVENIR EN ARRIÈRE" (v1.9.0) restaure le dernier cran de recul du modpack — voir "Retour en
  arrière d'un cran" ci-dessus. Désactivé tant qu'aucune sauvegarde n'existe.
- "SIGNALER UN PROBLÈME" (v1.9.0) copie dans le presse-papier un rapport prêt à coller sur Discord :
  version du launcher, RAM configurée, serveur, dernière synchro et les 30 dernières lignes de
  `launcher.log` (`SettingsWindow.BuildProblemReportAsync`) — évite de devoir demander à chaque
  joueur de recopier ces infos à la main quand il demande de l'aide.

**Mentions légales :** lien "MENTIONS LÉGALES" en bas de cette fenêtre, ouvre `LegalWindow.xaml(.cs)`
— rappel du statut non officiel du launcher, et surtout la liste des dépendances open source
utilisées (nom, licence, lien GitHub) : `CmlLib.Core`, `CmlLib.Core.Installer.Forge` (MIT),
`Microsoft.Identity.Client`/`.Extensions.Msal` (MIT), polices Chakra Petch/Inter/JetBrains Mono
(SIL OFL 1.1). Les liens ouvrent le navigateur par défaut (`Hyperlink.RequestNavigate` →
`Process.Start`).

## Profil connecté

Après authentification réussie (bouton `SE CONNECTER` ou clic direct sur `JOUER`), le launcher
affiche le pseudo et un rendu de tête (avatar) dans le bloc profil en bas de la barre latérale
(v1.5.0 : ce bloc a remplacé la zone "SE CONNECTER" au même endroit) — récupéré depuis
[crafatar.com](https://crafatar.com) (`https://crafatar.com/avatars/{uuid}`, service public gratuit
de rendu de skins Minecraft, requête avec un `User-Agent` explicite comme les autres appels HTTP du
launcher, certains services renvoyant un 403 silencieux sans ça ; minotar.net en secours si crafatar
échoue). Best-effort : si les deux échouent, seul le pseudo texte s'affiche, avec un indicateur ⚠
survolable (tooltip = message d'erreur exact) au lieu d'un échec silencieux.

**Modifier son skin :** cliquer sur l'avatar ouvre la page officielle de changement de skin
(`minecraft.net/en-us/msaprofile/mygames/editskin`) dans le navigateur par défaut — pas d'éditeur de
skin intégré au launcher, minecraft.net gère déjà l'upload/la prévisualisation via la session du
navigateur.

**Rafraîchi périodiquement :** l'avatar n'était auparavant récupéré qu'à la connexion (ou à la
restauration de session au démarrage) — un changement de skin fait sur minecraft.net pendant une
session déjà longue du launcher n'était visible qu'au redémarrage suivant. Un `DispatcherTimer`
(30 min) le re-télécharge tant qu'un profil reste affiché.

## Identité visuelle (branding Omnéria)

Fichiers : `AppTheme.xaml`, `SplashWindow.xaml(.cs)`, `MainWindow.xaml`, `Assets/`

- **Thème** (`AppTheme.xaml`) : palette néon sur fond bleu-nuit (cyan `#00E5C7` en accent unique),
  géométrie à coins vifs/biseautés (jamais de `CornerRadius`), polices Chakra Petch / Inter /
  JetBrains Mono embarquées (licence OFL) pour un rendu identique sans installation côté joueur.
- **Fenêtre principale** (v1.5.0, refonte "Fusion") : chrome Windows par défaut désactivé
  (`WindowStyle="None"`), barre de titre réduite aux boutons réduire/fermer/mode compact (la marque
  vit désormais dans la barre latérale). Disposition en tableau de bord : barre latérale fixe
  (logo Omnéria, navigation, profil connecté) + zone principale à deux colonnes (actus/changelog à
  gauche, statut du serveur Astral Nexus avec son crest + lancement à droite). Voir
  `MainWindow.xaml.cs` pour l'orchestration Java → Forge → Sync → Auth → Lancement, inchangée.
- **Redimensionnable + mode compact** (v1.8.0) : `WindowChrome` (`System.Windows.Shell`,
  `CaptionHeight="0"`) restaure des bords redimensionnables à la souris sur cette fenêtre sans
  chrome natif — un simple `ResizeMode="CanResize"` seul n'en donne aucun. Le bouton "⤢" de la
  barre de titre bascule un mode compact qui masque la colonne ACTUS pour ne garder que le statut
  serveur + lancement, avec la fenêtre rétrécie en conséquence (utile sur petit écran). Taille de
  fenêtre et mode compact sont mémorisés dans `settings.json`
  (`LauncherWindowWidth`/`LauncherWindowHeight`/`IsCompactMode`) et réappliqués au démarrage
  suivant, au lieu de revenir systématiquement à la taille par défaut.
- **Écran de démarrage** (`SplashWindow`) : affiche le logo Omnéria (`omneria-mark.png`) avec une
  entrée animée (rotation + zoom, easing à rebond) avant de céder la place à la fenêtre principale ;
  ouvert par `App.xaml.cs` au lancement, à la place de `MainWindow` directement.
- **Icône/exécutable** : l'exécutable publié s'appelle `AL Launcher.exe` (`AssemblyName`, inchangé
  pour ne pas casser le workflow de release), porte une icône Windows multi-résolutions générée
  depuis le logo (`Assets/Images/omneria-mark.ico`), et ne génère plus de fichier `.pdb`
  (`DebugType=None`).
- **Fond animé** : les deux halos radiaux (violet/cyan) de la fenêtre principale dérivent lentement
  et pulsent en opacité en boucle infinie (`Storyboard` déclenché sur `Window.Loaded`,
  `AutoReverse="True"`, durées de 9 à 17s) — signature discrète, jamais assez rapide pour distraire
  de l'UI.
- **Sons** : `System.Media.SystemSounds` (aucun asset audio à embarquer) — `Asterisk` quand une
  mise à jour du launcher est détectée, `Hand` quand le jeu quitte anormalement (crash).

## Build

Le projet cible `net8.0-windows` (WPF) : à builder/exécuter sous Windows avec le SDK .NET 8.

```powershell
dotnet restore
dotnet build
dotnet run --project src/MinecraftLauncherPerso
```

> Le développement se fait dans un environnement Linux, qui ne peut pas compiler de projet WPF
> (`net8.0-windows`) : impossible de builder ou tester ici. Un workflow CI GitHub Actions
> (`.github/workflows/build-windows.yml`) build le projet sur `windows-latest` à chaque push et
> publie un exécutable en artifact — c'est le moyen de vérifier qu'un changement compile toujours.
>
## Configuration avant premier lancement

`ModpackZipUrl`, `MicrosoftClientId` et `ServerHost`/`ServerPort` sont déjà préconfigurés par
défaut : rien à faire pour se connecter et jouer directement sur Astral Nexus, tout le monde
partage le même Client ID (voir section Authentification).
Le dépôt étant public, `ModpackZipUrl` n'apparaît pas en clair dans le code source (stockée
encodée en base64 dans `LauncherSettings.cs`, décodée au démarrage) pour ne pas exposer l'IP du
VPS à quiconque parcourt le dépôt — ce n'est qu'une précaution légère (le launcher final l'utilise
bien en clair au runtime), pas une vraie protection contre quelqu'un qui inspecterait
l'exécutable. `ServerHost` pointe lui sur `astralnexusmc.duckdns.org` (DuckDNS) plutôt que sur
l'IP du VPS directement — celle-ci est aussi celle du VPN, donc découplée derrière un nom de
domaine au lieu d'être encodée en base64 (qui n'aurait de toute façon pas empêché `servers.dat` de
l'exposer en clair une fois résolue). Si le VPS change d'adresse, seul l'enregistrement DNS
DuckDNS est à mettre à jour, pas le launcher. Le Client ID Azure AD, lui, n'a pas besoin d'être
masqué (il identifie l'application, pas un secret : c'est la même logique que pour n'importe quel
launcher tiers public).

Pour ajuster RAM, URL du modpack, adresse du serveur (`ServerHost`/`ServerPort`) ou dossier de jeu
sans passer par l'UI, modifier
ce même fichier.

**Assistant de premier lancement (v1.8.0) :** `WelcomeWindow.xaml(.cs)`, affiché une seule fois —
`SettingsManager.SettingsFileExists()` (appelé avant `Load()`, qui crée le fichier) indique à
`MainWindow` si c'est la toute première exécution sur cette machine. Trois pages courtes (accueil,
choix du dossier de jeu avec la valeur par défaut déjà pré-remplie, orientation vers "SE
CONNECTER"/"JOUER") plutôt qu'un flux de connexion dupliqué dans l'assistant lui-même. Fermable à
tout moment (✕) sans perdre les valeurs déjà choisies.

## Avertissement

Ce launcher est un outil non officiel développé pour un usage privé entre amis, sans lien avec
Mojang, Microsoft ou Minecraft. Fourni "en l'état", sans garantie — chacun l'utilise à ses propres
risques (comme n'importe quel logiciel tiers modifiant l'installation du jeu). Chaque joueur reste
responsable du respect des conditions d'utilisation de Mojang/Microsoft liées à son propre compte.

Contrairement à certains launchers tiers commerciaux (ex. Paladium, dont le CLUF autorise
explicitement un scan de la RAM de la machine pour détecter des logiciels tiers, avec remontée au
serveur de l'intitulé du compte, de l'IP et des specs matérielles), **ce launcher ne scanne rien sur
la machine du joueur et n'envoie aucune télémétrie** : les seules requêtes réseau qu'il effectue
sont celles nécessaires à son fonctionnement (Java, Forge, mods, auth Microsoft, ping du statut
serveur, vérification de mise à jour) — voir les sections ci-dessus pour le détail de chacune.
