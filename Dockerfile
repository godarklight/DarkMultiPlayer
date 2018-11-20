FROM mono:latest

COPY . /tmp/build/

RUN ls /tmp/build

# Compile the Server project
RUN msbuild /tmp/build/Server/Server.csproj /p:Configuration=Release

WORKDIR /server/

RUN cp -r /tmp/build/Server/bin/Release/* /server/

RUN rm -r /tmp/build

EXPOSE 6702

ENTRYPOINT ["mono", "/server/DMPServer.exe"]
