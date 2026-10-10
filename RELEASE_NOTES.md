<!-- Notes de la PROCHAINE release, en langage clair pour un joueur (pas un changelog technique) :
     une ligne "- ..." par changement visible pour lui, pas un résumé de ce qui a changé dans le code.
     Publiées telles quelles comme corps de la GitHub Release (voir .github/workflows/build-windows.yml,
     job "release", --notes-file ; ce commentaire est retiré avant publication) au lieu des notes
     auto-générées à partir des titres de PR (illisibles pour un joueur). Le build échoue exprès si ce
     fichier n'a pas changé depuis le tag précédent ou ne contient aucune ligne "- ..." (voir l'étape
     "Vérifie que RELEASE_NOTES.md a été mis à jour") : mets-le à jour à chaque fois, au même commit
     que <Version> dans le csproj, avant de poser le tag suivant. -->

- Nouvelle interface : accueil centré sur JOUER avec trois tuiles (actus, modpack, launcher), et pages ACTUS, SERVEUR, PARAMÈTRES, journaux et mentions légales dans la même fenêtre.
- Lancement : liste des étapes en direct, ANNULER, RÉESSAYER sur l'étape en échec ; notifications et dialogues intégrés au launcher.
- Page SERVEUR : disponibilité des dernières 24 h, joueurs connectés et depuis combien de temps, prévenir quand le serveur revient en ligne ou quand un ami se connecte.
- Nouveaux réglages : réduire le launcher pendant le jeu, sons, amis à surveiller. Serveur hors ligne : JOUER est grisé, LANCER QUAND MÊME reste possible.
