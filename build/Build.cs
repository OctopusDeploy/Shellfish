// ReSharper disable RedundantUsingDirective

using System;
using Nuke.Common;
using Nuke.Common.CI.TeamCity;
using Nuke.Common.CI.TeamCity.Configuration;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Octopus;
using Nuke.Common.Tools.OctoVersion;
using Nuke.Common.Utilities.Collections;
using Serilog;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
[TeamCity(
    Description = "",
    AutoGenerate = true,
    ExcludedTargets = new []
    {
        nameof(Clean),
        nameof(Restore),
        nameof(CopyToLocalPackages),
        nameof(Publish),
        nameof(Pack),
        nameof(Compile),
        nameof(Test),
    },
    NightlyBuildAlways = true,
    NightlyTriggeredTargets = new [] { nameof(ChainNightlyBuild)},
    VcsTriggeredTargets = new [] { nameof(ChainBuildAndTestAndPublish)}
    
)]
class Build : NukeBuild
{
    const string CiBranchNameEnvVariable = "OCTOVERSION_CurrentBranch";

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")] readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;

    [Parameter("Whether to auto-detect the branch name - this is okay for a local build, but should not be used under CI.")] readonly bool AutoDetectBranch = IsLocalBuild;

    [OctoVersion(Framework = "net6.0",
        BranchParameter = nameof(BranchName),
        AutoDetectBranchParameter = nameof(AutoDetectBranch))]
    public OctoVersionInfo OctoVersionInfo;

    [Parameter("Branch name for OctoVersion to use to calculate the version number. Can be set via the environment variable " + CiBranchNameEnvVariable + ".", Name = CiBranchNameEnvVariable)]
    string BranchName { get; set; }
    
    [Parameter("The Octopus Server to publish to")]
    public string OctopusServerUrl { get; set; }
    [Parameter("The ApiKey to use to authenticate to Octopus Server")]
    [Secret]
    public string OctopusServerApiKey { get; set; }
    
    AbsolutePath SourceDirectory => RootDirectory / "source";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath LocalPackagesDirectory => RootDirectory / ".." / "LocalPackages";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj", "**/TestResults").ForEach(DeleteDirectory);
            EnsureCleanDirectory(ArtifactsDirectory);
        });

    Target CalculateVersion => _ => _
        .Executes(() =>
        {
            //all the magic happens inside `[NukeOctoVersion]` above. we just need a target for TeamCity to call
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetRestore(_ => _
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(CalculateVersion)
        .DependsOn(Clean)
        .DependsOn(Restore)
        .Executes(() =>
        {
            Log.Information("Building Octopus.Shellfish v{0}", OctoVersionInfo.FullSemVer);

            DotNetBuild(_ => _
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetVersion(OctoVersionInfo.FullSemVer)
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(_ => _
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetNoBuild(true)
                .EnableNoRestore());
        });

    Target Pack => _ => _
        .DependsOn(BuildAndTestWindows)
        .Consumes(BuildAndTestWindows, "*") //this can go :boom: if the target is not specified by `.DependsOn()`
        .Produces("artifacts/*") //this feels like it should be on Pack, not here
        .Executes(() =>
        {
            DotNetPack(_ => _
                .SetProject(Solution)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(ArtifactsDirectory)
                .EnableNoBuild()
                .AddProperty("Version", OctoVersionInfo.FullSemVer)
            );
        });

    Target CopyToLocalPackages => _ => _
        .OnlyWhenStatic(() => IsLocalBuild)
        .TriggeredBy(Pack)
        .Executes(() =>
        {
            EnsureExistingDirectory(LocalPackagesDirectory);
            CopyFileToDirectory(ArtifactsDirectory / $"Octopus.Shellfish.{OctoVersionInfo.FullSemVer}.nupkg", LocalPackagesDirectory, FileExistsPolicy.Overwrite);
        });

    /************************/
    /** Testing nuke generation **/
    /************************/
    

    Target TestLinux => _ => _
        .DependsOn(Compile)
        .DependsOn(Test);

    Target BuildAndTestWindows => _ => _
        .DependsOn(Compile)
        .DependsOn(Test)
        ;

    Target Publish => _ => _
        .OnlyWhenStatic(() => !IsLocalBuild)
        .DependsOn(Pack)
        .Consumes(Pack, "*")
        .Executes(() =>
        {
            OctopusTasks.OctopusBuildInformation(s => s
                .SetServer(OctopusServerUrl)
                .SetApiKey(OctopusServerApiKey)
                .SetSpace("Core Platform")
                .SetPackageId("Octopus.Shellfish")
                .SetVersion(OctoVersionInfo.FullSemVer)
            );
            OctopusTasks.OctopusPush(s => s
                .SetServer(OctopusServerUrl)
                .SetApiKey(OctopusServerApiKey)
                .SetSpace("Core Platform")
                .SetPackage($"**/Octopus.Shellfish.{OctoVersionInfo.FullSemVer}.nupkg")
            );
            OctopusTasks.OctopusCreateRelease(s => s
                .SetServer(OctopusServerUrl)
                .SetApiKey(OctopusServerApiKey)
                .SetSpace("Core Platform")
                .SetProject("Octopus.Shellfish")
                .SetVersion(OctoVersionInfo.FullSemVer)
            );
        });

    Target ChainBuildAndTestAndPublish => _ => _
        .DependsOn(TestLinux)
        .DependsOn(BuildAndTestWindows)
        .DependsOn(Publish);
    
    Target ChainNightlyBuild => _ => _
        .DependsOn(CalculateVersion) //we need to generate a "always re-run" here
        .DependsOn(TestLinux)
        .DependsOn(BuildAndTestWindows)
        .DependsOn(Publish);
    
    /// Support plugins are available for:
    /// - JetBrains ReSharper        https://nuke.build/resharper
    /// - JetBrains Rider            https://nuke.build/rider
    /// - Microsoft VisualStudio     https://nuke.build/visualstudio
    /// - Microsoft VSCode           https://nuke.build/vscode
    public static int Main() => Execute<Build>(x => x.Pack);
}
