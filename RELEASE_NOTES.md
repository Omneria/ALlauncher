<!-- Notes de la PROCHAINE release, en langage clair pour un joueur (pas un changelog technique) :
     une ligne "- ..." par changement visible pour lui, pas un résumé de ce qui a changé dans le code.
     Publiées telles quelles comme corps de la GitHub Release (voir .github/workflows/build-windows.yml,
     job "release", --notes-file ; ce commentaire est retiré avant publication) au lieu des notes
     auto-générées à partir des titres de PR (illisibles pour un joueur). Le build échoue exprès si ce
     fichier n'a pas changé depuis le tag précédent ou ne contient aucune ligne "- ..." (voir l'étape
     "Vérifie que RELEASE_NOTES.md a été mis à jour") : mets-le à jour à chaque fois, au même commit
     que <Version> dans le csproj, avant de poser le tag suivant. -->

- Le serveur Astral Nexus a changé de port (25566) : le launcher s'y connecte automatiquement, rien à configurer.
- Les mods retirés du pack côté serveur sont maintenant aussi retirés de ton ordinateur à la synchro (avant, ils restaient et pouvaient empêcher de rejoindre le serveur).
- Chaque mod téléchargé est vérifié avant d'être installé : un téléchargement abîmé est refusé au lieu de faire planter le jeu plus tard.
- Le launcher ne se fige plus pendant la détection de Java et l'extraction du modpack après un clic sur JOUER.
- Une erreur dans une actu, le changelog ou la bannière de maintenance ne peut plus fermer le launcher : elle est journalisée et le launcher continue.
- La bannière de maintenance reste affichée si le serveur ne répond plus, au lieu de disparaître pile pendant la coupure qu'elle annonce.
- Le numéro de version dans la carte CHANGELOG est cliquable et ouvre la release sur GitHub.
- Un bouton ANNULER permet d'interrompre un lancement bloqué (téléchargement qui n'avance plus) sans fermer le launcher ; le lancement suivant reprend là où il en était.
- Le launcher télécharge maintenant les mods en HTTPS (connexion chiffrée).
- Le tableau de bord s'affiche plus vite au démarrage (statut serveur, actus, changelog, session chargés en parallèle) et la mise à jour du launcher affiche sa progression en pourcentage.
