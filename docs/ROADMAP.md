# Pistes d'amélioration (audit complet, v1.11.0)

Issu d'une relecture intégrale du dépôt (code C#, XAML, workflow CI, scripts VPS, README). Les
problèmes trouvés ont été corrigés dans la v1.11.0 (voir `RELEASE_NOTES.md` et l'historique git) ;
ce document liste ce qui **reste à faire**, classé par rapport valeur/effort, avec le raisonnement
derrière chaque piste pour pouvoir trancher sans relire tout le code.

Légende effort : ▲ petit (une soirée), ▲▲ moyen (quelques jours), ▲▲▲ gros chantier.

## 1. Sécurité et confiance (à faire en premier)

### 1.1 HTTPS sur le VPS ▲ — impact fort — **fait en v1.11.0**

URL par défaut en `https://astralnexusmc.duckdns.org/modpack/...` (plus de base64), migration
des `settings.json` existants, Caddy + Let's Encrypt en place sur le VPS, manifest régénéré en
HTTPS, aucun repli HTTP côté launcher, plus rien servi en clair côté VPS.

Tout ce qui vient du VPS (`manifest.json`, chaque `.jar`, `news.txt`, `maintenance.txt`, le zip)
transite en **HTTP simple**. Le SHA-256 du manifest ne protège que contre la corruption, pas contre
une altération volontaire : le manifest lui-même arrive par le même canal non chiffré, un
intermédiaire (Wi-Fi public, FAI, proxy) peut remplacer un mod *et* son empreinte. Un `.jar`
s'exécute avec les droits du joueur. Depuis v1.11.0 le launcher refuse au moins les chemins hors
dossier de jeu, mais ça ne couvre pas un jar malveillant à un chemin légitime.

- Un nom de domaine existe déjà (`astralnexusmc.duckdns.org`) : un reverse proxy Caddy sur le VPS
  obtient et renouvelle un certificat Let's Encrypt tout seul (2 lignes de Caddyfile).
- Le launcher n'a **rien** à changer côté code : seules les URL par défaut dans
  `LauncherSettings.cs` passent en `https://`, et une migration `SettingsManager` (même schéma que
  `MigrateModpackManifestUrl`) bascule les `settings.json` existants.
- Ça permet aussi de **retirer l'encodage base64 de l'IP** : avec un domaine, plus rien à cacher,
  et le base64 n'a jamais protégé quoi que ce soit (décodable en une ligne, l'IP est de toute
  façon visible dans `servers.dat` et dans n'importe quel outil réseau).

### 1.2 Signature de l'exécutable (Authenticode) ▲▲ — impact moyen — **côté code : fait en v1.11.0, reste le certificat**

Fait : le job `release` signe l'exe (`signtool`, horodaté) dès que les secrets
`CODE_SIGNING_PFX_BASE64`/`CODE_SIGNING_PFX_PASSWORD` existent, et `GitHubUpdateService` exige
qu'une mise à jour soit signée par le même éditeur dès lors que le launcher installé l'est
lui-même (`AuthenticodeVerifier`, WinVerifyTrust). Reste : obtenir un certificat de signature de
code émis par une autorité reconnue (Azure Trusted Signing est le moins cher pour un particulier)
et le déposer dans les secrets du dépôt. Un certificat auto-signé n'apporte rien.

L'exe n'est pas signé : SmartScreen affiche "éditeur inconnu" à chaque nouvelle version, et
l'auto-update remplace un exe par un autre que rien n'authentifie au-delà d'un `.sha256` servi par
la même origine (si le compte GitHub est compromis, les deux le sont ensemble). Un certificat de
signature de code (Azure Trusted Signing est le moins cher pour un particulier/petite structure)
signé dans le job `release` règlerait les deux : SmartScreen se calme après quelques installations,
et `GitHubUpdateService` peut vérifier la signature (`X509Certificate.CreateFromSignedFile` +
comparaison de l'empreinte de l'éditeur) avant de remplacer l'exe.

### 1.3 Analyseurs .NET et avertissements bloquants en CI ▲

`<EnableNETAnalyzers>true</EnableNETAnalyzers>`, `<AnalysisLevel>latest-recommended</AnalysisLevel>`
et `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` dans le csproj, plus
`dotnet format --verify-no-changes` en CI. À activer sur `dev` d'abord : il y aura une première
salve d'avertissements à corriger (CA2007 ConfigureAwait est à désactiver pour du WPF, CA1848
logging aussi). Ça empêche durablement les `catch (Exception)` vides et les `async void` oubliés
qui sont à l'origine des trois régressions de v1.9.x.

