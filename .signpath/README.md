# Signature de l'exe avec SignPath

L'exe de chaque release peut être signé (Authenticode) par le programme gratuit
[SignPath Foundation](https://signpath.org) réservé aux projets open source. Tant que
le projet n'est pas accepté et configuré, les releases restent **non signées**,
exactement comme avant : rien ne casse.

## 1. Candidater

Formulaire : <https://signpath.org/apply>. Informations à fournir :

| Champ | Valeur |
|---|---|
| Dépôt | `https://github.com/Omneria/ALlauncher` |
| Licence | MIT (fichier `LICENSE` à la racine) |
| Ce qui est signé | `AL Launcher.exe`, launcher Minecraft Windows (.NET 8, WPF), construit par GitHub Actions |
| Politique de signature | section "Politique de signature du code" du `README.md` |

Conditions du programme (à relire sur leur site, elles évoluent) : licence open
source reconnue, projet maintenu et publié, build sur l'infrastructure CI
publique, **authentification à deux facteurs activée sur GitHub** pour chaque
personne qui peut pousser sur le dépôt, et politique de signature publiée.

## 2. Une fois accepté, côté SignPath

1. **Projet** : slug `ALlauncher`, dépôt GitHub `Omneria/ALlauncher` connecté
   (SignPath vérifie que l'artifact vient bien d'un run de ce dépôt).
2. **Artifact configuration** : slug `initial`, contenu de
   `.signpath/artifact-configuration.xml`.
3. **Signing policy** : slug `release-signing`, certificat de la SignPath
   Foundation, **approbation manuelle** par toi.
4. **Utilisateur CI** : créer un jeton d'API (CI user) avec le droit de
   soumettre des demandes sur ce projet.

Si tu choisis d'autres slugs, reporte-les dans les variables GitHub ci-dessous
(`SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG`,
`SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`), sinon ces valeurs par défaut suffisent.

## 3. Côté GitHub

Settings → Secrets and variables → Actions :

| Type | Nom | Valeur |
|---|---|---|
| Variable | `SIGNPATH_ORGANIZATION_ID` | l'ID d'organisation SignPath (Settings de l'organisation) |
| Secret | `SIGNPATH_API_TOKEN` | le jeton de l'utilisateur CI |

C'est la présence de `SIGNPATH_ORGANIZATION_ID` qui active l'étape de signature
dans le job `release` de `.github/workflows/build-windows.yml`.

## 4. À chaque release

Après `git push origin vX.Y.Z`, le job `release` envoie l'exe à SignPath et
**attend ton approbation** (jusqu'à une heure) : ouvre signpath.io, approuve la
demande, et la release GitHub se publie avec l'exe signé. Sans approbation dans
l'heure, le job échoue et aucune release n'est publiée : relancer le job.
