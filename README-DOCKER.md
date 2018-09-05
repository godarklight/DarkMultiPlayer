# DarkMultiPlayer

DarkMultiPlayer is a multiplayer mod for Kerbal Space Program. It supports subspace style (and master controlled warp) warping and career mode, with an easy to edit server database.

## Build and start Container

* Copy the following assemblies from `[KSP root folder]/KSP_Data/Managed`:
  * `Assembly-CSharp`
  * `Assembly-CSharp-firstpass`
  * `UnityEngine`
  * `UnityEngine.UI`
* Paste the copied assemblies where `DarkMultiPlayer.sln` is located at.
* Run following command to build the Container.

```bash
docker build -t dmp-server .
```

* To run the server use this:

```bash
docker run -d -p 6702:6702 dmp-server
```

## Run with persistant volumes

If you want to edit the configuration files and your save to be persistant, you can map the config directory on your filesystem and use an named volume for the save. Substitute `[path/on/host]` with your actual path.

```bash
docker volume create dmpsave
docker run -d \
  -p 6702:6702 \
  -v [path/on/host]:/DMP/Config \
  -v dmpsave:/DMP/Universe \
  dmp-server
```

## Sample `docker-compose.yml`

A sample `docker-compose.yml`, if you want to manage your containers with docker-compose. The path after build should match your folder with the sources.

```docker-compose
version: '2'

services:
    server:
        build: ./DarkMultiPlayer
        image: dmp-server
        container_name: dmp-server
        ports:
         - "6702:6702"
        volumes:
         - "[path/on/host]:/DMP/Config"
         - "dmpsave:/DMP/Universe"
```