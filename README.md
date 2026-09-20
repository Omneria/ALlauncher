# Launcher Minecraft personnalisé

Launcher WPF (.NET / C#) pour un serveur Minecraft privé (8 joueurs max), basé sur
[CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) et
[CmlLib.Core.Installer.Forge](https://github.com/CmlLib/CmlLib.Core.Installer.Forge).

## Contexte serveur

- Minecraft **1.16.5**, Forge **36.2.34**
- 87 mods (Botania, Create, Quark, Minecolonies, Twilight Forest, Biomes O'Plenty, ...)
- **Java 8 obligatoire** (Forge 1.16.5 est incompatible avec Java 11+)
- Mods/config hébergés sur un VPS perso, accessibles en HTTP direct (voir manifeste ci-dessous)

## Ce que fait le launcher

1. Vérifie/installe Java 8 (build Temurin/Adoptium si absent) — **implémenté**
2. Installe Forge 1.16.5-36.2.34 via CmlLib.Core.Installer.Forge — **implémenté**
3. Synchronise `mods/` et `config/` depuis le VPS (par hash, pas à chaque lancement) — **implémenté**
4. Authentifie via OAuth Microsoft direct (navigateur système, sans dépendre du launcher officiel)
   puis la chaîne Xbox Live → XSTS → Minecraft — **implémenté, en attente d'approbation Microsoft**
5. Lance le jeu avec le bon classpath Forge et la RAM configurée — **implémenté**

Pas de gestion multi-comptes : usage privé entre amis, un seul compte par machine.

> **Statut (2026-09) :** l'authentification OAuth Microsoft directe (MSAL.NET, navigateur système +
> Xbox Live/XSTS, sans dépendre du launcher officiel) est implémentée et le code fonctionne (Xbox
> Live et XSTS répondent correctement). Mais Minecraft exige une **approbation manuelle par
> Microsoft** de toute nouvelle application Azure AD avant d'autoriser l'appel à
> `login_with_xbox` — sans cette approbation, cet appel échoue avec `403 "Invalid app
> registration"` (formulaire de demande : https://aka.ms/mce-reviewappid, délai variable, de 24h à
> plusieurs mois selon les témoignages). Il n'existe pas d'API permettant de vérifier ce statut
> autrement qu'en tentant une vraie connexion : lancez le launcher et connectez-vous avec un compte
> Microsoft réel — un succès (arrivée sur le profil Minecraft) confirme l'approbation, un
> `403 "Invalid app registration"` confirme qu'elle est toujours en attente. Si l'app n'est pas
> encore approuvée, l'ancien mode de secours (lecture de la session du launcher officiel via
> `launcher_accounts.json`) reste disponible dans l'historique git (commit `b97b6fa` et avant) et
> peut être restauré temporairement.

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
│       ├── AlgaronTheme.xaml               # charte graphique (couleurs, polices, styles de contrôles)
│       ├── Assets/
│       │   ├── Fonts/                      # Chakra Petch / Inter / JetBrains Mono (OFL), embarquées
│       │   └── Images/                     # logo Algaron (mark/lockup) + icône .ico du launcher
│       ├── Models/
│       │   ├── LauncherSettings.cs         # préférences persistées (RAM, dossier de jeu, URL modpack)
│       │   ├── JavaVersionInfo.cs
│       │   └── JavaSetupProgress.cs
│       └── Services/
│           ├── Java/                       # détection + installation Java 8
│           │   ├── IJavaManager.cs
│           │   ├── JavaManager.cs
│           │   └── AdoptiumApiClient.cs
│           ├── Forge/                      # installation Forge (CmlLib.Core.Installer.Forge)
│           │   ├── IForgeManager.cs
│           │   └── ForgeManager.cs
│           ├── ModSync/                    # synchro du modpack .zip depuis le VPS (ETag/Last-Modified)
│           │   ├── IModSyncService.cs
│           │   └── ModSyncService.cs
│           ├── Auth/                       # OAuth Microsoft direct (MSAL.NET) -> Xbox Live -> XSTS -> Minecraft
│           │   ├── IAuthService.cs
│           │   └── MicrosoftAuthService.cs
│           ├── Launch/                     # construction + démarrage du process Forge/Minecraft
│           │   ├── IGameLauncher.cs
│           │   └── GameLauncher.cs
│           └── Configuration/
│               └── SettingsManager.cs      # charge/sauvegarde settings.json
├── README.md
└── .gitignore
```

Chaque responsabilité (Java, Forge, sync mods, auth, lancement) est isolée dans son propre
service derrière une interface (`IJavaManager`, `IForgeManager`, `IModSyncService`,
`IAuthService`, `IGameLauncher`), injectées dans `MainWindow` qui orchestre l'enchaînement complet
au clic sur "Jouer".

## Logique de vérification/installation de Java 8

Fichier : `src/MinecraftLauncherPerso/Services/Java/JavaManager.cs`

Ordre de résolution dans `EnsureJava8Async` :

1. **Java 8 portable déjà installé par ce launcher** : recherche récursive d'un exécutable
   `java(.exe)` sous `%AppData%/MinecraftLauncherPerso/runtime/java8`, validé en exécutant
   `java -version` et en vérifiant que la version majeure vaut bien 8.
2. **Java 8 déjà présent sur la machine** : `JAVA_HOME`, `java` sur le `PATH`, puis les dossiers
   d'installation courants sous Windows (`Program Files\Java`, `...\Eclipse Adoptium`,
   `...\AdoptOpenJDK`).
3. **Téléchargement automatique** : si aucun Java 8 valide n'est trouvé, interrogation de l'API
   [Adoptium](https://api.adoptium.net) (`/v3/assets/latest/8/hotspot`) pour récupérer la dernière
   build Temurin 8 (JRE) correspondant à l'OS/architecture de la machine, téléchargement avec
   suivi de progression, puis extraction dans le dossier `runtime/java8` ci-dessus.

La détection de version parse la sortie de `java -version` (`version "1.8.0_392"` →
version majeure 8 ; `version "17.0.9"` → version majeure 17), ce qui permet de rejeter tout
Java déjà installé qui ne serait pas une version 8, même si un JDK plus récent est présent.

## Installation de Forge

Fichier : `src/MinecraftLauncherPerso/Services/Forge/ForgeManager.cs`

Utilise le package `CmlLib.Core.Installer.Forge` :
`ForgeInstaller.Install(minecraftVersion, forgeVersion, options)` installe/mappe le profil de
version composé (vanilla + Forge) et retourne son identifiant (ex. `1.16.5-forge-36.2.34`).
Ce mapping seul ne télécharge pas les fichiers de la version : `MinecraftLauncher.InstallAsync`
est appelé juste après pour installer réellement le jar, les libs et les assets vanilla dont
Forge dépend.

## Synchronisation mods/config (VPS)

Fichier : `src/MinecraftLauncherPerso/Services/ModSync/ModSyncService.cs`

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

## Authentification (OAuth Microsoft direct)

Fichier : `src/MinecraftLauncherPerso/Services/Auth/MicrosoftAuthService.cs`

Flux MSAL.NET interactif, navigateur système (pas de WebView2, pas de code à recopier — même
expérience que CurseForge/Paladium) :

1. `PublicClientApplicationBuilder` (autorité `consumers`, redirection `http://localhost`) tente
   d'abord un `AcquireTokenSilent` sur un compte déjà en cache (`msal-cache.bin` dans
   `%AppData%/MinecraftLauncherPerso/`) ; en cas d'échec/expiration, ouvre le navigateur par défaut
   pour une connexion interactive (`AcquireTokenInteractive`, scopes `XboxLive.signin` +
   `offline_access`).
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
`MLaunchOption` avec `JavaPath`, `MinimumRamMb`/`MaximumRamMb`, `ServerIp`/`ServerPort` (voir
ci-dessous), puis appelle `MinecraftLauncher.BuildProcessAsync(versionId, options)`. Le process
est ensuite démarré manuellement (au lieu du `ProcessWrapper.StartWithEvents()` fourni par
CmlLib.Core, qui force `CreateNoWindow=false`) avec `CreateNoWindow=true` : sans ça, une fenêtre
de console Windows s'ouvrait pour `java.exe` (application console sans parent console attaché) en
plus de la fenêtre du launcher. Le launcher ne bloque pas en attendant la fermeture du jeu : le
bouton "Jouer" redevient disponible dès que le process a démarré, et les logs du jeu remontent
dans le journal de statut tant que la fenêtre reste ouverte.

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

