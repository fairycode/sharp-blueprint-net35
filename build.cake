#tool nuget:?package=OpenCover&version=4.6.519
#tool nuget:?package=ReportGenerator&version=2.5.8
#tool nuget:?package=xunit.runner.console&version=2.1.0

var target = Context.Argument("target", "Default");

var configuration =
    HasArgument("Configuration") ? Argument<string>("Configuration") :
    EnvironmentVariable("Configuration") != null ? EnvironmentVariable("Configuration") : "Release";

var buildSystem = Context.BuildSystem();

var isLocalBuild = buildSystem.IsLocalBuild;
var isRunningOnAppVeyor = buildSystem.AppVeyor.IsRunningOnAppVeyor;
var isRunningOnWindows = Context.IsRunningOnWindows();

var isPullRequest = buildSystem.AppVeyor.Environment.PullRequest.IsPullRequest;
var isBuildTagged = IsBuildTagged(buildSystem);

var buildNumber =
    HasArgument("BuildNumber") ? Argument<int>("BuildNumber") :
    isRunningOnAppVeyor ? AppVeyor.Environment.Build.Number :
    EnvironmentVariable("BuildNumber") != null ? int.Parse(EnvironmentVariable("BuildNumber")) : 0;

var artifactsDir = Directory("./artifacts");
var testResultsDir = Directory("./artifacts/test-results");
var nugetDir = System.IO.Path.Combine(artifactsDir, "nuget");

//
// Tasks
//

Task("Info")
    .Does(() =>
{
    Information("Target: {0}", target);
    Information("Configuration: {0}", configuration);
    Information("Build number: {0}", buildNumber);

    var projects = GetFiles("./src/**/*.csproj");
    foreach (var project in projects) {
        Information("{0} version: {1}", project.GetFilenameWithoutExtension(), GetVersion(project.FullPath));
    }
});

Task("Clean")
    .Does(() =>
{
    CleanDirectory(artifactsDir);
});

Task("Restore-Packages")
    .Does(() =>
{
    var nugetCmd = MakeAbsolute(new FilePath("tools/nuget.exe"));
    var sln = MakeAbsolute(new FilePath("./SharpBlueprint.NET35.sln"));

    foreach (var file in GetFiles("tools/*"))
        Information(file);

    var packageConfigs = GetFiles("./src/**/packages.config");
    packageConfigs.Add(GetFiles("./test/**/packages.config"));

    foreach (var packageConfig in packageConfigs)
    {
        Information("packageConfig: " + packageConfig);
        using (var process = StartAndReturnProcess(
            nugetCmd,
            new ProcessSettings { Arguments = "restore " + packageConfig + " -SolutionDirectory " + sln.GetDirectory() }))
        {
            process.WaitForExit();
            if (process.GetExitCode() != 0)
                throw new Exception("Restoring packages for " + packageConfig.GetFilename() + " has failed!");
        }
    }
});

Task("Build")
    .IsDependentOn("Info")
    .IsDependentOn("Clean")
    .IsDependentOn("Restore-Packages")
    .Does(() =>
{
    var projects = GetFiles("./src/**/*.csproj");
    projects.Add(GetFiles("./test/**/*.csproj"));

    //var msbuildCmd = "C:\\Program Files (x86)\\Microsoft Visual Studio\\2017\\Community\\MSBuild\\15.0\\Bin\\MSBuild.exe";
    var msbuildCmd = "msbuild";

    foreach(var project in projects)
    {
        using (var process = StartAndReturnProcess(
            msbuildCmd,
            new ProcessSettings {
                Arguments = "/p:Configuration=" + configuration + " " + project
            }
        ))
        {
            process.WaitForExit();
            if (process.GetExitCode() != 0)
                throw new Exception("Build has failed!");
        }
    }
});

