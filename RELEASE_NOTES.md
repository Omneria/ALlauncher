<!-- Notes de la PROCHAINE release, en langage clair pour un joueur (pas un changelog technique) :
     une ligne "- ..." par changement visible pour lui, pas un résumé de ce qui a changé dans le code.
     Publiées telles quelles comme corps de la GitHub Release (voir .github/workflows/build-windows.yml,
     job "release", --notes-file ; ce commentaire est retiré avant publication) au lieu des notes
     auto-générées à partir des titres de PR (illisibles pour un joueur). Le build échoue exprès si ce
     fichier n'a pas changé depuis le tag précédent ou ne contient aucune ligne "- ..." (voir l'étape
     "Vérifie que RELEASE_NOTES.md a été mis à jour") : mets-le à jour à chaque fois, au même commit
     que <Version> dans le csproj, avant de poser le tag suivant. -->

- Le lancement affiche ses étapes (Java, Forge, mods, connexion, jeu) avec celle qui est en cours, et un bouton RÉESSAYER si l'une d'elles échoue.
- Les questions et les erreurs du launcher s'affichent dans la fenêtre au lieu d'ouvrir des boîtes Windows ; les informations (serveur de nouveau en ligne, connexion rétablie, lancement annulé) apparaissent en notification qui disparaît toute seule.
- Textes secondaires plus lisibles.
- Paramètres, journaux et mentions légales s'ouvrent dans la fenêtre du launcher au lieu de fenêtres séparées. Les paramètres sont rangés en JEU, LAUNCHER, MAINTENANCE et À PROPOS.
- Nouvel accueil centré sur JOUER : grande carte serveur avec le bouton JOUER, et trois tuiles en dessous (actus, mises à jour du modpack, notes du launcher).
