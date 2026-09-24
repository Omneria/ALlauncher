# Roadmap

État au 24 septembre 2026, après la publication de la **v1.11.0**. Chaque piste donne le
raisonnement qui la justifie, pour pouvoir trancher sans relire le code.

Légende effort : ▲ petit (une soirée), ▲▲ moyen (quelques jours), ▲▲▲ gros chantier.

## Livré en v1.11.0

Détail joueur dans `RELEASE_NOTES.md` (et la release GitHub), détail technique dans le README.

- **Robustesse** : filet global qui ne ferme plus le launcher une fois le tableau de bord affiché,
  rafraîchissements best-effort (`RunSafeAsync`), timeouts courts sur `news.txt`/`maintenance.txt`,
  chargements initiaux en parallèle, plus aucun travail long sur le thread UI (détection Java,
  extractions), barre de progression protégée contre NaN.
- **Synchro du modpack** : chemins du manifest confinés au dossier de jeu, empreinte vérifiée avant
  écriture, mods retirés du pack supprimés chez les joueurs.
- **HTTPS de bout en bout** : Caddy + Let's Encrypt sur le VPS, URL par défaut en `https://` sur le
  domaine, plus aucun repli ni service en clair.
- **Lancement** : orchestration sortie dans `LaunchPipeline` (testable), bouton ANNULER.
- **Serveur** : port 25566, migré automatiquement, écrit dans `servers.dat`.
- **GitHub** : requêtes conditionnelles (ETag) pour ne plus épuiser le quota de 60 requêtes/heure.
- **CI** : notes de release publiées depuis `RELEASE_NOTES.md` et contrôlées, Dependabot vers `dev`.

## Prochaine version : v1.12.0

Classées par priorité. Les deux premières ne sont pas des fonctionnalités mais évitent des
problèmes à court terme.

### 1. Passer à .NET 10 ▲▲ — urgent

**.NET 8 n'est plus maintenu après le 10 novembre 2026** : plus de correctifs de sécurité pour le
runtime embarqué dans l'exe autonome. .NET 10 est la version LTS suivante (supportée jusqu'en
novembre 2028). Changer `net8.0-windows` en `net10.0-windows` dans les deux csproj et
`dotnet-version` dans la CI, vérifier la compatibilité de CmlLib, MSAL et CmlLib.Core.Installer.Forge,
puis tester un lancement complet. Ne pas en profiter pour réactiver `InvariantGlobalization`
(crash silencieux de WPF, voir le csproj).

### 2. Analyseurs .NET et avertissements bloquants en CI ▲

`<EnableNETAnalyzers>`, `<AnalysisLevel>latest-recommended</AnalysisLevel>` et
`<TreatWarningsAsErrors>` dans le csproj, plus `dotnet format --verify-no-changes` en CI. Une
première salve d'avertissements sera à corriger (désactiver CA2007 ConfigureAwait, inadapté à WPF).
Empêche durablement les `catch` vides et les `async void` oubliés, à l'origine des régressions de
v1.9.x. À faire juste après la migration .NET 10, sur le même élan.

### 3. Builds de test identifiables ▲

Un exe de test de `dev` porte déjà le numéro de la version à venir : une fois la release publiée,
son auto-update ne voit rien de plus récent et le joueur reste sur un build intermédiaire (vécu en
v1.11.0). La CI pourrait marquer les builds hors tag (`-p:InformationalVersion=X.Y.Z-dev.<run>`) et
`GitHubUpdateService` traiter un build `-dev` comme antérieur à la release de même numéro. Le
launcher afficherait aussi "v1.12.0-dev" dans la barre latérale, ce qui évite toute confusion.

### 4. Compatibilité et latence du serveur ▲

Le Server List Ping renvoie déjà `version.protocol`, `version.name`, le MOTD et permet de mesurer
la latence, tous ignorés par `ServerStatusService.ParseStatus`. Comparer le protocole à celui de
`MinecraftVersion` (1.20.1 = 763) avertirait *avant* un lancement de plusieurs minutes voué à finir
en "Incompatible client" (le scénario de la migration 1.16.5 → 1.20.1). Afficher latence et MOTD
dans la carte serveur. Prépare aussi la page SERVEUR de la v2.0.0.

