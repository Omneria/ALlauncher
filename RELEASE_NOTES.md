<!-- Notes de la PROCHAINE release, en langage clair pour un joueur (pas un changelog technique) :
     une ligne "- ..." par changement visible pour lui, pas un résumé de ce qui a changé dans le code.
     Publiées telles quelles comme corps de la GitHub Release (voir .github/workflows/build-windows.yml,
     job "release", --notes-file ; ce commentaire est retiré avant publication) au lieu des notes
     auto-générées à partir des titres de PR (illisibles pour un joueur). Le build échoue exprès si ce
     fichier n'a pas changé depuis le tag précédent ou ne contient aucune ligne "- ..." (voir l'étape
     "Vérifie que RELEASE_NOTES.md a été mis à jour") : mets-le à jour à chaque fois, au même commit
     que <Version> dans le csproj, avant de poser le tag suivant. -->

- Les mises à jour du modpack se téléchargent beaucoup plus vite : plusieurs mods à la fois, avec la progression en Mo.
- « Revenir en arrière » (Paramètres) tient enfin : la version précédente du modpack est gardée jusqu'à la prochaine mise à jour publiée, au lieu d'être annulée au lancement suivant. « Réparer le modpack » revient à la version du serveur.