## Identité visuelle (branding Algaron)

Fichiers : `AlgaronTheme.xaml`, `SplashWindow.xaml(.cs)`, `MainWindow.xaml`, `Assets/`

- **Thème** (`AlgaronTheme.xaml`) : palette néon sur fond bleu-nuit (cyan `#00E5C7` en accent unique),
  géométrie à coins vifs/biseautés (jamais de `CornerRadius`), polices Chakra Petch / Inter /
  JetBrains Mono embarquées (licence OFL) pour un rendu identique sans installation côté joueur.
- **Fenêtre principale** : chrome Windows par défaut désactivé (`WindowStyle="None"`), barre de titre
  et boutons réduire/fermer personnalisés, dessinés dans le thème.
- **Écran de démarrage** (`SplashWindow`) : affiche le logo Algaron (`algaron-lockup.png`) avec une
  entrée animée (rotation + zoom, easing à rebond) avant de céder la place à la fenêtre principale ;
  ouvert par `App.xaml.cs` au lancement, à la place de `MainWindow` directement.
- **Icône/exécutable** : l'exécutable publié s'appelle `AL Launcher.exe` (`AssemblyName`), porte une
  icône Windows multi-résolutions générée depuis le logo (`Assets/Images/algaron-mark.ico`), et ne
  génère plus de fichier `.pdb` (`DebugType=None`).

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
l'exécutable. `ServerHost` pointe lui sur `astranexusmc.duckdns.org` (DuckDNS) plutôt que sur
l'IP du VPS directement — celle-ci est aussi celle du VPN, donc découplée derrière un nom de
domaine au lieu d'être encodée en base64 (qui n'aurait de toute façon pas empêché `servers.dat` de
l'exposer en clair une fois résolue). Si le VPS change d'adresse, seul l'enregistrement DNS
DuckDNS est à mettre à jour, pas le launcher. Le Client ID Azure AD, lui, n'a pas besoin d'être
masqué (il identifie l'application, pas un secret : c'est la même logique que pour n'importe quel
launcher tiers public).

Pour ajuster RAM, URL du modpack, adresse du serveur (`ServerHost`/`ServerPort`) ou dossier de jeu
sans passer par l'UI, modifier
ce même fichier.