Task("Run-Unit-Tests")
    .IsDependentOn("Build")
    .Does(() =>
{
    var testFile = MakeAbsolute(new FilePath("./test/SharpBlueprint.Client.Tests" + "/bin/" + configuration + "/SharpBlueprint.Client.Tests.dll"));
    var testResultsFile = MakeAbsolute(testResultsDir.Path.CombineWithFilePath("SharpBlueprint.Client").AppendExtension("xml"));;

    var workingDirectory = MakeAbsolute(new DirectoryPath("./test/SharpBlueprint.Client.Tests")).FullPath;

    var xunitConsole = GetFiles("tools/xunit.runner.console/tools/xunit.console.exe")
        .OrderByDescending(file => file.FullPath)
        .FirstOrDefault();

    Action<ICakeContext> testAction = tool => {
        using (var process = tool.StartAndReturnProcess(
            xunitConsole,
            new ProcessSettings {
                Arguments = testFile + " -xml " + testResultsFile + " -nologo -noshadow",
                WorkingDirectory = workingDirectory
            }
        ))
        {
            process.WaitForExit();
            if (process.GetExitCode() != 0)
                throw new Exception("Tests have failed!");
        }
    };

    EnsureDirectoryExists(testResultsDir);

    var openCoverXml = MakeAbsolute(testResultsDir.Path.CombineWithFilePath("OpenCover").AppendExtension("xml"));;
    var coverageReportDir = System.IO.Path.Combine(testResultsDir, "report");

    var settings = new OpenCoverSettings
    {
        Register = "user",
        ReturnTargetCodeOffset = 0,
        WorkingDirectory = workingDirectory,
        ArgumentCustomization =
            args =>
                args.Append(
                    "-skipautoprops -mergebyhash -mergeoutput -oldstyle -hideskipped:All")
    }
    .WithFilter("+[*]* -[xunit.*]* -[*.Tests]*")
    .ExcludeByAttribute("*.ExcludeFromCodeCoverage*")
    .ExcludeByFile("*/*Designer.cs;*/*.g.cs;*/*.g.i.cs");

    OpenCover(testAction, openCoverXml, settings);

    // for non-local build coverage is uploaded to codecov.io so no need to generate the report
    if (FileExists(openCoverXml) && isLocalBuild)
    {
        ReportGenerator(openCoverXml, coverageReportDir,
            new ReportGeneratorSettings {
                ArgumentCustomization = args => args.Append("-reporttypes:html")
            }
        );
    }
});

Task("Create-Packages")
    .IsDependentOn("Run-Unit-Tests")
    .Does(() =>
{
    var projectFile = MakeAbsolute(new FilePath("./src/SharpBlueprint.Client/SharpBlueprint.Client.csproj"));
    var nugetCmd = MakeAbsolute(new FilePath("tools/nuget.exe"));

    var args = new StringBuilder();

    args.Append("pack ").Append(projectFile);
    args.Append(" -Symbols -Properties Configuration=").Append(configuration);
    args.Append(" -OutputDirectory ").Append(nugetDir);
    //args.Append(" -Suffix build-").Append(buildNumber.ToString("D4"));

    // nuget.exe pack SharpBlueprint.Client.csproj -Symbols -Properties Configuration=configuration -OutputDirectory nugetDir -Suffix revision
    using (var process = StartAndReturnProcess(
        nugetCmd,
        new ProcessSettings { Arguments = args.ToString() }
    ))
    {
        process.WaitForExit();
        if (process.GetExitCode() != 0)
            throw new Exception("Creating package has failed!");
    }
});

Task("Publish-MyGet")
    .IsDependentOn("Create-Packages")
    .WithCriteria(() => !isLocalBuild && !isPullRequest && !isBuildTagged)
    .Does(() =>
{
    var url = EnvironmentVariable("MYGET_API_URL");
    if (string.IsNullOrEmpty(url))
        throw new InvalidOperationException("Could not resolve MyGet API url");

    var key = EnvironmentVariable("MYGET_API_KEY");
    if (string.IsNullOrEmpty(key))
        throw new InvalidOperationException("Could not resolve MyGet API key");

    UploadPackages(Context, url, key, nugetDir);
})
.OnError(exception =>
{
    Information("Error: " + exception.Message);
});

