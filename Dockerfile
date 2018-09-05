FROM mono:latest

EXPOSE 6702

COPY . /DarkMultiPlayer/

RUN msbuild /DarkMultiPlayer/DarkMultiPlayer.sln /p:Configuration=Release

WORKDIR /DMP

RUN cp /DarkMultiPlayer/Server/bin/Release/* /DMP

ENTRYPOINT [ "mono", "/DMP/DMPServer.exe" ]