### 5. Changelog du modpack visible ▲

`changelog.txt` n'est affiché que dans le journal masqué du panneau de chargement : personne ne le
voit. `generate-manifest.py` peut comparer l'ancien et le nouveau manifest (mods ajoutés, retirés,
mis à jour) et écrire `changelog.txt` tout seul ; le launcher l'afficherait dans la carte ACTUS
avec un badge MODPACK.

### 6. Rapport de problème plus complet ▲

"SIGNALER UN PROBLÈME" copie 30 lignes de `launcher.log`. Y ajouter les 50 dernières lignes de
`logs/latest.log` du jeu, la liste des fichiers de `mods/` et la RAM physique : ce sont les trois
questions posées systématiquement ensuite sur Discord.

### 7. État "pas de connexion internet" ▲

Sans réseau, tout échoue en silence (cartes vides, serveur hors ligne, JOUER qui échoue à Forge).
Un bandeau explicite, déclenché quand tous les chargements initiaux échouent ensemble, évite au
joueur de chercher une panne dans le launcher.

### 8. Un seul appel GitHub au lieu de deux ▲

`releases/latest` (mise à jour) et `releases?per_page=4` (changelog) portent la même information.
Un client partagé divise encore le trafic par deux et supprime une classe. Faible priorité depuis
les requêtes conditionnelles de la v1.11.0.

### 9. Rollback durable en mode manifest ▲▲

"REVENIR EN ARRIÈRE" est annulé par la synchro suivante. Le VPS pourrait publier
`manifest-previous.json` (renommé par `generate-manifest.py` avant d'écrire le nouveau) et le
launcher mémoriser "épinglé sur la version précédente" jusqu'au prochain changement du pack.

## Réservé à la v2.0.0 : refonte de l'interface

Maquettes : <https://claude.ai/artifact/G94daoRRiq13jpF97HV3Qb> (lien privé, à partager depuis la
page si besoin). Mise de côté volontairement après la v1.11.0.

- **Navigation par pages** dans une seule fenêtre (HUB, ACTUS, SERVEUR, PARAMÈTRES) au lieu de
  quatre fenêtres sans chrome.
- **Hub** centré sur un grand bouton JOUER, avec la liste des étapes de `LaunchPipeline`.
- **Dialogues à la charte** à la place des `MessageBox` Windows, et **toasts** non bloquants.
- **Accessibilité** : vrais boutons pour la navigation (focus clavier, lecteur d'écran), aucun
  texte sous 11 px.
- **Technique** : MVVM, dictionnaires de ressources séparés, contrôles réutilisables
  (`BevelBorder`, `StatusPill`, `StepList`).

Deux décisions à prendre avant de commencer :
1. **Un seul nom** : aujourd'hui "Omnéria Launcher" (titre de fenêtre) et "AL Launcher" (exe,
   dialogues, mutex) coexistent.
2. **Actus du modpack** dans la page ACTUS : dépend de la piste 5 ci-dessus.

## Exploitation et VPS

- **Fichiers inutiles servis publiquement** : `generate-manifest.py` et l'ancien zip 1.16.5 sont
  téléchargeables depuis `/modpack/`. Sans danger, mais à déplacer hors de `/var/www/html`.
- **Redémarrage du VPS** en attente pour charger le nouveau noyau (Caddy repart seul, nginx est
  désactivé).
- **Protection de `main`** (optionnel) : exiger une CI verte avant tout push. Ne pas exiger de pull
  request, sinon le flux actuel (merge de `dev` dans `main` puis tag) ne fonctionne plus.

## Écarté

- **Signature de l'exe (Authenticode)** : plus de fichier .pfx possible depuis 2023, services
  payants ou contraignants (SignPath Foundation étudié puis abandonné), et SmartScreen avertit de
  toute façon sans réputation. Le `.sha256` des releases reste le contrôle d'intégrité des mises
  à jour.
- **Multi-comptes / multi-profils** : hors périmètre, usage privé mono-serveur.
- **Discord Rich Presence** : dépendance native pour un gain cosmétique.
- **Traduction** : tout est en français, l'audience aussi.
