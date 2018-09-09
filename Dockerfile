FROM mono:latest

# Copy source files
COPY ./Common /build/Common
COPY ./Server /build/Server

# Copy required libraries
COPY ./SettingsParser.dll /build/
COPY ./MessageWriter2 /build/

# Compile the Server project
RUN msbuild /build/Server/Server.csproj /p:Configuration=Release

WORKDIR /server/

RUN cp -r /build/Server/bin/Release/* /server/

EXPOSE 6702

ENTRYPOINT ["mono", "/server/DMPServer.exe"]