## 2. Fiabilité du lancement

### 2.1 Bouton ANNULER pendant le pipeline de lancement ▲▲ — **fait en v1.11.0**

Une fois JOUER cliqué, aucun moyen d'interrompre un téléchargement Forge/modpack bloqué (VPS qui
répond au compte-gouttes) autrement qu'en fermant le launcher. Toutes les étapes acceptent déjà un
`CancellationToken` : il manque un `CancellationTokenSource` dans `PlayButton_Click`, un bouton
dans `LoadingPanel`, et la gestion propre d'`OperationCanceledException` (message "annulé" plutôt
que "erreur"). Le `.part` reprenable rend l'annulation sans perte.

### 2.2 Extraire l'orchestration de `MainWindow` dans un `LaunchPipeline` testable ▲▲▲ — **fait en v1.11.0** (`Services/Launch/LaunchPipeline.cs`, `LaunchPipelineTests`)

`MainWindow.xaml.cs` dépasse 1 100 lignes et mélange orchestration (Java → Forge → sync → auth →
lancement), état du tableau de bord et détails visuels. Rien de cette orchestration n'est testable
aujourd'hui (impossible d'instancier une `Window` dans xUnit). Un `LaunchPipeline` (service pur,
sans WPF) qui prend les cinq interfaces déjà existantes et expose `RunAsync(IProgress<LaunchStep>,
CancellationToken)` permettrait de tester "que se passe-t-il si Forge échoue après que Java a été
installé", l'ordre des étapes, le nettoyage. C'est le chantier qui rend 2.1 et 2.3 faciles, et
MVVM (`DashboardViewModel`) peut suivre ou non — pas indispensable pour un launcher privé.

### 2.3 Vérification de compatibilité serveur avant de jouer ▲

Le Server List Ping renvoie déjà `version.protocol` et `version.name`, ignorés par
`ServerStatusService.ParseStatus`. Comparer au protocole de `MinecraftVersion` (1.20.1 = 763)
permettrait d'afficher "serveur en 1.20.1, launcher en 1.20.1 ✓" ou d'avertir *avant* un lancement
de 3 minutes qui finira par un "Incompatible client" — c'est exactement le scénario vécu lors de la
migration 1.16.5 → 1.20.1. Bonus : afficher la latence (`ms`) et le MOTD dans la carte serveur.

### 2.4 Changelog *du modpack* dans l'interface ▲

`changelog.txt` (nouveautés du pack, côté VPS) n'est affiché que dans `StatusLogTextBox`, qui est
masqué (`Visibility="Collapsed"`) : personne ne le voit jamais. Deux options : (a) le remonter
dans la carte ACTUS avec un badge "MODPACK", ou (b) le générer automatiquement —
`generate-manifest.py` peut diffuser l'ancien et le nouveau manifest (mods ajoutés / retirés / mis
à jour) et écrire `changelog.txt` sans intervention manuelle. (b) rend (a) utile.

### 2.5 Rollback durable en mode manifest ▲▲

"REVENIR EN ARRIÈRE" est annulé à la synchro suivante en mode manifest (documenté dans le README).
Une vraie protection : le VPS publie aussi `manifest-previous.json`, et le rollback mémorise
"épinglé sur la version précédente" dans `settings.json` jusqu'à ce que le manifest courant change
à nouveau côté VPS. `generate-manifest.py` n'a qu'à renommer l'ancien manifest avant d'écrire.

## 3. Expérience joueur

### 3.1 Dialogues intégrés à la charte à la place de `MessageBox` ▲▲

