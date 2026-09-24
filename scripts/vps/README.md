# Outils VPS (hors périmètre du launcher lui-même)

Ces scripts ne font pas partie de l'application C# (pas de dépendance depuis
`src/`) : ils servent à produire les fichiers que le launcher va lire côté VPS
pour le mode manifest et la bannière de maintenance. Voir le README principal,
sections "Mode manifest (synchro incrémentale)" et "Bannière de maintenance".

## Manifest (synchro incrémentale)

`generate-manifest.py` parcourt `mods/` et `config/` sous `--root`, calcule le
SHA-256 de chaque fichier et écrit le `manifest.json` attendu par
`ModpackManifest.cs`.

```bash
python3 generate-manifest.py \
    --root /var/www/modpack \
    --base-url http://185.185.82.180/modpack/files \
    --output /var/www/modpack/manifest.json
```

- `--root` : dossier contenant `mods/` et `config/` (les seuls sous-dossiers
  pris en compte — le launcher ne synchronise que ceux-là).
- `--base-url` : URL HTTP de base sous laquelle ces fichiers sont déjà servis
  statiquement (nginx/Apache). Chaque fichier est publié à
  `<base-url>/<chemin relatif>` — vérifier que ça correspond à la config du
  serveur web avant de pointer `ModpackManifestUrl` dessus.
- `--output` : où écrire `manifest.json`. Doit être accessible en HTTP à
  l'URL que vous mettrez dans `ModpackManifestUrl` (settings.json du
  launcher).

À relancer à chaque mise à jour du modpack (ajout/suppression/modification
d'un mod ou d'un fichier de config) : le launcher compare les hash au fichier
tel qu'il est au moment de la synchro, un manifest périmé ferait retélécharger
ou, pire, laisserait un fichier obsolète non détecté.

Aucune dépendance externe, Python 3 standard suffit.

## Bannière de maintenance

`maintenance.txt.example` est un modèle prêt à copier :

```bash
cp maintenance.txt.example /var/www/modpack/maintenance.txt
# éditer la première ligne (titre), la ligne FIN: (optionnelle) et le sous-titre
```

Format lu par le launcher (`RefreshMaintenanceBannerAsync`) :
- **1ère ligne** → titre en gras dans la bannière.
- **une ligne `FIN: <valeur>`** (n'importe où, optionnelle) → affichée dans le bloc "FIN ESTIMÉE"
  à droite de la carte (`<valeur>` est un texte libre : une heure `23:00`, une date, "indéterminée"...).
  Absente = ce bloc ne s'affiche pas.
- **les autres lignes** (optionnelles) → sous-titre, affiché en dessous du titre.

Publier ce fichier à l'URL configurée dans `MaintenanceMessageUrl`
(settings.json) fait apparaître la bannière sur le dashboard au prochain
rafraîchissement (démarrage du launcher, ou dans les 5 minutes qui suivent).
**Supprimer ou vider le fichier** fait disparaître la bannière (absence =
ignorée silencieusement, comme le changelog).
