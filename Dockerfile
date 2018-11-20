FROM mono:latest

COPY . /tmp/build/

# Compile the Server project
RUN msbuild /tmp/build/Server/Server.csproj /p:Configuration=Release && \
    mkdir /opt/dmp/ && \
    cp -r /tmp/build/Server/bin/Release/* /opt/dmp/ && \
    rm -r /tmp/build

WORKDIR /opt/dmp

EXPOSE 6702

ENTRYPOINT ["mono", "/opt/dmp/DMPServer.exe"]
