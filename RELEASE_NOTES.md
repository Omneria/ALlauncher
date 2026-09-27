<!-- Notes de la PROCHAINE release, en langage clair pour un joueur (pas un changelog technique) :
     une ligne "- ..." par changement visible pour lui, pas un résumé de ce qui a changé dans le code.
     Publiées telles quelles comme corps de la GitHub Release (voir .github/workflows/build-windows.yml,
     job "release", --notes-file ; ce commentaire est retiré avant publication) au lieu des notes
     auto-générées à partir des titres de PR (illisibles pour un joueur). Le build échoue exprès si ce
     fichier n'a pas changé depuis le tag précédent ou ne contient aucune ligne "- ..." (voir l'étape
     "Vérifie que RELEASE_NOTES.md a été mis à jour") : mets-le à jour à chaque fois, au même commit
     que <Version> dans le csproj, avant de poser le tag suivant. -->

- Le launcher s'appelle désormais Omnéria Games.
- Les réglages que tu changes dans les mods (minicarte JourneyMap, Quark, JEI...) ne sont plus remis à zéro à chaque lancement.
- La carte ACTUS affiche les mises à jour du modpack (mods ajoutés, mis à jour ou retirés), avec un badge MODPACK.
- Plus de RAM par défaut pour le nouveau modpack : 4 Go sur un PC de 8 Go, 6 Go sur 12 Go, 8 Go au-delà. Si tu n'avais jamais touché au réglage, il est ajusté tout seul ; les Paramètres indiquent ce que le modpack conseille, et le launcher te prévient avant de lancer si c'est trop peu.
- La carte du serveur affiche ta latence, le message du serveur, et prévient si le serveur passe à une autre version de Minecraft.
- Le launcher suit automatiquement la version de Forge du serveur.
- Passage à .NET 10 : le launcher reste à jour côté sécurité après l'arrêt de .NET 8.