Les confirmations (serveur hors ligne, crash, mise à jour, erreurs) passent par `MessageBox`
Windows : chrome gris système au milieu d'une interface bleu-nuit/néon, et modal bloquant. Une
`OmneriaDialog` (fenêtre sans chrome, mêmes styles que `SettingsWindow`, boutons primaire/fantôme)
avec une API `ShowAsync(title, message, buttons)` unifierait tout. Même chose pour les erreurs de
lancement : aujourd'hui elles s'affichent en magenta dans `LoadingPanel` en texte brut, sans bouton
"réessayer".

### 3.2 Toasts non bloquants dans l'application ▲

"Mise à jour disponible", "serveur de retour en ligne", "avatar indisponible" : aujourd'hui un son
système + un bouton qui apparaît, ou une bulle `NotifyIcon` dépendante des réglages Windows. Un
petit empilement de toasts en bas à droite du tableau de bord (fondu 5 s, cliquable) rendrait ces
événements visibles sans interrompre.

### 3.3 Un seul nom ▲

Titre de fenêtre "Omnéria Launcher", boîtes de dialogue "AL Launcher", exe "AL Launcher.exe",
texte de version "LAUNCHER v…", mutex `AL_Launcher`. À décider (probablement "AL Launcher"
partout, "Omnéria" restant la marque graphique) et à aligner — trivial mais visible.

### 3.4 Accessibilité minimale ▲

Les items de navigation sont des `Border` cliquables : pas de focus visuel au clavier, pas de rôle
"bouton" pour un lecteur d'écran (seul `AutomationProperties.Name` est posé). Les transformer en
`Button` avec un style dédié (`NavButtonStyle`) règle les deux et supprime `NavItem_KeyDown`.
Vérifier aussi le contraste `InkDim` (#8494B0) sur `Surface` (#0C1120) : ~5,5:1, correct, mais les
libellés en 9,5 px (`JOUEURS CONNECTÉS`) sont sous la taille lisible confortable.

### 3.5 Écran "hors ligne" assumé ▲

Sans réseau, tout échoue silencieusement (cartes vides, statut "hors ligne", JOUER qui échoue à
l'étape Forge). Un état explicite "Pas de connexion internet" en haut du tableau de bord (détecté
par `NetworkInterface.GetIsNetworkAvailable()` + échec simultané de tous les chargements initiaux)
évite au joueur de chercher un problème dans le launcher.

## 4. Exploitation

### 4.1 Rapport de problème plus complet ▲

"SIGNALER UN PROBLÈME" copie 30 lignes de `launcher.log`. Y ajouter les 50 dernières lignes de
`logs/latest.log` du jeu, la liste des fichiers de `mods/` avec leur taille, et la RAM physique
totale — ce sont les trois questions qu'on pose systématiquement ensuite sur Discord.

### 4.2 Un seul appel GitHub au lieu de deux ▲

`releases/latest` (mise à jour) et `releases?per_page=4` (changelog) contiennent la même
information : le premier élément non pré-release du second *est* la dernière release. Un
`GitHubReleasesClient` partagé, interrogé une fois par minute, alimenterait les deux services.
Moins urgent depuis les requêtes conditionnelles (v1.11.0), mais ça divise encore par deux le
trafic et supprime une classe.

### 4.3 Nettoyage des branches ▲

`claude/focused-babbage-z2gq3f`, `claude/minecraft-launcher-csharp-942eie` (local) et
`fix-version-1-8-0` (distante) sont mortes depuis la mise en place du duo `main`/`dev`. À
supprimer (`git push origin --delete <branche>`) pour que la liste des branches reflète le
workflow réel. Activer aussi la protection de `main` (PR obligatoire, CI verte requise) pour que
personne, y compris un outil, ne puisse y pousser directement.

## Ce qui a été volontairement écarté

- **Multi-comptes / multi-profils** : hors périmètre, usage privé mono-serveur.
- **Discord Rich Presence** : sympathique, mais ajoute une dépendance native pour un gain cosmétique.
- **Traduction** : tout est en français, l'audience aussi.
- **Passage à .NET 9/10** : rien à y gagner tant que `net8.0-windows` est en support (LTS jusqu'en
  novembre 2026) ; à planifier avant cette date.
