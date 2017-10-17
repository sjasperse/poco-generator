dotnet restore src/
dotnet build src/ --runtime osx.10.10-x64
./src/bin/Debug/netcoreapp1.1/osx.10.10-x64/poco-generator "$@"