Task("Publish-NuGet")
    .IsDependentOn("Create-Packages")
    .WithCriteria(() => !isLocalBuild && !isPullRequest && isBuildTagged)
    .Does(() =>
{
    var url = EnvironmentVariable("NUGET_API_URL");
    if (string.IsNullOrEmpty(url))
        throw new InvalidOperationException("Could not resolve NuGet API url");

    var key = EnvironmentVariable("NUGET_API_KEY");
    if (string.IsNullOrEmpty(key))
        throw new InvalidOperationException("Could not resolve NuGet API key");

    UploadPackages(Context, url, key, nugetDir);
})
.OnError(exception =>
{
    Information("Error: " + exception.Message);
});

//
// Targets
//

Task("Default")
    .IsDependentOn("Create-Packages");

//
// Run build
//

RunTarget(target);


// **********************************************
// ***               Utilities                ***
// **********************************************

/// <summary>
/// Checks if build is tagged.
/// </summary>
private static bool IsBuildTagged(BuildSystem buildSystem)
{
    return buildSystem.AppVeyor.Environment.Repository.Tag.IsTag
           && !string.IsNullOrWhiteSpace(buildSystem.AppVeyor.Environment.Repository.Tag.Name);
}

/// <summary>
/// Get's version from AssemblyVersion attribute.
/// </summary>
private static string GetVersion(string csproj)
{
    var csprojInfo = new FileInfo(csproj);

    var assemblyInfo =
        new FileInfo(
            System.IO.Path.Combine(
                System.IO.Path.Combine(csprojInfo.DirectoryName, "Properties"), "AssemblyInfo.cs"));

    if (!assemblyInfo.Exists)
        throw new Exception("Not Found AssemblyInfo.cs file.");

    var versionRegex =
        new System.Text.RegularExpressions.Regex(@"\[assembly\: AssemblyVersion\(""(\d{1,})\.(\d{1,})\.(\d{1,})\.(\d{1,})""\)\]",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    using (var reader = new StreamReader(assemblyInfo.FullName))
    {
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
                continue;

            var match = versionRegex.Match(line);
            if (match.Success)
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}.{1}.{2}",
                    match.Groups[1].Value,
                    match.Groups[2].Value,
                    match.Groups[3].Value);
            }
        }
    }

    return null;
}

/// <summary>
/// Uploads packages to the repo.
/// </summary>
private static void UploadPackages(ICakeContext context, string url, string key, string nugetDir)
{
    var nugetCmd = context.MakeAbsolute(new FilePath("tools/nuget.exe"));

    foreach (var package in context.GetFiles(nugetDir + "/*.nupkg"))
    {
        // symbols packages are pushed alongside regular ones so no need to push them explicitly
        if (package.FullPath.EndsWith("symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            continue;

        var args = new StringBuilder();
        args.Append("push ").Append(context.MakeAbsolute(package));
        args.Append(" -Source ").Append(url);
        args.Append(" -ApiKey ").Append(key);
        args.Append(" -NonInteractive");

        var attempt = 0;
        var pushed = false;
        while (!pushed && attempt++ <= 3)
        {
            using (var process = context.StartAndReturnProcess(
                nugetCmd,
                new ProcessSettings { Arguments = args.ToString() }
            ))
            {
                process.WaitForExit();
                var exitCode = process.GetExitCode();
                if (exitCode != 0)
                {
                    if (attempt < 3)
                    {
                        context.Information("Failed to push " + package.GetFilename() + ". Error code: " + exitCode);
                        context.Information("Attempt: " + (attempt + 1));
                    }
                    else
                        context.Information("Pushing " + package.GetFilename() + " has failed!");
                    continue;
                }
                pushed = true;
            }
        }
    }
}
