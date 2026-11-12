# VoicemeeterBG

Affiche une image de fond dans **toutes les fenêtres Voicemeeter** (Voicemeeter, Banana, Potato, 32 et 64 bits).

Voicemeeter ne permet pas de changer son fond. VoicemeeterBG pose donc une fenêtre transparente par-dessus chaque fenêtre Voicemeeter, qui la suit et laisse passer tous les clics. Rien n'est modifié dans Voicemeeter : on quitte le programme et tout redevient comme avant.

## Installation

1. Double-cliquer sur `build.bat` : il crée `VoicemeeterBG.exe` avec le compilateur C# déjà présent dans Windows (rien à installer).
2. Lancer `VoicemeeterBG.exe` et choisir une image.

L'exécutable prêt à l'emploi est aussi disponible dans les *Releases* du dépôt.

## Utilisation

Le programme vit dans la zone de notification (près de l'horloge). Clic droit sur l'icône :

| Menu | Effet |
| --- | --- |
| Choisir l'image... | PNG, JPG, BMP, GIF (aussi par double-clic sur l'icône) |
| Activé | Affiche ou retire le fond |
| Opacité | Force de l'image, de 20 à 100 % |
| Mode | **Intelligent** : l'image n'apparaît que sur le fond sombre, les commandes restent nettes. **Simple** : voile uniforme sur toute la fenêtre, sans charge CPU |
| Détection du fond | Règle ce qui est considéré comme du fond en mode intelligent |
| Ajustement de l'image | Remplir, ajuster, étirer ou mosaïque |
| Inclure Macro Buttons | Applique aussi le fond à la fenêtre Macro Buttons |
| Lancer au démarrage de Windows | Démarre avec la session |

Les réglages sont enregistrés dans `%APPDATA%\VoicemeeterBG\config.ini` (on peut y changer `fps`, la fréquence de mise à jour du mode intelligent, 15 par défaut).

## Limites

- Windows 8.1 ou plus récent pour le mode intelligent.
- En mode intelligent, le masque est recalculé `fps` fois par seconde : un élément qui bouge vite peut être légèrement teinté par l'image pendant un instant.
- Si Voicemeeter est lancé en administrateur, VoicemeeterBG doit l'être aussi .
  
