# MSBuild Targets Visual Studio Extension

Allows the execution of custom MSBuild project targets (defined in any type of Visual Studio project file) from within the Visual Studio IDE by right clicking on the Project item in Solution Explorer.

It supports custom MSBuild targets for all types of Visual Studio project files, including C# (.csproj), C/C++ (.vcxproj), NodeJs (.njsproj), etc. Custom MSBuild target examples for C# projects may include creating a Nuget package, publishing a site with custom actions, cleaning output folders or for NodeJs (.njsproj) projects calling arbitrary npm/yarn targets from within Visual Studio.

![img2](img2.png)

## Examples:

### Target for creating a Nuget package from a C# project file:
```xml
<Target Name="Package" DependsOnTargets="Build">
  <Message Importance="High" Text="Package" />
  <MakeDir Directories="bin\Nuget" />
  <Exec Command="nuget.exe pack -NoPackageAnalysis -NonInteractive $(MSBuildProjectName).csproj" />
</Target>
```

### Target for cleaning up the project output folders:
```xml
<Target Name="CleanOutputs">
  <Message Text="CleanOutputs" Importance="high" />
  <RemoveDir Directories="$(OutputPath);obj" ContinueOnError="true">
    <Output TaskParameter="RemovedDirectories" ItemName="removed" />
  </RemoveDir>
  <Message Text="Removed: %(removed.FullPath)" Importance="high" />
</Target>
```

## Building
For building the project you will need Visual Studio 2015 with Visual Studio Extensibility Tools.

