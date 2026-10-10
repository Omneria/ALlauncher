<!-- Notes de la PROCHAINE release, en langage clair pour un joueur (pas un changelog technique) :
     une ligne "- ..." par changement visible pour lui, pas un résumé de ce qui a changé dans le code.
     Publiées telles quelles comme corps de la GitHub Release (voir .github/workflows/build-windows.yml,
     job "release", --notes-file ; ce commentaire est retiré avant publication) au lieu des notes
     auto-générées à partir des titres de PR (illisibles pour un joueur). Le build échoue exprès si ce
     fichier n'a pas changé depuis le tag précédent ou ne contient aucune ligne "- ..." (voir l'étape
     "Vérifie que RELEASE_NOTES.md a été mis à jour") : mets-le à jour à chaque fois, au même commit
     que <Version> dans le csproj, avant de poser le tag suivant. -->

- Les libellés (titres, boutons, pastilles) ont maintenant l'espacement de lettres de la maquette.
- Page SERVEUR : la tête de chaque joueur connecté s'affiche à côté de son pseudo.
- Interface plus fiable : l'accueil, le lancement, la page SERVEUR et les Paramètres sont désormais vérifiés par des tests automatiques.
