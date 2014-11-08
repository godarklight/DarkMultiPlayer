#DarkMultiPlayer 0.1.6.2.

DarkMultiPlayer is a multiplayer mod for KSP 0.25. It supports subspace-style (and master controlled warp) warping & career mode, with an easy-to-edit server database.  
  
The DarkMultiPlayer server and client is cross platform, it runs under mono on linux and mac.  
  
  
##Client

###Installation / Updating
Option a) Extract the [DarkMultiPlayer zip](https://kerbalstuff.com/mod/4) to GameData.  
Option b) Download DMPUpdater from http://godarklight.info.tm/dmp/downloads/dmpupdater/, place the program next to KSP.exe (Or KSP.app/KSP.x86_64), and run it.  

####LINUX BUG
The linux version of KSP ships with a bugged libpng, which can be disabled by hex editing the executable.  
  
Make sure the sha256 sum is correct, these instructions are specifically for 0.25  

    sha256sum KSP.x86_64
    17d395c8b2b30e2fdcca65a0078859f0422f1e36ba509f6bbf2f56b68b36148a KSP.x86_64
  
Patch the executable  

    echo "0099f587: 00" | xxd -r - KSP.x86_64
    echo "0099f58c: 00" | xxd -r - KSP.x86_64

Check it patched correctly:  

    sha256sum KSP.x86_64
    7342792efa6e0390ceba1db7b92dce84811091d8436087f27697073a25277f92 KSP.x86_64
  
###Connecting to a server
The connection window will appear on the main menu. Type in a username, press add server, type in the details, and then hit add.  
A player keypair will be generated (At GameData/DarkMultiPlayer/Plugins/Data/private.txt) during the first DMP start. DMP registers your username to the server with this keypair. If you lose your keypair, you will no longer be able to connect to the server with that username until the server admin deletes you from Universe/Players/.  
  
###Connecting, If you are also running a server
If you are running the server locally, you will want to connect to 127.0.0.1/localhost.  
If you are running the server on a networked computer, you will most likely be connecting to a 10.x.x.x or 192.168.x.x address.  
Do not type in your public IP, this usually refers to your modem, which won't be running the DMP server (Unless you have the public IP address in your computer, which is rare).  
  

###Flag sharing
DMP will sync any flags under the GameData/DarkMultiPlayer/Flags folder - Put your local flags in here if you want other players to see them. All other flags will work in any location if all players have the flags installed, but it will get reset to default if a player gets near a vessel with a flag they do not have.
  
###Chat window
By default, you will join the Global channel (and cannot leave it).  
To PM a player, type /query playername or click on their player name in the global chat.  
To start a group chat, type /join groupname.  
To leave a PM or group chat, type /part or /leave in the window you want to leave. You can also press the 'Leave' button.  
Group chats are indicated by the '#' mark, player chats are indicated by the '@' mark.  
Newlines can be entered in the chat window with shift+enter.  
  
###Craft sharing window
To upload a craft, press on your user name and then click 'Upload'.  
To download a craft, press on the owning player's username and select one of their uploaded vessels.  
  
###Screenshot sharing window
To upload a screenshot, press 'Upload'.  
To view a players screenshot, press on their player name.  
If the server has screenshot saving enabled, The players name will go red when there is a new screenshot to view.  
  
###Mods
If you try to connect to a modded server, DMP will tell you everything you are missing, and tell you everything you shouldn't have in order to connect. If there is any DLL's that need to be added, you will need to restart KSP.  
  
###Warping
Both regular and phyiscs warp are available. When you warp, you will be put into your own "time" (called a subspace) which other players can sync to. All updates from the past affect future players, but future players will not affect players in the past.  
  
  
##Server

###Installation
Option a) Extract the DarkMultiPlayer zip to a folder, Not under the KSP or GameData folders.  
  
###Updating
Option a) Extract the zip as above  
Option b) Place DMPUpdater next to DMPServer.exe and run it.  
  
