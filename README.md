# Multiplayer Platformer Game

This repository contains the C\# scripts for a multiplayer platformer party game built in Unity. Players first build the level together by placing items, and then they race to the finish.

### Important Note

This repository **only contains the C\# scripts**. To respect the ownership of third-party assets, no models, sounds, or visual art are included. You will need to use your own assets to run the project.

-----

## Gameplay

The game is played in rounds with a simple, engaging loop:

1.  **Build**: At the start of a round, each player chooses an item and places it on the map.
2.  **Race**: Once all items are placed, the timer starts and everyone races through the custom-built level to reach the finish line.
3.  **Score**: Players earn stars for finishing. The first to reach the target score wins\!

## Core Features

- **Real-time Multiplayer**: Using Unity Netcode for GameObjects.  
  <details>
    <summary>▶️ Watch: Player Synchronization</summary>

    ![dance](https://github.com/user-attachments/assets/20f98705-2bf4-46ed-b71f-04950ca7c619)
  </details>

- **Lobby & Matchmaking**: Powered by Unity Gaming Services (UGS) for easy connecting.  
  <details>
    <summary>▶️ Watch: Lobby System</summary>

    ![Lobby](https://github.com/user-attachments/assets/298ba922-64d0-4cc0-a1ca-f4244ee55e3f)
  </details>

- **Dynamic Level Building**: Players build the course together before each round.  
  <details>
    <summary>▶️ Watch: Level Building Phase</summary>

    ![Level Building](https://github.com/user-attachments/assets/467bf5e7-33b3-4ca4-aef7-5f07c025eee4)
  </details>

- **Interactive Items**: Placeable cannons, bounce pads, hazards, and more to create chaotic fun.  
  <details>
    <summary>▶️ Watch: Interactive Items</summary>

    ![Items](https://github.com/user-attachments/assets/cb7d3ac4-c67c-4dba-949f-af7adc85ee92)
  </details>

- **Scoring and Round System**: Keeps the competition engaging round after round.
