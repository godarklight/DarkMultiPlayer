DarkMultiPlayer 0.1.0.0.  
  
DarkMultiPlayer is a multiplayer mod for KSP 0.23.5. It supports subspace-style (and master controlled warp) warping & career mode, with an easy-to-edit server database.  
  
The DarkMultiPlayer server and client is cross platform, it runs under mono on linux and mac.
  
  
===Client===  
---Installation / Updating---  
Option a) Extract the DarkMultiPlayer zip to GameData.  
Option b) Download DMPUpdater from http://chrisand.no-ip.info/dmp/downloads/dmpupdater/, place the program next to KSP.exe (Or KSP.app/KSP.x86_64), and run it.  
  
---Connecting to a server---  
The connection window will appear on the main menu. Type in a username, press add server, type in the details, and then hit add.  
A player token will be generated (At GameData/DarkMultiPlayer/Plugins/Data/token.txt) during the first DMP start. DMP registers your username to the server with this token. If you lose your token, you will no longer be able to connect to the server with that username until the server admin deletes you from Universe/Players/.  
  
---Connecting, If you are also running a server---  
If you are running the server locally, you will want to connect to 127.0.0.1/localhost.  
If you are running the server on a networked computer, you will most likely be connecting to a 10.x.x.x or 192.168.x.x address.  
Do not type in your public IP, this usually refers to your modem, which won't be running the DMP server (Unless you have the public IP address in your computer, which is rare).  
  
---Mods---  
If you try to connect to a modded server, DMP will tell you everything you are missing, and tell you everything you shouldn't have in order to connect. If there is any DLL's that need to be added, you will need to restart KSP.  
  
---Warping---  
Both regular and phyiscs warp are available. When you warp, you will be put into your own "time" (called a subspace) which other players can sync to. All updates from the past affect future players, but future players will not affect players in the past.  
  
  
  
===Server===  
---Installation---  
Option a) Extract the DarkMultiPlayer zip to a folder, Not under the KSP or GameData folders.  
  
---Updating---  
Option a) Extract the zip as above  
Option b) Place DMPUpdater next to DMPServer.exe and run it.  
  
---Options---  
The file is located at DMPServerSettings.txt next to DMPServer.exe. You will need to edit this file while the server is offline. The file is created on the first server start.
port: The port to listen on, default 6702.  
  
warpmode: The warp type.  
Mode 0: MCW_FORCE, You take a warp lock, every player will follow you into warp, and you will create a subspace when you come out of warp that everyone will sync to.  
Mode 1: MCW_VOTE: Same as MCW_FORCE, but you have to vote first - This option may be good for players doing the same thing with voice chat.  
Mode 2: MCW_LOWEST: NOT IMPLEMENTED - Warps to the lowest common warp factor.  
Mode 3: SUBSPACE_SIMPLE: NOT IMPLEMENTED - Allows you to create a subspace in the future, but you can only sync to the latest player with the '>' key.  
Mode 4: SUBSPACE: The default, and most important mode. Each player can warp at will, and they can "sync" to other players times. This is the only mode that allows QuickSaving/QuickLoading  
Mode 5: NONE: Players will be unable to warp.  
  
gamemode:  
Mode 0: SANDBOX - The default sandbox game mode.  
Mode 1: CAREER - Everyone has their own career mode points. Shared science is currently not implemented.  
  
---Mods---  
Read DMPModControl.txt, it's commented/documented. The file can be copied from a *development* KMPServer (The one where you can use SHA sums, not the one with the !md5 section) - The file format is the same.  
  
If you are running a private server, it's safe enough to just add the missing parts.  