###Server console commands
admin           - Sets a player as admin/removes admin from the player  
ban             - Bans a player from the server  
banip           - Bans an IP Address from the server  
bankey          - Bans a Guid from the server  
connectionstats - Displays network traffic usage  
countclients    - Counts connected clients  
dekessler       - Clears out debris from the server  
exit            - Shuts down the server  
help            - Displays this help  
kick            - Kicks a player from the server  
listclients     - Lists connected clients  
nukeksc         - Clears ALL vessels from KSC and the Runway  
pm              - Sends a message to a player  
quit            - Shuts down the server  
restart         - Restarts the server  
say             - Broadcasts a message to clients  
shutdown        - Shuts down the server  
whitelist       - Change the server whitelist  
  
###Options
The file is located at DMPServerSettings.txt next to DMPServer.exe. You will need to edit this file while the server is offline. The file is created on the first server start.  
  
###address
address - The address the server listens on  
WARNING: You do not need to change this unless you are running 2 servers on the same port.  
Changing this setting from 0.0.0.0 will only give you trouble if you aren't running multiple servers.  
  
####port
The port to listen on, default 6702.  
  
####warpmode
The warp type.  
- Mode 0: MCW_FORCE, You take a warp lock, every player will follow you into warp, and you will create a subspace when you come out of warp that everyone will sync to.  
- Mode 1: MCW_VOTE: Same as MCW_FORCE, but you have to vote first - This option may be good for players doing the same thing with voice chat.  
- Mode 2: MCW_LOWEST: NOT IMPLEMENTED - Warps to the lowest common warp factor.  
- Mode 3: SUBSPACE_SIMPLE: NOT IMPLEMENTED - Allows you to create a subspace in the future, but you can only sync to the latest player with the '>' key.  
- Mode 4: SUBSPACE: The default, and most important mode. Each player can warp at will, and they can "sync" to other players times. This is the only mode that allows QuickSaving/QuickLoading  
- Mode 5: NONE: Players will be unable to warp.  
  

####gamemode
- Mode 0: SANDBOX - The default sandbox game mode.  
- Mode 1: SCIENCE - Everyone has their own science points. Shared science is currently not implemented.  
- Mode 2: CAREER - Everyone has their own career mode points and funds. Shared science is currently not implemented.  
  
####whitelisted
Enable whitelisting on the server. The commands are /whitelist [add|del] playername or /whitelist show
- 0 : Off  
- 1 : On  
  

####modcontrol
Enables or disables modcontrol - Only turn this off if you are running a private server where everyone has the same mods.  
- 0 : Off  
- 1 : On (Don't sync vessels with invalid parts)  
- 2 : On (Prevent vessels with invalid parts from launching)  
  
  
####useutctimeinlog
Use UTC instead of system time in the log. This is useful if you want to co-ordinate logging between the server and client.  


####loglevel
Minimum log level to display. While DMP is in alpha, it's recommended to leave this on DEBUG (0) if you want to submit bug reports ;).
  
  
####screenshotsperplayer
Number of screenshots to save. You need to have a number higher than -1 in order for players to view screenshots for players they are not currently watching.  
- -1 is disabled  
- 0 is unlimited.  
  
  
####screenshotheight
The height of the screenshot in pixels.  
  
  
####cheats
Enable use of cheats ingame.
- 0 : Off  
- 1 : On  
  
####httpport
HTTP port for server status. 0 = Disabled
  
####servername
Name of the server. This is the name that shows up in the JSON output (and server list if added)  
  
####maxplayers
Maximum amount of players that can join the server.  
  
####screenshotdirectory
Specify a custom screenshot directory.  
This directory must exist in order to be used. Leave blank to store it in Universe.  
  
####autonuke
Specify in minutes how often /nukeksc automatically runs. 0 = Disabled  
  
####autodekessler
Specify in minutes how often /dekessler automatically runs. 0 = Disabled  
  
####numberofasteroids
How many untracked asteroids to spawn into the universe. 0 = Disabled  
  
####consoleidentifier
Specify the name that will appear when you send a message using the server's console.  
  
####servermotd
Specify the server's MOTD (message of the day).  
  
  
##Mods
Read DMPModControl.txt, it's commented/documented. The file can be copied from a *development* KMPServer (The one where you can use SHA sums, not the one with the !md5 section) - The file format is the same.  
  
If you are running a private server, it's safe enough to just add the missing parts.  
  
You can get the DMP client to make a DMPModControl.txt file specific for your gamedata directory by pressing Options -> Generate DMPModControl.txt file.
The whitelist option will only allow you to connect with the mods in your gamedata directory.
The blacklist option will allow you to connect with any mods.
