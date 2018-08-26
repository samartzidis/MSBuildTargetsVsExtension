# MSBuild Targets Visual Studio Extension

Allows the execution of custom MSBuild project targets (defined in any type of Visual Studio project file) from within the Visual Studio IDE by right clicking on the Project item in Solution Explorer.

Custom MSBuild target examples may include creating a Nuget or Chocolatey package from your project binaries, publishing a site with custom actions, cleaning the project output folders, etc.

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

