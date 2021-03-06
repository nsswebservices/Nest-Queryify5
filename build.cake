#tool "GitVersion.CommandLine"

var target              = Argument("target", "Default");
var artifacts           = Directory("./artifacts");
var buildOutput         = Directory("./artifacts/build");
var configuration       = Argument("configuration", "Release");
var versionAssemblyInfo = Argument("versionAssemblyInfo", "src/VersionAssemblyInfo.cs");
var targetFrameworks    = Argument("target-frameworks", "netstandard1.3,net45");
GitVersion versionInfo  = null;

var targets             = targetFrameworks.Split(',');

Setup(ctx => {
    
});

Task("Clean").Does(() => {
    CleanDirectories(artifacts);
});

Task("Create-Version-Info")
    .WithCriteria(() => !FileExists(versionAssemblyInfo))
    .Does(() =>
{
    Information("Creating version assembly info");
    CreateAssemblyInfo(versionAssemblyInfo, new AssemblyInfoSettings {
        Version = "0.0.0.0",
        FileVersion = "0.0.0.0",
        InformationalVersion = "",
    });
});

Task("Update-Version-Info")
    .IsDependentOn("Create-Version-Info")
    .Does(() => 
{
    versionInfo = GitVersion(new GitVersionSettings {
        UpdateAssemblyInfo = true,
        UpdateAssemblyInfoFilePath = versionAssemblyInfo,
        NoFetch = true
    });

    if(versionInfo != null) {
        Information("Version: {0}", versionInfo.FullSemVer);
    } else {
        throw new Exception("Unable to determine version");
    }
});

Task("Upload-AppVeyor-Artifacts")
    .IsDependentOn("Pack")
    .WithCriteria(() => AppVeyor.IsRunningOnAppVeyor)
    .Does(() =>
{
    var nupkgFiles = System.IO.Directory.EnumerateFiles(artifacts.ToString(), "*.nupkg").ToList();
    
    foreach(var nupkg in nupkgFiles) {
        if(FileExists(nupkg)) {
            AppVeyor.UploadArtifact(nupkg);
        }
    }
});

Task("Update-AppVeyor-Build-Number")
    .IsDependentOn("Update-Version-Info")
    .WithCriteria(() => AppVeyor.IsRunningOnAppVeyor)
    .Does(() =>
{
    AppVeyor.UpdateBuildVersion(versionInfo.FullSemVer +".build." +AppVeyor.Environment.Build.Number);
});

Task("Nuget-Restore").Does(() => DotNetCoreRestore("src/Nest.Queryify5/"));

Task("Build")
    .IsDependentOn("Clean")
    .IsDependentOn("Update-Version-Info")
    .IsDependentOn("Nuget-Restore")
    .Does(() => {

    foreach(var target in targets) {
        var output = Directory(buildOutput.ToString() +"/" +target);
        Information("Building for TargetFramework [{0}] to {1}", target, output);
        EnsureDirectoryExists(output);

        var buildSettings = new DotNetCoreBuildSettings() {
            Configuration = configuration,
            NoIncremental = true,
            OutputDirectory = output,
            Framework = target
        };

        DotNetCoreBuild("src/Nest.Queryify5/", buildSettings);
    }
});

Task("Pack")
    .Does(() => {

    var nugetFiles = new List<NuSpecContent>();
    var nugetDependencies = new List<NuSpecDependency>();

    foreach(var target in targets) {
        var output = Directory(buildOutput.ToString() +"/" +target);
        Information("Collecting files for TargetFramework [{0}] to {1}", target, output);
        EnsureDirectoryExists(output);

        nugetFiles.AddRange(new [] {
            new NuSpecContent { Source = File(output.ToString() +"/Nest.Queryify5.dll"), Target = "lib/" +target },
            new NuSpecContent { Source = File(output.ToString() +"/Nest.Queryify5.pdb"), Target = "lib/" +target }
        });

        nugetDependencies.AddRange(new [] {
            new NuSpecDependency { Id = "Nest", Version = "[5,6]", TargetFramework=target },
        });
    }

    Information("Packing {0} files for {1} target frameworks [Version: {2}]", nugetFiles.Count(), targets.Count(), versionInfo.NuGetVersionV2);

    var settings = new NuGetPackSettings {
        Id = "Nest.Queryify5",
        Authors = new [] { "Phil Oyston" },
        Owners = new [] {"Phil Oyston", "Storm ID" },
        Description = "Provides a mechanism to interact with Elasticsearch via a query object pattern",
        LicenseUrl = new Uri("https://raw.githubusercontent.com/stormid/nest-queryify/master/LICENSE"),
        ProjectUrl = new Uri("https://github.com/stormid/nest-queryify"),
        IconUrl = new Uri("http://stormid.com/_/images/icons/apple-touch-icon.png"),
        RequireLicenseAcceptance = false,
        Properties = new Dictionary<string, string> { { "Configuration", configuration }},
        Symbols = false,
        NoPackageAnalysis = true,
        Version = versionInfo.NuGetVersionV2,
        OutputDirectory = artifacts,
        Tags = new[] { "Elasticsearch", "Nest", "Storm" },
        Files = nugetFiles,
        Dependencies = nugetDependencies
    };
    NuGetPack("Nest.Queryify.nuspec", settings);
});

Task("Default")
    .IsDependentOn("Build");

Task("CI")
    .IsDependentOn("Update-AppVeyor-Build-Number")
    .IsDependentOn("Default")
    .IsDependentOn("Pack")
    .IsDependentOn("Upload-AppVeyor-Artifacts");

RunTarget(target);