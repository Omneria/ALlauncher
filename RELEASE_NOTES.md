<!-- Notes de la PROCHAINE release, en langage clair pour un joueur (pas un changelog technique) :
     une ligne "- ..." par changement visible pour lui, pas un résumé de ce qui a changé dans le code.
     Publiées telles quelles comme corps de la GitHub Release (voir .github/workflows/build-windows.yml,
     job "release", --notes-file) au lieu des notes auto-générées à partir des titres de PR (illisibles
     pour un joueur). Le build échoue exprès si ce fichier n'a pas changé depuis le tag précédent (voir
     l'étape "Vérifie que RELEASE_NOTES.md a été mis à jour") : mets-le à jour à chaque fois, au même
     commit que <Version> dans le csproj, avant de poser le tag suivant. -->

- Sépare les actus et les notes de version en deux cartes sur le tableau de bord (avant, une seule carte "Actus & changelog" n'affichait que les actus).
- Les notes de version affichées dans le launcher viennent maintenant directement des releases GitHub, comme sur le site.
- Toutes les informations qui se rafraîchissent automatiquement (statut du serveur, actus, maintenance, notes de version, avatar) se mettent maintenant à jour toutes les minutes, sans avoir à redémarrer le launcher.
