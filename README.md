# FastCodeNav (currently only for C# on Windows for repos in Azure DevOps)

The vision for this extension is to enable code navigation almost immediately after VSCode is launched even for very big codebases. The idea is to utilize pre-cached symbols index for repositories provided by an online code search service and quickly handle commands like 'Go to Symbol', 'Find All References', 'Go to Definition'. The online index might not be fully up to date but for big codebases it is often good enough to allow code base navigation without waiting for source code to be parsed by the relevant VSCode language server.

The current implementation has the following key dependencies/assumptions/limitations:
- Only C# codebases are supported. Instead of implementing its own language server FastCodeNav plugs into C# extension (using OmniSharp's extensibility) to prioritize symbol information as it becomes available in the workspaces over the online index.
- The only code search service provider currently supported is [Azure DevOps](https://docs.microsoft.com/en-us/rest/api/azure/devops/search/) since Microsoft has quite a few internal big repos hosted by the service. Other providers to consider adding in the future: [GitHub](https://developer.github.com/v3/search/#search-code), [Codex](https://github.com/Ref12/Codex).
- Only Windows is supported at the moment but because the extension is written in TypeScript and .NET Core it should be quite easy to enable the extension for other platforms (after figuring out auth).

## Prepare
- Install [Node.js](https://nodejs.org/)
- Install [.NET Core SDK](https://dotnet.microsoft.com/download)
- Unless the whole Visual Studio 2019 is already installed, get [Build Tools for Visual Studio 2019](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2019).
- [VSCode](https://code.visualstudio.com/download) and [C# extension](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp)

## Develop
In VSCode open fastcodenav.code-workspace workspace to get access to Tasks exposed by both VSCode extension and OmniSharp plugin.

To open the OmniSharp plugin in Visual Studio on Windows, go to omnisharp directory and run `dotnet msbuild /t:slngen`. This will generate dirs.sln and open the solution in Visual Studio. Run `dotnet msbuild /t:slngen /p:SlnGenLaunchVisualStudio=false` to generate dirs.sln file without launching the IDE. The solution file should not be checked in because it can always be re-generated based on MSBuild projects graph.

## Build
The extension consists of two parts - the actual VSCode extension and OmniSharp plugin. Open 'Developer Command Prompt for VS 2019' and build the extension using the following commands:
```
pushd extension
npm i
npm run compile
popd
pushd omnisharp
dotnet restore --interactive
dotnet build /p:Configuration=Release
msbuild /t:publish /p:Configuration=Release
popd
```
- Replace 'Release' with 'Debug' to build 'Debug' flavor.
- 'Developer Command Prompt for VS 2019' is only needed for the last publishing step because at the moment 'dotnet publish' copies incorrect versions of plugin assemblies.

## Run/Debug
- From VSCode select and run 'Run Extension' configuration.
- In the launched [Extension Development Host] VSCode instance press `F1` and run `FastCodeNav: Enable plugin for C#` command. This needs to be done only once to activate OmniSharp plugin.
- To debug plugin activation use `omnisharp.waitForDebugger` VSCode setting.

## Create VSIX
```
pushd extension
vsce package --out out
```

## Install VSIX
`code --install-extension <path-to-fastcodenav-N.N.N.vsix>`

## Uninstall VSIX
`code --uninstall-extension <path-to-fastcodenav-N.N.N.vsix>`