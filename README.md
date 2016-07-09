# MSBuildTargetsVsExtension
Visual Studio MSBuild Targets Integration

Allows the execution of custom MSbuild targets from the Visual Studio IDE in a manner similar to the Eclipse and Ant integration.

You can use this for exposing your custom project targets and executing them from the Visual Studio IDE such as creating a Nuget or Chocolatey package, publishing a site, cleaning output folders, etc. 

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

